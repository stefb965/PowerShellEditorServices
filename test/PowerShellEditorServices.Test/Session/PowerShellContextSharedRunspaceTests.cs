using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.PowerShell.EditorServices.Test.Session
{
    public class PowerShellContextSharedRunspaceTests
    {
        private Runspace sharedRunspace;
        private List<PowerShellContext> powerShellContexts;
        private System.Management.Automation.PowerShell powerShell;

        public PowerShellContextSharedRunspaceTests()
        {
            this.powerShellContexts = new List<PowerShellContext>();

            this.sharedRunspace = RunspaceFactory.CreateRunspace();
            this.sharedRunspace.Open();

            this.powerShell = System.Management.Automation.PowerShell.Create();
            this.powerShell.Runspace = this.sharedRunspace;
        }

        public void Dispose()
        {
            foreach (var powerShellContext in this.powerShellContexts)
            {
                powerShellContext.Dispose();
            }

            this.powerShellContexts = null;
            this.powerShell.Dispose();
            this.sharedRunspace.Dispose();
        }

        private async Task<IEnumerable<PowerShellContext>> CreatePowerShellContexts(int numContexts)
        {
            foreach (var index in Enumerable.Range(0, numContexts))
            {
                PowerShellContext context =
                    new PowerShellContext(
                        this.sharedRunspace);

                await context.Initialize();

                this.powerShellContexts.Add(context);
            }


            return this.powerShellContexts;
        }

        private async Task<PowerShellContext> CreatePowerShellContext()
        {
            await this.CreatePowerShellContexts(1);
            return this.powerShellContexts[0];
        }

        [Fact]
        public async Task QueuesRequestForBusyRunspace()
        {
            PowerShellContext context = await this.CreatePowerShellContext();

            this.powerShell.Commands.AddScript("Start-Sleep -Seconds 1");
            this.powerShell.BeginInvoke();

            Task<IEnumerable<object>> executeTask =
               context.ExecuteScriptString("42", false, false);

            Task completedTask =
                await Task.WhenAny(
                    executeTask,
                    Task.Delay(5000));

            Assert.True(completedTask == executeTask, "Execution timed out!");
            Assert.Single(executeTask.Result, 42);
        }

        [Fact]
        public async Task InitializesAsyncOnBusyRunspace()
        {
            this.powerShell.Commands.AddScript("Start-Sleep -Seconds 2");
            var r = this.powerShell.BeginInvoke();

            // Create the PowerShellContext while the runspace is busy
            PowerShellContext context = await this.CreatePowerShellContext();

            // Try to run a task to see if it gets queued to run after initialization
            Task<IEnumerable<object>> executeTask =
                context.ExecuteScriptString("42", false, false);

            Task completedTask =
                await Task.WhenAny(
                    executeTask,
                    Task.Delay(100000));

            Assert.True(completedTask == executeTask, "Execution timed out!");
            Assert.Single(executeTask.Result, 42);
        }

        [Fact]
        public async Task MultipleContextsWorkWithSingleRunspace()
        {
            this.powerShell.Commands.AddScript("Start-Sleep -Seconds 2");
            var r = this.powerShell.BeginInvoke();

            // Create the PowerShellContext while the runspace is busy
            PowerShellContext context = await this.CreatePowerShellContext();

            // Try to run a task to see if it gets queued to run after initialization
            Task<IEnumerable<object>> executeTask =
                context.ExecuteScriptString("$profile", false, false);

            Task completedTask =
                await Task.WhenAny(
                    executeTask,
                    Task.Delay(100000));

            Assert.True(completedTask == executeTask, "Execution timed out!");
            Assert.Single(executeTask.Result, PowerShellContextTests.TestProfilePaths.CurrentUserCurrentHost);
        }
    }
}
