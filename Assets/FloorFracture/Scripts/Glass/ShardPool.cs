using System.Collections.Generic;
using UnityEngine;

namespace FloorFracture
{
    // One dynamic mesh draws every falling fragment, even when several panes drop at once.
    public sealed class ShardPool : MonoBehaviour
    {
        struct Piece
        {
            public int Face;
            public Vector3 Origin, Offset, Axis;
            public float Born, Spin, Speed, Shade;
        }
        readonly List<Piece> active=new(256);
        readonly List<Vector3> vertices=new(1024);
        readonly List<Color> colors=new(1024);
        readonly List<int> triangles=new(1800);
        readonly Vector3[] centroids=new Vector3[GlassGeometry.Pieces.Length];
        Mesh mesh;
        MeshRenderer view;
        float paneScale;
        float pausedAt;
        internal bool FullFall { get; set; }

        void OnDisable() { pausedAt = Time.unscaledTime; }
        void OnEnable()
        {
            float elapsed = Time.unscaledTime - pausedAt;
            for (int i = 0; i < active.Count; i++)
            {
                var piece = active[i];
                piece.Born += elapsed;
                active[i] = piece;
            }
        }

        public void Initialize(Material material, float tileScale)
        {
            paneScale=tileScale;
            mesh=new Mesh{name="Pooled falling glass"}; mesh.MarkDynamic();
            view=GlassTile.Part(transform,"Falling fragments",mesh,material); view.enabled=false;
            for(int i=0;i<GlassGeometry.Pieces.Length;i++)
            {
                var p=GlassGeometry.Pieces[i]; var c=Vector3.zero;
                foreach(int index in p) c+=GlassGeometry.Ground(GlassGeometry.Points[index])*paneScale;
                centroids[i]=c/p.Length;
            }
        }
        public void Drop(Vector3 center,int tile,float now)
        {
            for(int i=0;i<GlassGeometry.Pieces.Length;i++)
            {
                int hash=(tile*19+i*37)%61;
                active.Add(new Piece { Face=i, Origin=center+centroids[i], Born=now+hash*.0016f,
                    Offset=centroids[i]*(.28f+hash*.005f), Axis=new Vector3(.4f+hash%3,.2f,(i%2==0?1:-1)).normalized,
                    Spin=(i%2==0?-1:1)*(50+hash*2), Speed=.18f+hash*.009f, Shade=.48f+(hash%13)*.036f });
            }
            view.enabled=true;
        }
        public void Clear()
        {
            active.Clear(); if(mesh) mesh.Clear(); if(view) view.enabled=false;
        }
        void LateUpdate()
        {
            if(active.Count==0) return;
            vertices.Clear(); colors.Clear(); triangles.Clear(); float now=Time.unscaledTime;
            for(int k=active.Count-1;k>=0;k--)
            {
                var piece=active[k]; float t=Mathf.Max(0,now-piece.Born);
                if(t>(FullFall ? 2.55f : 1.65f)) { active.RemoveAt(k); continue; }
                var rot=Quaternion.AngleAxis(t*piece.Spin,piece.Axis);
                var center=piece.Origin+piece.Offset*t+Vector3.down*(piece.Speed*t+2.1f*t*t);
                var face=GlassGeometry.Pieces[piece.Face]; int first=vertices.Count;
                float flash=(.68f+.32f*Mathf.Abs(Mathf.Sin(t*3+piece.Shade)))*piece.Shade;
                var color=new Color(.24f*flash,.78f*flash,.84f*flash,1);
                foreach(int i in face)
                {
                    vertices.Add(center+rot*(GlassGeometry.Ground(GlassGeometry.Points[i])*paneScale-centroids[piece.Face])); colors.Add(color);
                }
                for(int i=1;i<face.Length-1;i++) { triangles.Add(first); triangles.Add(first+i); triangles.Add(first+i+1); }
            }
            mesh.Clear();
            if (vertices.Count > ushort.MaxValue) mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles,0); mesh.RecalculateBounds();
            view.enabled=active.Count!=0;
        }
        void OnDestroy() { if(mesh) Destroy(mesh); }
    }
}
