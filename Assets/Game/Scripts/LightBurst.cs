using System.Collections;
using UnityEngine;

/// <summary>Scare: a ceiling tube flares up and bursts with a pop, leaving that part of the corridor dark.</summary>
public class LightBurst : MonoBehaviour
{
    public Light lightSource;
    [Tooltip("The glowing tube (its glow is switched off).")]
    public Renderer tube;
    [Tooltip("0 = stays dead for good.")]
    public float deadSeconds;

    bool m_Played;

    public void Play()
    {
        if (!m_Played)
            StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        m_Played = true;
        var l = lightSource != null ? lightSource : GetComponentInChildren<Light>();
        if (l == null)
            yield break;
        var flicker = l.GetComponent<FlickerLight>();
        if (flicker != null) flicker.enabled = false;
        var baseIntensity = l.intensity;
        l.intensity = baseIntensity * 4f;
        GameAudio.At("tube_pop", l.transform.position, 1f, 20f);
        yield return new WaitForSeconds(0.07f);
        l.enabled = false;
        Material mat = null;
        if (tube != null)
        {
            mat = tube.material;
            mat.SetColor("_EmissionColor", Color.black);
        }
        if (deadSeconds <= 0f)
            yield break;
        yield return new WaitForSeconds(deadSeconds);
        l.intensity = baseIntensity;
        l.enabled = true;
        if (flicker != null) flicker.enabled = true;
        m_Played = false;
    }
}
