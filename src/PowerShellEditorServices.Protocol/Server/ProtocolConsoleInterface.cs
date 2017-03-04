//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.PowerShell.EditorServices.Console;
using Microsoft.PowerShell.EditorServices.Protocol.MessageProtocol;
using System.Management.Automation.Host;
using System.Security;
using System.Threading.Tasks;

namespace Microsoft.PowerShell.EditorServices.Protocol.Server
{
    public class ProtocolConsoleInterface : IConsoleHost
    {
        private IConsoleHost terminalConsoleInterface;
        private IPromptHandlerContext promptHandlerContext;

        public IMessageSender MessageSender { get; set; }

        public ProtocolConsoleInterface()
        {
            this.promptHandlerContext =
                new ProtocolPromptHandlerContext(
                    this,
                    this.editorSession.ConsoleService);
        }

        public ChoicePromptHandler GetChoicePromptHandler()
        {
            throw new NotImplementedException();
        }

        public InputPromptHandler GetInputPromptHandler()
        {
            throw new NotImplementedException();
        }

        public PSHostRawUserInterface GetRawUI()
        {
            return new SimplePSHostRawUserInterface();
        }

        public Task<string> ReadCommandLine(PowerShellContext powerShellContext)
        {
            throw new NotImplementedException();
        }

        public Task<SecureString> ReadSecureLine(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<string> ReadSimpleLine(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public void SendControlC()
        {
            throw new NotImplementedException();
        }

        public void UpdateProgress(long sourceId, ProgressDetails progressDetails)
        {
            throw new NotImplementedException();
        }

        public void WriteOutput(
            string outputString,
            bool includeNewLine,
            OutputType outputType,
            ConsoleColor foregroundColor,
            ConsoleColor backgroundColor)
        {
            //this.OutputWritten?.Invoke(
            //    this,
            //    new OutputWrittenEventArgs(
            //        outputString,
            //        includeNewLine,
            //        outputType,
            //        foregroundColor,
            //        backgroundColor));

            // TODO: Write using OutputDebouncer
        }
    }
}
