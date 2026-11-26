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
            // An Entire Floor toggle restarts the fit in LoadRoom's finally; it is not a tracking change.
            if (refitRequested) return false;
            if (scanRevision == revision && IsRoomLocalized && HasPlacementFocus)
                return true;
            awaitingSavedFloor = true;
            nextFloorCheck = Time.unscaledTimeAsDouble + .5;
            SetState(RoomPlacementStatus.UnsafeRoom, "Room tracking changed. Checking the updated floor automatically.");
            return false;
        }

        private async Task<BoardPlacement?> FitSurveyAsync(Vector2 preferred, List<Vector2> suggestions, int scanRevision, CancellationToken cancel, bool measureView)
        {
            bool entireFloor = EntireFloor;
            var semantic = floors;
            var snapshot = meshSnapshot;
            var excluded = declined.ToArray();
            float headroom = Mathf.Max(2.2f, rig.centerEyeAnchor.position.y - semantic[0].Height + .3f);
            var evidence = await Task.Run(() => semantic.ConvertAll(f => new FloorSurvey(f, snapshot, headroom, excluded, cancel)), cancel);
            cancel.ThrowIfCancellationRequested();
            if (!StillLocal(scanRevision))
                return null;
            surveys = evidence;
            if (Debug.isDebugBuild || Application.isEditor)
                LogSurvey(evidence, "saved mesh", snapshot, true);
            bool liveVeto = false;
            bool triedLiveSurface = false;
            // Refit the same padded pane geometry after every live veto, retaining the veto
            // through finer grids and alternate headings.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var refined = await Task.Run(() => FloorSurvey.Refine(evidence, snapshot, headroom,
                    excluded, preferred, suggestions, cancel, entireFloor), cancel);
                cancel.ThrowIfCancellationRequested();
                if (!StillLocal(scanRevision)) return null;
                surveys = evidence = refined.surveys;
                var result = refined.fit;
#if UNITY_ANDROID && !UNITY_EDITOR
                if (attempt == 0 && (!result.HasValue || measureView) && liveManager && EnvironmentRaycastManager.IsSupported)
                {
                    triedLiveSurface = true;
                    var floor = semantic.Find(f => FloorGeometry.Contains(f.Boundary, preferred)) ?? semantic[0];
                    var observedObstacles = new List<Vector2[]>();
                    var surface = await ReadDepthSurface(floor, observedObstacles, cancel);
                    cancel.ThrowIfCancellationRequested();
                    if (!StillLocal(scanRevision)) return null;
                    if (surface != null)
                    {
                        var verified = await Task.Run(() =>
                        {
                            // The depth surface describes the viewed floor. Retain all other
                            // surveyed floor patches and prove candidates against their own data.
                            var current = new List<FloorSurvey>(evidence);
                            int index = current.FindIndex(s => ReferenceEquals(s.Floor, floor));
                            current[index] = new FloorSurvey(floor, snapshot, headroom, excluded, cancel, observedObstacles, surface);
                            return FloorSurvey.Refine(current, snapshot, headroom, excluded, preferred, suggestions, cancel, entireFloor);
                        }, cancel);
                        cancel.ThrowIfCancellationRequested();
                        if (!StillLocal(scanRevision)) return null;
                        surveys = evidence = verified.surveys;
                        result = verified.fit;
                        if (Debug.isDebugBuild) LogSurvey(evidence, "verified with live floor", snapshot, false);
                    }
                }
