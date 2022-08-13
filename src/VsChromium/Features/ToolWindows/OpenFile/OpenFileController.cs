// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Controls;
using VsChromium.Core.Configuration;
using VsChromium.Core.Files;
using VsChromium.Core.Ipc;
using VsChromium.Core.Ipc.TypedMessages;
using VsChromium.Core.Linq;
using VsChromium.Core.Logging;
using VsChromium.Core.Threads;
using VsChromium.Features.BuildOutputAnalyzer;
using VsChromium.Features.IndexServerInfo;
using VsChromium.Features.SourceExplorerHierarchy;
using VsChromium.Package;
using VsChromium.ServerProxy;
using VsChromium.Settings;
using VsChromium.Threads;
using VsChromium.Views;
using VsChromium.Wpf;
using TreeView = System.Windows.Controls.TreeView;

namespace VsChromium.Features.ToolWindows.OpenFile {
  public partial class OpenFileController : IOpenFileController {
    private static class OperationsIds {
      public const string FileSystemScanning = "file-system-scanning";
      public const string FilesLoading = "files-loading";
      public const string SearchFilePaths = "file-names-search";
    }

    private readonly OpenFileControl _control;
    private readonly IDispatchThreadServerRequestExecutor _dispatchThreadServerRequestExecutor;
    private readonly IFileSystemTreeSource _fileSystemTreeSource;
    // ReSharper disable once NotAccessedField.Local
    private readonly ITypedRequestProcessProxy _typedRequestProcessProxy;
    private readonly IStandarImageSourceFactory _standarImageSourceFactory;
    private readonly IWindowsExplorer _windowsExplorer;
    private readonly IClipboard _clipboard;
    private readonly ISynchronizationContextProvider _synchronizationContextProvider;
    private readonly IOpenDocumentHelper _openDocumentHelper;
    private readonly IDispatchThreadEventBus _eventBus;
    private readonly IGlobalSettingsProvider _globalSettingsProvider;
    private readonly IBuildOutputParser _buildOutputParser;
    private readonly IVsEditorAdaptersFactoryService _adaptersFactoryService;
    private readonly IShowServerInfoService _showServerInfoService;
    private readonly TaskCancellation _taskCancellation;

    private long _currentFileSystemTreeVersion = -1;
    private string _cachedSearchPattern;

    /// <summary>
    /// For generating unique id n progress bar tracker.
    /// </summary>
    private int _operationSequenceId;

    public OpenFileController(
      OpenFileControl control,
      IDispatchThreadServerRequestExecutor dispatchThreadServerRequestExecutor,
      IDispatchThreadDelayedOperationExecutor dispatchThreadDelayedOperationExecutor,
      IFileSystemTreeSource fileSystemTreeSource,
      ITypedRequestProcessProxy typedRequestProcessProxy,
      IStandarImageSourceFactory standarImageSourceFactory,
      IWindowsExplorer windowsExplorer,
      IClipboard clipboard,
      ISynchronizationContextProvider synchronizationContextProvider,
      IOpenDocumentHelper openDocumentHelper,
      ITextDocumentTable textDocumentTable,
      IDispatchThreadEventBus eventBus,
      IGlobalSettingsProvider globalSettingsProvider,
      IBuildOutputParser buildOutputParser,
      IVsEditorAdaptersFactoryService adaptersFactoryService,
      IShowServerInfoService showServerInfoService) {
      _control = control;
      _dispatchThreadServerRequestExecutor = dispatchThreadServerRequestExecutor;
      _fileSystemTreeSource = fileSystemTreeSource;
      _typedRequestProcessProxy = typedRequestProcessProxy;
      _standarImageSourceFactory = standarImageSourceFactory;
      _windowsExplorer = windowsExplorer;
      _clipboard = clipboard;
      _synchronizationContextProvider = synchronizationContextProvider;
      _openDocumentHelper = openDocumentHelper;
      _eventBus = eventBus;
      _globalSettingsProvider = globalSettingsProvider;
      _buildOutputParser = buildOutputParser;
      _adaptersFactoryService = adaptersFactoryService;
      _showServerInfoService = showServerInfoService;
      _taskCancellation = new TaskCancellation();

      typedRequestProcessProxy.EventReceived += TypedRequestProcessProxy_OnEventReceived;

      dispatchThreadServerRequestExecutor.ProcessFatalError += DispatchThreadServerRequestExecutor_OnProcessFatalError;

      fileSystemTreeSource.TreeReceived += FileSystemTreeSource_OnTreeReceived;
      fileSystemTreeSource.ErrorReceived += FileSystemTreeSource_OnErrorReceived;
    }

