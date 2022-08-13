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

namespace VsChromium.Features.ToolWindows.CodeSearch {
  public partial class CodeSearchController : ICodeSearchController {
    private static class OperationsIds {
      public const string FileSystemScanning = "file-system-scanning";
      public const string FilesLoading = "files-loading";
      public const string SearchCode = "files-contents-search";
      public const string SearchFilePaths = "file-names-search";
    }

    private class FlatFileResultPendingLoad
    {
      public string Path { get; set; }
      public IEnumerable<FlatFilePositionViewModel> FilePositions { get; set; }
    }

    private readonly CodeSearchControl _control;
    private readonly IDispatchThreadServerRequestExecutor _dispatchThreadServerRequestExecutor;
    private readonly IFileSystemTreeSource _fileSystemTreeSource;
    // ReSharper disable once NotAccessedField.Local
    private readonly ITypedRequestProcessProxy _typedRequestProcessProxy;
    private readonly IProgressBarTracker _progressBarTracker;
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
    private readonly SearchResultsDocumentChangeTracker _searchResultDocumentChangeTracker;
    private readonly object _eventBusCookie1;
    private readonly object _eventBusCookie2;
    private readonly object _eventBusCookie3;

    private long _currentFileSystemTreeVersion = -1;
    private bool _performSearchOnNextRefresh;
    
    enum SearchType
    {
      Invalid,
      Code,
      Files
    }
    private SearchType _performSearchOnLoad = SearchType.Invalid;
    private string _cachedSearchPattern = null;

    /// <summary>
    /// For generating unique id n progress bar tracker.
    /// </summary>
    private int _operationSequenceId;

    private Queue<FlatFileResultPendingLoad> _flatResultsPendingLoad;
    private bool _flatResultsPendingLoadReinitialized;
    private readonly object _flatResultsPendingLoadLock;
    private readonly AutoResetEvent _flatResultsPendingLoadEvent;

    public CodeSearchController(
      CodeSearchControl control,
      IDispatchThreadServerRequestExecutor dispatchThreadServerRequestExecutor,
      IDispatchThreadDelayedOperationExecutor dispatchThreadDelayedOperationExecutor,
      IFileSystemTreeSource fileSystemTreeSource,
      ITypedRequestProcessProxy typedRequestProcessProxy,
      IProgressBarTracker progressBarTracker,
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
      _progressBarTracker = progressBarTracker;
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
      _searchResultDocumentChangeTracker = new SearchResultsDocumentChangeTracker(
        dispatchThreadDelayedOperationExecutor,
        textDocumentTable);
      _taskCancellation = new TaskCancellation();

      _flatResultsPendingLoad = new Queue<FlatFileResultPendingLoad>();
      _flatResultsPendingLoadLock = new object();
      _flatResultsPendingLoadEvent = new AutoResetEvent(false);

      // Ensure initial values are in sync.
      GlobalSettingsOnPropertyChanged(null, null);

      // Ensure changes to ViewModel are synchronized to global settings
      ViewModel.PropertyChanged += ViewModelOnPropertyChanged;

      // Ensure changes to global settings are synchronized to ViewModel
      _globalSettingsProvider.GlobalSettings.PropertyChanged += GlobalSettingsOnPropertyChanged;

      _eventBusCookie1 = _eventBus.RegisterHandler(EventNames.TextDocument.DocumentOpened, TextDocumentOpenHandler);
      _eventBusCookie2 = _eventBus.RegisterHandler(EventNames.TextDocument.DocumentClosed, TextDocumentClosedHandler);
      _eventBusCookie3 = _eventBus.RegisterHandler(EventNames.TextDocument.DocumentFileActionOccurred, TextDocumentFileActionOccurred);

      typedRequestProcessProxy.EventReceived += TypedRequestProcessProxy_OnEventReceived;

      dispatchThreadServerRequestExecutor.ProcessFatalError += DispatchThreadServerRequestExecutor_OnProcessFatalError;

      fileSystemTreeSource.TreeReceived += FileSystemTreeSource_OnTreeReceived;
      fileSystemTreeSource.ErrorReceived += FileSystemTreeSource_OnErrorReceived;

      new Thread(FlatResultsPendingLoadLoop) { IsBackground = true }.Start();
    }

    public CodeSearchViewModel ViewModel => _control.ViewModel;
    public IDispatchThreadServerRequestExecutor DispatchThreadServerRequestExecutor => _dispatchThreadServerRequestExecutor;
    public IStandarImageSourceFactory StandarImageSourceFactory => _standarImageSourceFactory;
    public IClipboard Clipboard => _clipboard;
    public IWindowsExplorer WindowsExplorer => _windowsExplorer;
    public GlobalSettings GlobalSettings => _globalSettingsProvider.GlobalSettings;
    public ISynchronizationContextProvider SynchronizationContextProvider => _synchronizationContextProvider;
    public IOpenDocumentHelper OpenDocumentHelper => _openDocumentHelper;

    public void Dispose() {
      Logger.LogInfo("{0} disposed.", GetType().FullName);

      _globalSettingsProvider.GlobalSettings.PropertyChanged -= GlobalSettingsOnPropertyChanged;
      _eventBus.UnregisterHandler(_eventBusCookie1);
      _eventBus.UnregisterHandler(_eventBusCookie2);
      _eventBus.UnregisterHandler(_eventBusCookie3);
    }

    public void Start() {
      // Server may not be started yet, so display a message as we wait for start-up
      var items = CreateInfromationMessages("Waiting for VsChromium index server to respond to first request");
      ViewModel.SetInformationMessages(items);

      // Send a request to server to ensure it is started and we have up to date
      // information file system version, index, etc.
      _fileSystemTreeSource.Fetch();
    }

