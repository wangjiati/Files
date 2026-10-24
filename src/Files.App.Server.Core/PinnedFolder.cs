// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Server.Core;

public sealed record PinnedFolder(string Path, string Name, byte[] Pidl, bool IsPinned);
