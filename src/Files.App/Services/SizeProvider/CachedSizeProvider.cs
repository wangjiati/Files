// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers;
using System.IO;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;

namespace Files.App.Services.SizeProvider
{
	public sealed partial class CachedSizeProvider : ISizeProvider
	{
		private const int MaxCachedPaths = 4096;

		private readonly LruCache<string, ulong> sizes = new(MaxCachedPaths);

		public event EventHandler<SizeChangedEventArgs>? SizeChanged;

		// Session-level cache: dropping the oldest half is enough, misses are recalculated on demand.
		public Task CleanAsync()
		{
			sizes.EvictOldest(sizes.Count / 2);
			return Task.CompletedTask;
		}

		public Task ClearAsync()
		{
			sizes.Clear();
			return Task.CompletedTask;
		}

		public async Task UpdateAsync(string path, CancellationToken cancellationToken)
		{
			await Task.Yield();
			if (!sizes.TryGetValue(path, out _))
				RaiseSizeChanged(path, 0, SizeChangedValueState.None);

			var stopwatch = Stopwatch.StartNew();
			ulong size = await Calculate(path);

			sizes.Set(path, size);
			RaiseSizeChanged(path, size, SizeChangedValueState.Final);

			async Task<ulong> Calculate(string path, int level = 0)
			{
				if (string.IsNullOrEmpty(path))
					return 0;

				static unsafe (FindCloseSafeHandle, WIN32_FIND_DATAW) FindFirstFileEx(string path)
				{
					WIN32_FIND_DATAW initialFindData = default;
					var hFile = PInvoke.FindFirstFileEx($"{path}{Path.DirectorySeparatorChar}*.*", FINDEX_INFO_LEVELS.FindExInfoBasic,
						&initialFindData, FINDEX_SEARCH_OPS.FindExSearchNameMatch, FIND_FIRST_EX_FLAGS.FIND_FIRST_EX_LARGE_FETCH);
					var findData = initialFindData;
					return (hFile, findData);
				}
				(FindCloseSafeHandle hFile, WIN32_FIND_DATAW findData) = FindFirstFileEx(path);
				using FindCloseSafeHandle findHandleScope = hFile;

				ulong size = 0;
				ulong localSize = 0;
				string localPath = string.Empty;

				if (!hFile.IsInvalid)
				{
					do
					{
						if (((FileAttributes)findData.dwFileAttributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
							// Skip symbolic links and junctions
							continue;

						bool isDirectory = ((FileAttributes)findData.dwFileAttributes & FileAttributes.Directory) is FileAttributes.Directory;
						if (!isDirectory)
						{
							size += (ulong)findData.GetSize();
						}
						else
						{
							string fileName = findData.cFileName.ToString();
							if (fileName is not "." and not "..")
							{
								localPath = Path.Combine(path, fileName);
								localSize = await Calculate(localPath, level + 1);
								size += localSize;
							}
						}

						if (level <= 3)
						{
							await Task.Yield();
							sizes.Set(localPath, localSize);
						}
						if (level is 0 && stopwatch.ElapsedMilliseconds > 500)
						{
							// Limit updates to every 0.5 seconds to prevent crashes due to frequent updates
							stopwatch.Restart();
							RaiseSizeChanged(path, size, SizeChangedValueState.Intermediate);
						}

						if (cancellationToken.IsCancellationRequested)
							break;
					} while (PInvoke.FindNextFile(hFile, out findData));
				}
				return size;
			}
		}

		public bool TryGetSize(string path, out ulong size) => sizes.TryGetValue(path, out size);

		public void Dispose() { }

		private void RaiseSizeChanged(string path, ulong newSize, SizeChangedValueState valueState)
			=> SizeChanged?.Invoke(this, new SizeChangedEventArgs(path, newSize, valueState));
	}
}