    public void PerformSearch(bool immediate) {
      var searchCodeText = ViewModel.SearchCodeValue;
      var searchFilePathsText = ViewModel.SearchFilePathsValue;

      if (string.IsNullOrWhiteSpace(searchCodeText) &&
          string.IsNullOrWhiteSpace(searchFilePathsText)) {
        CancelSearch();
        return;
      }

      if (string.IsNullOrWhiteSpace(searchCodeText)) {
        SearchFilesPaths(searchFilePathsText, immediate);
        return;
      }

      SearchCode(searchCodeText, searchFilePathsText, immediate);
    }

    public void QuickSearchCode(string searchPattern) {
      if (!ViewModel.ServerIsRunning) {
        _performSearchOnLoad = SearchType.Code;
        _cachedSearchPattern = searchPattern;
        return;
      }
      if (!string.IsNullOrEmpty(searchPattern)) {
        _control.SearchCodeCombo.Text = searchPattern;
      }
      //_control.SearchFilePathsCombo.Text = "";
      _control.SearchCodeCombo.Focus();
      PerformSearch(true);
    }

    public void QuickFilePaths(string searchPattern) {
      if (!ViewModel.ServerIsRunning) {
        _performSearchOnLoad = SearchType.Files;
        _cachedSearchPattern = searchPattern;
        return;
      }
      //ExplorerControl.SearchCodeCombo.Text = "";
      if (!string.IsNullOrEmpty(searchPattern)) {
        _control.SearchFilePathsCombo.Text = searchPattern;
      }
      _control.SearchFilePathsCombo.Focus();
      PerformSearch(true);
    }

    public void FocusQuickSearchCode() {
      if (!ViewModel.ServerIsRunning) {
        return;
      }
      //ExplorerControl.SearchFilePathsCombo.Text = "";
      _control.SearchCodeCombo.Focus();
      PerformSearch(true);
    }

    public void FocusQuickSearchFilePaths() {
      if (!ViewModel.ServerIsRunning) {
        return;
      }
      //ExplorerControl.SearchCodeCombo.Text = "";
      _control.SearchFilePathsCombo.Focus();
      PerformSearch(true);
    }

    public void CancelSearch() {
      _searchResultDocumentChangeTracker.Disable();
      ViewModel.SwitchToInformationMessages();
    }

    public void ShowServerInfo(bool forceGarbageCollection) {
      _showServerInfoService.ShowServerStatusDialog(forceGarbageCollection);
    }

    public void RefreshFileSystemTree() {
      var uiRequest = new DispatchThreadServerRequest {
        Request = new RefreshFileSystemTreeRequest(),
        Id = "RefreshFileSystemTreeRequest",
        Delay = TimeSpan.FromSeconds(0.0),
        OnThreadPoolSend = () => {
          _performSearchOnNextRefresh = true;
        }
      };

      _dispatchThreadServerRequestExecutor.Post(uiRequest);
    }

    public void PauseResumeIndexing() {
      var uiRequest = ViewModel.IndexingPaused
        ? new DispatchThreadServerRequest {
          Request = new ResumeIndexingRequest(),
          Id = nameof(ResumeIndexingRequest),
          RunOnSequentialQueue = true,
          Delay = TimeSpan.FromSeconds(0.0),
          OnThreadPoolReceive = FetchDatabaseStatistics,
        }
        : new DispatchThreadServerRequest {
          Request = new PauseIndexingRequest(),
          Id = nameof(PauseIndexingRequest),
          RunOnSequentialQueue = true,
          Delay = TimeSpan.FromSeconds(0.0),
          OnThreadPoolReceive = FetchDatabaseStatistics,
        };

      _dispatchThreadServerRequestExecutor.Post(uiRequest);
    }

    public void OpenFileInEditor(IFileEntryViewModel fileEntry, int lineNumber, int columnNumber, int length) {
      OpenFileInEditorWorker(fileEntry, vsTextView => TranslateLineColumnToSpan(vsTextView, lineNumber, columnNumber, length));
    }

    public void OpenFileInEditor(IFileEntryViewModel fileEntry, Span? span) {
      OpenFileInEditorWorker(fileEntry, _ => span);
    }

    private void OpenFileInEditorWorker(IFileEntryViewModel fileEntry, Func<IVsTextView, Span?> spanProvider) {
      // Using "Post" is important: it allows the newly opened document to
      // receive the focus.
      SynchronizationContextProvider.DispatchThreadContext.Post(() => {
        // Note: This has to run on the UI thread!
        OpenDocumentHelper.OpenDocument(fileEntry.Path, vsTextView => {
          var span = spanProvider(vsTextView);
          return _searchResultDocumentChangeTracker.TranslateSpan(fileEntry.GetFullPath(), span);
        });
      });
    }

    public void OpenFileInEditorWith(IFileEntryViewModel fileEntry, int lineNumber, int columnNumber, int length) {
      OpenFileInEditorWithWorker(fileEntry, vsTextView => TranslateLineColumnToSpan(vsTextView, lineNumber, columnNumber, length));
    }

    public void OpenFileInEditorWith(IFileEntryViewModel fileEntry, Span? span) {
      OpenFileInEditorWithWorker(fileEntry, _ => span);
    }

    /// <summary>
    /// Navigate to the FileSystemTree directory entry corresponding to
    /// <paramref name="relativePathEntry"/>. This is a no-op if the FileSystemTree
    /// is already the currently active ViewModel.
    /// </summary>
    public void ShowInSourceExplorer(FileSystemEntryViewModel relativePathEntry) {
      var path = relativePathEntry.GetFullPath();
      _eventBus.PostEvent(EventNames.SolutionExplorer.ShowFile, relativePathEntry, new FilePathEventArgs {
        FilePath = path
      });
    }

