using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Health system for the player:
/// - Replaces the old broken PlayerCaught flow.
/// - Each jump scare damages the player, surges the heartbeat, and shows a blood effect on camera.
/// - If health drops to zero, the player dies, the stage resets, and they respawn at the stage start.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    static PlayerHealth s_Instance;
    public static PlayerHealth Instance
    {
        get
        {
            if (s_Instance == null)
            {
                s_Instance = FindAnyObjectByType<PlayerHealth>();
                if (s_Instance == null)
                {
                    var go = new GameObject("Player Health");
                    s_Instance = go.AddComponent<PlayerHealth>();
                    DontDestroyOnLoad(go);
                }
            }
            return s_Instance;
        }
    }

    [Tooltip("Maximum player health (default 100). 3 jump scares will kill the player.")]
    public float maxHealth = 100f;
    public float currentHealth { get; private set; } = 100f;
    public bool isDead { get; private set; }

    [Header("Visuals & Audio")]
    [Tooltip("Damage dealt per jump scare.")]
    public float scareDamage = 35f;

    // Heartbeat intensity: 0 to 1
    static float s_HeartbeatSurge;

    // Blood overlay components
    static Texture2D s_BloodTexture;
    GameObject m_BloodQuad;
    Material m_BloodMaterial;
    float m_FlashAlpha;      // fast pulse on hit — fades in ~1 s
    float m_PersistentBlood; // accumulates per scare, fades over ~20 s
    float m_CurrentAlpha;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_Instance = null;
        s_HeartbeatSurge = 0f;
        if (s_BloodTexture != null)
        {
            Destroy(s_BloodTexture);
            s_BloodTexture = null;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (s_Instance == null)
            s_Instance = Instance;
    }

    void Awake()
    {
        if (s_Instance != null && s_Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        s_Instance = this;
        currentHealth = maxHealth;
        isDead = false;
        m_FlashAlpha = 0f;
        m_CurrentAlpha = 0f;
    }

    void Start()
    {
        EnsureBloodTexture();
        SetupCameraQuad();
    }

    void Update()
    {
        if (m_BloodQuad == null && PlayerRig.Head != null)
            SetupCameraQuad();

        // Press B in Play mode to test the blood flash without needing a monster scare.
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.bKey.wasPressedThisFrame) ApplyDamage(35f, 1f);

        // Decay heartbeat surge over time
        if (s_HeartbeatSurge > 0f)
            s_HeartbeatSurge = Mathf.MoveTowards(s_HeartbeatSurge, 0f, Time.deltaTime * 0.22f);

        // Fast flash decays in ~1 s; persistent stain decays slowly (~0.05/s → ~20 s full clear).
        if (m_FlashAlpha > 0f)
            m_FlashAlpha = Mathf.MoveTowards(m_FlashAlpha, 0f, Time.deltaTime * 0.75f);
        if (m_PersistentBlood > 0f)
            m_PersistentBlood = Mathf.MoveTowards(m_PersistentBlood, 0f, Time.deltaTime * 0.05f);

        // Baseline blood visibility based on missing health (always 0 while immortal, but kept for future use).
        float missingRatio = Mathf.Clamp01(1f - (currentHealth / maxHealth));
        float pulse = (m_PersistentBlood > 0.15f)
            ? Mathf.Sin(Time.time * (1.8f + m_PersistentBlood * 2f)) * 0.06f * m_PersistentBlood
            : 0f;
        float targetAlpha = Mathf.Clamp01(missingRatio * 0.75f + m_PersistentBlood + m_FlashAlpha + pulse);

        m_CurrentAlpha = Mathf.MoveTowards(m_CurrentAlpha, targetAlpha, Time.deltaTime * 4f);

        if (m_BloodMaterial != null)
        {
            m_BloodMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, m_CurrentAlpha));
            if (m_BloodQuad != null)
            {
                var r = m_BloodQuad.GetComponent<MeshRenderer>();
                if (r != null) r.enabled = m_CurrentAlpha > 0.005f;
            }
        }
    }

    /// <summary>Call this whenever a jump scare occurs or a monster hits the player.</summary>
    public static void TakeScareDamage(float damage = 35f, float surgeIntensity = 1f)
    {
        Instance.ApplyDamage(damage, surgeIntensity);
    }

    public void ApplyDamage(float amount, float surgeIntensity)
    {
        // Player is immortal: scares trigger visual/audio feedback but deal no real damage.
        // Blood flash, heartbeat surge, haptics and screen-shake all still fire.
        m_FlashAlpha     = Mathf.Min(1.0f, m_FlashAlpha + 0.85f * surgeIntensity);
        // Each scare adds a persistent blood stain that takes ~20 s to fully clear.
        m_PersistentBlood = Mathf.Min(0.85f, m_PersistentBlood + 0.30f * surgeIntensity);
        s_HeartbeatSurge = Mathf.Max(s_HeartbeatSurge,  Mathf.Clamp01(0.9f * surgeIntensity));
        ScareDirector.Haptics(1f, 0.4f);
        DesktopPlayer.Shake(0.08f, 0.5f);
    }

    /// <summary>Calculates current heartbeat strength (0..1) combining panic surge and health loss.</summary>
    public static float GetHeartbeatIntensity()
    {
        if (s_Instance == null) return 0f;
        float healthDeficit = Mathf.Clamp01(1f - (s_Instance.currentHealth / s_Instance.maxHealth));
        return Mathf.Clamp01(s_HeartbeatSurge + healthDeficit * 0.8f);
    }

    IEnumerator DieRoutine()
    {
        isDead = true;
        GameAudio.Flat("caught_sting", 1f);

        // Fade view to dark red / black
        yield return ScreenFade.FadeTo(1f, 0.8f);

        yield return new WaitForSeconds(1.2f);

        // Reset all monsters to initial states
        EntityBase.ResetAll();

        // Respawn player at the current stage start spawn
        var flow = GameFlow.Instance;
        if (flow != null && flow.stageSpawns != null && flow.CurrentStage - 1 < flow.stageSpawns.Length)
        {
            var spawn = flow.stageSpawns[flow.CurrentStage - 1];
            if (spawn != null)
                PlayerRig.TeleportTo(spawn);
        }

        // Restore health & clear blood
        currentHealth = maxHealth;
        m_FlashAlpha = 0f;
        s_HeartbeatSurge = 0f;
        isDead = false;

        yield return new WaitForSeconds(0.4f);
        yield return ScreenFade.FadeTo(0f, 1.2f);
    }

    void SetupCameraQuad()
    {
        var head = PlayerRig.Head;
        if (head == null || m_BloodQuad != null)
            return;

        EnsureBloodTexture();

        m_BloodQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        m_BloodQuad.name = "Blood Vignette Quad";
        Destroy(m_BloodQuad.GetComponent<Collider>());
        m_BloodQuad.transform.SetParent(head, false);
        // Positioned 0.11m in front of eyes (ScreenFade is at 0.12m so fade covers blood)
        m_BloodQuad.transform.localPosition = new Vector3(0f, 0f, 0.11f);
        m_BloodQuad.transform.localScale = new Vector3(1.4f, 1.4f, 1f);

        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent");
        m_BloodMaterial = new Material(shader);
        // URP transparency setup ------------------------------------------------
        // Must enable the keyword BEFORE setting float properties in URP.
        m_BloodMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m_BloodMaterial.SetFloat("_Surface", 1f);           // surface type: transparent
        m_BloodMaterial.SetFloat("_Blend", 0f);             // blend mode: alpha
        m_BloodMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m_BloodMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m_BloodMaterial.SetInt("_ZWrite", 0);
        // Note: URP Unlit does NOT expose _ZTest. Use a very high renderQueue so the
        // quad renders after ALL scene geometry — equivalent to ZTest Always for a
        // camera-parented overlay.
        m_BloodMaterial.SetOverrideTag("RenderType", "Transparent");
        m_BloodMaterial.renderQueue = 5000;                 // renders absolutely last
        // URP Unlit samples _BaseMap; legacy _MainTex is ignored by that shader.
        // Set both so it works whether URP or built-in fallback shader is active.
        m_BloodMaterial.mainTexture = s_BloodTexture;
        if (m_BloodMaterial.HasProperty("_BaseMap"))
            m_BloodMaterial.SetTexture("_BaseMap", s_BloodTexture);
        m_BloodMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));

        var mr = m_BloodQuad.GetComponent<MeshRenderer>();
        mr.sharedMaterial = m_BloodMaterial;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.enabled = false;
    }

    void EnsureBloodTexture()
    {
        if (s_BloodTexture != null)
            return;

        const int size = 256;
        s_BloodTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        s_BloodTexture.wrapMode = TextureWrapMode.Clamp;
        s_BloodTexture.name = "Procedural_Blood_Vignette";

        var pixels = new Color[size * size];
        var center = new Vector2(0.5f, 0.5f);

        for (int y = 0; y < size; y++)
        {
            float v = (float)y / (size - 1);
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / (size - 1);
                var pos = new Vector2(u, v);
                float dist = Vector2.Distance(pos, center) * 1.414f; // 0 at center, ~1 at corners

                // Splatter noise at the edges
                float n1 = Mathf.PerlinNoise(u * 8f, v * 8f);
                float n2 = Mathf.PerlinNoise(u * 18f, v * 18f) * 0.5f;
                float splatter = Mathf.Clamp01((dist - 0.45f) / 0.55f + (n1 + n2 - 0.75f) * 0.4f);

                // Deep crimson with darker coagulated specks
                float speck = Mathf.PerlinNoise(u * 28f, v * 28f);
                Color bloodColor = speck > 0.6f ? new Color(0.35f, 0.01f, 0.01f, splatter) : new Color(0.78f, 0.03f, 0.03f, splatter * 0.95f);

                pixels[y * size + x] = bloodColor;
            }
        }

        s_BloodTexture.SetPixels(pixels);
        s_BloodTexture.Apply();
    }

    void OnGUI()
    {
        if (!PlayerRig.DesktopMode || m_CurrentAlpha <= 0.005f || s_BloodTexture == null)
            return;

        var prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, m_CurrentAlpha);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), s_BloodTexture, ScaleMode.StretchToFill);
        GUI.color = prev;
    }

    void OnDestroy()
    {
        if (m_BloodQuad != null)
            Destroy(m_BloodQuad);
        if (m_BloodMaterial != null)
            Destroy(m_BloodMaterial);
    }
}
