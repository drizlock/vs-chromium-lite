// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Linq;
using VsChromium.Core.Files;
using VsChromium.Core.Ipc.TypedMessages;
using VsChromium.Core.Linq;

namespace VsChromium.Features.ToolWindows.CodeSearch {
  public abstract class FileSystemEntryViewModel : CodeSearchItemViewModelBase {
    protected FileSystemEntryViewModel(
        ICodeSearchController controller,
        TreeViewItemViewModel parentViewModel,
        bool lazyLoadChildren)
      : base(controller, parentViewModel, lazyLoadChildren) {
    }

    public abstract FileSystemEntry FileSystemEntry { get; }

    public string Name { get { return FileSystemEntry.Name; } }

    public override string DisplayText {
      get {
        return this.Name;
      }
    }

    public static IEnumerable<IEnumerable<FileSystemEntryViewModel>> Create(
      ICodeSearchController host,
      TreeViewItemViewModel parentViewModel,
      FileSystemEntry fileSystemEntry,
      Action<FileSystemEntryViewModel> postCreate,
      bool flattenResults) {
      var fileEntry = fileSystemEntry as FileEntry;
      if (fileEntry != null) {
        return new[] { CreateFileEntry(null, fileEntry, host, parentViewModel, fileSystemEntry, postCreate, flattenResults) };
      }
      else {
        if (flattenResults) { 
          var directoryEntry = fileSystemEntry as DirectoryEntry;
          return directoryEntry
            .Entries
            .Select(entry => CreateFileEntry(directoryEntry, (FileEntry) entry, host, parentViewModel, fileSystemEntry, postCreate, flattenResults))
            .ToList();
        }
        else { 
          var result = new DirectoryEntryViewModel(host, parentViewModel, (DirectoryEntry) fileSystemEntry, postCreate);
          postCreate(result);
          return new[] { new[] { result } };
        }
      }
    }

    private static IEnumerable<FileSystemEntryViewModel> CreateFileEntry(
      DirectoryEntry directoryEntry,
      FileEntry fileEntry,
      ICodeSearchController host,
      TreeViewItemViewModel parentViewModel,
      FileSystemEntry fileSystemEntry, 
      Action<FileSystemEntryViewModel> postCreate,
      bool flattenResults) {
        if (flattenResults) {
          var positionsData = fileEntry.Data as FilePositionsData;
          if (positionsData != null) {
            var flatFilePositions = positionsData
              .Positions
              .Select(x => new FlatFilePositionViewModel(host, parentViewModel, directoryEntry, fileEntry, x))
              .ToList();
            flatFilePositions.ForAll(postCreate);
            return flatFilePositions;
          }
          else {
            var result = new FlatFilePositionViewModel(host, parentViewModel, directoryEntry, fileEntry, null);
            postCreate(result);
            return new[] { result };
          }
        }
        else { 
          var result = new FileEntryViewModel(host, parentViewModel, fileEntry);
          postCreate(result);
          return new[] { result };
        }
    }

    public virtual string GetFullPath() {
      var parent = ParentViewModel as FileSystemEntryViewModel;
      if (parent == null)
        return Name;
      return PathHelpers.CombinePaths(parent.GetFullPath(), Name);
    }

    public virtual string GetRelativePath() {
      var parent = ParentViewModel as FileSystemEntryViewModel;
      if (parent == null)
        return "";
      return PathHelpers.CombinePaths(parent.GetRelativePath(), Name);
    }
  }
}
