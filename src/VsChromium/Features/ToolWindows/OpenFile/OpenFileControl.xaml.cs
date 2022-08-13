// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Navigation;
using VsChromium.Core.Logging;
using VsChromium.Core.Utility;
using VsChromium.Features.AutoUpdate;
using VsChromium.Features.BuildOutputAnalyzer;
using VsChromium.Features.IndexServerInfo;
using VsChromium.Package;
using VsChromium.ServerProxy;
using VsChromium.Settings;
using VsChromium.Threads;
using VsChromium.Views;
using VsChromium.Wpf;

namespace VsChromium.Features.ToolWindows.OpenFile {
  /// <summary>
  /// Interaction logic for CodeSearchControl.xaml
  /// </summary>
  public partial class OpenFileControl {

    private IDispatchThreadServerRequestExecutor _dispatchThreadServerRequestExecutor;
    private OpenFileController _controller;
    private OpenFileToolWindow _toolWindow;
    private ListSortDirection _sortDirection;

    public OpenFileControl() {
      InitializeComponent();
      // Add the "VsColors" brushes to the WPF resources of the control, so that the
      // resource keys used on the XAML file can be resolved dynamically.
      Resources.MergedDictionaries.Add(VsResources.BuildResourceDictionary());
      DataContext = new OpenFileViewModel();

      SearchFileTextBox.TextChanged += (s, e) => {
        ViewModel.SearchPattern = SearchFileTextBox.Text;
        RefreshSearchResults(false);
      };
    }

    /// <summary>
    /// Called when Visual Studio creates our container ToolWindow.
    /// </summary>
    public void OnVsToolWindowCreated(IServiceProvider serviceProvider) {
      _toolWindow = serviceProvider as OpenFileToolWindow;
      var componentModel = (IComponentModel)serviceProvider.GetService(typeof(SComponentModel));

      _dispatchThreadServerRequestExecutor = componentModel.DefaultExportProvider.GetExportedValue<IDispatchThreadServerRequestExecutor>();

      var standarImageSourceFactory = componentModel.DefaultExportProvider.GetExportedValue<IStandarImageSourceFactory>();
      _controller = new OpenFileController(
        this,
        _dispatchThreadServerRequestExecutor,
        componentModel.DefaultExportProvider.GetExportedValue<IDispatchThreadDelayedOperationExecutor>(),
        componentModel.DefaultExportProvider.GetExportedValue<IFileSystemTreeSource>(),
        componentModel.DefaultExportProvider.GetExportedValue<ITypedRequestProcessProxy>(),
        standarImageSourceFactory,
        componentModel.DefaultExportProvider.GetExportedValue<IWindowsExplorer>(),
        componentModel.DefaultExportProvider.GetExportedValue<IClipboard>(),
        componentModel.DefaultExportProvider.GetExportedValue<ISynchronizationContextProvider>(),
        componentModel.DefaultExportProvider.GetExportedValue<IOpenDocumentHelper>(),
        componentModel.DefaultExportProvider.GetExportedValue<ITextDocumentTable>(),
        componentModel.DefaultExportProvider.GetExportedValue<IDispatchThreadEventBus>(),
        componentModel.DefaultExportProvider.GetExportedValue<IGlobalSettingsProvider>(),
        componentModel.DefaultExportProvider.GetExportedValue<IBuildOutputParser>(),
        componentModel.DefaultExportProvider.GetExportedValue<IVsEditorAdaptersFactoryService>(),
        componentModel.DefaultExportProvider.GetExportedValue<IShowServerInfoService>());
      _controller.Start();
    }

    public OpenFileViewModel ViewModel {
      get {
        return (OpenFileViewModel)DataContext;
      }
    }

    public IOpenFileController Controller {
      get { return _controller; }
    }

