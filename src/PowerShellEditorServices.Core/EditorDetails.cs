using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.PowerShell.EditorServices
{
    public class EditorDetails
    {
        public string Name { get; private set; }

        public string Version { get; private set; }

        public string ExtensionVersion { get; private set; }

        public int ProcessId { get; private set; }
    }
}
