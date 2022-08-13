// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using Microsoft.VisualStudio.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using VsChromium.Core.Files;
using VsChromium.Core.Ipc.TypedMessages;
using VsChromium.Core.Linq;
using VsChromium.Core.Utility;
using VsChromium.Threads;

namespace VsChromium.Features.ToolWindows.CodeSearch {
  public class FlatFilePositionViewModel : FileSystemEntryViewModel, IFileEntryViewModel {
    private readonly DirectoryEntry _directoryEntry;
    private readonly FileEntry _fileEntry;
    private readonly FilePositionSpan _matchPosition;
    private FileExtract _extractPosition;
    private int _lineNumber = -1;
    private int _columnNumber = -1;

    public FlatFilePositionViewModel(
      ICodeSearchController controller,
      TreeViewItemViewModel parentViewModel,
      DirectoryEntry directoryEntry,
      FileEntry fileEntry,
      FilePositionSpan matchPosition)
      : base(controller, parentViewModel, fileEntry.Data != null) {
      _directoryEntry = directoryEntry;
      _fileEntry = fileEntry;
      _matchPosition = matchPosition;
    }

    public override FileSystemEntry FileSystemEntry { get { return _fileEntry; } }

    public override string DisplayText {
      get {
        if(Controller.GlobalSettings.DisplayRelativePath) { 
          if (_extractPosition == null)
            return _fileEntry.Name;

          return string.Format("{0}({1}): ", GetRelativePath(), _extractPosition.LineNumber + 1);
        }
        else { 
          if (_extractPosition == null)
            return PathHelpers.CombinePaths(_directoryEntry?.Name, _fileEntry.Name);

          return string.Format("{0}({1}): ", GetFullPath(), _extractPosition.LineNumber + 1);
        }
      }
    }

    public string CopyPathAndText { 
      get {
        if(Controller.GlobalSettings.DisplayRelativePath) { 
          if (_extractPosition == null) {
              return string.Format("{0}{1}", GetRelativePath(), LineColumnText);
          }
        
          return string.Format("{0}({1}): {2}", GetRelativePath(), _extractPosition.LineNumber + 1, _extractPosition.Text.TrimEnd(Environment.NewLine.ToArray()));
        }
        else { 
          if (_extractPosition == null) {
              return string.Format("{0}{1}", GetFullPath(), LineColumnText);
          }
        
          return string.Format("{0}({1}): {2}", GetFullPath(), _extractPosition.LineNumber + 1, _extractPosition.Text.TrimEnd(Environment.NewLine.ToArray()));
        }
      }
    }

    public string CopyText { 
      get {
        if(Controller.GlobalSettings.DisplayRelativePath) { 
          if (_extractPosition == null) {
              return string.Format("{0}{1}", GetRelativePath(), LineColumnText);
          }
        
          return string.Format("{0}", _extractPosition.Text.Trim(Environment.NewLine.ToArray()));
        }
        else { 
          if (_extractPosition == null) {
              return string.Format("{0}{1}", GetFullPath(), LineColumnText);
          }
        
          return string.Format("{0}", _extractPosition.Text.TrimEnd(Environment.NewLine.ToArray()));
        }
      }
    }

    public int Position { get { return _matchPosition.Position; } }

    public int Length { get { return _matchPosition.Length; } }

    public string Path { get { return GetFullPath(); } }

    /// <summary>
    /// Databound! Return text representing  of items (if children are present)
    /// </summary>
    public string LineColumnText {
      get {
        if (_lineNumber < 0)
          return "";
        
        if (_columnNumber < 0)
          return string.Format("({0})", _lineNumber + 1);

        return string.Format("({0},{1})", _lineNumber + 1, _columnNumber + 1);
      }
    }

    /// <summary>
    /// Databound! Return text representing  of items (if children are present)
    /// </summary>
    public string ItemCountText {
      get {
        if (ChildrenCount == 0)
          return "";

        if (ChildrenCount == 1)
          return " (1 item)";

        return string.Format(" ({0} items)", ChildrenCount);
      }
    }

    public bool HasMatchText { get { return _matchPosition != null; } }

    public string TextBeforeMatch {
      get {
        if (_extractPosition == null)
          return "";

        // [extract - match - extract]
        var offset = 0;
        var length = _matchPosition.Position - _extractPosition.Offset;
        return _extractPosition.Text.Substring(offset, length).TrimStart();
      }
    }

    public string MatchText {
      get {
        if (_extractPosition == null)
          return "";
        // [extract - match - extract]
        var offset = _matchPosition.Position - _extractPosition.Offset;
        var length = _matchPosition.Length;
        return _extractPosition.Text.Substring(offset, length);
      }
    }

