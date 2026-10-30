using UnityEngine;

/// <summary>
/// What to do next, always on screen: playtesters tuned the narrator out and then didn't know where to go.
/// Worked out from the game state (stage + items), never from what was said. The current objective sits in the
/// inventory panel; when it changes it flashes large in the middle of your view with a soft beep, and if you're
/// stuck on it for <see cref="remindAfter"/> seconds it flashes again. Works in VR and PC mode. Auto-creates itself.
/// </summary>
public class Objectives : MonoBehaviour
{
    [Tooltip("Seconds stuck on the same objective before it's shown again.")]
    public float remindAfter = 90f;
    [Tooltip("How long the big objective banner stays up (seconds).")]
    public float bannerSeconds = 5f;

    static Objectives s_Instance;

    /// <summary>The current objective ("" when there's nothing to do, e.g. after the ending).</summary>
    public static string Current => s_Instance != null ? s_Instance.m_Current : "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => s_Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (s_Instance != null || FindAnyObjectByType<GameFlow>() == null)
            return;
        var go = new GameObject("Objectives");
        DontDestroyOnLoad(go);
        s_Instance = go.AddComponent<Objectives>();
    }

    string m_Current = "";
    float m_Since, m_BannerUntil;
    TextMesh m_Banner, m_BannerShadow;
    GUIStyle m_Style, m_HeadStyle;

    void Awake()
    {
        if (s_Instance != null && s_Instance != this) { Destroy(gameObject); return; }
        s_Instance = this;
    }

    // ------------------------------------------------------------------ the objectives
    // Each stage is a list of steps in story order, each with "is it done?". The objective is the step after the
    // furthest one that's done, so steps you skipped or did out of order never hold you back (take the tape before
    // the torch, and it's straight on to the VCR). Items stay in GameFlow even after they're used up, so a step
    // never "undoes" itself.
    struct Step
    {
        public System.Func<bool> done;
        public System.Func<string> text;
        public Step(System.Func<bool> done, System.Func<string> text) { this.done = done; this.text = text; }
    }

    static bool Has(string id) => GameFlow.HasItem(id);

    static int Batteries()
    {
        var n = 0;
        foreach (var b in new[] { "battery_1", "battery_2", "battery_3" })
            if (Has(b)) n++;
        return n;
    }

    static readonly Step[][] k_Stages =
    {
        new[] // stage 1: the hotel
        {
            new Step(() => Has("torch"), () => "Take the torch from the table by the lift."),
            new Step(() => Has("tape_305"), () => "Find room 305 and the labelled tape inside it."),
            new Step(() => Has("code_seen"), () => "Play the tape in the VCR in room 305."),
            new Step(() => false, () => "Enter 0317 on the keypad by the stairwell door, at the end of the hall."),
        },
        new[] // stage 2: the basement
        {
            new Step(() => Batteries() >= 3, () => "Find the batteries for the fuse box (" + Batteries() + "/3)."),
            new Step(() => Has("power"), () => "Put the batteries in the fuse box by the tunnel door."),
            new Step(() => false, () => "Go through the tunnel door."),
        },
        new[] // stage 3: the train
        {
            new Step(() => Has("vhs"), () => "Look at the teddy bear in the third compartment."),
            new Step(() => Has("tape_watched"), () => "Play the tape on the TV in the third compartment."),
            new Step(() => false, () => "Get to the front door of the carriage. Don't turn around."),
        },
    };

    static string Work()
    {
        var flow = GameFlow.Instance;
        if (flow == null || Has("won") || flow.CurrentStage < 1 || flow.CurrentStage > k_Stages.Length)
            return "";
        var steps = k_Stages[flow.CurrentStage - 1];
        var furthest = -1;
        for (var i = 0; i < steps.Length; i++)
            if (steps[i].done())
                furthest = i;
        return steps[Mathf.Min(furthest + 1, steps.Length - 1)].text();
    }

    void Update()
    {
        var now = Work();
        if (now != m_Current)
        {
            m_Current = now;
            m_Since = Time.time;
            if (now.Length > 0)
                Flash();
        }
        else if (now.Length > 0 && Time.time - m_Since > remindAfter)
        {
            m_Since = Time.time;
            Flash();
        }
        UpdateBanner();
    }

    void Flash()
    {
        m_BannerUntil = Time.time + bannerSeconds;
        GameAudio.Flat("keypad_beep", 0.25f);
    }

    // ------------------------------------------------------------------ banner (VR: in front of you; PC: OnGUI)
    void UpdateBanner()
    {
        // never on top of a jump scare or a screen fade
        var show = Time.time < m_BannerUntil && !ScareDirector.Busy && m_Current.Length > 0;
        if (PlayerRig.DesktopMode)
            show = false;
        if (show && m_Banner == null)
            MakeBanner();
        if (m_Banner == null)
            return;
        if (m_Banner.gameObject.activeSelf != show)
            m_Banner.gameObject.SetActive(show);
        if (!show)
            return;
        var text = "<size=60>OBJECTIVE</size>\n" + InventoryDisplay.Wrap(m_Current, 28);
        if (m_Banner.text != text)
            m_Banner.text = m_BannerShadow.text = text;
        // fade out over the last second
        var a = Mathf.Clamp01(m_BannerUntil - Time.time);
        m_Banner.color = new Color(1f, 0.86f, 0.45f, a);
        m_BannerShadow.color = new Color(0f, 0f, 0f, 0.85f * a);
    }

    void MakeBanner()
    {
        var head = PlayerRig.Head;
        if (head == null)
            return;
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        // above everything else in view: the inventory panel hangs just above eye level, subtitles sit below it
        m_Banner = MakeText(head, font, new Vector3(0f, 0.4f, 1.3f), 4200);
        m_BannerShadow = MakeText(m_Banner.transform, font, new Vector3(0.003f, -0.003f, 0.002f), 4199);
        m_BannerShadow.transform.localScale = Vector3.one;
    }

    static TextMesh MakeText(Transform parent, Font font, Vector3 localPos, int queue)
    {
        var go = new GameObject("Objective Banner");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var tm = go.AddComponent<TextMesh>();
        tm.font = font;
        // a HUD banner: drawn on top of everything, like the subtitles
        go.GetComponent<MeshRenderer>().sharedMaterial = Narrator.OverlayTextMaterial(font, queue);
        tm.fontSize = 120;
        tm.characterSize = 0.0065f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.richText = true;
        return tm;
    }

    void OnGUI()
    {
        if (!PlayerRig.DesktopMode || m_Current.Length == 0 || Time.time >= m_BannerUntil || ScareDirector.Busy)
            return;
        if (m_Style == null)
        {
            m_Style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = Mathf.Max(18, Screen.height / 26) };
            m_HeadStyle = new GUIStyle(m_Style) { fontSize = Mathf.Max(12, Screen.height / 50) };
        }
        var a = Mathf.Clamp01(m_BannerUntil - Time.time);
        var w = Screen.width * 0.6f;
        var r = new Rect((Screen.width - w) / 2f, Screen.height * 0.16f, w, Screen.height * 0.16f);
        GUI.color = new Color(0f, 0f, 0f, 0.6f * a);
        GUI.Box(r, GUIContent.none);
        GUI.color = Color.white;
        m_HeadStyle.normal.textColor = new Color(1f, 0.86f, 0.45f, 0.8f * a);
        m_Style.normal.textColor = new Color(1f, 0.92f, 0.7f, a);
        GUI.Label(new Rect(r.x, r.y + 6f, r.width, r.height * 0.3f), "OBJECTIVE", m_HeadStyle);
        GUI.Label(new Rect(r.x + 10f, r.y + r.height * 0.28f, r.width - 20f, r.height * 0.7f), m_Current, m_Style);
    }
}
