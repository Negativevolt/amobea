using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public enum Monster { Watcher, Crawler, Mother, Tentacle }

/// <summary>
/// Runs the jump scares. Monsters never kill you: they lunge into your face instead.
///   InFace      - the monster rushes from ~1.5 m to right in front of your eyes, twitching, screaming, lit from below,
///                 and stays glued to your face for a moment (turning your head doesn't help), then cut to black.
///   LookBehind  - it silently stands right behind you and whispers. Turn around and it lunges. Don't, and it's gone.
///   FigureCross - it walks across a corridor in the distance.
///   Appear      - it just stands somewhere for a moment, staring, then vanishes.
/// Uses copies of the monster models kept in "Scare Templates" (built by Tools > Horror Game > Build Game).
/// Controllers rumble in VR; the camera shakes in PC test mode.
/// </summary>
public class ScareDirector : MonoBehaviour
{
    [Tooltip("Inactive copies of each monster's model (index = Monster enum).")]
    public GameObject[] templates = new GameObject[3];

    static ScareDirector s_Instance;
    public static bool Busy { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { s_Instance = null; Busy = false; }

    public static ScareDirector Instance
    {
        get
        {
            if (s_Instance == null)
                s_Instance = FindAnyObjectByType<ScareDirector>();
            if (s_Instance == null)
                s_Instance = new GameObject("Scare Director").AddComponent<ScareDirector>();
            return s_Instance;
        }
    }

    void Awake()
    {
        s_Instance = this;
        Busy = false;
    }

    // ------------------------------------------------------------------ public API
    /// <param name="instant">Skip the build-up silence (use when the monster has just physically reached the player).</param>
    public static Coroutine InFace(Monster m, float hold = 0.9f, bool instant = false) => Instance.StartCoroutine(Instance.FaceRoutine(m, hold, null, instant));

    public static void LookBehind(Monster m, float timeout = 7f) => Instance.StartCoroutine(Instance.BehindRoutine(m, timeout));

    public static void FigureCross(Monster m, Vector3 from, Vector3 to, float speed = 2.2f) => Instance.StartCoroutine(Instance.CrossRoutine(m, from, to, speed));

    public static void Appear(Monster m, Vector3 at, float seconds = 1.5f) => Instance.StartCoroutine(Instance.AppearRoutine(m, at, seconds));

    /// <summary>Tentacles burst out of something (a wall, a TV, a window, the ceiling), lash at you, then pull back.</summary>
    public static void TentacleBurst(Vector3 at, Vector3 dir, float seconds = 1.6f, float maxReach = 1.6f)
        => Instance.StartCoroutine(Instance.BurstRoutine(at, dir, seconds, maxReach));

    /// <summary>Rumble both VR controllers.</summary>
    public static void Haptics(float amplitude, float seconds)
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, devices);
        foreach (var d in devices)
            if (d.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                d.SendHapticImpulse(0, amplitude, seconds);
    }

    // ------------------------------------------------------------------ models
    static readonly string[] k_Scream = { "watcher_vanish", "crawler_screech", "mother_scream", "crawler_screech" };
    static readonly string[] k_LungeClip = { "Reach", "Lunge", "Reach", "-" };
    // where the face is, as a fraction of the model's height
    static readonly float[] k_FaceHeight = { 0.9f, 0.62f, 0.88f, 0.5f };
    static readonly Dictionary<int, float> s_FaceY = new Dictionary<int, float>();

    Transform Spawn(Monster m)
    {
        if (m == Monster.Tentacle)
            return TentacleBeast.Create(true).transform;
        var template = templates != null && (int)m < templates.Length ? templates[(int)m] : null;
        if (template == null)
        {
            // fall back to a monster in the level (even an inactive one)
            foreach (var e in FindObjectsByType<EntityBase>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var ok = (m == Monster.Watcher && e is WatcherEntity) || (m == Monster.Crawler && e is CrawlerEntity) || (m == Monster.Mother && e is MotherEntity);
                var model = ok ? e.transform.Find("Model") : null;
                if (model == null)
                    continue;
                var root = new GameObject("Scare " + m).transform;
                var copy = Instantiate(model.gameObject, root);
                copy.transform.localPosition = model.localPosition;
                copy.transform.localRotation = model.localRotation;
                return Prepare(root, m);
            }
            return null;
        }
        var go = Instantiate(template);
        go.name = "Scare " + m;
        go.transform.SetParent(null, false);
        go.SetActive(true);
        return Prepare(go.transform, m);
    }

