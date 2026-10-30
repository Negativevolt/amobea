using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Entry points for running Unity without its window (batch mode), e.g.
///   Unity.exe -batchmode -projectPath &lt;project&gt; -executeMethod BatchTools.BuildAll -quit
/// Builds the game into "Main VR", saves it, then renders preview pictures of each stage and monster.
/// </summary>
public static class BatchTools
{
    const string k_PreviewDir = @"C:\Users\VISHESH\Downloads\HorrorPack_src\_tools\previews";

    public static void BuildAll()
    {
        // These one-time helpers would repeat the work next time the editor opens: remove them first.
        AssetDatabase.DeleteAsset("Assets/Game/Editor/BuildGameOnce.cs");
        AssetDatabase.DeleteAsset("Assets/HorrorPack/Editor/FixTorchGlowOnce.cs");

        var scene = EditorSceneManager.OpenScene("Assets/Main VR.unity");

        // Torch fix: no backwards glow, beam straight out of the lens.
        foreach (var torch in Object.FindObjectsByType<PSXFlashlight>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (torch.glow != null)
            {
                Object.DestroyImmediate(torch.glow.gameObject);
                torch.glow = null;
            }
            if (torch.beam != null)
                torch.beam.transform.localRotation = Quaternion.identity;
            EditorUtility.SetDirty(torch);
        }

        GameBuilder.Build();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Batch] Game built and saved.");

        try { RenderPreviews(); }
        catch (System.Exception e) { Debug.LogError("[Batch] Preview render failed: " + e); }
    }

    /// <summary>Opens Main VR and enters Play mode; PlaytestDriver (started with -autoPlaytest) plays it and quits.</summary>
    public static void Playtest()
    {
        EditorSceneManager.OpenScene("Assets/Main VR.unity");
        SessionState.SetBool(k_Pending, true);
        SessionState.SetFloat(k_Since, (float)EditorApplication.timeSinceStartup);
        ArmPlaytest();
    }

    const string k_Pending = "BatchTools.PlaytestPending";
    const string k_Since = "BatchTools.PlaytestSince";

    // Survives the script reload OpenXR causes on startup: re-arms itself after every domain reload.
    [InitializeOnLoadMethod]
    static void ReArm()
    {
        if (SessionState.GetBool(k_Pending, false))
            ArmPlaytest();
    }

    static void ArmPlaytest()
    {
        var quietSince = -1.0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (!SessionState.GetBool(k_Pending, false)) { EditorApplication.update -= wait; return; }
            var now = EditorApplication.timeSinceStartup;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { quietSince = -1.0; return; }
            if (quietSince < 0) quietSince = now;
            if (now - SessionState.GetFloat(k_Since, 0f) > 25.0 && now - quietSince > 10.0)
            {
                EditorApplication.update -= wait;
                SessionState.SetBool(k_Pending, false);
                if (EditorSceneManager.GetActiveScene().name != "Main VR")
                    EditorSceneManager.OpenScene("Assets/Main VR.unity");
                Debug.Log("[Batch] Editor settled, entering Play mode for the playtest.");
                EditorApplication.EnterPlaymode();
            }
        };
        EditorApplication.update += wait;
    }

    /// <summary>Fixes monster facing in the saved scene, saves, then re-renders the previews.</summary>
    public static void FixFacingAndPreview()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Main VR.unity");
        foreach (var e in Object.FindObjectsByType<EntityBase>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (e.transform.Find("Model") != null)
                Debug.Log("[Batch] " + GameBuilder.FixFacing(e));
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        try { RenderPreviews(); }
        catch (System.Exception ex) { Debug.LogError("[Batch] Preview render failed: " + ex); }
    }

    // ------------------------------------------------------------------ previews (not saved into the scene)
    static void RenderPreviews()
    {
        Directory.CreateDirectory(k_PreviewDir);
        // Bright, fog-free, ceiling-free views so the layout can be checked.
        RenderSettings.fog = false;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.75f, 0.75f, 0.75f);
        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            v.enabled = false;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (r.name.StartsWith("Ceiling") || r.transform.root.name.StartsWith("Ceiling") || (r.transform.parent != null && r.transform.parent.name.StartsWith("Ceiling")))
                r.enabled = false;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name.StartsWith("Ceiling1Unit"))
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                    r.enabled = false;

        var camGo = new GameObject("Preview Camera");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
        cam.fieldOfView = 55f;
        cam.farClipPlane = 200f;
        var sun = new GameObject("Preview Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.1f;
        sun.transform.rotation = Quaternion.Euler(55f, 30f, 0f);

        foreach (var stageName in new[] { "Stage 1 - Apartment", "Stage 2 - Basement", "Stage 3 - Nursery" })
        {
            var stage = GameObject.Find(stageName);
            if (stage == null) continue;
            var b = Bounds(stage);
            cam.transform.position = b.center + new Vector3(-b.extents.x * 0.9f, b.extents.magnitude * 1.1f + 4f, -b.extents.z * 1.1f - 3f);
            cam.transform.LookAt(b.center);
            Shot(cam, stageName.Split(' ')[1] + "_overview");
        }

        // Each monster from the front (checks it faces the right way) and its surroundings.
        foreach (var e in Object.FindObjectsByType<EntityBase>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            foreach (var r in e.GetComponentsInChildren<Renderer>(true))
                r.enabled = true;
            var b = Bounds(e.gameObject);
            var front = e.transform.forward;
            cam.transform.position = b.center + front * (b.extents.magnitude * 2.2f + 0.8f) + Vector3.up * 0.3f;
            cam.transform.LookAt(b.center);
            Shot(cam, "entity_" + e.name);
        }
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(sun.gameObject);
        Debug.Log("[Batch] Previews written to " + k_PreviewDir);
    }

    static void Shot(Camera cam, string name)
    {
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        File.WriteAllBytes(Path.Combine(k_PreviewDir, name + ".png"), tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}
