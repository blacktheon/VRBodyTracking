using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;
using UnityEngine.Android;
#endif

namespace Blacktheon.BodyTracking
{
    internal sealed class SlimeVrDiscovery
    {
        private static readonly byte[] Probe = Encoding.ASCII.GetBytes("BLACKTHEON_SLIMEVR_DISCOVER_1");
        private static readonly byte[] Reply = Encoding.ASCII.GetBytes("BLACKTHEON_SLIMEVR_HERE_1");
        // Stock SlimeVR sends packet ID 0 and sequence 0 in network byte order.
        private static readonly byte[] StockHeartbeat = new byte[12];
        private readonly IPEndPoint service;
        private readonly int bridgePort;
        private readonly IPAddress heartbeatBindAddress;
        private readonly byte[] buffer = new byte[128];
        private readonly Dictionary<string, double> rejectedUntil = new Dictionary<string, double>();
        private Socket probeSocket, heartbeatSocket;
        private double nextProbe, nextRead;
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject multicastLock;
#endif

        public SlimeVrDiscovery() : this(new IPEndPoint(IPAddress.Broadcast, 6969), 21110,
#if UNITY_ANDROID && !UNITY_EDITOR
            IPAddress.Any
#else
            // A desktop or Editor instance may share the PC with the actual tracker server.
            null
#endif
        ) { }

        internal SlimeVrDiscovery(IPEndPoint service, int bridgePort, IPAddress heartbeatBindAddress)
        {
            this.service = service;
            this.bridgePort = bridgePort;
            this.heartbeatBindAddress = heartbeatBindAddress;
        }

        public Uri Find(double now)
        {
            if (now >= nextProbe)
            {
                nextProbe = now + 1.5;
                if (heartbeatSocket == null && heartbeatBindAddress != null)
                {
                    heartbeatSocket = Bind(heartbeatBindAddress, service.Port);
#if UNITY_ANDROID && !UNITY_EDITOR
                    if (heartbeatSocket != null) AcquireMulticastLock();
#endif
                }
                if (probeSocket == null)
                    probeSocket = Bind(IPAddress.Any, 0);
                if (probeSocket != null)
                {
                    try { probeSocket.SendTo(Probe, service); }
                    catch (SocketException) { probeSocket.Dispose(); probeSocket = null; }
                }
            }

            if (now < nextRead)
                return null;
            nextRead = now + 0.05;
            return Read(probeSocket, Reply, now) ?? Read(heartbeatSocket, StockHeartbeat, now);
        }

        public void Reject(Uri candidate, double now)
        {
            // Let another advertising PC respond before retrying an incompatible bridge.
            if (rejectedUntil.Count >= 16)
                rejectedUntil.Clear();
            rejectedUntil[candidate.Host] = now + 6;
        }

        public void Stop()
        {
            probeSocket?.Dispose();
            heartbeatSocket?.Dispose();
            probeSocket = null;
            heartbeatSocket = null;
            nextProbe = nextRead = 0;
            rejectedUntil.Clear();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (multicastLock != null)
            {
                try { multicastLock.Call("release"); }
                catch (AndroidJavaException) { /* Wi-Fi service may have restarted. */ }
                multicastLock.Dispose();
                multicastLock = null;
            }
#endif
        }

        private static Socket Bind(IPAddress localAddress, int port)
        {
            Socket socket = null;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.EnableBroadcast = true;
                socket.Blocking = false;
                socket.Bind(new IPEndPoint(localAddress, port));
                return socket;
            }
            catch (SocketException)
            {
                socket?.Dispose();
                return null;
            }
        }

        private Uri Read(Socket socket, byte[] expected, double now)
        {
            if (socket == null)
                return null;
            try
            {
                EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                for (int i = 0; i < 16 && socket.Poll(0, SelectMode.SelectRead); i++)
                {
                    int size = socket.ReceiveFrom(buffer, ref sender);
                    var peer = (IPEndPoint)sender;
                    if (peer.Port != service.Port || size != expected.Length || !Matches(expected))
                        continue;
                    string host = peer.Address.ToString();
                    if (!rejectedUntil.TryGetValue(host, out double retryAt) || now >= retryAt)
                        return new UriBuilder("ws", host, bridgePort).Uri;
                }
            }
            catch (SocketException) { /* Datagrams can be dropped or exceed the buffer. */ }
            return null;
        }

        private bool Matches(byte[] expected)
        {
            for (int i = 0; i < expected.Length; i++)
                if (buffer[i] != expected[i]) return false;
            return true;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void AcquireMulticastLock()
        {
            try
            {
                using (var wifi = AndroidApplication.currentContext?.Call<AndroidJavaObject>("getSystemService", "wifi"))
                {
                    if (wifi == null) return;
                    multicastLock = wifi.Call<AndroidJavaObject>("createMulticastLock", "blacktheon-slimevr-discovery");
                    multicastLock.Call("setReferenceCounted", false);
                    multicastLock.Call("acquire");
                }
            }
            catch (AndroidJavaException)
            {
                // Unicast replies still work on devices that restrict the optional Wi-Fi lock.
                multicastLock?.Dispose();
                multicastLock = null;
            }
        }
#endif
    }
}
