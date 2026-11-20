#if UNITY_EDITOR
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FloorFracture
{
    // The macOS simulator supplies a scanned synthetic room but does not supply live Quest depth.
    internal sealed class SimulatorRoomOcclusion : MonoBehaviour
    {
        private Mesh mesh;
        private Material material;

        internal static SimulatorRoomOcclusion Create(MRUKRoom room, float floorHeight, int floorQueue)
        {
            var anchor = room.GlobalMeshAnchor;
            var scan = anchor ? anchor.GlobalMesh : null;
            var stock = AssetDatabase.LoadAssetAtPath<Shader>(
                "Packages/com.meta.xr.mrutilitykit/Core/Shaders/InvisibleOccluder.shader");
            if (!scan || !scan.isReadable || !stock) return null;

            // Keep the furniture silhouettes, including gaps in the mesh. The floor's own triangles
            // would compete with the brick and could seal the glass holes.
            var vertices = scan.vertices;
            var aboveFloor = new bool[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                aboveFloor[i] = anchor.transform.TransformPoint(vertices[i]).y > floorHeight + .035f;
            var triangles = scan.triangles;
            var retained = new List<int>(triangles.Length);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                if (!aboveFloor[a] && !aboveFloor[b] && !aboveFloor[c]) continue;
                retained.Add(a);
                retained.Add(b);
                retained.Add(c);
            }
            if (retained.Count == 0) return null;

            var go = new GameObject("Simulator room furniture depth", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(anchor.transform, false);
            var preview = go.AddComponent<SimulatorRoomOcclusion>();
            preview.mesh = Instantiate(scan);
            preview.mesh.SetTriangles(retained, 0);
            preview.material = new Material(stock) { renderQueue = floorQueue - 1 };
            go.GetComponent<MeshFilter>().sharedMesh = preview.mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = preview.material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return preview;
        }

        private void OnDestroy()
        {
            if (mesh) Destroy(mesh);
            if (material) Destroy(material);
        }
    }
}
#endif
