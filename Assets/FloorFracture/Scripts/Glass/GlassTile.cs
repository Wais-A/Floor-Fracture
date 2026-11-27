using UnityEngine;
using UnityEngine.Rendering;

namespace FloorFracture
{
    public class GlassTile : MonoBehaviour
    {
        public static readonly Color Safe = new(.19f,.36f,.40f,.70f);
        static readonly Color Edge = new(.58f,.88f,.88f,.91f);
        static readonly Color Danger = new(1f,.41f,.15f,1f);
        static readonly Color Fire = new(1f,.78f,.36f,1f);
        // Set these on the GlassTile prefab: every pane on the board is an instance of it, and the Settings sample copies it.
        [SerializeField] Color resting = Safe;
        [SerializeField] Color rim = Edge;
        [SerializeField] Color warning = Danger;
        [SerializeField] Color hot = Fire;
        [Tooltip("A warned pane's face pulses from the first of these to the second.")]
        [SerializeField] Color warnedFaceDim = new(.36f,.24f,.23f,.79f);
        [SerializeField] Color warnedFaceBright = new(1f,.56f,.13f,.90f);
        public Color Resting => resting;
        public void CopyColours(GlassTile from)
        {
            resting=from.resting; rim=from.rim; warning=from.warning; hot=from.hot;
            warnedFaceDim=from.warnedFaceDim; warnedFaceBright=from.warnedFaceBright;
        }
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int SheenId = Shader.PropertyToID("_Sheen");
        static Material surface;
        public static Material Surface => surface ? surface : surface = MakeMaterial("FloorFractureSurface");
        MeshRenderer pane, body, frame, fracture;
        MeshFilter frameFilter;
        AudioSource crackVoice;
        bool lastWarning, lastLow;
        float lastPulse=-1;
        MaterialPropertyBlock block;
        public static Material MakeMaterial(string path)
        {
            var shader=Resources.Load<Shader>(path);
            if (!shader) throw new System.InvalidOperationException("Missing Floor Fracture shader "+path);
            return new Material(shader){name=path,hideFlags=HideFlags.DontSave};
        }
        public static MeshRenderer Part(Transform parent,string name,Mesh mesh,Material material)
        {
            var go=SceneParts.Child(parent,name,out bool created).gameObject;
            // The mesh generated for this run and its material replace the snapshots saved in the scene.
            SceneParts.Require<MeshFilter>(go).sharedMesh=mesh;
            var r=SceneParts.Require<MeshRenderer>(go); r.sharedMaterial=material;
            if(!created) return r;
            r.shadowCastingMode=ShadowCastingMode.Off; r.receiveShadows=false;
            r.lightProbeUsage=LightProbeUsage.Off; r.reflectionProbeUsage=ReflectionProbeUsage.Off; return r;
        }
        public static void Tint(Renderer renderer,Color color,float sheen=0)
        {
            var props=new MaterialPropertyBlock(); props.SetColor(ColorId,color); props.SetFloat(SheenId,sheen); renderer.SetPropertyBlock(props);
        }
        public void Initialize(Material material = null, Material bodyMaterial = null, float depthScale = 1f)
        {
            // ShowWarning asks on every draw. Only a board fitting its panes passes a material, and it builds again.
            if(pane && !material) return;
            if (!material) material=Surface;
            block ??= new MaterialPropertyBlock();
            pane=Part(transform,"Smoky pane",GlassGeometry.Face,material);
            if (bodyMaterial)
            {
                body=Part(transform,"Glass sides and bottom",GlassGeometry.BlockSides,bodyMaterial);
                body.transform.localScale=new Vector3(1,depthScale,1);
                ColorPart(body,resting,.7f);
            }
            frame=Part(transform,"Rim",GlassGeometry.SafeRim,material);
            frameFilter=frame.GetComponent<MeshFilter>();
            fracture=Part(transform,"Fissures",GlassGeometry.Cracks,material);
            ColorPart(pane,resting,.7f); ColorPart(frame,rim); fracture.enabled=false;
            // A pane fitted again starts as a new one: no warning showing, and no crack voice tuned to its last cell.
            lastWarning=lastLow=false; lastPulse=-1;
            if (crackVoice)
            {
                crackVoice.gameObject.SetActive(false);
                SceneParts.Release(crackVoice.gameObject);
                crackVoice=null;
            }
        }
#if UNITY_EDITOR
        // A colour painted on one renderer is not saved in a scene or prefab. In edit mode the resting look is
        // painted again whenever the Editor loads this pane or one of its colour fields changes.
        void OnValidate()
        {
            if (Application.isPlaying) return;
            foreach (var (part,colour,sheen) in new[] {("Smoky pane",resting,.7f),("Glass sides and bottom",resting,.7f),("Rim",rim,0f)})
            {
                var child=transform.Find(part);
                if (child && child.TryGetComponent<Renderer>(out var shown)) Tint(shown,colour,sheen);
            }
        }
#endif
        void ColorPart(Renderer r,Color color,float sheen=0)
        {
            block.SetColor(ColorId,color); block.SetFloat(SheenId,sheen); r.SetPropertyBlock(block);
        }
        internal void SoundWarning(GameSound sound, bool warned, int cell, int groupSize, double age, double remaining)
        {
            if (!warned || sound.MasterVolume <= 0f)
            {
                StopCrack();
                return;
            }

            bool start = !crackVoice || !crackVoice.gameObject.activeSelf;
            if (!crackVoice) crackVoice = sound.CreateCrackVoice(transform, cell);
            // Keep simultaneous cracks legible and soften the end of short reveals.
            float gain = .74f / Mathf.Sqrt(Mathf.Min(groupSize, 16));
            crackVoice.volume = sound.MasterVolume * gain * Mathf.Clamp01((float)remaining / .035f);
            if (!start) return;
            crackVoice.gameObject.SetActive(true);
            // Different positions in the recording keep simultaneous panes from sounding in unison.
            float offset = (cell % 7) * .071f;
            crackVoice.time = offset + (float)age * crackVoice.pitch;
            crackVoice.Play();
        }

        void StopCrack()
        {
            if (crackVoice && crackVoice.gameObject.activeSelf) crackVoice.gameObject.SetActive(false);
        }

        void OnDisable() => StopCrack();

        public void ShowWarning(bool warn,double revealAge,bool low,double revealDuration=.36)
        {
            Initialize();
            if(!warn&&!lastWarning) return;
            // Short expert clues still complete one amber pulse.
            float progress=Mathf.Clamp01((float)(revealAge/System.Math.Min(.36,revealDuration)));
            float pulse=warn && progress<1f ? Mathf.Sin(Mathf.PI*progress) : 0f;
            if(warn==lastWarning && low==lastLow && Mathf.Abs(pulse-lastPulse)<.025f) return;
            if (lastWarning != warn) frameFilter.sharedMesh=warn ? GlassGeometry.Rim : GlassGeometry.SafeRim;
            lastWarning=warn; lastLow=low; lastPulse=pulse; fracture.enabled=warn;
            ColorPart(frame,warn?Color.Lerp(warning,hot,low?.23f+pulse*.18f:.12f+pulse*.72f):rim);
            if(warn) ColorPart(fracture,Color.Lerp(warning,hot,low?.40f:.36f+pulse*.46f));
            Color tint=warn ? Color.Lerp(warnedFaceDim,warnedFaceBright,pulse*(low?.22f:.70f)) : resting;
            ColorPart(pane,tint,.7f);
            if (body) ColorPart(body,tint,.7f);
        }
    }
}
