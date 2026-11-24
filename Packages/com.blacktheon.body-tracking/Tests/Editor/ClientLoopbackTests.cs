using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Blacktheon.BodyTracking.Tests
{
    public sealed class ClientLoopbackTests
    {
        [Test]
        public void ChangingModeAndFocusReleasesTheSocketAndGroundsOldReadings()
        {
            using (var server = new LoopbackBridge())
            {
                var rig = new GameObject("Test rig");
                try
                {
                    var client = rig.AddComponent<BodyTrackingClient>();
                    client.Connect(server.Address, 1, BodyTrackingMode.SixTrackers, rig.transform, rig.transform);
                    WaitFor(client, server, () => client.TryGetJoint(BodyJoint.Hips, out _));
                    client.Calibrate();
                    WaitFor(client, server, () => server.YawResets == 1);
                    client.Connect(server.Address, 2, BodyTrackingMode.PairedFeet, null, null);
                    Assert.That(client.TryGetJoint(BodyJoint.Hips, out _), Is.False);
                    WaitFor(client, server, () => client.TryGetFootLift(true, out _));
                    client.Calibrate();
                    Assert.That(client.TryGetFootLift(true, out _), Is.False);
                    WaitFor(client, server, () => client.TryGetFootLift(false, out _));
                    client.OnApplicationFocus(false);
                    Assert.That(client.TryGetFootLift(false, out float height), Is.False);
                    Assert.That(height, Is.Zero);
                    client.OnApplicationFocus(true);
                    WaitFor(client, server, () => client.TryGetFootLift(true, out _));
                    client.Disconnect();
                    Assert.That(client.TryGetFootLift(true, out _), Is.False);
                    Assert.That(server.Accepted, Is.EqualTo(3));
                    Assert.That(server.Error, Is.Null);
                }
                finally { Object.DestroyImmediate(rig); }
            }
        }

        [Test]
        public void ServerClosingTheSocketTriggersAutomaticReconnectAndRecalibration()
        {
            using (var server = new LoopbackBridge(false))
            {
                var rig = new GameObject("Test rig");
                var client = rig.AddComponent<BodyTrackingClient>();
                try
                {
                    client.Connect(server.Address, 2, BodyTrackingMode.PairedFeet, null, null);
                    WaitFor(client, server, () => client.TryGetFootLift(true, out _));
                    server.CloseOnNextPoll();
                    WaitFor(client, server, () => !client.TryGetFootLift(true, out _));
                    WaitFor(client, server, () => client.TryGetFootLift(true, out _));
                    Assert.That(server.Accepted, Is.EqualTo(2));
                }
                finally
                {
                    client.Disconnect();
                    Object.DestroyImmediate(rig);
                }
            }
        }

        private static void WaitFor(BodyTrackingClient client, LoopbackBridge server, Func<bool> ready)
        {
            var timer = Stopwatch.StartNew();
            do
            {
                client.Update();
                if (ready())
                    return;
                if (server.Error != null)
                    Assert.Fail(server.Error);
                Thread.Sleep(10);
            } while (timer.ElapsedMilliseconds < 5000);
            Assert.Fail("No usable data from the loopback bridge. " + client.Status);
        }

        // A real RFC 6455 loopback peer, using the platform WebSocket stream after the HTTP upgrade.
        private sealed class LoopbackBridge : IDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly CancellationTokenSource stop = new CancellationTokenSource();
            private readonly ConcurrentQueue<string> errors = new ConcurrentQueue<string>();
            private readonly Stopwatch samples = Stopwatch.StartNew();
            private readonly bool firstConnectionIsSixTrackers;
            private int closeOnNextPoll;
            private int yawResets;
            private int accepted;
            public int YawResets => Volatile.Read(ref yawResets);
            public void CloseOnNextPoll() => Interlocked.Exchange(ref closeOnNextPoll, 1);
            public int Accepted => Volatile.Read(ref accepted);
            public string Error => errors.TryPeek(out var error) ? error : null;
            public string Address { get; }

            public LoopbackBridge(bool firstConnectionIsSixTrackers = true)
            {
                this.firstConnectionIsSixTrackers = firstConnectionIsSixTrackers;
                listener.Start();
                Address = "127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
                _ = Task.Run(Listen);
            }

            public void Dispose()
            {
                stop.Cancel();
                listener.Stop();
            }

            private async Task Listen()
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        using (var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false))
                        {
                            int number = Interlocked.Increment(ref accepted);
                            try { await Serve(client.GetStream(), number).ConfigureAwait(false); }
                            catch (WebSocketException) { /* Client aborted this connection on a mode or focus change. */ }
                            catch (IOException) { /* Some Mono versions report socket shutdown as an I/O error. */ }
                        }
                    }
                }
                catch (Exception e) { if (!stop.IsCancellationRequested) errors.Enqueue(e.ToString()); }
            }

            private async Task Serve(NetworkStream stream, int number)
            {
                using (var headers = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                {
                    string key = null;
                    string line;
                    while (!string.IsNullOrEmpty(line = await headers.ReadLineAsync().ConfigureAwait(false)))
                    {
                        if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                            key = line.Substring(18).Trim();
                    }
                    if (key == null)
                        throw new InvalidOperationException("Missing WebSocket key");
                    using (var sha = SHA1.Create())
                    {
                        string accept = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                        byte[] reply = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
                        await stream.WriteAsync(reply, 0, reply.Length, stop.Token).ConfigureAwait(false);
                    }
                }

                using (var socket = WebSocket.CreateFromStream(stream, true, null, Timeout.InfiniteTimeSpan))
                {
                    var buffer = new byte[2048];
                    bool sixTrackers = firstConnectionIsSixTrackers && number == 1;
                    if (sixTrackers)
                        await Reply(socket, "{\"type\":\"config\",\"tracker_id\":\"hip\",\"location\":\"waist\"}").ConfigureAwait(false);
                    var broadcast = new ArraySegment<byte>(new byte[] { 0, 0, 255, 128 });
                    await socket.SendAsync(broadcast, WebSocketMessageType.Binary, false, stop.Token).ConfigureAwait(false);
                    await socket.SendAsync(broadcast, WebSocketMessageType.Binary, true, stop.Token).ConfigureAwait(false);
                    while (!stop.IsCancellationRequested)
                    {
                        var incoming = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), stop.Token).ConfigureAwait(false);
                        if (incoming.MessageType == WebSocketMessageType.Close)
                            return;
                        string request = Encoding.UTF8.GetString(buffer, 0, incoming.Count);
                        if (Interlocked.Exchange(ref closeOnNextPoll, 0) == 1)
                        {
                            await socket.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, "Restart", stop.Token).ConfigureAwait(false);
                            return;
                        }
                        if (sixTrackers && request.Contains("\"action\"") && request.Contains("\"calibrate\""))
                            Interlocked.Increment(ref yawResets);
                        else if (sixTrackers && request.Contains("\"pos\""))
                            await Reply(socket, "{\"type\":\"pos\",\"tracker_id\":\"hip\",\"x\":0,\"y\":0.5,\"z\":0,\"qx\":0,\"qy\":0,\"qz\":0,\"qw\":1}").ConfigureAwait(false);
                        else if (!sixTrackers && request.Contains("\"blacktheon.feet.v1\"") && request.Contains("\"slot\":2"))
                        {
                            long t = samples.ElapsedMilliseconds;
                            await Reply(socket, "{\"type\":\"blacktheon.feet.v1\",\"slot\":2,\"left\":{\"id\":\"L\",\"valid\":true,\"ay\":0,\"t\":" + t +
                                "},\"right\":{\"id\":\"R\",\"valid\":true,\"ay\":0,\"t\":" + t + "}}").ConfigureAwait(false);
                        }
                        else
                            throw new InvalidOperationException("Unexpected request on mode-specific socket. " + request);
                    }
                }
            }

            private async Task Reply(WebSocket socket, string json)
            {
                byte[] data = Encoding.UTF8.GetBytes(json);
                int first = data.Length / 2;
                await socket.SendAsync(new ArraySegment<byte>(data, 0, first), WebSocketMessageType.Text, false, stop.Token).ConfigureAwait(false);
                await socket.SendAsync(new ArraySegment<byte>(data, first, data.Length - first), WebSocketMessageType.Text, true, stop.Token).ConfigureAwait(false);
            }
        }
    }
}
