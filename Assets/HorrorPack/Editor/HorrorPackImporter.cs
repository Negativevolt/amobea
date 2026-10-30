using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Import rules for Assets/HorrorPack:
///  - Textures: point filtering, no compression, no mipmaps (crisp PS1 look).
///  - Models: correct axes from Blender, no cameras/lights/animation, and every material is
///    swapped for ONE shared URP material per texture in Assets/HorrorPack/Materials
///    (e.g. all wood_dark parts share Materials/wood_dark.mat). Edit those to restyle everything.
/// </summary>
class HorrorPackImporter : AssetPostprocessor
{
    const string k_Root = "Assets/HorrorPack/";
    const string k_Textures = k_Root + "Textures/";
    const string k_Materials = k_Root + "Materials/";

    public override uint GetVersion() => 5;

    // ------------------------------------------------------------- textures
    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(k_Root))
            return;
        var t = (TextureImporter)assetImporter;
        t.filterMode = FilterMode.Point;
        t.textureCompression = TextureImporterCompression.Uncompressed;
        t.mipmapEnabled = false;
        t.wrapMode = TextureWrapMode.Repeat;
        t.maxTextureSize = 256;

        if (assetPath.Contains("/Lighting/"))
        {
            // Light cookies (e.g. the flashlight beam pattern).
            t.textureType = TextureImporterType.Cookie;
            var settings = new TextureImporterSettings();
            t.ReadTextureSettings(settings);
            settings.alphaSource = TextureImporterAlphaSource.FromGrayScale;
            settings.wrapMode = TextureWrapMode.Clamp;
            settings.filterMode = FilterMode.Point;
            settings.mipmapEnabled = false;
            t.SetTextureSettings(settings);
        }
    }

    // ------------------------------------------------------------- models
    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(k_Root))
            return;
        var m = (ModelImporter)assetImporter;
        m.bakeAxisConversion = true;
        m.importCameras = false;
        m.importLights = false;
        m.importAnimation = false;
        m.animationType = ModelImporterAnimationType.None;
        m.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        m.materialLocation = ModelImporterMaterialLocation.InPrefab;
        m.addCollider = true; // walls, floors and furniture block the player and accept teleports
        m.isReadable = true; // needed for MeshColliders and the smash effect
    }

    Material OnAssignMaterialModel(Material source, Renderer renderer)
    {
        if (!assetPath.StartsWith(k_Root))
            return null;
        var name = CleanName(source.name);
        if (string.IsNullOrEmpty(name))
            return null;
        // Only LOAD here; materials are created outside the import by HorrorPackTools.EnsureMaterials.
        return AssetDatabase.LoadAssetAtPath<Material>(k_Materials + name + ".mat");
    }

    static string CleanName(string n)
    {
        var dot = n.IndexOf('.');
        if (dot > 0)
            n = n.Substring(0, dot);
        return n.Replace(" (Instance)", "").Trim();
    }

    // ------------------------------------------------------------- shared materials
    internal static Material SharedMaterial(string name)
    {
        var path = k_Materials + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null)
            return mat;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        mat = new Material(shader) { name = name };

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(k_Textures + name + ".png");
        if (tex != null)
        {
            mat.SetTexture("_BaseMap", tex);
            mat.SetTexture("_MainTex", tex);
        }
        mat.SetColor("_BaseColor", Color.white);

        Configure(mat, name);
        Directory.CreateDirectory(k_Materials);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static void Configure(Material mat, string name)
    {
        float metallic = 0f, smoothness = 0.15f, alpha = 1f, emission = 0f;
        switch (name)
        {
            case "metal_steel": metallic = 0.8f; smoothness = 0.55f; break;
            case "metal_dark": metallic = 0.7f; smoothness = 0.5f; break;
            case "brass": metallic = 0.8f; smoothness = 0.5f; break;
            case "can": metallic = 0.6f; smoothness = 0.5f; break;
            case "metal_rusty": metallic = 0.4f; smoothness = 0.2f; break;
            case "metal_green": metallic = 0.3f; smoothness = 0.3f; break;
            case "ceramic":
            case "ceramic_blue":
            case "trashbag": smoothness = 0.65f; break;
            case "glass": smoothness = 0.85f; alpha = 0.45f; break;
            case "glass_green": smoothness = 0.85f; alpha = 0.8f; break;
            case "bulb": smoothness = 0.7f; emission = 0.6f; break;
            case "crt": smoothness = 0.8f; emission = 0.15f; break;
        }
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);

        if (alpha < 1f)
        {
            mat.SetFloat("_Surface", 1f); // transparent
            mat.SetFloat("_Blend", 0f);   // alpha blend
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetColor("_BaseColor", new Color(1f, 1f, 1f, alpha));
        }

        if (emission > 0f)
        {
            var tex = mat.GetTexture("_BaseMap");
            mat.SetTexture("_EmissionMap", tex);
            mat.SetColor("_EmissionColor", Color.white * emission);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
    }
}