    public void BringItemViewModelToView(TreeViewItemViewModel item) {
      // We look for the tree view item corresponding to "item", swallowing
      // the "BringIntoView" request to avoid flickering as we descend into
      // the virtual tree and realize the sub-panels at each level.
      _control.SwallowsRequestBringIntoView(true);
      var treeViewItem = SelectTreeViewItem(_control.FileTreeView, item);

      // If we found it, allow the "BringIntoView" requests to be handled
      // and ask the tree view item to bring itself into view.
      // Note: The "BrinIntoView" call is a no-op if the tree view item
      // is already visible.
      if (treeViewItem != null) {
        _control.SwallowsRequestBringIntoView(false);
        treeViewItem.BringIntoView();
        _control.SwallowsRequestBringIntoView(true);
      }
    }

    public TreeViewItem SelectTreeViewItem(TreeView treeView, TreeViewItemViewModel item) {
      return WpfUtilities.SelectTreeViewItem(treeView, item);
    }

    public bool ExecuteOpenCommandForItem(TreeViewItemViewModel tvi) {
      if (tvi == null)
        return false;

      if (!tvi.IsSelected)
        return false;

      {
        var filePosition = tvi as FilePositionViewModel;
        if (filePosition != null) {
          filePosition.OpenCommand.Execute(filePosition);
          return true;
        }
      }

      {
        var fileEntry = tvi as FileEntryViewModel;
        if (fileEntry != null) {
          fileEntry.OpenCommand.Execute(fileEntry);
          return true;
        }
      }

      {
        var directoryEntry = tvi as DirectoryEntryViewModel;
        if (directoryEntry != null) {
          directoryEntry.OpenCommand.Execute(directoryEntry);
          return true;
        }
      }
      
      {
        var flatFileEntry = tvi as FlatFilePositionViewModel;
        if (flatFileEntry != null) {
          flatFileEntry.OpenCommand.Execute(flatFileEntry);
          return true;
        }
      }

      return false;
    }

    public bool HasNextLocation() {
      return GetNextLocationEntry(Direction.Next) != null;
    }

    public bool HasPreviousLocation() {
      return GetNextLocationEntry(Direction.Previous) != null;
    }

    public void NavigateToNextLocation() {
      var nextItem = GetNextLocationEntry(Direction.Next);
      NavigateToTreeViewItem(nextItem);
    }

    public void NavigateToPreviousLocation() {
      var previousItem = GetNextLocationEntry(Direction.Previous);
      NavigateToTreeViewItem(previousItem);
    }

    private void DispatchThreadServerRequestExecutor_OnProcessFatalError(object sender, ErrorEventArgs args) {
      ViewModel.ServerIsRunning = false;
      ReportServerError(ErrorResponseHelper.CreateErrorResponse(args.GetException()));
    }

    private void FileSystemTreeSource_OnTreeReceived(FileSystemTree fileSystemTree) {
      WpfUtilities.Post(_control, () => {
        ViewModel.ServerHasStarted = true;
        ViewModel.ServerIsRunning = true;
        OnFileSystemTreeScanSuccess(fileSystemTree);
      });
    }

    private void FileSystemTreeSource_OnErrorReceived(ErrorResponse errorResponse) {
      WpfUtilities.Post(_control, () => {
        ReportServerError(errorResponse);
      });
    }

    private void TextDocumentOpenHandler(object sender, EventArgs eventArgs) {
      var doc = (ITextDocument)sender;
      var args = eventArgs;
      _searchResultDocumentChangeTracker.DocumentOpen(doc, args);
    }

    private void TextDocumentClosedHandler(object sender, EventArgs eventArgs) {
      var doc = (ITextDocument)sender;
      var args = eventArgs;
      _searchResultDocumentChangeTracker.DocumentClose(doc, args);
    }

    private void TextDocumentFileActionOccurred(object sender, EventArgs eventArgs) {
      var doc = (ITextDocument)sender;
      var args = (TextDocumentFileActionEventArgs)eventArgs;
      //Logger.LogInfo("  FileActionOccurred \"{0}\": {1}", doc.FilePath, args.FileActionType);
      _searchResultDocumentChangeTracker.FileActionOccurred(doc, args);
    }

    private void GlobalSettingsOnPropertyChanged(object sender, PropertyChangedEventArgs args) {
      var setting = _globalSettingsProvider.GlobalSettings;
      ViewModel.FlattenSearchResults = setting.FlattenSearchResults;
      ViewModel.DisplayRelativePath = setting.DisplayRelativePath;
      ViewModel.MatchCase = setting.SearchMatchCase;
      ViewModel.MatchWholeWord = setting.SearchMatchWholeWord;
      ViewModel.UseRegex = setting.SearchUseRegEx;
      ViewModel.IncludeSymLinks = setting.SearchIncludeSymLinks;
      ViewModel.UseSpaceAsWildcard = setting.SearchSpaceAsWildcard;
      ViewModel.UnderstandBuildOutputPaths = setting.SearchUnderstandBuildOutputPaths;
      ViewModel.TextExtractFontFamily = setting.TextExtractFont.FontFamily.Name;
      ViewModel.TextExtractFontSize = setting.TextExtractFont.Size;
      ViewModel.DisplayFontFamily = setting.DisplayFont.FontFamily.Name;
      ViewModel.DisplayFontSize = setting.DisplayFont.Size;
      ViewModel.PathFontFamily = setting.PathFont.FontFamily.Name;
      ViewModel.PathFontSize = setting.PathFont.Size;
      ViewModel.HorizontalSearchLayout = setting.SearchHorizontalLayout;
    }

    private void ViewModelOnPropertyChanged(object sender, PropertyChangedEventArgs args) {
      var settings = _globalSettingsProvider.GlobalSettings;
      var model = (CodeSearchViewModel)sender;
      settings.SearchMatchCase = model.MatchCase;
      settings.SearchMatchWholeWord = model.MatchWholeWord;
      settings.SearchUseRegEx = model.UseRegex;
      settings.SearchIncludeSymLinks = model.IncludeSymLinks;
      settings.SearchSpaceAsWildcard = model.UseSpaceAsWildcard;
      settings.FlattenSearchResults = model.FlattenSearchResults;
      settings.SearchHorizontalLayout = model.HorizontalSearchLayout;
    }

