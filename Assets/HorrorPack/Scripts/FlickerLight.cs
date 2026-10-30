using UnityEngine;

/// <summary>Makes a light flicker like a failing bulb: a steady buzz plus random blackouts.</summary>
[RequireComponent(typeof(Light))]
public class FlickerLight : MonoBehaviour
{
    [Range(0f, 1f)] public float buzz = 0.15f;
    [Tooltip("Chance per second of a blackout burst.")]
    [Range(0f, 3f)] public float blackoutsPerSecond = 0.35f;
    [Tooltip("How dark a blackout gets (0 = fully off).")]
    [Range(0f, 1f)] public float blackoutLevel = 0f;

    Light m_Light;
    float m_Base, m_Seed, m_BlackoutEnd, m_ForcedEnd;

    /// <summary>Scare helper: fully dark for a moment.</summary>
    public void ForceBlackout(float seconds) => m_ForcedEnd = Time.time + seconds;

    void Awake()
    {
        m_Light = GetComponent<Light>();
        m_Base = m_Light.intensity;
        m_Seed = Random.value * 100f;
    }

    void Update()
    {
        if (Time.time < m_ForcedEnd)
        {
            m_Light.intensity = 0f;
            return;
        }
        var k = 1f + (Mathf.PerlinNoise(m_Seed, Time.time * 14f) - 0.5f) * 2f * buzz;
        if (Time.time > m_BlackoutEnd && Random.value < blackoutsPerSecond * Time.deltaTime)
            m_BlackoutEnd = Time.time + Random.Range(0.05f, 0.9f);
        if (Time.time < m_BlackoutEnd)
            k *= Mathf.PerlinNoise(m_Seed + 30f, Time.time * 35f) > 0.6f ? 0.6f : blackoutLevel;
        m_Light.intensity = m_Base * k;
    }
}
