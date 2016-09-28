//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using Microsoft.PowerShell.EditorServices.Console;
using Microsoft.PowerShell.EditorServices.Extensions;
using Microsoft.PowerShell.EditorServices.Session;
using Microsoft.PowerShell.EditorServices.Utility;
using System.IO;
using System.Management.Automation.Runspaces;
using System.Threading.Tasks;

namespace Microsoft.PowerShell.EditorServices
{
    /// <summary>
    /// Manages a single session for all editor services.  This 
    /// includes managing all open script files for the session.
    /// </summary>
    public class EditorSession
    {
        #region Properties

        /// <summary>
        /// Gets the Workspace instance for this session.
        /// </summary>
        public Workspace Workspace { get; private set; }

        /// <summary>
        /// Gets the PowerShellContext instance for this session.
        /// </summary>
        public PowerShellContext PowerShellContext { get; private set; }

        /// <summary>
        /// Gets the LanguageService instance for this session.
        /// </summary>
        public LanguageService LanguageService { get; private set; }

        /// <summary>
        /// Gets the AnalysisService instance for this session.
        /// </summary>
        public AnalysisService AnalysisService { get; private set; }

        /// <summary>
        /// Gets the DebugService instance for this session.
        /// </summary>
        public DebugService DebugService { get; private set; }

        /// <summary>
        /// Gets the ConsoleService instance for this session.
        /// </summary>
        public ConsoleService ConsoleService { get; private set; }

        /// <summary>
        /// Gets the ExtensionService instance for this session.
        /// </summary>
        public ExtensionService ExtensionService { get; private set; }

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts the session using the provided IConsoleHost implementation
        /// for the ConsoleService.
        /// </summary>
        public async Task StartSession(Runspace runspace)
        {
            this.PowerShellContext = new PowerShellContext(runspace);
            await this.PowerShellContext.Initialize();

            this.InitializeServices(false);
        }

        /// <summary>
        /// Starts a debug-only session using the provided IConsoleHost implementation
        /// for the ConsoleService.
        /// </summary>
        public async Task StartDebugSession(Runspace runspace)
        {
            this.PowerShellContext = new PowerShellContext(runspace);
            await this.PowerShellContext.Initialize();

            this.InitializeServices(false);
        }

        private void InitializeServices(bool debugOnly)
        {
            // Initialize all auxiliary services
            if (!debugOnly)
            {
                this.LanguageService = new LanguageService(this.PowerShellContext);
                this.ExtensionService = new ExtensionService(this.PowerShellContext);

                this.InstantiateAnalysisService();
            }

            this.DebugService = new DebugService(this.PowerShellContext);
            this.ConsoleService = new ConsoleService(this.PowerShellContext);

            // Create a workspace to contain open files
            this.Workspace = new Workspace(this.PowerShellContext.PowerShellVersion);
        }

        internal void InstantiateAnalysisService(string settingsPath = null)
        {
            // Only enable the AnalysisService if the machine has PowerShell
            // v5 installed.  Script Analyzer works on earlier PowerShell
            // versions but our hard dependency on their binaries complicates
            // the deployment and assembly loading since we would have to
            // conditionally load the binaries for v3/v4 support.  This problem
            // will be solved in the future by using Script Analyzer as a
            // module rather than an assembly dependency.
            if (this.PowerShellContext.PowerShellVersion.Major >= 5)
            {
                // AnalysisService will throw FileNotFoundException if
                // Script Analyzer binaries are not included.
                try
                {
                    this.AnalysisService = new AnalysisService(null, settingsPath);
                }
                catch (FileNotFoundException)
                {
                    Logger.Write(
                        LogLevel.Warning,
                        "Script Analyzer binaries not found, AnalysisService will be disabled.");
                }
            }
            else
            {
                Logger.Write(
                    LogLevel.Normal,
                    "Script Analyzer cannot be loaded due to unsupported PowerShell version " +
                    this.PowerShellContext.PowerShellVersion.ToString());
            }
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes of any Runspaces that were created for the
        /// services used in this session.
        /// </summary>
        public void Dispose()
        {
            if (this.AnalysisService != null)
            {
                this.AnalysisService.Dispose();
                this.AnalysisService = null;
            }

            if (this.PowerShellContext != null)
            {
                this.PowerShellContext.Dispose();
                this.PowerShellContext = null;
            }
        }

        #endregion
    }
}