    public void UpdateSelection() {
      FileListView.SelectedItems.Clear();

      if (FileListView.Items.Count > 0) {
        int index = 0;
        int spaceIndex = SearchFileTextBox.Text.IndexOf(" ");
        string matchWord = spaceIndex > -1 ? SearchFileTextBox.Text.Substring(0, spaceIndex) : SearchFileTextBox.Text;

        for (int i = 0; i < FileListView.Items.Count; ++i) {
          FileEntryViewModel fileEntry = FileListView.Items[i] as FileEntryViewModel;
          if (fileEntry.Filename.StartsWith(matchWord, StringComparison.CurrentCultureIgnoreCase)) {
            index = i;
            break;
          }
        }

        FileListView.SelectedItems.Add(FileListView.Items[index]);
        FileListView.ScrollIntoView(FileListView.Items[index]);
      }
    }

#region WPF Event handlers

    private void RefreshSearchResults(bool immediate) {
      Controller.PerformSearch(immediate);
    }

    private void ClearFilePathsPattern_Click(object sender, RoutedEventArgs e) {
      SearchFileTextBox.Text = "";
      RefreshSearchResults(true);
      SearchFileTextBox.Focus();
    }

    void ListViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
      var item = sender as ListBoxItem;
      if (item == null)
        return;

      e.Handled = OpenFile(item.DataContext as FileEntryViewModel);
    }

    void GridViewColumnHeader_Click(object sender, RoutedEventArgs e) {
      GridViewColumnHeader headerClicked = e.OriginalSource as GridViewColumnHeader;
      ListView listView = sender as ListView;
      ICollectionView view = CollectionViewSource.GetDefaultView(listView.ItemsSource);

      if (headerClicked.Column == null)
        return;

      string header = "";
      if (headerClicked.Column.DisplayMemberBinding != null) {
        header = ((System.Windows.Data.Binding)headerClicked.Column.DisplayMemberBinding).Path.Path;
      } else {
        try {
          DataTemplate cellTemplate = headerClicked.Column.CellTemplate;
          Grid grid = cellTemplate.LoadContent() as Grid;
          TextBlock textBlock = grid.Children.OfType<TextBlock>().FirstOrDefault();
          header = BindingOperations.GetBinding(textBlock, TextBlock.TextProperty).Path.Path;
        } catch {
        }
      }

      string lastHeaderName = view.SortDescriptions[0].PropertyName;
      ListSortDirection lastDirection = view.SortDescriptions[0].Direction;
      if (headerClicked != null) {
        if (headerClicked.Role != GridViewColumnHeaderRole.Padding) {
          if (header != lastHeaderName) {
            _sortDirection = ListSortDirection.Ascending;
          } else {
            if (lastDirection == ListSortDirection.Ascending) {
              _sortDirection = ListSortDirection.Descending;
            } else {
              _sortDirection = ListSortDirection.Ascending;
            }
          }
          if (header != "") {
            SortDescription monsort = new SortDescription(header, _sortDirection);
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(monsort);
          }
        }
      }
    }

    private void FileListView_PreviewKeyDown(object sender, KeyEventArgs e) {
      if (e.Key == Key.Enter) {
        foreach (var item in FileListView.SelectedItems) {
          OpenFile(item as FileEntryViewModel);
        }
      } else if (e.Key == Key.Escape) {
        if (!_toolWindow.IsDocked)
          _toolWindow.Hide();
      } else if (e.Key != Key.Up && e.Key != Key.Down) {
        SearchFileTextBox.Focus();
      }
    }

    private void SearchFileTextBox_PreviewKeyDown(object sender, KeyEventArgs e) {
      if (e.Key == Key.Up || e.Key == Key.Down) {
        FileListView.Focus();
        var container = (UIElement)FileListView.ItemContainerGenerator.ContainerFromItem(FileListView.SelectedItem);
        if (container != null) {
          container.Focus();
        }
      } else if (e.Key == Key.Enter) {
        foreach (var item in FileListView.SelectedItems) {
          OpenFile(item as FileEntryViewModel);
        }
      } else if (e.Key == Key.Escape) {
        if (!_toolWindow.IsDocked)
          _toolWindow.Hide();
      }
    }

    private bool OpenFile(FileEntryViewModel item) {
      if (Controller.ExecuteOpenCommandForItem(item)) {
        if (!_toolWindow.IsDocked)
          _toolWindow.Hide();
        return true;
      }

      return false;
    }
#endregion
  }
}
