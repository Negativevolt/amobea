using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// All game sound. Clips live in Resources/SFX; "step_wood" picks randomly between step_wood_1, _2, ...
/// Also runs the background: a looping bed per stage, random distant sounds (knocks, creaks, drips,
/// whispers), the chase drone + heartbeat when something is hunting you, and the nursery music box.
/// Created automatically when the game starts; every call is safe even if a clip is missing.
/// </summary>
public class GameAudio : MonoBehaviour
{
    static GameAudio s_Instance;
    static readonly Dictionary<string, List<AudioClip>> s_Clips = new Dictionary<string, List<AudioClip>>();

    [Range(0f, 1f)] public float master = 1f;
    [Range(0f, 1f)] public float ambienceVolume = 0.75f;

    readonly List<AudioSource> m_Pool = new List<AudioSource>();
    AudioSource m_BedA, m_BedB, m_Drone, m_Heart;
    int m_Stage = -1;
    float m_NextOneShot, m_NextMusicBox, m_NextDangerCheck, m_Danger;
    float m_NextPhantomApproach, m_NextFalseAlarm, m_FalseAlarmUntil;
    readonly HashSet<GameObject> m_Decorated = new HashSet<GameObject>();

    // Random distant sounds per stage.
    static readonly string[][] k_StageOneShots =
    {
        new[] { "distant_knock", "distant_creak", "distant_creak", "distant_thud", "whisper" },
        new[] { "drip", "drip", "drip", "distant_knock", "distant_thud", "distant_creak" },
        new[] { "whisper", "whisper", "distant_creak", "distant_knock" },
    };
    static readonly string[] k_StageBeds = { "amb_apartment", "amb_basement", "amb_nursery" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (s_Instance == null)
            new GameObject("Game Audio").AddComponent<GameAudio>();
    }

    static GameAudio Instance
    {
        get
        {
            if (s_Instance == null)
                Boot();
            return s_Instance;
        }
    }

