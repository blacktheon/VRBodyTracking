using UnityEngine;

namespace Blacktheon.BodyTracking.Demo
{
    // The mirror shows the raw six-tracker solve. The first-person avatar uses each shoe's measured lift.
    internal sealed class SolvedSkeleton
    {
        static readonly BodyJoint[] Roles =
        {
            BodyJoint.Head, BodyJoint.Chest, BodyJoint.Hips, BodyJoint.LeftKnee,
            BodyJoint.RightKnee, BodyJoint.LeftFoot, BodyJoint.RightFoot
        };
        static readonly int[] Parent = { -1, 0, 1, 2, 2, 3, 4 };
        readonly Transform root;
        readonly Transform[] mirror = new Transform[7], bones = new Transform[7];
        readonly bool[] visible = new bool[7];
        readonly float?[] restingFootHeight = new float?[2];
        readonly float[] lift = new float[2];
        internal int JointCount { get; private set; }

        internal SolvedSkeleton(DemoRoom room)
        {
            root = new GameObject("Six-tracker mirror").transform;
            root.SetParent(room.Root, false);
            for (int i = 0; i < Roles.Length; i++)
            {
                bool shoe = i >= 5;
                PrimitiveType mesh = shoe ? PrimitiveType.Cube : PrimitiveType.Sphere;
                Vector3 size = shoe ? new Vector3(0.135f, 0.11f, 0.27f) :
                    i == 0 ? new Vector3(0.20f, 0.22f, 0.19f) : Vector3.one * 0.115f;
                Color color = i == 3 || i == 5 ? DemoRoom.SeatColors[0] : i == 4 || i == 6 ? DemoRoom.SeatColors[1] : Color.white;
                mirror[i] = room.Part(root, "Mirror " + Roles[i], mesh, size, color);
                if (i > 0) bones[i] = room.Part(root, "Bone " + Roles[i], PrimitiveType.Cube, Vector3.one, color);
            }
            Hide();
        }

        internal void Hide()
        {
            root.gameObject.SetActive(false);
            JointCount = 0;
        }

        internal void ResetFootReference()
        {
            restingFootHeight[0] = restingFootHeight[1] = null;
        }

        internal bool TryGetFootLift(bool left, out float metres)
        {
            int foot = left ? 0 : 1;
            bool fresh = visible[5 + foot];
            metres = fresh ? lift[foot] : 0;
            return fresh;
        }

        internal void Draw(BodyTrackingClient client, Transform head, Quaternion heading, float trackingFloorY)
        {
            root.gameObject.SetActive(true);
            Vector3 normal = heading * Vector3.forward;
            Vector3 plane = head.position + normal * 0.95f;
            JointCount = 0;
            for (int i = 0; i < Roles.Length; i++)
            {
                visible[i] = client.TryGetJoint(Roles[i], out Pose joint);
                mirror[i].gameObject.SetActive(visible[i]);
                if (i > 0) bones[i].gameObject.SetActive(visible[i] && visible[Parent[i]]);
                if (!visible[i]) continue;
                JointCount++;
                joint.position.y -= trackingFloorY;
                if (i >= 5)
                {
                    int foot = i - 5;
                    restingFootHeight[foot] ??= joint.position.y;
                    lift[foot] = Mathf.Max(0, joint.position.y - restingFootHeight[foot].Value);
                }
                Quaternion rotation = Quaternion.LookRotation(Vector3.Reflect(joint.forward, normal), Vector3.Reflect(joint.up, normal));
                mirror[i].SetPositionAndRotation(plane + Vector3.Reflect(joint.position - plane, normal), rotation);
                if (i > 0 && visible[Parent[i]]) DemoRoom.Link(bones[i], mirror[Parent[i]].position, mirror[i].position, 0.045f);
            }
        }
    }
}
