using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meta.XR.EnvironmentDepth;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace FloorFracture
{
    public enum RoomPlacementStatus { Loading, PermissionDenied, NoRoom, UnsafeRoom, NoFit, Confirmed }

    [DisallowMultipleComponent, RequireComponent(typeof(MRUK))]
    public sealed partial class RoomBoardPlacement : MonoBehaviour
    {
        [SerializeField] private OVRCameraRig rig;
        [SerializeField] private FindSpawnPositions floorSpawns;
        [UnityEngine.Serialization.FormerlySerializedAs("floorWaterMesh")]
        [SerializeField] private EffectMesh floorEffect;
        [SerializeField] private Shader outlineShader = null;
#if UNITY_EDITOR
        [Tooltip("Use a recorded MRUK room with its global mesh during desktop Play Mode when no OpenXR display is active.")]
        [SerializeField] private bool useEditorFixture = true;
        [SerializeField, Tooltip("Optional MRUK JSON recording for desktop Play Mode. It must contain a measured global mesh.")]
        private TextAsset editorRoomRecording;
        private bool usingFixture;
        private bool restoreBackgroundSetting;
        private SimulatorRoomOcclusion simulatorOcclusion;
        internal const string FixturePath = "Packages/com.meta.xr.mrutilitykit/Core/Rooms/Json/MeshLivingRoom1.json";
#endif
        public RoomPlacementStatus Status { get; private set; } = RoomPlacementStatus.Loading;
        public string Message { get; private set; } = "Loading your room…";
        public BoardPlacement Placement { get; private set; }
        public int ScannedSpawnCount { get; private set; }
        public EffectMesh FloorEffect => floorEffect;
        public event Action Changed;

        private const string DeniedMessage = "Allow spatial data for Floor Fracture in app permissions, then retry.";
        private const string MissingRoomMessage = "The headset hasn't found a saved room here yet. Look around and check again, or scan the floor and furniture in Space Setup.";
        private readonly List<Vector2[]> declined = new List<Vector2[]>();
        private MRUK mruk;
        private MRUKRoom room;
        private List<RoomFloor> floors;
        private RoomOutlineView outlines;
        private Material floorMaterial;
        private FloorDepthHistory floorDepth;
        private readonly FloorPatchCoverage floorCoverage = new();
        private MRUKRoom decoratedRoom;
        private int decoratedRevision = -1;
        private bool playAreaVisible;
        private bool busy;
        private bool awaitingSavedFloor;
        private double nextFloorCheck;
        private string floorReadIssue;
        private int revision;

        public bool IsRoomLocalized => room && CurrentTrackedRoom() == room;

        private MRUKRoom CurrentTrackedRoom()
        {
            if (!mruk || !rig || !rig.centerEyeAnchor || !rig.trackingSpace) return null;
            bool fixture = false;
#if UNITY_EDITOR
            fixture = usingFixture;
            if (fixture && mruk.Rooms.Count == 1 && mruk.Rooms[0].FloorAnchors.Count <= 1) return mruk.Rooms[0];
#endif
            if (!fixture && !mruk.IsWorldLockActive) return null;
            var eye = rig.centerEyeAnchor.position;
            return MrukFloorReader.FindContainingRoom(mruk.Rooms, mruk.GetCurrentRoom(),
                eye, TrackingFloorAtEye(eye), fixture);
        }

        private void Awake()
        {
            mruk = GetComponent<MRUK>();
            if (!rig) rig = FindAnyObjectByType<OVRCameraRig>();
            outlines = new RoomOutlineView(transform, outlineShader);
            InitializePlacementDebug();
            if (floorEffect && floorEffect.MeshMaterial)
            {
                floorMaterial = new Material(floorEffect.MeshMaterial);
                floorEffect.MeshMaterial = floorMaterial;
                // A Quest without live depth shows passthrough until furniture can occlude the brick.
                floorMaterial.SetFloat("_RequireEnvironmentDepth", Application.platform == RuntimePlatform.Android ? 1f : 0f);
                floorDepth = new FloorDepthHistory(floorMaterial);
            }
        }

        private void OnEnable()
        {
            mruk.RoomCreatedEvent.AddListener(RoomDiscovered);
            mruk.RoomUpdatedEvent.AddListener(RoomChanged);
            mruk.RoomRemovedEvent.AddListener(RoomChanged);
            AddAnchorListeners();
            OVRManager.InputFocusAcquired += OnTrackingAvailable;
            OVRManager.HMDMounted += OnTrackingAvailable;
            OVRManager.TrackingAcquired += OnTrackingAvailable;
            RenderPipelineManager.beginCameraRendering += UpdateFloorDepth;
        }

        private void UpdateFloorDepth(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType == CameraType.Game && floorEffect && !floorEffect.HideMesh)
                floorDepth?.Update(Time.realtimeSinceStartupAsDouble);
        }

        private void Start()
        {
#if UNITY_EDITOR_OSX
            var runtime = Environment.GetEnvironmentVariable("XR_RUNTIME_JSON");
            if (!Application.isBatchMode && runtime != null &&
                runtime.EndsWith("meta_openxr_simulator.json", StringComparison.OrdinalIgnoreCase))
                StartCoroutine(RecoverSimulatorPassthrough());
#endif
            Retry();
        }

        private void Update()
        {
            if (displayedDebug != showPlacementDebug) RefreshPlacementDebug();
            if (!busy && Status == RoomPlacementStatus.Confirmed && (!IsRoomLocalized || !room.GlobalMeshAnchor))
            {
                awaitingSavedFloor = true;
                nextFloorCheck = 0;
                surveys = null;
                meshSnapshot = null;
                SetState(RoomPlacementStatus.UnsafeRoom, "Floor tracking was lost. The glass will return after the room and its mesh are verified again.");
                return;
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!busy && Status == RoomPlacementStatus.Confirmed && HasPlacementFocus) CheckForNewObstacle();
            if (!busy && Status == RoomPlacementStatus.NoFit && MovedToNewFloorView())
            {
                LoadRoom(false, true);
                return;
            }
#endif
            if (!awaitingSavedFloor || busy || !HasPlacementFocus || Time.unscaledTimeAsDouble < nextFloorCheck)
                return;
            nextFloorCheck = Time.unscaledTimeAsDouble + 1;
            if (!CheckCurrentPermission()) { awaitingSavedFloor = false; return; }
            SetRoom(CurrentTrackedRoom());
            if (room && TryReadFloors()) LoadRoom(false, true);
        }

#if UNITY_EDITOR_OSX
        private System.Collections.IEnumerator RecoverSimulatorPassthrough()
        {
            // On repeated Play sessions Meta can initialize passthrough without submitting its layer.
            double deadline = Time.realtimeSinceStartupAsDouble + 10;
            while (!OVRManager.IsInsightPassthroughInitialized() && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            yield return new WaitForSecondsRealtime(2);
            var manager = OVRManager.instance;
            var layer = FindAnyObjectByType<OVRPassthroughLayer>();
            var overlay = layer ? layer.GetComponentInChildren<OVROverlay>(true) : null;
            if (!UnityEngine.XR.XRSettings.isDeviceActive || !manager || !manager.isInsightPassthroughEnabled ||
                !layer || layer.hidden || !overlay || overlay.layerId > 0 || !OVRManager.IsInsightPassthroughInitialized())
                yield break;

            manager.isInsightPassthroughEnabled = false;
            try
            {
                deadline = Time.realtimeSinceStartupAsDouble + 1;
                while (OVRManager.IsInsightPassthroughInitialized() && Time.realtimeSinceStartupAsDouble < deadline)
                    yield return null;
            }
            finally
            {
                if (manager) manager.isInsightPassthroughEnabled = true;
            }
            Debug.Log("[Floor Fracture] Restarted the missing Meta simulator passthrough layer.", this);
        }
#endif

        private void OnDisable()
        {
            if (liveManager) liveManager.gameObject.SetActive(false);
            revision++;
            debugView?.Show(false, default);
            mruk.RoomCreatedEvent.RemoveListener(RoomDiscovered);
            mruk.RoomUpdatedEvent.RemoveListener(RoomChanged);
            mruk.RoomRemovedEvent.RemoveListener(RoomChanged);
            RemoveAnchorListeners();
            OVRManager.InputFocusAcquired -= OnTrackingAvailable;
            OVRManager.HMDMounted -= OnTrackingAvailable;
            OVRManager.TrackingAcquired -= OnTrackingAvailable;
            RenderPipelineManager.beginCameraRendering -= UpdateFloorDepth;
            SetPlayAreaVisible(false);
        }

        private void OnDestroy()
        {
            floorCoverage.Dispose();
            outlines?.Dispose();
            debugView?.Dispose();
            floorDepth?.Dispose();
            if (floorMaterial) Destroy(floorMaterial);
#if UNITY_EDITOR
            if (restoreBackgroundSetting) Application.runInBackground = false;
            if (simulatorOcclusion) Destroy(simulatorOcclusion.gameObject);
#endif
        }

        public void SetPlayAreaVisible(bool visible, bool showFallFloor = false)
        {
            playAreaVisible = visible && !showFallFloor && Status == RoomPlacementStatus.Confirmed;
            if (!visible || Status != RoomPlacementStatus.Confirmed || !room || !floorEffect || !floorMaterial)
            {
                floorDepth?.Clear();
                if (floorEffect) floorEffect.HideMesh = true;
#if UNITY_EDITOR
                if (simulatorOcclusion) simulatorOcclusion.gameObject.SetActive(false);
#endif
                return;
            }

            // The shader clips the whole grid, including panes that have already fallen.
            var fit = Placement;
            float angle = fit.AngleDegrees * Mathf.Deg2Rad;
            floorMaterial.SetVector("_BoardFrame", new Vector4(fit.Center.x, fit.Center.y, Mathf.Cos(angle), Mathf.Sin(angle)));
            floorMaterial.SetVector("_BoardHalfSize", new Vector4(fit.Columns * fit.Pitch * .5f, fit.Rows * fit.Pitch * .5f,
                floors?.Find(f => BoardFitter.Fits(f, fit, FloorSurvey.FootMargin))?.LabelHeight ?? fit.FloorHeight,
                fit.Layout.IsRectangle ? 0 : 1f / fit.Pitch));
            floorMaterial.SetInteger("_BoardMaskLow", unchecked((int)fit.Layout.Mask));
            floorMaterial.SetInteger("_BoardMaskHigh", unchecked((int)(fit.Layout.Mask >> 32)));
            if (decoratedRoom != room || decoratedRevision != revision)
            {
                // Apply the same reviewed scan as the glass, including every native floor patch.
                ClearFloorMeshes();
                foreach (var floor in room.FloorAnchors)
                {
                    if (!floor) continue;
                    var patch = floorEffect.CreateEffectMesh(floor);
                    if (patch?.effectMeshGO) floorCoverage.Add(floor, patch.effectMeshGO.GetComponent<MeshFilter>());
                }
#if UNITY_EDITOR
                if (simulatorOcclusion) Destroy(simulatorOcclusion.gameObject);
                simulatorOcclusion = !usingFixture && !EnvironmentDepthManager.IsSupported
                    ? SimulatorRoomOcclusion.Create(room, fit.FloorHeight, floorMaterial.renderQueue) : null;
                floorMaterial.SetFloat("_RequireEnvironmentDepth", usingFixture || simulatorOcclusion ? 0f : 1f);
#endif
                decoratedRoom = room;
                decoratedRevision = revision;
            }
#if UNITY_EDITOR
            if (simulatorOcclusion) simulatorOcclusion.gameObject.SetActive(true);
#endif
            // The fall sequence uses these meshes at the rim and during the descent.
            // During normal play, passthrough shows the real floor around the glass.
            floorEffect.HideMesh = !showFallFloor;
        }

        private void ClearFloorMeshes()
        {
            floorDepth?.Clear();
            floorCoverage.Dispose();
            if (floorEffect) floorEffect.DestroyMesh();
            decoratedRoom = null;
            decoratedRevision = -1;
        }

        public void Retry()
        {
            if (busy) return;
            declined.Clear();
            LoadRoom(false, false, Status == RoomPlacementStatus.Confirmed || Status == RoomPlacementStatus.NoFit);
        }

        public void RequestSpaceSetup()
        {
            if (busy) return;
            declined.Clear();
            LoadRoom(true, false);
        }

        public void Decline()
        {
            if (busy || Status != RoomPlacementStatus.Confirmed) return;
            // A missed object could be anywhere in the declined footprint.
            if (Placement.Layout.IsRectangle) declined.Add(Placement.FootprintOutline());
            else
                for (int z = 0; z < Placement.Rows; z++)
                    for (int x = 0; x < Placement.Columns; x++)
                        if (Placement.Layout.HasPane(x, z)) declined.Add(Placement.TileOutline(x, z, 0));
            LoadRoom(false, true);
        }

        public void ShowClearance(bool visible)
        {
            bool show = visible && Status == RoomPlacementStatus.Confirmed;
            outlines.Show(show, Placement);
        }

        public void OpenPermissionSettings()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var uriClass = new AndroidJavaClass("android.net.Uri");
                using var uri = uriClass.CallStatic<AndroidJavaObject>("parse", "package:" + Application.identifier);
                using var intent = new AndroidJavaObject("android.content.Intent", "android.settings.APPLICATION_DETAILS_SETTINGS", uri);
                activity.Call("startActivity", intent);
            }
            catch (AndroidJavaException)
            {
                SetState(RoomPlacementStatus.PermissionDenied, DeniedMessage);
            }
#endif
        }

        private async void LoadRoom(bool capture, bool reuseRoom, bool measureView = false)
        {
            busy = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            waitingForDepthView = false;
#endif
            awaitingSavedFloor = false;
            ScannedSpawnCount = 0;
            surveys = null;
            meshSnapshot = null;
            CancellationToken lifetime = destroyCancellationToken;
            SetState(RoomPlacementStatus.Loading, capture ? "Opening Space Setup…" : "Loading your room…");
            try
            {
                if (!reuseRoom || !room)
                {
                    // SDK 209 can remove a mesh from its registry only while its
                    // anchor's child still exists. Release it before replacing the scan.
                    if (decoratedRoom) ClearFloorMeshes();
                    SetRoom(null);
                    floors = null;
                    MRUK.LoadDeviceResult result;
#if UNITY_EDITOR
                    // A simulator window can keep its OpenXR runtime while the Editor loses display focus.
                    bool runtimeHead = !Application.isBatchMode && (UnityEngine.XR.XRSettings.isDeviceActive ||
                        (OVRPlugin.initialized && OVRPlugin.GetSystemHeadsetType() != OVRPlugin.SystemHeadset.None));
                    usingFixture = useEditorFixture && !runtimeHead;
                    if (runtimeHead && !Application.runInBackground)
                    {
                        // The external simulator owns focus while the player looks around. Keep Unity submitting frames.
                        Application.runInBackground = true;
                        restoreBackgroundSetting = true;
                    }
                    if (usingFixture)
                    {
                        var fixture = editorRoomRecording ? editorRoomRecording : UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(FixturePath);
                        if (!fixture) throw new InvalidOperationException("The installed MRUK room recording is missing.");
                        result = await mruk.LoadSceneFromJsonString(fixture.text);
                    }
                    else
#endif
                    {
#if UNITY_ANDROID && !UNITY_EDITOR
                        if (!await HasSpatialPermission(lifetime))
                        {
                            lifetime.ThrowIfCancellationRequested();
                            SetState(RoomPlacementStatus.PermissionDenied, DeniedMessage);
                            return;
                        }
#endif
                        if (capture && !await OVRScene.RequestSpaceSetup())
                        {
                            lifetime.ThrowIfCancellationRequested();
                            SetState(RoomPlacementStatus.NoRoom, "Space Setup was closed. Finish a scan or retry your saved room.");
                            return;
                        }
                        lifetime.ThrowIfCancellationRequested();
                        // Space Setup can finish while the system still owns input. Wait for
                        // the app before querying and give a freshly published scan time to arrive.
                        int attempts = capture ? 3 : 2;
                        do
                        {
                            while (!HasPlacementFocus) await Awaitable.NextFrameAsync(lifetime);
                            result = await mruk.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: false);
                            lifetime.ThrowIfCancellationRequested();
                            if ((result != MRUK.LoadDeviceResult.NoRoomsFound && result != MRUK.LoadDeviceResult.DiscoveryOngoing) || --attempts == 0) break;
                            await Task.Delay(1000, lifetime);
                        } while (true);
                    }
                    lifetime.ThrowIfCancellationRequested();
                    if (!AcceptLoad(result)) return;
                }
                if (mruk.Rooms.Count == 0)
                {
                    SetState(RoomPlacementStatus.NoRoom, MissingRoomMessage);
                    return;
                }
                // GetCurrentRoom may initially return the first saved room until the headset
                // localizes. Only accept the current room after world lock and eye containment agree.
                SetState(RoomPlacementStatus.Loading, "Look around your scanned room while the headset finds its floor.");
                float focusedSeconds = 0f;
                bool ready = false;
                nextFloorCheck = 0;
                while (focusedSeconds < 8f)
                {
                    if (HasPlacementFocus && Time.unscaledTimeAsDouble >= nextFloorCheck)
                    {
                        var current = CurrentTrackedRoom();
                        if (!reuseRoom) SetRoom(current);
                        if (current && current == room && TryReadFloors())
                        {
                            ready = true;
                            break;
                        }
                        nextFloorCheck = Time.unscaledTimeAsDouble + 0.35;
                    }
                    if (HasPlacementFocus) focusedSeconds += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                    await Awaitable.NextFrameAsync(lifetime);
                }
                if (!ready)
                {
                    awaitingSavedFloor = true;
                    string message = !room || !IsRoomLocalized
                        ? LocalizationMessage()
                        : floorReadIssue + " Checking again automatically.";
                    SetState(RoomPlacementStatus.UnsafeRoom, message);
                    return;
                }
                SetState(RoomPlacementStatus.Loading, "Floor found. Fitting the glass between the walls and furniture…");
                var head = rig.centerEyeAnchor.position;
                var preferred = new Vector2(head.x, head.z);
                int scanRevision = revision;
                var suggestions = ReadSpawnCenters(preferred);
                var verified = await FitSurveyAsync(preferred, suggestions, scanRevision, lifetime, measureView);
                lifetime.ThrowIfCancellationRequested();
                if (!verified.HasValue) return;
                var fit = verified.Value;
                Placement = fit;
                int tileCm = Mathf.RoundToInt(fit.Pitch * 100f);
                string description = fit.Layout.IsRectangle
                    ? $"{fit.Columns} × {fit.Rows} panes at {tileCm} cm each, covering {fit.Columns * fit.Pitch:F2} × {fit.Rows * fit.Pitch:F2} m"
                    : $"{fit.Layout.PaneCount} connected panes at {tileCm} cm each, covering {fit.Layout.PaneCount * fit.Pitch * fit.Pitch:F2} m²";
                SetState(RoomPlacementStatus.Confirmed, $"Your room scan fits {description}.");
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (lifetime.IsCancellationRequested) return;
                Debug.LogWarning($"Room placement: {error}", this);
                SetState(RoomPlacementStatus.UnsafeRoom, "The room scan could not be read. Look around and retry, or run Space Setup.");
            }
            finally { busy = false; }
        }

        private bool AcceptLoad(MRUK.LoadDeviceResult result)
        {
            if (result == MRUK.LoadDeviceResult.Success) return true;
            if (result == MRUK.LoadDeviceResult.NoScenePermission || result == MRUK.LoadDeviceResult.FailurePermissionInsufficient)
                SetState(RoomPlacementStatus.PermissionDenied, DeniedMessage);
            else if (result == MRUK.LoadDeviceResult.NoRoomsFound)
                SetState(RoomPlacementStatus.NoRoom, MissingRoomMessage);
            else
                SetState(RoomPlacementStatus.UnsafeRoom, $"Room scan unavailable ({result}). Check lighting and tracking, then retry.");
            return false;
        }

        private bool TryReadFloors()
        {
            if (!mruk.IsInitialized || !rig || !rig.centerEyeAnchor || !rig.trackingSpace)
            {
                floorReadIssue = "Waiting for the room scene and headset tracking.";
                return false;
            }
            var eye = rig.centerEyeAnchor.position;
            float y = TrackingFloorAtEye(eye);
            if (!MrukFloorReader.TryRead(room, y, eye.y - y, out floors, out floorReadIssue)) return false;
            if (FloorMeshScan.TryRead(room, out meshSnapshot))
            {
                for (int i = 0; i < floors.Count; i++)
                    floors[i] = meshSnapshot.AlignFloor(floors[i], y);
                return true;
            }
            floorReadIssue = "The saved room has no readable global mesh. Finish scanning the open floor and furniture in Space Setup.";
            return false;
        }

        private float TrackingFloorAtEye(Vector3 eye)
        {
            var space = rig.trackingSpace;
#if UNITY_EDITOR
            if (usingFixture) return MrukFloorReader.TrackingFloorHeight(space.position, space.up, eye);
#endif
            var input = OVRManager.GetCurrentInputSubsystem();
            if (input != null && (input.GetTrackingOriginMode() & UnityEngine.XR.TrackingOriginModeFlags.Floor) != 0)
                return MrukFloorReader.TrackingFloorHeight(space.position, space.up, eye);
            // Meta's Tracking Origin sample exposes the calibrated floor relative to
            // the current origin, including when the runtime temporarily uses eye level.
            var floor = OVRPlugin.GetTrackingTransformRelativePose(OVRPlugin.TrackingOrigin.FloorLevel).ToOVRPose();
            if (floor.position == Vector3.zero && floor.orientation == Quaternion.identity) return float.NaN;
            return MrukFloorReader.TrackingFloorHeight(space.TransformPoint(floor.position),
                space.rotation * floor.orientation * Vector3.up, eye);
        }

        private string LocalizationMessage()
        {
            if (rig && rig.centerEyeAnchor && rig.trackingSpace && mruk.IsWorldLockActive)
            {
                var eye = rig.centerEyeAnchor.position;
                float height = TrackingFloorAtEye(eye);
                if (!FloorGeometry.Finite(height)) return "Waiting for the headset's floor-level tracking. Look around. The game will check again automatically.";
                var hint = mruk.GetCurrentRoom();
                if (hint && hint.IsLocal && MrukFloorReader.ContainsFloorPoint(hint, eye, null) &&
                    !MrukFloorReader.ContainsFloorPoint(hint, eye, height))
                    return "The floor in your scan is at a different height from the headset's floor level. Check the floor height in Space Setup, then return here.";
            }
            return "Looking for your saved floor. Stand inside the scanned room and look around. The board will open when tracking returns.";
        }

        // Floor Zone uses this same Meta block to locate open floor. The SDK markers are
        // transient search points. Only a fitted rectangle is presented to the player.
        private List<Vector2> ReadSpawnCenters(Vector2 preferred)
        {
            if (!floorSpawns) throw new InvalidOperationException("The room needs Meta's Find Spawn Positions block.");
            var centers = new List<Vector2>(floorSpawns.SpawnAmount);
            try
            {
                floorSpawns.StartSpawn(room);
                foreach (var marker in floorSpawns.SpawnedObjects)
                {
                    Vector3 point = marker.transform.position;
                    marker.SetActive(false);
                    centers.Add(new Vector2(point.x, point.z));
                }
            }
            finally { floorSpawns.ClearSpawnedPrefabs(); }
            centers.Sort((a, b) => (a - preferred).sqrMagnitude.CompareTo((b - preferred).sqrMagnitude));
            ScannedSpawnCount = centers.Count;
            return centers;
        }

        private static bool HasPlacementFocus
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return Application.isFocused && OVRManager.hasInputFocus;
#else
                return true;