    private void OnIndexingStateChanged() {
      FetchDatabaseStatistics();
    }

    private void OnFileSystemTreeScanStarted() {
      // Display a generic "loading" message the first time a file system tree
      // is loaded.
      if (!ViewModel.FileSystemTreeAvailable) {
        var items = CreateInfromationMessages(
          "Loading files from VS Chromium projects...");
        ViewModel.SetInformationMessagesNoActivate(items);

        // Display the new info messages, except if there is an active search
        // result.
        if (ViewModel.ActiveDisplay == CodeSearchViewModel.DisplayKind.InformationMessages) {
          _searchResultDocumentChangeTracker.Disable();
          ViewModel.SwitchToInformationMessages();
        }
      }
    }

    private void OnFileSystemTreeScanSuccess(FileSystemTree tree) {
      switch (_performSearchOnLoad)
      {
        case SearchType.Files:
          QuickFilePaths(_cachedSearchPattern);
          _performSearchOnLoad = SearchType.Invalid;
          return;
        case SearchType.Code:
          QuickSearchCode(_cachedSearchPattern);
          _performSearchOnLoad = SearchType.Invalid;
          return;
      }

      ViewModel.FileSystemTreeAvailable = (tree.Projects.Count > 0);
      _currentFileSystemTreeVersion = tree.Version;

      if (ViewModel.FileSystemTreeAvailable) {
        var items = CreateInfromationMessages(
          "No search results available - Type text to search for " +
          "in the \"Search Code\" or \"File Paths\" text box.");
        ViewModel.SetInformationMessagesNoActivate(items);
        if (ViewModel.ActiveDisplay == CodeSearchViewModel.DisplayKind.InformationMessages) {
          _searchResultDocumentChangeTracker.Disable();
          ViewModel.SwitchToInformationMessages();
        }

        RefreshView(tree.Version);
        FetchDatabaseStatistics();
      } else {
        var items = CreateInfromationMessages(
          "Open a source file from a local Chromium enlistment or" + "\r\n" +
          string.Format("from a directory containing a \"{0}\" file.", ConfigurationFileNames.ProjectFileName));
        ViewModel.SetInformationMessagesNoActivate(items);
        _searchResultDocumentChangeTracker.Disable();
        ViewModel.SwitchToInformationMessages();
        FetchDatabaseStatistics();
      }
    }

    private void ReportServerError(ErrorResponse error) {
      if (!ErrorResponseHelper.IsReportableError(error))
        return;

      var viewModel = CreateErrorResponseViewModel(error);
      ViewModel.SetInformationMessages(viewModel);
    }

    private void OnFilesLoadingProgress() {
      FetchDatabaseStatistics();
    }

    private void OnFilesLoaded(long treeVersion) {
      RefreshView(treeVersion);
      FetchDatabaseStatistics();
    }

    /// <summary>
    /// Refresh the view model after a significant event (such as a new file
    /// system tree, files loaded, etc.)
    /// </summary>
    private void RefreshView(long treeVersion) {
      // We'll get an update soon enough.
      if (treeVersion != _currentFileSystemTreeVersion)
        return;

      // Perform a new search if the user forced an index refresh
      if (_performSearchOnNextRefresh) {
        _performSearchOnNextRefresh = false;
        PerformSearch(true);
      } else {
        // Add top level nodes in search results explaining results may be outdated
        AddResultsOutdatedMessage();
      }
    }

    private void AddResultsOutdatedMessage() {
      // Don't do this for now because
      // 1) the message tends to be annoying
      // 2) there is an issue with "BringObjectToView" that assumes nodes list
      //    have the same # of children, and we get an error 
      //    "TreeView item data context is not the right data object."
#if false
      // Add only if we have search results currently displayed.
      if (ViewModel.ActiveDisplay == CodeSearchViewModel.DisplayKind.InformationMessages) {
        return;
      }

      // Add message only once. 
      if (ViewModel.RootNodes.Count > 0 &&
          ViewModel.RootNodes[0] is TextWarningItemViewModel) {
        return;
      }

      // Add message
      var parent = ViewModel.RootNodes[0].ParentViewModel;
      var item = new TextWarningItemViewModel(
        StandarImageSourceFactory,
        parent,
        "Search results may be out of date as index has been updated. Run search again to display up-to-date results.");
      ViewModel.RootNodes.Insert(0, item);
#endif
    }

    private Span? TranslateLineColumnToSpan(IVsTextView vsTextView, int lineNumber, int columnNumber, int length) {
      if (lineNumber < 0)
        return null;

      var textView = _adaptersFactoryService.GetWpfTextView(vsTextView);
      if (textView == null)
        return null;

      var snapshot = textView.TextBuffer.CurrentSnapshot;
      if (lineNumber < 0 || lineNumber >= snapshot.LineCount)
        return null;

      var line = snapshot.GetLineFromLineNumber(lineNumber);

      // Ensure columnNumber and length are in the line bounds
      columnNumber = Math.Max(columnNumber, 0);
      columnNumber = Math.Min(columnNumber, line.Length);
      length = Math.Max(0, length);
      length = Math.Min(length, line.Length - columnNumber);

      return new Span(line.Start + columnNumber, length);
    }

