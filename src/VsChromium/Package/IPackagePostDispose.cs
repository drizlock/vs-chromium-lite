// Copyright 2015 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

namespace VsChromium.Package {
  public interface IPackagePostDispose {
    int Priority { get; }
    void Run(IVisualStudioPackage package);
  }
}