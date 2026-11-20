using UnityEngine;

namespace FloorFracture
{
    public sealed class GlassBoardView : MonoBehaviour
    {
        static readonly Vector3[] EdgeNormals = { Vector3.back, Vector3.right, Vector3.forward, Vector3.left };
        static readonly float[] LedgeDepths = { .10f, 1.15f };
        BoardState model;
        GlassTile[] tiles;
        GlassTile moving;
        MeshRenderer occupancy, fog, holes, underground;
        ShardPool shards;
        Mesh cutouts, shaft, fogPlane, boundary, labelBackground;
        Material maskMaterial, wallsMaterial, fogMaterial, shardMaterial;
        TextMesh readout;
        Transform labelPlate;
        bool clockPositionSet;
        float readoutWidth=.68f, readoutHeight=.23f;
        ulong holeBits;
        Vector2? outsideOpening;
        int practiceHole=-1;
        string lastText;
        public Transform Board => transform;
        internal bool HasOpenPanes => isActiveAndEnabled && (holeBits != 0 || outsideOpening.HasValue);
        internal float ChamberRadius
        {
            get
            {
                float extent = Mathf.Max(model.Width,model.Height)*model.Pitch*.5f;
                if (outsideOpening is Vector2 center)
                    extent = Mathf.Max(extent, Mathf.Max(Mathf.Abs(center.x),Mathf.Abs(center.y)) + model.Pitch*.5f);
                return Mathf.Max(1.48f, extent/.76f+.4f);
            }
        }
        internal float PaneWidth => model.Pitch;

