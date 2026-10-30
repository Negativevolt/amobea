using UnityEngine;

/// <summary>Electrical buzz from a lamp. Follows the light: when it flickers or blacks out, the buzz cuts too.</summary>
public class LightHum : MonoBehaviour
{
    Light m_Light;
    AudioSource m_Source;
    float m_Volume, m_FullIntensity;

    public static void Attach(Light light, float volume)
    {
        if (light == null || light.GetComponent<LightHum>() != null)
            return;
        var hum = light.gameObject.AddComponent<LightHum>();
        hum.m_Light = light;
        hum.m_Volume = volume;
    }

    void Start()
    {
        m_Source = GameAudio.Loop("hum_lights", transform, m_Volume, 6f);
        m_FullIntensity = Mathf.Max(0.01f, m_Light != null ? m_Light.intensity : 1f);
    }

    void Update()
    {
        if (m_Source == null || m_Light == null)
            return;
        m_FullIntensity = Mathf.Max(m_FullIntensity, m_Light.intensity);
        var on = m_Light.enabled ? Mathf.Clamp01(m_Light.intensity / m_FullIntensity) : 0f;
        m_Source.volume = m_Volume * on;
        m_Source.pitch = 0.97f + 0.06f * on;
    }
}
