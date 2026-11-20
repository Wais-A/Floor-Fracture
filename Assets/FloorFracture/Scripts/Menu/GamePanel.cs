using System;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.UI;

namespace FloorFracture
{
    public readonly struct MenuChoice
    {
        public string Label { get; }
        public Action OnSelect { get; }
        public MenuChoice(string label, Action onSelect) { Label = label; OnSelect = onSelect; }
    }

    [DefaultExecutionOrder(-40)]
    public sealed class GamePanel : MonoBehaviour
    {
        public const string CanvasName = "Floor Fracture Menu";
        private const float Width = 672, Margin = 32, RowHeight = 57, RowGap = 8;
        private static readonly Color Paper = new Color32(13, 32, 41, 249);
        private static readonly Color Idle = new Color32(35, 66, 76, 255);
        private static readonly Color Hovered = new Color32(55, 109, 116, 255);
        private static readonly Color Pressed = new Color32(119, 196, 183, 255);
        private static readonly Color Ink = new Color32(237, 247, 248, 255);
        private static readonly Color Secondary = new Color32(192, 216, 221, 255);

        private sealed class Row
        {
            internal RectTransform Rect;
            internal UnityEngine.UI.Image Fill;
            internal UnityEngine.UI.Text Text;
            internal UnityEngine.UI.Button Button;
        }

        private readonly List<Row> rows = new List<Row>();
        private readonly PanelController[] pointers = new PanelController[2];
        private MenuChoice[] choices = Array.Empty<MenuChoice>();
        private OVRCameraRig rig;
        private global::GameSound sound;
        private RectTransform panel;
        private Canvas menuCanvas;
#if UNITY_EDITOR
        private PointableCanvas pointable;
        private RayInteractable raySurface;
        public bool EditorSimulatesControllers => desktop != null && desktop.Active;
#endif
        private UnityEngine.UI.Text title, body;
        private Font font;
        private Camera eye;
        private bool wasTracked, paused;
        private int lastSelectionFrame = -1;
        private int mouseHover = -1, mousePress = -1;
#if UNITY_EDITOR
        private EditorPanelInput desktop;
#endif

        public bool Visible => panel && panel.gameObject.activeSelf;
        public Transform Head => rig ? rig.centerEyeAnchor : null;

        public bool Tracked
        {
            get
            {
                if (!Head || paused) return false;
#if UNITY_EDITOR
                if (desktop != null && desktop.Active) return true;
#endif
                return PanelController.HeadTracked();
            }
        }

        public Transform Grip(bool left)
        {
#if UNITY_EDITOR
            if (desktop != null && desktop.Active) return desktop.Grip(left);
#endif
            return !rig ? null : (left ? rig.leftHandAnchor : rig.rightHandAnchor);
        }

        public bool GripHeld(bool left)
        {
#if UNITY_EDITOR
            if (desktop != null && desktop.Active) return desktop.GripHeld(left);
#endif
            return Tracked && pointers[left ? 0 : 1].GripHeld;
        }

