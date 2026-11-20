using UnityEngine;

namespace FloorFracture
{
    public sealed class GlassBoardView : MonoBehaviour
    {
        BoardState model;
        GlassTile[] tiles;
        GlassTile moving;
        MeshRenderer direction, occupancy, fog, holes, underground;
        ShardPool shards;
        Mesh cutouts, shaft, fogPlane, boundary, labelBackground;
        Material maskMaterial, wallsMaterial, fogMaterial, shardMaterial;
        TextMesh readout;
        Transform labelPlate;
        bool clockPositionSet;
        ulong holeBits;
        int practiceHole=-1, lossOpening=-1;
        string lastText;
        const float Depth=4.6f;
        public Transform Board => transform;

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
            moving=Instantiate(prefab,transform); moving.name="Sliding pane"; moving.Initialize(); moving.gameObject.SetActive(false);
            direction=GlassTile.Part(moving.transform,"Direction",GlassGeometry.Direction,GlassTile.Surface);
            GlassTile.Tint(direction,new Color(.8f,1f,.67f));
            occupancy=GlassTile.Part(transform,"Occupied square",GlassGeometry.Ring,GlassTile.Surface);
            occupancy.transform.localScale=fittedScale;
            GlassTile.Tint(occupancy,new Color(.90f,1f,.73f)); occupancy.enabled=false;
            maskMaterial=GlassTile.MakeMaterial("FloorFractureMask"); wallsMaterial=GlassTile.MakeMaterial("FloorFractureDepth");
            wallsMaterial.SetFloat("_Courses",1);
            shardMaterial=GlassTile.MakeMaterial("FloorFractureDepth"); fogMaterial=GlassTile.MakeMaterial("FloorFractureFog");
            cutouts=new Mesh{name="Open floor apertures"}; shaft=new Mesh{name="Connected shafts"};
            holes=GlassTile.Part(transform,"Opening stencil",cutouts,maskMaterial);
            underground=GlassTile.Part(transform,"Exposed walls and basin",shaft,wallsMaterial);
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
            text.GetComponent<MeshRenderer>().sharedMaterial=text.font.material;
            text.fontSize=96; text.characterSize=.026f; text.anchor=TextAnchor.LowerCenter; text.alignment=TextAlignment.Center;
            var b=new GlassGeometry.Builder();
            b.Quad(new(-.5f,-.5f,0),new(.5f,-.5f,0),new(.5f,.5f,0),new(-.5f,.5f,0),Color.white,Color.white);
            labelBackground=b.Create("Readout backing");
            var plate=GlassTile.Part(go.transform,"Contrast backing",labelBackground,GlassTile.Surface);
            plate.sortingOrder=-1; GlassTile.Tint(plate,new Color(.017f,.055f,.067f,.91f)); labelPlate=plate.transform;
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
            readout.transform.localPosition=p; clockPositionSet=true;
        }
        public void SetReadout(string text,Transform head)
        {
            if(!readout) return;
            if(text!=lastText)
            {
                readout.text=text; lastText=text;
                labelPlate.gameObject.SetActive(text.Length>0);
                if(text.Length>0)
                {
                    var bounds=readout.GetComponent<MeshRenderer>().localBounds;
                    if(bounds.size.x>.001f&&bounds.size.y>.001f)
                    {
                        float scale=Mathf.Min(.68f/bounds.size.x,.23f/bounds.size.y);
                        readout.transform.localScale=Vector3.one*scale;
                        labelPlate.localPosition=bounds.center+Vector3.forward*(.003f/scale);
                        labelPlate.localScale=new Vector3(bounds.size.x+.048f/scale,bounds.size.y+.025f/scale,1);
                    }
                }
            }
            if(!head||text.Length==0) return;
            if(!clockPositionSet) AnchorReadout(head);
            Vector3 away=readout.transform.position-head.position;
            if(away.sqrMagnitude>.001f) readout.transform.rotation=Quaternion.LookRotation(away,head.up);
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
                if(visible) tiles[i].ShowWarning((shown&&wave.IsBreaking(i))||i==practiceWarning,(float)(now%10)*.80f,gentle);
            }
            if(bits!=holeBits) RebuildHoles(bits);
            var slide=model.Slider;
            moving.gameObject.SetActive(slide!=null);
            if(slide!=null)
            {
                moving.transform.localPosition=GlassGeometry.Ground(slide.CenterAt(now),.014f);
                moving.transform.localScale=new Vector3(slide.HalfSize/GlassGeometry.Half,1,slide.HalfSize/GlassGeometry.Half);
                moving.ShowWarning(shown&&wave.IsBreaking(slide.Tile),(float)(now%10)*.8f,gentle);
                direction.enabled=!slide.Locked;
                direction.transform.localRotation=Quaternion.LookRotation(GlassGeometry.Ground(slide.Direction),Vector3.up);
            }
            occupancy.enabled=sample.Kind==SupportKind.Glass && sample.Index>=0 && sample.Index!=practiceHole && (sample.IsSlider||model.IsIntact(sample.Index));
            if(occupancy.enabled)
            {
                var pos=sample.IsSlider&&slide!=null?slide.CenterAt(now):model.Center(sample.Index);
                occupancy.transform.localPosition=GlassGeometry.Ground(pos,.014f);
            }
            fog.enabled=wave!=null&&wave.Fog;
        }
        public void Shatter(int index)
        {
            if(index<0||index>=model.Count) return;
            shards.Drop(GlassGeometry.Ground(model.Center(index),-.005f),index,Time.unscaledTime);
        }
        public void PracticeBreak(int index) { practiceHole=index; Shatter(index); }
        public void ClearTransient() { practiceHole=lossOpening=-1; if(shards) shards.Clear(); }
        public void RestorePracticePane() { practiceHole=-1; }
        public void RevealLossAperture(int cell)
        {
            if (cell < 0 || cell >= tiles.Length) return;
            lossOpening=cell;
            tiles[cell].gameObject.SetActive(false);
            occupancy.enabled=false;
            RebuildHoles(holeBits | (1UL << cell));
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
                // The falling player's shaft supplies this tile's walls and destination.
                // Keep the stencil aperture and the neighboring holes' regular geometry.
                if(i==lossOpening) continue;
                var down=Vector3.down*Depth; var bottom=new Color(.012f,.026f,.044f);
                w.Quad(c[0]+down,c[1]+down,c[2]+down,c[3]+down,bottom,bottom);
                int col=i%model.Width, row=i/model.Width;
                int[] neighbors={row>0?i-model.Width:-1,col+1<model.Width?i+1:-1,row+1<model.Height?i+model.Width:-1,col>0?i-1:-1};
                for(int j=0;j<4;j++)
                {
                    if(neighbors[j]>=0&&(bits&(1UL<<neighbors[j]))!=0) continue;
                    var a=c[j]; var b=c[(j+1)%4]; var top=Color.Lerp(new Color(.14f,.27f,.30f),new Color(.21f,.35f,.38f),j*.21f);
                    w.Quad(a,b,b+down,a+down,top,bottom,Depth);
                }
            }
            m.Write(cutouts); w.Write(shaft); holes.enabled=underground.enabled=bits!=0;
        }
        void OnDestroy()
        {
            foreach(Object asset in new Object[]{cutouts,shaft,fogPlane,boundary,labelBackground,maskMaterial,wallsMaterial,shardMaterial,fogMaterial})
                if(asset) Destroy(asset);
        }
    }
}
