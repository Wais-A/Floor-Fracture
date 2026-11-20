using System;
using Oculus.Interaction;
using UnityEngine;

namespace FloorFracture
{
    public sealed class PracticeStone : MonoBehaviour
    {
        Rigidbody stone;
        Grabbable grabbable;
        BoardState board;
        Transform surface;
        GamePanel input;
        int target;
        Action onHit;
        bool held,thrown,passed;
        Vector3 previousLocal;
#if UNITY_EDITOR
        readonly bool[] simulatedSelection = new bool[2];
#endif
        float releaseTime;
        const float Radius=.045f;
        public int Target => target;
        public Rigidbody Body => stone;

        public void Begin(BoardState boardState,Transform boardTransform,GamePanel panel,Action hit)
        {
            board=boardState; surface=boardTransform; input=panel; onHit=hit;
            Vector3 local=surface.InverseTransformPoint(input.Head.position+Vector3.ProjectOnPlane(input.Head.forward,Vector3.up).normalized*.35f);
            int col=Mathf.Clamp(Mathf.FloorToInt(local.x/board.Pitch+board.Width*.5f),0,board.Width-1);
            int row=Mathf.Clamp(Mathf.FloorToInt(local.z/board.Pitch+board.Height*.5f),0,board.Height-1);
            target=row*board.Width+col;
            if(!stone)
            {
                var template=Resources.Load<GameObject>("PracticeStone");
                if(!template) throw new InvalidOperationException("Missing baked Meta Grab Interaction stone.");
                var go=Instantiate(template,transform); go.name="Practice stone";
                var stoneRenderer=go.GetComponentInChildren<MeshRenderer>();
                var tint=new MaterialPropertyBlock();
                stoneRenderer.GetPropertyBlock(tint);
                tint.SetColor("_Color",new Color(.73f,.67f,.53f,1f));
                stoneRenderer.SetPropertyBlock(tint);
                stone=go.GetComponent<Rigidbody>(); grabbable=go.GetComponent<Grabbable>(); grabbable.WhenPointerEventRaised+=OnPointer;
            }
            ResetStone();
        }
        public void End()
        {
            onHit=null;
            if(stone) stone.gameObject.SetActive(false);
            held=thrown=passed=false;
        }
        void ResetStone()
        {
            // Disabling an interactable releases any old selection before respawning it.
            stone.gameObject.SetActive(false); held=thrown=passed=false; stone.useGravity=false;
            stone.isKinematic=false;
            Vector3 forward=Vector3.ProjectOnPlane(input.Head.forward,Vector3.up).normalized;
            if(forward.sqrMagnitude<.5f) forward=surface.forward;
            Vector3 spawn=input.Head.position+forward*.34f+Vector3.down*.27f;
            stone.transform.position=spawn;
            stone.gameObject.SetActive(true);
            stone.position=spawn;
            stone.linearVelocity=Vector3.zero; stone.angularVelocity=Vector3.zero;
#if UNITY_EDITOR
            Array.Clear(simulatedSelection,0,simulatedSelection.Length);
#endif
            previousLocal=surface.InverseTransformPoint(stone.position);
        }
        void OnPointer(PointerEvent evt)
        {
            if(!HasActiveStone) return;
            if(evt.Type==PointerEventType.Select)
            {
                held=true; thrown=false; stone.useGravity=true;
            }
            else if((evt.Type==PointerEventType.Unselect||evt.Type==PointerEventType.Cancel)&&held&&grabbable.SelectingPointsCount==0)
            {
                held=false; thrown=true; releaseTime=Time.unscaledTime;
                previousLocal=surface.InverseTransformPoint(stone.position);
            }
        }
        bool HasActiveStone => onHit!=null&&stone&&stone.gameObject.activeInHierarchy;
#if UNITY_EDITOR
        // The desktop fixture has no physical controllers. It feeds ordinary ISDK pointer events
        // into the prefab so the SDK transformer and grab/release lifecycle still run.
        void Update()
        {
            if(!HasActiveStone||!input.EditorSimulatesControllers||passed) return;
            for(int i=0;i<2;i++)
            {
                bool left=i==0; var hand=input.Grip(left); if(!hand) continue;
                var pose=new Pose(hand.position,hand.rotation); int id=-920-i;
                if(simulatedSelection[i])
                {
                    grabbable.ProcessPointerEvent(new PointerEvent(id,PointerEventType.Move,pose));
                    if(!input.GripHeld(left))
                    {
                        grabbable.ProcessPointerEvent(new PointerEvent(id,PointerEventType.Unselect,pose));
                        grabbable.ProcessPointerEvent(new PointerEvent(id,PointerEventType.Unhover,pose)); simulatedSelection[i]=false;
                    }
                }
                else if(!held&&!thrown&&input.GripHeld(left)&&Vector3.Distance(hand.position,stone.position)<=.16f)
                {
                    grabbable.ProcessPointerEvent(new PointerEvent(id,PointerEventType.Hover,pose));
                    grabbable.ProcessPointerEvent(new PointerEvent(id,PointerEventType.Select,pose)); simulatedSelection[i]=true;
                }
            }
        }
#endif
        void OnDestroy() { if(grabbable) grabbable.WhenPointerEventRaised-=OnPointer; }
        void FixedUpdate()
        {
            if(!thrown||!stone||!surface) return;
            var now=surface.InverseTransformPoint(stone.position);
            if(!passed&&now.y<=Radius&&previousLocal.y>=Radius)
            {
                float t=Mathf.InverseLerp(previousLocal.y,now.y,Radius);
                var contact=Vector3.Lerp(previousLocal,now,t);
                if(board.IndexAt(new(contact.x,contact.z))==target)
                {
                    passed=true; onHit?.Invoke();
                }
                else { ResetStone(); return; }
            }
            if(passed&&now.y<-1.4f) { End(); return; }
            if(!passed&&(now.y<-.14f||Time.unscaledTime-releaseTime>4)) { ResetStone(); return; }
            previousLocal=now;
        }
    }
}
