using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FloorFracture.Editor
{
    public static partial class GameplaySmoke
    {
        private static readonly string[] EndingNames = { "Water", "Lava", "Tar Pit", "Space", "Sand", "Snow" };

        private static IEnumerator VerifyEveryEndingExit()
        {
            foreach (bool reduced in new[] { false, true })
            {
                Choose("Main menu");
                Choose("Settings");
                if ((PlayerPrefs.GetInt("ff.reduced", 0) != 0) != reduced) Choose("Fall motion");
                Choose("Back");
                foreach (string ending in EndingNames)
                {
                    Choose("Development");
                    Choose("Next ending");
                    yield return VerifyEndingExit(ending, reduced, false);
                    Choose("Main menu");
                    Choose("Development");
                    yield return VerifyEndingExit(ending, reduced, true);
                    Choose("Main menu");
                }
                Choose("Development");
                Choose("Seed 379 · level 1");
                yield return Stage(GameStage.Ready);
            }
            Record("All six endings reached a black frame and Results in Full and Reduced. An early Skip also worked in both modes. Every Restart restored the board, score, room floor and audio.");
        }

        private static IEnumerator VerifyEndingExit(string ending, bool reduced, bool skip)
        {
            MeshRenderer previewSurface = null;
            Color32[][] previewWalls = null;
            if (!skip)
            {
                StandOn(CornerPane);
                game.BoardView.PracticeBreak(AdjacentPane(CornerPane));
                yield return Delay(1.80);
                LookAtTile(AdjacentPane(CornerPane));
                var previewChamber = GameObject.Find("Fall chamber");
                Require(previewChamber && !game.Ending.IsPlaying, "A safe pane did not reveal the shared chamber before loss.");
                previewSurface = previewChamber.transform.Find("Hazard surface").GetComponent<MeshRenderer>();
                previewWalls = VerifyOpenLanding(ending, reduced, previewSurface);
                if (!reduced) Capture("open-shaft-"+ending.ToLowerInvariant(), SmokeView.CenterEye);
            }
            if (!skip && (ending == "Tar Pit" || (reduced && (ending == "Sand" || ending == "Water" || ending == "Lava"))))
            {
                SetStandingHead(game.State.Center(CornerPane),head.rotation,1.12f);
                yield return Frames(2);
            }
            Choose("Preview loss");
            yield return Stage(GameStage.Fall);
            string trial = ending + " / " + (reduced ? "Reduced" : "Full") + (skip ? " / Skip" : " / complete");
            Require(game.Ending.IsPlaying && game.Ending.LastEndingName == ending, trial + " selected a different ending.");
            var chamber = GameObject.Find("Fall chamber");
            if (previewSurface)
            {
                var properties = new MaterialPropertyBlock();
                previewSurface.GetPropertyBlock(properties);
                Require(previewSurface.transform == chamber.transform.Find("Hazard surface") &&
                    (int)properties.GetFloat("_Theme") == Array.IndexOf(EndingNames,ending) &&
                    Mathf.Abs(game.BoardView.transform.position.y - previewSurface.transform.position.y - 80f) < .015f,
                    ending + " switched the landing between the broken pane and the start of the fall.");
                VerifyOpenLanding(ending, reduced, previewSurface, previewWalls);
            }
            var veil = game.Rig.centerEyeAnchor.Find("View fade").GetComponent<MeshRenderer>();
            var material = new MaterialPropertyBlock();
            double started = Now;
            double expectedSeconds = reduced ? 3.93 : 4.44061 + (ending == "Space" ? 8 : ending == "Snow" ? 5.2 : 5);
            if (skip)
            {
                // The preview button bypasses the gameplay shatter. Queue the ordinary glass
                // layers so this skip also has a delayed echo to cancel.
                game.GetComponent<GameSound>().Break(game.Rig.centerEyeAnchor.position + Vector3.down, 1);
                yield return Delay(.18);
                game.Ending.Skip();
            }
            bool sawBlack = false;
            float[] viewTimes = skip ? Array.Empty<float>() : LandingViewTimes(ending,reduced);
            int nextView = 0;
            bool checkedPause = false, queuedGlassAtExit = false;
            double pauseAge = ending == "Space" ? (reduced ? .44 : 2.35) : (reduced ? 2.84 : 7.68);
            var motion = new EndingMotionCheck(ending,reduced,chamber.transform,veil);
            while (game.Stage == GameStage.Fall)
            {
                double age = Now - started;
                if (!skip)
                {
                    motion.Sample();
                    if (ending == "Space" && !queuedGlassAtExit && age >= (reduced ? .62 : 4.24))
                    {
                        game.GetComponent<GameSound>().Break(game.Rig.centerEyeAnchor.position + Vector3.down, 1);
                        queuedGlassAtExit = true;
                    }
                    if (!checkedPause && age >= pauseAge)
                    {
                        checkedPause = true;
                        double paused = Now;
                        var shaft = chamber.transform.Find("Shaft walls");
                        Vector3 atPause = shaft.position;
                        game.gameObject.SendMessage("OnApplicationPause",true);
                        yield return Stage(GameStage.Paused);
                        yield return Delay(.20);
                        Require(!chamber.activeSelf && !veil.gameObject.activeInHierarchy && shaft.position == atPause &&
                            motion.PausedAudioIsSilent(), trial + " advanced or played fall audio during tracking loss.");
                        game.gameObject.SendMessage("OnApplicationPause",false);
                        Choose("Show board"); yield return Stage(GameStage.Fall);
                        started += Now - paused;
                    }
                }
                Require(age < (skip ? 1.5 : expectedSeconds + 1.0), trial + " timed out.");
                if (nextView < viewTimes.Length && age >= viewTimes[nextView])
                {
                    VerifyLandingViews(chamber.transform,ending,reduced,(float)age,nextView);
                    nextView++;
                }
                if (skip || age >= expectedSeconds - .25)
                {
                    veil.GetPropertyBlock(material);
                    Color tint = material.GetColor("_Tint");
                    sawBlack |= veil.enabled && tint.a >= .999f && tint.maxColorComponent < .002f;
                }
                yield return null;
            }
            if (!skip) motion.Finish();
            Require(motion.GlassIsClear(), trial + " left scheduled glass after the ending.");
            int expectedCues = skip || ending == "Space" ? 0 : ending == "Tar Pit" ? 3 : 1;
            Require(game.GetComponent<GameSound>().EditorLandingCueCount == expectedCues,
                trial + " played the wrong number of contact and suction cues.");
            double duration = Now - started;
            Require(nextView == viewTimes.Length, trial + " ended before its landing views were checked.");
            Require((skip || duration >= expectedSeconds - .12) && sawBlack && game.Stage == GameStage.Results && game.Panel.Visible,
                trial + " did not present its final black frame and Results at the expected time.");
            Require(chamber && !chamber.activeSelf && !veil.enabled && !game.Ending.IsPlaying, trial + " left the ending visible after Results.");
            foreach (string name in new[] { "Shaft air", "Shaft roar", "Ending ambience", "Ending impact", "Tar bubbling" })
            {
                var source = game.transform.Find(name).GetComponent<AudioSource>();
                Require(!source.isPlaying && !source.clip, trial + " left audio on " + name);
            }
            VerifyFloorVisibility(false);
            Choose("Retry Level");
            yield return Stage(GameStage.Ready);
            Require(game.Level == 1 && game.WaveNumber == 1 && game.State.WavesSurvived == 0 &&
                game.State.ActiveWave == null && game.BoardView.gameObject.activeSelf, trial + " left an incomplete board reset.");
            for (int cell = 0; cell < game.State.Count; cell++)
                Require(game.State.IsIntact(cell) == game.State.Layout.Contains(cell), trial + " left a broken pane at " + cell);
            VerifyFloorVisibility(false);
            report.endingExits.Add(new EndingExitEvidence { ending = ending, reduced = reduced, skipped = skip, seconds = duration,
                eyeHeight = motion.EyeHeight, eyeCrossing = motion.EyeCrossing, firstPull = motion.FirstPull, secondPull = motion.SecondPull });
            Save();
        }

        // Sample the visible renderer transforms and the live audio sources from the production scene.
        private sealed class EndingMotionCheck
        {
            readonly string ending;
            readonly bool reduced;
            readonly Transform shaft, surface;
            readonly MeshRenderer sky, veil, interior;
            readonly AudioSource impact, bed, hiss, roar, ambience;
            readonly AudioSource[] glass;
            bool heardApproach, heardLanding;
            readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
            readonly float originY, eyeHeight;
            float lastTime, lastHeight, crossingsAt = -1;
            internal float EyeHeight => eyeHeight;
            internal float EyeCrossing => crossingsAt;
            internal float FirstPull { get; private set; } = -1;
            internal float SecondPull { get; private set; } = -1;
            int tarCues;
            bool white, black, muffledTail, checkedLine, checkedInterior;
            float previousSandLight = float.PositiveInfinity;

            internal EndingMotionCheck(string ending, bool reduced, Transform room, MeshRenderer veil)
            {
                this.ending = ending; this.reduced = reduced; this.veil = veil;
                shaft = room.Find("Shaft walls"); surface = room.Find("Hazard surface");
                interior = veil.transform.Find("Submerged view").GetComponent<MeshRenderer>();
                sky = room.Find("Chamber darkness").GetComponent<MeshRenderer>();
                impact = game.transform.Find("Ending impact").GetComponent<AudioSource>();
                bed = game.transform.Find("Tar bubbling").GetComponent<AudioSource>();
                hiss = game.transform.Find("Shaft air").GetComponent<AudioSource>();
                roar = game.transform.Find("Shaft roar").GetComponent<AudioSource>();
                ambience = game.transform.Find("Ending ambience").GetComponent<AudioSource>();
                glass = game.GetComponent<GameSound>().GetComponentsInChildren<AudioSource>()
                    .Where(v => v.name.StartsWith("Pane impact ") || v.name.StartsWith("Glass fragments ") || v.name.StartsWith("Shaft echo ")).ToArray();
                originY = room.position.y;
                eyeHeight = game.Rig.centerEyeAnchor.position.y-originY;
            }

            internal bool GlassIsClear() => glass.All(voice => !voice.clip && !voice.isPlaying);
            internal bool PausedAudioIsSilent() =>
                !hiss.isPlaying && !roar.isPlaying && !impact.isPlaying && !ambience.isPlaying && !bed.isPlaying && GlassIsClear();

            void SampleSound(float secondsSinceContact)
            {
                if (secondsSinceContact < -.025f)
                {
                    Require(!impact.clip && !ambience.clip && ambience.volume == 0 && bed.volume == 0, ending + " announced the landing early.");
                    if (secondsSinceContact > (reduced ? -.25f : -.8f))
                    {
                        float master = PlayerPrefs.GetInt("ff.volume", 3) / 4f;
                        Require(hiss.clip && roar.clip && hiss.volume > 0 && roar.volume > 0 &&
                            hiss.spatialBlend == 0 && roar.spatialBlend == 0 &&
                            (reduced ? hiss.volume < .31f*master && roar.volume < .11f*master : roar.volume > .40f*master),
                            ending + " lost the motion-specific shaft swell before the far end.");
                        heardApproach = true;
                    }
                    return;
                }
                if (secondsSinceContact < 0) return;
                if (ending == "Space")
                {
                    Require(hiss.volume == 0 && roar.volume == 0 && !hiss.isPlaying && !roar.isPlaying &&
                        !impact.clip && !ambience.clip && !bed.clip && glass.All(voice => !voice.isPlaying && !voice.clip),
                        "A wind, impact, ambience or glass tail crossed the Space exit.");
                    heardLanding = true;
                    return;
                }
                if (secondsSinceContact > .12f)
                    Require(hiss.volume == 0 && roar.volume == 0, "Wind masked the landing after contact.");
                if (heardLanding) return;
                if (ending == "Sand")
                    Require(ambience.clip && !ambience.loop && ambience.clip.length >= 5f,
                        "Burial must play one continuous sand recording from contact.");
                string expectedClip = ending switch { "Water" => "WaterImpact", "Lava" => "LavaImpact",
                    "Tar Pit" => "TarImpact", "Snow" => "SnowImpact", _ => "SandImpact" };
                Require(impact.clip && impact.clip.name == expectedClip && impact.spatialBlend == 1f && impact.minDistance >= Vector3.Distance(impact.transform.position, game.Rig.centerEyeAnchor.position) &&
                    Mathf.Abs(impact.transform.position.y - originY) < .3f,
                    ending + " did not play its positional contact cue at the landing.");
                heardLanding = true;
            }

            internal void Sample()
            {
                var clock = sky.enabled ? sky : interior;
                if (!clock.enabled) return;
                clock.GetPropertyBlock(block);
                float t = block.GetFloat("_Age") - (reduced ? .82f : 4.44061f);
                SampleSound(t);
                if (t <= lastTime) return;
                float height = surface.position.y-originY;
                if (reduced)
                    Require(Mathf.Abs(height)<.005f, ending+" moved the reduced-motion landing.");
                else if (ending == "Space")
                    Require(Mathf.Abs(shaft.position.y-originY-80f-39.598f*Mathf.Min(t,8f))<.035f, "Space lost its exit speed.");
                else if (ending == "Snow")
                {
                    if (t>=.7f) Require(Mathf.Abs(height-eyeHeight*.5f)<.015f,"Snow did not settle at waist height.");
                }
                else
                {
                    // Travel includes an 80 m shaft before the world-space surface is sampled.
                    // Compare the minimum physical rise over the clamped motion interval at that precision.
                    float interval=Mathf.Clamp(t,0f,5f)-Mathf.Clamp(lastTime,0f,5f);
                    float minimumRise=.20f*eyeHeight*interval;
                    Require(height-lastHeight >= minimumRise-.00003f,
                        $"{ending} rose {height-lastHeight:F6} m over {interval:F6} s after contact (age {t:F6} s).");
                    if (height>=eyeHeight && crossingsAt<0)
                        crossingsAt = Mathf.Lerp(lastTime,t,Mathf.InverseLerp(lastHeight,height,eyeHeight));
                }
                float duration = ending=="Space" ? 8 : ending=="Snow" ? 5.2f : 5;
                float seconds = reduced ? t*duration/3.11f : t;
                if (interior.enabled)
                {
                    interior.GetPropertyBlock(block);
                    bool under = block.GetVector("_InteriorPlane").z != 0;
                    if (!checkedLine && !under)
                    {
                        VerifyImmersionPixels(interior,shaft.parent,ending,reduced,true);
                        checkedLine = true;
                    }
                    if (under && ending == "Sand")
                    {
                        float light = block.GetFloat("_Brightness");
                        Require(light<=previousSandLight+.0001f,"Buried sand grew brighter with depth.");
                        previousSandLight=light;
                    }
                    // Give nearby bubbles time to enter the water after the line closes.
                    if (!checkedInterior && under && seconds>=(ending=="Water" ? 1.85f : ending=="Sand" ? 2.68f : ending=="Tar Pit" ? 2.93f : 2.17f))
                    {
                        VerifyImmersionPixels(interior,shaft.parent,ending,reduced,false);
                        checkedInterior=true;
                    }
                }
                veil.GetPropertyBlock(block);
                var tint = block.GetColor("_Tint");
                bool opaque = veil.enabled && tint.a>=.999f;
                if (ending=="Snow" && seconds>=2.81f && seconds<=3.19f)
                {
                    Require(opaque && Mathf.Min(tint.r,Mathf.Min(tint.g,tint.b))>.99f,"Snow skipped its full whiteout.");
                    white=true;
                }
                if (ending=="Space" && seconds>=7.02f)
                {
                    Require(opaque && tint.maxColorComponent<.002f,"Space exposed scenery during its black hold.");
                    black=true;
                }
                if (ending=="Tar Pit")
                {
                    if (impact.clip && impact.clip.name=="TarImpact") tarCues |= 1;
                    if (impact.clip && impact.clip.name=="TarPull")
                    {
                        if (impact.pitch>.85f)
                        {
                            if (FirstPull<0) FirstPull=seconds;
                            tarCues |= 2;
                        }
                        else
                        {
                            if (SecondPull<0) SecondPull=seconds;
                            tarCues |= 4;
                        }
                    }
                    // A GPU readback can span the cue time. Check the live source against
                    // the ending clock on each observed frame.
                    int expectedCues = seconds >= 1.57f ? 7 : seconds >= .47f ? 3 : 1;
                    Require(tarCues == expectedCues,
                        $"Tar cue mask at {seconds:F3}s was {tarCues}, expected {expectedCues}.");
                    if (seconds>4.25f && seconds<4.7f)
                    {
                        Require(opaque && tint.maxColorComponent<.002f && bed.clip && bed.clip.name=="TarBubbles" &&
                            bed.GetComponent<AudioLowPassFilter>().cutoffFrequency<550 && bed.volume>0,
                            "The tar gurgle did not continue under cover after submersion.");
                        muffledTail=true;
                    }
                }
                lastTime=t; lastHeight=height;
            }

            internal void Finish()
            {
                Require(heardApproach && heardLanding, ending + " missed the shaft approach or contact audio check.");
                if (ending!="Space" && ending!="Snow")
                    Require(checkedLine && checkedInterior,ending+" missed its stereo crossing or buried-view check.");
                if (ending=="Tar Pit") Require(tarCues==7 && muffledTail,
                    $"Tar cues at {FirstPull:F3} and {SecondPull:F3}s or muffled tail missed their timing.");
                if (ending=="Snow") Require(white,"No white frame before the snow fade.");
                if (ending=="Space") Require(black,"No final black hold in Space.");
                if (reduced || ending=="Snow" || ending=="Space") return;
                float expected = ending=="Water" ? 1.2f : ending=="Lava" ? 1.8f : ending=="Sand" ? 2.3f : 2.5f;
                Require(Mathf.Abs(crossingsAt-expected)<.06f,$"{ending} crossed the eye at {crossingsAt:F3}s, expected {expected:F2}s.");
            }
        }

        private static Color32[][] VerifyOpenLanding(string ending, bool reduced, MeshRenderer surface, Color32[][] preview = null)
        {
            var block = new MaterialPropertyBlock();
            surface.GetPropertyBlock(block);
            Require(surface.enabled && (int)block.GetFloat("_Theme") == Array.IndexOf(EndingNames,ending) &&
                surface.sharedMaterial.GetInt("_PortalComparison") == (int)CompareFunction.Equal &&
                Mathf.Abs(game.BoardView.transform.position.y-surface.transform.position.y-80f) < .015f,
                ending + " was not already present 80 metres below the open pane.");
            var board = game.BoardView.transform;
            if (ending == "Water")
            {
                var native = Resources.Load<Material>("Endings/MetaFloorZone/materials/Water");
                Require(native && native.GetTexture("_WaterNormal") &&
                    surface.sharedMaterial.GetTexture("_WaterNormal") == native.GetTexture("_WaterNormal"),
                    "The pre-loss water did not use the approved Meta Floor Zone ripple map.");
            }
            Transform room = surface.transform.parent;
            var walls = room.Find("Shaft walls").GetComponent<Renderer>();
            var ledges = room.Find("Shaft landmarks").GetComponent<Renderer>();
            Require(walls.enabled && ledges.enabled, "The opening lost its view of the shared shaft.");
            var wallViews = new Color32[2][];
            var objects = new[] { board.Find("Opening stencil"),
                room.Find("Shaft walls"), room.Find("Shaft landmarks"), surface.transform };
            var oldLayers = Array.ConvertAll(objects, t => t.gameObject.layer);
            const int layer = 30, size = 96;
            var probe = new GameObject("Open shaft stereo probe");
            var camera = probe.AddComponent<Camera>();
            var target = RenderTexture.GetTemporary(size,size,24,RenderTextureFormat.ARGB32);
            var oldTarget = RenderTexture.active;
            var image = new Texture2D(size,size,TextureFormat.RGB24,false);
            try
            {
                foreach (var t in objects) t.gameObject.layer=layer;
                camera.enabled=false;
                camera.GetUniversalAdditionalCameraData().allowXRRendering=false;
                camera.cullingMask=1<<layer;
                camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=Color.magenta;
                camera.nearClipPlane=.05f; camera.farClipPlane=180; camera.fieldOfView=50; camera.aspect=1;
                camera.targetTexture=target;
                var center = board.TransformPoint(GlassGeometry.Ground(game.State.Center(AdjacentPane(CornerPane))));
                var standing = board.TransformPoint(GlassGeometry.Ground(game.State.Center(CornerPane)))+Vector3.up*1.55f;
                Quaternion rotation = Quaternion.LookRotation(center-standing, Vector3.up);
                foreach (float offset in new[] { -.032f, .032f })
                {
                    probe.transform.SetPositionAndRotation(standing+rotation*Vector3.right*offset,rotation);
                    surface.enabled=false; var without=Read();
                    surface.enabled=true; var with=Read();
                    if (ending == "Tar Pit" && preview == null)
                    {
                        float originalClock = block.GetFloat("_SurfaceClock");
                        try
                        {
                            block.SetFloat("_SurfaceClock",13); surface.SetPropertyBlock(block);
                            var first = Read();
                            block.SetFloat("_SurfaceClock",13.8f); surface.SetPropertyBlock(block);
                            var second = Read();
                            int moving = 0;
                            for (int i=0;i<first.Length;i++)
                                if (Difference(with[i],without[i])>=12 && Difference(first[i],second[i])>=6) moving++;
                            Require(moving>=6,$"Only {moving} tar pixels changed inside the visible pool.");
                        }
                        finally
                        {
                            block.SetFloat("_SurfaceClock",originalClock); surface.SetPropertyBlock(block);
                        }
                    }
                    walls.enabled = ledges.enabled = false;
                    var landingAlone = Read();
                    walls.enabled = ledges.enabled = true;
                    int eye = offset < 0 ? 0 : 1;
                    wallViews[eye] = without;
                    int destinationPixels=0, wallPixels=0, changedWallPixels=0, escaped=0;
                    for (int y=0;y<size;y++)
                    for (int x=0;x<size;x++)
                    {
                        int i=y*size+x;
                        bool landing = Difference(with[i], without[i]) >= 12;
                        bool wall = Difference(with[i], landingAlone[i]) >= 12;
                        if (!landing && !wall && preview == null) continue;
                        Ray ray=camera.ViewportPointToRay(new Vector3((x+.5f)/size,(y+.5f)/size));
                        Vector3 hit=board.InverseTransformVector(ray.GetPoint((center.y-ray.origin.y)/ray.direction.y)-center);
                        float edge=Mathf.Max(Mathf.Abs(hit.x),Mathf.Abs(hit.z))-game.State.Pitch*.5f;
                        if (edge<-.015f)
                        {
                            if (landing) destinationPixels++;
                            if (wall) wallPixels++;
                            if (preview != null && Difference(without[i],preview[eye][i]) >= 12) changedWallPixels++;
                        }
                        if (edge>.025f && (landing || wall)) escaped++;
                    }
                    Require(destinationPixels>35 && wallPixels>35 && (preview != null || escaped==0),
                        $"{ending} eye {eye} saw {wallPixels} wall pixels and {destinationPixels} landing pixels, {escaped} outside the first hole.");
                    if (preview != null)
                        Require(changedWallPixels <= 3, $"{ending} eye {eye} changed {changedWallPixels} wall pixels at fall entry.");
                    else
                        report.openLandings.Add(new LandingReadback { ending=ending, reduced=reduced, eye=eye==0 ? "left" : "right",
                            wallPixels=wallPixels, destinationPixels=destinationPixels, outsideAperturePixels=escaped,
                            depthMetres=board.position.y-surface.transform.position.y });
                }
                return wallViews;
                int Difference(Color32 a, Color32 b) => Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b);
                Color32[] Read()
                {
                    camera.Render(); RenderTexture.active=target;
                    image.ReadPixels(new Rect(0,0,size,size),0,0); image.Apply();
                    return image.GetPixels32();
                }
            }
            finally
            {
                walls.enabled=ledges.enabled=surface.enabled=true;
                for (int i=0;i<objects.Length;i++) objects[i].gameObject.layer=oldLayers[i];
                camera.targetTexture=null; RenderTexture.active=oldTarget;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        [Serializable]
        private sealed class LandingReadback
        {
            public string ending, eye;
            public int wallPixels, destinationPixels, outsideAperturePixels;
            public bool reduced;
            public float depthMetres;
        }

        [Serializable]
        private sealed class EndingExitEvidence
        {
            public string ending;
            public bool reduced, skipped;
            public double seconds;
            public float eyeHeight, eyeCrossing, firstPull, secondPull;
        }

        private static IEnumerator ObserveSelectedFall(string ending, bool reduced, int nextEndingPresses)
        {
            Choose("Main menu"); Choose("Settings");
            if ((PlayerPrefs.GetInt("ff.reduced", 0) != 0) != reduced) Choose("Fall motion");
            if (PlayerPrefs.GetInt("ff.volume", 3) == 0) Choose("Volume");
            Choose("Back"); Choose("Development");
            for (int i = 0; i < nextEndingPresses; i++) Choose("Next ending");
            Choose("Seed 379 · level 1"); yield return Stage(GameStage.Ready);

            // Development previews use a separate bag. Seed the real one before starting this hazard check.
            int desiredEnding = Array.IndexOf(EndingNames, ending);
            for (int seed = 0; seed < 256 && game.Ending.EditorPeekNextLevelEnding != desiredEnding; seed++)
                game.Ending.SetShuffleSeed(seed);
            Require(game.Ending.EditorPeekNextLevelEnding == desiredEnding, "No fixture seed selected " + ending);
            // A real deadline loss shatters the occupied tile before the rim shot.
            var stance = game.State.Center(CornerPane) + new Vector2(-.34f,.26f) * game.State.Pitch;
            var board = game.BoardView.Board;
            Vector3 bearing = board.TransformDirection(-GlassGeometry.Ground(stance));
            SetStandingHead(stance, Quaternion.LookRotation(bearing, Vector3.up) * Quaternion.Euler(32f, 0f, 0f),
                reduced ? 1.6f : ending == "Lava" ? 2.2f : 1.5f);
            yield return Frames(2);
            Pose trackedAtStart = head;
            Pose rigAtStart = new Pose(game.Rig.transform.position, game.Rig.transform.rotation);
            var arena = new FallArenaWitness(trackedAtStart, reduced);
            // Exercise a connected opening at a board corner on the actual deadline-loss path.
            if (!reduced) game.BoardView.PracticeBreak(AdjacentPane(CornerPane));
            Choose("Start wave"); yield return Stage(GameStage.Countdown);
            yield return Stage(GameStage.Fall, 8);
            Require(!game.State.IsIntact(CornerPane) && game.Ending.IsPlaying && game.Ending.LastEndingName == ending,
                "The chosen ending did not follow the real starting tile's deadline loss: " + ending);

            var witness = new FallWitness(ending, reduced, trackedAtStart, rigAtStart, arena);
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
            Choose("Retry Level"); yield return Stage(GameStage.Ready);
            arena.VerifyReset();
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
            private readonly FallArenaWitness arena;
            private readonly Pose eyePose, rigPose;
            private readonly Transform chamber, eye;
            private readonly MeshRenderer marks, walls, floor, flames, sky, veil;
            private readonly MaterialPropertyBlock material = new MaterialPropertyBlock();
            private readonly AudioSource wind, impact;
            private readonly float firstMarks, firstWalls, firstFloor, firstSpan;
            private int lastFrame = -1;
            private bool reducedEntryCovered;
            private float lastTravel, speedSampleTravel;
            private double lastAge, speedSampleAge;

            internal FallWitness(string ending, bool reduced, Pose initialEye, Pose initialRig, FallArenaWitness arena)
            {
                this.arena = arena;
                this.reduced = reduced; water = ending == "Water";
                start = Now; contact = reduced ? 0.82 : 4.44061; expectedFinish = reduced ? 3.93 : 9.44061;
                eyePose = initialEye; rigPose = initialRig; eye = game.Rig.centerEyeAnchor;
                var chamberObject = GameObject.Find("Fall chamber");
                Require(chamberObject, "The active fall chamber is missing.");
                chamber = chamberObject.transform;
                marks = Renderer("Shaft landmarks"); walls = Renderer("Shaft walls"); floor = Renderer("Hazard surface"); flames = Renderer("Lava flames");
                sky = Renderer("Chamber darkness"); veil = eye.Find("View fade").GetComponent<MeshRenderer>();
                firstMarks = chamber.position.y;
                firstWalls = walls.transform.position.y; firstFloor = floor.transform.position.y;
                wind = game.transform.Find("Shaft air").GetComponent<AudioSource>();
                impact = game.transform.Find("Ending impact").GetComponent<AudioSource>();
                Require(wind.clip && !impact.clip, "The fall did not start with shaft air and a silent impact source.");
                firstSpan = marks.bounds.size.y;
                float openingY = game.BoardView.Board.position.y;
                Require(Mathf.Abs(firstFloor - (openingY - 80f)) < 0.10f,
                    $"The hazard began at {firstFloor:F2} m for an opening at {openingY:F2} m.");
                evidence = new FallEvidence { ending = ending, motion = reduced ? "reduced" : "full", plannedContact = contact,
                    eyeHeight = eye.position.y - openingY, landmarkSpan = firstSpan, floorOpeningY = openingY, startingHazardY = firstFloor };
                arena.VerifyEntry(walls);
                if (reduced) game.Ending.ChamberEntered += CheckReducedCut;
                if (!reduced)
                {
                    Require(walls.enabled && marks.enabled && !veil.enabled, "The recognition beat did not expose the shaft lip and its walls.");
                    Require(sky.bounds.min.y < firstFloor && eye.GetComponent<Camera>().farClipPlane > eye.position.y - firstFloor,
                        "The camera or sky enclosure cuts off the deep destination.");
                    CheckShaftMeshes();
                }
                Observe();
            }

            private void CheckReducedCut()
            {
                game.Ending.ChamberEntered -= CheckReducedCut;
                veil.GetPropertyBlock(material);
                reducedEntryCovered = veil.enabled && material.GetColor("_Tint").a >= .999f;
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
                if (age < contact) evidence.windAtImpact = Mathf.Max(evidence.windAtImpact,wind.volume);
                bool effect = SecondaryEffect();
                bool entered = reduced ? !game.BoardView.gameObject.activeSelf : walls.transform.position.y >= eye.position.y;
                if (entered && evidence.chamberEnteredAt < 0) evidence.chamberEnteredAt = age;
                arena.Observe(evidence, travel, walls);
                if (!reduced)
                {
                    CheckGravity(age, travel);
                    if (floor.transform.position.y >= evidence.floorOpeningY - .0001f && evidence.surfaceReachedAt < 0)
                        evidence.surfaceReachedAt = age;
                    ObserveLighting(age, entered);
                }
                if (impact.clip && evidence.impactSoundAt < 0)
                {
                    evidence.impactSoundAt = age;
                    Require(entered && (reduced || evidence.surfaceReachedAt >= 0),
                        $"The impact sound started before the chamber entry or surface arrival at {age:F3} s.");
                }
                if (effect && evidence.firstSecondaryEffectAt < 0)
                {
                    evidence.firstSecondaryEffectAt = age;
                    evidence.contactHazardY = floor.transform.position.y;
                    Require(evidence.impactSoundAt >= 0 && Mathf.Abs(evidence.contactHazardY - evidence.floorOpeningY) < evidence.eyeHeight*.30f,
                        $"The hazard appeared at {evidence.contactHazardY:F3} m before the landing cue at {evidence.floorOpeningY:F3} m.");
                }
                if (reduced ? age < contact - 0.025 : evidence.surfaceReachedAt < 0)
                {
                    // Reduced motion holds each tableau stationary and swaps under the black veil.
                    float expectedFloor = reduced ? (entered ? evidence.floorOpeningY : firstFloor) : firstFloor + travel;
                    float expectedWalls = firstWalls + (reduced ? (entered ? 5f : 0f) : travel);
                    float floorError = Mathf.Abs(floor.transform.position.y - expectedFloor);
                    float wallError = Mathf.Abs(walls.transform.position.y - expectedWalls);
                    float spacingError = Mathf.Abs(marks.bounds.size.y - firstSpan);
                    evidence.maxPreContactOffsetError = Mathf.Max(evidence.maxPreContactOffsetError, Mathf.Max(floorError, Mathf.Max(wallError, spacingError)));
                    Require(evidence.maxPreContactOffsetError < 0.035f, $"Shaft, bands, or floor diverged by {evidence.maxPreContactOffsetError:F3} m before contact at {age:F3} s.");
                    Require(!effect && !impact.clip, $"A secondary hazard or impact sound started early at {age:F3} s.");
                    if (reduced && entered) Require(reducedEntryCovered, "The reduced chamber moved in view.");
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
                Require(Math.Abs(evidence.surfaceReachedAt - evidence.impactSoundAt) < .015 && marks.transform.position.y - firstMarks > 80f,
                    $"The shaft landed at {evidence.surfaceReachedAt:F3} s after travelling {marks.transform.position.y - firstMarks:F3} m.");
                Require(evidence.visibleWallSeconds >= 3.0, $"Only {evidence.visibleWallSeconds:F2} s of clearly visible walls passed the {evidence.eyeHeight:F2} m eye before impact.");
                Require(evidence.skyMostlyDarkAt - evidence.skyFirstDimAt > 1.0, "The room light above the shaft disappeared too quickly.");
            }

            internal void CheckImmersion()
            {
                if (!water)
                {
                    Require(!SecondaryEffect() && !floor.enabled && floor.transform.position.y > evidence.floorOpeningY + 0.5f,
                        "Submerged lava still exposed its surface or flames.");
                    return;
                }
                var underwater = veil.transform.Find("Submerged view").GetComponent<MeshRenderer>();
                underwater.GetPropertyBlock(material);
                evidence.immersionTintAlpha = material.GetVector("_InteriorPlane").z;
                Require(underwater.enabled && evidence.immersionTintAlpha == 1 && !floor.enabled,
                    "Water failed to cover the shaft at the immersion frame.");
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
                        Require(evidence.fadeDarkAt - evidence.fadeStartedAt > 1.15 && evidence.fadeStartedAt - contact > 1.0,
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
            public int overheadGlassPixels, overheadBrickPixels;
            public float offCenterMetres, maxArenaRegistrationError, maxRoomAnchorDisplacement;
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
