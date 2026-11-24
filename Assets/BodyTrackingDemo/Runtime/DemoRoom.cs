using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Blacktheon.BodyTracking.Demo
{
    internal sealed class DemoRoom : IDisposable
    {
        internal static readonly Color[] SeatColors =
        {
            new Color32(72, 222, 204, 255), new Color32(255, 170, 83, 255), new Color32(174, 154, 255, 255)
        };
        internal static readonly Color Inactive = new Color32(110, 129, 144, 255);
        static readonly int ColorId = Shader.PropertyToID("_Color");
        readonly Material material;
        readonly MaterialPropertyBlock tint = new();
        internal Transform Root { get; }

        internal DemoRoom(Transform parent)
        {
            Root = new GameObject("Seated tracking room").transform;
            Root.SetParent(parent, false);
            material = new Material(Resources.Load<Shader>("SeatedTrackingSurface"));
            Box("Floor", Vector3.down * 0.065f, new Vector3(10.5f, 0.12f, 10.5f), new Color32(31, 50, 62, 255));
            Color grid = new Color32(58, 83, 93, 255);
            for (int i = -5; i <= 5; i++)
            {
                Box("Grid", new Vector3(i, -0.004f, 0), new Vector3(0.012f, 0.002f, 10), grid);
                Box("Grid", new Vector3(0, -0.003f, i), new Vector3(10, 0.002f, 0.012f), grid);
            }
            for (int i = 0; i < 3; i++)
            {
                var heading = Quaternion.Euler(0, i == 1 ? 120 : i == 2 ? -120 : 0, 0);
                Transform mark = Box("Seat " + (i + 1), heading * (Vector3.back * 1.85f),
                    new Vector3(0.56f, 0.005f, 0.70f), Color.Lerp(grid, SeatColors[i], 0.35f));
                mark.rotation = heading;
            }
        }

        Transform Box(string name, Vector3 position, Vector3 size, Color color)
        {
            Transform part = Part(Root, name, PrimitiveType.Cube, size, color);
            part.localPosition = position;
            return part;
        }

        internal Transform Part(Transform parent, string name, PrimitiveType mesh, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(mesh);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = size;
            // Keep the native types needed by CreatePrimitive in stripped Android players.
            Collider collider = mesh == PrimitiveType.Sphere ? go.GetComponent<SphereCollider>() : go.GetComponent<BoxCollider>();
            if (collider != null) { collider.enabled = false; Object.Destroy(collider); }
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Tint(renderer, color);
            return go.transform;
        }

        internal void Tint(Renderer renderer, Color color)
        {
            tint.SetColor(ColorId, color);
            renderer.SetPropertyBlock(tint);
        }

        internal static void Link(Transform part, Vector3 start, Vector3 end, float width)
        {
            Vector3 line = end - start;
            part.position = (start + end) * 0.5f;
            part.rotation = line.sqrMagnitude > 0.00001f ? Quaternion.FromToRotation(Vector3.up, line) : Quaternion.identity;
            part.localScale = new Vector3(width, line.magnitude, width);
        }

        public void Dispose()
        {
            Object.Destroy(material);
            if (Root != null) Object.Destroy(Root.gameObject);
        }
    }

    internal sealed class SeatAvatar
    {
        readonly DemoRoom room;
        readonly Transform root, head, torso;
        readonly Transform[] hands = new Transform[2], feet = new Transform[2],
            thighs = new Transform[2], shins = new Transform[2], arms = new Transform[2];
        readonly Renderer[] shoes = new Renderer[2];
        readonly bool own;
        readonly Color color;
        int previousFresh = -1;

        internal SeatAvatar(DemoRoom room, int slot, bool own)
        {
            this.room = room;
            this.own = own;
            color = DemoRoom.SeatColors[slot - 1];
            Color suit = Color.Lerp(new Color32(38, 58, 73, 255), color, 0.45f);
            root = new GameObject((own ? "My " : "") + "Seat " + slot).transform;
            root.SetParent(room.Root, false);
            head = room.Part(root, "Headset", PrimitiveType.Sphere, new Vector3(0.24f, 0.25f, 0.23f), color);
            Transform visor = room.Part(head, "Visor", PrimitiveType.Cube, new Vector3(0.83f, 0.42f, 0.45f), new Color32(25, 37, 52, 255));
            visor.localPosition = new Vector3(0, 0, 0.38f);
            torso = room.Part(root, "Torso", PrimitiveType.Sphere, new Vector3(0.38f, 0.48f, 0.24f), suit);
            head.gameObject.SetActive(!own);
            torso.gameObject.SetActive(!own);
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? "Left " : "Right ";
                hands[i] = room.Part(root, side + "hand", PrimitiveType.Sphere, new Vector3(0.085f, 0.10f, 0.13f), color);
                feet[i] = room.Part(root, side + "shoe", PrimitiveType.Cube, new Vector3(0.135f, 0.11f, 0.27f), color);
                shoes[i] = feet[i].GetComponent<Renderer>();
                Transform stripe = room.Part(feet[i], "Toe stripe", PrimitiveType.Cube, new Vector3(0.96f, 0.04f, 0.13f), Color.white);
                stripe.localPosition = new Vector3(0, 0.503f, 0.29f);
                thighs[i] = room.Part(root, side + "thigh", PrimitiveType.Cube, Vector3.one, suit);
                shins[i] = room.Part(root, side + "shin", PrimitiveType.Cube, Vector3.one, suit);
                arms[i] = room.Part(root, side + "arm", PrimitiveType.Cube, Vector3.one, suit);
                arms[i].gameObject.SetActive(!own);
            }
            Show(false);
        }

        internal void Show(bool show)
        {
            if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
        }

        internal void Draw(AvatarPose pose)
        {
            Show(true);
            Quaternion body = pose.LeftFoot.rotation;
            Vector3 up = Vector3.up;
            Vector3 chest = pose.Head.position - 0.34f * up - body * (0.05f * Vector3.forward);
            head.SetPositionAndRotation(pose.Head.position, pose.Head.rotation);
            torso.SetPositionAndRotation(chest, body);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1 : 1;
                Pose hand = i == 0 ? pose.LeftHand : pose.RightHand;
                Pose foot = i == 0 ? pose.LeftFoot : pose.RightFoot;
                hands[i].SetPositionAndRotation(hand.position, hand.rotation);
                feet[i].SetPositionAndRotation(foot.position, foot.rotation);
                Vector3 hip = chest + body * new Vector3(0.11f * side, -0.27f, -0.05f);
                Vector3 knee = foot.position + up * Mathf.Clamp(hip.y - foot.position.y - 0.06f, 0.13f, 0.41f);
                DemoRoom.Link(thighs[i], hip, knee, 0.092f);
                DemoRoom.Link(shins[i], knee, foot.position, 0.075f);
                if (!own) DemoRoom.Link(arms[i], chest + body * new Vector3(side * 0.16f, 0.12f, 0), hand.position, 0.060f);
                if (previousFresh != pose.FreshFeet)
                    room.Tint(shoes[i], (pose.FreshFeet & (1 << i)) != 0 ? color : DemoRoom.Inactive);
            }
            previousFresh = pose.FreshFeet;
        }
    }
}