        public void Initialize(OVRCameraRig cameraRig, global::GameSound gameSound)
        {
            if (!cameraRig) throw new ArgumentNullException(nameof(cameraRig));
            if (panel) return;
            rig = cameraRig;
            sound = gameSound;
            eye = Head ? Head.GetComponent<Camera>() : Camera.main;
#if UNITY_EDITOR
            desktop = new EditorPanelInput(rig, transform);
            desktop.UpdateMode();
#endif
            // This built-in dynamic font is included in the player without a TMP Essentials import.
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var authored = transform.Find(CanvasName);
            GameObject go = authored ? authored.gameObject : null;
#if UNITY_EDITOR
            // A bare no-XR fixture can still use the keyboard and mouse during scene authoring.
            if (!go && desktop.Active)
            {
                go = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                go.transform.SetParent(transform, false);
            }
#endif
            if (!go) throw new InvalidOperationException("Install the Meta ray canvas with MenuSceneSetup before building this scene.");
            panel = (RectTransform)go.transform;
            // A 39 cm panel at 56 cm keeps the original angular size and puts buttons within reach.
            panel.localScale = Vector3.one * 0.00058f;
            menuCanvas = go.GetComponent<Canvas>();
            menuCanvas.renderMode = RenderMode.WorldSpace;
            menuCanvas.worldCamera = eye;
            menuCanvas.sortingOrder = 12;
#if UNITY_EDITOR
            raySurface = go.GetComponentInChildren<RayInteractable>(true);
            if (raySurface) pointable = raySurface.GetComponent<PointableCanvas>();
#endif
            var background = go.AddComponent<UnityEngine.UI.Image>();
            background.color = Paper;
            background.raycastTarget = false;
            title = Label(panel, "Title", 46, FontStyle.Bold, Ink);
            body = Label(panel, "Instructions", 26, FontStyle.Normal, Secondary);
            pointers[0] = new PanelController(transform, true);
            pointers[1] = new PanelController(transform, false);
            Hide();
        }

        public void Show(string heading, string message, params MenuChoice[] options)
        {
            bool position = !Visible;
            choices = options ?? Array.Empty<MenuChoice>();
            panel.gameObject.SetActive(true);
            if (position) ClearPointers();
            title.text = heading ?? "";
            for (int i = rows.Count; i < choices.Length; ++i) BuildRow(i);
            for (int i = 0; i < rows.Count; ++i)
            {
                bool use = i < choices.Length;
                rows[i].Rect.gameObject.SetActive(use);
                if (use) rows[i].Text.text = choices[i].Label;
            }
            SetBody(message);
            Paint();
            if (position || (Head && Vector3.Dot(Head.forward, panel.position - Head.position) < 0))
                Recenter();
        }

        public void SetBody(string message)
        {
            body.text = message ?? "";
            Place(title.rectTransform, Margin, Margin - 6, Width - 2 * Margin, 61);
            Place(body.rectTransform, Margin, 93, Width - 2 * Margin, 40);
            float bodyHeight = Mathf.Max(30, Mathf.Ceil(body.preferredHeight) + 4);
            Place(body.rectTransform, Margin, 93, Width - 2 * Margin, bodyHeight);
            float top = 93 + bodyHeight + (choices.Length > 0 ? 23 : 0);
            for (int i = 0; i < choices.Length; ++i)
                Place(rows[i].Rect, Margin, top + i * (RowHeight + RowGap), Width - 2 * Margin, RowHeight);
            float height = top + choices.Length * (RowHeight + RowGap) - (choices.Length > 0 ? RowGap : 0) + Margin;
            panel.sizeDelta = new Vector2(Width, height);
        }

        public void Hide()
        {
            ClearPointers();
            if (panel) panel.gameObject.SetActive(false);
        }

        public void Recenter()
        {
            if (!Head || !panel) return;
            Vector3 forward = Vector3.ProjectOnPlane(Head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(rig.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            Vector3 pos = Head.position + forward.normalized * .56f + Vector3.down * .07f;
            panel.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - Head.position, Vector3.up));
        }

        private UnityEngine.UI.Text Label(Transform parent, string name, int size, FontStyle style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<UnityEngine.UI.Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.alignment = TextAnchor.UpperLeft;
            text.raycastTarget = false;
            return text;
        }

