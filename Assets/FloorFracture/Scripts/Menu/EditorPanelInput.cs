#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace FloorFracture
{
    // Desktop controls exist only in the Editor. Once an XR display starts, a headset
    // disconnect always takes the normal tracking-loss path for the remainder of play.
    internal sealed class EditorPanelInput
    {
        private readonly OVRCameraRig rig;
        private readonly Transform parent;
        private readonly Transform[] grips = new Transform[2];
        private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        private static readonly Key[] Digits = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6 };
        private bool sawXr, rigWasEnabled, heldLeft, heldRight, triggerLeft, triggerRight;
        private Vector3 originalHeadPosition;
        private Quaternion originalHeadRotation;
        private double probeAt;
        private float yaw, pitch;
        internal bool Active { get; private set; }
        internal bool Override { get; private set; }

        internal EditorPanelInput(OVRCameraRig rig, Transform parent)
        {
            this.rig = rig;
            this.parent = parent;
        }

        internal void UpdateMode()
        {
            if (Override || sawXr || !rig || !rig.centerEyeAnchor || Time.unscaledTimeAsDouble < probeAt) return;
            probeAt = Time.unscaledTimeAsDouble + 0.4;
            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays)
            {
                if (!display.running) continue;
                sawXr = true;
                ReleaseRig();
                return;
            }
            AcquireRig();
        }

        private void AcquireRig()
        {
            if (Active) return;
            Active = true;
            var head = rig.centerEyeAnchor;
            originalHeadPosition = head.localPosition;
            originalHeadRotation = head.localRotation;
            rigWasEnabled = rig.enabled;
            rig.enabled = false;
            Vector3 position = head.position;
            position.y = (rig.trackingSpace ? rig.trackingSpace.position.y : rig.transform.position.y) + 1.60f;
            yaw = head.eulerAngles.y;
            pitch = 0;
            head.SetPositionAndRotation(position, Quaternion.Euler(pitch, yaw, 0));
            for (int i = 0; i < grips.Length; ++i)
            {
                if (grips[i]) continue;
                grips[i] = new GameObject(i == 0 ? "Editor left grip" : "Editor right grip").transform;
                grips[i].SetParent(parent, false);
            }
            PositionHands();
        }

        internal Transform Grip(bool left) => grips[left ? 0 : 1];
        internal bool TriggerHeld(bool left) => left ? triggerLeft : triggerRight;

        internal bool GripHeld(bool left)
        {
            if (Override) return left ? heldLeft : heldRight;
            var keyboard = Keyboard.current;
            return keyboard != null && (left ? keyboard.qKey.isPressed : keyboard.eKey.isPressed);
        }

        internal int NumberPressed()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
                for (int i = 0; i < Digits.Length; ++i)
                    if (keyboard[Digits[i]].wasPressedThisFrame) return i;
            return -1;
        }

        internal void Move()
        {
            if (Override) return;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            Vector2 walk = Vector2.zero;
            if (keyboard != null)
            {
                walk.x = Axis(keyboard.dKey.isPressed, keyboard.aKey.isPressed);
                walk.y = Axis(keyboard.wKey.isPressed, keyboard.sKey.isPressed);
                yaw += dt * 78 * Axis(keyboard.rightArrowKey.isPressed, keyboard.leftArrowKey.isPressed);
                pitch += dt * 78 * Axis(keyboard.downArrowKey.isPressed, keyboard.upArrowKey.isPressed);
            }
            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue() * 0.14f;
                yaw += delta.x;
                pitch -= delta.y;
            }
            pitch = Mathf.Clamp(pitch, -80, 85);
            Transform head = rig.centerEyeAnchor;
            Quaternion flat = Quaternion.Euler(0, yaw, 0);
            Vector2 step = Vector2.ClampMagnitude(walk, 1) * (0.95f * dt);
            head.SetPositionAndRotation(head.position + flat * new Vector3(step.x, 0, step.y), Quaternion.Euler(pitch, yaw, 0));
            PositionHands();
        }

        private static int Axis(bool positive, bool negative) => (positive ? 1 : 0) - (negative ? 1 : 0);

        private void PositionHands()
        {
            Transform head = rig.centerEyeAnchor;
            Quaternion flat = Quaternion.Euler(0, yaw, 0);
            for (int i = 0; i < grips.Length; ++i)
            {
                Vector3 offset = new Vector3(i == 0 ? -0.1f : 0.1f, -0.27f, 0.34f);
                grips[i].SetPositionAndRotation(head.position + flat * offset, head.rotation);
            }
        }

        internal void SetPose(Pose head, Pose left, Pose right, bool leftHeld, bool rightHeld)
        {
            AcquireRig();
            Override = true;
            rig.centerEyeAnchor.SetPositionAndRotation(head.position, head.rotation);
            grips[0].SetPositionAndRotation(left.position, left.rotation);
            grips[1].SetPositionAndRotation(right.position, right.rotation);
            heldLeft = leftHeld;
            heldRight = rightHeld;
        }

        internal void SetTriggers(bool left, bool right)
        {
            triggerLeft = left;
            triggerRight = right;
        }

        private void ReleaseRig()
        {
            if (!Active) return;
            Active = false;
            if (rig && rig.centerEyeAnchor)
            {
                rig.centerEyeAnchor.SetLocalPositionAndRotation(originalHeadPosition, originalHeadRotation);
                rig.enabled = rigWasEnabled;
            }
        }

        internal void Dispose()
        {
            ReleaseRig();
            foreach (Transform grip in grips)
                if (grip) Object.Destroy(grip.gameObject);
        }
    }
}
#endif