    private void OpenFileInEditorWithWorker(IFileEntryViewModel fileEntry, Func<IVsTextView, Span?> spanProvider) {
      // Using "Post" is important: it allows the newly opened document to
      // receive the focus.
      SynchronizationContextProvider.DispatchThreadContext.Post(() => {
        // Note: This has to run on the UI thread!
        OpenDocumentHelper.OpenDocumentWith(fileEntry.Path, null, 0, vsTextView => {
          var span = spanProvider(vsTextView);
          return _searchResultDocumentChangeTracker.TranslateSpan(fileEntry.GetFullPath(), span);
        });
      });
    }

    private List<TreeViewItemViewModel> CreateInfromationMessages(params string[] messages) {
      var result = new List<TreeViewItemViewModel>();
      var rootNode = new RootTreeViewItemViewModel(StandarImageSourceFactory);
      foreach (var text in messages) {
        result.Add(new TextItemViewModel(StandarImageSourceFactory, rootNode, text));
      }
      TreeViewItemViewModel.ExpandNodes(result, true);
      return result;
    }

    private List<TreeViewItemViewModel> CreateSearchFilePathsResult(
      FilePathSearchInfo searchInfo,
      DirectoryEntry fileResults,
      string description,
      string additionalWarning,
      bool expandAll) {

      Action<FileSystemEntryViewModel> setLineColumn = entry => {
        var fileEntry = entry as IFileEntryViewModel;
        if (fileEntry != null && searchInfo.LineNumber >= 0)
          fileEntry.SetLineColumn(searchInfo.LineNumber, searchInfo.ColumnNumber);
      };
      
      var flattenResults = ViewModel.FlattenSearchResults;
      var rootNode = new RootTreeViewItemViewModel(StandarImageSourceFactory);
      var result = Enumerable
        .Empty<TreeViewItemViewModel>()
        .ConcatSingle(new TextItemViewModel(StandarImageSourceFactory, rootNode, description))
        .ConcatSingle(new TextWarningItemViewModel(StandarImageSourceFactory, rootNode, additionalWarning), () => !string.IsNullOrEmpty(additionalWarning))
        .Concat(fileResults.Entries.Select(x => FileSystemEntryViewModel.Create(this, rootNode, x, setLineColumn, flattenResults)).SelectMany(x => x).SelectMany(x => x))
        .ToList();
      result.ForAll(rootNode.AddChild);
      TreeViewItemViewModel.ExpandNodes(result, expandAll);
      return result;
    }

    private List<TreeViewItemViewModel> CreateSearchCodeResultViewModel(
        DirectoryEntry searchResults,
        string description,
        string additionalWarning,
        bool expandAll) {
      var flattenResults = ViewModel.FlattenSearchResults;
      var rootNode = new RootTreeViewItemViewModel(StandarImageSourceFactory);
      var fileSystemEntryCollections = searchResults.Entries.Select(x => FileSystemEntryViewModel.Create(this, rootNode, x, _ => { }, flattenResults)).ToList();
      var result = Enumerable
        .Empty<TreeViewItemViewModel>()
        .ConcatSingle(new TextItemViewModel(StandarImageSourceFactory, rootNode, description))
        .ConcatSingle(new TextWarningItemViewModel(StandarImageSourceFactory, rootNode, additionalWarning), () => !string.IsNullOrEmpty(additionalWarning))
        .Concat(fileSystemEntryCollections.SelectMany(x => x).SelectMany(x => x))
        .ToList();
      result.ForAll(rootNode.AddChild);
      TreeViewItemViewModel.ExpandNodes(result, expandAll);

      if (flattenResults)
      {
        lock (_flatResultsPendingLoadLock)
        {
          if (fileSystemEntryCollections.Any())
          {
            _flatResultsPendingLoad = new Queue<FlatFileResultPendingLoad>(
            fileSystemEntryCollections.Single().Select(
              entryCollection => new FlatFileResultPendingLoad
              {
                Path = entryCollection.First().GetFullPath(),
                FilePositions = entryCollection.Cast<FlatFilePositionViewModel>()
              }));
          }
          else
          {
            _flatResultsPendingLoad = new Queue<FlatFileResultPendingLoad>();
          }

          _flatResultsPendingLoadReinitialized = true;
          _flatResultsPendingLoadEvent.Set();
        }
      }

      return result;
    }

    private List<TreeViewItemViewModel> CreateErrorResponseViewModel(ErrorResponse errorResponse) {
      var messages = new List<TreeViewItemViewModel>();
      if (errorResponse.IsRecoverable()) {
        // For a recoverable error, the deepest exception contains the 
        // "user friendly" error message.
        var rootError = new TextWarningItemViewModel(
          StandarImageSourceFactory,
          null,
          errorResponse.GetBaseError().Message);
        messages.Add(rootError);
      } else {
        var message = ViewModel.ServerHasStarted
          ? "There was an issue sending a request to the index server."
          : "There was an issue starting the index server.";
        // In case of non recoverable error, display a generic "user friendly"
        // message, with nested nodes for exception messages.
        var rootError = new TextErrorItemViewModel(
          StandarImageSourceFactory,
          null,
          message + " You may need to restart Visual Studio.");
        messages.Add(rootError);

        // Add all errors to the parent
        while (errorResponse != null) {
          rootError.Children.Add(new TextItemViewModel(StandarImageSourceFactory, rootError, errorResponse.Message));
          errorResponse = errorResponse.InnerError;
        }
      }
      TreeViewItemViewModel.ExpandNodes(messages, true);
      return messages;
    }