#endif
                if (!result.HasValue) break;
                var fit = result.Value;
                var (clear, grounded) = await CheckVisibleTiles(fit, evidence, cancel);
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
                    var selectedSurvey = evidence.Find(s => s.ChecksEveryTile(fit));
                    if (selectedSurvey != null && snapshot.Matches(meshSnapshot))
                        return grounded;
                    // World locking can refine anchor poses while placing. Prove the selected footprint
                    // against the latest buffers off the main thread, then accept only if they did not move again.
                    var latestFloors = floors;
                    var latest = meshSnapshot;
                    var current = selectedSurvey == null ? null : await Task.Run(() =>
                    {
                        foreach (var floor in latestFloors)
                        {
                            if (!BoardFitter.Fits(floor, fit, FloorSurvey.FootMargin)) continue;
                            var refreshed = selectedSurvey.Refresh(floor, latest, headroom, excluded, cancel);
                            if (refreshed.ChecksEveryTile(fit)) return refreshed;
                        }
                        return null;
                    }, cancel);
                    cancel.ThrowIfCancellationRequested();
                    if (!StillLocal(scanRevision)) return null;
                    if (current != null && TryReadFloors() && latest.Matches(meshSnapshot))
                    {
                        surveys = new List<FloorSurvey> { current };
                        return grounded;
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


        private async Task<(bool? clear, BoardPlacement grounded)> CheckVisibleTiles(BoardPlacement fit, List<FloorSurvey> evidence, CancellationToken cancel)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!EnvironmentRaycastManager.IsSupported)
                return (true, fit); // The native floor and saved obstacles still constrain every pane.
            if (!liveManager) return (null, fit);
            liveManager.gameObject.SetActive(true);
            if (!liveManager.isActiveAndEnabled) return (null, fit);

            var eye = rig.centerEyeAnchor.GetComponent<Camera>();
            if (!eye)
                return (null, fit);
            int calls = 0, hits = 0, unseen = 0, warming = 0, exclusions = 0;
            async Task<EnvironmentRaycastHit?> Sample(Vector3 target)
            {
                cancel.ThrowIfCancellationRequested();
                var view = eye.WorldToViewportPoint(target);
                if (view.z <= 0 || view.x < 0 || view.x > 1 || view.y < 0 || view.y > 1) return null;
                if (++calls % 8 == 0) await Awaitable.NextFrameAsync(cancel);
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
                foreach (var survey in evidence)
                    if (Mathf.Abs(survey.Floor.Height - fit.FloorHeight) <= .025f)
                        survey.RecordLiveObstacle(point);
            }
            // Allow the newly enabled Meta raycaster to warm up with the same candidate and room.
            double deadline = Time.realtimeSinceStartupAsDouble + 1.5;
            bool? result;
            var grounded = fit;
            do
            {
                (result, grounded) = await ProbeGroundedPanes(fit, () => eye.transform.position, Sample, WaitForDepth, Exclude);
                if (result.HasValue) break;
                await WaitForDepth();
            } while (Time.realtimeSinceStartupAsDouble < deadline);
            if (Debug.isDebugBuild)
                Debug.Log($"[Floor Fracture depth] Candidate panes={fit.Layout.PaneCount}, hits={hits}, unobserved={unseen}, warming={warming}, confirmed obstructions={exclusions}, floor adjustment={(grounded.FloorHeight-fit.FloorHeight)*100:F1} cm, result={result?.ToString() ?? "using mapped floor"}.", this);
            // Occlusion, an offscreen pane or a warming sensor is no positive
            // obstruction. The native floor remains valid and monitoring continues.
            return (result ?? true, grounded);
#else
            await Task.CompletedTask;
            return (true, fit);
#endif
        }

        internal static async Task<(bool? clear, BoardPlacement grounded)> ProbeGroundedPanes(BoardPlacement fit,
            Func<Vector3> eye, Func<Vector3, Task<EnvironmentRaycastHit?>> sample, Func<Task> waitForDepth, Action<Vector2> exclude)
        {
            var samples = new List<(int pane, Vector3 point)>();
            var clear = await ProbeLivePanes(fit, eye, sample, waitForDepth, exclude, samples);
            if (clear != true || !(LowerFloorHeight(fit, samples) is float height)) return (clear, fit);

            await waitForDepth();
            var candidate = new BoardPlacement(fit.Center, height, fit.AngleDegrees, fit.Layout, fit.Pitch);
            samples.Clear();
            var confirmed = await ProbeLivePanes(candidate, eye, sample, waitForDepth, exclude, samples);
            if (confirmed == false) return (false, fit);
            if (confirmed == true && LowerFloorHeight(fit, samples) is float repeat && Mathf.Abs(repeat - height) <= .012f)
                return (true, candidate);
            return (true, fit);
        }

        // One blinded ray or a small low spot cannot pull the board down. Only count
        // distinct physical locations on the fitted glass, with broad agreement across panes.
        internal static float? LowerFloorHeight(BoardPlacement fit, List<(int pane, Vector3 point)> samples)
        {
            var cells = new HashSet<Vector2Int>();
            var readings = new List<(int pane, float height)>();
            foreach (var sample in samples)
            {
                var point = sample.point;
                var cell = new Vector2Int(Mathf.FloorToInt(point.x * 10), Mathf.FloorToInt(point.z * 10));
                if (cells.Add(cell)) readings.Add((sample.pane, point.y));
            }
            if (readings.Count < 12) return null;
            readings.Sort((a, b) => a.height.CompareTo(b.height));
            float median = readings[readings.Count / 2].height;
            if (median >= fit.FloorHeight - .003f || median < fit.FloorHeight - FloorSurvey.DropDepth) return null;
            var votes = new Dictionary<int, int>();
            int supportedPanes = 0;
            int agreeing = 0;
            foreach (var reading in readings)
                if (Mathf.Abs(reading.height - median) <= .015f)
                {
                    votes.TryGetValue(reading.pane, out int count);
                    votes[reading.pane] = count + 1;
                    if (count + 1 == 3) supportedPanes++;
                    agreeing++;
                }
            return supportedPanes >= 2 && agreeing >= 12 && agreeing >= readings.Count * .75f ? median : (float?)null;
        }

        // The caller has checked the padded panes against the mapped floor and saved obstacles. Live rays sample
        // the centre, edges and interior of each padded pane for changes since that scan.
        internal static async Task<bool?> ProbeLivePanes(BoardPlacement fit, Func<Vector3> eye,
            Func<Vector3, Task<EnvironmentRaycastHit?>> sample, Func<Task> waitForDepth, Action<Vector2> exclude, List<(int pane, Vector3 point)> floorSamples = null)
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
                        if (kind == TileClearance.Clear)
                        {
                            sawFloor = true;
                            floorSamples?.Add((pane, hit.Value.point));
                        }
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
