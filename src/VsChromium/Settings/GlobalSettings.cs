// Copyright 2015 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System.ComponentModel;
using System.Drawing;
using VsChromium.Core.Utility;

namespace VsChromium.Settings {
  public class GlobalSettings : INotifyPropertyChanged {
    private int _maxTextExtractLength;
    private int _searchFilePathsMaxResults;
    private int _searchCodeMaxResults;
    private int _autoSearchDelayMsec;
    private bool _flattenSearchResults;
    private int _flatResultsMaxImmediateRequests;
    private int _flatResultsRequestsPerSecond;
    private bool _displayRelativePath;
    private bool _searchMatchCase;
    private bool _searchMatchWholeWord;
    private bool _searchUseRegEx;
    private bool _searchSpaceAsWildcard;
    private bool _searchIncludeSymLinks;
    private bool _searchUnderstandBuildOutputPaths;
    private bool _searchHorizontalLayout;
    private bool _codingStyleAccessorIndent;
    private bool _codingStyleTrailingSpace;
    private bool _codingStyleTabCharacter;
    private bool _codingStyleSpaceAfterForKeyword;
    private bool _codingStyleOpenBraceAfterNewLine;
    private bool _codingStyleLongLine;
    private bool _codingStyleEndOfLineCharacter;
    private bool _codingStyleElseIfOnNewLine;
    private Font _displayFont;
    private Font _textExtractFont;
    private Font _pathFont;

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName) {
      var handler = PropertyChanged;
      if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
    }

    private static int InRange(int value, int min, int max) {
      if (value < min)
        return min;
      if (value > max)
        return max;
      return value;
    }

