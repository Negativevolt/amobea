using UnityEditor;
using UnityEngine;

/// <summary>Import rules for Assets/Entities: legacy animation clips (simple to script), crisp PS1 textures.</summary>
class EntitiesImporter : AssetPostprocessor
{
    const string k_Root = "Assets/Entities/";

    public override uint GetVersion() => 3;

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(k_Root))
            return;
        var m = (ModelImporter)assetImporter;
        m.bakeAxisConversion = true;
        m.animationType = ModelImporterAnimationType.Legacy;
        m.importAnimation = true;
        m.importCameras = false;
        m.importLights = false;
        m.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        m.addCollider = false;
    }

    void OnPreprocessTexture()
    {
        if (assetPath.StartsWith("Assets/Game/Textures/"))
        {
            // hotel textures: chunky PS1 pixels, tiling
            var g = (TextureImporter)assetImporter;
            g.filterMode = FilterMode.Point;
            g.mipmapEnabled = false;
            g.textureCompression = TextureImporterCompression.Uncompressed;
            g.wrapMode = TextureWrapMode.Repeat;
            g.alphaIsTransparency = assetPath.Contains("decal") || assetPath.Contains("dust");
            return;
        }
        if (!assetPath.StartsWith(k_Root))
            return;
        var t = (TextureImporter)assetImporter;
        t.filterMode = FilterMode.Point;
        t.mipmapEnabled = false;
        t.textureCompression = TextureImporterCompression.Uncompressed;
    }
}
