using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.UI;

namespace Blacktheon.BodyTracking.Demo
{
    internal sealed class TabletPanel
    {
        readonly Camera camera;
        readonly RectTransform sidebar, content;
        readonly Text address, network;
        readonly Text[] seats = new Text[3];
        string ip;
        double nextAddress;

        internal TabletPanel(Transform parent)
        {
            var cameraObject = new GameObject("Tablet overview");
            cameraObject.transform.SetParent(parent, false);
            camera = cameraObject.AddComponent<Camera>();
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = DemoUi.Panel;
            camera.orthographic = true;
            camera.nearClipPlane = 0.1f;
            camera.transform.position = new Vector3(3.8f, 4.6f, -5.9f);
            camera.transform.LookAt(Vector3.up * 0.5f);
            var canvas = DemoUi.Canvas(parent, "Tablet dashboard", false);
            sidebar = (RectTransform)DemoUi.Block(canvas, "Status panel", 0, 0, 0, 0, DemoUi.Panel).transform;
            sidebar.anchorMin = Vector2.zero;
            sidebar.anchorMax = new Vector2(0.34f, 1);
            sidebar.offsetMin = sidebar.offsetMax = Vector2.zero;
            content = (RectTransform)new GameObject("Seat status", typeof(RectTransform)).transform;
            content.SetParent(sidebar, false);
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(0, 1);
            content.sizeDelta = new Vector2(350, 595);
            content.anchoredPosition = Vector2.zero;
            DemoUi.Label(content, "Title", 20, 17, 310, 50, 37, Color.white, FontStyle.Bold).text = "Tracking room";
            address = DemoUi.Label(content, "Tablet address", 21, 79, 310, 67, 24, DemoUi.Accent);
            network = DemoUi.Label(content, "Network", 21, 144, 310, 85, 20, DemoUi.Muted);
            for (int i = 0; i < 3; i++)
            {
                var card = DemoUi.Block(content, "Seat " + (i + 1), 16, 243 + 110 * i, 316, 96, DemoUi.Row);
                DemoUi.Block(card.transform, "Color", 0, 0, 5, 96, DemoRoom.SeatColors[i]);
                DemoUi.Label(card.transform, "Seat", 15, 5, 285, 37, 25, DemoRoom.SeatColors[i], FontStyle.Bold).text = "Seat " + (i + 1);
                seats[i] = DemoUi.Label(card.transform, "Feet", 15, 43, 287, 49, 19, Color.white);
            }
        }

        internal void Draw(PoseRoster roster, string status, int port, double now)
        {
            content.localScale = Vector3.one * Mathf.Min(sidebar.rect.width / 350f, sidebar.rect.height / 595f);
            camera.rect = new Rect(0.34f, 0, 0.66f, 1);
            camera.orthographicSize = Mathf.Max(3.8f, 5.3f / Mathf.Max(0.15f, camera.aspect));
            if (ip == null || now >= nextAddress)
            {
                nextAddress = now + 5;
                ip = LocalAddresses();
            }
            address.text = ip;
            network.text = "UDP " + port + "     " + roster.Count + " / 3 seats\n" + status;
            for (int i = 0; i < 3; i++)
                seats[i].text = roster.TryGet(i + 1, out _, out AvatarPose pose)
                    ? "L  " + Lift(pose, true) + "     R  " + Lift(pose, false) : "Waiting for headset";
        }

        static string LocalAddresses()
        {
            try
            {
                var addresses = new List<(int rank, string ip)>();
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus == OperationalStatus.Down) continue;
                    int rank = LanPriority(adapter.Name, adapter.NetworkInterfaceType);
                    if (rank < 0) continue;
                    foreach (var host in adapter.GetIPProperties().UnicastAddresses)
                        if (host.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(host.Address))
                            addresses.Add((rank, host.Address.ToString()));
                }
                if (addresses.Count > 0)
                    return string.Join("\n", addresses.OrderBy(a => a.rank).Select(a => a.ip).Distinct().Take(2));
            }
            catch (Exception ex) when (ex is NetworkInformationException || ex is SocketException || ex is NotSupportedException || ex is NotImplementedException)
            {
                // Direct IP entry and discovery both remain usable when an OS hides interface details.
            }
            return "Check tablet Wi-Fi address";
        }

        internal static int LanPriority(string name, NetworkInterfaceType type)
        {
            name = name.ToLowerInvariant();
            if (name.StartsWith("ap") || name.StartsWith("softap") || name.StartsWith("swlan")) return 0;
            if (type == NetworkInterfaceType.Wireless80211 || name.StartsWith("wlan") || name.StartsWith("wifi")) return 1;
            if (name.StartsWith("rmnet") || name.StartsWith("wwan") || name.StartsWith("tun")) return -1;
            return type == NetworkInterfaceType.Ethernet ? 2 : -1;
        }

        internal static string Lift(AvatarPose pose, bool left)
        {
            if ((pose.FreshFeet & (left ? 1 : 2)) == 0) return "no data";
            float height = (left ? pose.LeftFoot : pose.RightFoot).position.y - SeatedMotion.ShoeCenterHeight;
            return Mathf.Max(0, Mathf.RoundToInt(height * 100)) + " cm";
        }
    }
}
