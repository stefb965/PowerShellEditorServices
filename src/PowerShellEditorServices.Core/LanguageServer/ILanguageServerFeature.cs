using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.PowerShell.EditorServices.LanguageServer
{
    public interface ILanguageServerFeature
    {
        void RegisterRequestHandler();

        void RegisterEventHandler();

        void SendRequest();

        void SendEvent();
    }
}
