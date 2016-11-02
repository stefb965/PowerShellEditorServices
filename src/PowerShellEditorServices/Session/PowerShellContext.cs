//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using Microsoft.PowerShell.EditorServices.Utility;
using System;
using System.Collections;
using System.Globalization;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PowerShell.EditorServices
{
    using Session;
    using System.Management.Automation;
    using System.Management.Automation.Host;
    using System.Management.Automation.Runspaces;
    using System.Reflection;

    /// <summary>
    /// TODO: This should be rewritten!
    /// Manages the lifetime and usage of a PowerShell session.
    /// Handles nested PowerShell prompts and also manages execution of 
    /// commands whether inside or outside of the debugger.
    /// </summary>
    public class PowerShellContext : IDisposable
    {
        #region Fields

        private bool isRunspaceDebuggable;
        private Runspace initialRunspace;
        private Runspace currentRunspace;
        private IVersionSpecificOperations versionSpecificOperations;
        private int pipelineThreadId;

        private bool isDebuggerStopped;
        private TaskCompletionSource<IPipelineExecutionRequest> pipelineExecutionTask;
        private TaskCompletionSource<IPipelineExecutionRequest> pipelineResultTask;

        private Task executionQueueTask;
        private CancellationTokenSource executionQueueCancellationToken;
        private PSEventSubscriber onIdleSubscriber;

        // TODO: Alias this type?
        private AsyncQueue<Tuple<IExecutionRequest, TaskCompletionSource<bool>>> executionRequestQueue = new AsyncQueue<Tuple<IExecutionRequest, TaskCompletionSource<bool>>>();

        #endregion

        #region Properties

        /// <summary>
        /// Gets a boolean that indicates whether the debugger is currently stopped,
        /// either at a breakpoint or because the user broke execution.
        /// </summary>
        public bool IsDebuggerStopped
        {
            get
            {
                return this.isDebuggerStopped; //this.debuggerStoppedTask != null;
            }
        }

        /// <summary>
        /// Gets the current state of the session.
        /// </summary>
        public PowerShellContextState SessionState
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the PowerShell version of the current runspace.
        /// </summary>
        public Version PowerShellVersion
        {
            get; private set;
        }

        /// <summary>
        /// Gets the PowerShell edition of the current runspace.
        /// </summary>
        public string PowerShellEdition
        {
            get; private set;
        }

        /// <summary>
        /// Gets the PSHost exposed within the current runspace.
        /// </summary>
        public PSHost RunspacePSHost { get; private set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the PowerShellContext class using
        /// an existing runspace for the session.
        /// </summary>
        /// <param name="initialRunspace">The initial runspace to use for this instance.</param>
        public PowerShellContext(Runspace initialRunspace)
        {
            Validate.IsNotNull("initialRunspace", initialRunspace);

            // Watch for Runspace availability changes
            this.initialRunspace = initialRunspace;
            this.currentRunspace = initialRunspace;
        }

        public async Task Initialize()
        {
            this.SessionState = PowerShellContextState.NotStarted;

            this.currentRunspace = initialRunspace;
            this.currentRunspace.AvailabilityChanged += this.currentRunspace_AvailabilityChanged;

            // TODO: Wrap this?
            this.onIdleSubscriber =
                this.currentRunspace.Events.SubscribeEvent(
                    null,
                    "PowerShell.OnIdle",
                    "PowerShell.OnIdle",
                    null,
                    (obj, args) =>
                    {
                        this.HandleOnIdleEvent(obj, args).Wait();
                    },
                    true,
                    false);

            Logger.Write(LogLevel.Verbose, $"PowerShell.OnIdle handler has been subscribed.");

            if (this.currentRunspace.Debugger != null)
            {
                this.ConfigureDebugger();
                this.isRunspaceDebuggable = true;

                Logger.Write(
                    LogLevel.Verbose,
                    "Runspace debugging is configured, events have been subscribed.");
            }
            else
            {
                Logger.Write(
                    LogLevel.Warning,
                    "Runspace is not configured for debugging, breakpoints will not work in this session.");
            }

            // Start a thread that processes the execution request queue
            //this.executionQueueCancellationToken = new CancellationTokenSource();
            //this.executionQueueTask =
            //    Task.Factory.StartNew(
            //        () => 
            //        {
            //            try
            //            {
            //                this.ProcessExecutionRequestQueue(this.executionQueueCancellationToken.Token).GetAwaiter().GetResult();
            //            }
            //            catch (TaskCanceledException)
            //            {
            //                Logger.Write(
            //                    LogLevel.Verbose,
            //                    "PowerShellContext execution request queue thread is exiting");
            //            }
            //        },
            //        CancellationToken.None,
            //        TaskCreationOptions.LongRunning,
            //        TaskScheduler.Default);

            await this.ConfigureRunspace();

            this.SessionState = PowerShellContextState.Ready;
        }

        private void ConfigureDebugger()
        {
            this.currentRunspace.Debugger.BreakpointUpdated += OnBreakpointUpdated;

            // Find the DebuggerStop event using reflection so that we can
            // get access to its current list of registered handlers
            //Type debuggerType = this.currentRunspace.Debugger.GetType();
            //EventInfo eventInfo = debuggerType.GetEvent("DebuggerStop");
            //FieldInfo eventField =
            //    eventInfo.DeclaringType.GetField(
            //        "DebuggerStop",
            //        BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            //EventHandler<DebuggerStopEventArgs> eventObject =
            //    (EventHandler<DebuggerStopEventArgs>)eventField.GetValue(
            //        this.currentRunspace.Debugger);

            //Delegate[] originalHandlerList = new Delegate[0];
            //if (eventObject != null)
            //{
            //    // Temporarily remove the current handlers
            //    eventObject.GetInvocationList();
            //    foreach (var handler in originalHandlerList)
            //    {
            //        eventInfo.RemoveEventHandler(this.currentRunspace.Debugger, handler);
            //    }
            //}

            //// Add our OnDebuggerStop method as a handler
            //eventInfo.AddEventHandler(
            //    this.currentRunspace.Debugger,
            //    (EventHandler<DebuggerStopEventArgs>)this.OnDebuggerStop);

            //// Add back the existing handlers
            //foreach (var handler in originalHandlerList)
            //{
            //    eventInfo.AddEventHandler(this.currentRunspace.Debugger, handler);
            //}

            //// Add a final handler so we know when the debugger has resumed execution
            //this.currentRunspace.Debugger.DebuggerStop +=
            //    (obj, args) =>
            //    {
            //        // At this point the debugger must have resumed
            //        this.isDebuggerStopped = false;
            //    };
        }

        private async Task ProcessExecutionRequestQueue(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Tuple<IExecutionRequest, TaskCompletionSource<bool>> executionDetails = await this.executionRequestQueue.DequeueAsync(cancellationToken);

                // Try to execute the task until it succeeds or is cancelled
                while (!executionDetails.Item2.Task.IsCanceled)
                {
                    // TODO: Should there be an ExecutionResult or something?
                    try
                    {
                        await executionDetails.Item1.Execute(this.currentRunspace);
                        executionDetails.Item2.TrySetResult(true);

                        // Listen for the next task
                        break;
                    }
                    catch (PSInvalidOperationException e)
                    {
                        // TODO: This NanoServer check is likely not needed when compiling against latest CoreCLR.
#if !NanoServer
                        // TODO: Does this work across PowerShell versions?
                        if (e.TargetSite.Name.StartsWith("DoConcurrentCheck"))
                        {
#endif
                            Logger.Write(LogLevel.Verbose, "Runspace was busy when executing, trying again soon...");

                            await Task.Delay(250);
#if !NanoServer
                        }
                        else
                        {
                            Logger.Write(LogLevel.Verbose, $"Different kind of message... {e.TargetSite.Name}\r\n\r\n{e.Message}");
                        }
#endif
                    }
                }
            }
        }

        private async Task FlushExecutionRequestQueue(CancellationToken cancellationToken)
        {
            // Try to execute the task until it succeeds or is cancelled
            while (!cancellationToken.IsCancellationRequested && !this.executionRequestQueue.IsEmpty)
            {
                Tuple<IExecutionRequest, TaskCompletionSource<bool>> executionDetails = await this.executionRequestQueue.DequeueAsync(cancellationToken);

                // TODO: Should there be an ExecutionResult or something?
                try
                {
                    await executionDetails.Item1.Execute(this.currentRunspace);
                    executionDetails.Item2.TrySetResult(true);

                    // Listen for the next task
                    break;
                }
                catch (PSInvalidOperationException e)
                {
                    // TODO: This NanoServer check is likely not needed when compiling against latest CoreCLR.
#if !NanoServer
                    // TODO: Does this work across PowerShell versions?
                    if (e.TargetSite.Name.StartsWith("DoConcurrentCheck"))
                    {
#endif
                        Logger.Write(LogLevel.Verbose, "Runspace was busy when executing, trying again soon...");

                        await Task.Delay(250);
#if !NanoServer
                    }
                    else
                    {
                        Logger.Write(LogLevel.Verbose, $"Different kind of message... {e.TargetSite.Name}\r\n\r\n{e.Message}");
                    }
#endif
                }
            }
        }

        private async Task QueueExecutionRequest(IExecutionRequest executionRequest)
        {
            TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>();

            await this.executionRequestQueue.EnqueueAsync(
                Tuple.Create(executionRequest, taskCompletionSource));

            // Wait for the execution task to complete
            await taskCompletionSource.Task;
        }

        private async Task ConfigureRunspace()
        {
            // Get the PowerShell runtime version
            Tuple<Version, string> versionEditionTuple = await GetPowerShellVersion();
            this.PowerShellVersion = versionEditionTuple.Item1;
            this.PowerShellEdition = versionEditionTuple.Item2;

            // Write out the PowerShell version for tracking purposes
            Logger.Write(
                LogLevel.Normal,
                string.Format(
                    "PowerShell runtime version: {0}, edition: {1}",
                    this.PowerShellVersion,
                    this.PowerShellEdition));

            if (PowerShellVersion >= new Version(5,0))
            {
                this.versionSpecificOperations = new PowerShell5Operations();
            }
            else if (PowerShellVersion.Major == 4)
            {
                this.versionSpecificOperations = new PowerShell4Operations();
            }
            else if (PowerShellVersion.Major == 3)
            {
                this.versionSpecificOperations = new PowerShell3Operations();
            }
            else
            {
                throw new NotSupportedException(
                    "This computer has an unsupported version of PowerShell installed: " +
                    PowerShellVersion.ToString());
            }

            // Get the PSHost for the runspace
            this.RunspacePSHost = await GetHostVariable();

            if (this.PowerShellEdition != "Linux")
            {
                // TODO: Should this be configurable?
                await this.SetExecutionPolicy(ExecutionPolicy.RemoteSigned);
            }
        }

        private async Task HandleOnIdleEvent(object source, PSEventArgs args)
        {
            //if (this.currentRunspace.Debugger.InBreakpoint)
            {
                Logger.Write(LogLevel.Verbose, "# ON IDLE ---------------------------------------------------");
            }

            await this.FlushExecutionRequestQueue(CancellationToken.None);
        }

        private async Task<PSHost> GetHostVariable()
        {
            PSHost host = null;

            try
            {
                host =
                    await this.ExecuteWithRunspace(
                        runspace =>
                        {
                            return runspace.SessionStateProxy.GetVariable("host") as PSHost;
                        });
            }
            catch (Exception ex)
            {
                Logger.Write(
                    LogLevel.Warning,
                    $"Failed access $host variable.\r\n\r\n" +
                    $"Exception '{ex.GetType().Name}': {ex.Message}\r\n");
            }

            return host;
        }

        private async Task<Tuple<Version, string>> GetPowerShellVersion()
        {
            Version powerShellVersion = new Version(5, 0);
            string powerShellEdition = "Desktop";

            try
            {
                PSObject result =
                    await this.ExecuteWithRunspace(
                        runspace =>
                        {
                            return runspace.SessionStateProxy.GetVariable("PSVersionTable") as Hashtable;
                        });
                
                var psVersionTable = result.BaseObject as Hashtable;
                if (psVersionTable != null)
                {
                    var version = psVersionTable["PSVersion"] as Version;
                    if (version != null)
                    {
                        powerShellVersion = version;
                    }

                    var edition = psVersionTable["PSEdition"] as string;
                    if (edition != null)
                    {
                        powerShellEdition = edition;
                    }
                }
            }
            catch (Exception ex)
            {

                Logger.Write(
                    LogLevel.Warning,
                    $"Failed to look up PowerShell version. Defaulting to version 5.\r\n\r\n" +
                    $"Exception '{ex.GetType().Name}': {ex.Message}\r\n");
            }

            return new Tuple<Version, string>(powerShellVersion, powerShellEdition);
        }

        #endregion

        #region Public Methods

        private void currentRunspace_AvailabilityChanged(object sender, RunspaceAvailabilityEventArgs e)
        {
            // Log availability changes to help debug runspace synchronization problems
            Logger.Write(
                LogLevel.Verbose,
                $"RunspaceAvailability changed: {this.currentRunspace.RunspaceAvailability}");
        }

        public Task<TResult> ExecuteWithRunspace<TResult>(Func<Runspace, TResult> runspaceFunc)
        {
            return this.ExecuteWithRunspace(
                runspaceFunc,
                CancellationToken.None);
        }

        public async Task<TResult> ExecuteWithRunspace<TResult>(
            Func<Runspace, TResult> runspaceFunc,
            CancellationToken cancellationToken)
        {
            var executionRequest = new RunspaceExecutionRequest<TResult>(runspaceFunc);
            await this.QueueExecutionRequest(executionRequest);

            return executionRequest.Result;
        }

        public Task ExecuteWithRunspace(Action<Runspace> runspaceAction)
        {
            return this.ExecuteWithRunspace(runspaceAction, CancellationToken.None);
        }

        public async Task ExecuteWithRunspace(
            Action<Runspace> runspaceAction,
            CancellationToken cancellationToken)
        {
            var executionRequest = new RunspaceExecutionRequest(runspaceAction);
            await this.QueueExecutionRequest(executionRequest);
        }

        /// <summary>
        /// Executes a PSCommand against the session's runspace and returns
        /// a collection of results of the expected type.
        /// </summary>
        /// <typeparam name="TResult">The expected result type.</typeparam>
        /// <param name="psCommand">The PSCommand to be executed.</param>
        /// <param name="sendOutputToHost">
        /// If true, causes any output written during command execution to be written to the host.
        /// </param>
        /// <param name="sendErrorToHost">
        /// If true, causes any errors encountered during command execution to be written to the host.
        /// </param>
        /// <returns>
        /// An awaitable Task which will provide results once the command
        /// execution completes.
        /// </returns>
        public Task<IEnumerable<TResult>> ExecuteCommand<TResult>(
            PSCommand psCommand,
            bool sendOutputToHost = false,
            bool sendErrorToHost = true)
        {
            return ExecuteCommand<TResult>(psCommand, null, sendOutputToHost, sendErrorToHost);
        }

        /// <summary>
        /// Executes a PSCommand against the session's runspace and returns
        /// a collection of results of the expected type.
        /// </summary>
        /// <typeparam name="TResult">The expected result type.</typeparam>
        /// <param name="psCommand">The PSCommand to be executed.</param>
        /// <param name="errorMessages">Error messages from PowerShell will be written to the StringBuilder. 
        /// You must set sendErrorToHost to false for errors to be written to the StringBuilder. This value can be null.</param>
        /// <param name="sendOutputToHost">
        /// If true, causes any output written during command execution to be written to the host.
        /// </param>
        /// <param name="sendErrorToHost">
        /// If true, causes any errors encountered during command execution to be written to the host.
        /// </param>
        /// <returns>
        /// An awaitable Task which will provide results once the command
        /// execution completes.
        /// </returns>
        public async Task<IEnumerable<TResult>> ExecuteCommand<TResult>(
            PSCommand psCommand,
            StringBuilder errorMessages,
            bool sendOutputToHost = false,
            bool sendErrorToHost = true)
        {
            IEnumerable<TResult> executionResult = Enumerable.Empty<TResult>();

            // If the PowerShellContext is not yet initialized, throw an exception
            if (this.SessionState == PowerShellContextState.Unknown)
            {
                throw new InvalidOperationException("The PowerShellContext has not yet been initialized");
            }

            try
            {
                // Instruct PowerShell to send output and errors to the host
                if (sendOutputToHost)
                {
                    psCommand.Commands[0].MergeMyResults(
                        PipelineResultTypes.Error,
                        PipelineResultTypes.Output);

                    psCommand.Commands.Add(
                        this.GetOutputCommand(
                            endOfStatement: false));
                }

                if (this.currentRunspace.RunspaceAvailability == RunspaceAvailability.AvailableForNestedCommand)
                {
                    Logger.Write(
                        LogLevel.Verbose,
                        string.Format(
                            "Attempting to execute nested pipeline command(s):\r\n\r\n{0}",
                            GetStringForPSCommand(psCommand)));

                    if (this.isDebuggerStopped)
                    {
                        executionResult =
                            this.ExecuteCommandInDebugger<TResult>(
                                psCommand,
                                sendOutputToHost);
                    }
                    else
                    {
                        // TODO: Execute in the pipeline
                        Logger.Write(
                            LogLevel.Error,
                            "Requested execute while AvailableForNestedCommand, did nothing!");
                    }
                }
                else
                {
                    CommandExecutionRequest<TResult> executionRequest = new CommandExecutionRequest<TResult>(psCommand);
                    await this.QueueExecutionRequest(executionRequest);
                    executionResult = executionRequest.Result;
                }
            }
            catch (RuntimeException e)
            {
                Logger.Write(
                    LogLevel.Error,
                    "Runtime exception occurred while executing command:\r\n\r\n" + e.ToString());

                errorMessages?.Append(e.Message);

                if (sendErrorToHost)
                {
                    // Write the error to the host
                    this.WriteExceptionToHost(e);
                }
            }

            return executionResult;
        }

        /// <summary>
        /// Executes a PSCommand in the session's runspace without
        /// expecting to receive any result.
        /// </summary>
        /// <param name="psCommand">The PSCommand to be executed.</param>
        /// <returns>
        /// An awaitable Task that the caller can use to know when
        /// execution completes.
        /// </returns>
        public Task ExecuteCommand(PSCommand psCommand)
        {
            return this.ExecuteCommand<object>(psCommand);
        }

        /// <summary>
        /// Executes a script string in the session's runspace.
        /// </summary>
        /// <param name="scriptString">The script string to execute.</param>
        /// <returns>A Task that can be awaited for the script completion.</returns>
        public Task<IEnumerable<object>> ExecuteScriptString(
            string scriptString)
        {
            return this.ExecuteScriptString(scriptString, false, true);
        }

        /// <summary>
        /// Executes a script string in the session's runspace.
        /// </summary>
        /// <param name="scriptString">The script string to execute.</param>
        /// <param name="writeInputToHost">If true, causes the script string to be written to the host.</param>
        /// <param name="writeOutputToHost">If true, causes the script output to be written to the host.</param>
        /// <returns>A Task that can be awaited for the script completion.</returns>
        public async Task<IEnumerable<object>> ExecuteScriptString(
            string scriptString,
            bool writeInputToHost,
            bool writeOutputToHost)
        {
            if (writeInputToHost)
            {
                this.WriteOutput(
                    scriptString + Environment.NewLine,
                    true);
            }

            PSCommand psCommand = new PSCommand();
            psCommand.AddScript(scriptString);

            return await this.ExecuteCommand<object>(psCommand, writeOutputToHost);
        }

        /// <summary>
        /// Executes a script file at the specified path.
        /// </summary>
        /// <param name="scriptPath">The path to the script file to execute.</param>
        /// <param name="arguments">Arguments to pass to the script.</param>
        /// <returns>A Task that can be awaited for completion.</returns>
        public Task ExecuteScriptAtPath(string scriptPath, string arguments = null)
        {
            PSCommand command = new PSCommand();

            if (arguments != null)
            {
                // If we don't escape wildcard characters in the script path, the script can
                // fail to execute if say the script name was foo][.ps1.
                // Related to issue #123.
                string escapedScriptPath = EscapePath(scriptPath, escapeSpaces: true);
                string scriptWithArgs = escapedScriptPath + " " + arguments;

                command.AddScript(scriptWithArgs);
            }
            else
            {
                command.AddCommand(scriptPath);
            }

            return this.ExecuteCommand<object>(command, true);
        }

        /// <summary>
        /// Causes the current execution to be aborted no matter what state
        /// it is currently in.
        /// </summary>
        public void AbortExecution()
        {
            if (this.SessionState != PowerShellContextState.Aborting &&
                this.SessionState != PowerShellContextState.Disposed)
            {
                Logger.Write(LogLevel.Verbose, "Execution abort requested...");

                //this.powerShell.BeginStop(null, null);
                this.SessionState = PowerShellContextState.Aborting;

                if (this.IsDebuggerStopped)
                {
                    this.ResumeDebugger(DebuggerResumeAction.Stop);
                }
            }
            else
            {
                Logger.Write(
                    LogLevel.Verbose,
                    string.Format(
                        $"Execution abort requested when already aborted (SessionState = {this.SessionState})"));
            }
        }

        /// <summary>
        /// Causes the debugger to break execution wherever it currently is.
        /// This method is internal because the real Break API is provided
        /// by the DebugService.
        /// </summary>
        internal void BreakExecution()
        {
            Logger.Write(LogLevel.Verbose, "Debugger break requested...");

            // Pause the debugger
            this.versionSpecificOperations.PauseDebugger(
                this.currentRunspace);
        }

        internal void ResumeDebugger(DebuggerResumeAction resumeAction)
        {
            // TODO: What do we do here?  Send command somehow?

            //if (this.debuggerStoppedTask != null)
            //{
            //    // Set the result so that the execution thread resumes.
            //    // The execution thread will clean up the task.
            //    this.debuggerStoppedTask.SetResult(resumeAction);
            //}
            //else
            //{
            //    // TODO: Throw InvalidOperationException?
            //}
        }

        /// <summary>
        /// Disposes the runspace and any other resources being used
        /// by this PowerShellContext.
        /// </summary>
        public void Dispose()
        {
            // TODO: I'm skeptical of the necessity of this.  Should the host take care of
            // terminating the running command?  Maybe we should terminate our own running
            // command if there is one...

            // Do we need to abort a running execution?
            if (this.SessionState == PowerShellContextState.Running ||
                this.IsDebuggerStopped)
            {
                this.AbortExecution();
            }

            this.SessionState = PowerShellContextState.Disposed;

            // Kill the thread that's processing execution requests
            this.executionQueueCancellationToken.Cancel();
        }

        /// <summary>
        /// Sets the current working directory of the powershell context.  The path should be
        /// unescaped before calling this method.
        /// </summary>
        /// <param name="path"></param>
        public void SetWorkingDirectory(string path)
        {
            this.currentRunspace.SessionStateProxy.Path.SetLocation(path);
        }

        /// <summary>
        /// Returns the passed in path with the [ and ] characters escaped. Escaping spaces is optional.
        /// </summary>
        /// <param name="path">The path to process.</param>
        /// <param name="escapeSpaces">Specify True to escape spaces in the path, otherwise False.</param>
        /// <returns>The path with [ and ] escaped.</returns>
        public static string EscapePath(string path, bool escapeSpaces)
        {
            string escapedPath = Regex.Replace(path, @"(?<!`)\[", "`[");
            escapedPath = Regex.Replace(escapedPath, @"(?<!`)\]", "`]");

            if (escapeSpaces)
            {
                escapedPath = Regex.Replace(escapedPath, @"(?<!`) ", "` ");
            }

            return escapedPath;
        }

        /// <summary>
        /// Unescapes any escaped [, ] or space characters. Typically use this before calling a
        /// .NET API that doesn't understand PowerShell escaped chars.
        /// </summary>
        /// <param name="path">The path to unescape.</param>
        /// <returns>The path with the ` character before [, ] and spaces removed.</returns>
        public static string UnescapePath(string path)
        {
            if (!path.Contains("`"))
            {
                return path;
            }

            return Regex.Replace(path, @"`(?=[ \[\]])", "");
        }

        #endregion

        #region Events

        // NOTE: This event is 'internal' because the DebugService provides
        //       the publicly consumable event.
        internal event EventHandler<DebuggerStopEventArgs> DebuggerStop;

        private async void OnDebuggerStop(object sender, DebuggerStopEventArgs e)
        {
            Logger.Write(LogLevel.Verbose, "Debugger stopped execution.");

            this.isDebuggerStopped = true;

            //// Set the task so a result can be set
            //this.debuggerStoppedTask =
            //     new TaskCompletionSource<DebuggerResumeAction>();

            //// Save the pipeline thread ID and create the pipeline execution task
            //this.pipelineThreadId = Thread.CurrentThread.ManagedThreadId;
            //this.pipelineExecutionTask = new TaskCompletionSource<IPipelineExecutionRequest>();

            // Raise the event for the debugger service
            this.DebuggerStop?.Invoke(sender, e);

            // NOTE: At this point we expect the host to handle the breakpoint
            // actions subsequent prompt loop

            //Logger.Write(LogLevel.Verbose, "Starting pipeline thread message loop...");

            //while (true)
            //{
            //    int taskIndex =
            //        Task.WaitAny(
            //            this.debuggerStoppedTask.Task,
            //            this.pipelineExecutionTask.Task);

            //    if (taskIndex == 0)
            //    {
            //        // Write a new output line before continuing
            //        // TODO: Re-enable this with fix for #133
            //        //this.WriteOutput("", true);

            //        e.ResumeAction = this.debuggerStoppedTask.Task.Result;
            //        Logger.Write(LogLevel.Verbose, "Received debugger resume action " + e.ResumeAction.ToString());

            //        break;
            //    }
            //    else if (taskIndex == 1)
            //    {
            //        Logger.Write(LogLevel.Verbose, "Received pipeline thread execution request.");

            //        IPipelineExecutionRequest executionRequest =
            //            this.pipelineExecutionTask.Task.Result;

            //        this.pipelineExecutionTask = new TaskCompletionSource<IPipelineExecutionRequest>();

            //        executionRequest.Execute().Wait();

            //        Logger.Write(LogLevel.Verbose, "Pipeline thread execution completed.");

            //        this.pipelineResultTask.SetResult(executionRequest);
            //    }
            //    else
            //    {
            //        // TODO: How to handle this?
            //    }
            //}

            //// Clear the task so that it won't be used again
            //this.debuggerStoppedTask = null;
        }

        // NOTE: This event is 'internal' because the DebugService provides
        //       the publicly consumable event.
        internal event EventHandler<BreakpointUpdatedEventArgs> BreakpointUpdated;

        private void OnBreakpointUpdated(object sender, BreakpointUpdatedEventArgs e)
        {
            if (this.BreakpointUpdated != null)
            {
                this.BreakpointUpdated(sender, e);
            }
        }

        #endregion

        #region Private Methods

        private IEnumerable<TResult> ExecuteCommandInDebugger<TResult>(PSCommand psCommand, bool sendOutputToHost)
        {
            return this.versionSpecificOperations.ExecuteCommandInDebugger<TResult>(
                this,
                this.currentRunspace,
                psCommand,
                sendOutputToHost);
        }

        internal void WriteOutput(string outputString, bool includeNewLine)
        {
            this.WriteOutput(
                outputString,
                includeNewLine,
                OutputType.Normal);
        }

        internal void WriteOutput(
            string outputString,
            bool includeNewLine,
            OutputType outputType)
        {
            //if (this.ConsoleHost != null)
            //{
            //    this.ConsoleHost.WriteOutput(
            //        outputString,
            //        includeNewLine,
            //        outputType);
            //}
        }

        private void WriteExceptionToHost(Exception e)
        {
            const string ExceptionFormat =
                "{0}\r\n{1}\r\n    + CategoryInfo          : {2}\r\n    + FullyQualifiedErrorId : {3}";

            IContainsErrorRecord containsErrorRecord = e as IContainsErrorRecord;

            if (containsErrorRecord == null || 
                containsErrorRecord.ErrorRecord == null)
            {
                this.WriteError(e.Message, null, 0, 0);
                return;
            }

            ErrorRecord errorRecord = containsErrorRecord.ErrorRecord;
            if (errorRecord.InvocationInfo == null)
            {
                this.WriteError(errorRecord.ToString(), String.Empty, 0, 0);
                return;
            }

            string errorRecordString = errorRecord.ToString();
            if ((errorRecord.InvocationInfo.PositionMessage != null) &&
                errorRecordString.IndexOf(errorRecord.InvocationInfo.PositionMessage, StringComparison.Ordinal) != -1)
            {
                this.WriteError(errorRecordString);
                return;
            }

            string message = 
                string.Format(
                    CultureInfo.InvariantCulture,
                    ExceptionFormat,
                    errorRecord.ToString(),
                    errorRecord.InvocationInfo.PositionMessage,
                    errorRecord.CategoryInfo,
                    errorRecord.FullyQualifiedErrorId);

            this.WriteError(message);
        }

        private void WriteError(
            string errorMessage,
            string filePath,
            int lineNumber,
            int columnNumber)
        {
            const string ErrorLocationFormat = "At {0}:{1} char:{2}";

            this.WriteError(
                errorMessage +
                Environment.NewLine +
                string.Format(
                    ErrorLocationFormat,
                    String.IsNullOrEmpty(filePath) ? "line" : filePath,
                    lineNumber,
                    columnNumber));
        }

        private void WriteError(string errorMessage)
        {
            //if (this.ConsoleHost != null)
            //{
            //    this.ConsoleHost.WriteOutput(
            //        errorMessage,
            //        true,
            //        OutputType.Error,
            //        ConsoleColor.Red,
            //        ConsoleColor.Black);
            //}
        }

        private Command GetOutputCommand(bool endOfStatement)
        {
            Command outputCommand =
                new Command(
                    command: this.IsDebuggerStopped ? "Out-String" : "Out-Default",
                    isScript: false,
                    useLocalScope: true);

            if (this.IsDebuggerStopped)
            {
                // Out-String needs the -Stream parameter added
                outputCommand.Parameters.Add("Stream");
            }

            return outputCommand;
        }

        private static string GetStringForPSCommand(PSCommand psCommand)
        {
            StringBuilder stringBuilder = new StringBuilder();

            foreach (var command in psCommand.Commands)
            {
                stringBuilder.Append("    ");
                stringBuilder.AppendLine(command.ToString());
            }

            return stringBuilder.ToString();
        }
        
        private async Task SetExecutionPolicy(ExecutionPolicy desiredExecutionPolicy)
        {
            var currentPolicy = ExecutionPolicy.Undefined;

            // Get the current execution policy so that we don't set it higher than it already is 
            PSCommand psCommand = new PSCommand();
            psCommand.AddCommand("Get-ExecutionPolicy");
            var result = await this.ExecuteCommand<ExecutionPolicy>(psCommand);
            currentPolicy = result.FirstOrDefault();

            if (desiredExecutionPolicy < currentPolicy ||
                desiredExecutionPolicy == ExecutionPolicy.Bypass ||
                currentPolicy == ExecutionPolicy.Undefined)
            {
                Logger.Write(
                    LogLevel.Verbose,
                    string.Format(
                        "Setting execution policy:\r\n    Current = ExecutionPolicy.{0}\r\n    Desired = ExecutionPolicy.{1}",
                        currentPolicy,
                        desiredExecutionPolicy));

                psCommand.Clear();
                psCommand
                    .AddCommand("Set-ExecutionPolicy")
                    .AddParameter("ExecutionPolicy", desiredExecutionPolicy)
                    .AddParameter("Scope", ExecutionPolicyScope.Process)
                    .AddParameter("Force");

                await this.ExecuteCommand(psCommand);

                // TODO: Ensure there were no errors?
            }
            else
            {
                Logger.Write(
                    LogLevel.Verbose,
                    string.Format(
                        "Current execution policy: ExecutionPolicy.{0}",
                        currentPolicy));

            }
        }

        private void WritePromptToHost(Func<PSCommand, string> invokeAction)
        {
            string promptString = null;

            try
            {
                promptString = 
                    invokeAction(
                        new PSCommand().AddCommand("prompt"));
            }
            catch(RuntimeException e)
            {
                Logger.Write(
                    LogLevel.Verbose,
                    "Runtime exception occurred while executing prompt command:\r\n\r\n" + e.ToString());
            }
            finally
            {
                promptString = promptString ?? "PS >";
            }

            this.WriteOutput(
                Environment.NewLine,
                false);

            // Trim the '>' off the end of the prompt string to reduce
            // user confusion about where they can type.
            // TODO: Eventually put this behind a setting, #133
            promptString = promptString.TrimEnd(' ', '>', '\r', '\n');

            // Write the prompt string
            this.WriteOutput(
                promptString,
                true);
        }

        private void WritePromptWithRunspace(Runspace runspace)
        {
            //this.WritePromptToHost(
            //    command =>
            //    throw new NotImplementedException();
            //    {
            //        this.powerShell.Commands = command;

            //        return
            //            this.powerShell
            //                .Invoke<string>()
            //                .FirstOrDefault();
            //    });
        }

        private void WritePromptWithNestedPipeline()
        {
            using (var pipeline = this.currentRunspace.CreateNestedPipeline())
            {
                this.WritePromptToHost(
                    command =>
                    {
                        pipeline.Commands.Clear();
                        pipeline.Commands.Add(command.Commands[0]);

                        return
                            pipeline
                                .Invoke()
                                .Select(pso => pso.BaseObject)
                                .Cast<string>()
                                .FirstOrDefault();
                    });
            }
        }

        
        #endregion

        #region Nested Classes

        private interface IExecutionRequest
        {
            Task Execute(Runspace runspace);
        }

        private class RunspaceExecutionRequest : IExecutionRequest
        {
            private Action<Runspace> runspaceAction;

            public RunspaceExecutionRequest(Action<Runspace> runspaceAction)
            {
                Validate.IsNotNull(nameof(runspaceAction), runspaceAction);

                this.runspaceAction = runspaceAction;
            }

            public Task Execute(Runspace runspace)
            {
                this.runspaceAction(runspace);
                return Task.FromResult(true);
            }
        }

        private class RunspaceExecutionRequest<TResult> : IExecutionRequest
        {
            private Func<Runspace, TResult> runspaceFunc;

            public TResult Result { get; private set; }

            public RunspaceExecutionRequest(Func<Runspace, TResult> runspaceFunc)
            {
                Validate.IsNotNull(nameof(runspaceFunc), runspaceFunc);

                this.runspaceFunc = runspaceFunc;
            }

            public Task Execute(Runspace runspace)
            {
                this.Result = this.runspaceFunc(runspace);
                return Task.FromResult(true);
            }
        }

        private class CommandExecutionRequest<TResult> : IExecutionRequest
        {
            private PSCommand psCommand;

            public Collection<TResult> Result { get; private set; }

            public CommandExecutionRequest(PSCommand psCommand)
            {
                Validate.IsNotNull(nameof(psCommand), psCommand);

                this.psCommand = psCommand;
            }

            public async Task Execute(Runspace runspace)
            {
                Logger.Write(
                    LogLevel.Verbose,
                    string.Format(
                        "Attempting to execute command(s):\r\n\r\n{0}",
                        GetStringForPSCommand(psCommand)));

                using (PowerShell powerShell = PowerShell.Create())
                {
                    powerShell.Runspace = runspace;
                    powerShell.Commands = this.psCommand;
                    this.Result =
                        await Task.Factory.StartNew(
                            () =>
                            {
                                powerShell.Commands = psCommand;
                                Collection<TResult> result = powerShell.Invoke<TResult>();
                                return result;
                            },
                            CancellationToken.None, // Might need a cancellation token
                            TaskCreationOptions.AttachedToParent,
                            TaskScheduler.Default
                        );

                    if (powerShell.HadErrors)
                    {
                        string errorMessage = "Execution completed with errors:\r\n\r\n";

                        foreach (var error in powerShell.Streams.Error)
                        {
                            errorMessage += error.ToString() + "\r\n";
                        }

                        Logger.Write(LogLevel.Error, errorMessage);
                    }
                    else
                    {
                        Logger.Write(
                            LogLevel.Verbose,
                            "Execution completed successfully.");
                    }
                }
            }
        }

        private interface IPipelineExecutionRequest
        {
            Task Execute();
        }

        /// <summary>
        /// Contains details relating to a request to execute a
        /// command on the PowerShell pipeline thread.
        /// </summary>
        /// <typeparam name="TResult">The expected result type of the execution.</typeparam>
        private class PipelineExecutionRequest<TResult> : IPipelineExecutionRequest
        {
            PowerShellContext powerShellContext;
            PSCommand psCommand;
            StringBuilder errorMessages;
            bool sendOutputToHost;

            public IEnumerable<TResult> Results { get; private set; }

            public PipelineExecutionRequest(
                PowerShellContext powerShellContext,
                PSCommand psCommand,
                StringBuilder errorMessages,
                bool sendOutputToHost)
            {
                this.powerShellContext = powerShellContext;
                this.psCommand = psCommand;
                this.errorMessages = errorMessages;
                this.sendOutputToHost = sendOutputToHost;
            }

            public async Task Execute()
            {
                this.Results =
                    await this.powerShellContext.ExecuteCommand<TResult>(
                        psCommand,
                        errorMessages,
                        sendOutputToHost);

                // TODO: Deal with errors?
            }
        }

        #endregion
    }
}

