using UnityEngine;
using UnityEngine.XR;
#if UNITY_EDITOR
using Oculus.Interaction;
using UnityEngine.Rendering;
#endif

namespace FloorFracture
{
    internal sealed class PanelController
    {
        internal int Hover = -1;
        internal int Pressed = -1;
        private readonly bool left;
        private double stopVibrationAt = double.PositiveInfinity;

        internal PanelController(Transform parent, bool left)
        {
            this.left = left;
#if UNITY_EDITOR
            editorRoot = parent;
#endif
        }

        private OVRInput.Controller Device => left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
        private XRNode Node => left ? XRNode.LeftHand : XRNode.RightHand;
        internal bool GripHeld => OVRInput.Get(left ? OVRInput.RawButton.LHandTrigger : OVRInput.RawButton.RHandTrigger, Device);

        internal static bool HeadTracked()
        {
            // Window focus only means headset focus on the Quest. In the Editor the Meta XR Simulator's
            // own window holds focus while it drives the head, so require it only on the device.
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Application.isFocused) return false;
#endif
            if (!OVRManager.instance || !OVRManager.hasInputFocus || !OVRManager.instance.isUserPresent) return false;
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            bool positioned = head.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState state) &&
                (state & InputTrackingState.Position) != 0;
            return positioned || (OVRManager.tracker != null && OVRManager.tracker.isPositionTracked);
        }

        internal void Pulse(float amplitude, float duration)
        {
            var controller = InputDevices.GetDeviceAtXRNode(Node);
            if (controller.isValid && controller.SendHapticImpulse(0, amplitude, duration)) return;
            OVRInput.SetControllerVibration(0.65f, amplitude, Device);
            stopVibrationAt = Time.unscaledTimeAsDouble + duration;
        }

        internal void EndPulse(bool immediately = false)
        {
            if (double.IsPositiveInfinity(stopVibrationAt) || (!immediately && Time.unscaledTimeAsDouble < stopVibrationAt)) return;
            OVRInput.SetControllerVibration(0, 0, Device);
            stopVibrationAt = double.PositiveInfinity;
        }

        internal void Clear()
        {
            Hover = Pressed = -1;
#if UNITY_EDITOR
            EndEditorHover();
            if (line) line.enabled = false;
#endif
        }

#if UNITY_EDITOR
        // The SDK creates positive IDs. These two belong only to the no-headset test poses.
        internal const int EditorLeftId = -37901, EditorRightId = -37902;
        private readonly Transform editorRoot;
        private PointableCanvas editorTarget;
        private bool wasHeld;
        private Pose lastHit;
        private LineRenderer line;

        internal void EditorRay(Ray ray, bool held, RayInteractable surface, PointableCanvas target)
        {
            const float limit = 4f;
            bool hits = surface.Raycast(ray, out var hit, limit, false);
            DrawEditorRay(ray, hits ? hit.Distance : 1.5f);
            if (!hits)
            {
                EndEditorHover();
                return;
            }
            lastHit = new Pose(hit.Point, Quaternion.LookRotation(hit.Normal));
            if (target != editorTarget)
            {
                EndEditorHover();
                editorTarget = target;
                Send(PointerEventType.Hover);
            }
            Send(PointerEventType.Move);
            if (held != wasHeld) Send(held ? PointerEventType.Select : PointerEventType.Unselect);
            wasHeld = held;
        }

        private void EndEditorHover()
        {
            if (editorTarget && editorTarget.isActiveAndEnabled)
            {
                if (wasHeld) Send(PointerEventType.Cancel);
                Send(PointerEventType.Unhover);
            }
            editorTarget = null;
            wasHeld = false;
        }

        private void Send(PointerEventType type)
        {
            editorTarget.ProcessPointerEvent(new PointerEvent(left ? EditorLeftId : EditorRightId, type, lastHit));
        }

        private void DrawEditorRay(Ray ray, float distance)
        {
            if (!line)
            {
                var go = new GameObject(left ? "Editor left menu ray" : "Editor right menu ray");
                go.transform.SetParent(editorRoot, false);
                line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.startWidth = 0.002f;
                line.endWidth = 0.004f;
                line.numCapVertices = 3;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.sharedMaterial = UnityEngine.UI.Graphic.defaultGraphicMaterial;
                line.startColor = line.endColor = left ? new Color(0.51f, 0.94f, 0.91f) : new Color(1.0f, 0.78f, 0.49f);
            }
            line.enabled = true;
            line.SetPosition(0, ray.origin);
            line.SetPosition(1, ray.GetPoint(Mathf.Max(0, distance - 0.004f)));
        }
#endif
    }
}
