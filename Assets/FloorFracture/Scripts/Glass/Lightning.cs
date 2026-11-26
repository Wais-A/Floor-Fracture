using System;
using UnityEngine;

namespace FloorFracture
{
    // Lightning overhead: each strike flashes the sky disc, glass, fog and beams, then thunder rolls in from its side.
    internal sealed class Lightning : MonoBehaviour
    {
        const float SkyRadius = 4f, SkyAbove = .5f, StrikeDistance = 9f, Elevation = 55f*Mathf.Deg2Rad;
        // The longest flash curve (Reduced motion) ends 0.6 s after its strike.
        const double FlashSeconds = .6;
        static readonly int FlashId = Shader.PropertyToID("_FloorFractureFlash");
        static readonly int TintId = Shader.PropertyToID("_Tint");
        MeshRenderer sky, veil;
        Material veilMaterial;
        Mesh veilMesh;
        MaterialPropertyBlock veilBlock;
        Material skyMaterial;
        Mesh skyMesh;
        GameSound sound;
        WaveHazards hazards = WaveHazards.None;
        Vector3 strikeLocal;
        float pitch, shownFlash = -1;
        int next;

        public void Build(Transform board, GameSound gameSound, float lampHeightLocal)
        {
            transform.SetParent(board, false);
            sound = gameSound;
            skyMaterial = GlassTile.MakeMaterial("FloorFractureSky");
            var b = new GlassGeometry.Builder();
            b.Quad(new(-SkyRadius,0,-SkyRadius),new(SkyRadius,0,-SkyRadius),new(SkyRadius,0,SkyRadius),new(-SkyRadius,0,SkyRadius),Color.white,Color.white);
            skyMesh = b.Create("Sky flash");
            sky = GlassTile.Part(transform, "Sky flash", skyMesh, skyMaterial);
            sky.transform.localPosition = new Vector3(0, lampHeightLocal+SkyAbove, 0);
            sky.enabled = false;
            // A full-view white wash, so a strike lights the view even when the player looks at the floor.
            veilMaterial = new Material(Resources.Load<Shader>("Endings/ViewFade")) { renderQueue = 4190 };
            veilMesh = FallMeshes.Box();
            veilBlock = new MaterialPropertyBlock();
            veil = GlassTile.Part(transform, "Flash veil", veilMesh, veilMaterial);
            veil.transform.localScale = Vector3.one * 1.5f;
            veil.enabled = false;
        }

        public void Show(WaveHazards shown)
        {
            hazards = shown;
            next = 0;
            var random = new System.Random(unchecked(shown.VisualSeed*53+11)); // apart from the fog's and lamps' streams
            float azimuth = 2*Mathf.PI*(float)random.NextDouble();
            strikeLocal = StrikeDistance*new Vector3(Mathf.Cos(Elevation)*Mathf.Cos(azimuth), Mathf.Sin(Elevation), Mathf.Cos(Elevation)*Mathf.Sin(azimuth));
            pitch = .92f + .14f*(float)random.NextDouble();
        }

        // `now` is the pause-aware gameplay clock, so a menu pause holds a pending strike until resume.
        public void Tick(double now, float fade, bool reduced)
        {
            var strikes = hazards.Strikes;
            bool visible = isActiveAndEnabled && fade > 0;
            for (; next < strikes.Count && strikes[next] <= now; ++next)
            {
                double late = now - strikes[next];
                // A strike that passed while the board was hidden or before Show is dropped, not replayed late.
                if (visible && late < FlashSeconds && sound)
                    sound.PlayThunder(transform.TransformPoint(strikeLocal),
                        Math.Max(0, WaveHazards.ThunderDelay(hazards.VisualSeed, next) - late), pitch);
            }
            float flash = visible && next > 0 ? WaveHazards.FlashIntensity(now - strikes[next-1], reduced)*fade : 0;
            bool wash = flash > 0 && !reduced;
            var head = wash ? Camera.main : null;
            bool veiled = head != null;
            if (head) veil.transform.SetPositionAndRotation(head.transform.position, head.transform.rotation);
            if (flash == shownFlash && veil.enabled == veiled) return;
            shownFlash = flash;
            Shader.SetGlobalFloat(FlashId, flash);
            sky.enabled = flash > 0;
            veil.enabled = veiled;
            if (veil.enabled)
            {
                veilBlock.SetColor(TintId, new Color(.85f, .9f, 1, .5f*flash));
                veil.SetPropertyBlock(veilBlock);
            }
        }

        // A pause or teardown hides the board, so nothing may stay lit.
        void OnDisable()
        {
            Shader.SetGlobalFloat(FlashId, 0);
            shownFlash = 0;
            if (sky) sky.enabled = false;
            if (veil) veil.enabled = false;
        }

        void OnDestroy()
        {
            if (skyMesh) Destroy(skyMesh);
            if (skyMaterial) Destroy(skyMaterial);
            if (veilMesh) Destroy(veilMesh);
            if (veilMaterial) Destroy(veilMaterial);
        }
    }
}