    public OpenFileViewModel ViewModel => _control.ViewModel;
    public IDispatchThreadServerRequestExecutor DispatchThreadServerRequestExecutor => _dispatchThreadServerRequestExecutor;
    public IStandarImageSourceFactory StandarImageSourceFactory => _standarImageSourceFactory;
    public IClipboard Clipboard => _clipboard;
    public IWindowsExplorer WindowsExplorer => _windowsExplorer;
    public GlobalSettings GlobalSettings => _globalSettingsProvider.GlobalSettings;
    public ISynchronizationContextProvider SynchronizationContextProvider => _synchronizationContextProvider;
    public IOpenDocumentHelper OpenDocumentHelper => _openDocumentHelper;

    public void Dispose() {
      Logger.LogInfo("{0} disposed.", GetType().FullName);
    }

    public void Start() {
      // Send a request to server to ensure it is started and we have up to date
      // information file system version, index, etc.
      _fileSystemTreeSource.Fetch();
    }

    public void PerformSearch(bool immediate) {
      var searchFilePathsText = ViewModel.SearchPattern;

      if (string.IsNullOrWhiteSpace(searchFilePathsText)) {
        ViewModel.ClearFileList();
        return;
      }

      SearchFilesPaths(searchFilePathsText, immediate);
    }

    public void FileSearch(string searchPattern) {
      if (!ViewModel.ServerIsRunning) {
        return;
      }

      if (!string.IsNullOrEmpty(searchPattern)) {
        _control.SearchFileTextBox.Text = searchPattern;
      }
      _control.SearchFileTextBox.Focus();
      PerformSearch(true);
    }

    public void FocusSearch() {
      //ExplorerControl.SearchCodeCombo.Text = "";
      _control.SearchFileTextBox.Focus();
      PerformSearch(true);
    }

    private void DispatchThreadServerRequestExecutor_OnProcessFatalError(object sender, ErrorEventArgs args) {
    }

    private void FileSystemTreeSource_OnTreeReceived(FileSystemTree fileSystemTree) {
      WpfUtilities.Post(_control, () => {
        ViewModel.ServerIsRunning = true;
        OnFileSystemTreeScanSuccess(fileSystemTree);
      });
    }

    private void FileSystemTreeSource_OnErrorReceived(ErrorResponse errorResponse) {

      ViewModel.ServerIsRunning = false;
    }

    private void OnFileSystemTreeScanSuccess(FileSystemTree tree) {
      if (_cachedSearchPattern != null) {
        FileSearch(_cachedSearchPattern);
        _cachedSearchPattern = null;
      }

      _currentFileSystemTreeVersion = tree.Version;
      RefreshView(tree.Version);
    }

    /// <summary>
    /// Refresh the view model after a significant event (such as a new file
    /// system tree, files loaded, etc.)
    /// </summary>
    private void RefreshView(long treeVersion) {
      // We'll get an update soon enough.
      if (treeVersion != _currentFileSystemTreeVersion)
        return;

      PerformSearch(true);
    }

    private List<FileEntryViewModel> CreateSearchFilePathsResult(
      DirectoryEntry fileResults,
      string description,
      string additionalWarning,
      bool expandAll) {
      List<FileEntryViewModel> results = fileResults.Entries
                                           .Select(x => FileEntryViewModel.Create(this, x))
                                           .SelectMany(x => x)
                                           .ToList();
      results.Sort((item1, item2) => item1.Filename.CompareTo(item2.Filename));
      return results;
    }

