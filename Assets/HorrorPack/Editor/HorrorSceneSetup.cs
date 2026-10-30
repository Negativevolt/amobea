using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Tools > Horror Pack menu items that set up the open scene:
///  - Add Flashlight: a grabbable, working flashlight (trigger / F toggles it).
///  - Add Horror Atmosphere: darkness, fog, moonlight, film grain, vignette and chunky low-res pixels.
/// Both can be undone with Ctrl+Z.
/// </summary>
static class HorrorSceneSetup
{
    const string k_Root = "Assets/HorrorPack/";
    const string k_FlashlightModel = k_Root + "Models/Flashlight02/Flashlight02.fbx";
    const string k_Cookie = k_Root + "Lighting/flashlight_cookie.png";
    const string k_Profile = k_Root + "Lighting/HorrorVolume.asset";

    // ------------------------------------------------------------------ flashlight
    [MenuItem("Tools/Horror Pack/Add Torch")]
    public static GameObject AddFlashlight()
    {
        var root = new GameObject("Torch");
        var body = TorchModel.Create();
        body.name = "Model";
        body.transform.SetParent(root.transform, false);

        // Rigidbodies need convex colliders: swap the imported mesh colliders for one box.
        foreach (var mc in body.GetComponentsInChildren<MeshCollider>())
            Object.DestroyImmediate(mc);

        var renderers = body.GetComponentsInChildren<Renderer>();
        var bounds = renderers[0].bounds;
        foreach (var r in renderers)
            bounds.Encapsulate(r.bounds);

        // The beam points from the middle of the torch out through the glass lens.
        var lens = renderers.FirstOrDefault(r => r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("glass"));
        var lensCenter = lens != null ? lens.bounds.center : bounds.center + Vector3.forward * bounds.extents.z;
        var dir = (lensCenter - bounds.center).normalized;
        if (dir.sqrMagnitude < 0.5f)
            dir = Vector3.forward;

        // Re-centre the model so the root sits in the middle of the torch, beam along +Z.
        body.transform.position -= bounds.center;
        body.transform.rotation = Quaternion.FromToRotation(dir, Vector3.forward) * body.transform.rotation;
        body.transform.position = Quaternion.FromToRotation(dir, Vector3.forward) * body.transform.position;
        lensCenter = Quaternion.FromToRotation(dir, Vector3.forward) * (lensCenter - bounds.center);

        renderers = body.GetComponentsInChildren<Renderer>();
        var local = new Bounds(renderers[0].bounds.center, Vector3.zero);
        foreach (var r in renderers)
            local.Encapsulate(r.bounds);

        var box = root.AddComponent<BoxCollider>();
        box.center = local.center;
        box.size = local.size;

        // Beam
        var beamGo = new GameObject("Beam");
        beamGo.transform.SetParent(root.transform, false);
        beamGo.transform.localPosition = new Vector3(0f, 0f, local.max.z + 0.01f);
        var beam = beamGo.AddComponent<Light>();
        beam.type = LightType.Spot;
        beam.range = 18f;
        beam.spotAngle = 48f;
        beam.innerSpotAngle = 20f;
        beam.intensity = 5f;
        beam.color = new Color(1f, 0.8f, 0.45f); // warm, yellowish old-bulb light
        beam.shadows = LightShadows.Hard;
        beam.shadowStrength = 0.9f;
        beam.cookie = AssetDatabase.LoadAssetAtPath<Texture>(k_Cookie);

        // Hold it by the handle, beam pointing where the controller points
        var grip = new GameObject("Grip Point");
        grip.transform.SetParent(root.transform, false);
        grip.transform.localPosition = new Vector3(0f, 0f, local.min.z + local.size.z * 0.3f);

        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 0.2f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var grab = root.AddComponent<XRGrabInteractable>();
        grab.movementType = XRBaseInteractable.MovementType.Instantaneous; // steady beam while holding
        grab.attachTransform = grip.transform;
        grab.useDynamicAttach = false;
        grab.throwOnDetach = true;

        var torch = root.AddComponent<PSXFlashlight>();
        torch.beam = beam;

        Place(root.transform);

        Undo.RegisterCreatedObjectUndo(root, "Add Torch");
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        return root;
    }

    /// <summary>On the playground table if there is one, otherwise just in front of the player.</summary>
    static void Place(Transform t)
    {
        var playground = Object.FindAnyObjectByType<DestructionPlayground>();
        if (playground != null)
        {
            var p = playground.transform;
            t.SetPositionAndRotation(p.TransformPoint(new Vector3(-0.15f, 0.82f, -0.18f)), p.rotation * Quaternion.Euler(0f, 90f, 0f));
            return;
        }
        var origin = Object.FindAnyObjectByType<XROrigin>();
        var basePose = origin != null ? origin.transform : null;
        var pos = basePose != null ? basePose.position + basePose.forward * 0.8f : new Vector3(0f, 0f, 0.8f);
        t.SetPositionAndRotation(pos + Vector3.up * 1.0f, basePose != null ? basePose.rotation : Quaternion.identity);
    }

    // ------------------------------------------------------------------ atmosphere
    [MenuItem("Tools/Horror Pack/Add Horror Atmosphere")]
    public static void AddAtmosphere()
    {
        var scene = EditorSceneManager.GetActiveScene();

        // Post-processing profile
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(k_Profile);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, k_Profile);

            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.75f);
            grain.response.Override(0.5f);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.42f);
            vignette.smoothness.Override(0.45f);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(-0.1f);
            color.contrast.Override(18f);
            color.saturation.Override(-30f);
            color.colorFilter.Override(new Color(0.92f, 0.97f, 0.9f));

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.35f);
            bloom.threshold.Override(1.1f);

            foreach (var c in profile.components)
                AssetDatabase.AddObjectToAsset(c, profile);
            AssetDatabase.SaveAssets();
        }

        var existing = GameObject.Find("Horror Atmosphere");
        if (existing != null)
            Undo.DestroyObjectImmediate(existing);

        var go = new GameObject("Horror Atmosphere");
        var volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10f;
        volume.sharedProfile = profile;
        go.AddComponent<PSXLook>();
        Undo.RegisterCreatedObjectUndo(go, "Add Horror Atmosphere");

        // Darkness and fog
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.03f, 0.03f, 0.04f);
        RenderSettings.reflectionIntensity = 0.15f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.09f;
        RenderSettings.fogColor = new Color(0.015f, 0.015f, 0.02f);

        // Moonlight instead of daylight
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type != LightType.Directional)
                continue;
            Undo.RecordObject(light, "Horror Atmosphere");
            light.intensity = 0.06f;
            light.color = new Color(0.55f, 0.62f, 0.85f);
        }

        // Cameras: black background, post-processing on
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            Undo.RecordObject(cam, "Horror Atmosphere");
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var data = cam.GetUniversalAdditionalCameraData();
            Undo.RecordObject(data, "Horror Atmosphere");
            data.renderPostProcessing = true;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[Horror Pack] Horror atmosphere added. Delete the 'Horror Atmosphere' object (or Ctrl+Z) to remove the grain/pixel look.");
    }
}
