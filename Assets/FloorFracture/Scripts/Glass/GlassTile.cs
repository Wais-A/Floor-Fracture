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
        MeshRenderer pane, frame, fracture;
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
        public void Initialize()
        {
            if(pane) return;
            block=new MaterialPropertyBlock();
            pane=Part(transform,"Smoky pane",GlassGeometry.Face,Surface);
            frame=Part(transform,"Rim",GlassGeometry.SafeRim,Surface);
            frameFilter=frame.GetComponent<MeshFilter>();
            fracture=Part(transform,"Fissures",GlassGeometry.Cracks,Surface);
            ColorPart(pane,Safe,.7f); ColorPart(frame,Edge); fracture.enabled=false;
        }
        void ColorPart(Renderer r,Color color,float sheen=0)
        {
            block.SetColor(ColorId,color); block.SetFloat(SheenId,sheen); r.SetPropertyBlock(block);
        }
        public void ShowWarning(bool warn,double now,bool low)
        {
            Initialize();
            if(!warn&&!lastWarning) return;
            // Slow luminance modulation. The branching shape stays visible at both intensity settings.
            float phase=(float)(now%10)*.8f;
            float pulse=warn?Mathf.SmoothStep(0,1,.5f-.5f*Mathf.Cos(phase*2*Mathf.PI)):0;
            if(warn==lastWarning && low==lastLow && Mathf.Abs(pulse-lastPulse)<.025f) return;
            if (lastWarning != warn) frameFilter.sharedMesh=warn ? GlassGeometry.Rim : GlassGeometry.SafeRim;
            lastWarning=warn; lastLow=low; lastPulse=pulse; fracture.enabled=warn;
            ColorPart(frame,warn?Color.Lerp(Danger,Fire,low?.23f+pulse*.18f:.12f+pulse*.72f):Edge);
            if(warn) ColorPart(fracture,Color.Lerp(Danger,Fire,low?.40f:.36f+pulse*.46f));
            ColorPart(pane,warn?new Color(.36f,.24f,.23f,.79f):Safe,.7f);
        }
    }
}
