using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Shared code for every monster: animation, torch-beam checks, facing the player, resetting.</summary>
public abstract class EntityBase : MonoBehaviour
{
    static readonly List<EntityBase> s_All = new List<EntityBase>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { s_All.Clear(); s_Torch = null; }
    static PSXFlashlight s_Torch;

    protected Animation m_Anim;
    protected bool m_Frozen;
    Vector3 m_SpawnPos;
    Quaternion m_SpawnRot;

    public static PSXFlashlight Torch
    {
        get
        {
            if (s_Torch == null)
                s_Torch = FindAnyObjectByType<PSXFlashlight>();
            return s_Torch;
        }
    }

    protected virtual void Awake()
    {
        s_All.Add(this);
        m_SpawnPos = transform.position;
        m_SpawnRot = transform.rotation;
        m_Anim = GetComponentInChildren<Animation>();
        if (m_Anim != null)
        {
            m_Anim.playAutomatically = false;
            foreach (AnimationState s in m_Anim)
                s.wrapMode = s.name.Contains("Reach") || s.name.Contains("Lunge") ? WrapMode.ClampForever : WrapMode.Loop;
        }
    }

    protected virtual void OnDestroy() => s_All.Remove(this);

    public static void ResetAll()
    {
        foreach (var e in s_All.ToArray())
            if (e != null)
                e.ResetEntity();
    }

    public static void FreezeAll()
    {
        foreach (var e in s_All)
            if (e != null)
                e.m_Frozen = true;
    }

    public virtual void ResetEntity()
    {
        var agent = GetComponent<NavMeshAgent>();
        if (agent != null && agent.isOnNavMesh)
            agent.Warp(m_SpawnPos);
        transform.SetPositionAndRotation(m_SpawnPos, m_SpawnRot);
    }

    /// <summary>Crossfade to the first clip whose name contains <paramref name="key"/> (Idle, Walk, Crawl, Reach, Lunge).</summary>
    protected void Play(string key, float speed = 1f, float fade = 0.2f)
    {
        if (m_Anim == null)
            return;
        foreach (AnimationState s in m_Anim)
        {
            if (!s.name.Contains(key))
                continue;
            s.speed = speed;
            if (!m_Anim.IsPlaying(s.name))
                m_Anim.CrossFade(s.name, fade);
            return;
        }
    }

    protected void SetAnimSpeed(float speed)
    {
        if (m_Anim == null)
            return;
        foreach (AnimationState s in m_Anim)
            s.speed = speed;
    }

    protected void FacePlayer(float turnSpeed = 6f)
    {
        var head = PlayerRig.Head;
        if (head == null)
            return;
        var to = head.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f)
            return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), Time.deltaTime * turnSpeed);
    }

    public float DistanceToPlayer
    {
        get
        {
            var a = transform.position;
            var b = PlayerRig.Feet;
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }

    /// <summary>Is the torch switched on, steady (not flickering) and shining on this point with nothing in the way?</summary>
    protected bool IsLitByTorch(Vector3 point)
    {
        var t = Torch;
        if (t == null || t.beam == null || !t.IsSteadyOn)
            return false;
        var b = t.beam.transform;
        var to = point - b.position;
        var d = to.magnitude;
        if (d > t.beam.range || Vector3.Angle(b.forward, to) > t.beam.spotAngle * 0.5f)
            return false;
        return !Blocked(b.position, point, t.transform);
    }

    /// <summary>Is the player looking roughly towards this point with a clear line of sight?</summary>
    protected bool IsSeenByPlayer(Vector3 point, float halfAngle = 60f)
    {
        var head = PlayerRig.Head;
        if (head == null)
            return false;
        if (Vector3.Angle(head.forward, point - head.position) > halfAngle)
            return false;
        return !Blocked(head.position, point, null);
    }

    bool Blocked(Vector3 from, Vector3 to, Transform ignore)
    {
        var dir = to - from;
        var d = dir.magnitude;
        foreach (var hit in Physics.RaycastAll(from, dir / d, d - 0.2f, ~0, QueryTriggerInteraction.Ignore))
        {
            var t = hit.transform;
            if (t.IsChildOf(transform) || (ignore != null && t.IsChildOf(ignore)))
                continue;
            if (PlayerRig.Origin != null && t.IsChildOf(PlayerRig.Origin.transform))
                continue;
            if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                continue; // small loose props don't block sight
            return true;
        }
        return false;
    }

    protected static bool SampleNavMesh(Vector3 p, out Vector3 result, float range = 3f)
    {
        if (NavMesh.SamplePosition(p, out var hit, range, NavMesh.AllAreas))
        {
            result = hit.position;
            return true;
        }
        result = p;
        return false;
    }
}