    static Transform Prepare(Transform root, Monster m)
    {
        root.gameObject.SetActive(true);
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            r.enabled = true;
            r.gameObject.SetActive(true);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (var mat in r.materials)
                if (mat.name.Contains("eyes") || mat.name.Contains("mouth"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", mat.name.Contains("eyes") ? Color.white * 4f : new Color(0.6f, 0.05f, 0.05f) * 2f);
                }
        }
        foreach (var c in root.GetComponentsInChildren<Collider>(true))
            c.enabled = false;
        var anim = root.GetComponentInChildren<Animation>(true);
        if (anim != null)
            anim.playAutomatically = false;
        return root;
    }

    static bool PlayClip(Transform root, string key, float speed, WrapMode wrap)
    {
        var anim = root != null ? root.GetComponentInChildren<Animation>() : null;
        if (anim == null)
            return false;
        foreach (AnimationState s in anim)
        {
            if (!s.name.Contains(key))
                continue;
            s.speed = speed;
            s.wrapMode = wrap;
            anim.Play(s.name);
            return true;
        }
        return false;
    }

    /// <summary>Where the monster's face is right now (follows the head bone through the animation).</summary>
    static Vector3 FacePoint(Transform root, Monster m)
    {
        var beast = root.GetComponent<TentacleBeast>();
        if (beast != null)
            return beast.FaceWorld;

        // Mother: her "head" bone isn't where the visible head is, so measure the face height from her mesh once
        // (rest pose) and keep it fixed. The point must move with the model — the lunge loop shifts the model
        // until this point sits in front of the eyes. Nudged forward so her face stops just short of the camera.
        if (m == Monster.Mother)
        {
            var id = root.GetInstanceID();
            if (!s_FaceY.TryGetValue(id, out var faceY))
            {
                if (s_FaceY.Count > 32) s_FaceY.Clear();
                faceY = LocalFace(root, m).y;
                s_FaceY[id] = faceY;
            }
            return root.TransformPoint(new Vector3(0f, faceY, 0.18f));
        }

        Transform head = null;
        foreach (var t in root.GetComponentsInChildren<Transform>())
            if (t.name == "head") { head = t; break; }
        if (head == null)
            return root.TransformPoint(LocalFace(root, m));
        // the head bone starts at the neck; the eyes sit a little further along it
        return m == Monster.Crawler
            ? head.position + root.forward * 0.15f + root.up * 0.02f
            : head.position + root.up * 0.17f + root.forward * 0.06f;
    }

