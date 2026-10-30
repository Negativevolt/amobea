using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Unity's default font material draws 3D text (TextMesh) on top of everything in URP, so the keypad digits, its
/// display, the TV code and the signs showed through the player's hands and through walls. When a scene loads this
/// swaps that material for one that hides behind things in front of it (Resources/Shaders/GameWorldText).
/// Text with its own material (subtitles, inventory, credits: drawn on top on purpose) is left alone.
/// </summary>
public static class WorldText
{
    static readonly Dictionary<Font, Material> s_Materials = new Dictionary<Font, Material>();
    static Shader s_Shader;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_Materials.Clear();
        s_Copies.Clear();
        s_Shader = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Font.textureRebuilt -= OnFontTextureRebuilt;
        Font.textureRebuilt += OnFontTextureRebuilt;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (var text in Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Fix(text);
    }

    /// <summary>Makes this text hide behind things, if it still uses its font's default (always on top) material.</summary>
    public static void Fix(TextMesh text)
    {
        if (text == null || text.font == null)
            return;
        var r = text.GetComponent<MeshRenderer>();
        if (r == null || r.sharedMaterial != text.font.material)
            return;
        var m = MaterialFor(text.font);
        if (m != null)
            r.sharedMaterial = m;
    }

    /// <summary>Shared depth-tested material for 3D text in this font (null if the shader is missing).</summary>
    public static Material MaterialFor(Font font)
    {
        if (font == null)
            return null;
        if (s_Materials.TryGetValue(font, out var m) && m != null)
            return m;
        if (s_Shader == null)
            s_Shader = Resources.Load<Shader>("Shaders/GameWorldText");
        if (s_Shader == null)
        {
            Debug.LogWarning("[WorldText] Resources/Shaders/GameWorldText is missing; 3D text stays on top of everything.");
            return null;
        }
        m = new Material(s_Shader) { name = font.name + " (World Text)" };
        if (font.material != null)
            m.mainTexture = font.material.mainTexture;
        s_Materials[font] = m;
        return m;
    }

    /// <summary>
    /// Depth-tested text material of its own, drawn in render queue <paramref name="queue"/> (e.g. a shadow copy that
    /// must draw before the text on top of it). Null if the shader is missing.
    /// </summary>
    public static Material MaterialFor(Font font, int queue)
    {
        var shared = MaterialFor(font);
        if (shared == null)
            return null;
        var m = new Material(shared) { renderQueue = queue };
        s_Copies.Add(new KeyValuePair<Font, Material>(font, m));
        return m;
    }

    static readonly List<KeyValuePair<Font, Material>> s_Copies = new List<KeyValuePair<Font, Material>>();

    // Dynamic fonts re-render their glyph texture when they need more characters; keep the copies pointing at it.
    static void OnFontTextureRebuilt(Font font)
    {
        if (font == null || font.material == null)
            return;
        if (s_Materials.TryGetValue(font, out var m) && m != null)
            m.mainTexture = font.material.mainTexture;
        foreach (var copy in s_Copies)
            if (copy.Key == font && copy.Value != null)
                copy.Value.mainTexture = font.material.mainTexture;
    }
}
