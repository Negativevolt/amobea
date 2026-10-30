using System.Collections;
using UnityEngine;

/// <summary>
/// Scare: the corridor lamp blacks out; when it flickers back the Watcher is standing at the far end;
/// next blackout, it's gone.
/// </summary>
public class WatcherFlickerScare : MonoBehaviour
{
    public FlickerLight lamp;
    public WatcherEntity watcher;
    public Transform spot;

    bool m_Played;

    public void Play()
    {
        if (!m_Played)
            StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        m_Played = true;
        if (lamp != null) lamp.ForceBlackout(0.6f);
        if (lamp != null) GameAudio.At("light_flicker", lamp.transform.position, 0.8f, 12f);
        yield return new WaitForSeconds(0.55f);
        if (watcher != null) watcher.AppearAt(spot != null ? spot : watcher.transform);
        GameAudio.Flat("stinger_jump", 0.45f);
        yield return new WaitForSeconds(1.8f);
        if (lamp != null) lamp.ForceBlackout(0.35f);
        if (lamp != null) GameAudio.At("light_flicker", lamp.transform.position, 0.8f, 12f);
        yield return new WaitForSeconds(0.15f);
        if (watcher != null) watcher.Vanish();
    }
}