    public int MaxTextExtractLength {
      get { return _maxTextExtractLength; }
      set {
        value = InRange(value, 10, 1024);
        if (value == _maxTextExtractLength)
          return;

        _maxTextExtractLength = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.MaxTextExtractLength));

      }
    }

    public int SearchFilePathsMaxResults {
      get { return _searchFilePathsMaxResults; }
      set {
        value = InRange(value, 100, 1000 * 1000);
        if (value == _searchFilePathsMaxResults)
          return;

        _searchFilePathsMaxResults = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.SearchFilePathsMaxResults));

      }
    }

    public int SearchCodeMaxResults {
      get { return _searchCodeMaxResults; }
      set {
        value = InRange(value, 1000, 1000 * 1000);
        if (value == _searchCodeMaxResults)
          return;

        _searchCodeMaxResults = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.SearchCodeMaxResults));

      }
    }

    public int AutoSearchDelayMsec {
      get { return _autoSearchDelayMsec; }
      set {
        value = InRange(value, 0, int.MaxValue);
        if (value == _autoSearchDelayMsec)
          return;

        _autoSearchDelayMsec = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.AutoSearchDelayMsec));
      }
    }

    public Font DisplayFont {
      get { return _displayFont; }
      set {
        if (object.Equals(value, _displayFont))
          return;

        _displayFont = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.DisplayFont));
      }
    }

    public Font TextExtractFont {
      get { return _textExtractFont; }
      set {
        if (object.Equals(value, _textExtractFont))
          return;

        _textExtractFont = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.TextExtractFont));
      }
    }

    public Font PathFont {
      get { return _pathFont; }
      set {
        if (object.Equals(value, _pathFont))
          return;

        _pathFont = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.PathFont));
      }
    }

    public bool FlattenSearchResults {
      get { return _flattenSearchResults; }
      set {
        if (value == _flattenSearchResults)
          return;

        _flattenSearchResults = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.FlattenSearchResults));
      }
    }


    public int FlatResultsMaxImmediateRequests {
      get { return _flatResultsMaxImmediateRequests; }
      set {
        if (value == _flatResultsMaxImmediateRequests)
          return;

        _flatResultsMaxImmediateRequests = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.FlatResultsMaxImmediateRequests));
      }
    }



    public int FlatResultsRequestsPerSecond {
      get { return _flatResultsRequestsPerSecond; }
      set {
        if (value == _flatResultsRequestsPerSecond)
          return;

        _flatResultsRequestsPerSecond = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.FlatResultsRequestsPerSecond));
      }
    }



    public bool DisplayRelativePath {
      get { return _displayRelativePath; }
      set {
        if (value == _displayRelativePath)
          return;

        _displayRelativePath = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.DisplayRelativePath));
      }
    }

    public bool SearchMatchCase {
      get { return _searchMatchCase; }
      set {
        if (value == _searchMatchCase)
          return;

        _searchMatchCase = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.SearchMatchCase));
      }
    }

    public bool SearchMatchWholeWord {
      get { return _searchMatchWholeWord; }
      set {
        if (value == _searchMatchWholeWord)
          return;

        _searchMatchWholeWord = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.SearchMatchWholeWord));
      }
    }

    public bool SearchUseRegEx {
      get { return _searchUseRegEx; }
      set {
        if (value == _searchUseRegEx)
          return;

        _searchUseRegEx = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.SearchUseRegEx));
      }
    }

    public bool SearchSpaceAsWildcard {
      get { return _searchSpaceAsWildcard; }
      set {
        if (value == _searchSpaceAsWildcard)
          return;

        _searchSpaceAsWildcard = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.SearchSpaceAsWildcard));
      }
    }

    public bool SearchIncludeSymLinks {
      get { return _searchIncludeSymLinks; }
      set {
        if (value == _searchIncludeSymLinks)
          return;

        _searchIncludeSymLinks = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.SearchIncludeSymLinks));
      }
    }

    public bool SearchUnderstandBuildOutputPaths {
      get { return _searchUnderstandBuildOutputPaths; }
      set {
        if (value == _searchUnderstandBuildOutputPaths)
          return;

        _searchUnderstandBuildOutputPaths = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.SearchUnderstandBuildOutputPaths));
      }
    }

    public bool SearchHorizontalLayout {
      get { return _searchHorizontalLayout; }
      set {
        if (value == _searchHorizontalLayout)
          return;

        _searchHorizontalLayout = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.SearchHorizontalLayout));
      }
    }

    #region Chromium Coding Style

    public bool CodingStyleAccessorIndent {
      get { return _codingStyleAccessorIndent; }
      set {
        if (value == _codingStyleAccessorIndent)
          return;

        _codingStyleAccessorIndent = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.CodingStyleAccessorIndent));
      }
    }

    public bool CodingStyleElseIfOnNewLine {
      get { return _codingStyleElseIfOnNewLine; }
      set {
        if (value == _codingStyleElseIfOnNewLine)
          return;

        _codingStyleElseIfOnNewLine = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.CodingStyleElseIfOnNewLine));
      }
    }

    public bool CodingStyleEndOfLineCharacter {
      get { return _codingStyleEndOfLineCharacter; }
      set {
        if (value == _codingStyleEndOfLineCharacter)
          return;

        _codingStyleEndOfLineCharacter = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.CodingStyleEndOfLineCharacter));
      }
    }

    public bool CodingStyleLongLine {
      get { return _codingStyleLongLine; }
      set {
        if (value == _codingStyleLongLine)
          return;

        _codingStyleLongLine = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.CodingStyleLongLine));
      }
    }

    public bool CodingStyleOpenBraceAfterNewLine {
      get { return _codingStyleOpenBraceAfterNewLine; }
      set {
        if (value == _codingStyleOpenBraceAfterNewLine)
          return;

        _codingStyleOpenBraceAfterNewLine = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.CodingStyleOpenBraceAfterNewLine));
      }
    }

    public bool CodingStyleSpaceAfterForKeyword {
      get { return _codingStyleSpaceAfterForKeyword; }
      set {
        if (value == _codingStyleSpaceAfterForKeyword)
          return;

        _codingStyleSpaceAfterForKeyword = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.CodingStyleSpaceAfterForKeyword));
      }
    }

    public bool CodingStyleTabCharacter {
      get { return _codingStyleTabCharacter; }
      set {
        if (value == _codingStyleTabCharacter)
          return;

        _codingStyleTabCharacter = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.CodingStyleTabCharacter));
      }
    }

    public bool CodingStyleTrailingSpace {
      get { return _codingStyleTrailingSpace; }
      set {
        if (value == _codingStyleTrailingSpace)
          return;

        _codingStyleTrailingSpace = value;
        OnPropertyChanged(ReflectionUtils.GetPropertyName(this, x => x.CodingStyleTrailingSpace));
      }
    }

    #endregion
  }
}