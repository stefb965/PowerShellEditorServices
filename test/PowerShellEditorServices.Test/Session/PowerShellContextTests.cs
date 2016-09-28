//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using Microsoft.PowerShell.EditorServices.Session;
using Microsoft.PowerShell.EditorServices.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.PowerShell.EditorServices.Test.Session
{
    public class PowerShellContextTests : IAsyncLifetime
    {
        private Runspace runspace;
        private PowerShellContext powerShellContext;

        private const string DebugTestFilePath =
            @"..\..\..\PowerShellEditorServices.Test.Shared\Debugging\DebugTest.ps1";

        public static readonly HostDetails TestHostDetails =
            new HostDetails(
                "PowerShell Editor Services Test Host",
                "Test.PowerShellEditorServices",
                new Version("1.0.0"));

        // NOTE: These paths are arbitrarily chosen just to verify that the profile paths
        //       can be set to whatever they need to be for the given host.

        public static readonly ProfilePaths TestProfilePaths =
            new ProfilePaths(
                TestHostDetails.ProfileId, 
                    Path.GetFullPath(
                        @"..\..\..\PowerShellEditorServices.Test.Shared\Profile"),
                    Path.GetFullPath(
                        @"..\..\..\PowerShellEditorServices.Test.Shared"));

        public async Task InitializeAsync()
        {
            this.runspace = 
                RunspaceFactory.CreateRunspace(
                    InitialSessionState.CreateDefault2());

            this.runspace.Open();

            this.powerShellContext = new PowerShellContext(this.runspace);
            await this.powerShellContext.Initialize();
        }

        public Task DisposeAsync()
        {
            this.powerShellContext.Dispose();
            this.powerShellContext = null;
            this.runspace.Dispose();

            return Task.FromResult(true);
        }

        [Fact]
        public async Task CanExecuteWithRunspace()
        {
            int result =
                await this.powerShellContext.ExecuteWithRunspace(
                    runspace =>
                    {
                        using (var powerShell = System.Management.Automation.PowerShell.Create())
                        {
                            powerShell.Runspace = runspace;
                            powerShell.Commands.AddScript("42");
                            return powerShell.Invoke<int>().FirstOrDefault();
                        }
                    });

            Assert.Equal(42, result);
        }

        [Fact]
        public async Task ExecutesAfterRunspaceIsAvailable()
        {
            var powerShell = System.Management.Automation.PowerShell.Create();
            powerShell.Runspace = this.runspace;
            powerShell.Commands.AddScript("Start-Sleep -Seconds 2");
            IAsyncResult invokeAsync = powerShell.BeginInvoke();

            int result =
                await this.powerShellContext.ExecuteWithRunspace(
                    runspace =>
                    {
                        using (var innerPowerShell = System.Management.Automation.PowerShell.Create())
                        {
                            innerPowerShell.Runspace = runspace;
                            innerPowerShell.Commands.AddScript("42");
                            return innerPowerShell.Invoke<int>().FirstOrDefault();
                        }
                    });

            powerShell.EndInvoke(invokeAsync);
            powerShell.Dispose();

            Assert.Equal(42, result);
        }

        [Fact]
        public async Task CanExecutePSCommand()
        {
            PSCommand psCommand = new PSCommand();
            psCommand.AddScript("$a = \"foo\"; $a");

            var executeTask =
                this.powerShellContext.ExecuteCommand<string>(psCommand);

            var result = await executeTask;
            Assert.Equal("foo", result.First());
        }

        [Fact]
        public async Task CanQueueParallelRunspaceRequests()
        {
            // Concurrently initiate 4 requests in the session
            PSCommand psCommand = new PSCommand();
            psCommand.AddScript("$x");

            IEnumerable<object>[] resultCollections =
                await Task.WhenAll(
                    this.powerShellContext.ExecuteScriptString("$x = 100"),
                    this.powerShellContext.ExecuteScriptString("$x += 200"),
                    this.powerShellContext.ExecuteScriptString("$x = $x / 100"),
                    this.powerShellContext.ExecuteCommand<object>(psCommand));

            // 100 + 200 = 300, then divided by 100 is 3.  We are ensuring that
            // the commands were executed in the sequence they were called.
            Assert.Equal(3, (int)resultCollections[3].First());
        }

        [Fact]
        public async Task CanQueueParallelRunspaceRequestsInBusyRunspace()
        {
            // Concurrently initiate 4 requests in the session
            PSCommand psCommand = new PSCommand();
            psCommand.AddScript("$x");

            Task<IEnumerable<object>[]> executionTask =
                Task.WhenAll(
                    Task.Run(async () => { await Task.Delay(150); return Enumerable.Empty<object>(); }),
                    this.powerShellContext.ExecuteScriptString("$x = 100"),
                    this.powerShellContext.ExecuteScriptString("$x += 200"),
                    this.powerShellContext.ExecuteScriptString("$x = $x / 100"),
                    this.powerShellContext.ExecuteCommand<object>(psCommand));

            // Introduce sleeps of varying lengths in the runspace to emulate
            // while the PowerShellContext is trying to process execution requests
            int[] delayTimes = new int[] { 350, 100, 550 };
            for (int i = 0; i < delayTimes.Length; i++)
            {
                try
                {
                    using (var powerShell = System.Management.Automation.PowerShell.Create())
                    {
                        powerShell.Runspace = this.runspace;
                        powerShell.Commands.AddScript($"Start-Sleep -Milliseconds {delayTimes[i]}");
                        powerShell.Invoke();

                        // Delay a bit before moving forward to give the other thread some time to execute
                        await Task.Delay(75);
                    }
                }
                catch (PSInvalidOperationException)
                {
                    // Runspace was busy, try again
                    i--;
                }
            }

            // 100 + 200 = 300, then divided by 100 is 3.  We are ensuring that
            // the commands were executed in the sequence they were called.
            IEnumerable<object>[] resultCollections = await executionTask;
            Assert.Equal(3, (int)resultCollections[4].First());
        }

        [Fact]
        public async Task CanAbortExecution()
        {
            var executeTask =
                Task.Run(
                    async () =>
                    {
                        var unusedTask = this.powerShellContext.ExecuteScriptAtPath(DebugTestFilePath);
                        await Task.Delay(250);
                        this.powerShellContext.AbortExecution();
                    });

            // TODO: How to verify that we aborted execution?
            Assert.True(false, "Need a way to know if execution was aborted!");

            await executeTask;
        }

        // TODO: This belongs elsewhere now
        //[Fact]
        //public async Task CanResolveAndLoadProfilesForHostId()
        //{
        //    string[] expectedProfilePaths =
        //        new string[]
        //        {
        //            TestProfilePaths.AllUsersAllHosts,
        //            TestProfilePaths.AllUsersCurrentHost,
        //            TestProfilePaths.CurrentUserAllHosts,
        //            TestProfilePaths.CurrentUserCurrentHost
        //        };

        //    // Load the profiles for the test host name
        //    // TODO: What should I do with this test?  Move to hosting tests?
        //    //await this.powerShellContext.LoadHostProfiles();

        //    // Ensure that all the paths are set in the correct variables
        //    // and that the current user's host profile got loaded
        //    PSCommand psCommand = new PSCommand();
        //    psCommand.AddScript(
        //        "\"$($profile.AllUsersAllHosts) " +
        //        "$($profile.AllUsersCurrentHost) " +
        //        "$($profile.CurrentUserAllHosts) " +
        //        "$($profile.CurrentUserCurrentHost) " +
        //        "$(Assert-ProfileLoaded)\"");

        //    var result =
        //        await this.powerShellContext.ExecuteCommand<string>(
        //            psCommand);

        //    string expectedString =
        //        string.Format(
        //            "{0} True",
        //            string.Join(
        //                " ",
        //                expectedProfilePaths));

        //    Assert.Equal(expectedString, result.FirstOrDefault(), true);
        //}
    }
}

