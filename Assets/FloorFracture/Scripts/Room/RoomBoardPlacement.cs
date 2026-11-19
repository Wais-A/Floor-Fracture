using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace FloorFracture
{
    public enum RoomPlacementStatus { Loading, PermissionDenied, NoRoom, UnsafeRoom, NoFit, Preview, Confirmed }

    [DisallowMultipleComponent, RequireComponent(typeof(MRUK))]
    public sealed class RoomBoardPlacement : MonoBehaviour
    {
        [SerializeField] private OVRCameraRig rig;
        [SerializeField] private Shader outlineShader = null;
#if UNITY_EDITOR
        [Tooltip("Load the installed MRUK Rec Room sample and draw its scanned outlines in Play Mode.")]
        [SerializeField] private bool useEditorFixture = true;
        internal const string FixturePath = "Packages/com.meta.xr.mrutilitykit/Core/Rooms/Prefabs/Other/OtherRoom02_RecRoom.prefab";
#endif
        public RoomPlacementStatus Status { get; private set; } = RoomPlacementStatus.Loading;
        public string Message { get; private set; } = "Loading your room…";
        public BoardPlacement Placement { get; private set; }
        public event Action Changed;

        private const string DeniedMessage = "Allow spatial data for Floor Fracture in app permissions, then retry.";
        private const string MissingRoomMessage = "No saved room here. Open Space Setup, scan the floor and furniture, then return.";
        private readonly List<Vector2[]> declined = new List<Vector2[]>();
        private MRUK mruk;
        private MRUKRoom room;
        private List<RoomFloor> floors;
        private RoomOutlineView outlines;
        private bool busy;
        private int revision;

        public bool IsRoomLocalized
        {
            get
            {
                if (!mruk || !room) return false;
#if UNITY_EDITOR
                if (useEditorFixture) return true;
#endif
                var eye = rig ? rig.centerEyeAnchor : null;
                return eye && mruk.IsWorldLockActive && mruk.GetCurrentRoom() == room && room.IsPositionInRoom(eye.position, false);
            }
        }

        private void Awake()
        {
            mruk = GetComponent<MRUK>();
            if (!rig) rig = FindAnyObjectByType<OVRCameraRig>();
            outlines = new RoomOutlineView(transform, outlineShader);
        }

        private void OnEnable()
        {
            mruk.RoomUpdatedEvent.AddListener(RoomChanged);
            mruk.RoomRemovedEvent.AddListener(RoomChanged);
        }

        private void Start() => Retry();

        private void OnDisable()
        {
            mruk.RoomUpdatedEvent.RemoveListener(RoomChanged);
            mruk.RoomRemovedEvent.RemoveListener(RoomChanged);
        }

        private void OnDestroy() => outlines?.Dispose();

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
            if (busy || Status != RoomPlacementStatus.Preview) return;
            // The missed object could be anywhere in this rectangle. A replacement must
            // leave the entire declined area outside its clearance.
            declined.Add(Placement.ClearanceOutline());
            LoadRoom(false, true);
        }

        public bool Confirm()
        {
            if (Status != RoomPlacementStatus.Preview) return false;
            if (!IsRoomLocalized || !TryReadFloors() || !floors.Exists(f => BoardFitter.Fits(f, Placement)))
            {
                SetState(RoomPlacementStatus.UnsafeRoom, "The scan or tracking changed. Look around the room, then retry placement.");
                return false;
            }
            SetState(RoomPlacementStatus.Confirmed, "Play area confirmed.");
            return true;
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
            CancellationToken lifetime = destroyCancellationToken;
            SetState(RoomPlacementStatus.Loading, capture ? "Opening Space Setup…" : "Loading your room…");
            try
            {
                if (!reuseRoom || !room)
                {
                    room = null;
                    floors = null;
                    MRUK.LoadDeviceResult result;
#if UNITY_EDITOR
                    if (useEditorFixture)
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
                    room = mruk.GetCurrentRoom();
#if UNITY_EDITOR
                    if (!room && useEditorFixture && mruk.Rooms.Count == 1) room = mruk.Rooms[0];
#endif
                }
                if (!room)
                {
                    SetState(RoomPlacementStatus.NoRoom, MissingRoomMessage);
                    return;
                }
                // MRUK may report a saved room before the headset localizes its anchors.
                float deadline = Time.realtimeSinceStartup + 4f;
                while (!IsRoomLocalized && Time.realtimeSinceStartup < deadline)
                    await Awaitable.NextFrameAsync(lifetime);
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
                BoardPlacement fit = default;
                bool found = await Task.Run(() => BoardFitter.TryFind(searchFloors, preferred, out fit, lifetime), lifetime);
                lifetime.ThrowIfCancellationRequested();
                if (revision != scanRevision || !IsRoomLocalized)
                {
                    SetState(RoomPlacementStatus.UnsafeRoom, "The room changed while checking the floor. Look around and retry.");
                    return;
                }
                if (!found)
                {
                    SetState(RoomPlacementStatus.NoFit, declined.Count > 0
                        ? "No separate clear patch fits. Move the obstruction and retry, or open Space Setup to rescan."
                        : "A 2 × 2 board needs 1.8 × 1.8 m of clear floor. Clear more space and rescan.");
                    return;
                }
                Placement = fit;
                SetState(RoomPlacementStatus.Preview,
                    $"Check the {fit.Columns} × {fit.Rows} grid and its outer clearance. Confirm only if the whole area is clear.");
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

        private void RoomChanged(MRUKRoom changedRoom)
        {
            if (!room || changedRoom != room) return;
            revision++;
            if (Status == RoomPlacementStatus.Preview || Status == RoomPlacementStatus.Confirmed)
                SetState(RoomPlacementStatus.UnsafeRoom, "The room scan changed. Retry to check and confirm a new play area.");
        }

        private void OnApplicationFocus(bool focused)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (focused && (Status == RoomPlacementStatus.Preview || Status == RoomPlacementStatus.Confirmed) &&
                !Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission))
                SetState(RoomPlacementStatus.PermissionDenied, DeniedMessage);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static async Task<bool> HasSpatialPermission(CancellationToken lifetime)
        {
            const string permission = OVRPermissionsRequester.ScenePermission;
            if (Permission.HasUserAuthorizedPermission(permission)) return true;
            var result = new TaskCompletionSource<bool>();
            using var cancellation = lifetime.Register(() => result.TrySetCanceled());
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => result.TrySetResult(true);
            callbacks.PermissionDenied += _ => result.TrySetResult(false);
            callbacks.PermissionRequestDismissed += _ => result.TrySetResult(false);
            Permission.RequestUserPermission(permission, callbacks);
            return await result.Task;
        }
#endif

        private void SetState(RoomPlacementStatus next, string message)
        {
            Status = next;
            Message = message;
            bool showFixture = false;
#if UNITY_EDITOR
            showFixture = useEditorFixture && (next == RoomPlacementStatus.Preview || next == RoomPlacementStatus.NoFit);
#endif
            outlines.Show(next == RoomPlacementStatus.Preview, Placement, showFixture ? floors : null);
            Changed?.Invoke();
        }
    }
}
