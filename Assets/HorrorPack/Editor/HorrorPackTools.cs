using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Horror Pack menu.
///  - Rebuild Materials: makes sure every texture in HorrorPack/Textures has a shared material.
///  - Build Showcase Scene: lays every model out in a grid in HorrorPack/HorrorPack_Showcase.unity,
///    a catalog you can open and copy pieces from.
/// Materials are also created automatically whenever the editor reloads, so models always find them.
/// </summary>
[InitializeOnLoad]
static class HorrorPackTools
{
    const string k_Root = "Assets/HorrorPack/";
    const string k_ShowcasePath = k_Root + "HorrorPack_Showcase.unity";

    static HorrorPackTools()
    {
        EditorApplication.delayCall += () =>
        {
            if (EnsureMaterials() > 0)
                AssetDatabase.ImportAsset(k_Root + "Models", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        };
    }

    /// <summary>Creates any missing shared materials. Returns how many were created.</summary>
    static int EnsureMaterials()
    {
        if (!AssetDatabase.IsValidFolder(k_Root + "Textures"))
            return 0;
        var created = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { k_Root + "Textures" }))
        {
            var name = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
            if (AssetDatabase.LoadAssetAtPath<Material>(k_Root + "Materials/" + name + ".mat") != null)
                continue;
            HorrorPackImporter.SharedMaterial(name);
            created++;
        }
        if (created > 0)
            AssetDatabase.SaveAssets();
        return created;
    }

    [MenuItem("Tools/Horror Pack/Rebuild Materials")]
    static void RebuildMaterials()
    {
        var n = EnsureMaterials();
        AssetDatabase.ImportAsset(k_Root + "Models", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        Debug.Log($"[Horror Pack] {n} material(s) created; models reimported.");
    }

    [MenuItem("Tools/Horror Pack/Build Showcase Scene")]
    public static void BuildShowcase()
    {
        EnsureMaterials();
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        var previous = EditorSceneManager.GetActiveScene().path;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var paths = AssetDatabase.FindAssets("t:Model", new[] { k_Root + "Models" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(p => p)
            .ToList();

        var root = new GameObject("Horror Pack (all models)").transform;
        const float spacing = 0.5f, rowWidth = 14f;
        float x = 0f, z = 0f, rowDepth = 0f;
        foreach (var path in paths)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
                continue;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
            var b = Bounds(go);
            if (x > 0f && x + b.size.x > rowWidth)
            {
                x = 0f;
                z -= rowDepth + spacing;
                rowDepth = 0f;
            }
            // put the model's bounding box min corner at (x, 0, z - depth)
            go.transform.position += new Vector3(x - b.min.x, -b.min.y, z - b.max.z);
            x += b.size.x + spacing;
            rowDepth = Mathf.Max(rowDepth, b.size.z);
        }

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.position = new Vector3(rowWidth / 2f, -0.001f, z / 2f - 1f);
        floor.transform.localScale = new Vector3(rowWidth / 10f + 0.4f, 1f, -z / 10f + 0.6f);
        floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(k_Root + "Materials/floor_tile.mat");

        EditorSceneManager.SaveScene(scene, k_ShowcasePath);
        Debug.Log($"[Horror Pack] Showcase scene with {paths.Count} models saved to {k_ShowcasePath}.");

        if (!string.IsNullOrEmpty(previous))
            EditorSceneManager.OpenScene(previous);
    }

    static Bounds Bounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(go.transform.position, Vector3.one * 0.1f);
        var b = renderers[0].bounds;
        foreach (var r in renderers)
            b.Encapsulate(r.bounds);
        return b;
    }
}
