using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meta.XR;
using UnityEngine;

namespace FloorFracture
{
    public sealed partial class RoomBoardPlacement
    {
        private FloorMeshScan meshSnapshot;
        private List<FloorSurvey> surveys;
        [SerializeField] private EnvironmentRaycastManager liveManager;
        private readonly List<Vector2[]> detectedObstacles = new();
#if UNITY_ANDROID && !UNITY_EDITOR
        private static readonly Vector2[] DepthSampleOffsets = {
            Vector2.zero, new(-.3f, -.3f), new(.3f, -.3f), new(-.3f, .3f), new(.3f, .3f)
        };
        private int nextLivePane, liveSample, pendingPane = -1;
        private Vector3 pendingWorldHit;
        private double nextLiveCheck;
#endif
        [SerializeField, Tooltip("Show accepted panes in green and the boundaries of measured scan regions.")]
        private bool showPlacementDebug;
        private RoomOutlineView debugView;
        private bool displayedDebug;
        private string lastSurveyReport;
        public bool PlacementDebug => showPlacementDebug;

        public const string DebugLegend = "Green panes are playable. Blue marks the scanned floor edge. Red is mapped furniture. Orange is a supported obstacle or drop. Purple means the room mesh is unavailable. Yellow is a live obstruction, pink is uncertain depth, and brown is a declined area.";
        private void InitializePlacementDebug()
        {
            debugView = new RoomOutlineView(transform, outlineShader, "Placement diagnostics");
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Debug.isDebugBuild) return;
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var intent = activity.Call<AndroidJavaObject>("getIntent");
            showPlacementDebug |= intent.Call<bool>("getBooleanExtra", "floorFracturePlacementDebug", false);
#endif
        }

        public void TogglePlacementDebug()
        {
            showPlacementDebug = !showPlacementDebug;
            RefreshPlacementDebug();
        }

        private void RefreshPlacementDebug()
        {
            displayedDebug = showPlacementDebug;
            if (!showPlacementDebug)
            {
                debugView?.Show(false, default);
                return;
            }

            var visible = surveys;
            if (visible == null && floors != null && Status == RoomPlacementStatus.UnsafeRoom && meshSnapshot == null)
                visible = floors.ConvertAll(f => new FloorSurvey(f, null, 2.2f, null, default));
            debugView?.ShowSurvey(visible, Status == RoomPlacementStatus.Confirmed ? Placement : (BoardPlacement?)null);
            if (Status != RoomPlacementStatus.Loading)
                Debug.Log($"[Floor Fracture placement] {Status}: {Message} {DebugLegend}", this);
        }

        private bool StillLocal(int scanRevision)
        {
            if (!isActiveAndEnabled || !CheckCurrentPermission())
                return false;
            if (scanRevision == revision && IsRoomLocalized && HasPlacementFocus)
                return true;
            awaitingSavedFloor = true;
            nextFloorCheck = Time.unscaledTimeAsDouble + .5;
            SetState(RoomPlacementStatus.UnsafeRoom, "Room tracking changed. Checking the updated floor automatically.");
            return false;
        }

