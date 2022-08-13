// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System;
using VsChromium.Threads;
using VsChromium.Views;

namespace VsChromium.Features.ToolWindows.OpenFile {
  public interface IOpenFileController : IDisposable {
    IDispatchThreadServerRequestExecutor DispatchThreadServerRequestExecutor { get; }
    IWindowsExplorer WindowsExplorer { get; }

    void Start();

    void PerformSearch(bool immediate);
    void FileSearch(string searchPattern);
    void FocusSearch();

    void OpenFileInEditor(FileEntryViewModel fileEntry);
    bool ExecuteOpenCommandForItem(FileEntryViewModel fevm);
  }
}
