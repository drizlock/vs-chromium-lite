// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VsChromium.Features.ToolWindows.OpenFile {
  public class OpenFileViewModel {
    private ObservableCollection<FileEntryViewModel> _fileList = new ObservableCollection<FileEntryViewModel>();

    public OpenFileViewModel() {
    }

    public void UpdateFileList(IEnumerable<FileEntryViewModel> fileList) {
      _fileList.Clear();

      foreach (var item in fileList)
        _fileList.Add(item);
    }

    public void ClearFileList() {
      _fileList.Clear();
    }

    public bool ServerIsRunning {
      get; set;
    }

    public ObservableCollection<FileEntryViewModel> FileList {
      get { return _fileList; }
    }

    public string SearchPattern { get; set; }
  }
}