    private void SearchWorker(SearchWorkerParams workerParams) {
      // Cancel all previously running tasks
      _taskCancellation.CancelAll();
      var cancellationToken = _taskCancellation.GetNewToken();

      var id = Interlocked.Increment(ref _operationSequenceId);
      var progressId = string.Format("{0}-{1}", workerParams.OperationName, id);
      var sw = new Stopwatch();
      var request = new DispatchThreadServerRequest {
        // Note: Having a single ID for all searches ensures previous search
        // requests are superseeded.
        Id = "MetaSearch",
        Request = workerParams.TypedRequest,
        Delay = workerParams.Delay,
        OnThreadPoolSend = () => {
          sw.Start();
          _progressBarTracker.Start(progressId, workerParams.HintText);
        },
        OnThreadPoolReceive = () => {
          sw.Stop();
          _progressBarTracker.Stop(progressId);
        },
        OnDispatchThreadSuccess = typedResponse => {
          if (cancellationToken.IsCancellationRequested)
            return;
          workerParams.ProcessResponse(typedResponse, sw);
        },
        OnDispatchThreadError = errorResponse => {
          if (cancellationToken.IsCancellationRequested)
            return;
          workerParams.ProcessError(errorResponse, sw);
        }
      };

      _dispatchThreadServerRequestExecutor.Post(request);
    }

    private void FetchDatabaseStatistics() {
      _showServerInfoService.FetchDatabaseStatistics(UpdateIndexingServerToolbarStatus);
    }

    private void UpdateIndexingServerToolbarStatus(GetDatabaseStatisticsResponse response) {
      ViewModel.IndexingPaused = response.ServerStatus == IndexingServerStatus.Paused || response.ServerStatus == IndexingServerStatus.Yield;
      ViewModel.IndexingPausedDueToError = response.ServerStatus == IndexingServerStatus.Yield;
      ViewModel.IndexingBusy = response.ServerStatus == IndexingServerStatus.Busy;
      ViewModel.IndexStatusText = _showServerInfoService.GetIndexStatusText(response);
      ViewModel.IndexingServerStateText = _showServerInfoService.GetIndexingServerStatusText(response);
      ViewModel.ServerStatusToolTipText = _showServerInfoService.GetIndexingServerStatusToolTipText(response);
    }

    private string PreproccessFilePathSearchString(string searchPattern) { 
      if (ViewModel.UseSpaceAsWildcard && searchPattern != null) {
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
      }
      else {
        return searchPattern;
      }
    }

    private FilePathSearchInfo PreprocessFilePathSearchPattern(string searchPattern) {
      var result = _globalSettingsProvider.GlobalSettings.SearchUnderstandBuildOutputPaths
        ? _buildOutputParser.ParseFullOrRelativePath(searchPattern)
        : null;

      if (result == null || result.LineNumber < 0) {
        var processedSearchPattern = PreproccessFilePathSearchString(searchPattern);
        return new FilePathSearchInfo {
          RawSearchPattern = searchPattern,
          SearchPattern = processedSearchPattern,
          LineNumber = -1,
          ColumnNumber = -1
        };
      }

      return new FilePathSearchInfo {
        RawSearchPattern = searchPattern,
        SearchPattern = result.FileName,
        LineNumber = result.LineNumber,
        ColumnNumber = result.ColumnNumber
      };
    }

    private void SearchFilesPaths(string searchPattern, bool immediate) {
      var searchInfo = PreprocessFilePathSearchPattern(searchPattern);
      var maxResults = GlobalSettings.SearchFilePathsMaxResults;
      SearchWorker(new SearchWorkerParams {
        OperationName = OperationsIds.SearchFilePaths,
        HintText = "Searching for matching file paths...",
        Delay = TimeSpan.FromMilliseconds(immediate ? 0 : GlobalSettings.AutoSearchDelayMsec),
        TypedRequest = new SearchFilePathsRequest {
          SearchParams = new SearchParams {
            SearchString = searchInfo.SearchPattern,
            MaxResults = maxResults,
            MatchCase = false, //ViewModel.MatchCase,
            MatchWholeWord = false, //ViewModel.MatchWholeWord,
            IncludeSymLinks = ViewModel.IncludeSymLinks,
            UseRe2Engine = true,
            Regex = false, //ViewModel.UseRegex,
          }
        },
        ProcessError = (errorResponse, stopwatch) => {
          var viewModel = CreateErrorResponseViewModel(errorResponse);
          _searchResultDocumentChangeTracker.Disable();
          ViewModel.SetSearchFilePathsResult(viewModel);
        },
        ProcessResponse = (typedResponse, stopwatch) => {
          var response = ((SearchFilePathsResponse)typedResponse);
          var msg = string.Format("Found {0:n0} path(s) among {1:n0} ({2:0.00} seconds) matching pattern \"{3}\"",
            response.HitCount,
            response.TotalCount,
            stopwatch.Elapsed.TotalSeconds,
            ViewModel.UseSpaceAsWildcard ? searchInfo.RawSearchPattern : searchInfo.SearchPattern);
          if (searchInfo.LineNumber >= 0) {
            msg += ", Line " + (searchInfo.LineNumber + 1);
          }
          if (searchInfo.ColumnNumber >= 0) {
            msg += ", Column " + (searchInfo.ColumnNumber + 1);
          }
          var limitMsg = CreateMaxResultsHitMessage(response.HitCount, maxResults);
          var viewModel = CreateSearchFilePathsResult(searchInfo, response.SearchResult, msg, limitMsg, true);
          _searchResultDocumentChangeTracker.Disable();
          ViewModel.SetSearchFilePathsResult(viewModel);
        }
      });
    }

