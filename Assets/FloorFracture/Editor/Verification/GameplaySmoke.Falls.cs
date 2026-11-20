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
            SetHead(new Pose(head.position, Quaternion.LookRotation(bearing, Vector3.up) * Quaternion.Euler(32f, 0f, 0f)));
            Pose trackedAtStart = head;
            Pose rigAtStart = new Pose(game.Rig.transform.position, game.Rig.transform.rotation);
            Choose("Start wave"); yield return Stage(GameStage.Countdown);
            yield return Stage(GameStage.Fall, 8);
            Require(!game.State.IsIntact(0) && game.Ending.IsPlaying && game.Ending.LastEndingName == ending,
                "The chosen ending did not follow the real starting tile's deadline loss: " + ending);

            var witness = new FallWitness(ending, reduced, trackedAtStart, rigAtStart);
            report.falls.Add(witness.evidence);
            yield return FallFrame(witness, "rim", 0.10, SmokeView.FallRim);
            yield return FallFrame(witness, "early-shaft", reduced ? 0.40 : 0.25);
            yield return FallFrame(witness, "mid-shaft", reduced ? 0.70 : 0.67, SmokeView.FallDown, SmokeView.FallForward);
            yield return FallFrame(witness, "late-shaft", reduced ? 0.76 : 0.97, SmokeView.FallDown);
            yield return FallFrame(witness, "contact", witness.evidence.plannedContact + 0.12, SmokeView.FallDown, SmokeView.FallRim);
            Require(witness.SecondaryEffect(), ending + " produced no visible contact effect at the first post-impact sample.");
            witness.CheckArrival();
            double immersionAt = reduced ? 2.95 : witness.evidence.plannedContact + (ending == "Water" ? 2.65 : 2.30);
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
                : ending + " traversed five metres under gravity. The rim passed the stationary eye before the impact sound and hazard effect.");
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
            // Analytic reference distances in metres for a release from rest at 9.8 m/s².
            private static readonly (double seconds, float metres)[] GravityCheckpoints =
            {
                (0.25, 0.30625f), (0.50, 1.225f), (0.75, 2.75625f), (0.90, 3.969f), (0.96, 4.51584f)
            };
            internal readonly FallEvidence evidence;
            internal readonly double expectedFinish;
            internal double Age => Now - start;
            private readonly double start, contact;
            private readonly bool reduced, water;
            private readonly Pose eyePose, rigPose;
            private readonly Transform chamber, eye;
            private readonly MeshRenderer marks, walls, floor, flames;
            private readonly AudioSource wind, impact;
            private readonly float firstMarks, firstWalls, firstFloor, firstSpan;
            private int lastFrame = -1;
            private float lastTravel, speedSampleTravel;
            private double lastAge, speedSampleAge;

            internal FallWitness(string ending, bool reduced, Pose initialEye, Pose initialRig)
            {
                this.reduced = reduced; water = ending == "Water";
                start = Now; contact = reduced ? 0.82 : 1.01015; expectedFinish = reduced ? 3.93 : 4.58015;
                eyePose = initialEye; rigPose = initialRig; eye = game.Rig.centerEyeAnchor;
                var chamberObject = GameObject.Find("Fall chamber");
                Require(chamberObject, "The active fall chamber is missing.");
                chamber = chamberObject.transform;
                marks = Renderer("Shaft landmarks"); walls = Renderer("Shaft walls"); floor = Renderer("Hazard surface"); flames = Renderer("Flames or crystals");
                firstMarks = reduced ? marks.transform.position.y : chamber.position.y;
                firstWalls = walls.transform.position.y; firstFloor = floor.transform.position.y;
                wind = game.transform.Find("Shaft air").GetComponent<AudioSource>();
                impact = game.transform.Find("Ending impact").GetComponent<AudioSource>();
                Require(wind.clip && !impact.clip, "The fall did not start with shaft air and a silent impact source.");
                firstSpan = marks.bounds.size.y;
                Require(firstSpan > 2f, "The shaft landmark mesh lacks vertically separated geometry.");
                float openingY = game.BoardView.Board.position.y;
                Require(Mathf.Abs(firstFloor - (openingY - (reduced ? 0f : 5f))) < 0.10f,
                    $"The hazard began at {firstFloor:F2} m for an opening at {openingY:F2} m.");
                evidence = new FallEvidence { ending = ending, motion = reduced ? "reduced" : "full", plannedContact = contact,
                    landmarkSpan = firstSpan, floorOpeningY = openingY, startingHazardY = firstFloor };
                Observe();
            }

            private MeshRenderer Renderer(string name)
            {
                var child = chamber.Find(name);
                var renderer = child ? child.GetComponent<MeshRenderer>() : null;
                Require(renderer, "Missing fall evidence renderer: " + name);
                return renderer;
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
                    Require(Mathf.Abs(measured - target.metres) < 0.065f,
                        $"The shaft travelled {measured:F3} m at {target.seconds:F2} s. Expected {target.metres:F3} m.");
                }

                double interval = age - speedSampleAge;
                if (travel >= 4.999f || interval < 0.025) return;
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
                Require(Math.Abs(evidence.surfaceReachedAt - evidence.impactSoundAt) < .015 && Mathf.Abs(marks.transform.position.y - firstMarks - 5f) < .025f,
                    $"The shaft landed at {evidence.surfaceReachedAt:F3} s after travelling {marks.transform.position.y - firstMarks:F3} m.");
            }

            internal void CheckImmersion()
            {
                if (!water)
                {
                    Require(SecondaryEffect() && floor.transform.position.y > evidence.floorOpeningY + 0.5f,
                        "Lava's post-impact rise and surrounding flames were missing from the immersion frame.");
                    return;
                }
                var veil = eye.Find("View fade").GetComponent<MeshRenderer>();
                var material = new MaterialPropertyBlock();
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
                    Require(evidence.gravityChecks.Count == GravityCheckpoints.Length && evidence.peakAirborneSpeed > 9f && evidence.peakAirborneSpeed < 10f,
                        $"The final measured airborne speed was {evidence.peakAirborneSpeed:F2} m/s.");
                    Require(mid.landmarksVisible && late.landmarksVisible && !late.secondaryEffectActive && late.landmarkTravel > 4.4f,
                        $"The shaft landmarks or approaching surface were missing before contact at {late.seconds:F3} s.");
                    Require(evidence.windAtImpact > 0f, "The shaft air was silent on arrival.");
                    foreach (var sample in evidence.samples)
                        if (sample.seconds < contact)
                            Require(Math.Abs(sample.windVolume / evidence.windAtImpact - sample.seconds / contact) < .06,
                                $"The wind did not follow falling speed at {sample.seconds:F3} s.");
                }
            }
        }

        [Serializable]
        private sealed class FallEvidence
        {
            public string ending, motion;
            public double plannedContact, finishedAt, chamberEnteredAt = -1, surfaceReachedAt = -1, impactSoundAt = -1, firstSecondaryEffectAt = -1;
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
