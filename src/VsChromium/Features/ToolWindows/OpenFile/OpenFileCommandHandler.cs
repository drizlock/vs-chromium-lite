// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System;
using System.ComponentModel.Composition;
using System.ComponentModel.Design;
using VsChromium.Commands;
using VsChromium.Package.CommandHandler;

namespace VsChromium.Features.ToolWindows.OpenFile {
  [Export(typeof(IPackageCommandHandler))]
  public class OpenFilePathsCommandHandler : PackageCommandHandlerBase {
    private readonly IToolWindowAccessor _toolWindowAccessor;

    [ImportingConstructor]
    public OpenFilePathsCommandHandler(IToolWindowAccessor toolWindowAccessor) {
      _toolWindowAccessor = toolWindowAccessor;
    }

    public override CommandID CommandId {
      get {
        return new CommandID(GuidList.GuidVsChromiumCmdSet, (int)PkgCmdIdList.CmdidOpenFile);
      }
    }

    public override void Execute(object sender, EventArgs e) {
      _toolWindowAccessor.OpenFile.FocusSearchCodeBox();
    }
  }
}