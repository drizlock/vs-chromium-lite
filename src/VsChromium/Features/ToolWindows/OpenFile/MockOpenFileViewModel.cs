using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VsChromium.Features.ToolWindows.OpenFile
{
    public class MockOpenFileViewModel : OpenFileViewModel
    {
        public MockOpenFileViewModel()
        {
            UpdateFileList(FileEntryViewModel.CreateMock(null));
        }
    }
}
