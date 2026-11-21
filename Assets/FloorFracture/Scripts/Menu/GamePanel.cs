using System;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

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
        private const float Width = 672, Margin = 32, RowHeight = 72, RowGap = 14;
        private const float WarningPreviewSize = 144;
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
            internal MenuPageButton Button;
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
        private Canvas countCanvas;
        private UnityEngine.UI.Text countLabel;
        private static readonly string[] StartCounts = { "GO!", "1", "2", "3" };
        private GlassTile warningPreview;
        private bool previewGentle;
        private double previewStarted;
        private bool roomRecovery;
        private Font font;
        private Camera eye;
        private OVRDisplay recenterDisplay;
        private XRInputSubsystem originSubsystem;
        private int recenterRequestedFrame = -1;
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
            // The physical row is 4.2 cm tall, with 8 mm between adjacent actions.
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
            ShowPage(heading, message, options, false);
        }

        public void ShowStartCount(int count, float opacity = 1f)
        {
            if (!countCanvas)
            {
                var go = new GameObject("Start count", typeof(RectTransform), typeof(Canvas));
                var rect = (RectTransform)go.transform;
                rect.SetParent(Head, false);
                rect.localPosition = new Vector3(0, .12f, .95f);
                rect.localScale = Vector3.one * .001f;
                rect.sizeDelta = new Vector2(360, 150);
                countCanvas = go.GetComponent<Canvas>();
                countCanvas.renderMode = RenderMode.WorldSpace;
                countCanvas.worldCamera = eye;
                countCanvas.sortingOrder = 30;
                countLabel = Label(rect, "Count", 100, FontStyle.Bold, Ink);
                countLabel.rectTransform.sizeDelta = rect.sizeDelta;
                countLabel.alignment = TextAnchor.MiddleCenter;
                var outline = countLabel.gameObject.AddComponent<UnityEngine.UI.Outline>();
                outline.effectColor = new Color(0, .05f, .07f, .95f);
                outline.effectDistance = new Vector2(3, -3);
            }
            countLabel.text = StartCounts[count];
            var color = Ink;
            color.a = opacity;
            countLabel.color = color;
            countCanvas.gameObject.SetActive(true);
        }

        public void HideStartCount()
        {
            if (countCanvas) countCanvas.gameObject.SetActive(false);
        }

        public void ShowRoomRecovery(string message, Action checkAgain, Action openSpaceSetup, Action togglePlacement = null)
        {
            var options = new List<MenuChoice> { new MenuChoice("Check again", checkAgain), new MenuChoice("Space Setup", openSpaceSetup) };
            if (togglePlacement != null) options.Add(new MenuChoice("Show / hide placement", togglePlacement));
            ShowPage("Find a clear floor", message + "\nCheck again uses your saved scan. Space Setup opens the Quest scanner, where you can use a Touch controller.", options.ToArray(), true);
        }

        private void ShowPage(string heading, string message, MenuChoice[] options, bool isRoomRecovery)
        {
            bool position = !Visible;
            bool updatingRecovery = isRoomRecovery && roomRecovery && Visible && choices.Length == options.Length;
            roomRecovery = isRoomRecovery;
            choices = options ?? Array.Empty<MenuChoice>();
            if (updatingRecovery)
            {
                // Tracking can change the explanation while a hand is hovering or pressing the same action.
                SetBody(message);
                return;
            }
            if (warningPreview) warningPreview.gameObject.SetActive(false);
            panel.gameObject.SetActive(true);
            ClearPointers();
            title.text = heading ?? "";
            for (int i = rows.Count; i < choices.Length; ++i) BuildRow(i);
            for (int i = 0; i < rows.Count; ++i)
            {
                rows[i].Button.ClearPendingPresses();
                bool use = i < choices.Length;
                rows[i].Rect.gameObject.SetActive(use);
                if (use) rows[i].Text.text = choices[i].Label;
            }
            SetBody(message);
            Paint();
            var center = eye ? eye.WorldToViewportPoint(panel.position) : Vector3.zero;
            if (position || center.z <= 0 || center.x < 0 || center.x > 1 || center.y < 0 || center.y > 1)
                Recenter();
        }

        public void ShowWarningPreview(bool gentle)
        {
            if (!warningPreview)
            {
                // Keep the real pane and its branching cracks beside the Settings title,
                // in reach even when the wearer opens the menu from outside the board.
                var sample = new GameObject("Warning preview");
                sample.transform.SetParent(title.rectTransform, false);
                sample.transform.localPosition = new Vector3(title.rectTransform.rect.xMax - WarningPreviewSize / 2f,
                    -WarningPreviewSize / 2f, -2f);
                sample.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                sample.transform.localScale = Vector3.one * (WarningPreviewSize / GlassGeometry.AuthoredPitch);
                warningPreview = sample.AddComponent<GlassTile>();
                warningPreview.Initialize();
                foreach (var renderer in sample.GetComponentsInChildren<MeshRenderer>())
                    renderer.sortingOrder = menuCanvas.sortingOrder + 1;
            }
            previewGentle = gentle;
            previewStarted = Time.unscaledTimeAsDouble;
            warningPreview.gameObject.SetActive(true);
            warningPreview.ShowWarning(true, 0, gentle);
            SetBody(body.text);
        }

        internal Action RememberPage()
        {
            string heading=title.text, message=body.text;
            var buttons=choices;
            return () => Show(heading,message,buttons);
        }

        public void SetBody(string message)
        {
            body.text = message ?? "";
            Place(title.rectTransform, Margin, Margin - 6, Width - 2 * Margin, 61);
            bool showPreview = warningPreview && warningPreview.gameObject.activeSelf;
            float bodyWidth = Width - 2 * Margin - (showPreview ? WarningPreviewSize + 20 : 0);
            Place(body.rectTransform, Margin, 93, bodyWidth, 40);
            float bodyHeight = Mathf.Max(30, Mathf.Ceil(body.preferredHeight) + 4);
            Place(body.rectTransform, Margin, 93, bodyWidth, bodyHeight);
            float headerBottom = Mathf.Max(93 + bodyHeight, showPreview ? Margin - 6 + WarningPreviewSize : 0);
            float top = headerBottom + (choices.Length > 0 ? 23 : 0);
            for (int i = 0; i < choices.Length; ++i)
                Place(rows[i].Rect, Margin, top + i * (RowHeight + RowGap), Width - 2 * Margin, RowHeight);
            float height = top + choices.Length * (RowHeight + RowGap) - (choices.Length > 0 ? RowGap : 0) + Margin;
            // On the recovery page the first button's top edge stays at the panel origin.
            // Additional lines of tracking status expand the panel above a resting hand.
            panel.pivot = new Vector2(.5f, roomRecovery ? (height - top) / height : .5f);
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
            // The native rig can report tracking before its first floor-relative eye pose arrives.
            var manager = OVRManager.instance ? OVRManager.instance : rig.GetComponent<OVRManager>();
            var origin = manager ? manager.trackingOriginType : OVRManager.TrackingOrigin.EyeLevel;
            bool floorOrigin = origin == OVRManager.TrackingOrigin.FloorLevel || origin == OVRManager.TrackingOrigin.Stage;
            if (!Tracked || (floorOrigin && Mathf.Abs(Head.localPosition.y) < .001f))
            {
                RequestMenuRecenter();
                return;
            }
            Vector3 forward = Vector3.ProjectOnPlane(Head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.Cross(Head.right, Vector3.up);
            // Keep the panel in poke range, low enough to aim from chest height with a relaxed wrist.
            Vector3 pos = Head.position + forward.normalized * .56f + Vector3.down * .24f;
            panel.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - Head.position, Vector3.up));
        }

        private void SetRecenterSources(OVRDisplay display, XRInputSubsystem input)
        {
            if (display != recenterDisplay)
            {
                if (recenterDisplay != null) recenterDisplay.RecenteredPose -= RequestMenuRecenter;
                recenterDisplay = display;
                if (display != null) display.RecenteredPose += RequestMenuRecenter;
            }
            if (input != originSubsystem)
            {
                if (originSubsystem != null) originSubsystem.trackingOriginUpdated -= OnOriginUpdated;
                originSubsystem = input;
                if (input != null) input.trackingOriginUpdated += OnOriginUpdated;
            }
        }

        private void RequestMenuRecenter() => recenterRequestedFrame = Time.frameCount;
        private void OnOriginUpdated(XRInputSubsystem input) => RequestMenuRecenter();

        private void OnInputFocusAcquired()
        {
            SetRecenterSources(OVRManager.display, OVRManager.GetCurrentInputSubsystem());
            RequestMenuRecenter();
        }

        private void LateUpdate()
        {
            // Meta can report the same reset through the display and the XR input origin.
            // Read the center eye after a full frame of camera tracking and MRUK world locking.
            if (recenterRequestedFrame < 0 || Time.frameCount <= recenterRequestedFrame) return;
            if (Visible && !Tracked) return;
            recenterRequestedFrame = -1;
            if (!Visible) return;
            Recenter();
            if (Debug.isDebugBuild)
                Debug.Log($"[Floor Fracture] Centered menu '{title.text}' at {panel.position:F2}, head {Head.position:F2}.");
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
            var go = new GameObject("Choice " + (index + 1), typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(MenuPageButton));
            go.transform.SetParent(panel, false);
            var row = new Row
            {
                Rect = (RectTransform)go.transform,
                Fill = go.GetComponent<UnityEngine.UI.Image>(),
                Button = go.GetComponent<MenuPageButton>()
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
                choices[index].OnSelect?.Invoke();
                if (sound) sound.Click();
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
            // The loader can create the display after OnEnable on a cold launch.
            if (recenterDisplay == null && OVRManager.display != null)
                SetRecenterSources(OVRManager.display, OVRManager.GetCurrentInputSubsystem());
            foreach (var pointer in pointers) pointer.EndPulse();
#if UNITY_EDITOR
            desktop.UpdateMode();
            if (desktop.Active) desktop.Move();
#endif
            bool tracked = Tracked;
            if (!Visible) { wasTracked = tracked; return; }
            if (warningPreview && warningPreview.gameObject.activeSelf)
            {
                double age = (Time.unscaledTimeAsDouble - previewStarted) % 4;
                warningPreview.ShowWarning(age < .8, age, previewGentle);
            }
            if (!tracked) { ClearPointers(); wasTracked = false; Paint(); return; }
            if (!wasTracked) RequestMenuRecenter();
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
            SetRecenterSources(OVRManager.display, OVRManager.GetCurrentInputSubsystem());
            OVRManager.InputFocusAcquired += OnInputFocusAcquired;
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
                // Meta's comprehensive rig exposes controller and hand data on the same branch.
                // Its interactor decoration identifies which pointers can deliver haptics.
                InteractorControllerDecorator.TryGetControllerForInteractorId(id, out var controller);
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
            if (paused && !value) OnInputFocusAcquired();
            paused = value;
            if (paused) foreach (var pointer in pointers) pointer?.EndPulse(true);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) wasTracked = false;
        }

        private void OnDisable()
        {
            HideStartCount();
            SetRecenterSources(null, null);
            OVRManager.InputFocusAcquired -= OnInputFocusAcquired;
            recenterRequestedFrame = -1;
            wasTracked = false;
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

        public void EditorUseRecenterDisplay(OVRDisplay display) =>
            SetRecenterSources(display, OVRManager.GetCurrentInputSubsystem());

#endif
        private void OnDestroy()
        {
            if (countCanvas) Destroy(countCanvas.gameObject);
#if UNITY_EDITOR
            desktop?.Dispose();
#endif
        }
    }
}