    public string TextAfterMatch {
      get {
        if (_extractPosition == null)
          return "";
        // [extract - match - extract]
        var offset = _matchPosition.Position + _matchPosition.Length - _extractPosition.Offset;
        var length = _extractPosition.Length - offset;
        return _extractPosition.Text.Substring(offset, length).TrimEnd();
      }
    }

    #region Command Handlers

    public ICommand OpenCommand {
      get {
        return CommandDelegate.Create(sender => {
          if (_extractPosition != null) {
            Controller.OpenFileInEditor(this, _extractPosition.LineNumber, _extractPosition.ColumnNumber, Length);
          }
          else if(_matchPosition != null) {
            Controller.OpenFileInEditor(this, new Span(Position, Length));
          }
          else {
            Controller.OpenFileInEditor(this, _lineNumber, _columnNumber, 0);
          }
        });
      }
    }

    public ICommand OpenWithCommand {
      get {
        return CommandDelegate.Create(sender => {
          if (_extractPosition != null) {
            Controller.OpenFileInEditorWith(this, _extractPosition.LineNumber, _extractPosition.ColumnNumber, Length);
          }
          else if(_matchPosition != null) {
            Controller.OpenFileInEditorWith(this, new Span(Position, Length));
          }
          else {
            Controller.OpenFileInEditorWith(this, _lineNumber, _columnNumber, 0);
          }
        });
      }
    }

    public ICommand CopyCommand {
      get {
        return CommandDelegate.Create(sender => Controller.Clipboard.SetText(CopyPathAndText));
      }
    }

    public ICommand CopyFullPathCommand {
      get {
        return CommandDelegate.Create(sender => Controller.Clipboard.SetText(GetFullPath()));
      }
    }

    public ICommand CopyRelativePathCommand {
      get {
        return CommandDelegate.Create(sender => Controller.Clipboard.SetText(GetRelativePath()));
      }
    }

    public ICommand CopyFullPathPosixCommand {
      get {
        return CommandDelegate.Create(sender => Controller.Clipboard.SetText(PathHelpers.ToPosix(GetFullPath())));
      }
    }

    public ICommand CopyRelativePathPosixCommand {
      get {
        return CommandDelegate.Create(sender => Controller.Clipboard.SetText(PathHelpers.ToPosix(GetRelativePath())));
      }
    }

    public ICommand OpenContainingFolderCommand {
      get {
        return CommandDelegate.Create(sender => Controller.WindowsExplorer.OpenContainingFolder(this.GetFullPath()));
      }
    }

    public ICommand ShowInSourceExplorerCommand {
      get {
        return CommandDelegate.Create(
          sender => Controller.ShowInSourceExplorer(this),
          sender => Controller.GlobalSettings.EnableSourceExplorerHierarchy);
      }
    }

    #endregion
    
    public override string GetFullPath() {
      return PathHelpers.CombinePaths(_directoryEntry?.Name, _fileEntry.Name);
    }

    public override string GetRelativePath() {
      return _fileEntry.Name;
    }

    public void SetLineColumn(int lineNumber, int columnNumber) {
      _lineNumber = lineNumber;
      _columnNumber = columnNumber;
      OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.LineColumnText));
    }

    public void SetTextExtract(FileExtract value) {
      _extractPosition = value;
      OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.DisplayText));
      OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.TextBeforeMatch));
      OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.MatchText));
      OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.TextAfterMatch));
    }

    public static void LoadFileExtracts(ICodeSearchController host, string path, IEnumerable<FlatFilePositionViewModel> filePositions)
    {
      var positions = filePositions.ToList();
      if (!positions.Any())
        return;

      var request = new GetFileExtractsRequest
      {
        FileName = path,
        MaxExtractLength = host.GlobalSettings.MaxTextExtractLength,
        Positions = positions
          .Select(x => new FilePositionSpan
          {
            Position = x.Position,
            Length = x.Length
          })
          .ToList()
      };

      var uiRequest = new DispatchThreadServerRequest
      {
        Request = request,
        Id = "FlatFilePositionViewModel-" + path,
        Delay = TimeSpan.FromSeconds(0.0),
        OnDispatchThreadSuccess = (typedResponse) => {
          var response = (GetFileExtractsResponse)typedResponse;
          positions
            .Zip(response.FileExtracts, (x, y) => new {
              FilePositionViewModel = x,
              FileExtract = y
            })
            .Where(x => x.FileExtract != null)
            .ForAll(x => x.FilePositionViewModel.SetTextExtract(x.FileExtract));
        }
      };

      host.DispatchThreadServerRequestExecutor.Post(uiRequest);
    }
  }
}
