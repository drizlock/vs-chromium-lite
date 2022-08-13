// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System;
using VsChromium.Wpf;

namespace VsChromium.Features.ToolWindows {
  public class TreeViewRootNodes<T> : LazyObservableCollection<T>  where T : class {
    public TreeViewRootNodes(int lazyCount, Func<T> lazyItemFactory) : base(lazyCount, lazyItemFactory) {
    }
  }
}