    void Awake()
    {
        if (s_Instance != null && s_Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        s_Instance = this;
        LoadClips();
        m_BedA = MakeLoopSource("Ambience A", 0f);
        m_BedB = MakeLoopSource("Ambience B", 0f);
        m_Drone = MakeLoopSource("Chase Drone", 0f);
        m_Heart = MakeLoopSource("Heartbeat", 0f);
        m_Drone.clip = Clip("drone_chase");
        m_Heart.clip = Clip("heartbeat");
        m_NextOneShot = Time.time + Random.Range(6f, 12f);
        m_NextMusicBox = Time.time + 8f;
        m_NextPhantomApproach = Time.time + Random.Range(40f, 70f);
        m_NextFalseAlarm      = Time.time + Random.Range(60f, 120f);
    }

    static void LoadClips()
    {
        if (s_Clips.Count > 0)
            return;
        foreach (var c in Resources.LoadAll<AudioClip>("SFX"))
        {
            // "step_wood_3" -> also registered under "step_wood"
            Add(c.name, c);
            var cut = c.name.LastIndexOf('_');
            if (cut > 0 && int.TryParse(c.name.Substring(cut + 1), out _))
                Add(c.name.Substring(0, cut), c);
        }
    }

    static void Add(string key, AudioClip c)
    {
        if (!s_Clips.TryGetValue(key, out var list))
            s_Clips[key] = list = new List<AudioClip>();
        list.Add(c);
    }

    public static AudioClip Clip(string name)
    {
        LoadClips();
        if (string.IsNullOrEmpty(name) || !s_Clips.TryGetValue(name, out var list) || list.Count == 0)
            return null;
        return list[Random.Range(0, list.Count)];
    }

    AudioSource MakeLoopSource(string name, float spatial)
    {
        var s = new GameObject(name).AddComponent<AudioSource>();
        s.transform.SetParent(transform, false);
        s.loop = true;
        s.playOnAwake = false;
        s.spatialBlend = spatial;
        s.volume = 0f;
        return s;
    }

    AudioSource FreeSource()
    {
        foreach (var s in m_Pool)
            if (s != null && !s.isPlaying)
                return s;
        var src = new GameObject("SFX").AddComponent<AudioSource>();
        src.transform.SetParent(transform, false);
        src.playOnAwake = false;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.dopplerLevel = 0f;
        m_Pool.Add(src);
        return src;
    }

    // ------------------------------------------------------------------ public API
    /// <summary>Cuts every sound at once and stops the background for good (the hard cut to the credits).</summary>
    public static void Silence()
    {
        if (s_Instance != null)
        {
            s_Instance.StopAllCoroutines();
            s_Instance.enabled = false;
        }
        foreach (var s in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            s.Stop();
    }

    /// <summary>Plays a sound in the world at a position (gets quieter with distance).</summary>
    public static AudioSource At(string name, Vector3 pos, float volume = 1f, float range = 15f, float pitchJitter = 0.06f)
    {
        var clip = Clip(name);
        if (clip == null || Instance == null)
            return null;
        var s = Instance.FreeSource();
        s.transform.position = pos;
        s.clip = clip;
        s.spatialBlend = 1f;
        s.minDistance = 1f;
        s.maxDistance = range;
        s.volume = volume * Instance.master;
        s.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        s.loop = false;
        s.Play();
        return s;
    }

    /// <summary>Plays a sound "in your head" (stingers, UI-ish sounds).</summary>
    public static AudioSource Flat(string name, float volume = 1f)
    {
        var clip = Clip(name);
        if (clip == null || Instance == null)
            return null;
        var s = Instance.FreeSource();
        s.clip = clip;
        s.spatialBlend = 0f;
        s.volume = volume * Instance.master;
        s.pitch = 1f;
        s.loop = false;
        s.Play();
        return s;
    }

    /// <summary>Adds a looping 3D sound to an object (hums, ticking clocks, skittering).</summary>
    public static AudioSource Loop(string name, Transform parent, float volume = 1f, float range = 10f, bool play = true)
    {
        var clip = Clip(name);
        if (clip == null || parent == null)
            return null;
        var s = new GameObject("Loop " + name).AddComponent<AudioSource>();
        s.transform.SetParent(parent, false);
        s.clip = clip;
        s.loop = true;
        s.spatialBlend = 1f;
        s.rolloffMode = AudioRolloffMode.Linear;
        s.minDistance = 0.8f;
        s.maxDistance = range;
        s.dopplerLevel = 0f;
        s.volume = volume * (Instance != null ? Instance.master : 1f);
        s.playOnAwake = false;
        if (play)
        {
            s.time = Random.Range(0f, clip.length * 0.9f); // don't let every copy start in sync
            s.Play();
        }
        return s;
    }

    // ------------------------------------------------------------------ background
    void Update()
    {
        var flow = GameFlow.Instance;
        var stage = flow != null ? flow.CurrentStage : 1;
        if (stage != m_Stage)
            EnterStage(stage);

        // random ambient sounds — sometimes distant, occasionally very close behind you
        if (Time.time > m_NextOneShot && PlayerRig.Head != null)
        {
            m_NextOneShot = Time.time + Random.Range(6f, 14f);
            var pool = k_StageOneShots[Mathf.Clamp(stage - 1, 0, 2)];
            var pick = pool[Random.Range(0, pool.Length)];
            Vector3 pos;
            float vol;
            var head = PlayerRig.Head;
            // 25% chance: fire the sound RIGHT BEHIND the player (1–2 m away), very quiet but closer
            if (Random.value < 0.25f)
            {
                var back = Quaternion.Euler(0f, Random.Range(-40f, 40f), 0f) * -new Vector3(head.forward.x, 0f, head.forward.z).normalized;
                pos = head.position + back * Random.Range(1f, 2.2f) + Vector3.up * Random.Range(-0.3f, 0.2f);
                vol = ambienceVolume * (pick == "whisper" ? 0.55f : 0.7f);
            }
            else
            {
                var dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
                pos = head.position + dir * Random.Range(4f, 9f) + Vector3.up * Random.Range(-1f, 1f);
                vol = ambienceVolume * (pick == "whisper" ? 0.3f : 0.9f);
            }
            At(pick, pos, vol, 25f, 0.1f);
        }

        // phantom footsteps: something approaches from behind, then stops
        if (Time.time > m_NextPhantomApproach && PlayerRig.Head != null)
        {
            m_NextPhantomApproach = Time.time + Random.Range(45f, 90f);
            StartCoroutine(PhantomApproach());
        }

        // false-alarm heartbeat: spike as if something is near, then silence — keeps players paranoid
        if (Time.time > m_NextFalseAlarm)
        {
            m_FalseAlarmUntil = Time.time + Random.Range(5f, 9f);
            m_NextFalseAlarm  = m_FalseAlarmUntil + Random.Range(75f, 150f);
        }

        // the music box plays from the teddy (hint: that's where the tape is)
        if (stage == 3 && Time.time > m_NextMusicBox)
        {
            m_NextMusicBox = Time.time + Random.Range(35f, 55f);
            var tape = FindTape();
            if (tape != null)
                At("musicbox", tape.position, 0.7f, 12f, 0f);
        }

        // chase drone + heartbeat when something is hunting you
        if (Time.time > m_NextDangerCheck)
        {
            m_NextDangerCheck = Time.time + 0.25f; // searching the scene every frame is wasteful on Quest
            m_Danger = HuntDanger();
        }
        var danger = m_Danger;
        // False alarm: heartbeat spikes even when no monster is present — players can't trust silence
        var falseAlarm = Time.time < m_FalseAlarmUntil ? 0.55f : 0f;
        var heartbeat = Mathf.Max(danger, falseAlarm, PlayerHealth.GetHeartbeatIntensity());
        Fade(m_Drone, Mathf.Max(danger, falseAlarm * 0.4f) * 0.6f, 1.5f);
        Fade(m_Heart, Mathf.Clamp01(heartbeat * 1.6f - 0.1f), 1.2f); // louder, responds faster
        if (m_Heart.isPlaying)
            m_Heart.pitch = Mathf.Lerp(0.85f, 1.7f, heartbeat);
    }

    static Transform FindTape()
    {
        foreach (var p in FindObjectsByType<Pickup>(FindObjectsSortMode.None))
            if (p.itemId == "vhs")
                return p.transform;
        return null;
    }

    static float HuntDanger()
    {
        var best = 0f;
        foreach (var c in FindObjectsByType<CrawlerEntity>(FindObjectsSortMode.None))
            if (c.IsChasing)
                best = Mathf.Max(best, Mathf.InverseLerp(14f, 2f, c.DistanceToPlayer));
        foreach (var m in FindObjectsByType<MotherEntity>(FindObjectsSortMode.None))
            if (m.IsHunting)
                best = Mathf.Max(best, Mathf.InverseLerp(10f, 1.5f, m.DistanceToPlayer));
        return best;
    }

    static void Fade(AudioSource s, float target, float speed)
    {
        if (s == null || s.clip == null)
            return;
        s.volume = Mathf.MoveTowards(s.volume, target, speed * Time.deltaTime);
        if (s.volume > 0.001f && !s.isPlaying)
            s.Play();
        else if (s.volume <= 0.001f && s.isPlaying)
            s.Stop();
    }

    void EnterStage(int stage)
    {
        m_Stage = stage;
        StopAllCoroutines();
        StartCoroutine(CrossfadeBed(Clip(k_StageBeds[Mathf.Clamp(stage - 1, 0, 2)])));
        m_NextMusicBox = Time.time + 10f;
        DecorateStage(stage);
    }

    IEnumerator CrossfadeBed(AudioClip clip)
    {
        var from = m_BedA.isPlaying ? m_BedA : m_BedB;
        var to = from == m_BedA ? m_BedB : m_BedA;
        to.clip = clip;
        to.volume = 0f;
        if (clip != null)
            to.Play();
        var startFrom = from.volume;
        for (var t = 0f; t < 3f; t += Time.deltaTime)
        {
            to.volume = Mathf.Lerp(0f, ambienceVolume * master, t / 3f);
            from.volume = Mathf.Lerp(startFrom, 0f, t / 3f);
            yield return null;
        }
        from.Stop();
        to.volume = ambienceVolume * master;
    }

    /// <summary>Gives the stage's objects their own sounds: ticking clocks and buzzing lamps.</summary>
    void DecorateStage(int stage)
    {
        var flow = GameFlow.Instance;
        if (flow == null || flow.stageRoots == null || stage - 1 >= flow.stageRoots.Length)
            return;
        var root = flow.stageRoots[stage - 1];
        if (root == null || !m_Decorated.Add(root))
            return;
        foreach (var t in root.GetComponentsInChildren<Transform>())
        {
            var n = t.name.ToLowerInvariant();
            if (n.Contains("clock") && !n.StartsWith("trigger") && t.parent != null && !t.parent.name.ToLowerInvariant().Contains("clock"))
                Loop("clock_tick", t, 0.5f, 6f);
        }
        var hums = 0;
        foreach (var l in root.GetComponentsInChildren<Light>())
        {
            if (hums >= 4 || l.type == LightType.Directional || l.GetComponentInParent<PSXFlashlight>() != null)
                continue;
            if (l.GetComponent<FlickerLight>() != null || l.GetComponentInParent<FlickerLight>() != null)
            {
                LightHum.Attach(l, 0.35f);
                hums++;
            }
        }
    }

    // Four footsteps approach from behind the player, then a whisper or dead silence.
    // No monster spawns — the imagination does the rest.
    IEnumerator PhantomApproach()
    {
        var head = PlayerRig.Head;
        if (head == null) yield break;

        // Direction: roughly behind the player with a little randomisation
        var back = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f)
                   * -new Vector3(head.forward.x, 0f, head.forward.z).normalized;

        for (int i = 0; i < 4; i++)
        {
            head = PlayerRig.Head;
            if (head == null) yield break;
            // Lerp from ~5 m away down to ~0.8 m (right behind you)
            var dist = Mathf.Lerp(5f, 0.8f, i / 3f);
            var pos  = head.position + back * dist + Vector3.up * Random.Range(-0.15f, 0.1f);
            At("step_wood", pos, 0.9f, 14f, 0.04f);
            yield return new WaitForSeconds(Random.Range(0.45f, 0.75f));
        }

        // Brief pause, then either a whisper right behind the ear or total silence
        yield return new WaitForSeconds(Random.Range(0.2f, 0.6f));
        head = PlayerRig.Head;
        if (head != null && Random.value < 0.65f)
        {
            var earPos = head.position + back * 0.5f + Vector3.up * 0.1f;
            At("whisper", earPos, 0.8f, 5f, 0f);
        }
    }

    /// <summary>Buzzing hum for each of these lamps (used when the basement power comes on).</summary>
    public static void HumLights(IEnumerable<Light> lights, int max = 4)
    {
        var n = 0;
        foreach (var l in lights)
        {
            if (l == null || n >= max)
                continue;
            LightHum.Attach(l, 0.3f);
            n++;
        }
    }
}
