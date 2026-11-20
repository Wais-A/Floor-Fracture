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
    public sealed class RoomBoardPlacement : MonoBehaviour
    {
        [SerializeField] private OVRCameraRig rig;
        [SerializeField] private FindSpawnPositions floorSpawns;
        [UnityEngine.Serialization.FormerlySerializedAs("floorWaterMesh")]
        [SerializeField] private EffectMesh floorEffect;
        [SerializeField] private Shader outlineShader = null;
#if UNITY_EDITOR
        [Tooltip("Use the MRUK Rec Room fixture during desktop Play Mode when no OpenXR display is active.")]
        [SerializeField] private bool useEditorFixture = true;
        private bool usingFixture;
        private bool restoreBackgroundSetting;
        private SimulatorRoomOcclusion simulatorOcclusion;
        internal const string FixturePath = "Packages/com.meta.xr.mrutilitykit/Core/Rooms/Prefabs/Other/OtherRoom02_RecRoom.prefab";
#endif
        public RoomPlacementStatus Status { get; private set; } = RoomPlacementStatus.Loading;
        public string Message { get; private set; } = "Loading your room…";
        public BoardPlacement Placement { get; private set; }
        public int ScannedSpawnCount { get; private set; }
        public EffectMesh FloorEffect => floorEffect;
        public event Action Changed;

        private const string DeniedMessage = "Allow spatial data for Floor Fracture in app permissions, then retry.";
        private const string MissingRoomMessage = "No saved room here. Open Space Setup, scan the floor and furniture, then return.";
        private readonly List<Vector2[]> declined = new List<Vector2[]>();
        private MRUK mruk;
        private MRUKRoom room;
        private List<RoomFloor> floors;
        private RoomOutlineView outlines;
        private Material floorMaterial;
        private FloorDepthHistory floorDepth;
        private MRUKRoom decoratedRoom;
        private int decoratedRevision = -1;
        private bool busy;
        private int revision;

        public bool IsRoomLocalized
        {
            get
            {
                if (!mruk || !room) return false;
#if UNITY_EDITOR
                if (usingFixture) return true;
#endif
                var eye = rig ? rig.centerEyeAnchor : null;
                return eye && room.IsLocal && mruk.IsWorldLockActive && mruk.GetCurrentRoom() == room && room.IsPositionInRoom(eye.position, false);
            }
        }

        private void Awake()
        {
            mruk = GetComponent<MRUK>();
            if (!rig) rig = FindAnyObjectByType<OVRCameraRig>();
            outlines = new RoomOutlineView(transform, outlineShader);
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
            mruk.RoomUpdatedEvent.AddListener(RoomChanged);
            mruk.RoomRemovedEvent.AddListener(RoomChanged);
            AddAnchorListeners();
            RenderPipelineManager.beginCameraRendering += UpdateFloorDepth;
        }

        private void UpdateFloorDepth(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType == CameraType.Game && floorEffect && !floorEffect.HideMesh)
                floorDepth?.Update(Time.realtimeSinceStartupAsDouble);
        }

        private void Start() => Retry();

        private void OnDisable()
        {
            mruk.RoomUpdatedEvent.RemoveListener(RoomChanged);
            mruk.RoomRemovedEvent.RemoveListener(RoomChanged);
            RemoveAnchorListeners();
            RenderPipelineManager.beginCameraRendering -= UpdateFloorDepth;
            ShowFloorSurface(false);
        }

        private void OnDestroy()
        {
            outlines?.Dispose();
            floorDepth?.Dispose();
            if (floorMaterial) Destroy(floorMaterial);
#if UNITY_EDITOR
            if (restoreBackgroundSetting) Application.runInBackground = false;
            if (simulatorOcclusion) Destroy(simulatorOcclusion.gameObject);
#endif
        }

        public void ShowFloorSurface(bool visible)
        {
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
            floorMaterial.SetVector("_BoardHalfSize", new Vector4(fit.Columns * fit.Pitch * .5f, fit.Rows * fit.Pitch * .5f, fit.FloorHeight, 0));
            if (decoratedRoom != room || decoratedRevision != revision)
            {
                // Apply the same reviewed scan as the glass, including every native floor patch.
                floorDepth?.Clear();
                floorEffect.DestroyMesh();
                foreach (var floor in room.FloorAnchors)
                    if (floor) floorEffect.CreateEffectMesh(floor);
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
            floorEffect.HideMesh = false;
        }

        public void Retry()
        {
            if (busy) return;
            declined.Clear();
            LoadRoom(false, false);
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
            declined.Add(Placement.FootprintOutline());
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

        private async void LoadRoom(bool capture, bool reuseRoom)
        {
            busy = true;
            ScannedSpawnCount = 0;
            CancellationToken lifetime = destroyCancellationToken;
            SetState(RoomPlacementStatus.Loading, capture ? "Opening Space Setup…" : "Loading your room…");
            try
            {
                if (!reuseRoom || !room)
                {
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
                        var fixture = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(FixturePath);
                        if (!fixture) throw new InvalidOperationException("The installed MRUK Rec Room prefab is missing.");
                        result = await mruk.LoadSceneFromPrefab(fixture);
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
                        result = await mruk.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: false);
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
                while (focusedSeconds < 8f)
                {
                    if (!reuseRoom)
                    {
                        var current = mruk.GetCurrentRoom();
#if UNITY_EDITOR
                        if (!current && usingFixture && mruk.Rooms.Count == 1) current = mruk.Rooms[0];
#endif
                        SetRoom(current);
                    }
                    if (IsRoomLocalized) break;
                    if (HasPlacementFocus) focusedSeconds += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                    await Awaitable.NextFrameAsync(lifetime);
                }
                if (!IsRoomLocalized || !TryReadFloors())
                {
                    SetState(RoomPlacementStatus.UnsafeRoom, "A level floor could not be verified here. Look around from inside the scanned room or rescan it.");
                    return;
                }
                var head = rig.centerEyeAnchor.position;
                var preferred = new Vector2(head.x, head.z);
                var searchFloors = new List<RoomFloor>();
                foreach (var floor in floors)
                {
                    var obstructions = new List<Vector2[]>(floor.Obstacles);
                    obstructions.AddRange(declined);
                    searchFloors.Add(new RoomFloor(floor.Boundary, obstructions, floor.Height));
                }
                int scanRevision = revision;
                var suggestions = ReadSpawnCenters(preferred);
                BoardPlacement fit = default;
                bool found = await Task.Run(() => BoardFitter.TryFind(searchFloors, preferred, out fit, lifetime, suggestions), lifetime);
                lifetime.ThrowIfCancellationRequested();
                if (!CheckCurrentPermission()) return;
                if (revision != scanRevision || !IsRoomLocalized)
                {
                    SetState(RoomPlacementStatus.UnsafeRoom, "The room changed while checking the floor. Look around and retry.");
                    return;
                }
                if (!found)
                {
                    SetState(RoomPlacementStatus.NoFit, declined.Count > 0
                        ? "No separate clear patch fits. Move the obstruction and retry, or open Space Setup to rescan."
                        : "A 2 × 2 board needs a clear 1 × 1 m patch of scanned floor. Clear more space and rescan.");
                    return;
                }
                // Fetch the current anchors on Unity's thread again after the background search.
                if (!TryReadFloors() || !floors.Exists(floor => BoardFitter.Fits(floor, fit)))
                {
                    SetState(RoomPlacementStatus.UnsafeRoom, "The scan changed during placement. Look around and check again.");
                    return;
                }
                Placement = fit;
                int tileCm = Mathf.RoundToInt(fit.Pitch * 100f);
                SetState(RoomPlacementStatus.Confirmed,
                    $"Your room scan fits {fit.Columns} × {fit.Rows} panes at {tileCm} cm each, covering {fit.Columns * fit.Pitch:F2} × {fit.Rows * fit.Pitch:F2} m.");
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
            if (!rig || !rig.centerEyeAnchor || !rig.trackingSpace) return false;
            float y = rig.trackingSpace.position.y;
            return MrukFloorReader.TryRead(room, y, rig.centerEyeAnchor.position.y - y, out floors);
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
        private void AnchorChanged(MRUKAnchor anchor) => RoomChanged(anchor.Room);

        private void RoomChanged(MRUKRoom changedRoom)
        {
            if (!room || changedRoom != room) return;
            revision++;
            if (Status == RoomPlacementStatus.Confirmed)
                SetState(RoomPlacementStatus.UnsafeRoom, "The room scan changed. Check again to fit the board to the updated room.");
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused && Status == RoomPlacementStatus.Confirmed) CheckCurrentPermission();
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
            ShowFloorSurface(next == RoomPlacementStatus.Confirmed);
            outlines.Show(false, Placement);
            Changed?.Invoke();
        }
    }
}
