using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The Crawler: blind, hunts by sound. Patrols the basement, runs to any noise it hears (footsteps when you
/// run, smashed bottles, dropped crates) and chases you if it gets close. Hide in a closet to lose it.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class CrawlerEntity : EntityBase
{
    public Transform[] patrolPoints;
    public float patrolSpeed = 1.1f, searchSpeed = 2.4f, chaseSpeed = 3.3f;
    [Tooltip("It can smell you this close even if you're silent (unless you hide).")]
    public float senseDistance = 2.0f;
    public float catchDistance = 1.0f;
    [Tooltip("Multiplies how far away noises can be heard.")]
    public float hearing = 1f;

    enum State { Patrol, Investigate, Chase, Busy }
    State m_State = State.Patrol;
    NavMeshAgent m_Agent;
    Vector3 m_Target;
    float m_WaitUntil;
    int m_Point;
    State m_LastState = State.Patrol;
    float m_LastScreech = -99f;
    AudioSource m_Skitter, m_Breath;
    Renderer[] m_Renderers;
    float m_NoCatchUntil;
    float m_NextAmbush;

    public bool IsChasing => isActiveAndEnabled && m_State == State.Chase && !m_Frozen;

    protected override void Awake()
    {
        base.Awake();
        m_Agent = GetComponent<NavMeshAgent>();
        m_Agent.angularSpeed = 540f;
        m_Agent.acceleration = 14f;
        NoiseSystem.Heard += OnNoise;
        m_Renderers = GetComponentsInChildren<Renderer>(true);
        m_Skitter    = GameAudio.Loop("crawler_skitter", transform, 0f, 12f);
        m_Breath     = GameAudio.Loop("crawler_breath", transform, 0.5f, 7f);
        m_NextAmbush = Time.time + Random.Range(90f, 150f);
    }

    void Sounds()
    {
        if (m_State == State.Chase && m_LastState != State.Chase && Time.time - m_LastScreech > 8f)
        {
            m_LastScreech = Time.time;
            GameAudio.At("crawler_screech", transform.position + Vector3.up * 0.5f, 1f, 25f);
        }
        m_LastState = m_State;
        var v = m_Agent.velocity.magnitude;
        if (m_Skitter != null)
            m_Skitter.volume = Mathf.MoveTowards(m_Skitter.volume, Mathf.Clamp01(v / 2.5f) * 0.9f, 3f * Time.deltaTime);
        if (m_Skitter != null)
            m_Skitter.pitch = Mathf.Lerp(0.8f, 1.4f, v / 3.5f);
        if (m_Breath != null)
            m_Breath.volume = m_Frozen ? 0f : v < 0.3f ? 0.55f : 0.25f;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        NoiseSystem.Heard -= OnNoise;
    }

    void OnNoise(Vector3 pos, float radius)
    {
        if (!isActiveAndEnabled || m_State == State.Busy || m_Frozen)
            return;
        if (Vector3.Distance(pos, transform.position) > radius * hearing)
            return;
        var playerMadeIt = Vector3.Distance(pos, PlayerRig.Feet) < 1.5f;
        if (playerMadeIt && !PlayerRig.IsHiding && Vector3.Distance(pos, transform.position) < 7f)
            m_State = State.Chase;
        else
            Investigate(pos);
    }

    void Investigate(Vector3 pos)
    {
        m_State = State.Investigate;
        SampleNavMesh(pos, out m_Target);
        m_WaitUntil = 0f;
    }

    void Update()
    {
        if (!m_Agent.isOnNavMesh)
        {
            if (SampleNavMesh(transform.position, out var p))
                m_Agent.Warp(p);
            return;
        }
        if (m_Frozen)
        {
            m_Agent.isStopped = true;
            return;
        }
        m_Agent.isStopped = m_State == State.Busy;
        Sounds();

        var d = DistanceToPlayer;
        switch (m_State)
        {
            case State.Patrol:
                m_Agent.speed = patrolSpeed;
                if (patrolPoints != null && patrolPoints.Length > 0 && !m_Agent.pathPending && m_Agent.remainingDistance < 0.6f)
                {
                    m_Point = (m_Point + 1) % patrolPoints.Length;
                    m_Agent.SetDestination(patrolPoints[m_Point].position);
                }
                if (!PlayerRig.IsHiding && d < senseDistance)
                    m_State = State.Chase;
                // Scheduled ambush in Stage 2 — keeps the basement feeling occupied
                if (GameFlow.Instance != null && GameFlow.Instance.CurrentStage == 2 && Time.time > m_NextAmbush)
                {
                    m_NextAmbush = Time.time + Random.Range(90f, 150f);
                    Ambush();
                }
                break;

            case State.Investigate:
                m_Agent.speed = searchSpeed;
                m_Agent.SetDestination(m_Target);
                if (!m_Agent.pathPending && m_Agent.remainingDistance < 0.7f)
                {
                    if (m_WaitUntil == 0f)
                        m_WaitUntil = Time.time + 2.5f; // sniff around
                    else if (Time.time > m_WaitUntil)
                        m_State = State.Patrol;
                }
                if (!PlayerRig.IsHiding && d < senseDistance)
                    m_State = State.Chase;
                break;

            case State.Chase:
                m_Agent.speed = chaseSpeed;
                m_Agent.SetDestination(PlayerRig.Feet);
                if (PlayerRig.IsHiding || d > 14f)
                    Investigate(PlayerRig.Feet);
                else if (d < catchDistance && Time.time > m_NoCatchUntil)
                    StartCoroutine(Catch());
                break;
        }

        if (m_State == State.Busy)
            return;
        var v = m_Agent.velocity.magnitude;
        if (v > 0.15f)
            Play("Crawl", Mathf.Clamp(v / 1.4f, 0.6f, 2.5f));
        else
            Play("Idle");
        if (m_State == State.Chase && v < 0.15f)
            FacePlayer();
    }

    /// <summary>No killing: it leaps into your face, then scuttles off into the dark to hunt again.</summary>
    IEnumerator Catch()
    {
        m_State = State.Busy;
        m_NoCatchUntil = Time.time + 10f;
        SetVisible(false);
        ScareDirector.InFace(Monster.Crawler, 0.8f);
        yield return new WaitForSeconds(1.8f);
        Vector3 far = transform.position;
        var best = 0f;
        if (patrolPoints != null)
            foreach (var p in patrolPoints)
            {
                var dd = Vector3.Distance(p.position, PlayerRig.Feet);
                if (dd > best) { best = dd; far = p.position; }
            }
        if (SampleNavMesh(far, out far) && m_Agent.isOnNavMesh)
            m_Agent.Warp(far);
        SetVisible(true);
        m_State = State.Patrol;
    }

    void SetVisible(bool v)
    {
        if (m_Renderers == null) return;
        foreach (var r in m_Renderers)
            if (r != null) r.enabled = v;
    }

    /// <summary>Scare: drops in 3 m behind the player, screams (lunge), then scuttles away into the dark.</summary>
    public void Ambush()
    {
        StartCoroutine(AmbushRoutine());
    }

    IEnumerator AmbushRoutine()
    {
        var head = PlayerRig.Head;
        if (head == null)
            yield break;
        var back = -head.forward;
        back.y = 0f;
        var spot = PlayerRig.Feet + back.normalized * 3f;
        if (!SampleNavMesh(spot, out spot))
            yield break;
        m_State = State.Busy;
        m_Agent.Warp(spot);
        var to = PlayerRig.Feet - spot;
        to.y = 0f;
        transform.rotation = Quaternion.LookRotation(to);
        Play("Lunge", 1.2f, 0.02f);
        GameAudio.Flat("stinger_jump", 0.8f);
        GameAudio.At("crawler_screech", spot + Vector3.up * 0.5f, 1f, 25f);
        m_LastScreech = Time.time;
        NoiseSystem.Emit(spot, 2f);
        yield return new WaitForSeconds(1.6f);
        // flee to the far side of the room
        Vector3 far = transform.position;
        var best = 0f;
        if (patrolPoints != null)
            foreach (var p in patrolPoints)
            {
                var dd = Vector3.Distance(p.position, PlayerRig.Feet);
                if (dd > best) { best = dd; far = p.position; }
            }
        Investigate(far);
        m_Agent.speed = chaseSpeed;
    }

    public override void ResetEntity()
    {
        StopAllCoroutines();
        base.ResetEntity();
        SetVisible(true);
        m_State = State.Patrol;
    }
}
