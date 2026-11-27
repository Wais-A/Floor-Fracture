using UnityEngine;

namespace FloorFracture
{
    /// <summary>
    /// The game's objects are saved in the scene. A builder adopts the saved object by name and
    /// creates one only when it is missing, so a bare object still gets everything it needs.
    /// </summary>
    public static class SceneParts
    {
        /// <summary>The direct child with this name, or a new one carrying these components.</summary>
        public static Transform Child(Transform parent, string name, out bool created, params System.Type[] components)
        {
            var found = parent.Find(name);
            created = !found;
            if (found) return found;
            var made = new GameObject(name, components).transform;
            made.SetParent(parent, false);
            return made;
        }

        public static T Require<T>(GameObject host) where T : Component =>
            host.TryGetComponent<T>(out var found) ? found : host.AddComponent<T>();

        /// <summary>The direct child with this name, or a new instance of the prefab under that name.</summary>
        public static GameObject Spawn(GameObject prefab, Transform parent, string name, out bool created)
        {
            var found = parent.Find(name);
            created = !found;
            if (found) return found.gameObject;
            GameObject made = null;
#if UNITY_EDITOR
            // Saved into the scene, the instance stays linked to its prefab.
            if (!Application.isPlaying) made = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
#endif
            if (!made) made = Object.Instantiate(prefab, parent, false);
            made.name = name;
            return made;
        }

        /// <summary>A private copy of Resources/Materials/name when that file exists, else a new material from the shader.</summary>
        public static Material MakeMaterial(string name, string shaderResource)
        {
            var saved = Resources.Load<Material>("Materials/" + name);
            if (saved) return new Material(saved) { name = name, hideFlags = HideFlags.DontSave };
            var shader = Resources.Load<Shader>(shaderResource);
            if (!shader) throw new System.InvalidOperationException("Missing Floor Fracture shader " + shaderResource);
            return new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
        }

        /// <summary>Destroys what a builder made. Asset files are never touched.</summary>
        public static void Release(Object item)
        {
            if (!item) return;
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(item)) return;
#endif
            if (Application.isPlaying) Object.Destroy(item);
            else Object.DestroyImmediate(item);
        }
    }
}
