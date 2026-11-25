using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace FloorFracture
{
    // Optional VR mode: passthrough off, the scanned room drawn by Meta's Effect Mesh so the player can see furniture.
    [DisallowMultipleComponent, RequireComponent(typeof(RoomBoardPlacement))]
    public sealed class ImmersiveRoom : MonoBehaviour
    {
        // Used when a room has no global mesh: Meta's labeled walls and furniture volumes.
        public const MRUKAnchor.SceneLabels FallbackLabels = MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.INNER_WALL_FACE |
            MRUKAnchor.SceneLabels.TABLE | MRUKAnchor.SceneLabels.COUCH | MRUKAnchor.SceneLabels.STORAGE | MRUKAnchor.SceneLabels.BED |
            MRUKAnchor.SceneLabels.SCREEN | MRUKAnchor.SceneLabels.LAMP | MRUKAnchor.SceneLabels.PLANT | MRUKAnchor.SceneLabels.OTHER;
        // CompareFunction.NotEqual: keep the stone out of broken panes, whose mask writes stencil bit 4.
        private const int OutsideHoles = 6;

        [SerializeField] private OVRPassthroughLayer passthrough;
        [SerializeField] private EffectMesh roomEffect;
        [SerializeField] private SkinnedMeshRenderer[] hands = new SkinnedMeshRenderer[0];
        [SerializeField] private Material virtualHand;
        [SerializeField] private Color background = new Color(.02f, .02f, .03f, 1);

        private RoomBoardPlacement placement;
        private Camera[] cameras = new Camera[0];
        private OVRManager manager;
        private Material realHand, mixedFloor, immersiveFloor;

        public bool On { get; private set; }
        public EffectMesh RoomEffect => roomEffect;

        private void Awake()
        {
            placement = GetComponent<RoomBoardPlacement>();
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig) { cameras = rig.GetComponentsInChildren<Camera>(true); manager = rig.GetComponent<OVRManager>(); }
            if (hands.Length > 0 && hands[0]) realHand = hands[0].sharedMaterial;
            if (roomEffect) roomEffect.HideMesh = true;
        }

        private void OnEnable() { if (placement) placement.Changed += Rebuild; }
        private void OnDisable() { if (placement) placement.Changed -= Rebuild; }
        private void OnDestroy() { if (immersiveFloor) Destroy(immersiveFloor); }

        public void Apply(bool on)
        {
            if (on == On) return;
            On = on;
            // The rig's own manager: OVRManager.instance stays null until XR starts.
            if (manager) manager.isInsightPassthroughEnabled = !on;
            if (passthrough) passthrough.hidden = on;
            foreach (var camera in cameras)
                if (camera) camera.backgroundColor = on ? background : Color.clear;
            // Real Hands cuts holes to passthrough, which would show black in VR.
            foreach (var hand in hands)
                if (hand && virtualHand && realHand) hand.sharedMaterial = on ? virtualHand : realHand;
            Rebuild();
        }

        private void Rebuild()
        {
            var floor = placement ? placement.FloorEffect : null;
            if (!roomEffect || !floor) return;
            roomEffect.DestroyMesh();
            floor.DestroyMesh();
            if (!mixedFloor) mixedFloor = floor.MeshMaterial;
            var room = placement.CurrentRoom;
            if (!On || placement.Status != RoomPlacementStatus.Confirmed || !room)
            {
                floor.MeshMaterial = mixedFloor;
                floor.HideMesh = roomEffect.HideMesh = true;
                return;
            }
            if (!immersiveFloor)
            {
                immersiveFloor = new Material(mixedFloor) { name = mixedFloor.name + " (Immersive)" };
                // Without passthrough there is no real floor to reveal, so draw the stone even where depth is unavailable.
                immersiveFloor.SetFloat("_RequireEnvironmentDepth", 0);
                immersiveFloor.SetInt("_PortalComparison", OutsideHoles);
            }
            floor.MeshMaterial = immersiveFloor;
            roomEffect.Labels = room.GlobalMeshAnchor ? MRUKAnchor.SceneLabels.GLOBAL_MESH : FallbackLabels;
            floor.HideMesh = roomEffect.HideMesh = false;
            roomEffect.CreateMesh(room);
            floor.CreateMesh(room);
        }
    }
}
