// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.App.UnitTests.Querying
{
	[TestClass]
	public sealed class DirectoryQueryServiceTests
	{
		private static readonly DateTimeOffset Modified = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
		private static readonly DateTimeOffset Created = new(2026, 1, 1, 7, 0, 0, TimeSpan.Zero);

		[TestMethod]
		public async Task BasicSelect_ReturnsSchemaValuesAndNullSize()
		{
			var result = await ExecuteAsync("""
				SELECT file_id, name, display_name, extension, size_bytes, modified_utc, is_folder
				FROM files
				ORDER BY file_id;
				""");

			Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
			CollectionAssert.AreEqual(
				new[] { "file_id", "name", "display_name", "extension", "size_bytes", "modified_utc", "is_folder" },
				result.Columns.Select(column => column.Name).ToArray());

			Assert.AreEqual(3, result.Rows.Count);
			Assert.AreEqual(0L, result.Rows[0].Values[0]);
			Assert.AreEqual("report.txt", result.Rows[0].Values[1]);
			Assert.AreEqual("report", result.Rows[0].Values[2]);
			Assert.AreEqual(".txt", result.Rows[0].Values[3]);
			Assert.AreEqual(1024L, result.Rows[0].Values[4]);
			Assert.AreEqual(Modified.ToUnixTimeSeconds(), result.Rows[0].Values[5]);
			Assert.AreEqual(0L, result.Rows[0].Values[6]);

			Assert.IsNull(result.Rows[1].Values[3]);
			Assert.IsNull(result.Rows[1].Values[4]);
			Assert.IsNull(result.Rows[1].Values[5]);
			Assert.AreEqual(1L, result.Rows[1].Values[6]);
		}

		[TestMethod]
		public async Task TagTables_ExposeDefinitionsAndRelations()
		{
			var result = await ExecuteAsync("""
				SELECT f.name, t.tag_id, t.name AS tag_name, t.color
				FROM files AS f
				JOIN file_tags AS ft ON ft.file_id = f.file_id
				JOIN tags AS t ON t.tag_id = ft.tag_id
				ORDER BY f.file_id;
				""");

			Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
			Assert.AreEqual(1, result.Rows.Count);
			CollectionAssert.AreEqual(new object?[] { "report.txt", "work", "Work", "#FF0000" }, result.Rows[0].Values.ToArray());
		}

		[TestMethod]
		public async Task SubqueryAggregateWindowJoinAndUnion_AreSupported()
		{
			var subquery = await ExecuteAsync("SELECT name FROM files WHERE file_id IN (SELECT file_id FROM files WHERE is_folder = 0) ORDER BY name;");
			Assert.IsTrue(subquery.IsSuccess, subquery.ErrorMessage);
			CollectionAssert.AreEqual(new object?[] { "notes.md", "report.txt" }, subquery.Rows.Select(row => row.Values[0]).ToArray());

			var aggregate = await ExecuteAsync("SELECT COUNT(*) AS file_count, SUM(size_bytes) AS total FROM files;");
			Assert.IsTrue(aggregate.IsSuccess, aggregate.ErrorMessage);
			Assert.AreEqual(3L, aggregate.Rows[0].Values[0]);
			Assert.AreEqual(3072L, aggregate.Rows[0].Values[1]);

			var window = await ExecuteAsync("SELECT name, ROW_NUMBER() OVER (ORDER BY name) AS position FROM files ORDER BY name;");
			Assert.IsTrue(window.IsSuccess, window.ErrorMessage);
			Assert.AreEqual(3, window.Rows.Count);
			Assert.AreEqual(1L, window.Rows[0].Values[1]);

			var union = await ExecuteAsync("SELECT name FROM files WHERE is_folder = 1 UNION ALL SELECT name FROM files WHERE extension = '.md' ORDER BY name;");
			Assert.IsTrue(union.IsSuccess, union.ErrorMessage);
			CollectionAssert.AreEqual(new object?[] { "folder", "notes.md" }, union.Rows.Select(row => row.Values[0]).ToArray());

			var recursive = await ExecuteAsync("""
				WITH RECURSIVE counter(x) AS (
					SELECT 1
					UNION ALL
					SELECT x + 1 FROM counter WHERE x < 5
				)
				SELECT x FROM counter ORDER BY x;
				""");
			Assert.IsTrue(recursive.IsSuccess, recursive.ErrorMessage);
			CollectionAssert.AreEqual(new object?[] { 1L, 2L, 3L, 4L, 5L }, recursive.Rows.Select(row => row.Values[0]).ToArray());
		}

		[TestMethod]
		public async Task WritesAndDdl_AreRejected()
		{
			foreach (var sql in new[]
			{
				"INSERT INTO files (file_id) VALUES (99);",
				"UPDATE files SET name = 'x';",
				"DELETE FROM files;",
				"CREATE TABLE evil (x INTEGER);",
				"DROP TABLE files;",
				"ALTER TABLE files ADD COLUMN evil TEXT;",
			})
			{
				var result = await ExecuteAsync(sql);
				Assert.IsFalse(result.IsSuccess, sql);
				Assert.AreEqual(DirectoryQueryError.NotSelect, result.Error, sql);
			}
		}

		[TestMethod]
		public async Task MultipleStatementsAndPragma_AreRejected()
		{
			var multiple = await ExecuteAsync("SELECT 1; ATTACH DATABASE ':memory:' AS other;");
			Assert.AreEqual(DirectoryQueryError.MultipleStatements, multiple.Error);

			var commentOnlyTail = await ExecuteAsync("SELECT 1; -- trailing comment");
			Assert.IsTrue(commentOnlyTail.IsSuccess, commentOnlyTail.ErrorMessage);

			var pragma = await ExecuteAsync("PRAGMA table_info(files);");
			Assert.AreEqual(DirectoryQueryError.NotSelect, pragma.Error);

			var attach = await ExecuteAsync("WITH x AS (SELECT 1) SELECT * FROM x; ATTACH DATABASE ':memory:' AS other;");
			Assert.AreEqual(DirectoryQueryError.MultipleStatements, attach.Error);
		}

		[TestMethod]
		public async Task DangerousFunctions_AreRejected()
		{
			var result = await ExecuteAsync("SELECT load_extension('evil');");
			Assert.IsFalse(result.IsSuccess);
			Assert.IsTrue(result.Error is DirectoryQueryError.ForbiddenOperation or DirectoryQueryError.SqliteError);
		}

		[TestMethod]
		public async Task CommentHiddenSecondStatement_IsRejected()
		{
			foreach (var sql in new[]
			{
				"SELECT 1 /* x */; ATTACH DATABASE ':memory:' AS other;",
				"SELECT 1; /* x */ ATTACH DATABASE ':memory:' AS other;",
				"SELECT 1; -- x\nATTACH DATABASE ':memory:' AS other;",
			})
			{
				var result = await ExecuteAsync(sql);
				Assert.AreEqual(DirectoryQueryError.MultipleStatements, result.Error, sql);
			}
		}

		[TestMethod]
		public async Task SchemaIntrospection_IsRejectedByTheAuthorizer()
		{
			var master = await ExecuteAsync("SELECT name FROM sqlite_master;");
			Assert.AreEqual(DirectoryQueryError.ForbiddenOperation, master.Error);

			var pragma = await ExecuteAsync("SELECT * FROM pragma_table_info('files');");
			Assert.AreEqual(DirectoryQueryError.ForbiddenOperation, pragma.Error);

			var cte = await ExecuteAsync("WITH c(x) AS (SELECT 1) SELECT * FROM c;");
			Assert.IsTrue(cte.IsSuccess, cte.ErrorMessage);
		}

		[TestMethod]
		public void AuthorizerPolicy_DeniesWritesPragmasAndSchemaTables()
		{
			var state = new object();

			Assert.AreEqual(SQLitePCL.raw.SQLITE_OK, DirectoryQuerySecurity.Authorize(state, SQLitePCL.raw.SQLITE_SELECT, string.Empty, string.Empty, "main", string.Empty));
			Assert.AreEqual(SQLitePCL.raw.SQLITE_OK, DirectoryQuerySecurity.Authorize(state, SQLitePCL.raw.SQLITE_READ, "files", "name", "main", string.Empty));
			Assert.AreEqual(SQLitePCL.raw.SQLITE_DENY, DirectoryQuerySecurity.Authorize(state, SQLitePCL.raw.SQLITE_INSERT, "files", string.Empty, "main", string.Empty));
			Assert.AreEqual(SQLitePCL.raw.SQLITE_DENY, DirectoryQuerySecurity.Authorize(state, SQLitePCL.raw.SQLITE_PRAGMA, "table_info", string.Empty, "main", string.Empty));
			Assert.AreEqual(SQLitePCL.raw.SQLITE_DENY, DirectoryQuerySecurity.Authorize(state, SQLitePCL.raw.SQLITE_READ, "sqlite_master", "name", "main", string.Empty));
			Assert.AreEqual(SQLitePCL.raw.SQLITE_DENY, DirectoryQuerySecurity.Authorize(state, SQLitePCL.raw.SQLITE_FUNCTION, "load_extension", string.Empty, string.Empty, string.Empty));
		}

		[TestMethod]
		public async Task MaxRows_MarksTruncatedInsteadOfSilentlyCutting()
		{
			var result = await ExecuteAsync(
				"SELECT name FROM files ORDER BY name;",
				new DirectoryQueryExecutionOptions(TimeSpan.FromSeconds(5), MaxRows: 2, ProgressInterval: 100));

			Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
			Assert.AreEqual(2, result.Rows.Count);
			Assert.IsTrue(result.IsTruncated);
		}

		[TestMethod]
		public async Task Timeout_StopsRunawayRecursiveQuery()
		{
			var result = await ExecuteAsync(
				"WITH RECURSIVE counter(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM counter) SELECT count(*) FROM counter;",
				new DirectoryQueryExecutionOptions(TimeSpan.FromMilliseconds(300), MaxRows: 100, ProgressInterval: 100));

			Assert.AreEqual(DirectoryQueryError.Timeout, result.Error);
			Assert.IsTrue(result.Elapsed < TimeSpan.FromSeconds(5));
		}

		[TestMethod]
		public async Task Cancellation_StopsRunawayRecursiveQuery()
		{
			using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

			var result = await ExecuteAsync(
				"WITH RECURSIVE counter(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM counter) SELECT count(*) FROM counter;",
				new DirectoryQueryExecutionOptions(TimeSpan.FromSeconds(30), MaxRows: 100, ProgressInterval: 100),
				cancellation.Token);

			Assert.AreEqual(DirectoryQueryError.Cancelled, result.Error);
		}

		[TestMethod]
		public async Task Classifier_MapsFileIdsAndKeepsRawRows()
		{
			var snapshot = CreateSnapshot();
			var result = await ExecuteAsync("SELECT file_id, name FROM files ORDER BY file_id;");
			var classified = DirectoryQueryResultClassifier.Classify(result, snapshot);

			Assert.IsNotNull(classified.FileResult);
			CollectionAssert.AreEqual(new long[] { 0, 1, 2 }, classified.FileResult!.FileIds.ToArray());
			Assert.AreEqual(3, classified.Rows.Count);
		}

		[TestMethod]
		public async Task Classifier_DeduplicatesFileIdsButKeepsRows()
		{
			var snapshot = CreateSnapshot();
			var result = await ExecuteAsync("SELECT file_id FROM files UNION ALL SELECT file_id FROM files ORDER BY file_id;");
			var classified = DirectoryQueryResultClassifier.Classify(result, snapshot);

			Assert.AreEqual(6, classified.Rows.Count);
			CollectionAssert.AreEqual(new long[] { 0, 1, 2 }, classified.FileResult!.FileIds.ToArray());
		}

		[TestMethod]
		public async Task Classifier_RequiresFileIdOnEveryRow()
		{
			var snapshot = CreateSnapshot();
			var aggregate = await ExecuteAsync("SELECT COUNT(*) AS file_count FROM files;");
			Assert.IsNull(DirectoryQueryResultClassifier.Classify(aggregate, snapshot).FileResult);

			var invalid = await ExecuteAsync("SELECT file_id + 100 AS file_id FROM files;");
			Assert.IsNull(DirectoryQueryResultClassifier.Classify(invalid, snapshot).FileResult);

			var mixed = await ExecuteAsync("SELECT file_id FROM files UNION ALL SELECT NULL AS file_id;");
			Assert.IsNull(DirectoryQueryResultClassifier.Classify(mixed, snapshot).FileResult);
		}

		[TestMethod]
		public async Task LargeSnapshot_BuildsAndQueriesWithoutPathologicalDelay()
		{
			var files = Enumerable.Range(0, 100_000)
				.Select(index => new DirectoryQueryFileSnapshot(
					index,
					$"file{index}.txt",
					$"file{index}",
					".txt",
					@$"C:\Data\file{index}.txt",
					"Text Document",
					index,
					Modified,
					Created,
					false))
				.ToList();

			var snapshot = new DirectoryQuerySnapshot(@"C:\Data", 1, files, [], []);
			var stopwatch = System.Diagnostics.Stopwatch.StartNew();

			var result = await new SqliteDirectoryQueryService().ExecuteAsync(
				snapshot,
				"SELECT COUNT(*) AS count, SUM(size_bytes) AS total FROM files;",
				new DirectoryQueryExecutionOptions(TimeSpan.FromSeconds(30), MaxRows: 10, ProgressInterval: 1_000));

			stopwatch.Stop();

			Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
			Assert.AreEqual(100_000L, result.Rows[0].Values[0]);
			Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"100k snapshot took {stopwatch.Elapsed}.");

			Console.WriteLine($"100k snapshot build+query: {stopwatch.ElapsedMilliseconds} ms");
		}

		private static Task<DirectoryQueryResult> ExecuteAsync(
			string sql,
			DirectoryQueryExecutionOptions? options = null,
			CancellationToken cancellationToken = default)
		{
			return new SqliteDirectoryQueryService().ExecuteAsync(
				CreateSnapshot(),
				sql,
				options ?? DirectoryQueryExecutionOptions.Default,
				cancellationToken);
		}

		private static DirectoryQuerySnapshot CreateSnapshot()
		{
			var files = new[]
			{
				new DirectoryQueryFileSnapshot(0, "report.txt", "report", ".txt", @"C:\Data\report.txt", "Text Document", 1024, Modified, Created, false),
				new DirectoryQueryFileSnapshot(1, "folder", "folder", null, @"C:\Data\folder", "File folder", null, null, null, true),
				new DirectoryQueryFileSnapshot(2, "notes.md", "notes", ".md", @"C:\Data\notes.md", "Markdown", 2048, Modified, Created, false),
			};

			var tags = new[] { new DirectoryQueryTagSnapshot("work", "Work", "#FF0000") };
			var fileTags = new[] { new DirectoryQueryFileTagSnapshot(0, "work") };

			return new DirectoryQuerySnapshot(@"C:\Data", 7, files, tags, fileTags);
		}
	}
}
