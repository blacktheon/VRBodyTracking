using System;
using UnityEngine;

namespace Blacktheon.BodyTracking
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-80)]
    public sealed class BodyTrackingClient : MonoBehaviour
    {
        [SerializeField, Range(0.25f, 4f), Tooltip("Vertical acceleration in m/s² that starts a seated lift or drop.")]
        private float motionThreshold = 0.7f;
        [SerializeField, Range(0.03f, 0.75f), Tooltip("Upper bound of an estimated foot lift in metres.")]
        private float maximumFootLift = 0.35f;

        private const string Searching = "Searching for SlimeVR on Wi-Fi";
        private Uri endpoint;
        private Uri lastKnownEndpoint;
        private SlimeVrDiscovery discovery;
        private int slot;
        private BodyTrackingMode mode;
        private Transform origin;
        private Transform hmd;
        private BridgeSocket socket;
        private TrackingSession session;
        private bool paused;
        private bool focused = true;
        private bool observedOpen;
        private int failures;
        private double openedAt, nextAttempt, nextPoll, nextHint;

        public string Status { get; private set; } = "Disconnected";
        public string ConnectedHost { get; private set; }

        public void Connect(string host, int slot, BodyTrackingMode mode, Transform trackingOrigin, Transform head)
        {
            Disconnect();
            if (!TryEndpoint(host, out var address))
            {
                Status = "Enter the SlimeVR PC host or IP address";
                return;
            }
            Configure(address, null, slot, mode, trackingOrigin, head);
        }

        public void ConnectAuto(int slot, BodyTrackingMode mode, Transform trackingOrigin, Transform head, string lastKnownHost = null)
        {
            ConnectAuto(new SlimeVrDiscovery(), slot, mode, trackingOrigin, head, lastKnownHost);
        }

        internal void ConnectAuto(SlimeVrDiscovery search, int slot, BodyTrackingMode mode, Transform trackingOrigin, Transform head, string lastKnownHost)
        {
            Disconnect();
            if (!TryEndpoint(lastKnownHost, out var hint))
                hint = null;
            Configure(hint, search, slot, mode, trackingOrigin, head);
        }

        private void Configure(Uri address, SlimeVrDiscovery search, int slot, BodyTrackingMode mode, Transform trackingOrigin, Transform head)
        {
            if (mode != BodyTrackingMode.SixTrackers && mode != BodyTrackingMode.PairedFeet)
            {
                Status = "Unknown tracking mode";
                return;
            }
            if (mode == BodyTrackingMode.PairedFeet && (slot < 1 || slot > 3))
            {
                Status = "Choose player slot 1, 2 or 3";
                return;
            }
            if (mode == BodyTrackingMode.SixTrackers && (!trackingOrigin || !head))
            {
                Status = "Assign the floor tracking origin and head";
                return;
            }

            endpoint = address;
            discovery = search;
            lastKnownEndpoint = search == null ? null : address;
            this.mode = mode;
            this.slot = slot;
            origin = trackingOrigin;
            hmd = head;
            RefreshActivity();
        }

        public void Disconnect()
        {
            discovery?.Stop();
            discovery = null;
            lastKnownEndpoint = null;
            endpoint = null;
            origin = null;
            hmd = null;
            failures = 0;
            nextAttempt = nextHint = 0;
            StopAttempt();
            Status = "Disconnected";
        }

        public void Calibrate()
        {
            if (mode == BodyTrackingMode.PairedFeet)
                session?.CalibrateFeet();
            else
                socket?.Queue(null, true);
        }

        public bool TryGetJoint(BodyJoint joint, out Pose pose)
        {
            if (session != null && socket.Open)
                return session.TryGetJoint(joint, origin, BridgeSocket.Now, out pose);
            pose = default;
            return false;
        }

        public bool TryGetFootLift(bool left, out float metres)
        {
            if (session != null && socket.Open)
                return session.TryGetFootLift(left, BridgeSocket.Now, out metres);
            metres = 0f;
            return false;
        }

        private void OnEnable() => RefreshActivity();
        private void OnDisable() => Suspend();
        private void OnDestroy() => Disconnect();

        private void OnApplicationPause(bool value)
        {
            paused = value;
            RefreshActivity();
        }

        internal void OnApplicationFocus(bool value)
        {
            focused = value;
            RefreshActivity();
        }

        private void RefreshActivity()
        {
            if (endpoint == null && discovery == null)
                return;
            if (!isActiveAndEnabled || paused || !focused)
                Suspend();
            else if (socket == null)
            {
                if (endpoint != null)
                    StartAttempt();
                else
                    Status = Searching;
            }
        }

        private void Suspend()
        {
            if (endpoint == null && discovery == null)
                return;
            StopAttempt();
            if (discovery != null)
            {
                discovery.Stop();
                endpoint = lastKnownEndpoint;
                nextAttempt = 0;
            }
            Status = "Tracking suspended";
        }

        private void StartAttempt()
        {
            session = new TrackingSession(mode, slot, motionThreshold, maximumFootLift);
            socket = new BridgeSocket(endpoint);
            observedOpen = false;
            nextPoll = 0;
            Status = "Connecting to " + endpoint.Authority;
        }

        private void StopAttempt()
        {
            socket?.Stop();
            socket = null;
            session = null;
            ConnectedHost = null;
        }

        private void Retry(string reason, double now)
        {
            bool confirmed = ConnectedHost != null;
            StopAttempt();
            if (discovery != null)
            {
                if (!confirmed)
                    discovery.Reject(endpoint, now);
                endpoint = null;
                nextAttempt = now + 0.25;
                nextHint = now + (confirmed ? 2 : 12);
                Status = Searching + " — " + reason;
                return;
            }
            double delay = Math.Min(8, 0.5 * (1 << Math.Min(failures, 4)));
            failures = Math.Min(failures + 1, 4);
            nextAttempt = now + delay;
            Status = "Reconnecting to " + endpoint.Authority + " — " + reason;
        }

        internal void Update()
        {
            if ((endpoint == null && discovery == null) || paused || !focused)
                return;
            double now = BridgeSocket.Now;
            if (socket == null)
            {
                if (now < nextAttempt)
                    return;
                if (endpoint == null)
                {
                    endpoint = discovery.Find(now);
                    if (endpoint == null && now >= nextHint)
                        endpoint = lastKnownEndpoint;
                }
                if (endpoint != null)
                    StartAttempt();
                return;
            }
            if (socket.Ended)
            {
                Retry(socket.Failure ?? "Socket closed", now);
                return;
            }
            if (!socket.Open)
                return;

            if (!observedOpen)
            {
                observedOpen = true;
                openedAt = now;
            }
            // Use network receipt time so a main-thread stall cannot refresh buffered measurements.
            for (int i = 0; i < 128 && socket.TryRead(out var frame); i++)
                session.Apply(frame.Json, frame.ReceivedAt);

            if (now - Math.Max(openedAt, session.LastReply) > 6)
            {
                Retry(mode == BodyTrackingMode.PairedFeet ? "No paired-feet response from PC" : "No solved body response from PC", now);
                return;
            }
            if (!double.IsNegativeInfinity(session.LastReply))
            {
                failures = 0;
                if (ConnectedHost == null)
                {
                    ConnectedHost = endpoint.Host;
                    if (discovery != null)
                    {
                        lastKnownEndpoint = endpoint;
                        discovery.Stop();
                    }
                }
            }
            if (mode == BodyTrackingMode.SixTrackers && (!origin || !hmd))
            {
                Status = "Tracking origin or head is missing";
                return;
            }

            bool probing = mode == BodyTrackingMode.PairedFeet &&
                           (!session.HasPairReply || !string.IsNullOrEmpty(session.PairError) || now - session.LastReply > 2.5);
            double interval = probing ? 0.65 : 0.02;
            if (nextPoll > now + interval || now - nextPoll >= interval)
                nextPoll = now;
            if (now >= nextPoll)
            {
                socket.Queue(session.BuildRequest(origin, hmd));
                nextPoll += interval;
            }
            Status = session.Describe(now);
        }

        private static bool TryEndpoint(string host, out Uri endpoint)
        {
            endpoint = null;
            if (string.IsNullOrWhiteSpace(host))
                return false;
            string address = host.Trim();
            if (address.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
                address = address.Substring(5);
            address = address.TrimEnd('/');
            if (Uri.CheckHostName(address) != UriHostNameType.Unknown)
            {
                endpoint = new UriBuilder("ws", address, 21110).Uri;
                return true;
            }
            return Uri.TryCreate("ws://" + address, UriKind.Absolute, out endpoint) &&
                   !string.IsNullOrEmpty(endpoint.Host) && endpoint.Port > 0 && endpoint.AbsolutePath == "/" &&
                   endpoint.UserInfo.Length == 0 && endpoint.Query.Length == 0 && endpoint.Fragment.Length == 0;
        }
    }
}
