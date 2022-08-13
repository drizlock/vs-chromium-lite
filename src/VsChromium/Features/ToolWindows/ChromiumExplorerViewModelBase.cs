// Copyright 2015 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using VsChromium.Core.Linq;
using VsChromium.Settings;
using VsChromium.Views;

namespace VsChromium.Features.ToolWindows {
  public class ChromiumExplorerViewModelBase : INotifyPropertyChanged {
    private readonly TreeViewRootNodes<TreeViewItemViewModel> _rootNodes;
    private List<TreeViewItemViewModel> _activeRootNodes;

    /// <summary>
    /// Databound!
    /// </summary>
    public TreeViewRootNodes<TreeViewItemViewModel> RootNodes { get { return _rootNodes; } }

    public List<TreeViewItemViewModel> ActiveRootNodes { get { return _activeRootNodes; } }

    public IStandarImageSourceFactory ImageSourceFactory { get; set; }

    public event PropertyChangedEventHandler PropertyChanged;

    public event EventHandler RootNodesChanged;

    public ChromiumExplorerViewModelBase() {
      _rootNodes = new TreeViewRootNodes<TreeViewItemViewModel>(HardCodedSettings.MaxExpandedTreeViewItemCount, CreateLazyItemViewModel);
    }

    protected void SetRootNodes(List<TreeViewItemViewModel> newRootNodes) {
      // Don't update if we are passed in the already active collection.
      if (object.ReferenceEquals(_activeRootNodes, newRootNodes))
        return;
      _activeRootNodes = newRootNodes;

      // Move the active root nodes into the observable collection so that
      // the TreeView is refreshed.
      _rootNodes.Clear();
      _activeRootNodes.ForAll(_rootNodes.Add);

      this.OnRootNodesChanged();
    }

    protected virtual void OnRootNodesChanged() {
      var handler = RootNodesChanged;
      if (handler != null) handler(this, EventArgs.Empty);
    }

    protected virtual void OnPropertyChanged(string propertyName) {
      PropertyChangedEventHandler handler = PropertyChanged;
      if (handler != null)
        handler(this, new PropertyChangedEventArgs(propertyName));
    }

    private LazyItemViewModel CreateLazyItemViewModel() {
      var result = new LazyItemViewModel(null, null);
      if (_activeRootNodes.Count > HardCodedSettings.MaxExpandedTreeViewItemCount)
        result.Text = string.Format("(Click to expand {0:n0} additional items...)",
                                    _activeRootNodes.Count - HardCodedSettings.MaxExpandedTreeViewItemCount);
      result.Expand += () => {
        var node = _rootNodes.ExpandLazyNode();
        node.IsSelected = true;
      };
      return result;
    }
  }
}
