using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Displays the player's inventory on the left side of their view as a WorldSpace TextMesh.
/// Works identically in VR (parented to the head) and in desktop mode (drawn by OnGUI).
/// Refreshes every 0.5 s; no UI Canvas required. Auto-creates itself on scene load.
/// </summary>
public class InventoryDisplay : MonoBehaviour
{
    [Tooltip("Horizontal offset from camera centre – negative = left side of view.")]
    public float xOffset = -0.41f;
    [Tooltip("Vertical offset – positive = above eye level.")]
    public float yOffset = 0.16f;
    [Tooltip("Distance in front of the eyes (metres).")]
    public float depth = 0.9f;
    
    public float refreshRate = 0.5f;

    static InventoryDisplay s_Instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => s_Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (s_Instance != null) return;
        var go = new GameObject("Inventory Display");
        DontDestroyOnLoad(go);
        s_Instance = go.AddComponent<InventoryDisplay>();
    }

    TextMesh m_Text;
    TextMesh m_Shadow;
    string m_Content = "";
    GUIStyle m_Style;

    void Awake()
    {
        if (s_Instance != null && s_Instance != this) { Destroy(gameObject); return; }
        s_Instance = this;
        InvokeRepeating(nameof(Refresh), 0.5f, refreshRate);
    }

    // ------------------------------------------------------------------ setup
    void EnsureTextMesh()
    {
        var head = PlayerRig.Head;
        if (head == null || m_Text != null)
            return;

        // Pivot follows the head.
        var pivot = new GameObject("Inventory Pivot").transform;
        pivot.SetParent(head, false);
        pivot.localPosition = new Vector3(xOffset, yOffset, depth);

        var go = new GameObject("Inventory Text");
        go.transform.SetParent(pivot, false);
        go.transform.localPosition = Vector3.zero;

        m_Text = go.AddComponent<TextMesh>();
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        m_Text.font = font;
        // Depth-tested so your hands cover it (it floats at hand height); it can hide behind a wall you stand right against.
        go.GetComponent<MeshRenderer>().sharedMaterial = WorldText.MaterialFor(font, 3110) ?? Narrator.OverlayTextMaterial(font, 3110);
        m_Text.fontSize   = 80;
        m_Text.characterSize = 0.005f * depth / 0.75f;
        m_Text.anchor    = TextAnchor.UpperLeft;
        m_Text.alignment = TextAlignment.Left;
        m_Text.color     = new Color(0.9f, 0.85f, 0.65f);

        // Dark shadow copy for readability against bright walls / film grain.
        var shadowGo = Instantiate(go, pivot);
        shadowGo.name = "Inventory Shadow";
        shadowGo.transform.localPosition = new Vector3(0.002f, -0.002f, 0.002f) * (depth / 0.75f);
        shadowGo.GetComponent<MeshRenderer>().sharedMaterial = WorldText.MaterialFor(font, 3105) ?? Narrator.OverlayTextMaterial(font, 3105);
        m_Shadow = shadowGo.GetComponent<TextMesh>();
        m_Shadow.color = new Color(0f, 0f, 0f, 0.75f);

        // Desktop mode: the TextMesh would be at screen-position; hide it and use OnGUI instead.
        if (PlayerRig.DesktopMode)
        {
            go.SetActive(false);
            shadowGo.SetActive(false);
        }
    }

    void SetContent(string text)
    {
        m_Content = text ?? "";
        if (m_Text != null)   m_Text.text   = m_Content;
        if (m_Shadow != null) m_Shadow.text = m_Content;
    }

    // ------------------------------------------------------------------ desktop fallback
    void OnGUI()
    {
        if (!PlayerRig.DesktopMode || string.IsNullOrEmpty(m_Content))
            return;
        if (m_Style == null)
            m_Style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                wordWrap  = false,
                fontSize  = Mathf.Max(14, Screen.height / 40),
            };
        m_Style.normal.textColor = new Color(0.9f, 0.85f, 0.65f);
        var w = Screen.width * 0.22f; // wide enough for the objective line
        var r = new Rect(8f, 8f, w, Screen.height * 0.5f);
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.Box(r, GUIContent.none);
        GUI.color = Color.white;
        GUI.Label(new Rect(r.x + 6f, r.y + 6f, r.width - 6f, r.height - 6f), m_Content, m_Style);
    }

    // ------------------------------------------------------------------ refresh
    public void Refresh()
    {
        EnsureTextMesh();

        var flow  = GameFlow.Instance;
        // Snapshot the set to avoid enumerating a live reference across frames.
        var items = flow != null ? new List<string>(flow.Items) : null;

        // Strip out consumed/flag items that should no longer appear in the inventory.
        if (items != null)
            items.RemoveAll(id => !ShouldShow(id, items));

        // Count by display name.
        var counts = new Dictionary<string, int>();
        if (items != null)
        {
            foreach (var id in items)
            {
                var dn = DisplayName(id);
                counts.TryGetValue(dn, out var n);
                counts[dn] = n + 1;
            }
        }

        var sb = new StringBuilder();

        // what to do next, first (see Objectives)
        var objective = Objectives.Current;
        if (!string.IsNullOrEmpty(objective))
        {
            sb.AppendLine("> " + Wrap(objective, 30).Replace("\n", "\n  "));
            sb.AppendLine();
        }

        if (counts.Count == 0)
        {
            sb.AppendLine("─ empty ─");
        }
        else
        {
            foreach (var kv in counts)
                sb.AppendLine($"{kv.Value}x {kv.Key}");
        }

        // Torch on its own line.
        var torch = FindAnyObjectByType<PSXFlashlight>(FindObjectsInactive.Include);
        if (torch != null)
            sb.Append(torch.IsOn ? "1x torch (on)" : "1x torch (off)");
        else
            sb.Append("no torch");

        SetContent(sb.ToString());
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Returns false for items that are internal flags (never physical) or that have been
    /// consumed/used — e.g. tape inserted into VCR, fuse slotted in.
    /// </summary>
    static bool ShouldShow(string id, List<string> snapshot)
    {
        // Pure flags — never a physical item in the player's hand.
        if (id == "code_seen")    return false; // the code was read, not carried
        if (id == "tape_watched") return false; // tape is done
        if (id == "power")        return false; // fuse box is fixed
        if (id == "torch" || id == "won") return false; // the torch has its own line; "won" is a flag
        // tape_305 was inserted into the VCR once code_seen is set.
        if (id == "tape_305"      && snapshot.Contains("code_seen"))    return false;
        // vhs was played once tape_watched is set.
        if (id == "vhs"           && snapshot.Contains("tape_watched")) return false;
        return true;
    }

    // Breaks text into lines of at most maxWidth characters, at spaces.
    internal static string Wrap(string text, int maxWidth)
    {
        var sb = new StringBuilder();
        var lineLen = 0;
        foreach (var word in text.Split(' '))
        {
            if (lineLen > 0 && lineLen + 1 + word.Length > maxWidth) { sb.Append('\n'); lineLen = 0; }
            else if (lineLen > 0) { sb.Append(' '); lineLen++; }
            sb.Append(word);
            lineLen += word.Length;
        }
        return sb.ToString();
    }

    static string DisplayName(string id)
    {
        if (id.StartsWith("battery"))             return "battery";
        if (id.StartsWith("tape") || id == "vhs") return "tape";
        return id.Replace("_", " ");
    }

    void OnDestroy() => CancelInvoke(nameof(Refresh));
}
