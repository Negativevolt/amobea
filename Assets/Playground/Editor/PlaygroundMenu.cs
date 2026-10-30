using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Destruction Playground menu: puts the playground props into the open scene as normal,
/// saved objects so you can see and arrange them in the editor.
/// </summary>
static class PlaygroundMenu
{
    const float k_Distance = 1.4f;

    [MenuItem("Tools/Destruction Playground/Add To Scene")]
    static void AddToScene()
    {
        var existing = Object.FindAnyObjectByType<DestructionPlayground>(FindObjectsInactive.Include);
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("Destruction Playground",
                    "This scene already has a Destruction Playground. Replace it with a fresh one?\n\n" +
                    "Anything you moved inside it will be reset.", "Replace", "Cancel"))
                return;
            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        // Stand it in front of the XR rig (or the world origin if there is no rig).
        var origin = Object.FindAnyObjectByType<XROrigin>();
        var basePos = origin != null ? origin.transform.position : Vector3.zero;
        var forward = origin != null ? Vector3.ProjectOnPlane(origin.transform.forward, Vector3.up) : Vector3.forward;
        if (forward.sqrMagnitude < 1e-4f)
            forward = Vector3.forward;
        forward.Normalize();

        // Stand it on the same floor height as the XR rig.
        var spot = basePos + forward * k_Distance;

        var go = new GameObject("Destruction Playground");
        go.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(forward, Vector3.up));
        var playground = go.AddComponent<DestructionPlayground>();
        playground.buildOnStart = false; // props are saved in the scene, don't generate more at runtime
        playground.Build();

        Undo.RegisterCreatedObjectUndo(go, "Add Destruction Playground");
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        SceneView.lastActiveSceneView?.FrameSelected();
        Debug.Log("[Destruction Playground] Added to the scene. Move the 'Destruction Playground' object to move it all, " +
                  "or move single props inside 'Props'. Save the scene (Ctrl+S) to keep it.");
    }

    [MenuItem("Tools/Destruction Playground/Remove From Scene")]
    static void RemoveFromScene()
    {
        var existing = Object.FindAnyObjectByType<DestructionPlayground>(FindObjectsInactive.Include);
        if (existing == null)
        {
            EditorUtility.DisplayDialog("Destruction Playground", "There is no Destruction Playground in this scene.", "OK");
            return;
        }
        var scene = existing.gameObject.scene;
        Undo.DestroyObjectImmediate(existing.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
    }
}