        public void Build(BoardState board,Pose pose)
        {
            model=board; transform.SetPositionAndRotation(pose.position+Vector3.up*.009f,pose.rotation);
            var template=Resources.Load<GameObject>("GlassTile");
            var prefab=template?template.GetComponent<GlassTile>():null;
            if(!prefab) throw new System.InvalidOperationException("Missing Resources/GlassTile prefab");
            tiles=new GlassTile[board.Count];
            float paneScale=board.Pitch/GlassGeometry.AuthoredPitch;
            var fittedScale=new Vector3(paneScale,1,paneScale);
            for(int i=0;i<tiles.Length;i++)
            {
                var t=Instantiate(prefab,transform); t.name="Glass "+(i%board.Width)+", "+(i/board.Width);
                t.transform.localPosition=GlassGeometry.Ground(board.Center(i));
                t.transform.localScale=fittedScale; t.Initialize(); tiles[i]=t;
            }
            moving=Instantiate(prefab,transform); moving.name="Sliding pane"; moving.transform.localScale=fittedScale;
            moving.Initialize(); moving.gameObject.SetActive(false);
            occupancy=GlassTile.Part(transform,"Occupied square",GlassGeometry.Ring,GlassTile.Surface);
            occupancy.transform.localScale=fittedScale;
            GlassTile.Tint(occupancy,new Color(.90f,1f,.73f)); occupancy.enabled=false;
            maskMaterial=GlassTile.MakeMaterial("FloorFractureMask"); wallsMaterial=GlassTile.MakeMaterial("FloorFractureDepth");
            wallsMaterial.SetFloat("_Courses",1);
            shardMaterial=GlassTile.MakeMaterial("FloorFractureDepth"); fogMaterial=GlassTile.MakeMaterial("FloorFractureFog");
            cutouts=new Mesh{name="Open floor apertures"}; shaft=new Mesh{name="Connected shafts"};
            holes=GlassTile.Part(transform,"Opening stencil",cutouts,maskMaterial);
            underground=GlassTile.Part(transform,"Glass opening edges",shaft,wallsMaterial);
            shards=gameObject.AddComponent<ShardPool>(); shards.Initialize(shardMaterial,paneScale);
            float x=board.Width*board.Pitch*.5f, z=board.Height*board.Pitch*.5f;
            var b=new GlassGeometry.Builder();
            Vector2[] corners={new(-x,-z),new(x,-z),new(x,z),new(-x,z)};
            for(int i=0;i<4;i++) b.Stroke(corners[i],corners[(i+1)%4],.012f,.006f);
            boundary=b.Create("Board perimeter"); var border=GlassTile.Part(transform,"Perimeter",boundary,GlassTile.Surface);
            GlassTile.Tint(border,new Color(.61f,.82f,.85f,.96f));
            b=new GlassGeometry.Builder();
            b.Quad(new(-x,.052f,-z),new(x,.052f,-z),new(x,.052f,z),new(-x,.052f,z),Color.white,Color.white);
            fogPlane=b.Create("Fog bank"); fog=GlassTile.Part(transform,"Low fog",fogPlane,fogMaterial); fog.enabled=false;
            readout=CreateReadout(); ClearTransient(); RebuildHoles(0);
        }
        TextMesh CreateReadout()
        {
            var go=new GameObject("Countdown and score"); go.transform.SetParent(transform,false);
            var text=go.AddComponent<TextMesh>(); text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var letters=text.GetComponent<MeshRenderer>();
            letters.sharedMaterial=text.font.material;
            letters.sortingOrder=2;
            text.fontSize=96; text.characterSize=.026f; text.anchor=TextAnchor.LowerCenter; text.alignment=TextAlignment.Center;
            var b=new GlassGeometry.Builder();
            b.Quad(new(-.5f,-.5f,0),new(.5f,-.5f,0),new(.5f,.5f,0),new(-.5f,.5f,0),Color.white,Color.white);
            labelBackground=b.Create("Readout backing");
            var plate=GlassTile.Part(go.transform,"Contrast backing",labelBackground,GlassTile.Surface);
            // Both draws follow the transparent room floor. Drawing the backing first in
            // the scene lets the bricks paint over it and leaves white numbers on mortar.
            plate.sortingOrder=1; GlassTile.Tint(plate,new Color(.017f,.055f,.067f,.94f)); labelPlate=plate.transform;
            text.color=new Color(.91f,1,.94f); return text;
        }
        public void AnchorReadout(Transform head)
        {
            if(!head||!readout) return;
            var local=transform.InverseTransformPoint(head.position);
            Vector3 bearing=local.x*local.x+local.z*local.z<.04f?transform.InverseTransformDirection(head.forward):new Vector3(-local.x,0,-local.z);
            float x=model.Width*model.Pitch*.5f, z=model.Height*model.Pitch*.5f;
            var p=Mathf.Abs(bearing.x)/x>Mathf.Abs(bearing.z)/z?
                new Vector3(Mathf.Sign(bearing.x)*(x+.08f),.11f,0):new Vector3(0,.11f,Mathf.Sign(bearing.z)*(z+.08f));
            // On a long strip put the sign on the side ahead of the player. The far end
            // can be many metres away, even though the guaranteed move is a single step.
            var onFloor = new Vector2(local.x,local.z);
            if (Vector2.Distance(onFloor,new Vector2(p.x,p.z))>3.1f)
            {
                var gaze=transform.InverseTransformDirection(head.forward);
                if (x>=z)
                    p=new Vector3(Mathf.Clamp(local.x+Mathf.Sign(bearing.x)*1.15f,-x,x),.11f,
                        Mathf.Sign(Mathf.Abs(gaze.z)>.25f?gaze.z:local.z)*(z+.08f));
                else
                    p=new Vector3(Mathf.Sign(Mathf.Abs(gaze.x)>.25f?gaze.x:local.x)*(x+.08f),.11f,
                        Mathf.Clamp(local.z+Mathf.Sign(bearing.z)*1.15f,-z,z));
            }
            readout.transform.localPosition=p; clockPositionSet=true;
            float distance=Vector3.Distance(p,local);
            readoutWidth=Mathf.Clamp(distance*.30f,.63f,1.05f);
            readoutHeight=Mathf.Clamp(distance*.11f,.22f,.36f);
            lastText=null;
        }
        public void SetReadout(string text,Transform head)
        {
            if(!readout) return;
            if(!clockPositionSet && head && text.Length>0) AnchorReadout(head);
            if(text!=lastText)
            {
                readout.text=text; lastText=text;
                labelPlate.gameObject.SetActive(text.Length>0);
                if(text.Length>0)
                {
                    var bounds=readout.GetComponent<MeshRenderer>().localBounds;
                    if(bounds.size.x>.001f&&bounds.size.y>.001f)
                    {
                        float scale=Mathf.Min(readoutWidth/bounds.size.x,readoutHeight/bounds.size.y);
                        readout.transform.localScale=Vector3.one*scale;
                        labelPlate.localPosition=bounds.center+Vector3.forward*(.003f/scale);
                        labelPlate.localScale=new Vector3(bounds.size.x+.048f/scale,bounds.size.y+.025f/scale,1);
                    }
                }
            }
            if(!head||text.Length==0) return;
            Vector3 away=readout.transform.position-head.position;
            if(away.sqrMagnitude>.001f) readout.transform.rotation=Quaternion.LookRotation(away,Vector3.up);
        }
        public void Draw(WavePlan wave,TileSample sample,double now,bool gentle,int practiceWarning=-1)
        {
            if(tiles==null) return;
            ulong bits=practiceHole>=0?1UL<<practiceHole:0UL;
            bool shown=wave!=null&&wave.ShowsWarnings(now);
            for(int i=0;i<tiles.Length;i++)
            {
                if(!model.IsIntact(i)) bits|=1UL<<i;
                bool visible=(bits&(1UL<<i))==0;
                if(tiles[i].gameObject.activeSelf!=visible) tiles[i].gameObject.SetActive(visible);
                if(visible) tiles[i].ShowWarning((shown&&wave.IsBreaking(i))||i==practiceWarning,now,gentle);
            }
            if(bits!=holeBits) RebuildHoles(bits);
            var slide=model.Slider;
            moving.gameObject.SetActive(slide!=null);
            if(slide!=null)
            {
                moving.transform.localPosition=GlassGeometry.Ground(slide.CenterAt(now));
                moving.ShowWarning(shown&&wave.IsBreaking(slide.Tile),now,gentle);
            }
            occupancy.enabled=sample.Kind==SupportKind.Glass && sample.Index>=0 && sample.Index!=practiceHole &&
                (sample.IsSlider ? slide!=null && slide.Tile==sample.Index : model.IsIntact(sample.Index));
            if(occupancy.enabled)
            {
                var center=sample.IsSlider&&slide!=null?slide.CenterAt(now):model.Center(sample.Index);
                // Keep the whole ring on the supporting glass when a foot overlaps a seam.
                float inset=(GlassGeometry.Half-GlassGeometry.RingOuterRadius)*occupancy.transform.localScale.x;
                Vector2 contact=sample.Stance-center;
                contact.x=Mathf.Clamp(contact.x,-inset,inset);
                contact.y=Mathf.Clamp(contact.y,-inset,inset);
                occupancy.transform.localPosition=GlassGeometry.Ground(center+contact,.014f);
            }
            fog.enabled=wave!=null&&wave.Fog;
        }
        public void Shatter(int index)
        {
            if (index >= 0 && index < model.Count) Shatter(index,model.Center(index));
        }
        internal Vector3 Shatter(int index, Vector2 center)
        {
            var local = GlassGeometry.Ground(center);
            shards.Drop(local,index,Time.unscaledTime);
            return transform.TransformPoint(local);
        }
        public void PracticeBreak(int index) { practiceHole=index; Shatter(index); }
        public void ClearTransient() { practiceHole=-1; if(shards) shards.Clear(); }
        public void RestorePracticePane() { practiceHole=-1; }
        public void RevealLossAperture(int cell)
        {
            occupancy.enabled=false;
            if (cell < 0 || cell >= tiles.Length) return;
            tiles[cell].gameObject.SetActive(false);
            RebuildHoles(holeBits | (1UL << cell));
        }