        private async Task<BoardPlacement?> FitSurveyAsync(Vector2 preferred, List<Vector2> suggestions, int scanRevision, CancellationToken cancel, bool measureView)
        {
            var semantic = floors;
            var snapshot = meshSnapshot;
            var excluded = declined.ToArray();
            float headroom = Mathf.Max(2.2f, rig.centerEyeAnchor.position.y - semantic[0].Height + .3f);
            var liveObstacles = detectedObstacles.ToArray();
            var evidence = await Task.Run(() => semantic.ConvertAll(f => new FloorSurvey(f, snapshot, headroom, excluded, cancel, liveObstacles)), cancel);
            cancel.ThrowIfCancellationRequested();
            if (!StillLocal(scanRevision))
                return null;
            surveys = evidence;
            if (Debug.isDebugBuild || Application.isEditor)
                LogSurvey(evidence, "saved mesh", snapshot, true);
            bool liveVeto = false;
            bool triedLiveSurface = false;
            var searchFloors = semantic.ConvertAll(f =>
            {
                var obstacles = new List<Vector2[]>(f.Obstacles);
                obstacles.AddRange(excluded);
                obstacles.AddRange(liveObstacles);
                return new RoomFloor(f.Boundary, obstacles, f.Height);
            });
            // A confirmed depth hit excludes its measured location before the next fit.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                bool first = attempt == 0;
                var result = await Task.Run<BoardPlacement?>(() =>
                {
                    BoardPlacement? rectangle = null;
                    if (first && BoardFitter.TryFindOnSurfaces(searchFloors, preferred, out var exact, cancel, suggestions, FloorSurvey.FootMargin) &&
                        evidence.Exists(s => s.ChecksEveryTile(exact))) rectangle = exact;
                    if (!rectangle.HasValue)
                    {
                        var rectangles = new List<RoomFloor>();
                        foreach (var s in evidence) s.AddClearRectangles(rectangles);
                        if (BoardFitter.TryFindOnSurfaces(rectangles, preferred, out var fitted, cancel, suggestions, FloorSurvey.FootMargin, true) &&
                            evidence.Exists(s => s.ChecksEveryTile(fitted))) rectangle = fitted;
                    }
                    if (first && !rectangle.HasValue) return null; // Refine also searches connected layouts.
                    var connected = FloorSurvey.FindConnected(evidence, preferred, cancel, out _);
                    return FloorSurvey.CoversMore(connected, rectangle) ? connected : rectangle;
                }, cancel);
                cancel.ThrowIfCancellationRequested();
                if (!StillLocal(scanRevision))
                    return null;
                if (!result.HasValue && first)
                {
                    var refined = await Task.Run(() => FloorSurvey.Refine(evidence, snapshot, headroom,
                        excluded, preferred, suggestions, cancel), cancel);
                    cancel.ThrowIfCancellationRequested();
                    if (!StillLocal(scanRevision)) return null;
                    surveys = evidence = refined.surveys;
                    result = refined.fit;
                    if (Debug.isDebugBuild || Application.isEditor)
                        LogSurvey(evidence, "refined saved mesh", snapshot, false);
                }
#if UNITY_ANDROID && !UNITY_EDITOR
                if (first && (!result.HasValue || measureView) && liveManager && EnvironmentRaycastManager.IsSupported)
                {
                    triedLiveSurface = true;
                    var floor = semantic.Find(f => FloorGeometry.Contains(f.Boundary, preferred)) ?? semantic[0];
                    var observedObstacles = new List<Vector2[]>(liveObstacles);
                    var surface = await ReadDepthSurface(floor, observedObstacles, cancel);
                    cancel.ThrowIfCancellationRequested();
                    if (!StillLocal(scanRevision)) return null;
                    if (surface != null)
                    {
                        var verified = await Task.Run(() =>
                        {
                            var current = new FloorSurvey(floor, snapshot, headroom, excluded, cancel, observedObstacles, surface);
                            return FloorSurvey.Refine(new List<FloorSurvey> { current }, snapshot, headroom,
                                excluded, preferred, suggestions, cancel);
                        }, cancel);
                        cancel.ThrowIfCancellationRequested();
                        if (!StillLocal(scanRevision)) return null;
                        var prior = result;
                        surveys = evidence = verified.surveys;
                        result = verified.fit;
                        if (prior is BoardPlacement old && evidence.Exists(s => s.ChecksEveryTile(old)) &&
                            (!result.HasValue || old.Layout.PaneCount * old.Pitch * old.Pitch >
                                result.Value.Layout.PaneCount * result.Value.Pitch * result.Value.Pitch))
                            result = prior;
                        if (Debug.isDebugBuild) LogSurvey(evidence, "verified with live floor", snapshot, false);
                    }
                }
#endif
                if (!result.HasValue) break;
                var fit = result.Value;
                bool? clear = await CheckVisibleTiles(fit, evidence, cancel);
                cancel.ThrowIfCancellationRequested();
                if (!StillLocal(scanRevision))
                    return null;
                if (!clear.HasValue)
                {
                    awaitingSavedFloor = true;
                    nextFloorCheck = Time.unscaledTimeAsDouble + 1;
                    SetState(RoomPlacementStatus.UnsafeRoom, "Look at the open floor. Checking live depth again automatically.");
                    return null;
                }

                if (!clear.Value)
                {
                    liveVeto = true;
                    if (Debug.isDebugBuild || Application.isEditor)
                        LogSurvey(evidence, "after live depth", snapshot, false);
                    continue;
                }
                // Re-read current Unity anchors and mesh before anything in GameFlow creates glass.
                if (TryReadFloors() && floors.Exists(f => BoardFitter.Fits(f, fit, FloorSurvey.FootMargin)))
                {
                    if (snapshot.Matches(meshSnapshot)) return fit;
                    // World locking can refine anchor poses while placing. Prove the selected footprint
                    // against the latest buffers in this frame so small corrections can settle at Welcome.
                    var selectedSurvey = evidence.Find(s => s.ChecksEveryTile(fit));
                    foreach (var floor in floors)
                    {
                        if (!BoardFitter.Fits(floor, fit, FloorSurvey.FootMargin)) continue;
                        if (selectedSurvey == null) break;
                        var current = selectedSurvey.Refresh(floor, meshSnapshot, headroom, excluded, cancel);
                        if (!current.ChecksEveryTile(fit)) continue;
                        surveys = new List<FloorSurvey> { current };
                        return fit;
                    }
                }
                awaitingSavedFloor = true;
                SetState(RoomPlacementStatus.UnsafeRoom, "The floor or obstacle scan changed. Checking the updated room again.");
                return null;
            }

