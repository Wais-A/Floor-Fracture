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
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int SheenId = Shader.PropertyToID("_Sheen");
        static Material surface;
        public static Material Surface => surface ? surface : surface = MakeMaterial("FloorFractureSurface");
        MeshRenderer pane, body, frame, fracture;
        MeshFilter frameFilter;
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
            var go=new GameObject(name); go.transform.SetParent(parent,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var r=go.AddComponent<MeshRenderer>(); r.sharedMaterial=material;
            r.shadowCastingMode=ShadowCastingMode.Off; r.receiveShadows=false;
            r.lightProbeUsage=LightProbeUsage.Off; r.reflectionProbeUsage=ReflectionProbeUsage.Off; return r;
        }
        public static void Tint(Renderer renderer,Color color,float sheen=0)
        {
            var props=new MaterialPropertyBlock(); props.SetColor(ColorId,color); props.SetFloat(SheenId,sheen); renderer.SetPropertyBlock(props);
        }
        public void Initialize(Material material = null, Material bodyMaterial = null, float depthScale = 1f)
        {
            if(pane) return;
            if (!material) material=Surface;
            block=new MaterialPropertyBlock();
            pane=Part(transform,"Smoky pane",GlassGeometry.Face,material);
            if (bodyMaterial)
            {
                body=Part(transform,"Glass sides and bottom",GlassGeometry.BlockSides,bodyMaterial);
                body.transform.localScale=new Vector3(1,depthScale,1);
                ColorPart(body,Safe,.7f);
            }
            frame=Part(transform,"Rim",GlassGeometry.SafeRim,material);
            frameFilter=frame.GetComponent<MeshFilter>();
            fracture=Part(transform,"Fissures",GlassGeometry.Cracks,material);
            ColorPart(pane,Safe,.7f); ColorPart(frame,Edge); fracture.enabled=false;
        }
        void ColorPart(Renderer r,Color color,float sheen=0)
        {
            block.SetColor(ColorId,color); block.SetFloat(SheenId,sheen); r.SetPropertyBlock(block);
        }
        public void ShowWarning(bool warn,double revealAge,bool low)
        {
            Initialize();
            if(!warn&&!lastWarning) return;
            // A single 0.36-second amber pulse leaves the cracks readable for the rest of the reveal.
            float progress=Mathf.Clamp01((float)revealAge/.36f);
            float pulse=warn && progress<1f ? Mathf.Sin(Mathf.PI*progress) : 0f;
            if(warn==lastWarning && low==lastLow && Mathf.Abs(pulse-lastPulse)<.025f) return;
            if (lastWarning != warn) frameFilter.sharedMesh=warn ? GlassGeometry.Rim : GlassGeometry.SafeRim;
            lastWarning=warn; lastLow=low; lastPulse=pulse; fracture.enabled=warn;
            ColorPart(frame,warn?Color.Lerp(Danger,Fire,low?.23f+pulse*.18f:.12f+pulse*.72f):Edge);
            if(warn) ColorPart(fracture,Color.Lerp(Danger,Fire,low?.40f:.36f+pulse*.46f));
            Color tint=warn ? Color.Lerp(new Color(.36f,.24f,.23f,.79f), new Color(1f,.56f,.13f,.90f),pulse*(low?.22f:.70f)) : Safe;
            ColorPart(pane,tint,.7f);
            if (body) ColorPart(body,tint,.7f);
        }
    }
}