#endif
            }
        }

        private void SetRoom(MRUKRoom value)
        {
            if (room == value) return;
            detectedObstacles.Clear();
            RemoveAnchorListeners();
            room = value;
            if (isActiveAndEnabled) AddAnchorListeners();
        }

        private void AddAnchorListeners()
        {
            if (!room) return;
            room.AnchorCreatedEvent.AddListener(AnchorChanged);
            room.AnchorUpdatedEvent.AddListener(AnchorChanged);
            room.AnchorRemovedEvent.AddListener(AnchorChanged);
        }

        private void RemoveAnchorListeners()
        {
            if (!room) return;
            room.AnchorCreatedEvent.RemoveListener(AnchorChanged);
            room.AnchorUpdatedEvent.RemoveListener(AnchorChanged);
            room.AnchorRemovedEvent.RemoveListener(AnchorChanged);
        }

        // MRUK emits individual floor/furniture changes independently of its room-level event.
        private void AnchorChanged(MRUKAnchor anchor) => RoomChanged(room);

        private void RoomDiscovered(MRUKRoom discovered)
        {
            // RoomCreated can precede the SDK publishing the room and its anchors. Let
            // the normal tracked-floor check use the complete room collection next frame.
            if (!discovered || Status != RoomPlacementStatus.NoRoom) return;
            awaitingSavedFloor = true;
            nextFloorCheck = 0;
        }

        private void RoomChanged(MRUKRoom changedRoom)
        {
            if (!room || changedRoom != room) return;
            detectedObstacles.Clear();
            if (decoratedRoom == changedRoom) ClearFloorMeshes();
            revision++;
            surveys = null;
            meshSnapshot = null;
            floors = null;
            if (Status == RoomPlacementStatus.Confirmed)
                SetState(RoomPlacementStatus.UnsafeRoom, "The room scan changed. Check again to fit the board to the updated room.");
            else if (Status == RoomPlacementStatus.NoFit && !awaitingSavedFloor)
            {
                // MRUK can publish several anchor events followed by the room event for one scan.
                // Reuse the localized room once the updated anchors are assembled.
                awaitingSavedFloor = true;
                nextFloorCheck = Time.unscaledTimeAsDouble + .25;
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused) OnTrackingAvailable();
        }

        private void OnTrackingAvailable()
        {
            if (Status == RoomPlacementStatus.Confirmed) CheckCurrentPermission();
            else if (Status == RoomPlacementStatus.NoRoom && !busy) Retry();
        }

        private bool CheckCurrentPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission))
            {
                SetState(RoomPlacementStatus.PermissionDenied, DeniedMessage);
                return false;
            }