    /// <summary>Face point of the model in the root's local space (measured once, in its rest pose).</summary>
    static Vector3 LocalFace(Transform root, Monster m)
    {
        var rs = root.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0)
            return Vector3.up * 1.6f;
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        var min = root.InverseTransformPoint(b.min);
        var max = root.InverseTransformPoint(b.max);
        var lo = Vector3.Min(min, max);
        var hi = Vector3.Max(min, max);
        var y = lo.y + (hi.y - lo.y) * k_FaceHeight[(int)m];
        var z = m == Monster.Crawler ? hi.z * 0.8f : hi.z * 0.35f; // the Crawler's face is at its front end
        return new Vector3(0f, y, z);
    }

    // ------------------------------------------------------------------ intensity helpers
    /// <summary>Everything else goes quiet (ambience, hums, drones) so the scare hits harder.</summary>
    public static void Duck(float seconds, float level = 0.08f) => Instance.StartCoroutine(Instance.DuckRoutine(seconds, level));

    IEnumerator DuckRoutine(float seconds, float level)
    {
        var ducked = new List<(AudioSource, float)>();
        foreach (var a in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            if (a != null && a.loop && a.isPlaying && a.volume > 0.01f)
            {
                ducked.Add((a, a.volume));
                a.volume *= level;
            }
        yield return new WaitForSeconds(seconds);
        for (var t = 0f; t < 1.5f; t += Time.deltaTime)
        {
            foreach (var (a, v) in ducked)
                if (a != null) a.volume = Mathf.Lerp(v * level, v, t / 1.5f);
            yield return null;
        }
        foreach (var (a, v) in ducked)
            if (a != null) a.volume = v;
    }

    /// <summary>Lamps around a point go dark for a moment.</summary>
    public static void KillLights(Vector3 at, float radius, float seconds)
    {
        foreach (var l in FindObjectsByType<FlickerLight>(FindObjectsSortMode.None))
            if (l != null && Vector3.Distance(l.transform.position, at) < radius)
                l.ForceBlackout(seconds);
    }

    IEnumerator HapticPulses(float seconds, float strength)
    {
        for (var t = 0f; t < seconds; t += 0.14f)
        {
            Haptics(strength * Random.Range(0.7f, 1f), 0.09f);
            yield return new WaitForSeconds(0.14f);
        }
    }

    /// <summary>Angle between where the player looks and a point.</summary>
    static float LookAngle(Vector3 at)
    {
        var head = PlayerRig.Head;
        return head == null ? 180f : Vector3.Angle(head.forward, at - head.position);
    }

    // ------------------------------------------------------------------ in your face
    IEnumerator FaceRoutine(Monster m, float hold, Transform existing, bool instant = false)
    {
        var waited = 0f;
        while (Busy && existing == null && waited < 4f)
        {
            waited += Time.deltaTime;
            yield return null;
        }
        if (Busy && existing == null)
            yield break;
        Busy = true;
        var head = PlayerRig.Head;

        // Dead quiet before a face scare is half the fear, but if it ALWAYS comes first, players learn that silence
        // means "brace now" (playtests: nobody jumped). So it's a coin flip: sometimes a long silence, sometimes none.
        if (existing == null && !instant && Random.value < 0.6f)
        {
            var calm = Random.Range(0.8f, 3.5f);
            Duck(calm + 0.4f, 0.03f);
            yield return new WaitForSeconds(calm);
        }

        var root = existing != null ? existing : Spawn(m);
        if (head == null || root == null)
        {
            GameAudio.Flat("stinger_face", 1f);
            Busy = false;
            if (root != null) Destroy(root.gameObject);
            yield break;
        }

        PlayClip(root, k_LungeClip[(int)m], 2.6f, WrapMode.ClampForever);
        hold *= Random.Range(0.8f, 1.35f);
        var lunge = Random.Range(0.07f, 0.14f);
        var total = lunge + hold;

        // only its face is lit: torch and nearby lamps die, everything else goes silent
        var torch = EntityBase.Torch;
        if (torch != null) torch.ForceBlackout(total + 0.15f);
        KillLights(head.position, 10f, total + 0.4f);
        Duck(total + 1.2f, 0.05f);
        var lamp = new GameObject("Face Light").AddComponent<Light>();
        lamp.type = LightType.Point;
        lamp.range = 2.5f;
        lamp.color = new Color(1f, 0.9f, 0.82f);
        lamp.shadows = LightShadows.None;

        GameAudio.Flat("stinger_face", 1f);
        GameAudio.Flat("stinger_jump", 1f);
        GameAudio.Flat(k_Scream[(int)m], 1f);
        GameAudio.Flat("static_burst", 0.6f);
        Haptics(1f, 0.5f);
        StartCoroutine(HapticPulses(total, 0.9f));
        DesktopPlayer.Shake(0.16f, total + 0.3f);
        DesktopPlayer.Punch(22f, total + 0.4f); // on a monitor: the view lurches in at it
        PlayerHealth.TakeScareDamage(); // blood flash + health hit

        var beast = root.GetComponent<TentacleBeast>();
        if (beast != null)
        {
            beast.target = head;
            beast.wrapAround = true;
            beast.writhe = 3.2f;
        }
        // sometimes it stops just short, twitching... then snaps the rest of the way
        var stutter = Random.value < 0.45f;
        var pause = stutter ? Random.Range(0.2f, 0.4f) : 0f;
        total += pause;
        var t = 0f;
        var jitter = Vector3.zero;
        var start = Random.Range(1.8f, 2.6f);
        while (t < total)
        {
            t += Time.deltaTime;
            float dist;
            if (stutter && t < lunge + pause)
            {
                var k1 = Mathf.Clamp01(t / lunge);
                dist = Mathf.Lerp(start, 0.75f, k1 * k1);
            }
            else
            {
                var k2 = Mathf.Clamp01((t - (stutter ? lunge + pause : 0f)) / (stutter ? 0.06f : lunge));
                dist = Mathf.Lerp(stutter ? 0.75f : start, 0.22f, k2 * k2);
                if (stutter && k2 > 0f && k2 < 0.2f) { GameAudio.Flat("stinger_jump", 1f); Haptics(1f, 0.3f); }
            }
            var k = Mathf.Clamp01(t / lunge);
            if (beast != null)
            {
                beast.reach = Mathf.Clamp01(k * 1.2f);
                beast.mouthOpen = Mathf.Clamp01(1.1f - dist);
            }
            if (t > lunge && Random.value < 0.4f)
                jitter = Random.insideUnitSphere * 0.03f; // twitching
            // stays glued in front of your eyes, wherever you look
            var fwd = head.forward;
            var flat = new Vector3(fwd.x, 0f, fwd.z);
            if (flat.sqrMagnitude < 0.01f) flat = head.up;
            var twitch = t > lunge ? 1f : 0f;
            root.rotation = Quaternion.LookRotation(-flat.normalized) * Quaternion.Euler(Random.Range(-4f, 4f) * twitch, Random.Range(-3f, 3f) * twitch, Random.Range(-6f, 6f) * twitch);
            var target = head.position + fwd * dist + jitter;
            root.position += target - FacePoint(root, m);
            lamp.transform.position = head.position + fwd * (dist * 0.5f) - Vector3.up * 0.35f;
            lamp.intensity = Random.value < 0.1f ? 0.3f : Random.Range(2.5f, 6f); // stuttering under-light
            yield return null;
        }

        yield return ScreenFade.FadeTo(1f, 0.03f);
        Destroy(root.gameObject);
        Destroy(lamp.gameObject);
        yield return new WaitForSeconds(Random.Range(0.25f, 0.8f));
        yield return ScreenFade.FadeTo(0f, 0.6f);
        Busy = false;
    }

    // ------------------------------------------------------------------ right behind you
    IEnumerator BehindRoutine(Monster m, float timeout)
    {
        var head = PlayerRig.Head;
        if (head == null || Busy)
            yield break;
        var root = Spawn(m);
        if (root == null)
            yield break;
        // behind you, a little to one side (never quite the same spot)
        var back = Quaternion.Euler(0f, Random.Range(-45f, 45f), 0f) * -new Vector3(head.forward.x, 0f, head.forward.z).normalized;
        var dist = Random.Range(0.8f, 1.2f);
        if (Physics.Raycast(head.position, back, out var hit, 1.6f, ~0, QueryTriggerInteraction.Ignore))
            dist = Mathf.Max(0.55f, hit.distance - 0.4f);
        root.position = PlayerRig.Feet + back * dist;
        root.rotation = Quaternion.LookRotation(-back);
        var behindBeast = root.GetComponent<TentacleBeast>();
        if (behindBeast != null)
        {
            root.position = new Vector3(root.position.x, head.position.y - 0.05f, root.position.z);
            behindBeast.writhe = 0.6f; // slow, patient
        }
        PlayClip(root, "Idle", 1f, WrapMode.Loop);
        var face = FacePoint(root, m);
        Duck(timeout, 0.25f);
        GameAudio.At("whisper", face, 1f, 6f);
        var breath = GameAudio.At("crawler_breath", face, 0.7f, 4f);
        var nextWhisper = Random.Range(1.6f, 2.6f);

        var t = 0f;
        while (t < timeout)
        {
            t += Time.deltaTime;
            face = FacePoint(root, m);
            if (t > nextWhisper)
            {
                GameAudio.At("whisper", face, 1f, 6f);
                nextWhisper = t + Random.Range(1.8f, 3f);
            }
            var body = root.position + Vector3.up * (m == Monster.Crawler ? 0.4f : m == Monster.Tentacle ? 0f : 1.2f);
            var to = body - head.position;
            to.y *= 0.3f; // it's right behind you: turning towards it is enough
            if (Vector3.Angle(head.forward, to) < 60f)
            {
                if (breath != null) breath.Stop();
                if (Random.value < 0.7f)
                {
                    // you turned around.
                    yield return FaceRoutine(m, 0.8f, root);
                    yield break;
                }
                // ...nothing there. Then it's in front of you when you turn back.
                Destroy(root.gameObject);
                GameAudio.At("watcher_vanish", face, 0.4f, 8f);
                yield return new WaitForSeconds(Random.Range(1.2f, 2.6f));
                yield return FaceRoutine(m, 0.9f, null);
                yield break;
            }
            yield return null;
        }
        // you never turned: it touches you, then it's gone
        if (breath != null) breath.Stop();
        Haptics(0.7f, 0.35f);
        GameAudio.At("whisper", head.position + head.right * 0.25f, 1f, 3f);
        GameAudio.At("watcher_vanish", face, 0.5f, 8f);
        Destroy(root.gameObject);
    }

    // ------------------------------------------------------------------ distant sightings
    IEnumerator CrossRoutine(Monster m, Vector3 from, Vector3 to, float speed)
    {
        var root = Spawn(m);
        if (root == null)
            yield break;
        speed *= Random.Range(0.8f, 1.2f);
        var dir = to - from;
        dir.y = 0f;
        var walkRot = Quaternion.LookRotation(dir.sqrMagnitude > 0.01f ? dir : Vector3.forward);
        root.position = from;
        root.rotation = walkRot;
        var walk = m == Monster.Crawler ? "Crawl" : "Walk";
        PlayClip(root, walk, speed / 1.2f, WrapMode.Loop);
        var length = Vector3.Distance(from, to);
        var stopAt = Random.value < 0.45f ? Random.Range(0.35f, 0.65f) * length : -1f; // sometimes it stops and stares
        var seen = false;
        var nextStep = 0f;
        for (var d = 0f; d < length; d += speed * Time.deltaTime)
        {
            root.position = Vector3.Lerp(from, to, d / Mathf.Max(0.01f, length));
            if (d >= nextStep)
            {
                GameAudio.At(m == Monster.Crawler ? "crawler_skitter" : "step_wood", root.position, 0.8f, 15f);
                nextStep = d + 0.65f;
            }
            if (!seen && LookAngle(root.position + Vector3.up * 1.2f) < 30f)
            {
                seen = true;
                GameAudio.Flat("stinger_jump", Random.Range(0.6f, 0.9f));
                Haptics(0.5f, 0.2f);
            }
            if (stopAt > 0f && d >= stopAt)
            {
                stopAt = -1f;
                // it stops... turns its head to you... then runs
                PlayClip(root, "Idle", 1f, WrapMode.Loop);
                var head = PlayerRig.Head;
                if (head != null)
                {
                    var look = head.position - root.position;
                    look.y = 0f;
                    for (var r = 0f; r < 0.5f; r += Time.deltaTime)
                    {
                        root.rotation = Quaternion.Slerp(walkRot, Quaternion.LookRotation(look), r / 0.5f);
                        yield return null;
                    }
                }
                GameAudio.Flat("stinger_jump", 0.9f);
                Haptics(0.8f, 0.3f);
                yield return new WaitForSeconds(Random.Range(0.8f, 1.6f));
                root.rotation = walkRot;
                speed *= 2.6f;
                if (!PlayClip(root, m == Monster.Crawler ? "Crawl" : "Run", speed / 1.2f, WrapMode.Loop)) PlayClip(root, walk, speed / 1.2f, WrapMode.Loop);
            }
            yield return null;
        }
        Destroy(root.gameObject);
    }

    IEnumerator AppearRoutine(Monster m, Vector3 at, float seconds)
    {
        var root = Spawn(m);
        if (root == null)
            yield break;
        seconds *= Random.Range(0.75f, 1.3f);
        root.position = at;
        var head = PlayerRig.Head;
        if (head != null)
        {
            var to = head.position - at;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f) root.rotation = Quaternion.LookRotation(to);
        }
        PlayClip(root, "Idle", 1f, WrapMode.Loop);
        var eye = at + Vector3.up * (m == Monster.Crawler ? 0.4f : 1.5f);
        var seen = false;
        var stare = 0f;
        for (var t = 0f; t < seconds || (!seen && t < seconds + 4f); t += Time.deltaTime)
        {
            var angle = LookAngle(eye);
            if (!seen && angle < 35f)
            {
                // the sting hits the moment YOU notice it, not when it appears
                seen = true;
                GameAudio.Flat("stinger_jump", Random.Range(0.75f, 1f));
                Haptics(0.7f, 0.25f);
                KillLights(at, 4f, 0.15f);
            }
            stare = angle < 12f ? stare + Time.deltaTime : 0f;
            if (stare > 0.6f)
            {
                if (Random.value < 0.4f && !Busy)
                {
                    // you stared too long
                    yield return FaceRoutine(m, 0.8f, root);
                    yield break;
                }
                break; // gone the instant you focus on it
            }
            yield return null;
        }
        GameAudio.At("watcher_vanish", eye, 0.7f, 12f);
        GameAudio.At("static_burst", eye, 0.35f, 8f);
        Destroy(root.gameObject);
    }

    // ------------------------------------------------------------------ tentacles out of something
    IEnumerator BurstRoutine(Vector3 at, Vector3 dir, float seconds, float maxReach)
    {
        var head = PlayerRig.Head;
        var beast = TentacleBeast.Create(false, 7, 1.5f);
        beast.transform.SetPositionAndRotation(at, Quaternion.LookRotation(dir.sqrMagnitude > 0.001f ? dir : Vector3.forward));
        beast.target = head;
        beast.wrapAround = false;
        beast.maxReach = maxReach;
        beast.extend = 0.02f;
        beast.reach = 0.85f;
        beast.writhe = 3f;
        var near = head != null && Vector3.Distance(head.position, at) < 4f;
        GameAudio.At("crawler_screech", at, 1f, 15f, 0.15f);
        GameAudio.At("crawler_skitter", at, 1f, 12f, 0.2f);
        if (near)
        {
            GameAudio.Flat("stinger_jump", 0.9f);
            Haptics(1f, 0.4f);
            StartCoroutine(HapticPulses(seconds, 0.6f));
            KillLights(at, 6f, 0.25f);
        }
        // out, fast
        for (var t = 0f; t < 0.18f; t += Time.deltaTime)
        {
            beast.extend = Mathf.Lerp(0.02f, 1f, t / 0.18f);
            yield return null;
        }
        beast.extend = 1f;
        // lashing
        for (var t = 0f; t < seconds; t += Time.deltaTime)
        {
            beast.reach = 0.6f + Mathf.Sin(t * 9f) * 0.25f;
            yield return null;
        }
        // and back in, slower
        beast.writhe = 1.4f;
        for (var t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            beast.extend = Mathf.Lerp(1f, 0f, t / 0.6f);
            yield return null;
        }
        Destroy(beast.gameObject);
    }
}
