using System;
using Oculus.Interaction;
using Oculus.Interaction.Editor.QuickActions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FloorFracture.Editor
{
    public static class MenuSceneSetup
    {
        public static void Install(Transform gameRoot)
        {
            var child = gameRoot.Find(GamePanel.CanvasName);
            if (!child)
            {
                var created = new GameObject(GamePanel.CanvasName, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                Undo.RegisterCreatedObjectUndo(created, "Add Floor Fracture menu canvas");
                created.transform.SetParent(gameRoot, false);
                child = created.transform;
            }
            var canvas = child.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)child).sizeDelta = new Vector2(672, 370);
            child.localScale = Vector3.one * 0.00116f;
            if (!child.GetComponentInChildren<RayInteractable>(true))
                QuickActionsAPI.AddRayCanvasInteraction(child.gameObject);
            if (!child.GetComponentInChildren<PokeInteractable>(true))
                QuickActionsAPI.AddPokeCanvasInteraction(child.gameObject);

            var module = UnityEngine.Object.FindAnyObjectByType<PointableCanvasModule>();
            if (!module)
            {
                var es = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
                if (!es) es = new GameObject("Pointable Canvas Module", typeof(EventSystem)).GetComponent<EventSystem>();
                module = Undo.AddComponent<PointableCanvasModule>(es.gameObject);
            }
            module.ExclusiveMode = true;
            RequireCanvas<RayInteractable>(child, canvas);
            RequireCanvas<PokeInteractable>(child, canvas);
            EditorUtility.SetDirty(module);
            EditorUtility.SetDirty(canvas);
            EditorSceneManager.MarkSceneDirty(gameRoot.gameObject.scene);
        }

        private static void RequireCanvas<T>(Transform root, Canvas canvas) where T : Component
        {
            var surfaces = root.GetComponentsInChildren<T>(true);
            if (surfaces.Length != 1 || !surfaces[0].TryGetComponent<PointableCanvas>(out var pointable) || pointable.Canvas != canvas)
                throw new InvalidOperationException($"The Meta menu needs one {typeof(T).Name} wired to its Canvas.");
        }
    }
}
