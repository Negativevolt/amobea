using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Horror Game > Bake Navigation: builds the monsters' walkable areas once in the editor and saves them
/// (Assets/Game/NavMesh), so pressing Play doesn't rebuild them every time. Runs again by itself after Build Game.
/// </summary>
public static class NavBake
{
    const string k_Dir = "Assets/Game/NavMesh";

    [MenuItem("Tools/Horror Game/Bake Navigation")]
    public static void Menu() => Debug.Log("[Horror Game] " + BakeAll());

    public static string BakeAll()
    {
        var flow = Object.FindAnyObjectByType<GameFlow>(FindObjectsInactive.Include);
        if (flow == null || flow.navMeshes == null)
            return "No Game Flow / navigation surfaces in the scene.";
        if (!AssetDatabase.IsValidFolder(k_Dir))
            AssetDatabase.CreateFolder("Assets/Game", "NavMesh");

        // every stage has to be switched on while its colliders are collected
        var was = new List<(GameObject, bool)>();
        foreach (var r in flow.stageRoots.Where(r => r != null))
        {
            was.Add((r, r.activeSelf));
            r.SetActive(true);
        }
        var log = new System.Text.StringBuilder("Navigation baked:");
        try
        {
            foreach (var s in flow.navMeshes.Where(s => s != null))
            {
                var path = k_Dir + "/" + s.gameObject.scene.name + " - " + s.name + ".asset";
                s.RemoveData();
                s.navMeshData = null;
                s.BuildNavMesh();
                var data = s.navMeshData;
                if (data == null) { log.Append(" " + s.name + " FAILED;"); continue; }
                AssetDatabase.DeleteAsset(path);
                data.name = s.name;
                AssetDatabase.CreateAsset(data, path);
                s.navMeshData = AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(path);
                EditorUtility.SetDirty(s);
                log.Append(" " + s.name + " -> " + path + ";");
            }
        }
        finally
        {
            foreach (var (go, active) in was)
                go.SetActive(active);
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(flow.gameObject.scene);
        EditorSceneManager.SaveScene(flow.gameObject.scene);
        return log.ToString();
    }
}