            if (liveVeto)
                SetState(RoomPlacementStatus.NoFit, "Live depth found obstructions on the proposed panes. Clear a continuous patch of floor and choose Check again.");
            else if (triedLiveSurface)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                waitingForDepthView = true;
#endif
                SetState(RoomPlacementStatus.NoFit, "Look down at a different open stretch of floor. The game will measure it as you turn. You can also choose Check again.");
            }
            else
            {
                float largest = 0;
                foreach (var survey in evidence) largest = Mathf.Max(largest, survey.AddClearRectangles(null));
                float pitch = BoardState.CompactMinPitch, margin = 2 * FloorSurvey.FootMargin;
                SetState(RoomPlacementStatus.NoFit, $"The widest verified square was {largest:F2} m. Two side-by-side panes need a clear strip at least {2*pitch + margin:F2} × {pitch + margin:F2} m. Clear and scan one continuous patch of floor.");
            }
            return null;
        }

        // Counts only. No spatial coordinates, room identifiers or camera images enter the log.
        private void LogSurvey(List<FloorSurvey> evidence, string stage, FloorMeshScan scan, bool first)
        {
            if (first) lastSurveyReport = System.DateTime.UtcNow.ToString("O") + "\n";
            foreach (var survey in evidence)
            {
                var area = new float[(int)TileClearance.Declined + 1];
                float cellArea = survey.Step.x * survey.Step.y;
                foreach (var reason in survey.Cells) area[(int)reason] += cellArea;
                string line = $"[Floor Fracture survey] {stage}, vertices={scan.Vertices.Length}, " +
                    $"square={survey.AddClearRectangles(null):F2} m, " +
                    $"clear={area[(int)TileClearance.Clear]:F2}, furniture={area[(int)TileClearance.Furniture]:F2}, " +
                    $"meshObstacle={area[(int)TileClearance.MeshObstacle]:F2}, unobserved={area[(int)TileClearance.Unobserved]:F2}, " +
                    $"liveObstacle={area[(int)TileClearance.LiveObstacle]:F2}, liveUnknown={area[(int)TileClearance.LiveUnknown]:F2} m².";
                if (first)
                {
                    var f = survey.Floor;
                    float tracked = TrackingFloorAtEye(rig.centerEyeAnchor.position);
                    line += $" Height relative to labelled floor (cm): mesh={100*(f.MeshFloorHeight - f.LabelHeight):F1}, " +
                        $"tracking={100*(tracked - f.LabelHeight):F1}, board={100*(f.Height - f.LabelHeight):F1}.";
                }
                Debug.Log(line, this);
                lastSurveyReport += line + "\n";
            }
            SaveSurveyReport();
        }

        private void SaveSurveyReport()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(Application.persistentDataPath, "Last-floor-survey.txt"), lastSurveyReport);
            }
            catch (System.IO.IOException error) { Debug.LogWarning(error.Message, this); }
            catch (System.UnauthorizedAccessException error) { Debug.LogWarning(error.Message, this); }
