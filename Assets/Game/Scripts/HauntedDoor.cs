using System.Collections;
using UnityEngine;

/// <summary>
/// A door on a hinge (this object is the hinge) that can be haunted: slam shut, creak open by itself,
/// get pounded on from the other side, or rattle.
/// </summary>
public class HauntedDoor : MonoBehaviour
{
    [Tooltip("+1 or -1: which way it swings open.")]
    public float openSign = 1f;
    [Tooltip("How far open it starts (degrees, 0 = closed).")]
    public float startOpen;

    Quaternion m_Closed;
    float m_Angle;
    Coroutine m_Run;

    void Awake()
    {
        // the builder leaves the door at its starting angle; work out where "shut" is
        m_Closed = transform.localRotation * Quaternion.Euler(0f, -startOpen * openSign, 0f);
        SetAngle(startOpen);
    }

    void SetAngle(float a)
    {
        m_Angle = a;
        transform.localRotation = m_Closed * Quaternion.Euler(0f, a * openSign, 0f);
    }

    void Run(IEnumerator r)
    {
        if (m_Run != null) StopCoroutine(m_Run);
        m_Run = StartCoroutine(r);
    }

    /// <summary>Slams shut (or, if already shut, bangs violently in its frame).</summary>
    public void Slam() => Run(SlamRoutine());

    public void CreakOpen() => Run(OpenRoutine(75f, 4f, "door_wood_open"));

    public void SwingOpen() => Run(OpenRoutine(95f, 1.4f, "door_metal_open"));

    /// <summary>Something pounds on it from the other side.</summary>
    public void Knock() => Run(KnockRoutine("knock_loud", new[] { 0f, 0.28f, 0.56f, 1.5f, 1.72f, 1.94f }));

    public void Rattle() => Run(KnockRoutine("door_locked", new[] { 0f, 0.11f, 0.22f, 0.33f, 0.44f, 0.55f }));

    IEnumerator SlamRoutine()
    {
        var start = m_Angle;
        if (start < 5f)
        {
            for (var t = 0f; t < 0.08f; t += Time.deltaTime) { SetAngle(Mathf.Lerp(start, 9f, t / 0.08f)); yield return null; }
            start = 9f;
        }
        for (var t = 0f; t < 0.12f; t += Time.deltaTime)
        {
            var k = t / 0.12f;
            SetAngle(Mathf.Lerp(start, 0f, k * k));
            yield return null;
        }
        SetAngle(0f);
        GameAudio.At("door_slam", transform.position + Vector3.up * 1.2f, 1f, 30f, 0.02f);
        NoiseSystem.Emit(transform.position, 12f);
        if (PlayerRig.Head != null && Vector3.Distance(PlayerRig.Head.position, transform.position) < 6f)
        {
            ScareDirector.Haptics(1f, 0.3f);
            DesktopPlayer.Shake(0.035f, 0.35f);
        }
        // the bang knocks the nearby lamps out for a second and everything else goes quiet
        ScareDirector.Duck(1.4f, 0.15f);
        ScareDirector.KillLights(transform.position, 7f, Random.Range(0.25f, 0.9f));
        // a little bounce
        for (var t = 0f; t < 0.2f; t += Time.deltaTime) { SetAngle(Mathf.Sin(t / 0.2f * Mathf.PI) * 2.5f); yield return null; }
        SetAngle(0f);
    }

    IEnumerator OpenRoutine(float angle, float seconds, string sound)
    {
        GameAudio.At(sound, transform.position + Vector3.up * 1.2f, 0.9f, 15f);
        var start = m_Angle;
        for (var t = 0f; t < seconds; t += Time.deltaTime)
        {
            var k = Mathf.SmoothStep(0f, 1f, t / seconds);
            SetAngle(Mathf.Lerp(start, angle, k));
            yield return null;
        }
        SetAngle(angle);
    }

    float m_Tempo = 1f;

    IEnumerator KnockRoutine(string sound, float[] hits)
    {
        m_Tempo = Random.Range(0.85f, 1.2f); // never quite the same rhythm
        GameAudio.At(sound, transform.position + Vector3.up * 1.3f, 1f, 25f, 0.02f);
        var t0 = Time.time;
        foreach (var h in hits)
        {
            while (Time.time - t0 < h * m_Tempo) yield return null;
            if (PlayerRig.Head != null && Vector3.Distance(PlayerRig.Head.position, transform.position) < 4f)
                ScareDirector.Haptics(0.35f, 0.05f);
            for (var t = 0f; t < 0.09f; t += Time.deltaTime)
            {
                SetAngle(m_Angle * 0f + Mathf.Sin(t / 0.09f * Mathf.PI) * 1.8f);
                yield return null;
            }
            SetAngle(0f);
        }
    }
}