        private void BuildRow(int index)
        {
            var go = new GameObject("Choice " + (index + 1), typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
            go.transform.SetParent(panel, false);
            var row = new Row
            {
                Rect = (RectTransform)go.transform,
                Fill = go.GetComponent<UnityEngine.UI.Image>(),
                Button = go.GetComponent<UnityEngine.UI.Button>()
            };
            row.Fill.raycastTarget = true;
            row.Button.targetGraphic = row.Fill;
            row.Button.transition = Selectable.Transition.None;
            row.Button.navigation = new Navigation { mode = Navigation.Mode.None };
            row.Button.onClick.AddListener(() =>
            {
                if (!Visible || index >= choices.Length) return;
                if (!Tracked) return;
#if UNITY_EDITOR
                if (!editorDirectChoice && lastSelectionFrame == Time.frameCount) return;
#else
                if (lastSelectionFrame == Time.frameCount) return;
#endif
                lastSelectionFrame = Time.frameCount;
                if (sound) sound.Click();
                choices[index].OnSelect?.Invoke();
            });
            row.Text = Label(go.transform, "Label", 28, FontStyle.Bold, Ink);
            row.Text.alignment = TextAnchor.MiddleLeft;
            Place(row.Text.rectTransform, 19, 0, Width - 2 * Margin - 35, RowHeight);
            rows.Add(row);
        }

        private static void Place(RectTransform item, float x, float top, float width, float height)
        {
            item.anchorMin = item.anchorMax = item.pivot = new Vector2(0, 1);
            item.anchoredPosition = new Vector2(x, -top);
            item.sizeDelta = new Vector2(width, height);
        }

        private void Update()
        {
            if (!panel) return;
            foreach (var pointer in pointers) pointer.EndPulse();
#if UNITY_EDITOR
            desktop.UpdateMode();
            if (desktop.Active) desktop.Move();
#endif
            bool tracked = Tracked;
            if (!Visible) { wasTracked = tracked; return; }
            if (!tracked) { ClearPointers(); wasTracked = false; Paint(); return; }
            if (!wasTracked) Recenter();
            wasTracked = true;
#if UNITY_EDITOR
            if (desktop.Active)
            {
                if (desktop.Override && pointable && raySurface && raySurface.Surface != null)
                    for (int i = 0; i < 2; ++i)
                    {
                        Transform hand = desktop.Grip(i == 0);
                        pointers[i].EditorRay(new Ray(hand.position, hand.forward), desktop.TriggerHeld(i == 0), raySurface, pointable);
                    }
                if (UpdateDesktopMenu()) return;
                Paint();
                return;
            }
#endif
            // ISDK ray and poke interactors dispatch to the same world-space uGUI buttons.
        }

        private void OnEnable()
        {
            PointableCanvasModule.WhenSelectableHovered += MetaHover;
            PointableCanvasModule.WhenSelectableUnhovered += MetaUnhover;
            PointableCanvasModule.WhenSelected += MetaPress;
            PointableCanvasModule.WhenUnselected += MetaRelease;
        }

        private void MetaHover(PointableCanvasEventArgs args) => PointerFeedback(args, true, true);
        private void MetaUnhover(PointableCanvasEventArgs args) => PointerFeedback(args, true, false);
        private void MetaPress(PointableCanvasEventArgs args) => PointerFeedback(args, false, true);
        private void MetaRelease(PointableCanvasEventArgs args) => PointerFeedback(args, false, false);

        private void PointerFeedback(PointableCanvasEventArgs args, bool hover, bool active)
        {
            if (!Visible || args.Canvas != menuCanvas || !args.PointerId.HasValue) return;
            int id = args.PointerId.Value;
            int side;
            bool haptics = false;
#if UNITY_EDITOR
            if (desktop.Active && (id == PanelController.EditorLeftId || id == PanelController.EditorRightId))
                side = id == PanelController.EditorLeftId ? 0 : 1;
            else
#endif
            {
                if (!UniqueIdentifier.TryGetInstanceFromIdentifier<Component>(Context.Global.GetInstance(), id, out var source)
                    || (!(source is RayInteractor) && !(source is PokeInteractor))) return;
                var controller = source.GetComponentInParent<IController>();
                var hand = controller == null ? source.GetComponentInParent<IHand>() : null;
                if (controller == null && hand == null) return;
                side = (controller?.Handedness ?? hand.Handedness) == Handedness.Left ? 0 : 1;
                haptics = controller != null;
            }
            int index = rows.FindIndex(r => r.Rect.gameObject == args.Hovered);
            var pointer = pointers[side];
            if (hover) pointer.Hover = active ? index : -1;
            else pointer.Pressed = active ? index : -1;
            if (active && index >= 0 && haptics) pointer.Pulse(hover ? 0.12f : 0.32f, hover ? 0.018f : 0.048f);
            Paint();
        }

#if UNITY_EDITOR
        private int Hit(Ray ray)
        {
            var plane = new Plane(panel.forward, panel.position);
            if (Vector3.Dot(ray.direction, panel.forward) <= 0.01f || !plane.Raycast(ray, out float t) || t > 4f)
                return -1;
            Vector3 point = ray.GetPoint(t);
            if (!panel.rect.Contains(panel.InverseTransformPoint(point))) return -1;
            for (int i = 0; i < choices.Length; ++i)
                if (rows[i].Rect.rect.Contains(rows[i].Rect.InverseTransformPoint(point))) return i;
            return -1;
        }

        private bool Select(int index)
        {
            if (!Visible || index < 0 || index >= choices.Length) return false;
            rows[index].Button.onClick.Invoke();
            return true;
        }
#endif

        private void Paint()
        {
            for (int i = 0; i < choices.Length; ++i)
            {
                bool down = mousePress == i || pointers[0].Pressed == i || pointers[1].Pressed == i;
                bool over = mouseHover == i || pointers[0].Hover == i || pointers[1].Hover == i;
                rows[i].Fill.color = down ? Pressed : over ? Hovered : Idle;
                rows[i].Text.color = down ? Paper : Ink;
            }
        }

        private void ClearPointers()
        {
            mouseHover = mousePress = -1;
            foreach (var pointer in pointers) pointer?.Clear();
        }

        private void OnApplicationPause(bool value)
        {
            paused = value;
            if (paused) foreach (var pointer in pointers) pointer?.EndPulse(true);
        }
        private void OnDisable()
        {
            PointableCanvasModule.WhenSelectableHovered -= MetaHover;
            PointableCanvasModule.WhenSelectableUnhovered -= MetaUnhover;
            PointableCanvasModule.WhenSelected -= MetaPress;
            PointableCanvasModule.WhenUnselected -= MetaRelease;
            ClearPointers();
            foreach (var pointer in pointers) pointer?.EndPulse(true);
        }

#if UNITY_EDITOR
        private bool editorDirectChoice;
        private bool UpdateDesktopMenu()
        {
            int number = desktop.NumberPressed();
            if (number >= 0 && Select(number)) return true;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            mouseHover = mouse != null && eye ? Hit(eye.ScreenPointToRay(mouse.position.ReadValue())) : -1;
            if (mouse == null) return false;
            if (mouse.leftButton.wasPressedThisFrame) mousePress = mouseHover;
            if (!mouse.leftButton.wasReleasedThisFrame) return false;
            int clicked = mousePress;
            mousePress = -1;
            return clicked == mouseHover && Select(clicked);
        }

        public bool EditorChoose(string labelPrefix)
        {
            if (!Visible || string.IsNullOrEmpty(labelPrefix)) return false;
            editorDirectChoice = true;
            try
            {
                for (int i = 0; i < choices.Length; ++i)
                    if (choices[i].Label.StartsWith(labelPrefix, StringComparison.OrdinalIgnoreCase)) return Select(i);
                return false;
            }
            finally { editorDirectChoice = false; }
        }

        public void EditorSetPose(Pose head, Pose leftGrip, Pose rightGrip, bool leftHeld, bool rightHeld) =>
            desktop.SetPose(head, leftGrip, rightGrip, leftHeld, rightHeld);

        public void EditorSetTriggers(bool left, bool right) => desktop.SetTriggers(left, right);

        private void OnDestroy() { desktop?.Dispose(); }
#endif
    }
}
