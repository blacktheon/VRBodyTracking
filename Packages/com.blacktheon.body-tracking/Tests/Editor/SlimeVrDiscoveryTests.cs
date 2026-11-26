using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NUnit.Framework;

namespace Blacktheon.BodyTracking.Tests
{
    public sealed class SlimeVrDiscoveryTests
    {
        [Test]
        public void AcceptsOnlyTheStockHeartbeatOnTheHeartbeatSocket()
        {
            // Two loopback addresses let the stock server and headset both bind the same UDP port.
            using (var server = new UdpClient(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 0)))
            using (var otherSender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                var service = (IPEndPoint)server.Client.LocalEndPoint;
                var headset = new IPEndPoint(IPAddress.Loopback, service.Port);
                var discovery = new SlimeVrDiscovery(service, 21110, IPAddress.Loopback);
                try
                {
                    Assert.That(discovery.Find(0), Is.Null);
                    server.Client.ReceiveTimeout = 2000;
                    IPEndPoint activeSocket = null;
                    server.Receive(ref activeSocket);
                    byte[] heartbeat = new byte[12];
                    byte[] reply = Encoding.ASCII.GetBytes("BLACKTHEON_SLIMEVR_HERE_1");
                    byte[] wrongType = new byte[12];
                    wrongType[3] = 1;
                    otherSender.Send(heartbeat, heartbeat.Length, headset);
                    otherSender.Send(reply, reply.Length, activeSocket);
                    server.Send(heartbeat, heartbeat.Length, activeSocket);
                    server.Send(reply, reply.Length, headset);
                    server.Send(wrongType, wrongType.Length, headset);
                    server.Send(new byte[13], 13, headset);
                    server.Send(new byte[0], 0, headset);
                    Assert.That(discovery.Find(0.2), Is.Null);

                    server.Send(heartbeat, heartbeat.Length, headset);
                    var endpoint = discovery.Find(0.4);
                    Assert.That(endpoint, Is.EqualTo(new Uri("ws://127.0.0.2:21110")));
                    discovery.Reject(endpoint, 0.4);
                    server.Send(heartbeat, heartbeat.Length, headset);
                    Assert.That(discovery.Find(0.6), Is.Null);
                    server.Send(heartbeat, heartbeat.Length, headset);
                    Assert.That(discovery.Find(30), Is.EqualTo(endpoint));
                }
                finally { discovery.Stop(); }

                using (var nextSession = new UdpClient(headset))
                    Assert.That(nextSession.Client.LocalEndPoint, Is.EqualTo(headset));
            }
        }
    }
}