    private void SearchFilesPaths(string searchPattern, bool immediate) {
      var processedSearchPattern = PreproccessFilePathSearchString(searchPattern);

      // Cancel all previously running tasks
      _taskCancellation.CancelAll();
      var cancellationToken = _taskCancellation.GetNewToken();

      var id = Interlocked.Increment(ref _operationSequenceId);
      var progressId = string.Format("{0}-{1}", OperationsIds.SearchFilePaths, id);
      var sw = new Stopwatch();
      var request = new DispatchThreadServerRequest {
        // Note: Having a single ID for all searches ensures previous search
        // requests are superseeded.
        Id = "MetaSearch",
        Request = new SearchFilePathsRequest { SearchParams = new SearchParams {
          SearchString = processedSearchPattern,
          MaxResults = 200, // TM_TODO
          MatchCase = false,
          MatchWholeWord = false,
          IncludeSymLinks = true,
          UseRe2Engine = true,
          Regex = false,
        } },
        Delay = TimeSpan.FromMilliseconds(immediate ? 0 : GlobalSettings.AutoSearchDelayMsec), OnThreadPoolSend = () => { sw.Start(); }, OnThreadPoolReceive = () => { sw.Stop(); }, OnDispatchThreadSuccess = typedResponse => {
          if (cancellationToken.IsCancellationRequested)
            return;
            var response = ((SearchFilePathsResponse)typedResponse);
            var msg = string.Format("Found {0:n0} path(s) among {1:n0} ({2:0.00} seconds) matching pattern \"{3}\"",
              response.HitCount,
              response.TotalCount,
              sw.Elapsed.TotalSeconds,
              processedSearchPattern);
            var viewModel = CreateSearchFilePathsResult(response.SearchResult, msg, "", true);
            ViewModel.UpdateFileList(viewModel);
            _control.UpdateSelection();
        }, OnDispatchThreadError = errorResponse => {
          if (cancellationToken.IsCancellationRequested)
            return; }
      };

      _dispatchThreadServerRequestExecutor.Post(request);
    }

    private string PreproccessFilePathSearchString(string searchPattern) {
      if (searchPattern != null) {
        StringBuilder searchPatternBuilder = new StringBuilder();
        string[] searchTerms = searchPattern.Split(';');
        foreach (string searchTerm in searchTerms) {
          if (searchTerm.Length == 0)
            continue;

          if (searchPatternBuilder.Length > 0)
            searchPatternBuilder.Append(";");

          string[] subterms = searchTerm.Split(' ');
          foreach (string subterm in subterms) {
            if (subterm.Length == 0)
              continue;

            // Automatically adds * around terms separated by space (unless the term contains special characters already)
            Match hasSpecialCharacters = Regex.Match(subterm, @".*[\\\/\*].*");
            if (hasSpecialCharacters.Success)
              searchPatternBuilder.Append(subterm);
            else
              searchPatternBuilder.AppendFormat("*{0}*", subterm);
          }
        }
        return searchPatternBuilder.ToString();
      } else {
        return searchPattern;
      }
    }

    private void TypedRequestProcessProxy_OnEventReceived(TypedEvent typedEvent) {
    }

    public void OpenFileInEditor(FileEntryViewModel fileEntry) {
      // Using "Post" is important: it allows the newly opened document to
      // receive the focus.
      SynchronizationContextProvider.DispatchThreadContext.Post(() => {
        // Note: This has to run on the UI thread!
        OpenDocumentHelper.OpenDocument(fileEntry.Path, null);
      });
    }

    public bool ExecuteOpenCommandForItem(FileEntryViewModel fevm) {
      if (fevm == null)
        return false;

      fevm.OpenCommand.Execute(fevm);
      return true;
    }
  }
}
