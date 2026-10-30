using UnityEngine;

/// <summary>Animated low-res static on a TV screen (green tinted, like an old CRT), with a soft glow.</summary>
public class TVStatic : MonoBehaviour
{
    public Renderer screen;
    public Color tint = new Color(0.55f, 1f, 0.45f);
    public float brightness = 1.2f;
    public Light glow;

    Texture2D m_Tex;
    Color32[] m_Pixels;
    Material m_Mat;
    float m_Next;
    public bool On = true;

    void Start()
    {
        if (screen == null) screen = GetComponentInChildren<Renderer>();
        if (screen == null) return;
        m_Tex = new Texture2D(48, 36, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
        m_Pixels = new Color32[48 * 36];
        m_Mat = screen.material;
        m_Mat.SetTexture("_BaseMap", m_Tex);
        m_Mat.SetTexture("_EmissionMap", m_Tex);
        m_Mat.EnableKeyword("_EMISSION");
    }

    void Update()
    {
        if (m_Mat == null || Time.time < m_Next)
            return;
        m_Next = Time.time + 0.045f;
        var t32 = (Color32)tint;
        for (var i = 0; i < m_Pixels.Length; i++)
        {
            var v = On ? (byte)Random.Range(0, 255) : (byte)0;
            m_Pixels[i] = new Color32((byte)(v * t32.r / 255), (byte)(v * t32.g / 255), (byte)(v * t32.b / 255), 255);
        }
        // a rolling dark band, like a bad signal
        var band = (int)(Time.time * 14f) % 36;
        for (var x = 0; x < 48; x++)
            for (var y = band; y < Mathf.Min(36, band + 3); y++)
                m_Pixels[y * 48 + x] = new Color32((byte)(m_Pixels[y * 48 + x].r / 3), (byte)(m_Pixels[y * 48 + x].g / 3), (byte)(m_Pixels[y * 48 + x].b / 3), 255);
        m_Tex.SetPixels32(m_Pixels);
        m_Tex.Apply(false);
        m_Mat.SetColor("_EmissionColor", On ? Color.white * brightness : Color.black);
        if (glow != null)
            glow.intensity = On ? Random.Range(0.6f, 1.1f) : 0f;
    }
}