#endif
        }

        // A depth ray can land on the same floor some distance past its target at a grazing angle.
        // Only a measured change of height inside the padded pane can contradict the saved mesh.
        internal static TileClearance ClassifyFloorRay(EnvironmentRaycastHit hit, BoardPlacement fit, int pane, Vector3 eye)
        {
            if (hit.status != EnvironmentRaycastHitStatus.Hit || !fit.Layout.Contains(pane))
                return TileClearance.LiveUnknown;
            Vector3 point = hit.point;
            if (!FloorGeometry.Finite(point.x) || !FloorGeometry.Finite(point.y) || !FloorGeometry.Finite(point.z))
                return TileClearance.LiveUnknown;
            Vector3 local = Quaternion.Inverse(fit.Pose.rotation) * (point - fit.Pose.position);
            float x = (pane % fit.Columns + .5f - fit.Columns * .5f) * fit.Pitch;
            float z = (pane / fit.Columns + .5f - fit.Rows * .5f) * fit.Pitch;
            float half = fit.Pitch * .5f + FloorSurvey.FootMargin;
            if (Mathf.Abs(local.x - x) > half || Mathf.Abs(local.z - z) > half) return TileClearance.LiveUnknown;
            float height = point.y - fit.FloorHeight;
            if (height >= -FloorSurvey.DropDepth && height <= FloorSurvey.RaisedHeight) return TileClearance.Clear;
            if (height >= 2.2f || (height > 0f && new Vector2(point.x - eye.x, point.z - eye.z).sqrMagnitude < .4f * .4f))
                return TileClearance.LiveUnknown;
            return TileClearance.LiveObstacle;
        }

        internal static bool IsObstacleOnPane(EnvironmentRaycastHit hit, BoardPlacement fit, int pane, Vector3 eye) =>
            ClassifyFloorRay(hit, fit, pane, eye) == TileClearance.LiveObstacle;

#if UNITY_ANDROID && !UNITY_EDITOR
        // Spread native depth queries over frames. Repeat a positive hit before interrupting a wave.
        private void CheckForNewObstacle()
        {
            double now = Time.unscaledTimeAsDouble;
            if (now < nextLiveCheck || !liveManager || !liveManager.isActiveAndEnabled) return;
            nextLiveCheck = now + .10;
            var eye = rig.centerEyeAnchor.GetComponent<Camera>();
            if (!eye || !playAreaVisible) return;
            var fit = Placement;
            for (int i = 0; i < fit.Layout.CellCount; i++)
            {
                bool confirming = pendingPane >= 0;
                int index = confirming ? pendingPane : nextLivePane;
                int sample = liveSample;
                if (!confirming && ++nextLivePane >= fit.Layout.CellCount)
                {
                    nextLivePane = 0;
                    liveSample = (liveSample + 1) % DepthSampleOffsets.Length;
                }
                if (!fit.Layout.Contains(index)) continue;
                Vector2 offset = DepthSampleOffsets[sample];
                Vector2 target = fit.ToWorldXZ(
                    (index % fit.Columns + .5f - fit.Columns * .5f + offset.x) * fit.Pitch,
                    (index / fit.Columns + .5f - fit.Rows * .5f + offset.y) * fit.Pitch);
                // Aim a confirmation ray at the height of the previous hit.
                var world = confirming ? pendingWorldHit : new Vector3(target.x, fit.FloorHeight, target.y);
                var viewport = eye.WorldToViewportPoint(world);
                pendingPane = -1;
                if (viewport.z <= 0 || viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1) continue;
                var delta = world - eye.transform.position;
                if (!liveManager.Raycast(new Ray(eye.transform.position, delta), out var hit, delta.magnitude + .12f)
                    || !IsObstacleOnPane(hit, fit, index, eye.transform.position)) return;
                var point = new Vector2(hit.point.x, hit.point.z);
                if (confirming && (hit.point - pendingWorldHit).sqrMagnitude < .08f * .08f)
                {
                    detectedObstacles.Add(new[] { point });
                    if (Debug.isDebugBuild) Debug.Log("[Floor Fracture depth] Rechecking the board after a repeated depth hit on a playable pane.", this);
                    LoadRoom(false, true);
                    return;
                }
                pendingPane = index;
                pendingWorldHit = hit.point;
                return;
            }
        }