#endif
            return true;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static async Task<bool> HasSpatialPermission(CancellationToken lifetime)
        {
            const string permission = OVRPermissionsRequester.ScenePermission;
            if (Permission.HasUserAuthorizedPermission(permission)) return true;
            var result = new TaskCompletionSource<bool>();
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => result.TrySetResult(true);
            callbacks.PermissionDenied += _ => result.TrySetResult(false);
            callbacks.PermissionRequestDismissed += _ => result.TrySetResult(false);
            Permission.RequestUserPermission(permission, callbacks);
            // Unity can omit callbacks when another system permission dialog was already open.
            // Check the actual grant, allow unbounded time inside the system UI, and restore
            // the retry/settings controls if the game regains input without a decision.
            float focusedSeconds = 0f;
            double nextCheck = 0.0;
            while (!result.Task.IsCompleted)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (now >= nextCheck)
                {
                    if (Permission.HasUserAuthorizedPermission(permission)) return true;
                    nextCheck = now + 0.25;
                }
                focusedSeconds = HasPlacementFocus ? focusedSeconds + Mathf.Min(Time.unscaledDeltaTime, 0.1f) : 0f;
                if (focusedSeconds >= 10f) return Permission.HasUserAuthorizedPermission(permission);
                await Awaitable.NextFrameAsync(lifetime);
            }
            return await result.Task;
        }
#endif

        private void SetState(RoomPlacementStatus next, string message)
        {
            Status = next;
            Message = message;
#if UNITY_ANDROID && !UNITY_EDITOR
            pendingPane = -1;
            nextLivePane = liveSample = 0;
            if (next != RoomPlacementStatus.Confirmed && liveManager) liveManager.gameObject.SetActive(false);
#endif
            if (next == RoomPlacementStatus.UnsafeRoom || next == RoomPlacementStatus.NoRoom || next == RoomPlacementStatus.NoFit || next == RoomPlacementStatus.Confirmed)
                Debug.Log($"[Floor Fracture room] {next}: {message} Saved rooms={mruk.Rooms.Count}, world lock={mruk.IsWorldLockActive}.", this);
            SetPlayAreaVisible(next == RoomPlacementStatus.Confirmed);
            outlines.Show(false, Placement);
            RefreshPlacementDebug();
            Changed?.Invoke();
        }
    }
}
