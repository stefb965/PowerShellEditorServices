//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using Microsoft.PowerShell.EditorServices.Console;
using Microsoft.PowerShell.EditorServices.Session;
using Microsoft.PowerShell.EditorServices.Utility;
using System;
using System.Management.Automation.Host;
using System.Management.Automation.Runspaces;

namespace Microsoft.PowerShell.EditorServices
{
    /// <summary>
    /// Provides an implementation of the PSHost class for the
    /// ConsoleService and routes its calls to an IConsoleHost
    /// implementation.
    /// </summary>
    public class ConsoleServicePSHost : PSHost, IHostSupportsInteractiveSession
    {
        #region Private Fields

        private HostDetails hostDetails;
        private bool isNativeApplicationRunning;
        private Guid instanceId = Guid.NewGuid();
        private PSHostUserInterface hostUserInterface;
        private IHostSupportsInteractiveSession hostSupportsInteractiveSession;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new instance of the ConsoleServicePSHost class
        /// </summary>
        public ConsoleServicePSHost() : this(null, null)
        {
        }

        /// <summary>
        /// Creates a new instance of the ConsoleServicePSHost class
        /// with the given IConsoleHost implementation.
        /// </summary>
        /// <param name="hostDetails">
        /// Provides details about the host application.
        /// </param>
        public ConsoleServicePSHost(IConsoleHost consoleHost, HostDetails hostDetails)
        {
            this.hostDetails = hostDetails ?? HostDetails.Default;
            this.hostUserInterface = new ConsoleServicePSHostUserInterface(consoleHost);

            System.Console.CancelKeyPress +=
               (obj, args) =>
               {
                   if (!this.isNativeApplicationRunning)
                   {
                        // We'll handle Ctrl+C
                        if (consoleHost != null)
                        {
                            args.Cancel = true;
                            consoleHost.SendControlC();
                        }
                   }
               };
        }

        #endregion

        #region Public Methods

        /// </param>
        /// <param name="hostSupportsInteractiveSession">
        /// An implementation of IHostSupportsInteractiveSession for runspace management.
        /// </param>
        public void Initialize(IHostSupportsInteractiveSession hostSupportsInteractiveSession)
        {
            this.hostSupportsInteractiveSession = hostSupportsInteractiveSession;
        }

        #endregion

        #region PSHost Implementation

        public override Guid InstanceId
        {
            get { return this.instanceId; }
        }

        public override string Name
        {
            get { return this.hostDetails.Name; }
        }

        public override Version Version
        {
            get { return this.hostDetails.Version; }
        }

        public override System.Globalization.CultureInfo CurrentCulture
        {
            get { return System.Globalization.CultureInfo.CurrentCulture; }
        }

        public override System.Globalization.CultureInfo CurrentUICulture
        {
            get { return System.Globalization.CultureInfo.CurrentUICulture; }
        }

        public override PSHostUserInterface UI
        {
            get { return this.hostUserInterface; }
        }

        public override void EnterNestedPrompt()
        {
            Logger.Write(LogLevel.Verbose, "EnterNestedPrompt() called.");
        }

        public override void ExitNestedPrompt()
        {
            Logger.Write(LogLevel.Verbose, "ExitNestedPrompt() called.");
        }

        public override void NotifyBeginApplication()
        {
            Logger.Write(LogLevel.Verbose, "NotifyBeginApplication() called.");
            this.isNativeApplicationRunning = true;
        }

        public override void NotifyEndApplication()
        {
            Logger.Write(LogLevel.Verbose, "NotifyEndApplication() called.");
            this.isNativeApplicationRunning = false;
        }

        public override void SetShouldExit(int exitCode)
        {
            if (this.IsRunspacePushed)
            {
                this.PopRunspace();
            }
        }

        #endregion

        #region IHostSupportsInteractiveSession Implementation

        public bool IsRunspacePushed
        {
            get
            {
                if (this.hostSupportsInteractiveSession != null)
                {
                    return this.hostSupportsInteractiveSession.IsRunspacePushed;
                }
                else
                {
                    throw new NotImplementedException();
                }
            }
        }

        public Runspace Runspace
        {
            get
            {
                if (this.hostSupportsInteractiveSession != null)
                {
                    return this.hostSupportsInteractiveSession.Runspace;
                }
                else
                {
                    throw new NotImplementedException();
                }
            }
        }

        public void PushRunspace(Runspace runspace)
        {
            if (this.hostSupportsInteractiveSession != null)
            {
                this.hostSupportsInteractiveSession.PushRunspace(runspace);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        public void PopRunspace()
        {
            if (this.hostSupportsInteractiveSession != null)
            {
                this.hostSupportsInteractiveSession.PopRunspace();
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        #endregion
    }
}