        // A development preview or a leaning eye can be just outside the fitted grid.
        // Continue its grid lines into the brick so this opening never covers an intact pane.
        internal Vector3? OpenOutsidePane(Vector3 entry)
        {
            Vector2 local = PlayerTileTracker.Project(transform,entry);
            if (model.IndexAt(local) >= 0) return null;
            float pitch = model.Pitch;
            var half = new Vector2(model.Width, model.Height) * .5f;
            var cell = local/pitch + half;
            var center = (new Vector2(Mathf.Floor(cell.x)+.5f,Mathf.Floor(cell.y)+.5f) - half) * pitch;
            outsideOpening = center;
            RebuildHoles(holeBits);
            return transform.TransformPoint(GlassGeometry.Ground(center));
        }

        internal void CloseOutsidePane()
        {
            if (!outsideOpening.HasValue) return;
            outsideOpening = null;
            RebuildHoles(holeBits);
        }

        internal void ShowShardsBelowRim(bool belowRim)
        {
            var comparison = belowRim ? UnityEngine.Rendering.CompareFunction.Always : UnityEngine.Rendering.CompareFunction.Equal;
            // The flared preview throat belongs inside the open panes even from below.
            // Its back faces would otherwise cover the rising brick and intact glass.
            shardMaterial.SetInt("_PortalComparison", (int)comparison);
        }
        void RebuildHoles(ulong bits)
        {
            holeBits=bits; var m=new GlassGeometry.Builder(); var w=new GlassGeometry.Builder();
            for(int i=0;i<model.Count;i++)
            {
                if((bits&(1UL<<i))==0) continue;
                var p=GlassGeometry.Ground(model.Center(i)); float h=model.Pitch*.5f;
                Vector3[] c={p+new Vector3(-h,0,-h),p+new Vector3(h,0,-h),p+new Vector3(h,0,h),p+new Vector3(-h,0,h)};
                m.Quad(c[0],c[1],c[2],c[3],Color.white,Color.white);
                // A short, flared brick throat gives a safe viewer near/far parallax.
                // It has no bottom. The landing is in the shared chamber 80 metres below.
                int col=i%model.Width, row=i/model.Width;
                int[] neighbors={row>0?i-model.Width:-1,col+1<model.Width?i+1:-1,row+1<model.Height?i+model.Width:-1,col>0?i-1:-1};
                bool Exposed(int edge) => neighbors[edge]<0 || (bits&(1UL<<neighbors[edge]))==0;
                for(int j=0;j<4;j++)
                {
                    if(!Exposed(j)) continue;
                    int previous=(j+3)%4, next=(j+1)%4;
                    Vector3 normal=EdgeNormals[j];
                    Vector3 expandA=normal+(Exposed(previous)?EdgeNormals[previous]:Vector3.zero);
                    Vector3 expandB=normal+(Exposed(next)?EdgeNormals[next]:Vector3.zero);
                    Vector3 a=c[j], b=c[next];
                    var top=Color.white*(.82f+j*.05f);
                    var lower=Color.white*.59f;
                    const float throatDepth=2.4f, flare=.30f;
                    Vector3 aLower=a+Vector3.down*throatDepth+expandA*(throatDepth*flare);
                    Vector3 bLower=b+Vector3.down*throatDepth+expandB*(throatDepth*flare);
                    w.Quad(a,b,bLower,aLower,top,lower,throatDepth,model.Pitch);
                    // A stone cap at the mouth and one ledge below make the depth legible
                    // while the far water or lava stays visible through the center.
                    foreach(float depth in LedgeDepths)
                    {
                        Vector3 aBack=a+Vector3.down*depth+expandA*(depth*flare);
                        Vector3 bBack=b+Vector3.down*depth+expandB*(depth*flare);
                        Vector3 aFront=aBack-expandA*.045f, bFront=bBack-expandB*.045f;
                        w.Quad(aBack,bBack,bFront,aFront,Color.white,Color.white,.045f,model.Pitch);
                        w.Quad(aFront,bFront,bFront+Vector3.down*.055f,aFront+Vector3.down*.055f,
                            lower,lower,.055f,model.Pitch);
                    }
                }
            }
            if (outsideOpening is Vector2 outside)
            {
                float h = model.Pitch*.5f;
                Vector3 p = GlassGeometry.Ground(outside);
                m.Quad(p + new Vector3(-h,0,-h), p + new Vector3(h,0,-h), p + new Vector3(h,0,h), p + new Vector3(-h,0,h), Color.white,Color.white);
            }
            m.Write(cutouts); w.Write(shaft);
            holes.enabled = bits!=0 || outsideOpening.HasValue;
            underground.enabled = bits!=0;
        }
        void OnDestroy()
        {
            foreach(Object asset in new Object[]{cutouts,shaft,fogPlane,boundary,labelBackground,maskMaterial,wallsMaterial,shardMaterial,fogMaterial})
                if(asset) Destroy(asset);
        }
    }
}
