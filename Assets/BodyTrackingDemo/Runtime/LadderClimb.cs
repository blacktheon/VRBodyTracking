using System;

namespace Blacktheon.BodyTracking.Demo
{
    internal enum LadderResult { None, Legal, Fell }

    internal sealed class LadderClimb
    {
        enum StepKind { Hand, Foot }
        enum FootPhase { AwaitingFloor, Grounded, Raised }

        const float LiftHeight = 0.08f;
        const float FloorHeight = 0.03f;
        readonly int[] hands = new int[2];
        readonly int[] feet = new int[2];
        readonly FootPhase[] phases = new FootPhase[2];
        int highestHandRung;
        StepKind lastStep;
        int consecutiveSteps;

        internal bool Holding => hands[0] != 0 || hands[1] != 0;
        internal int HandRung(bool left) => hands[left ? 0 : 1];
        internal int FootRung(bool left) => feet[left ? 0 : 1];

        internal LadderResult Grab(bool left, int rung, int rungCount)
        {
            int hand = left ? 0 : 1;
            if (hands[hand] != 0 || rung < 1 || rung > rungCount)
                return LadderResult.None;

            int support = hands[1 - hand];
            if (support == 0)
            {
                hands[hand] = highestHandRung = rung;
                return LadderResult.None;
            }
            if (Math.Abs(rung - support) > 1)
                return LadderResult.None;

            hands[hand] = rung;
            if (rung <= highestHandRung)
                return LadderResult.None;
            highestHandRung = rung;
            return CountStep(StepKind.Hand);
        }

        internal void Release(bool left)
        {
            hands[left ? 0 : 1] = 0;
            if (!Holding) CancelFootGestures();
        }

        internal LadderResult SampleFeet(byte fresh, float leftLift, float rightLift, int rungCount)
        {
            if (!Holding) return LadderResult.None;

            bool leftLanded = Landed(0, (fresh & 1) != 0, leftLift);
            bool rightLanded = Landed(1, (fresh & 2) != 0, rightLift);
            int moving;
            if (leftLanded && feet[0] <= feet[1] && feet[1] < rungCount) moving = 0;
            else if (rightLanded && feet[1] <= feet[0] && feet[0] < rungCount) moving = 1;
            else return LadderResult.None;

            int other = 1 - moving;
            feet[moving] = feet[other] + 1;
            // The next foot must start from the physical floor after this landing.
            if ((other == 0 ? leftLift : rightLift) > FloorHeight)
                phases[other] = FootPhase.AwaitingFloor;
            return CountStep(StepKind.Foot);
        }

        bool Landed(int foot, bool fresh, float lift)
        {
            if (!fresh || float.IsNaN(lift) || float.IsInfinity(lift))
                phases[foot] = FootPhase.AwaitingFloor;
            else if (lift <= FloorHeight)
            {
                bool landed = phases[foot] == FootPhase.Raised;
                phases[foot] = FootPhase.Grounded;
                return landed;
            }
            else if (lift >= LiftHeight && phases[foot] == FootPhase.Grounded)
                phases[foot] = FootPhase.Raised;
            return false;
        }

        LadderResult CountStep(StepKind kind)
        {
            consecutiveSteps = kind == lastStep ? consecutiveSteps + 1 : 1;
            lastStep = kind;
            if (consecutiveSteps <= 2) return LadderResult.Legal;
            Reset();
            return LadderResult.Fell;
        }

        internal void CancelFootGestures()
        {
            phases[0] = phases[1] = FootPhase.AwaitingFloor;
        }

        internal void Reset()
        {
            hands[0] = hands[1] = 0;
            feet[0] = feet[1] = 0;
            highestHandRung = consecutiveSteps = 0;
            CancelFootGestures();
        }
    }
}
