using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FloorFracture.Editor
{
    public static partial class GameplaySmoke
    {
        private static IEnumerator ObserveSelectedFall(string ending, bool reduced, int nextEndingPresses)
        {
            Choose("Main menu"); Choose("Settings");
            if ((PlayerPrefs.GetInt("ff.reduced", 0) != 0) != reduced) Choose("Fall motion");
            if (PlayerPrefs.GetInt("ff.volume", 3) == 0) Choose("Volume");
            Choose("Back"); Choose("Development");
            for (int i = 0; i < nextEndingPresses; i++) Choose("Next ending");
            Choose("Seed 379 · level 1"); yield return Stage(GameStage.Ready);

            // A real deadline loss shatters the occupied tile before the rim shot.
            StandOn(0);
            Vector3 bearing = Vector3.ProjectOnPlane(game.BoardView.Board.position - head.position, Vector3.up);
            Vector3 eyePosition = head.position;
            eyePosition.y = game.BoardView.Board.position.y + (reduced ? 1.6f : ending == "Lava" ? 2.2f : 1.5f);
            SetHead(new Pose(eyePosition, Quaternion.LookRotation(bearing, Vector3.up) * Quaternion.Euler(32f, 0f, 0f)));
            Pose trackedAtStart = head;
            Pose rigAtStart = new Pose(game.Rig.transform.position, game.Rig.transform.rotation);
            Choose("Start wave"); yield return Stage(GameStage.Countdown);
            yield return Stage(GameStage.Fall, 8);
            Require(!game.State.IsIntact(0) && game.Ending.IsPlaying && game.Ending.LastEndingName == ending,
                "The chosen ending did not follow the real starting tile's deadline loss: " + ending);

            var witness = new FallWitness(ending, reduced, trackedAtStart, rigAtStart);
            report.falls.Add(witness.evidence);
            yield return FallFrame(witness, "rim", 0.10, SmokeView.FallRim);
            yield return FallFrame(witness, "early-shaft", reduced ? 0.40 : 1.20, reduced ? Array.Empty<SmokeView>() : new[] { SmokeView.FallForward });
            yield return FallFrame(witness, "mid-shaft", reduced ? 0.70 : 2.70, SmokeView.FallDown, SmokeView.FallForward);
            yield return FallFrame(witness, "late-shaft", reduced ? 0.76 : 4.27, SmokeView.FallDown, SmokeView.FallForward);
            yield return FallFrame(witness, "contact", witness.evidence.plannedContact + 0.12, SmokeView.FallDown, SmokeView.FallRim);
            Require(witness.SecondaryEffect(), ending + " produced no visible contact effect at the first post-impact sample.");
            witness.CheckArrival();
            double immersionAt = reduced ? 2.95 : witness.evidence.plannedContact + (ending == "Water" ? 3.70 : 3.10);
            yield return FallFrame(witness, "immersion", immersionAt, SmokeView.FallDown);
            witness.CheckImmersion();
            while (game.Stage == GameStage.Fall)
            {
                witness.Observe();
                Require(witness.Age < witness.expectedFinish + 2.0, "The selected ending did not complete naturally.");
                yield return null;
            }
            Require(game.Stage == GameStage.Results, "The ending did not reach its results screen.");
            witness.Finish();
            Choose("Restart"); yield return Stage(GameStage.Ready);
            Record(reduced
                ? ending + " reduced motion kept the chamber geometry stationary before contact and reached results."
                : $"{ending} showed the moving walls for {witness.evidence.visibleWallSeconds:F2} s after entry at {witness.evidence.eyeHeight:F2} m eye height, before impact.");
        }

        private static IEnumerator FallFrame(FallWitness witness, string phase, double at, params SmokeView[] views)
        {
            while (witness.Age < at)
            {
                Require(game.Stage == GameStage.Fall, "Ended during fall evidence phase " + phase);
                witness.Observe();
                yield return null;
            }
            witness.Sample(phase);
            foreach (SmokeView view in views)
            {
                string angle = view == SmokeView.FallForward ? "forward" : view == SmokeView.FallRim ? (phase == "rim" ? "opening" : "steep") : "down";
                Capture($"fall-{witness.evidence.ending.ToLowerInvariant()}-{witness.evidence.motion}-{phase}-{angle}", view);
            }
            Save();
        }

        private sealed class FallWitness
        {
            // Elapsed seconds from shatter and analytic metres fallen after the 0.40-second rim hold.
            private static readonly (double seconds, float metres)[] GravityCheckpoints =
            {
                (0.30, 0f), (0.65, 0.30625f), (1.40, 4.9f), (2.40, 19.6f), (3.40, 44.1f), (4.15, 68.90625f), (4.40, 78.4f)
            };
            internal readonly FallEvidence evidence;
            internal readonly double expectedFinish;
            internal double Age => Now - start;
            private readonly double start, contact;
            private readonly bool reduced, water;
            private readonly Pose eyePose, rigPose;
            private readonly Transform chamber, eye;
            private readonly MeshRenderer marks, walls, floor, flames, sky, veil;
            private readonly MaterialPropertyBlock material = new MaterialPropertyBlock();
            private readonly AudioSource wind, impact;
            private readonly float firstMarks, firstWalls, firstFloor, firstSpan;
            private int lastFrame = -1;
            private float lastTravel, speedSampleTravel;
            private double lastAge, speedSampleAge;

            internal FallWitness(string ending, bool reduced, Pose initialEye, Pose initialRig)
            {
                this.reduced = reduced; water = ending == "Water";
                start = Now; contact = reduced ? 0.82 : 4.44061; expectedFinish = reduced ? 3.93 : 9.44061;
                eyePose = initialEye; rigPose = initialRig; eye = game.Rig.centerEyeAnchor;
                var chamberObject = GameObject.Find("Fall chamber");
                Require(chamberObject, "The active fall chamber is missing.");
                chamber = chamberObject.transform;
                marks = Renderer("Shaft landmarks"); walls = Renderer("Shaft walls"); floor = Renderer("Hazard surface"); flames = Renderer("Flames or crystals");
                sky = Renderer("Chamber darkness"); veil = eye.Find("View fade").GetComponent<MeshRenderer>();
                firstMarks = reduced ? marks.transform.position.y : chamber.position.y;
                firstWalls = walls.transform.position.y; firstFloor = floor.transform.position.y;
                wind = game.transform.Find("Shaft air").GetComponent<AudioSource>();
                impact = game.transform.Find("Ending impact").GetComponent<AudioSource>();
                Require(wind.clip && !impact.clip, "The fall did not start with shaft air and a silent impact source.");
                firstSpan = marks.bounds.size.y;
                float openingY = game.BoardView.Board.position.y;
                Require(Mathf.Abs(firstFloor - (openingY - (reduced ? 0f : 80f))) < 0.10f,
                    $"The hazard began at {firstFloor:F2} m for an opening at {openingY:F2} m.");
                evidence = new FallEvidence { ending = ending, motion = reduced ? "reduced" : "full", plannedContact = contact,
                    eyeHeight = eye.position.y - openingY, landmarkSpan = firstSpan, floorOpeningY = openingY, startingHazardY = firstFloor };
                if (!reduced)
                {
                    Require(walls.enabled && marks.enabled && !veil.enabled, "The recognition beat did not expose the shaft lip and its walls.");
                    Require(sky.bounds.min.y < firstFloor && eye.GetComponent<Camera>().farClipPlane > eye.position.y - firstFloor,
                        "The camera or sky enclosure cuts off the deep destination.");
                    CheckShaftMeshes();
                }
                Observe();
            }

            private MeshRenderer Renderer(string name)
            {
                var child = chamber.Find(name);
                var renderer = child ? child.GetComponent<MeshRenderer>() : null;
                Require(renderer, "Missing fall evidence renderer: " + name);
                return renderer;
            }

            private void CheckShaftMeshes()
            {
                // Probe the actual rendered wall mesh around its circumference at every metre.
                var probe = new GameObject("Editor shaft cross sections") { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    var collider = probe.AddComponent<MeshCollider>();
                    collider.sharedMesh = walls.GetComponent<MeshFilter>().sharedMesh;
                    for (int metre = 0; metre < 80; metre++)
                    {
                        Vector3 center = Vector3.down * (metre + .25f);
                        for (int bearing = 0; bearing < 16; bearing++)
                        {
                            Vector3 outward = Quaternion.Euler(0f, 22.5f * bearing + 4f, 0f) * Vector3.forward;
                            // The chamber material renders either winding.
                            bool hit = collider.Raycast(new Ray(center, outward), out _, 2f) ||
                                collider.Raycast(new Ray(center + 2f * outward, -outward), out _, 2f);
                            Require(hit, $"A hole in the shaft wall faces {bearing * 22.5f + 4f:F0}° at {metre + .25f:F2} m depth.");
                            evidence.testedWallRays++;
                        }
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(probe); }

                var ledges = marks.GetComponent<MeshFilter>().sharedMesh;
                var vertices = ledges.vertices;
                var triangles = ledges.triangles;
                var caps = new SortedSet<float>();
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    float y = vertices[triangles[i]].y;
                    if (y == vertices[triangles[i + 1]].y && y == vertices[triangles[i + 2]].y) caps.Add(-y);
                }
                Require(caps.Count > 0 && caps.Min < .05f, "The opening is missing its visible ledge.");
                float lastDepth = 0f;
                foreach (float cap in caps)
                {
                    evidence.greatestLedgeSpacing = Mathf.Max(evidence.greatestLedgeSpacing, cap - lastDepth);
                    lastDepth = cap;
                }
                evidence.greatestLedgeSpacing = Mathf.Max(evidence.greatestLedgeSpacing, 80f - lastDepth);
                Require(evidence.greatestLedgeSpacing < 3.5f, $"The shaft has a {evidence.greatestLedgeSpacing:F2} m stretch without any physical ledges.");
            }

            internal bool SecondaryEffect()
            {
                if (!water) return flames.enabled && flames.gameObject.activeInHierarchy;
                var splash = chamber.Find("Water contact splash");
                if (!splash || !splash.gameObject.activeInHierarchy) return false;
                var system = splash.GetComponent<ParticleSystem>();
                return system && (system.isPlaying || system.IsAlive(true));
            }

            internal void Observe()
            {
                if (Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                double age = Age;
                float markY = marks.transform.position.y;
                float travel = markY - firstMarks;
                float eyeTravel = Vector3.Distance(eyePose.position, eye.position);
                float eyeAngle = Quaternion.Angle(eyePose.rotation, eye.rotation);
                evidence.maxTrackedHeadDisplacement = Mathf.Max(evidence.maxTrackedHeadDisplacement, eyeTravel);
                evidence.maxTrackedHeadRotation = Mathf.Max(evidence.maxTrackedHeadRotation, eyeAngle);
                Require(eyeTravel < 0.002f && eyeAngle < 0.2f, $"The tracked head moved {eyeTravel:F3} m / {eyeAngle:F2}° during the loss.");
                float rigTravel = Vector3.Distance(rigPose.position, game.Rig.transform.position);
                float rigAngle = Quaternion.Angle(rigPose.rotation, game.Rig.transform.rotation);
                evidence.maxRigDisplacement = Mathf.Max(evidence.maxRigDisplacement, rigTravel);
                evidence.maxRigRotation = Mathf.Max(evidence.maxRigRotation, rigAngle);
                Require(rigTravel < 0.002f && rigAngle < 0.2f, $"The XR rig moved {rigTravel:F3} m / {rigAngle:F2}° during the loss.");
                bool effect = SecondaryEffect();
                bool entered = !game.BoardView.gameObject.activeSelf;
                if (entered && evidence.chamberEnteredAt < 0) evidence.chamberEnteredAt = age;
                if (!reduced)
                {
                    Require(entered == (walls.transform.position.y >= eye.position.y), $"The board/chamber handoff missed the rim passing the eye at {age:F3} s.");
                    CheckGravity(age, travel);
                    if (floor.transform.position.y >= evidence.floorOpeningY - .0001f && evidence.surfaceReachedAt < 0)
                        evidence.surfaceReachedAt = age;
                    ObserveLighting(age, entered);
                }
                if (impact.clip && evidence.impactSoundAt < 0)
                {
                    evidence.impactSoundAt = age;
                    evidence.windAtImpact = wind.volume;
                    Require(entered && (reduced || evidence.surfaceReachedAt >= 0),
                        $"The impact sound started before the chamber entry or surface arrival at {age:F3} s.");
                }
                if (effect && evidence.firstSecondaryEffectAt < 0)
                {
                    evidence.firstSecondaryEffectAt = age;
                    evidence.contactHazardY = floor.transform.position.y;
                    Require(evidence.impactSoundAt >= 0 && Mathf.Abs(evidence.contactHazardY - evidence.floorOpeningY) < 0.04f,
                        $"The hazard appeared at {evidence.contactHazardY:F3} m before the landing cue at {evidence.floorOpeningY:F3} m.");
                }
                if (reduced ? age < contact - 0.025 : evidence.surfaceReachedAt < 0)
                {
                    float floorError = Mathf.Abs((floor.transform.position.y - firstFloor) - travel);
                    float wallError = Mathf.Abs((walls.transform.position.y - firstWalls) - travel);
                    float spacingError = Mathf.Abs(marks.bounds.size.y - firstSpan);
                    evidence.maxPreContactOffsetError = Mathf.Max(evidence.maxPreContactOffsetError, Mathf.Max(floorError, Mathf.Max(wallError, spacingError)));
                    Require(evidence.maxPreContactOffsetError < 0.035f, $"Shaft, bands, or floor diverged by {evidence.maxPreContactOffsetError:F3} m before contact at {age:F3} s.");
                    Require(!effect && !impact.clip, $"A secondary hazard or impact sound started early at {age:F3} s.");
                    if (reduced) Require(Mathf.Abs(travel) < 0.04f, "The reduced tableau made a sustained vertical movement before contact.");
                }
                lastTravel = travel; lastAge = age;
            }

            private void ObserveLighting(double age, bool entered)
            {
                if (entered && evidence.surfaceReachedAt < 0)
                {
                    walls.GetPropertyBlock(material);
                    Require(walls.enabled && marks.enabled && walls.bounds.Contains(eye.position) && material.GetFloat("_Opacity") > .99f && !veil.enabled,
                        $"The eye lost its clear view of the moving shaft at {age:F3} s.");
                    evidence.visibleWallSeconds = age - evidence.chamberEnteredAt;
                    if (sky.enabled)
                    {
                        sky.GetPropertyBlock(material);
                        float darkness = material.GetFloat("_Opacity");
                        if (darkness >= .05f && evidence.skyFirstDimAt < 0) evidence.skyFirstDimAt = age;
                        if (darkness >= .95f && evidence.skyMostlyDarkAt < 0) evidence.skyMostlyDarkAt = age;
                    }
                }
                if (!water && age >= contact && veil.enabled)
                {
                    veil.GetPropertyBlock(material);
                    float darkness = material.GetColor("_Tint").a;
                    if (darkness >= .05f && evidence.fadeStartedAt < 0) evidence.fadeStartedAt = age;
                    if (darkness >= .95f && evidence.fadeDarkAt < 0) evidence.fadeDarkAt = age;
                }
            }

            private void CheckGravity(double age, float travel)
            {
                // Interpolate the measured mesh positions between rendered frames at each reference time.
                while (evidence.gravityChecks.Count < GravityCheckpoints.Length)
                {
                    var target = GravityCheckpoints[evidence.gravityChecks.Count];
                    if (age < target.seconds || age <= lastAge) break;
                    float between = (float)((target.seconds - lastAge) / (age - lastAge));
                    float measured = Mathf.Lerp(lastTravel, travel, between);
                    evidence.gravityChecks.Add(new GravityMeasurement { seconds = target.seconds, expectedMetres = target.metres, measuredMetres = measured });
                    Require(Mathf.Abs(measured - target.metres) < 0.10f,
                        $"The shaft travelled {measured:F3} m at {target.seconds:F2} s. Expected {target.metres:F3} m.");
                }

                double interval = age - speedSampleAge;
                if (travel >= 79.999f || interval < 0.025) return;
                float speed = (travel - speedSampleTravel) / (float)interval;
                Require(speed >= evidence.peakAirborneSpeed - 0.12f, $"The shaft slowed from {evidence.peakAirborneSpeed:F2} to {speed:F2} m/s before contact.");
                evidence.peakAirborneSpeed = Mathf.Max(evidence.peakAirborneSpeed, speed);
                speedSampleTravel = travel; speedSampleAge = age;
            }

            internal void Sample(string phase)
            {
                Observe();
                evidence.samples.Add(new FallMeasurement { phase = phase, seconds = Age, landmarkY = marks.transform.position.y,
                    wallY = walls.transform.position.y, floorY = floor.transform.position.y, landmarkTravel = marks.transform.position.y - firstMarks,
                    windVolume = wind.volume, landmarksVisible = marks.enabled && marks.gameObject.activeInHierarchy, secondaryEffectActive = SecondaryEffect() });
            }

            internal void CheckArrival()
            {
                Require(evidence.chamberEnteredAt >= 0 && evidence.chamberEnteredAt < evidence.impactSoundAt &&
                    evidence.impactSoundAt >= contact - .015 && evidence.impactSoundAt <= contact + .15 &&
                    evidence.firstSecondaryEffectAt >= evidence.impactSoundAt && evidence.firstSecondaryEffectAt < evidence.impactSoundAt + .15,
                    $"Entry, impact sound and hazard appeared at {evidence.chamberEnteredAt:F3}, {evidence.impactSoundAt:F3} and {evidence.firstSecondaryEffectAt:F3} s.");
                if (reduced) return;
                Require(Math.Abs(evidence.surfaceReachedAt - evidence.impactSoundAt) < .015 && Mathf.Abs(marks.transform.position.y - firstMarks - 80f) < .025f,
                    $"The shaft landed at {evidence.surfaceReachedAt:F3} s after travelling {marks.transform.position.y - firstMarks:F3} m.");
                Require(evidence.visibleWallSeconds >= 3.0, $"Only {evidence.visibleWallSeconds:F2} s of clearly visible walls passed the {evidence.eyeHeight:F2} m eye before impact.");
                Require(evidence.skyMostlyDarkAt - evidence.skyFirstDimAt > 1.0, "The room light above the shaft disappeared too quickly.");
            }

            internal void CheckImmersion()
            {
                if (!water)
                {
                    Require(SecondaryEffect() && floor.transform.position.y > evidence.floorOpeningY + 0.5f,
                        "Lava's post-impact rise and surrounding flames were missing from the immersion frame.");
                    return;
                }
                veil.GetPropertyBlock(material);
                evidence.immersionTintAlpha = material.GetColor("_Tint").a;
                Require(veil.enabled && evidence.immersionTintAlpha > 0.25f, "The Water immersion frame did not contain the underwater tint.");
                Require(reduced ? Mathf.Abs(floor.transform.position.y - evidence.floorOpeningY) < 0.04f : floor.transform.position.y > eye.position.y + 0.04f,
                    "The Water immersion frame did not correspond to the expected tracked-eye and surface heights.");
            }

            internal void Finish()
            {
                evidence.finishedAt = Age;
                Require(Math.Abs(evidence.finishedAt - expectedFinish) < 0.65, $"The {evidence.motion} ending finished at {Age:F2} s.");
                if (!reduced)
                {
                    var mid = evidence.samples.Find(s => s.phase == "mid-shaft");
                    var late = evidence.samples.Find(s => s.phase == "late-shaft");
                    Require(evidence.gravityChecks.Count == GravityCheckpoints.Length && evidence.peakAirborneSpeed > 38f && evidence.peakAirborneSpeed < 40f,
                        $"The final measured airborne speed was {evidence.peakAirborneSpeed:F2} m/s.");
                    Require(mid.landmarksVisible && late.landmarksVisible && !late.secondaryEffectActive && late.landmarkTravel > 70f,
                        $"The shaft landmarks or approaching surface were missing before contact at {late.seconds:F3} s.");
                    if (!water)
                        Require(evidence.fadeDarkAt - evidence.fadeStartedAt > 1.15 && evidence.fadeStartedAt - contact > 3.0,
                            $"The final veil darkened from 5% to 95% between {evidence.fadeStartedAt:F2} and {evidence.fadeDarkAt:F2} s.");
                    Require(evidence.windAtImpact > 0f, "The shaft air was silent on arrival.");
                    foreach (var sample in evidence.samples)
                        if (sample.seconds < contact)
                            Require(Math.Abs(sample.windVolume / evidence.windAtImpact - Math.Max(0, sample.seconds - .40) / (contact - .40)) < .06,
                                $"The wind did not follow falling speed at {sample.seconds:F3} s.");
                }
            }
        }

        [Serializable]
        private sealed class FallEvidence
        {
            public string ending, motion;
            public double plannedContact, finishedAt, chamberEnteredAt = -1, surfaceReachedAt = -1, impactSoundAt = -1, firstSecondaryEffectAt = -1;
            public double visibleWallSeconds, skyFirstDimAt = -1, skyMostlyDarkAt = -1, fadeStartedAt = -1, fadeDarkAt = -1;
            public float eyeHeight, greatestLedgeSpacing;
            public int testedWallRays;
            public float landmarkSpan, floorOpeningY, startingHazardY, contactHazardY, immersionTintAlpha, maxPreContactOffsetError;
            public float maxTrackedHeadDisplacement, maxTrackedHeadRotation, maxRigDisplacement, maxRigRotation, peakAirborneSpeed, windAtImpact;
            public List<FallMeasurement> samples = new List<FallMeasurement>();
            public List<GravityMeasurement> gravityChecks = new List<GravityMeasurement>();
        }

        [Serializable]
        private sealed class GravityMeasurement
        {
            public double seconds;
            public float expectedMetres, measuredMetres;
        }

        [Serializable]
        private sealed class FallMeasurement
        {
            public string phase;
            public double seconds;
            public float landmarkY, wallY, floorY, landmarkTravel, windVolume;
            public bool landmarksVisible, secondaryEffectActive;
        }
    }
}
