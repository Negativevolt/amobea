using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// One configurable scare, fired from a trigger zone, a pickup, a door... (call Play()).
/// </summary>
public class ScareEvent : MonoBehaviour
{
    public enum Kind { InFace, LookBehind, FigureCross, Appear, Sound, Blackout, Show, Hide, Nothing, TentacleBurst }

    public Kind kind = Kind.InFace;
    public Monster monster = Monster.Watcher;
    [Tooltip("Seconds to wait before it happens.")]
    public float delay;
    [Tooltip("InFace: how long it stays in your face. Appear: how long it stands there. Blackout: how long the lights die.")]
    public float seconds = 0.9f;
    [Tooltip("FigureCross: start and end points. Appear / Sound: where (this object if empty).")]
    public Transform from, to;
    public float speed = 2.2f;
    [Tooltip("Sound: clip name in Resources/SFX (e.g. door_slam).")]
    public string sound;
    public float volume = 1f;
    [Tooltip("Blackout: these lights die (empty = every flickering light within 'radius').")]
    public FlickerLight[] lights;
    public float radius = 12f;
    [Tooltip("Show / Hide: these objects appear or disappear.")]
    public GameObject[] objects;
    [Tooltip("Narration line said when it happens (optional).")]
    public string narrationId;
    [Tooltip("Extra random wait on top of 'delay' so scares never come at the same moment. -1 = automatic (only for scares without a fixed delay).")]
    public float randomDelay = -1f;
    [Tooltip("Appear / FigureCross: wait until the spot is out of view, so it's already there when you look.")]
    public bool waitUntilUnseen = true;
    [Tooltip("TentacleBurst: they come out of this object's position, along its blue (forward) axis, and never reach further than this.")]
    public float reachLimit = 1.6f;
    public bool once = true;
    public UnityEvent afterwards = new UnityEvent();

    bool m_Played;

    float AutoJitter()
    {
        switch (kind)
        {
            case Kind.InFace: return 0.8f;
            case Kind.LookBehind: return 3f;
            case Kind.FigureCross: return 1.5f;
            case Kind.Appear: return 1f;
            case Kind.Sound: return 2f;
            case Kind.Blackout: return 1f;
            case Kind.TentacleBurst: return 1.2f;
            default: return 0f;
        }
    }

    public void Play()
    {
        if (once && m_Played)
            return;
        m_Played = true;
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);
        var jitter = randomDelay >= 0f ? randomDelay : delay > 0f ? 0f : AutoJitter();
        if (jitter > 0f)
            yield return new WaitForSeconds(Random.Range(0f, jitter));
        var at = from != null ? from.position : transform.position;
        if (waitUntilUnseen && delay <= 0f && (kind == Kind.Appear || kind == Kind.FigureCross))
        {
            // don't let the player watch it pop into existence
            var spot = at + Vector3.up * 1.2f;
            for (var w = 0f; w < 6f && PlayerRig.Head != null
                 && Vector3.Angle(PlayerRig.Head.forward, spot - PlayerRig.Head.position) < 55f; w += Time.deltaTime)
                yield return null;
        }
        if (!string.IsNullOrEmpty(narrationId))
            Narrator.SayOnce(narrationId);
        switch (kind)
        {
            case Kind.InFace:
                ScareDirector.InFace(monster, seconds);
                break;
            case Kind.LookBehind:
                ScareDirector.LookBehind(monster, Mathf.Max(3f, seconds));
                break;
            case Kind.FigureCross:
                if (from != null && to != null)
                    ScareDirector.FigureCross(monster, from.position, to.position, speed);
                break;
            case Kind.Appear:
                ScareDirector.Appear(monster, at, seconds);
                break;
            case Kind.TentacleBurst:
                ScareDirector.TentacleBurst(at, (from != null ? from : transform).forward, Mathf.Max(0.6f, seconds), reachLimit);
                break;
            case Kind.Sound:
                GameAudio.At(sound, at, Mathf.Min(1f, volume * 1.3f), 20f, 0.1f);
                if (PlayerRig.Head != null && Vector3.Distance(PlayerRig.Head.position, at) < 5f && volume > 0.6f)
                    ScareDirector.Haptics(0.4f, 0.12f);
                break;
            case Kind.Blackout:
                var list = lights != null && lights.Length > 0 ? lights : FindObjectsByType<FlickerLight>(FindObjectsSortMode.None);
                foreach (var l in list)
                    if (l != null && (lights != null && lights.Length > 0 || Vector3.Distance(l.transform.position, transform.position) < radius))
                        l.ForceBlackout(seconds);
                GameAudio.At("light_flicker", transform.position + Vector3.up * 2f, 0.8f, 15f);
                break;
            case Kind.Show:
            case Kind.Hide:
                foreach (var o in objects)
                    if (o != null)
                        o.SetActive(kind == Kind.Show);
                if (!string.IsNullOrEmpty(sound))
                    GameAudio.At(sound, at, volume, 20f);
                break;
        }
        afterwards.Invoke();
    }
}
