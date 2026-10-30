using UnityEngine;

/// <summary>
/// The Watcher: stands silently at the end of corridors, staring. Harmless, but it vanishes the moment your
/// torch beam lands on it or you get close, so you're never sure you saw it.
/// </summary>
public class WatcherEntity : EntityBase
{
    public float vanishDistance = 3.5f;
    public bool startHidden = true;
    [Tooltip("Instead of quietly vanishing when you get close or light it, it lunges into your face.")]
    public bool faceScareOnApproach;

    Renderer[] m_Renderers;
    bool m_Shown;
    float m_ShownAt;

    protected override void Awake()
    {
        base.Awake();
        m_Renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in m_Renderers)
            foreach (var m in r.materials)
                if (m.name.Contains("eyes"))
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", Color.white * 3f);
                }
        SetShown(!startHidden);
    }

    void SetShown(bool shown)
    {
        m_Shown = shown;
        m_ShownAt = Time.time;
        foreach (var r in m_Renderers)
            r.enabled = shown;
    }

    public void Appear() => SetShown(true);

    public void AppearAt(Transform spot)
    {
        transform.position = spot.position;
        SetShown(true);
    }

    public void Vanish()
    {
        if (m_Shown)
            GameAudio.At("watcher_vanish", transform.position + Vector3.up * 1.5f, 0.8f, 20f);
        SetShown(false);
    }

    void Update()
    {
        if (!m_Shown || m_Frozen)
            return;
        Play("Idle");
        FacePlayer(2f);
        // a short grace period so the player actually gets to see it
        if (Time.time - m_ShownAt < 0.6f)
            return;
        if (faceScareOnApproach)
        {
            // waits until you walk right up to it (torchlight from far away doesn't count)
            if (DistanceToPlayer < vanishDistance && !ScareDirector.Busy)
            {
                faceScareOnApproach = false;
                SetShown(false);
                ScareDirector.InFace(Monster.Watcher, 1.0f);
            }
            return;
        }
        if (DistanceToPlayer < vanishDistance || IsLitByTorch(transform.position + Vector3.up * 1.9f))
        {
            if (faceScareOnApproach)
            {
                faceScareOnApproach = false;
                SetShown(false);
                ScareDirector.InFace(Monster.Watcher, 1.0f);
            }
            else
                Vanish();
        }
    }

    public override void ResetEntity()
    {
        base.ResetEntity();
        SetShown(!startHidden);
    }
}
