using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Blacktheon.BodyTracking
{
    internal sealed class WebSocketFrameBuffer
    {
        // The spare byte allows an exact-limit message to end with a zero-byte continuation.
        private const int MaxBytes = 8192;
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly byte[] bytes = new byte[MaxBytes + 1];
        private int used;

        public ArraySegment<byte> Remaining => new ArraySegment<byte>(bytes, used, bytes.Length - used);

        public string Append(WebSocketReceiveResult result)
        {
            if (result.MessageType == WebSocketMessageType.Close)
                throw new IOException("Server closed the WebSocket");
            // SolarXR broadcasts binary updates on the same socket. Each fragment can reuse this buffer.
            if (result.MessageType == WebSocketMessageType.Binary)
                return null;
            if (result.Count > MaxBytes - used)
                throw new InvalidDataException("Expected a text message of at most 8192 bytes");
            used += result.Count;
            if (!result.EndOfMessage)
                return null;

            string message = StrictUtf8.GetString(bytes, 0, used);
            used = 0;
            return message;
        }
    }

    // Each attempt owns a socket and its queues. A cancelled attempt cannot deliver into a new session.
    internal sealed class BridgeSocket
    {
        internal readonly struct Frame
        {
            public readonly string Json;
            public readonly double ReceivedAt;
            public Frame(string json) { Json = json; ReceivedAt = Now; }
        }

        private const int MaxQueuedFrames = 256;
        private const string ResetYaw = "{\"type\":\"action\",\"name\":\"calibrate\"}";
        private readonly object gate = new object();
        private readonly Queue<Frame> incoming = new Queue<Frame>();
        private readonly SemaphoreSlim outgoing = new SemaphoreSlim(0, 1);
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private ClientWebSocket socket;
        private bool stopping;
        private string pendingRequest;
        private bool pendingReset;
        private volatile bool open;
        private volatile bool ended;
        private volatile string failure;

        public static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        public bool Open => open;
        public bool Ended => ended;
        public string Failure => failure;

        public BridgeSocket(Uri endpoint) => _ = Task.Run(() => Run(endpoint));

        public bool TryRead(out Frame frame)
        {
            lock (gate)
            {
                if (incoming.Count > 0)
                {
                    frame = incoming.Dequeue();
                    return true;
                }
                frame = default;
                return false;
            }
        }

        // Keep the newest unsent pose or poll. Calibration is ordered ahead of that next request.
        public void Queue(string request, bool resetYaw = false)
        {
            lock (gate)
            {
                if (!open || stopping)
                    return;
                bool wasEmpty = pendingRequest == null && !pendingReset;
                if (resetYaw)
                    pendingReset = true;
                else if (request != null)
                    pendingRequest = request;
                else
                    return;
                if (wasEmpty)
                    outgoing.Release();
            }
        }

        public void Stop()
        {
            lock (gate)
            {
                if (stopping)
                    return;
                stopping = true;
                open = false;
                incoming.Clear();
                cancellation.Cancel();
                socket?.Abort();
            }
        }

        private async Task Run(Uri endpoint)
        {
            ClientWebSocket client = null;
            try
            {
                client = new ClientWebSocket();
                lock (gate)
                {
                    if (stopping)
                        return;
                    socket = client;
                }

                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    await client.ConnectAsync(endpoint, timeout.Token).ConfigureAwait(false);
                }
                lock (gate)
                {
                    if (stopping)
                        return;
                    open = true;
                }

                Task receive = Receive(client, cancellation.Token);
                Task send = Send(client, cancellation.Token);
                try
                {
                    Task first = await Task.WhenAny(receive, send).ConfigureAwait(false);
                    await first.ConfigureAwait(false);
                }
                finally
                {
                    Stop();
                    // Both loops must finish before their socket, token and semaphore can be disposed.
                    try { await Task.WhenAll(receive, send).ConfigureAwait(false); }
                    catch (Exception) { /* The failure from the first loop is reported to the caller. */ }
                }
            }
            catch (OperationCanceledException) { failure = "Connection timed out"; }
            catch (Exception ex) { failure = ex.Message; }
            finally
            {
                lock (gate)
                {
                    stopping = true;
                    open = false;
                    socket = null;
                    client?.Dispose();
                    outgoing.Dispose();
                    cancellation.Dispose();
                    ended = true;
                }
            }
        }

        private async Task Receive(ClientWebSocket client, CancellationToken token)
        {
            var buffer = new WebSocketFrameBuffer();
            while (true)
            {
                var result = await client.ReceiveAsync(buffer.Remaining, token).ConfigureAwait(false);
                string json = buffer.Append(result);
                if (json == null)
                    continue;
                lock (gate)
                {
                    if (stopping)
                        return;
                    if (incoming.Count >= MaxQueuedFrames)
                        throw new IOException("Tracking input queue exceeded its limit");
                    incoming.Enqueue(new Frame(json));
                }
            }
        }

        private async Task Send(ClientWebSocket client, CancellationToken token)
        {
            while (true)
            {
                await outgoing.WaitAsync(token).ConfigureAwait(false);
                string request;
                bool reset;
                lock (gate)
                {
                    request = pendingRequest;
                    reset = pendingReset;
                    pendingRequest = null;
                    pendingReset = false;
                }
                if (reset)
                    await SendText(client, ResetYaw, token).ConfigureAwait(false);
                if (request != null)
                    await SendText(client, request, token).ConfigureAwait(false);
            }
        }

        private static Task SendText(ClientWebSocket client, string json, CancellationToken token)
        {
            var bytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(json));
            return client.SendAsync(bytes, WebSocketMessageType.Text, true, token);
        }
    }
}
