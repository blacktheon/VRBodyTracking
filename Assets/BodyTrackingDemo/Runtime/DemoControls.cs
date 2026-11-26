using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using Usages = UnityEngine.XR.CommonUsages;

namespace Blacktheon.BodyTracking.Demo
{
    internal sealed class DemoControls
    {
        internal Vector2 Move { get; private set; }
        internal float Turn { get; private set; }
        internal Vector2Int Navigate { get; private set; }
        internal bool Confirm { get; private set; }
        internal bool Calibrate { get; private set; }
        internal bool Back { get; private set; }
        internal bool LeftGrip { get; private set; }
        internal bool RightGrip { get; private set; }
        bool wasConfirm, wasCalibrate, wasBack, holdingStick;

        internal void Read()
        {
            var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            LeftGrip = ReadGrip(left, XRController.leftHand, LeftGrip);
            RightGrip = ReadGrip(right, XRController.rightHand, RightGrip);
            if (!left.TryGetFeatureValue(Usages.primary2DAxis, out var move))
                move = XRController.leftHand?.TryGetChildControl<Vector2Control>("thumbstick")?.ReadValue() ?? Vector2.zero;
            if (!right.TryGetFeatureValue(Usages.primary2DAxis, out var turn))
                turn = XRController.rightHand?.TryGetChildControl<Vector2Control>("thumbstick")?.ReadValue() ?? Vector2.zero;
            if (!right.TryGetFeatureValue(Usages.primaryButton, out bool confirm))
                confirm = XRController.rightHand?.TryGetChildControl<ButtonControl>("primaryButton")?.isPressed ?? false;
            if (!left.TryGetFeatureValue(Usages.primaryButton, out bool calibrate))
                calibrate = XRController.leftHand?.TryGetChildControl<ButtonControl>("primaryButton")?.isPressed ?? false;
            if (!right.TryGetFeatureValue(Usages.secondaryButton, out bool back))
                back = XRController.rightHand?.TryGetChildControl<ButtonControl>("secondaryButton")?.isPressed ?? false;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed) move.y = 1;
                if (keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed) move.y = -1;
                if (keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed) move.x = -1;
                if (keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed) move.x = 1;
                if (keyboard.qKey.isPressed) turn.x = -1;
                if (keyboard.eKey.isPressed) turn.x = 1;
                confirm |= keyboard.enterKey.isPressed;
                calibrate |= keyboard.cKey.isPressed;
                back |= keyboard.escapeKey.isPressed;
            }
            Move = move.magnitude > 0.20f ? move : Vector2.zero;
            Turn = Mathf.Abs(turn.x) > 0.20f ? turn.x : 0;
            Confirm = confirm && !wasConfirm;
            Calibrate = calibrate && !wasCalibrate;
            Back = back && !wasBack;
            wasConfirm = confirm;
            wasCalibrate = calibrate;
            wasBack = back;
            Navigate = Vector2Int.zero;
            if (move.magnitude < 0.40f) holdingStick = false;
            else if (!holdingStick && move.magnitude > 0.68f)
            {
                holdingStick = true;
                Navigate = Mathf.Abs(move.x) > Mathf.Abs(move.y)
                    ? new Vector2Int(move.x > 0 ? 1 : -1, 0)
                    : new Vector2Int(0, move.y > 0 ? 1 : -1);
            }
        }

        static bool ReadGrip(UnityEngine.XR.InputDevice device, XRController controller, bool held)
        {
            if (device.TryGetFeatureValue(Usages.isTracked, out bool tracked) && !tracked) return false;
            float threshold = held ? 0.35f : 0.60f;
            if (device.TryGetFeatureValue(Usages.grip, out float pressure)) return pressure >= threshold;
            if (device.TryGetFeatureValue(Usages.gripButton, out bool pressed)) return pressed;
            var axis = controller?.TryGetChildControl<AxisControl>("grip");
            if (axis != null) return axis.ReadValue() >= threshold;
            return controller?.TryGetChildControl<ButtonControl>("gripPressed")?.isPressed ?? false;
        }
    }
}
