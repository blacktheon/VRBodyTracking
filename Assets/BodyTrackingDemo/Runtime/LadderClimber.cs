using UnityEngine;

namespace Blacktheon.BodyTracking.Demo
{
    // The tracked rig moves, but its head and controllers keep their physical tracking-space poses.
    internal sealed class LadderClimber
    {
        const float GripReach = 0.145f;
        readonly ClimbLadder ladder;
        readonly Collider[] vehicleSurfaces;
        readonly LadderClimb steps = new();
        readonly float[] gripHeightOffset = new float[2];
        bool leftWasDown, rightWasDown, waitForRelease, attached, heldTop;
        float groundRigY, elevation;
        Vector3 previousBase;
        Quaternion previousHeading;
        Matrix4x4 vehicleFrame;
        Quaternion vehicleRotation;
        double flashUntil;

        internal LadderClimber(ClimbLadder ladder)
        {
            this.ladder = ladder;
            vehicleSurfaces = ladder.VehicleRoot.GetComponentsInChildren<Collider>();
        }
        internal bool Holding => steps.Holding;
        internal bool OnRoof { get; private set; }
        internal float PlatformHeight { get; private set; }
        internal LadderResult Feedback { get; private set; }
        internal int NearbyRung { get; private set; }
        internal ClimbSnapshot Snapshot => new ClimbSnapshot
        {
            LeftHand = (byte)steps.HandRung(true), RightHand = (byte)steps.HandRung(false),
            LeftFoot = (byte)steps.FootRung(true), RightFoot = (byte)steps.FootRung(false),
            OnRoof = OnRoof,
            Feedback = Feedback == LadderResult.Fell ? ClimbFeedback.Fell :
                Feedback == LadderResult.Legal ? ClimbFeedback.Legal : ClimbFeedback.None
        };

        internal void Tick(Transform rig, Transform head, Transform left, Transform right, bool leftGrip, bool rightGrip,
            byte freshFeet, float leftLift, float rightLift, bool playing, double now)
        {
            if (now >= flashUntil) Feedback = LadderResult.None;
            bool grabLeft = leftGrip && !leftWasDown;
            bool grabRight = rightGrip && !rightWasDown;
            leftWasDown = leftGrip;
            rightWasDown = rightGrip;
            if (!playing)
            {
                Cancel(rig);
                return;
            }
            if (OnRoof)
            {
                FollowRoof(rig, head);
                return;
            }
            if (Feedback == LadderResult.Fell) return;
            if (waitForRelease)
            {
                waitForRelease = leftGrip || rightGrip;
                if (waitForRelease) return;
            }

            if (attached) FollowLadder(rig);
            int nearLeft = left ? ladder.FindRung(left.position, GripReach) : 0;
            int nearRight = right ? ladder.FindRung(right.position, GripReach) : 0;
            NearbyRung = nearLeft > 0 ? nearLeft : nearRight;
            // A transfer can contain a squeeze and a release in the same tracking frame.
            if (grabLeft && nearLeft > 0 && !TryGrab(rig, left, true, nearLeft, now)) return;
            if (grabRight && nearRight > 0 && !TryGrab(rig, right, false, nearRight, now)) return;
            if (!leftGrip || !left) steps.Release(true);
            if (!rightGrip || !right) steps.Release(false);
            if (!Holding)
            {
                // A seated climber's hands reach the top before the boots. Releasing both grips completes the climb.
                if (attached && !(heldTop && Mathf.Max(steps.FootRung(true), steps.FootRung(false)) > 0
                    && LandOnRoof(rig, head))) ReturnToGround(rig);
                return;
            }
            if (!Handle(steps.SampleFeet(freshFeet, leftLift, rightLift, ladder.RungCount), rig, now)) return;

            float pull = 0f;
            int grips = 0;
            void Pull(Transform hand, bool isLeft)
            {
                int rung = steps.HandRung(isLeft);
                if (rung == 0 || !hand) return;
                pull += ladder.RungPoint(rung, isLeft).y + gripHeightOffset[isLeft ? 0 : 1] - hand.position.y;
                grips++;
            }
            Pull(left, true);
            Pull(right, false);
            float baseY = ladder.RungPoint(0, true).y;
            float footSupport = 0.5f * (ladder.RungPoint(steps.FootRung(true), true).y
                + ladder.RungPoint(steps.FootRung(false), false).y) - baseY;
            float target = Mathf.Clamp(Mathf.Max(footSupport, elevation + pull / Mathf.Max(1, grips)),
                0f, Mathf.Max(0f, ladder.RungPoint(ladder.RungCount, true).y - baseY));
            rig.position += Vector3.up * (target - elevation);
            elevation = target;
        }

        bool TryGrab(Transform rig, Transform hand, bool left, int rung, double now)
        {
            if (!Handle(steps.Grab(left, rung, ladder.RungCount), rig, now)) return false;
            if (steps.HandRung(left) != rung) return true;
            heldTop |= rung == ladder.RungCount;
            gripHeightOffset[left ? 0 : 1] = hand.position.y - ladder.RungPoint(rung, left).y;
            if (!attached)
            {
                attached = true;
                groundRigY = rig.position.y;
                previousBase = ladder.RungPoint(0, true);
                previousHeading = Facing;
            }
            return true;
        }

