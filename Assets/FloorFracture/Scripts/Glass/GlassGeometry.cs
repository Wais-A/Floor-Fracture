using System.Collections.Generic;
using UnityEngine;

namespace FloorFracture
{
    // All local coordinates are metres. The same faces supply the visible cracks and the shards.
    public static class GlassGeometry
    {
        public const float AuthoredPitch = .50f;
        public const float Half = AuthoredPitch * BoardState.PaneHalfPitch;
        public const float RingRadius = .069f, RingWidth = .007f;
        private const float KeylineWidth = RingWidth * .5f;
        public const float RingOuterRadius = RingRadius + RingWidth * .5f + KeylineWidth;
        public static readonly Vector2[] Points = {
            new(-Half,-Half), new(.038f,-Half), new(Half,-Half), new(Half,.068f),
            new(Half,Half), new(-.050f,Half), new(-Half,Half), new(-Half,-.035f),
            new(-.092f,-.113f), new(.045f,-.086f), new(.132f,-.110f), new(.115f,.030f),
            new(.087f,.108f), new(-.032f,.115f), new(-.111f,.084f), new(-.116f,-.007f),
            new(-.019f,.012f)
        };
        public static readonly int[][] Pieces = MakePieces();
        private static Mesh blockSides, face, rim, safeRim, cracks, ring;
        public static Mesh Face => face ? face : face = Sheet(false);
        public static Mesh Rim => rim ? rim : rim = Sheet(true);
        public static Mesh SafeRim => safeRim ? safeRim : safeRim = BeveledRim();
        public static Mesh Cracks => cracks ? cracks : cracks = CrackMesh();
        public static Mesh Ring => ring ? ring : ring = Circle(RingRadius,RingWidth,false,KeylineWidth);
        public static Vector3 Ground(Vector2 v,float height=0) => new(v.x,height,v.y);

        static int[][] MakePieces()
        {
            var faces=new List<int[]>();
            for(int k=0;k<8;k++)
            {
                int next=(k+1)%8;
                faces.Add(new[]{k,next,8+next,8+k});
                faces.Add(new[]{8+k,8+next,16});
            }
            return faces.ToArray();
        }

        public sealed class Builder
        {
            public readonly List<Vector3> Vertices = new();
            public readonly List<Color> Colors = new();
            public readonly List<Vector2> Uv = new();
            public readonly List<int> Indices = new();
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color top,Color bottom,float uvHeight=1,float uvWidth=1)
            {
                int first=Vertices.Count;
                Vertices.Add(a); Vertices.Add(b); Vertices.Add(c); Vertices.Add(d);
                Colors.Add(top); Colors.Add(top); Colors.Add(bottom); Colors.Add(bottom);
                Uv.Add(Vector2.zero); Uv.Add(new Vector2(uvWidth,0)); Uv.Add(new Vector2(uvWidth,uvHeight)); Uv.Add(new Vector2(0,uvHeight));
                Indices.Add(first); Indices.Add(first+2); Indices.Add(first+1);
                Indices.Add(first); Indices.Add(first+3); Indices.Add(first+2);
            }
            public void Stroke(Vector2 a,Vector2 b,float width,float y)
            {
                var v=(b-a).normalized;
                var n=new Vector2(-v.y,v.x)*width*.5f;
                Quad(Ground(a+n,y),Ground(b+n,y),Ground(b-n,y),Ground(a-n,y),Color.white,Color.white);
            }
            public void Write(Mesh mesh)
            {
                mesh.Clear();
                if (Vertices.Count > ushort.MaxValue) mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(Vertices); mesh.SetColors(Colors); mesh.SetUVs(0,Uv);
                mesh.SetTriangles(Indices,0); mesh.RecalculateBounds();
            }
            public Mesh Create(string name) { var m=new Mesh{name=name}; Write(m); return m; }
        }

        static Mesh Sheet(bool border)
        {
            var b=new Builder();
            if(border)
            {
                for(int i=0;i<8;i++) b.Stroke(Points[i],Points[(i+1)%8],.009f,.004f);
            }
            else b.Quad(Ground(Points[0]),Ground(Points[2]),Ground(Points[4]),Ground(Points[6]),Color.white,Color.white);
            return b.Create(border?"Glass rim":"Glass surface");
        }

        internal static Mesh BlockSides
        {
            get
            {
                if (blockSides) return blockSides;
                var b = new Builder();
                Vector3 down = Vector3.down * (2 * Half);
                for (int k = 0; k < 4; k++)
                {
                    var a = Ground(Points[k*2]);
                    var z = Ground(Points[((k+1)%4)*2]);
                    b.Quad(z,a,a+down,z+down,Color.white,Color.white);
                }
                b.Quad(Ground(Points[6])+down,Ground(Points[4])+down,
                    Ground(Points[2])+down,Ground(Points[0])+down,Color.white,Color.white);
                blockSides = b.Create("Glass block sides and bottom");
                blockSides.RecalculateNormals();
                return blockSides;
            }
        }

        static Mesh BeveledRim()
        {
            // The two bevel tones occupy the width of the ordinary 9 mm edge.
            var b = new Builder();
            var slate = new Color(.024f, .041f, .052f, 1f);
            const float edgeWidth = .009f;
            float outer = Half + edgeWidth * .5f;
            float split = outer - edgeWidth * .60f;
            float inner = Half - edgeWidth * .5f;
            var corners = new[] { new Vector2(-1,-1), new Vector2(1,-1),
                new Vector2(1,1), new Vector2(-1,1) };
            for (int i = 0; i < corners.Length; i++)
            {
                var a = corners[i];
                var z = corners[(i + 1) % corners.Length];
                b.Quad(Ground(a * outer,.004f), Ground(z * outer,.004f),
                    Ground(z * split,.004f), Ground(a * split,.004f), Color.white, Color.white);
                b.Quad(Ground(a * split,.004f), Ground(z * split,.004f),
                    Ground(z * inner,.004f), Ground(a * inner,.004f), slate, slate);
            }
            return b.Create("Intact glass rim");
        }

        static Mesh CrackMesh()
        {
            var b=new Builder();
            var edges=new HashSet<int>();
            foreach(int[] piece in Pieces)
            for(int i=0;i<piece.Length;i++)
            {
                int a=piece[i], c=piece[(i+1)%piece.Length];
                if(a<8 && c<8) continue;
                int key=Mathf.Min(a,c)*32+Mathf.Max(a,c);
                if(edges.Add(key)) b.Stroke(Points[a],Points[c],(a==16||c==16)?.0040f:.0050f,.006f);
            }
            return b.Create("Fitted fracture lines");
        }

        public static Mesh Circle(float radius,float width,bool dashed,float keylineWidth=0f)
        {
            var b=new Builder();
            float brightEdge=radius+width*.5f, outerEdge=brightEdge+keylineWidth;
            var charcoal=new Color(.012f,.018f,.022f,1f);
            for(int i=0;i<40;i++)
            {
                if(dashed && i%5==0) continue;
                float a=i*Mathf.PI/20, z=(i+1)*Mathf.PI/20;
                var start=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
                var end=new Vector2(Mathf.Cos(z),Mathf.Sin(z));
                if(keylineWidth>0)
                    b.Quad(Ground(start*brightEdge,.018f),Ground(end*brightEdge,.018f),
                        Ground(end*outerEdge,.018f),Ground(start*outerEdge,.018f),charcoal,charcoal);
                b.Stroke(start*radius,end*radius,width,.018f);
            }
            return b.Create("Occupancy ring");
        }
    }
}
