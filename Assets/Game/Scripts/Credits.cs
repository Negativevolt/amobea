using UnityEngine;

/// <summary>End-of-game credits over the black screen. Head-locked text in VR, flat OnGUI text on a monitor.
/// A hard cut, like a new scene: the view goes black and silent at once and the whole screen is up in the same frame.</summary>
public class Credits : MonoBehaviour
{
    const string k_Title =
        "<size=340><b>THE END</b></size>\n" +
        "<size=150><i>Thanks for playing</i></size>\n" +
        "<size=170><b>VR AR MR</b></size>\n" +
        "<size=150><i>Presents</i></size>\n"+
        "<size=200><b>Haunted VR Escape</b></size>";

    const string k_Body =
        "<size=130>— — — — — — — —</size>\n\n" +
        "<size=170><b>C R E D I T S</b></size>\n\n" +
        "<size=120>DEVELOPED BY</size>\n\n" +
        "<size=200><b>Vishesh Lal</b></size>\n" +
        "<size=130>261B608   ·   1st Year</size>\n\n" +
        "<size=200><b>Sparsh Kumbhare</b></size>\n" +
        "<size=130>261B098   ·   1st Year</size>\n\n" +
        "<size=130>— — — — — — — —</size>";

    const float k_Depth = 2.2f;
    static readonly Color k_Gold = new Color(0.95f, 0.88f, 0.65f);
    static readonly Color k_Pale = new Color(0.85f, 0.82f, 0.76f);

    GUIStyle m_TitleStyle, m_BodyStyle;

    public static void Show()
    {
        ScreenFade.Set(1f);
        GameAudio.Silence();
        var c = new GameObject("Credits").AddComponent<Credits>();
        var head = PlayerRig.Head;
        if (head != null && !PlayerRig.DesktopMode)
        {
            c.transform.SetParent(head, false);
            c.transform.localPosition = new Vector3(0f, 0f, k_Depth);
            c.MakeText("Title", new Vector3(0f, 0.85f, 0f), 4200, k_Title, k_Gold);
            c.MakeText("Body", new Vector3(0f, -0.35f, 0f), 4201, k_Body, k_Pale);
        }
    }

    void MakeText(string name, Vector3 localPos, int queue, string text, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        var tm = go.AddComponent<TextMesh>();
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tm.font = font;
        go.GetComponent<MeshRenderer>().sharedMaterial = Narrator.OverlayTextMaterial(font, queue);
        tm.fontSize = 200;
        tm.characterSize = 0.0028f * k_Depth;
        tm.richText = true;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.lineSpacing = 0.9f;
        tm.color = color;
        tm.text = text;
    }

    void OnGUI()
    {
        if (!PlayerRig.DesktopMode)
            return;
        // OnGUI sizes are in pixels, so scale the TextMesh <size> values down to the screen.
        var scale = Screen.height / 4400f;
        m_TitleStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, richText = true };
        m_BodyStyle  ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, richText = true };
        GUI.depth = -100;

        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);

        var mid = Screen.height * 0.38f;
        GUI.color = k_Gold;
        GUI.Label(new Rect(0, 0, Screen.width, mid), Scale(k_Title, scale), m_TitleStyle);
        GUI.color = k_Pale;
        GUI.Label(new Rect(0, mid + Screen.height * 0.04f, Screen.width, Screen.height - mid), Scale(k_Body, scale), m_BodyStyle);
    }

    static string Scale(string rich, float k) =>
        System.Text.RegularExpressions.Regex.Replace(rich, @"<size=(\d+)>",
            m => "<size=" + Mathf.Max(10, Mathf.RoundToInt(int.Parse(m.Groups[1].Value) * k)) + ">");
}