    private void SearchCode(string searchPattern, string filePathPattern, bool immediate) {
      var maxResults = GlobalSettings.SearchCodeMaxResults;
      var request = CreateSearchCodeRequest(searchPattern, filePathPattern, maxResults);
      SearchWorker(new SearchWorkerParams {
        OperationName = OperationsIds.SearchCode,
        HintText = "Searching for matching text in files...",
        Delay = TimeSpan.FromMilliseconds(immediate ? 0 : GlobalSettings.AutoSearchDelayMsec),
        TypedRequest = request,
        ProcessError = (errorResponse, stopwatch) => {
          var viewModel = CreateErrorResponseViewModel(errorResponse);
          ViewModel.SetSearchCodeResult(viewModel);
          _searchResultDocumentChangeTracker.Disable();
        },
        ProcessResponse = (typedResponse, stopwatch) => {
          var response = ((SearchCodeResponse)typedResponse);
          var msg = string.Format("Found {0:n0} results among {1:n0} files ({2:0.00} seconds) matching text \"{3}\"",
            response.HitCount,
            response.SearchedFileCount,
            stopwatch.Elapsed.TotalSeconds,
            searchPattern);
          if (ViewModel.MatchCase) {
            msg += ", Match case";
          }
          if (ViewModel.MatchWholeWord) {
            msg += ", Whole word";
          }
          if (ViewModel.UseRegex) {
            msg += ", Regular expression";
          }
          if (!string.IsNullOrEmpty(filePathPattern)) {
            msg += string.Format(", File Paths: \"{0}\"", filePathPattern);
          }
          var limitMsg = CreateMaxResultsHitMessage(response.HitCount, maxResults);
          bool expandAll = response.HitCount < HardCodedSettings.SearchCodeExpandMaxResults;
          var result = CreateSearchCodeResultViewModel(response.SearchResults, msg, limitMsg, expandAll);
          ViewModel.SetSearchCodeResult(result);
          _searchResultDocumentChangeTracker.Enable(response.SearchResults);

          // Perform additional search if few results and search was restrictive
          if (response.HitCount <= HardCodedSettings.LowHitCountWarrantingAdditionalSearch) {
            if (request.SearchParams.MatchCase || request.SearchParams.MatchWholeWord) {
              SearchCodeLessRestrictive(searchPattern, filePathPattern, response.HitCount);
            }
          }
        }
      });
    }

    private void SearchCodeLessRestrictive(string searchPattern, string filePathPattern, long previousHitCount) {
      var maxResults = GlobalSettings.SearchCodeMaxResults;
      var request = CreateSearchCodeRequest(searchPattern, filePathPattern, maxResults);
      request.SearchParams.MatchCase = false;
      request.SearchParams.MatchWholeWord = false;

      SearchWorker(new SearchWorkerParams {
        OperationName = OperationsIds.SearchCode,
        HintText = "Searching for matching text in files...",
        Delay = TimeSpan.FromMilliseconds(0),
        TypedRequest = request,
        ProcessError = (errorResponse, stopwatch) => {
          // Nothing to do, ignore
          Logger.LogError(errorResponse.CreateException(), "Error running less restrictive search request");
        },
        ProcessResponse = (typedResponse, stopwatch) => {
          var response = ((SearchCodeResponse)typedResponse);
          // If same # of hits, no addional message
          if (response.HitCount <= previousHitCount) {
            return;
          }

          if (ViewModel.ActiveDisplay == CodeSearchViewModel.DisplayKind.SearchCodeResult) {
            if (ViewModel.RootNodes.Count > 0) {
              var parent = ViewModel.RootNodes[0];
              var msg = string.Format("{0:n0} results would be available with both Match case and Match word disabled", response.HitCount);
              parent.Children.Add(new TextItemViewModel(StandarImageSourceFactory, parent, msg));
            }
          }
        }
      });
    }

    private string PreprocessSearchString(string searchPattern)
    {
      if(ViewModel.UseRegex) {
        return searchPattern;
      }

      if(ViewModel.UseSpaceAsWildcard) { 
        string SpaceWildcard = "( |[A-Za-z0-9_:]*)";
        string SpaceWildcardWholeWord = "( |[A-Za-z0-9_]*)";

        StringBuilder searchPatternBuilder = new StringBuilder();
        string[] searchTerms = searchPattern.Split(' ');
        foreach(string searchTerm in searchTerms) {
          if (searchTerm.Length == 0)
            continue;

          string escapedTerm = Regex.Escape(searchTerm);
          escapedTerm = Regex.Replace(escapedTerm, @"(^|[^\\])\\\*", "$1.*");
          escapedTerm = Regex.Replace(escapedTerm, @"\\\\\\*", "\\*");

          if (searchPatternBuilder.Length == 0)
            searchPatternBuilder.Append(escapedTerm);
          else
            searchPatternBuilder.AppendFormat("{0}{1}", ViewModel.MatchWholeWord ? SpaceWildcardWholeWord : SpaceWildcard, escapedTerm);
        }
        return searchPatternBuilder.ToString();

      }

      return searchPattern;
    }

    private SearchCodeRequest CreateSearchCodeRequest(string searchPattern, string filePathPattern, int maxResults) {
      var processedSearchPattern = PreprocessSearchString(searchPattern);
      var processedFilePathPattern = PreproccessFilePathSearchString(filePathPattern);

      return new SearchCodeRequest {
        SearchParams = new SearchParams {
          SearchString = processedSearchPattern,
          FilePathPattern = processedFilePathPattern,
          MaxResults = maxResults,
          MatchCase = ViewModel.MatchCase,
          MatchWholeWord = ViewModel.MatchWholeWord,
          IncludeSymLinks = ViewModel.IncludeSymLinks,
          UseRe2Engine = true,
          Regex = ViewModel.UseRegex || ViewModel.UseSpaceAsWildcard,
        }
      };
    }

    private string CreateMaxResultsHitMessage(long hitCount, long maxResults) {
      if (hitCount >= maxResults) {
        return string.Format(
          "Maximum number of results ({0:n0}) hit - some results omitted. " +
          "Use Tools-> Options -> VsChromium -> General to increase the limit, " +
          "or change your query to exclude more results.",
          maxResults);
      }
      return "";
    }

    enum Direction {
      Next,
      Previous
    }

