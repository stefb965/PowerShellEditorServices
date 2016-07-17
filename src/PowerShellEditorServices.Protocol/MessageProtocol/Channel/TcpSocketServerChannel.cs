//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using Microsoft.PowerShell.EditorServices.Utility;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Microsoft.PowerShell.EditorServices.Protocol.MessageProtocol.Channel
{
    public class TcpSocketServerChannel : ChannelBase
    {
        private TcpListener tcpListener;
        private NetworkStream networkStream;
        private IMessageSerializer messageSerializer;

        public TcpSocketServerChannel(int portNumber)
        {
            this.tcpListener = new TcpListener(IPAddress.Loopback, portNumber);
            this.tcpListener.ExclusiveAddressUse = true;
            this.tcpListener.Start();
        }

        public override async Task WaitForConnection()
        {
            Socket socket = await this.tcpListener.AcceptSocketAsync();
            this.networkStream = new NetworkStream(socket, true);

            this.MessageReader =
                new MessageReader(
                    this.networkStream,
                    this.messageSerializer);

            this.MessageWriter =
                new MessageWriter(
                    this.networkStream,
                    this.messageSerializer);

            this.IsConnected = true;
        }

        protected override void Initialize(IMessageSerializer messageSerializer)
        {
            this.messageSerializer = messageSerializer;
        }

        protected override void Shutdown()
        {
            if (this.networkStream != null)
            {
                this.networkStream.Dispose();
                this.networkStream = null;
            }
        }
    }
}
