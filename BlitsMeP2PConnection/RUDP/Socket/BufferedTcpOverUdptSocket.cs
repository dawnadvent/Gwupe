﻿using System;
using BlitsMe.Communication.P2P.RUDP.Socket.API;
using BlitsMe.Communication.P2P.RUDP.Tunnel.API;
using BlitsMe.Communication.P2P.RUDP.Utils;
using log4net;

namespace BlitsMe.Communication.P2P.RUDP.Socket
{
    class BufferedTcpOverUdptSocket : IInternalTcpOverUdptSocket
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof (BufferedTcpOverUdptSocket));
        private ITcpTransportLayer _connection;
        private CircularBuffer<byte> clientBuffer;

        public bool Closing { get; private set; }

        public BufferedTcpOverUdptSocket(ITcpTransportLayer connection)
        {
            _connection = connection;
            clientBuffer = new CircularBuffer<byte>(32768);
        }

        public ITcpTransportLayer Connection
        {
            get { return _connection; }
        }

        public void Send(byte[] data, int timeout)
        {
            Connection.SendData(data, timeout);
        }

        public int Read(byte[] data, int maxRead)
        {
            if (!Closing)
            {
                int returnValue = clientBuffer.Get(data, maxRead);
                Logger.Debug("Read " + returnValue + " bytes from transport buffer");
                return returnValue;
            } else
            {
                throw new ObjectDisposedException("Socket has been closed");
            }
        }

        public bool Closed
        {
            get { return Closing; }
        }

        public void Close()
        {
            if(!Closing)
            {
                Closing = true;
                clientBuffer.Release();
                _connection.Close();
            }
        }

        public int BufferClientData(byte[] data)
        {
            try
            {
                clientBuffer.Add(data, 10000);
                Logger.Debug("Received " + data.Length + " bytes, added to client buffer, now " + clientBuffer.Count + " in size.");
                return clientBuffer.Available;
            }
            catch (Exception e)
            {
                Logger.Error("Failed to buffer data from tcp layer : " + e.Message, e);
                Close();
            }
            return 0;
        }
    }
}