        bool Handle(LadderResult result, Transform rig, double now)
        {
            if (result == LadderResult.None) return true;
            Feedback = result;
            flashUntil = now + 1d;
            if (result == LadderResult.Legal) return true;
            ReturnToGround(rig);
            waitForRelease = true;
            return false;
        }

        Quaternion Facing => Quaternion.LookRotation(ladder.FaceDirection, Vector3.up);

        bool LandOnRoof(Transform rig, Transform head)
        {
            int top = ladder.RungCount;
            Vector3 point = (ladder.RungPoint(top, true) + ladder.RungPoint(top, false)) * 0.5f
                + ladder.FaceDirection * 0.65f;
            Physics.SyncTransforms();
            if (!FindRoof(point, point.y, out var hit)) return false;
            Vector3 shift = Vector3.ProjectOnPlane(hit.point - head.position, Vector3.up);
            shift.y = groundRigY + hit.point.y - rig.position.y;
            rig.position += shift;
            PlatformHeight = hit.point.y;
            vehicleFrame = ladder.VehicleRoot.localToWorldMatrix;
            vehicleRotation = ladder.VehicleRoot.rotation;
            OnRoof = true;
            attached = heldTop = false;
            elevation = 0;
            NearbyRung = 0;
            steps.Reset();
            return true;
        }

        void FollowRoof(Transform rig, Transform head)
        {
            Transform vehicle = ladder.VehicleRoot;
            Matrix4x4 current = vehicle.localToWorldMatrix;
            float expectedHeight = PlatformHeight;
            if (current != vehicleFrame)
            {
                Vector3 oldSupport = new Vector3(head.position.x, PlatformHeight, head.position.z);
                Vector3 newSupport = current.MultiplyPoint3x4(vehicleFrame.inverse.MultiplyPoint3x4(oldSupport));
                float yaw = Mathf.DeltaAngle(vehicleRotation.eulerAngles.y, vehicle.eulerAngles.y);
                rig.RotateAround(head.position, Vector3.up, yaw);
                rig.position += Vector3.ProjectOnPlane(newSupport - oldSupport, Vector3.up);
                expectedHeight = newSupport.y;
                vehicleRotation = vehicle.rotation;
                vehicleFrame = current;
                Physics.SyncTransforms();
            }
            if (FindRoof(head.position, expectedHeight, out var hit))
            {
                rig.position += Vector3.up * (hit.point.y - PlatformHeight);
                PlatformHeight = hit.point.y;
            }
            else LeaveRoof(rig);
        }

        bool FindRoof(Vector3 point, float expectedHeight, out RaycastHit best)
        {
            var ray = new Ray(new Vector3(point.x, expectedHeight + 0.7f, point.z), Vector3.down);
            best = default;
            float distance = 1.4f;
            foreach (var surface in vehicleSurfaces)
            {
                if (surface == null || !surface.enabled || surface.isTrigger || !surface.gameObject.activeInHierarchy ||
                    !surface.Raycast(ray, out var hit, distance) || hit.normal.y < 0.7f) continue;
                best = hit;
                distance = hit.distance;
            }
            return best.collider != null;
        }

        void LeaveRoof(Transform rig)
        {
            rig.position -= Vector3.up * PlatformHeight;
            OnRoof = false;
            PlatformHeight = 0;
        }

        internal void ResetLocation(Transform rig)
        {
            if (OnRoof) LeaveRoof(rig);
            Cancel(rig);
        }

        void FollowLadder(Transform rig)
        {
            Vector3 currentBase = ladder.RungPoint(0, true);
            Quaternion currentHeading = Facing;
            if (currentBase != previousBase || currentHeading != previousHeading)
            {
                Quaternion turn = currentHeading * Quaternion.Inverse(previousHeading);
                rig.SetPositionAndRotation(currentBase + turn * (rig.position - previousBase), turn * rig.rotation);
                previousBase = currentBase;
                previousHeading = currentHeading;
            }
        }

        internal void ApplyPose(ref AvatarPose pose, float leftLift, float rightLift)
        {
            pose.Climb = Snapshot;
            if (!Holding) return;
            if (steps.HandRung(true) > 0) pose.LeftHand.position = ladder.RungPoint(steps.HandRung(true), true);
            if (steps.HandRung(false) > 0) pose.RightHand.position = ladder.RungPoint(steps.HandRung(false), false);
            Pose Shoe(bool left, float physicalLift)
            {
                Vector3 center = ladder.RungPoint(steps.FootRung(left), left) - ladder.FaceDirection * 0.06f;
                center.y += SeatedMotion.ShoeCenterHeight + (float.IsFinite(physicalLift) ? Mathf.Clamp(physicalLift, 0f, 0.35f) : 0f);
                return new Pose(center, Facing);
            }
            pose.LeftFoot = Shoe(true, leftLift);
            pose.RightFoot = Shoe(false, rightLift);
        }

        internal void Cancel(Transform rig)
        {
            if (attached) ReturnToGround(rig);
            waitForRelease = leftWasDown || rightWasDown;
            Feedback = LadderResult.None;
            NearbyRung = 0;
        }

        void ReturnToGround(Transform rig)
        {
            if (attached)
            {
                Vector3 p = rig.position;
                p.y = groundRigY;
                rig.position = p;
            }
            steps.Reset();
            attached = heldTop = false;
            elevation = 0f;
        }
    }
}
