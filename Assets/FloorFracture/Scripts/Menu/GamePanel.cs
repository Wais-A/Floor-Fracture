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
        // The physical row is 4.2 cm tall, with 8 mm between adjacent actions.
        public const float CanvasScale = 0.00058f;
        private const float Width = 672, Margin = 32, RowHeight = 72, RowGap = 14, RowStep = RowHeight + RowGap;
        // A half row peeks out at the edge to show the list scrolls; with Back the panel stays within the old six-row page.
        private const float ListRows = 4.5f;
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
        private ScrollRect scroll;
        private int listCount;
        private OVRCameraRig rig;
        private global::GameSound sound;
        private RectTransform panel;
        private Canvas menuCanvas;
#if UNITY_EDITOR
        private PointableCanvas pointable;
        private RayInteractable raySurface;
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


        public void Initialize(OVRCameraRig cameraRig, global::GameSound gameSound)
        {
            if (!cameraRig) throw new ArgumentNullException(nameof(cameraRig));
            if (rig) return;
            rig = cameraRig;
            sound = gameSound;
            eye = Head ? Head.GetComponent<Camera>() : Camera.main;
#if UNITY_EDITOR
            desktop = new EditorPanelInput(rig, transform);
            desktop.UpdateMode();
            // A bare no-XR fixture can still use the keyboard and mouse during scene authoring.
            if (!transform.Find(CanvasName) && desktop.Active)
                new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)).transform.SetParent(transform, false);
#endif
            BuildMenu();
            menuCanvas.worldCamera = countCanvas.worldCamera = eye;
#if UNITY_EDITOR
            raySurface = panel.GetComponentInChildren<RayInteractable>(true);
            if (raySurface) pointable = raySurface.GetComponent<PointableCanvas>();
