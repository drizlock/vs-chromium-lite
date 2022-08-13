
namespace VsChromium.Features.ToolWindows.CodeSearch
{
  public interface IFileEntryViewModel : IFileSystemEntryViewModel {
    string Path { get; }
    void SetLineColumn(int lineNumber, int columnNumber);
  }
}