#endif

        private async Task<bool?> CheckVisibleTiles(BoardPlacement fit, List<FloorSurvey> evidence, CancellationToken cancel)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!EnvironmentRaycastManager.IsSupported)
                return true; // The native floor and saved obstacles still constrain every pane.
            if (!liveManager) return null;
            liveManager.gameObject.SetActive(true);
            if (!liveManager.isActiveAndEnabled) return null;

            var eye = rig.centerEyeAnchor.GetComponent<Camera>();
            if (!eye)
                return null;
            int calls = 0, hits = 0, unseen = 0, warming = 0, exclusions = 0;
            async Task<EnvironmentRaycastHit?> Sample(Vector3 target)
            {
                if (++calls % 8 == 0) await Awaitable.NextFrameAsync(cancel);
                cancel.ThrowIfCancellationRequested();
                var view = eye.WorldToViewportPoint(target);
                if (view.z <= 0 || view.x < 0 || view.x > 1 || view.y < 0 || view.y > 1) return null;
                var delta = target - eye.transform.position;
                liveManager.Raycast(new Ray(eye.transform.position, delta), out var hit, delta.magnitude + .30f);
                if (hit.status == EnvironmentRaycastHitStatus.Hit) hits++;
                else if (hit.status == EnvironmentRaycastHitStatus.NotReady) warming++;
                else unseen++;
                return hit;
            }
            async Task WaitForDepth()
            {
                double until = Time.realtimeSinceStartupAsDouble + .10;
                do { await Awaitable.NextFrameAsync(cancel); } while (Time.realtimeSinceStartupAsDouble < until);
            }
            void Exclude(Vector2 point)
            {
                exclusions++;
                var observed = new[] { point };
                foreach (var survey in evidence)
                    if (Mathf.Abs(survey.Floor.Height - fit.FloorHeight) <= .025f)
                        survey.MarkWorld(observed, TileClearance.LiveObstacle);
            }
            // Allow the newly enabled Meta raycaster to warm up with the same candidate and room.
            double deadline = Time.realtimeSinceStartupAsDouble + 1.5;
            bool? result;
            do
            {
                result = await ProbeLivePanes(fit, () => eye.transform.position, Sample, WaitForDepth, Exclude);
                if (result.HasValue) break;
                await WaitForDepth();
            } while (Time.realtimeSinceStartupAsDouble < deadline);
            if (Debug.isDebugBuild)
                Debug.Log($"[Floor Fracture depth] Candidate panes={fit.Layout.PaneCount}, hits={hits}, unobserved={unseen}, warming={warming}, confirmed obstructions={exclusions}, result={result?.ToString() ?? "using mapped floor"}.", this);
            // Occlusion, an offscreen pane or a warming sensor is no positive
            // obstruction. The native floor remains valid and monitoring continues.
            return result ?? true;
#else
            await Task.CompletedTask;
            return true;
#endif
        }

        // The caller has checked the padded panes against the mapped floor and saved obstacles. Live rays sample
        // the centre, edges and interior of each padded pane for changes since that scan.
        internal static async Task<bool?> ProbeLivePanes(BoardPlacement fit, Func<Vector3> eye,
            Func<Vector3, Task<EnvironmentRaycastHit?>> sample, Func<Task> waitForDepth, Action<Vector2> exclude)
        {
            var possible = new List<(int pane, Vector3 hit)>();
            bool sawFloor = false, needsObservation = false;
            float half = fit.Pitch * .5f + FloorSurvey.FootMargin;
            for (int pane = 0; pane < fit.Layout.CellCount; pane++)
            {
                if (!fit.Layout.Contains(pane)) continue;
                float x = (pane % fit.Columns + .5f - fit.Columns * .5f) * fit.Pitch;
                float z = (pane / fit.Columns + .5f - fit.Rows * .5f) * fit.Pitch;
                for (int dz = -2; dz <= 2; dz++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        var p = fit.ToWorldXZ(x + dx * half * .5f, z + dz * half * .5f);
                        var hit = await sample(new Vector3(p.x, fit.FloorHeight, p.y));
                        if (!hit.HasValue) continue;
                        var kind = ClassifyFloorRay(hit.Value, fit, pane, eye());
                        if (kind == TileClearance.Clear) sawFloor = true;
                        else if (kind == TileClearance.LiveObstacle) possible.Add((pane, hit.Value.point));
                        // The headset render frustum is wider than the native depth frustum.
                        else if (hit.Value.status != EnvironmentRaycastHitStatus.HitPointOutsideOfCameraFrustum)
                            needsObservation = true;
                    }
            }
            if (possible.Count > 0) await waitForDepth();
            bool blocked = false;
            foreach (var candidate in possible)
            {
                var second = await sample(candidate.hit);
                if (!second.HasValue) { needsObservation = true; continue; }
                var kind = ClassifyFloorRay(second.Value, fit, candidate.pane, eye());
                if (kind == TileClearance.Clear) sawFloor = true;
                if (kind != TileClearance.LiveObstacle || (second.Value.point - candidate.hit).sqrMagnitude > .08f * .08f)
                {
                    needsObservation = true;
                    continue;
                }
                exclude(new Vector2(second.Value.point.x, second.Value.point.z));
                blocked = true;
            }
            if (blocked) return false;
            if (!sawFloor && needsObservation) return null;
            return true;
        }
    }
}
