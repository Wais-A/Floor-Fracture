using System;
using Oculus.Interaction;
using UnityEngine;

namespace FloorFracture
{
    public sealed class PracticeStone : MonoBehaviour
    {
        enum StoneState { Resting, Held, Flying, Hit, Returning }
        Rigidbody stone;
        Grabbable grabbable;
        ThrowWhenUnselected velocityThrow;
        BoardState board;
        Transform surface;
        GamePanel input;
        Action<int> onLanding;
        StoneState state;
        Vector3 previousLocal;
        Pose restPose;
        double stillSince, lastPoseTime;
#if UNITY_EDITOR
        readonly bool[] simulatedSelection = new bool[2];
#endif
        float releaseTime;
        const float Radius=.045f;
        public Rigidbody Body => stone;

        public void Begin(BoardState boardState,Transform boardTransform,GamePanel panel,Action<int> landing)
        {
            board=boardState; surface=boardTransform; input=panel; onLanding=landing;
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
                stone=go.GetComponent<Rigidbody>();
                grabbable=go.GetComponent<Grabbable>();
                grabbable.WhenPointerEventRaised+=OnPointer;
                velocityThrow=grabbable.VelocityThrow;
                velocityThrow.WhenThrown+=OnNativeThrow;
            }
            ReturnStone();
        }
        public void End()
        {
            onLanding=null;
            if(stone) stone.gameObject.SetActive(false);
        }
        public void ReturnStone()
        {
            if(!stone||onLanding==null) return;
            // Disabling an interactable releases any old selection before respawning it.
            state=StoneState.Returning;
            stone.gameObject.SetActive(false);
            if(!input.Tracked) return;
            stone.useGravity=false;
            stone.isKinematic=false;
            Vector3 forward=Vector3.ProjectOnPlane(input.Head.forward,Vector3.up);
            if(forward.sqrMagnitude<.01f) forward=Vector3.Cross(input.Head.right,Vector3.up);
            Vector3 spawn=input.Head.position+forward.normalized*.34f+Vector3.down*.27f;
            stone.transform.position=spawn;
            state=StoneState.Resting;
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
            if(onLanding==null||!stone.gameObject.activeInHierarchy||state==StoneState.Returning||state==StoneState.Hit) return;
            if(evt.Type==PointerEventType.Select)
            {
                state=StoneState.Held; stone.useGravity=true;
                ObserveHeldPose(true);
            }
            else if(evt.Type==PointerEventType.Move&&state==StoneState.Held)
            {
                bool lostConfidence=HandTrackingConfidenceProvider.TryGetTrackingConfidence(evt.Identifier,out bool confident)&&!confident;
                ObserveHeldPose(lostConfidence);
            }
            else if(state==StoneState.Held&&grabbable.SelectingPointsCount==0)
            {
                if(evt.Type==PointerEventType.Cancel) state=StoneState.Returning;
                else if(evt.Type==PointerEventType.Unselect)
                {
                    // Grabbable supplies the release velocity. Only the landing is game-specific.
                    state=StoneState.Flying; releaseTime=Time.unscaledTime;
                    previousLocal=surface.InverseTransformPoint(stone.position);
                }
            }
        }

        void ObserveHeldPose(bool restart=false)
        {
            var pose=new Pose(stone.transform.position,stone.transform.rotation);
            double now=Time.unscaledTimeAsDouble;
            // Require continuous samples within 4 mm and two degrees. A tracking gap
            // or renewed movement starts a new observation period.
            if(restart||now-lastPoseTime>.12||(pose.position-restPose.position).sqrMagnitude>.004f*.004f||
                Quaternion.Angle(pose.rotation,restPose.rotation)>2f)
            {
                restPose=pose;
                stillSince=now;
            }
            lastPoseTime=now;
        }

        void OnNativeThrow(Vector3 velocity,Vector3 angularVelocity)
        {
            if(onLanding==null||(state!=StoneState.Held&&state!=StoneState.Flying)||stone.isKinematic) return;
            ObserveHeldPose();
            if(lastPoseTime-stillSince<.25) return;
            // ISDK excludes identical positions from its velocity history. A player
            // who carried the stone and held it still long enough intends a drop.
            stone.linearVelocity=Vector3.zero;
            stone.angularVelocity=Vector3.zero;
            if(Debug.isDebugBuild)
                Debug.Log($"[Floor Fracture] Practice stone held still for {lastPoseTime-stillSince:F2} s. Native {velocity.magnitude:F3} m/s, applied {stone.linearVelocity.magnitude:F3} m/s.");
        }
#if UNITY_EDITOR
        // The desktop fixture has no physical controllers. It feeds ordinary ISDK pointer events
        // into the prefab so the SDK transformer and grab/release lifecycle still run.
        void Update()
        {
            if(onLanding==null||!stone.gameObject.activeInHierarchy||!input.EditorSimulatesControllers||state==StoneState.Hit||state==StoneState.Returning) return;
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
                else if(state==StoneState.Resting&&input.GripHeld(left)&&Vector3.Distance(hand.position,stone.position)<=.16f)
                {
                    grabbable.ProcessPointerEvent(new PointerEvent(id,PointerEventType.Hover,pose));
                    grabbable.ProcessPointerEvent(new PointerEvent(id,PointerEventType.Select,pose)); simulatedSelection[i]=true;
                }
            }
        }
#endif
        void OnDestroy()
        {
            if(grabbable) grabbable.WhenPointerEventRaised-=OnPointer;
            if(velocityThrow!=null) velocityThrow.WhenThrown-=OnNativeThrow;
        }
        void OnApplicationPause(bool pause) { if(pause&&state==StoneState.Held) state=StoneState.Returning; }
        void OnApplicationFocus(bool focus) { if(!focus&&state==StoneState.Held) state=StoneState.Returning; }
        void FixedUpdate()
        {
            if(onLanding==null||!stone||!surface) return;
            if(state==StoneState.Held&&!input.Tracked) state=StoneState.Returning;
            if(state==StoneState.Returning)
            {
                if(input.Tracked) ReturnStone();
                return;
            }
            if(state!=StoneState.Flying&&state!=StoneState.Hit) return;
            var now=surface.InverseTransformPoint(stone.position);
            if(state==StoneState.Hit)
            {
                if(now.y<-1.4f&&stone.gameObject.activeSelf) stone.gameObject.SetActive(false);
                return;
            }
            if(now.y<=Radius&&previousLocal.y>=Radius)
            {
                float t=Mathf.InverseLerp(previousLocal.y,now.y,Radius);
                var contact=Vector3.Lerp(previousLocal,now,t);
                int tile=board.IndexAt(new(contact.x,contact.z));
                if(board.IsIntact(tile))
                {
                    state=StoneState.Hit; onLanding(tile);
                    return;
                }
            }
            if(now.y<=Radius||Time.unscaledTime-releaseTime>4)
            {
                onLanding(-1);
                ReturnStone();
                return;
            }
            previousLocal=now;
        }
    }
}
