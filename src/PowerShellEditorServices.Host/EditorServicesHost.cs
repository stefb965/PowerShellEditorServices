//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using Microsoft.PowerShell.EditorServices.Protocol.MessageProtocol.Channel;
using Microsoft.PowerShell.EditorServices.Protocol.Server;
using Microsoft.PowerShell.EditorServices.Session;
using Microsoft.PowerShell.EditorServices.Utility;
using System;
using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PowerShell.EditorServices.Host
{
    public enum EditorServicesHostStatus
    {
        Started,
        Failed,
        Ended
    }

    public class EditorServicesHostConfiguration
    {
        public HostDetails HostDetails { get; private set; }

        public ProfilePaths ProfilePaths { get; set; }

        public string LogFilePath { get; set; }

        public LogLevel LogLevel { get; set; }

        public int LanguageServicePort { get; set; }

        public int DebugServicePort { get; set; }

        public EditorServicesHostConfiguration(HostDetails hostDetails)
        {
            Validate.IsNotNull(nameof(hostDetails), hostDetails);

            this.HostDetails = hostDetails;
        }
    }

    /// <summary>
    /// Provides a simplified interface for hosting the language and debug services
    /// over the named pipe server protocol.
    /// </summary>
    public class EditorServicesHost
    {
        #region Private Fields

        private Runspace sessionRunspace;
        private DebugAdapter debugAdapter;
        private LanguageServer languageServer;
        private EditorSession editorSession;

        #endregion

        #region Properties

        public EditorServicesHostStatus Status { get; private set; }

        public EditorServicesHostConfiguration Configuration { get; private set; }

        public int LanguageServicePort { get; private set; }

        public int DebugServicePort { get; private set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the EditorServicesHost class and waits for
        /// the debugger to attach if waitForDebugger is true.
        /// </summary>
        public EditorServicesHost(
            Runspace runspace,
            EditorServicesHostConfiguration hostConfiguration)
        {
            Validate.IsNotNull(nameof(runspace), runspace);
            Validate.IsNotNull(nameof(hostConfiguration), hostConfiguration);

            this.sessionRunspace = runspace;
            this.editorSession = new EditorSession();
            this.Configuration = hostConfiguration;

            // Catch unhandled exceptions for logging purposes
#if !NanoServer
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
#endif
        }

        #endregion

        #region Public Methods

        public Task Start()
        {
            return this.Start(false);
        }

        /// <param name="waitForDebugger">If true, causes the host to wait for the debugger to attach before proceeding.</param>
        public async Task Start(bool waitForDebugger)
        {
#if DEBUG
            int waitsRemaining = 10;
            if (waitForDebugger)
            {
                while (waitsRemaining > 0 && !System.Diagnostics.Debugger.IsAttached)
                {
                    Thread.Sleep(1000);
                    waitsRemaining--;
                }
            }
#endif

            this.StartLogging(this.Configuration.LogFilePath, this.Configuration.LogLevel);
            await this.StartLanguageService(this.Configuration.LanguageServicePort);
            this.StartDebugService(this.Configuration.DebugServicePort);
        }

        #endregion

        #region Events

        public event EventHandler Initialized;

        protected void OnInitialized()
        {
            Logger.Write(LogLevel.Verbose, $"Firing 'Initialized' event  (has listeners: {this.Initialized != null})");
            this.Initialized?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Starts the Logger for the specified file path and log level.
        /// </summary>
        private void StartLogging(string logFilePath, LogLevel logLevel)
        {
            Logger.Initialize(logFilePath, logLevel);

#if NanoServer
            FileVersionInfo fileVersionInfo =
                FileVersionInfo.GetVersionInfo(this.GetType().GetTypeInfo().Assembly.Location);

            // TODO #278: Need the correct dependency package for this to work correctly
            //string osVersionString = RuntimeInformation.OSDescription;
            //string processArchitecture = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "64-bit" : "32-bit";
            //string osArchitecture = RuntimeInformation.OSArchitecture == Architecture.X64 ? "64-bit" : "32-bit";
#else
            FileVersionInfo fileVersionInfo =
                FileVersionInfo.GetVersionInfo(this.GetType().Assembly.Location);
            string osVersionString = Environment.OSVersion.VersionString;
            string processArchitecture = Environment.Is64BitProcess ? "64-bit" : "32-bit";
            string osArchitecture = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
#endif

            string newLine = Environment.NewLine;

            Logger.Write(
                LogLevel.Normal,
                string.Format(
                    $"PowerShell Editor Services Host v{fileVersionInfo.FileVersion} starting (pid {Process.GetCurrentProcess().Id})..." + newLine + newLine +
                     "  Host application details:" + newLine + newLine +
                    $"    Name:      {this.Configuration.HostDetails.Name}" + newLine +
                    $"    ProfileId: {this.Configuration.HostDetails.ProfileId}" + newLine +
                    $"    Version:   {this.Configuration.HostDetails.Version}" + newLine +
#if !NanoServer
                    $"    Arch:      {processArchitecture}" + newLine + newLine +
                     "  Operating system details:" + newLine + newLine +
                    $"    Version: {osVersionString}" + newLine +
                    $"    Arch:    {osArchitecture}"));
#else
                    ""));
#endif
        }

        /// <param name="profilePaths">The object containing the profile paths to load for this session.</param>
        //private Task LoadProfiles(ProfilePaths profilePaths)
        //{
        //    // Set the $profile variable in the runspace
        //    if (this.Configuration.ProfilePaths != null)
        //    {
        //        this.SetProfileVariableInCurrentRunspace(profilePaths);
        //    }
        //}

        //private void SetProfileVariableInCurrentRunspace(ProfilePaths profilePaths)
        //{
        //    // Create the $profile variable
        //    PSObject profile = new PSObject(profilePaths.CurrentUserCurrentHost);

        //    profile.Members.Add(
        //        new PSNoteProperty(
        //            nameof(profilePaths.AllUsersAllHosts),
        //            profilePaths.AllUsersAllHosts));

        //    profile.Members.Add(
        //        new PSNoteProperty(
        //            nameof(profilePaths.AllUsersCurrentHost),
        //            profilePaths.AllUsersCurrentHost));

        //    profile.Members.Add(
        //        new PSNoteProperty(
        //            nameof(profilePaths.CurrentUserAllHosts),
        //            profilePaths.CurrentUserAllHosts));

        //    profile.Members.Add(
        //        new PSNoteProperty(
        //            nameof(profilePaths.CurrentUserCurrentHost),
        //            profilePaths.CurrentUserCurrentHost));

        //    Logger.Write(
        //        LogLevel.Verbose,
        //        string.Format(
        //            "Setting $profile variable in runspace.  Current user host profile path: {0}",
        //            profilePaths.CurrentUserCurrentHost));

        //    // Set the variable in the runspace
        //    this.powerShell.Commands.Clear();
        //    this.powerShell
        //        .AddCommand("Set-Variable")
        //        .AddParameter("Name", "profile")
        //        .AddParameter("Value", profile)
        //        .AddParameter("Option", "None");
        //    this.powerShell.Invoke();
        //    this.powerShell.Commands.Clear();
        //}

        ///// <summary>
        ///// Loads PowerShell profiles for the host from the specified
        ///// profile locations.  Only the profile paths which exist are
        ///// loaded.
        ///// </summary>
        ///// <returns>A Task that can be awaited for completion.</returns>
        //public async Task LoadHostProfiles()
        //{
        //    if (this.profilePaths != null)
        //    {
        //        // Load any of the profile paths that exist
        //        PSCommand command = null;
        //        foreach (var profilePath in this.profilePaths.GetLoadableProfilePaths())
        //        {
        //            command = new PSCommand();
        //            command.AddCommand(profilePath, false);
        //            await this.ExecuteCommand(command);
        //        }
        //    }
        //}

        /// <summary>
        /// Starts the language service with the specified TCP socket port.
        /// </summary>
        /// <param name="languageServicePort">The port number for the language service.</param>
        private async Task StartLanguageService(int languageServicePort)
        {
            await this.editorSession.StartSession(this.sessionRunspace);

            // TODO: Establish profile variable!

            this.languageServer =
                new LanguageServer(
                    this.editorSession,
                    new TcpSocketServerChannel(languageServicePort));

            await this.languageServer.Start();

            Logger.Write(
                LogLevel.Normal,
                string.Format(
                    "Language service started, listening on port {0}",
                    languageServicePort));

            this.OnInitialized();
        }

        /// <summary>
        /// Starts the debug service with the specified TCP socket port.
        /// </summary>
        /// <param name="debugServicePort">The port number for the debug service.</param>
        private void StartDebugService(int debugServicePort)
        {
            this.debugAdapter =
                new DebugAdapter(
                    this.editorSession,
                    new TcpSocketServerChannel(debugServicePort));

            this.debugAdapter.SessionEnded +=
                (obj, args) =>
                {
                    Logger.Write(
                        LogLevel.Normal,
                        "Previous debug session ended, restarting debug service...");

                    this.StartDebugService(debugServicePort);
                };

            this.debugAdapter.Start().Wait();

            Logger.Write(
                LogLevel.Normal,
                string.Format(
                    "Debug service started, listening on port {0}",
                    debugServicePort));
        }

        /// <summary>
        /// Stops the language or debug services if either were started.
        /// </summary>
        public void StopServices()
        {
            this.languageServer?.Stop().Wait();
            this.languageServer = null;

            this.debugAdapter?.Stop().Wait();
            this.debugAdapter = null;
        }

        /// <summary>
        /// Waits for either the language or debug service to shut down.
        /// </summary>
        public void WaitForCompletion()
        {
            // Wait based on which server is started.  If the language server
            // hasn't been started then we may only need to wait on the debug
            // adapter to complete.
            if (this.languageServer != null)
            {
                this.languageServer.WaitForExit();
            }
            else if (this.debugAdapter != null)
            {
                this.debugAdapter.WaitForExit();
            }
        }

#if !NanoServer
        static void CurrentDomain_UnhandledException(
            object sender,
            UnhandledExceptionEventArgs e)
        {
            // Log the exception
            Logger.Write(
                LogLevel.Error,
                string.Format(
                    "FATAL UNHANDLED EXCEPTION:\r\n\r\n{0}",
                    e.ExceptionObject.ToString()));
        }
#endif

        #endregion
    }
}