using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The Mother: wakes up after the tape plays. She teleports instantly when your torch isn't on her
/// (Weeping-Angel style) and stalks you with audio even while frozen. Keep the beam steady — but
/// the batteries are dying.
///
/// Stage 3 mode (ActivateStage3): torch only SLOWS her, never stops her. Speed ramps up over 45 s.
/// She forces a torch flicker and teleports closer every 5–8 s regardless.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class MotherEntity : EntityBase
{
    public float speed = 1.5f;
    [Tooltip("Speed when your torch is off (NavMesh movement between teleports).")]
    public float darkSpeed = 3.5f;
    public float catchDistance = 0.85f;

    [Tooltip("Stage 3: the VCR/TV — used for the tape-plays intro tentacle burst.")]
    public Transform tvTransform;

    NavMeshAgent m_Agent;
    Renderer[] m_Renderers;
    bool m_Active, m_Busy;
    float m_LastStep;

    // teleport timing
    float m_LastTeleport;
    float m_TeleportInterval = 2f;
    float m_DarkDuration;

    // torch-freeze tracking
    float m_LitDuration;

    // audio harassment
    float m_NextHarass;

    // Stage 3 mode
    bool m_Stage3Mode;
    float m_Stage3Elapsed;
    float m_LastBreakFree;

    bool m_Teleporting;

    static MotherEntity s_Live;

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { s_Live = null; }

    public bool IsHunting => m_Active && !m_Busy && !m_Frozen && isActiveAndEnabled;

    protected override void Awake()
    {
        // Destroy any accidental duplicate — only one live Mother at a time
        if (s_Live != null && s_Live != this)
        {
            Destroy(gameObject);
            return;
        }
        s_Live = this;
        base.Awake();
        m_Agent = GetComponent<NavMeshAgent>();
        m_Renderers = GetComponentsInChildren<Renderer>(true);
        SetVisible(false);
    }

    void SetVisible(bool v)
    {
        foreach (var r in m_Renderers)
            r.enabled = v;
    }

    /// <summary>Starts the finale: torch dies for 2 seconds; when it comes back she's right in front of you.</summary>
    public void Activate()
    {
        if (!m_Active)
            StartCoroutine(Intro());
    }

    /// <summary>
    /// Stage 3 activation: tape inserted in VCR. Tentacles from the TV, then she walks into the
    /// carriage. Torch only slows her now — it never freezes. Speed ramps up; she breaks free on
    /// a timer regardless of the torch.
    /// </summary>
    public void ActivateStage3()
    {
        if (m_Active) return;
        StartCoroutine(Stage3Intro());
    }

    IEnumerator Stage3Intro()
    {
        m_Active = true;
        m_Busy   = true;

        // --- Tape plays: TV goes berserk ---
        var head = PlayerRig.Head;
        ScareDirector.KillLights(head != null ? head.position : Vector3.zero, 22f, 3.5f);
        ScareDirector.Duck(5f, 0.02f);
        GameAudio.Flat("static_burst", 1f);

        // Hand/tentacles crawl out of the TV screen
        if (tvTransform != null)
            ScareDirector.TentacleBurst(tvTransform.position, tvTransform.forward, 1.8f, 1.5f);

        yield return new WaitForSeconds(2.2f);

        // Mother materialises deep in the carriage, facing the player
        head = PlayerRig.Head;
        var behind = head != null ? -head.forward : Vector3.back;
        behind.y = 0f;
        var spawnPos = PlayerRig.Feet + behind.normalized * 9f;
        if (!SampleNavMesh(spawnPos, out spawnPos, 5f))
            spawnPos = transform.position;
        if (m_Agent.isOnNavMesh)
            m_Agent.Warp(spawnPos);
        var toPlayer = PlayerRig.Feet - spawnPos;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(toPlayer.normalized);
        SetVisible(true);
        Play("Idle", 1f, 0.01f);
        GameAudio.At("distant_creak", spawnPos, 1f, 20f, 0f);

        yield return new WaitForSeconds(1.5f);
        Narrator.SayOnce("N3_03");
        yield return new WaitForSeconds(2.5f);

        // Torch now stutters more — signal to the player the rules have changed
        var torch = Torch;
        if (torch != null) torch.stutterEvery = 4f;
        Narrator.SayOnce("N3_04");

        m_Stage3Elapsed  = 0f;
        m_LastBreakFree  = Time.time + Random.Range(7f, 10f);
        m_NextHarass     = Time.time + Random.Range(4f, 8f);
        m_Busy           = false;
        m_Stage3Mode     = true;
    }

    void Stage3Update()
    {
        if (!m_Agent.isOnNavMesh) return;

        m_Stage3Elapsed += Time.deltaTime;

        // Whisper harassment
        if (Time.time > m_NextHarass && PlayerRig.Head != null)
        {
            m_NextHarass = Time.time + Random.Range(5f, 10f);
            var h    = PlayerRig.Head;
            var side = (Random.value < 0.5f ? h.right : -h.right) * Random.Range(0.2f, 0.4f);
            GameAudio.At(Random.value < 0.6f ? "whisper" : "distant_creak",
                         h.position + side + Vector3.up * Random.Range(-0.1f, 0.1f), 0.75f, 6f, 0f);
        }

        // Speed ramps from speed → darkSpeed over 45 s
        var ramp      = Mathf.Lerp(speed, darkSpeed, Mathf.Clamp01(m_Stage3Elapsed / 45f));
        var lit       = IsLitByTorch(transform.position + Vector3.up * 1.3f);
        m_Agent.speed = lit ? ramp * 0.3f : ramp;
        m_Agent.isStopped = false;
        SetAnimSpeed(1f);
        m_Agent.SetDestination(PlayerRig.Feet);
        Play(m_Agent.velocity.magnitude > 0.1f ? "Walk" : "Idle");

        if (m_Agent.velocity.magnitude > 0.1f && Time.time - m_LastStep > 0.9f / Mathf.Max(0.5f, m_Agent.velocity.magnitude))
        {
            m_LastStep = Time.time;
            GameAudio.At(Random.value < 0.3f ? "creak_floor" : "step_wood", transform.position, 0.65f, 14f, 0.15f);
        }

        // Break-free: forces a torch flicker, teleports closer — happens regardless of torch state
        if (Time.time > m_LastBreakFree)
        {
            m_LastBreakFree = Time.time + Random.Range(5f, 8f);
            var torch = Torch;
            if (torch != null) torch.ForceBlackout(0.6f);
            TeleportCloser(1.5f, 2.5f);
            GameAudio.Flat("static_burst", 0.35f);
        }

        if (DistanceToPlayer < catchDistance)
            StartCoroutine(Catch());
    }

    IEnumerator Intro()
    {
        m_Active = true;
        m_Busy = true;
        var torch = Torch;
        if (torch != null)
            torch.ForceBlackout(2.2f);
        yield return new WaitForSeconds(1.2f);
        ScareDirector.InFace(Monster.Mother, 1.1f);
        yield return new WaitForSeconds(2.2f);

        var head = PlayerRig.Head;
        var fwd  = head.forward;
        fwd.y = 0f;
        var spot  = PlayerRig.Feet + fwd.normalized * 3.5f;
        var found = false;
        foreach (var dist in new[] { 3.5f, 2.5f, 4.5f })
        {
            foreach (var dir in new[] { fwd.normalized, Vector3.forward, Vector3.back, -fwd.normalized, Vector3.right, Vector3.left })
            {
                var c = PlayerRig.Feet + dir * dist;
                if (!NavMesh.SamplePosition(c, out var hit, 0.6f, NavMesh.AllAreas))
                    continue;
                if (Physics.Linecast(head.position, hit.position + Vector3.up * 1.4f, out var block, ~0, QueryTriggerInteraction.Ignore)
                    && !block.transform.IsChildOf(transform))
                    continue;
                spot  = hit.position;
                found = true;
                break;
            }
            if (found) break;
        }
        if (found && m_Agent.isOnNavMesh)
            m_Agent.Warp(spot);
        else if (SampleNavMesh(spot, out spot, 3f) && m_Agent.isOnNavMesh)
            m_Agent.Warp(spot);
        var to = PlayerRig.Feet - transform.position;
        to.y = 0f;
        transform.rotation = Quaternion.LookRotation(to);
        SetVisible(true);
        Play("Idle", 1f, 0.01f);
        yield return new WaitForSeconds(1.0f);
        Narrator.SayOnce("N3_03");
        yield return new WaitForSeconds(2.5f);
        if (torch != null)
            torch.stutterEvery = 7f;
        Narrator.SayOnce("N3_04");
        Narrator.SayOnce("N3_05");
        m_NextHarass  = Time.time + Random.Range(5f, 10f);
        m_LastTeleport = Time.time + 3f; // brief grace period after intro
        m_Busy = false;
    }

    void Update()
    {
        if (!m_Active || m_Busy || m_Frozen || !m_Agent.isOnNavMesh)
            return;

        if (m_Stage3Mode)
        {
            Stage3Update();
            return;
        }

        // Audio harassment: she whispers near you regardless of whether she's frozen
        if (Time.time > m_NextHarass && PlayerRig.Head != null)
        {
            m_NextHarass = Time.time + Random.Range(7f, 14f);
            var head = PlayerRig.Head;
            var side  = (Random.value < 0.5f ? head.right : -head.right) * Random.Range(0.2f, 0.4f);
            var harassPos = head.position + side + Vector3.up * Random.Range(-0.15f, 0.1f);
            GameAudio.At(Random.value < 0.55f ? "whisper" : "distant_creak", harassPos, 0.75f, 6f, 0f);
        }

        var lit = IsLitByTorch(transform.position + Vector3.up * 1.3f);

        if (lit)
        {
            m_LitDuration  += Time.deltaTime;
            m_DarkDuration  = 0f;
            m_Agent.isStopped = true;
            SetAnimSpeed(0f);

            // After 10 s of being held at bay the torch starts to lose its grip (rare break-free)
            if (m_LitDuration > 10f && Random.value < 0.02f * Time.deltaTime * 60f)
            {
                var torch = Torch;
                if (torch != null) torch.ForceBlackout(0.25f);
                TeleportCloser(1.2f, 2.0f);
                GameAudio.Flat("static_burst", 0.35f);
            }
            return;
        }

        // Dark
        m_LitDuration  = 0f;
        m_DarkDuration += Time.deltaTime;

        // Teleport closer every 1.5–2.8 s while the torch is off
        if (m_DarkDuration > 0.3f && Time.time > m_LastTeleport + m_TeleportInterval)
        {
            m_LastTeleport     = Time.time;
            m_TeleportInterval = Random.Range(1.5f, 2.8f);
            TeleportCloser(1.5f, 2.8f);
        }

        // Also advance via NavMesh so she keeps pressure on even when teleport can't find a spot
        SetAnimSpeed(1f);
        m_Agent.isStopped = false;
        var torchComp = Torch;
        m_Agent.speed = torchComp != null && torchComp.IsOn ? speed : darkSpeed;
        m_Agent.SetDestination(PlayerRig.Feet);
        Play(m_Agent.velocity.magnitude > 0.1f ? "Walk" : "Idle");
        if (m_Agent.velocity.magnitude > 0.1f && Time.time - m_LastStep > 0.9f / Mathf.Max(0.5f, m_Agent.velocity.magnitude))
        {
            m_LastStep = Time.time;
            GameAudio.At(Random.value < 0.3f ? "creak_floor" : "step_wood", transform.position, 0.65f, 14f, 0.15f);
        }
        if (DistanceToPlayer < catchDistance)
            StartCoroutine(Catch());
    }

    void TeleportCloser(float minStep, float maxStep)
    {
        if (m_Teleporting) return;
        var playerFeet = PlayerRig.Feet;
        var fromPlayer = transform.position - playerFeet;
        fromPlayer.y = 0f;
        var currentDist = fromPlayer.magnitude;
        var step        = Random.Range(minStep, maxStep);
        var newDist     = Mathf.Max(1.0f, currentDist - step);
        var dir         = fromPlayer.sqrMagnitude > 0.01f ? fromPlayer.normalized : Vector3.back;
        var targetPos   = playerFeet + dir * newDist;
        if (!SampleNavMesh(targetPos, out var navPos, 2f))
            return;
        StartCoroutine(FlashTeleport(navPos));
    }

    IEnumerator FlashTeleport(Vector3 pos)
    {
        m_Teleporting = true;
        SetVisible(false);
        GameAudio.At("static_burst", transform.position, 0.3f, 8f, 0f);
        yield return null;
        if (m_Agent.isOnNavMesh)
            m_Agent.Warp(pos);
        else
            transform.position = pos;
        var to = PlayerRig.Feet - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(to);
        yield return null;
        SetVisible(true);
        m_Teleporting = false;
    }

    IEnumerator Catch()
    {
        m_Busy = true;
        if (m_Agent.isOnNavMesh)
        {
            m_Agent.isStopped = true;
            m_Agent.ResetPath();
        }
        SetVisible(false);
        // Instant: she's already touching you, so no build-up silence. Stay hidden until it's over.
        yield return ScareDirector.InFace(Monster.Mother, 1.0f, true);
        var away = transform.position;
        var best = 0f;
        for (var i = 0; i < 12; i++)
        {
            var p = PlayerRig.Feet + Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward * 6f;
            if (SampleNavMesh(p, out p, 2f))
            {
                var d = Vector3.Distance(p, PlayerRig.Feet);
                if (d > best) { best = d; away = p; }
            }
        }
        if (m_Agent.isOnNavMesh)
            m_Agent.Warp(away);
        SetVisible(true);
        yield return new WaitForSeconds(4f);
        m_Busy = false;
    }

    public override void ResetEntity()
    {
        StopAllCoroutines();
        m_Busy           = false;
        m_DarkDuration   = 0f;
        m_LitDuration    = 0f;
        m_LastTeleport   = 0f;
        m_NextHarass     = 0f;
        m_Stage3Mode     = false;
        m_Stage3Elapsed  = 0f;
        m_LastBreakFree  = 0f;
        m_Teleporting    = false;
        base.ResetEntity();
    }

    protected override void OnDestroy()
    {
        if (s_Live == this) s_Live = null;
        base.OnDestroy();
    }
}
