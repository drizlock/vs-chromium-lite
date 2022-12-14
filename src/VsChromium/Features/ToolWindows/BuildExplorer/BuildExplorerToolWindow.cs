// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using VsChromium.Commands;

namespace VsChromium.Features.ToolWindows.BuildExplorer {
  [Guid(GuidList.GuidBuildExplorerToolWindowString)]
  public class BuildExplorerToolWindow  : ToolWindowPane {
    /// <summary>
    /// Standard constructor for the tool window.
    /// </summary>
    public BuildExplorerToolWindow()
      : base(null) {
      // Set the window title reading it from the resources.
      Caption = Resources.BuildExplorerToolWindowTitle;
      // Set the image that will appear on the tab of the window frame
      // when docked with an other window
      // The resource ID correspond to the one defined in the resx file
      // while the Index is the offset in the bitmap strip. Each image in
      // the strip being 16x16.
      BitmapResourceID = 301;
      BitmapIndex = 1;

      // This is the user control hosted by the tool window; Note that, even if this class implements IDisposable,
      // we are not calling Dispose on this object. This is because ToolWindowPane calls Dispose on 
      // the object returned by the Content property.
      ExplorerControl = new BuildExplorerControl();
    }

    public BuildExplorerControl ExplorerControl {
      get { return Content as BuildExplorerControl; } 
      set { Content = value; }
    }

    public override void OnToolWindowCreated() {
      base.OnToolWindowCreated();
      ExplorerControl.OnToolWindowCreated(this);
    }
  }
}