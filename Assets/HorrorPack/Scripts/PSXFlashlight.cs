using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// A grabbable horror flashlight.
///  - Grab with GRIP, switch on/off with TRIGGER while holding it (or press F on the keyboard).
///  - Slight constant flicker plus the occasional "dying battery" stutter.
/// The beam is a spot light with a pixelated, grainy cookie for the PS1 look.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class PSXFlashlight : MonoBehaviour
{
    [Tooltip("The spot light that makes the beam.")]
    public Light beam;

    [Tooltip("Optional small light that makes the lens glow when on.")]
    public Light glow;

    public bool startOn = true;

    [Header("Flicker")]
    [Tooltip("Constant subtle wobble in brightness (0 = steady).")]
    [Range(0f, 0.5f)] public float flickerAmount = 0.08f;

    [Tooltip("Average seconds between 'dying battery' stutters. 0 = never.")]
    public float stutterEvery = 25f;

    [Header("Sound (optional)")]
    public AudioClip clickSound;

    XRGrabInteractable m_Grab;
    AudioSource m_Audio;
    float m_BeamIntensity, m_GlowIntensity;
    bool m_On;
    float m_NextStutter, m_StutterEnd, m_BlackoutEnd;
    bool m_EverGrabbed;
    float m_Seed;

    void Awake()
    {
        m_Grab = GetComponent<XRGrabInteractable>();
        if (beam != null) m_BeamIntensity = beam.intensity;
        if (glow != null) m_GlowIntensity = glow.intensity;
        m_Seed = Random.value * 100f;
        if (clickSound != null)
        {
            m_Audio = gameObject.AddComponent<AudioSource>();
            m_Audio.spatialBlend = 1f;
            m_Audio.playOnAwake = false;
        }
        ScheduleStutter();
        SetOn(startOn, false);
    }

    void OnEnable()
    {
        m_Grab.activated.AddListener(OnActivated);
        m_Grab.selectEntered.AddListener(OnGrabbed);
    }

    void OnDisable()
    {
        m_Grab.activated.RemoveListener(OnActivated);
        m_Grab.selectEntered.RemoveListener(OnGrabbed);
    }

    void OnGrabbed(SelectEnterEventArgs _)
    {
        GameFlow.AddItem("torch"); // you have it now (Objectives)
        if (m_EverGrabbed)
            return;
        m_EverGrabbed = true;
        Narrator.SayOnce("N1_02");
    }

    /// <summary>True when switched on (even while flickering).</summary>
    public bool IsOn => m_On;

    /// <summary>True when on and NOT flickering or blacked out (what the monsters check).</summary>
    public bool IsSteadyOn => m_On && Time.time >= m_StutterEnd && Time.time >= m_BlackoutEnd;

    /// <summary>Scare helper: the beam dies completely for a few seconds, then comes back by itself.</summary>
    public void ForceBlackout(float seconds) => m_BlackoutEnd = Time.time + seconds;

    void OnActivated(ActivateEventArgs _) => Toggle();

    public void Toggle() => SetOn(!m_On, true);

    public void SetOn(bool on, bool click)
    {
        m_On = on;
        if (beam != null) beam.enabled = on;
        if (glow != null) glow.enabled = on;
        if (click && m_Audio != null && clickSound != null)
            m_Audio.PlayOneShot(clickSound);
        else if (click)
            GameAudio.At("torch_click", transform.position, 0.6f, 4f);
    }

    void Update()
    {
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.fKey.wasPressedThisFrame)
            Toggle();

        if (!m_On || beam == null)
            return;

        if (Time.time < m_BlackoutEnd)
        {
            beam.intensity = 0f;
            if (glow != null) glow.intensity = 0f;
            return;
        }

        // Subtle wobble.
        var k = 1f + (Mathf.PerlinNoise(m_Seed, Time.time * 9f) - 0.5f) * 2f * flickerAmount;

        // Occasional stutter: rapid on/off for a fraction of a second.
        if (stutterEvery > 0f)
        {
            if (Time.time >= m_NextStutter)
            {
                m_StutterEnd = Time.time + Random.Range(0.15f, 0.6f);
                ScheduleStutter();
            }
            if (Time.time < m_StutterEnd)
                k *= Mathf.PerlinNoise(m_Seed + 50f, Time.time * 40f) > 0.5f ? 1f : 0.05f;
        }

        beam.intensity = m_BeamIntensity * k;
        if (glow != null)
            glow.intensity = m_GlowIntensity * k;
    }

    void ScheduleStutter()
    {
        m_NextStutter = stutterEvery > 0f ? Time.time + Random.Range(stutterEvery * 0.5f, stutterEvery * 1.5f) : float.MaxValue;
    }
}
