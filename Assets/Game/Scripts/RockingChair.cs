using System.Collections;
using UnityEngine;

/// <summary>Scare: an empty rocking chair starts rocking on its own, then slowly settles.</summary>
public class RockingChair : MonoBehaviour
{
    public float angle = 9f;
    public float seconds = 14f;

    Quaternion m_Rest;
    bool m_Rocking;

    void Awake() => m_Rest = transform.localRotation;

    public void Activate()
    {
        if (!m_Rocking)
            StartCoroutine(Rock());
    }

    IEnumerator Rock()
    {
        m_Rocking = true;
        var creak = GameAudio.Loop("rocking_chair", transform, 0.9f, 10f, false);
        if (creak != null)
        {
            creak.pitch = 2.4f / (2f * Mathf.PI / 2.6f); // one creak loop per swing
            creak.Play();
        }
        for (var t = 0f; t < seconds; t += Time.deltaTime)
        {
            var amp = angle * (1f - t / seconds);
            if (creak != null) creak.volume = 0.9f * (1f - t / seconds);
            transform.localRotation = m_Rest * Quaternion.Euler(Mathf.Sin(t * 2.6f) * amp, 0f, 0f);
            yield return null;
        }
        transform.localRotation = m_Rest;
        if (creak != null) Destroy(creak.gameObject);
        m_Rocking = false;
    }
}