#endif
            // Once the list moves nothing stays pressed, so a drag never looks like a selection.
            scroll.onValueChanged.AddListener(_ => { mousePress = pointers[0].Pressed = pointers[1].Pressed = -1; Paint(); });
            pointers[0] = new PanelController(transform, true);
            pointers[1] = new PanelController(transform, false);
            Hide();
        }

        /// <summary>
        /// Adopts the menu objects saved in the scene and creates any that are missing. A saved object
        /// keeps its own font, size and style. Needs no rig, so it also runs in edit mode.
        /// </summary>
        public void BuildMenu()
        {
            if (title) return;
            rows.Clear();
            var authored = transform.Find(CanvasName);
            if (!authored) throw new InvalidOperationException("Install the Meta ray canvas with MenuSceneSetup before building this scene.");
            // This built-in dynamic font is included in the player without a TMP Essentials import.
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            panel = (RectTransform)authored;
            panel.localScale = Vector3.one * CanvasScale;
            menuCanvas = panel.GetComponent<Canvas>();
            menuCanvas.renderMode = RenderMode.WorldSpace;
            menuCanvas.sortingOrder = 12;
            if (!panel.TryGetComponent<UnityEngine.UI.Image>(out var background))
            {
                background = panel.gameObject.AddComponent<UnityEngine.UI.Image>();
                background.color = Paper;
                background.raycastTarget = false;
            }
            title = Label(panel, "Title", 46, FontStyle.Bold, Ink, out _);
            body = Label(panel, "Instructions", 26, FontStyle.Normal, Secondary, out _);
            // PointableCanvasModule sends poke and ray drags to the nearest IDragHandler above the pressed row.
            var list = SceneParts.Child(panel, "Choice list", out bool made,
                typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(RectMask2D), typeof(ScrollRect));
            scroll = list.GetComponent<ScrollRect>();
            if (made)
            {
                // A clear backing lets a drag start between rows or on a locked level.
                list.GetComponent<UnityEngine.UI.Image>().color = Color.clear;
                scroll.horizontal = false;
                scroll.viewport = (RectTransform)list;
                scroll.content = (RectTransform)SceneParts.Child(list, "Content", out _, typeof(RectTransform));
                scroll.content.anchorMin = scroll.content.anchorMax = scroll.content.pivot = new Vector2(0, 1);
            }
            list.gameObject.SetActive(false);
            // Every saved row joins the pool now, or a page with fewer choices would leave the rest showing.
            while (panel.Find("Choice " + (rows.Count + 1))) BuildRow(rows.Count);

            // Saved beside the menu so it has its real size in the scene. ShowStartCount moves it to the head.
            var count = (RectTransform)SceneParts.Child(transform, "Start count", out made, typeof(RectTransform), typeof(Canvas));
            countCanvas = count.GetComponent<Canvas>();
            if (made)
            {
                count.localPosition = new Vector3(0, 1.72f, .95f);
                count.localScale = Vector3.one * .001f;
                count.sizeDelta = new Vector2(360, 150);
                countCanvas.renderMode = RenderMode.WorldSpace;
                countCanvas.sortingOrder = 30;
            }
            countLabel = Label(count, "Count", 100, FontStyle.Bold, Ink, out made);
            if (made)
            {
                countLabel.rectTransform.sizeDelta = count.sizeDelta;
                countLabel.alignment = TextAnchor.MiddleCenter;
                var outline = countLabel.gameObject.AddComponent<UnityEngine.UI.Outline>();
                outline.effectColor = new Color(0, .05f, .07f, .95f);
                outline.effectDistance = new Vector2(3, -3);
            }
            count.gameObject.SetActive(false);
            // A saved sample pane is adopted now, so one left switched on in the scene is hidden.
            if (title.transform.Find(WarningPreviewName)) BuildWarningPreview();
        }

        /// <summary>Lays out one page from plain labels, to save a sample in the scene. No rig, pointers or sound.</summary>
        public void BuildSample(string heading, string message, params string[] labels)
        {
            BuildMenu();
            roomRecovery = false;
            choices = Array.ConvertAll(labels, label => new MenuChoice(label, () => { }));
            FillPage(heading, message, 0);
            Paint();
            BuildWarningPreview();
        }

        public void Show(string heading, string message, params MenuChoice[] options)
        {
            ShowPage(heading, message, options, false);
        }

        // The listed choices scroll inside the panel, opened with listed[reveal] mid-view; the rest stay fixed below.
        public void ShowList(string heading, string message, MenuChoice[] listed, int reveal, params MenuChoice[] after)
        {
            var options = new MenuChoice[listed.Length + after.Length];
            listed.CopyTo(options, 0);
            after.CopyTo(options, listed.Length);
            ShowPage(heading, message, options, false, listed.Length);
            ScrollTo(reveal * RowStep + (RowHeight - scroll.viewport.rect.height) / 2);
        }

        private void ScrollTo(float offset)
        {
            scroll.StopMovement();
            float range = Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height);
            scroll.content.anchoredPosition = new Vector2(0, Mathf.Clamp(offset, 0, range));
        }

        public void ShowStartCount(int count, float opacity = 1f)
        {
            if (countCanvas.transform.parent != Head)
            {
                // The count rides with the head, a little above the line of sight.
                countCanvas.transform.SetParent(Head, false);
                countCanvas.transform.localPosition = new Vector3(0, .12f, .95f);
                countCanvas.transform.localRotation = Quaternion.identity;
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

        private void ShowPage(string heading, string message, MenuChoice[] options, bool isRoomRecovery, int listed = 0)
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
            FillPage(heading, message, listed);
            Paint();
            var center = eye ? eye.WorldToViewportPoint(panel.position) : Vector3.zero;
            if (position || center.z <= 0 || center.x < 0 || center.x > 1 || center.y < 0 || center.y > 1)
                Recenter();
        }

        private void FillPage(string heading, string message, int listed)
        {
            if (warningPreview) warningPreview.gameObject.SetActive(false);
            panel.gameObject.SetActive(true);
            ClearPointers();
            title.text = heading ?? "";
            listCount = listed;
            scroll.gameObject.SetActive(listed > 0);
            for (int i = rows.Count; i < choices.Length; ++i) BuildRow(i);
            for (int i = 0; i < rows.Count; ++i)
            {
                var parent = i < listed ? scroll.content : panel;
                if (rows[i].Rect.parent != parent) rows[i].Rect.SetParent(parent, false);
                rows[i].Button.ClearPendingPresses();
                bool use = i < choices.Length;
                rows[i].Rect.gameObject.SetActive(use);
                if (use)
                {
                    rows[i].Text.text = choices[i].Label;
                    rows[i].Button.interactable = choices[i].OnSelect != null;
                    rows[i].Fill.raycastTarget = rows[i].Button.interactable;
                }
            }
            SetBody(message);
        }

        private const string WarningPreviewName = "Warning preview";

        // Keep the real pane and its branching cracks beside the Settings title,
        // in reach even when the wearer opens the menu from outside the board.
        private void BuildWarningPreview()
        {
            if (warningPreview) return;
            var sample = SceneParts.Child(title.rectTransform, WarningPreviewName, out bool made);
            if (made)
            {
                sample.localPosition = new Vector3(title.rectTransform.rect.xMax - WarningPreviewSize / 2f,
                    -WarningPreviewSize / 2f, -2f);
                sample.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                sample.localScale = Vector3.one * (WarningPreviewSize / GlassGeometry.AuthoredPitch);
            }
            warningPreview = SceneParts.Require<GlassTile>(sample.gameObject);
            warningPreview.Initialize();
            foreach (var renderer in sample.GetComponentsInChildren<MeshRenderer>(true))
                renderer.sortingOrder = menuCanvas.sortingOrder + 1;
            sample.gameObject.SetActive(false);
        }

        public void ShowWarningPreview(bool gentle)
        {
            BuildWarningPreview();
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
            int listed=listCount;
            return () => ShowPage(heading,message,buttons,false,listed);
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
            float bottom = top;
            if (listCount > 0)
            {
                float listHeight = Mathf.Min(listCount, ListRows) * RowStep - RowGap;
                Place(scroll.viewport, Margin, top, Width - 2 * Margin, listHeight);
                scroll.content.sizeDelta = new Vector2(Width - 2 * Margin, listCount * RowStep - RowGap);
                bottom += listHeight + RowGap;
            }
            for (int i = 0; i < choices.Length; ++i)
                if (i < listCount) Place(rows[i].Rect, 0, i * RowStep, Width - 2 * Margin, RowHeight);
                else Place(rows[i].Rect, Margin, bottom + (i - listCount) * RowStep, Width - 2 * Margin, RowHeight);
            bottom += (choices.Length - listCount) * RowStep;
            float height = bottom - (choices.Length > 0 ? RowGap : 0) + Margin;
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

        private UnityEngine.UI.Text Label(Transform parent, string name, int size, FontStyle style, Color color, out bool created)
        {
            var text = SceneParts.Child(parent, name, out created, typeof(RectTransform), typeof(UnityEngine.UI.Text)).GetComponent<UnityEngine.UI.Text>();
            if (!created) return text;
            text.font = font;
            text.fontSize = size;
            // Unity raises this to the font size the next time it validates the Text; set here, a second save changes nothing.
            text.resizeTextMaxSize = Mathf.Max(text.resizeTextMaxSize, size);
            text.fontStyle = style;
            text.color = color;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.alignment = TextAnchor.UpperLeft;
            text.raycastTarget = false;
            return text;
        }

        // A row saved in the scene is adopted. A row past those is a copy of the first, so it inherits that row's styling.
        private void BuildRow(int index)
        {
            string name = "Choice " + (index + 1);
            var saved = panel.Find(name);
            bool fresh = !saved && rows.Count == 0;
            GameObject go = saved ? saved.gameObject
                : fresh ? new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(MenuPageButton))
                : Instantiate(rows[0].Rect.gameObject);
            go.name = name;
            if (!saved) go.transform.SetParent(panel, false);
            var row = new Row
            {
                Rect = (RectTransform)go.transform,
                Fill = go.GetComponent<UnityEngine.UI.Image>(),
                Button = go.GetComponent<MenuPageButton>()
            };
            if (fresh)
            {
                row.Fill.raycastTarget = true;
                row.Button.targetGraphic = row.Fill;
                row.Button.transition = Selectable.Transition.None;
                row.Button.navigation = new Navigation { mode = Navigation.Mode.None };
            }
            // A click listener is never saved or copied, so every row is wired here, once.
            row.Button.onClick.AddListener(() =>
            {
                if (!Visible || index >= choices.Length || !row.Button.interactable) return;
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
            row.Text = Label(go.transform, "Label", 28, FontStyle.Bold, Ink, out bool madeLabel);
            if (madeLabel)
            {
                row.Text.alignment = TextAnchor.MiddleLeft;
                Place(row.Text.rectTransform, 19, 0, Width - 2 * Margin - 35, RowHeight);
            }
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
            bool inList = scroll.viewport.rect.Contains(scroll.viewport.InverseTransformPoint(point));
            for (int i = 0; i < choices.Length; ++i)
                if (rows[i].Button.interactable && (i >= listCount || inList) &&
                    rows[i].Rect.rect.Contains(rows[i].Rect.InverseTransformPoint(point))) return i;
            return -1;
        }

        private bool Select(int index)
        {
            if (!Visible || index < 0 || index >= choices.Length || !rows[index].Button.interactable) return false;
            rows[index].Button.onClick.Invoke();
            return true;
        }
#endif

        private void Paint()
        {
            for (int i = 0; i < choices.Length; ++i)
            {
                if (!rows[i].Button.interactable)
                {
                    rows[i].Fill.color = Paper;
                    rows[i].Text.color = Secondary;
                    continue;
                }
                bool down = mousePress == i || pointers[0]?.Pressed == i || pointers[1]?.Pressed == i;
                bool over = mouseHover == i || pointers[0]?.Hover == i || pointers[1]?.Hover == i;
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
            // Clamped to one row per frame, because wheel delta units differ between platforms.
            float wheel = Mathf.Clamp(mouse.scroll.ReadValue().y, -1, 1);
            if (listCount > 0 && wheel != 0) ScrollTo(scroll.content.anchoredPosition.y - wheel * RowStep);
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

        public void EditorSetPose(Pose head, Pose leftGrip, Pose rightGrip) =>
            desktop.SetPose(head, leftGrip, rightGrip);

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
