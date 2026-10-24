// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;

namespace Files.App
{
	internal sealed partial class PortableDataLease : IDisposable
	{
		private readonly FileStream _lockStream;

		private PortableDataLease(FileStream lockStream)
		{
			_lockStream = lockStream;
		}

		public static PortableDataLease Acquire(string dataDirectory)
		{
			var lockPath = Path.Combine(dataDirectory, ".instance.lock");
			return new(new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
		}

		public void Dispose() => _lockStream.Dispose();
	}
}
