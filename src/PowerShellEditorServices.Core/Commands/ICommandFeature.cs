using System.Management.Automation;

namespace Microsoft.PowerShell.EditorServices.Commands
{
    public interface ICommandFeature
    {
        void RegisterCommand(string name, string displayName, ScriptBlock scriptBlock);
    }
}