    private T GetNextLocationEntry<T>(Direction direction) where T : class, IHierarchyObject {
      var item = _control.FileTreeView.SelectedItem;
      if (item == null) {
        if (ViewModel.ActiveRootNodes == null)
          return null;

        if (ViewModel.ActiveRootNodes.Count == 0)
          return null;

        item = ViewModel.ActiveRootNodes[0].ParentViewModel;
        if (item == null)
          return null;
      }

      var nextItem = (direction == Direction.Next)
        ? new HierarchyObjectNavigator().GetNextItemOfType<T>(item as IHierarchyObject)
        : new HierarchyObjectNavigator().GetPreviousItemOfType<T>(item as IHierarchyObject);

      return nextItem;
    }

    private TreeViewItemViewModel GetNextLocationEntry(Direction direction) {
      if (ViewModel.ActiveDisplay == CodeSearchViewModel.DisplayKind.SearchCodeResult) {
        return GetNextLocationEntry<FilePositionViewModel>(direction);
      }

      if (ViewModel.ActiveDisplay == CodeSearchViewModel.DisplayKind.SearchFilePathsResult) {
        return GetNextLocationEntry<FileEntryViewModel>(direction);
      }

      return null;
    }

    private void NavigateToTreeViewItem(TreeViewItemViewModel item) {
      if (item == null)
        return;
      BringItemViewModelToView(item);
      ExecuteOpenCommandForItem(item);
    }

    private void TypedRequestProcessProxy_OnEventReceived(TypedEvent typedEvent) {
      DispatchFileSystemTreeScanStarted(typedEvent);
      DispatchFileSystemTreeScanFinished(typedEvent);
      DispatchSearchEngineFilesLoading(typedEvent);
      DispatchSearchEngineFilesLoadingProgress(typedEvent);
      DispatchSearchEngineFilesLoaded(typedEvent);
      DispatchIndexingStateChanged(typedEvent);
    }

    private void DispatchIndexingStateChanged(TypedEvent typedEvent) {
      var @event = typedEvent as IndexingServerStateChangedEvent;
      if (@event != null) {
        WpfUtilities.Post(_control, () => {
          Logger.LogInfo("Indexing state has changed to \"{0}\".", @event.ServerStatus);
          OnIndexingStateChanged();
        });
      }
    }

    private void DispatchFileSystemTreeScanStarted(TypedEvent typedEvent) {
      var @event = typedEvent as FileSystemScanStarted;
      if (@event != null) {
        WpfUtilities.Post(_control, () => {
          Logger.LogInfo("FileSystemTree is being computed on server.");
          _progressBarTracker.Start(OperationsIds.FileSystemScanning,
            "Loading files and directory names from file system.");
          OnFileSystemTreeScanStarted();
        });
      }
    }

    private void DispatchFileSystemTreeScanFinished(TypedEvent typedEvent) {
      var evt = typedEvent as FileSystemScanFinished;
      if (evt != null) {
        WpfUtilities.Post(_control, () => {
          _progressBarTracker.Stop(OperationsIds.FileSystemScanning);
          if (evt.IsReportableError()) {
            ReportServerError(evt.Error);
            return;
          }
          Logger.LogInfo("New FileSystemTree bas been computed on server: version={0}.", evt.NewVersion);
        });
      }
    }

    private void DispatchSearchEngineFilesLoading(TypedEvent typedEvent) {
      var evt = typedEvent as SearchEngineFilesLoading;
      if (evt != null) {
        WpfUtilities.Post(_control, () => {
          Logger.LogInfo("Search engine is loading file database on server.");
          _progressBarTracker.Start(OperationsIds.FilesLoading, "Loading files contents from file system.");
        });
      }
    }

    private void DispatchSearchEngineFilesLoadingProgress(TypedEvent typedEvent) {
      var evt = typedEvent as SearchEngineFilesLoadingProgress;
      if (evt != null) {
        WpfUtilities.Post(_control, () => {
          OnFilesLoadingProgress();
          Logger.LogInfo("Search engine has produced intermediate file database index on server.");
        });
      }
    }

    private void DispatchSearchEngineFilesLoaded(TypedEvent typedEvent) {
      var evt = typedEvent as SearchEngineFilesLoaded;
      if (evt != null) {
        WpfUtilities.Post(_control, () => {
          _progressBarTracker.Stop(OperationsIds.FilesLoading);
          OnFilesLoaded(evt.TreeVersion);
          if (evt.IsReportableError()) {
            ReportServerError(evt.Error);
            return;
          }
          Logger.LogInfo("Search engine is done loading file database on server.");
        });
      }
    }

    private void FlatResultsPendingLoadLoop()
    {
      try
      {
        while (true)
        {
          _flatResultsPendingLoadEvent.WaitOne();
          bool moreToProcess;
          lock (_flatResultsPendingLoadLock)
          {
            var numberToProcess = Math.Min(
              _flatResultsPendingLoadReinitialized
                ? _globalSettingsProvider.GlobalSettings.FlatResultsMaxImmediateRequests
                : 1,
              _flatResultsPendingLoad.Count);

            moreToProcess = numberToProcess < _flatResultsPendingLoad.Count;

            _flatResultsPendingLoadReinitialized = false;

            for (int i = 0; i < numberToProcess; ++i)
            {
              var resultPendingLoad = _flatResultsPendingLoad.Dequeue();

              FlatFilePositionViewModel.LoadFileExtracts(this, resultPendingLoad.Path, resultPendingLoad.FilePositions);
            }
          }

          if (moreToProcess)
          {
            int flatResultsRequestsPerSecond = Math.Max(_globalSettingsProvider.GlobalSettings.FlatResultsRequestsPerSecond, 1);

            Thread.Sleep(TimeSpan.FromSeconds(1.0 / flatResultsRequestsPerSecond));
            _flatResultsPendingLoadEvent.Set();
          }
        }
      }
      catch (Exception e)
      {
        Logger.LogError(e, "Error in FlatResultsPendingLoadLoop.");
      }
    }
  }
}
