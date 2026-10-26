// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;
using SQLitePCL;
using System.Data;
using System.Diagnostics;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Builds a private in-memory SQLite snapshot and executes one read-only SELECT against it.
	/// All work runs off the caller's thread; cancellation interrupts SQLite through its progress handler.
	/// </summary>
	public sealed class SqliteDirectoryQueryService
	{
		private static readonly object InitializationLock = new();
		private static bool isInitialized;

		private static readonly strdelegate_authorizer Authorizer = DirectoryQuerySecurity.Authorize;

		public Task<DirectoryQueryResult> ExecuteAsync(
			DirectoryQuerySnapshot snapshot,
			string sql,
			DirectoryQueryExecutionOptions? options = null,
			CancellationToken cancellationToken = default)
		{
			options ??= DirectoryQueryExecutionOptions.Default;

			return Task.Run(
				() => ExecuteCore(snapshot, sql, options, cancellationToken),
				CancellationToken.None);
		}

		private DirectoryQueryResult ExecuteCore(
			DirectoryQuerySnapshot snapshot,
			string sql,
			DirectoryQueryExecutionOptions options,
			CancellationToken cancellationToken)
		{
			var stopwatch = Stopwatch.StartNew();

			if (string.IsNullOrWhiteSpace(sql))
				return Failure(snapshot, stopwatch, DirectoryQueryError.InvalidSql, null);

			if (!DirectoryQuerySecurity.IsSelectLike(sql))
				return Failure(snapshot, stopwatch, DirectoryQueryError.NotSelect, null);

			EnsureInitialized();

			var timedOut = false;

			try
			{
				using var connection = new SqliteConnection("Data Source=:memory:;Cache=Private");
				connection.Open();

				CreateSchema(connection);
				InsertSnapshot(connection, snapshot, cancellationToken);

				using (var queryOnly = connection.CreateCommand())
				{
					queryOnly.CommandText = "PRAGMA query_only = 1;";
					queryOnly.ExecuteNonQuery();
				}

				var handle = connection.Handle
					?? throw new InvalidOperationException("The SQLite connection has no handle.");

				raw.sqlite3_set_authorizer(handle, Authorizer, null);

				var statementError = ValidateStatement(handle, sql);
				if (statementError != DirectoryQueryError.None)
					return Failure(snapshot, stopwatch, statementError, null);

				delegate_progress progress = _ =>
				{
					if (cancellationToken.IsCancellationRequested)
						return 1;

					if (stopwatch.Elapsed > options.Timeout)
					{
						timedOut = true;
						return 1;
					}

					return 0;
				};

				raw.sqlite3_progress_handler(
					handle,
					Math.Max(1, options.ProgressInterval),
					progress,
					null);

				using var cancellationRegistration = cancellationToken.Register(() =>
				{
					try
					{
						raw.sqlite3_interrupt(handle);
					}
					catch (ObjectDisposedException)
					{
						// The query already finished.
					}
				});

				var result = ReadResult(connection, snapshot, sql, options, stopwatch, cancellationToken);
				GC.KeepAlive(progress);

				return result;
			}
			catch (SqliteException ex)
			{
				var error = ex.SqliteErrorCode switch
				{
					raw.SQLITE_AUTH => DirectoryQueryError.ForbiddenOperation,
					raw.SQLITE_INTERRUPT => cancellationToken.IsCancellationRequested
						? DirectoryQueryError.Cancelled
						: timedOut
							? DirectoryQueryError.Timeout
							: DirectoryQueryError.SqliteError,
					_ => DirectoryQueryError.SqliteError,
				};

				return Failure(snapshot, stopwatch, error, error is DirectoryQueryError.SqliteError ? ex.Message : null);
			}
			catch (OperationCanceledException)
			{
				return Failure(snapshot, stopwatch, DirectoryQueryError.Cancelled, null);
			}
			catch (Exception ex)
			{
				return Failure(snapshot, stopwatch, DirectoryQueryError.SqliteError, ex.Message);
			}
		}

		private static DirectoryQueryResult ReadResult(
			SqliteConnection connection,
			DirectoryQuerySnapshot snapshot,
			string sql,
			DirectoryQueryExecutionOptions options,
			Stopwatch stopwatch,
			CancellationToken cancellationToken)
		{
			using var command = connection.CreateCommand();
			command.CommandText = sql;
			command.CommandTimeout = Math.Max(1, (int)Math.Ceiling(options.Timeout.TotalSeconds));

			using var reader = command.ExecuteReader();

			var columns = new List<DirectoryQueryColumn>(reader.FieldCount);
			for (var index = 0; index < reader.FieldCount; index++)
				columns.Add(new DirectoryQueryColumn(reader.GetName(index), GetDeclaredType(reader, index)));

			var rows = new List<DirectoryQueryRow>();
			var isTruncated = false;

			while (reader.Read())
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (rows.Count >= options.MaxRows)
				{
					isTruncated = true;
					break;
				}

				var values = new object?[reader.FieldCount];
				for (var index = 0; index < reader.FieldCount; index++)
					values[index] = reader.IsDBNull(index) ? null : reader.GetValue(index);

				rows.Add(new DirectoryQueryRow(values));
			}

			stopwatch.Stop();

			return new DirectoryQueryResult(
				columns,
				rows,
				isTruncated,
				stopwatch.Elapsed,
				snapshot.DirectoryKey,
				snapshot.SourceRevision,
				DirectoryQueryError.None,
				null);
		}

		private static string? GetDeclaredType(SqliteDataReader reader, int index)
		{
			try
			{
				var type = reader.GetDataTypeName(index);
				return string.IsNullOrEmpty(type) ? null : type;
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static DirectoryQueryError ValidateStatement(sqlite3 handle, string sql)
		{
			sqlite3_stmt? statement = null;

			try
			{
				var result = raw.sqlite3_prepare_v2(handle, sql, out statement, out var tail);

				if (result != raw.SQLITE_OK)
					return result == raw.SQLITE_AUTH
						? DirectoryQueryError.ForbiddenOperation
						: DirectoryQueryError.InvalidSql;

				if (raw.sqlite3_stmt_readonly(statement) == 0)
					return DirectoryQueryError.NotSelect;

				if (DirectoryQuerySecurity.HasExecutableContent(tail))
					return DirectoryQueryError.MultipleStatements;

				return DirectoryQueryError.None;
			}
			finally
			{
				statement?.Dispose();
			}
		}

		private static void CreateSchema(SqliteConnection connection)
		{
			foreach (var statement in DirectoryQuerySchema.CreateStatements)
			{
				using var command = connection.CreateCommand();
				command.CommandText = statement;
				command.ExecuteNonQuery();
			}
		}

		private static void InsertSnapshot(
			SqliteConnection connection,
			DirectoryQuerySnapshot snapshot,
			CancellationToken cancellationToken)
		{
			using var transaction = connection.BeginTransaction();

			using (var fileCommand = connection.CreateCommand())
			{
				fileCommand.Transaction = transaction;
				fileCommand.CommandText = """
					INSERT INTO files (
						file_id, name, display_name, extension, full_path, item_type,
						size_bytes, modified_utc, created_utc, is_folder)
					VALUES (
						$file_id, $name, $display_name, $extension, $full_path, $item_type,
						$size_bytes, $modified_utc, $created_utc, $is_folder);
					""";

				var fileId = fileCommand.Parameters.Add("$file_id", SqliteType.Integer);
				var name = fileCommand.Parameters.Add("$name", SqliteType.Text);
				var displayName = fileCommand.Parameters.Add("$display_name", SqliteType.Text);
				var extension = fileCommand.Parameters.Add("$extension", SqliteType.Text);
				var fullPath = fileCommand.Parameters.Add("$full_path", SqliteType.Text);
				var itemType = fileCommand.Parameters.Add("$item_type", SqliteType.Text);
				var sizeBytes = fileCommand.Parameters.Add("$size_bytes", SqliteType.Integer);
				var modifiedUtc = fileCommand.Parameters.Add("$modified_utc", SqliteType.Integer);
				var createdUtc = fileCommand.Parameters.Add("$created_utc", SqliteType.Integer);
				var isFolder = fileCommand.Parameters.Add("$is_folder", SqliteType.Integer);

				foreach (var file in snapshot.Files)
				{
					cancellationToken.ThrowIfCancellationRequested();

					fileId.Value = file.FileId;
					name.Value = (object?)file.Name ?? DBNull.Value;
					displayName.Value = (object?)file.DisplayName ?? DBNull.Value;
					extension.Value = (object?)file.Extension ?? DBNull.Value;
					fullPath.Value = (object?)file.FullPath ?? DBNull.Value;
					itemType.Value = (object?)file.ItemType ?? DBNull.Value;
					sizeBytes.Value = (object?)file.SizeBytes ?? DBNull.Value;
					modifiedUtc.Value = (object?)file.ModifiedUtc?.ToUnixTimeSeconds() ?? DBNull.Value;
					createdUtc.Value = (object?)file.CreatedUtc?.ToUnixTimeSeconds() ?? DBNull.Value;
					isFolder.Value = file.IsFolder ? 1 : 0;

					fileCommand.ExecuteNonQuery();
				}
			}

			using (var tagCommand = connection.CreateCommand())
			{
				tagCommand.Transaction = transaction;
				tagCommand.CommandText = "INSERT OR IGNORE INTO tags (tag_id, name, color) VALUES ($tag_id, $name, $color);";

				var tagId = tagCommand.Parameters.Add("$tag_id", SqliteType.Text);
				var tagName = tagCommand.Parameters.Add("$name", SqliteType.Text);
				var color = tagCommand.Parameters.Add("$color", SqliteType.Text);

				foreach (var tag in snapshot.Tags)
				{
					tagId.Value = tag.TagId;
					tagName.Value = tag.Name;
					color.Value = (object?)tag.Color ?? DBNull.Value;
					tagCommand.ExecuteNonQuery();
				}
			}

			using (var relationCommand = connection.CreateCommand())
			{
				relationCommand.Transaction = transaction;
				relationCommand.CommandText = "INSERT OR IGNORE INTO file_tags (file_id, tag_id) VALUES ($file_id, $tag_id);";

				var fileId = relationCommand.Parameters.Add("$file_id", SqliteType.Integer);
				var tagId = relationCommand.Parameters.Add("$tag_id", SqliteType.Text);

				foreach (var relation in snapshot.FileTags)
				{
					fileId.Value = relation.FileId;
					tagId.Value = relation.TagId;
					relationCommand.ExecuteNonQuery();
				}
			}

			transaction.Commit();
		}

		private static DirectoryQueryResult Failure(
			DirectoryQuerySnapshot snapshot,
			Stopwatch stopwatch,
			DirectoryQueryError error,
			string? message)
		{
			stopwatch.Stop();

			return new DirectoryQueryResult(
				[],
				[],
				false,
				stopwatch.Elapsed,
				snapshot.DirectoryKey,
				snapshot.SourceRevision,
				error,
				message);
		}

		private static void EnsureInitialized()
		{
			if (isInitialized)
				return;

			lock (InitializationLock)
			{
				if (isInitialized)
					return;

				SQLitePCL.Batteries_V2.Init();
				isInitialized = true;
			}
		}
	}
}
