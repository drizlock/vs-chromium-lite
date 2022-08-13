using System.Collections.Generic;
using System.Linq;
using VsChromium.Core.Ipc.TypedMessages;
using VsChromium.Core.Files;
using System.Windows.Input;

namespace VsChromium.Features.ToolWindows.OpenFile {
  public class FileEntryViewModel {
    private IOpenFileController _controller;
    private DirectoryEntry _dirEntry;
    private FileEntry _fileEntry;
    private string _path;
    private string _filename;

    public FileEntryViewModel(IOpenFileController controller, DirectoryEntry dirEntry, FileEntry fileEntry) {
      _dirEntry = dirEntry;
      _fileEntry = fileEntry;
      _controller = controller;

      _path = PathHelpers.CombinePaths(dirEntry.Name, fileEntry.Name);
      _filename = PathHelpers.GetFileName(_path);
    }

    static public IEnumerable<FileEntryViewModel> Create(IOpenFileController controller, FileSystemEntry systemEntry) {
      FileEntry fileEntry = systemEntry as FileEntry;
      if (fileEntry != null) {
        return new[] { new FileEntryViewModel(controller, null, fileEntry) };
      } else {
        DirectoryEntry dirEntry = systemEntry as DirectoryEntry;
        if (dirEntry != null) {
          return dirEntry
            .Entries
            .Select(entry => new FileEntryViewModel(controller, dirEntry, (FileEntry)entry))
            .ToList();
        }
      }

      return new List<FileEntryViewModel>();
    }

    static public IEnumerable<FileEntryViewModel> CreateMock(IOpenFileController controller) {
      DirectoryEntry mockDirectory1 = new DirectoryEntry();
      mockDirectory1.Name = @"c:\mypath1\";
      DirectoryEntry mockDirectory2 = new DirectoryEntry();
      mockDirectory2.Name = @"c:\mypath2\";
      FileEntry mockFile1 = new FileEntry();
      mockFile1.Name = @"foo1.cpp";
      FileEntry mockFile2 = new FileEntry();
      mockFile2.Name = @"foo1.h";
      FileEntry mockFile3 = new FileEntry();
      mockFile3.Name = @"bar.cpp";

      mockDirectory1.Entries.Add(mockFile1);
      mockDirectory1.Entries.Add(mockFile2);
      mockDirectory2.Entries.Add(mockFile3);

      return Create(controller, mockDirectory1).Concat(Create(controller, mockDirectory2));
    }

    public string Filename {
      get { return _filename; }
    }

    public string Path {
      get { return _path; }
    }

    public string GetFullPath() {
      return PathHelpers.CombinePaths(_path, _filename);
    }

    public ICommand OpenCommand {
      get {
        return CommandDelegate.Create(sender => _controller.OpenFileInEditor(this));
      }
    }
  }
}
