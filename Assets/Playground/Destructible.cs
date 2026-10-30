using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Shatters the object into physics shards when it hits something hard enough
/// (thrown at a wall, smashed with something, dropped from a height).
/// Works on any prop with renderers (also on child meshes of imported models), a Collider and a Rigidbody.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Destructible : MonoBehaviour
{
    [Tooltip("Impact speed (m/s) needed to break it. Glass ~2, crates ~4, bricks ~5.")]
    public float breakSpeed = 4f;

    [Tooltip("Shards along each axis (3 = 27 shards).")]
    [Range(2, 5)] public int piecesPerAxis = 3;

    [Tooltip("How hard the shards fly apart.")]
    public float burstForce = 1.5f;

    [Tooltip("Seconds before shards shrink away.")]
    public float shardLifetime = 6f;

    [Tooltip("Ignore impacts for this long after spawning, so things don't break while settling.")]
    public float armDelay = 0.75f;

    [Tooltip("Sound in Resources/SFX: vase_shatter, ceramic_break, wood_break, cardboard_break...")]
    public string breakSound = "vase_shatter";

    [Tooltip("Narration said the first time the player breaks something.")]
    public string narrationId = "N1_03";

    public UnityEvent onBreak = new UnityEvent();

    /// <summary>Colliders shards should never hit (e.g. the player's CharacterController).</summary>
    public static readonly List<Collider> ignoredColliders = new List<Collider>();

    const int k_MaxShards = 300;
    static readonly Queue<GameObject> s_Shards = new Queue<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { ignoredColliders.Clear(); s_Shards.Clear(); }

    Rigidbody m_Body;
    float m_ArmedAt;
    bool m_Broken;

    void Awake()
    {
        m_Body = GetComponent<Rigidbody>();
        m_ArmedAt = Time.time + armDelay;
        ignoredColliders.RemoveAll(c => c == null);
        if (ignoredColliders.Count == 0 && PlayerRig.Origin != null)
            ignoredColliders.AddRange(PlayerRig.Origin.GetComponentsInChildren<CharacterController>(true));
    }

    /// <summary>Can't break for this long (e.g. while a VR hand pulls it in from across the room).</summary>
    public void Disarm(float seconds) => m_ArmedAt = Mathf.Max(m_ArmedAt, Time.time + seconds);

    void OnCollisionEnter(Collision collision)
    {
        if (m_Broken || Time.time < m_ArmedAt)
            return;
        if (collision.relativeVelocity.magnitude < breakSpeed)
            return;

        var point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        Break(point);
    }

    /// <summary>Shatter now. <paramref name="impactPoint"/> is where the shards burst from.</summary>
    public void Break(Vector3 impactPoint)
    {
        if (m_Broken)
            return;
        m_Broken = true;

        // Size and look of the shards come from every mesh in the prop.
        var renderers = GetComponentsInChildren<MeshRenderer>();
        Material material = null;
        var bounds = new Bounds(transform.position, Vector3.one * 0.1f);
        var first = true;
        foreach (var r in renderers)
        {
            if (!r.enabled) continue;
            if (material == null) material = r.sharedMaterial;
            if (first) { bounds = r.bounds; first = false; }
            else bounds.Encapsulate(r.bounds);
        }

        var velocity = m_Body.linearVelocity;
        var angular = m_Body.angularVelocity;
        var n = Mathf.Clamp(piecesPerAxis, 2, 5);
        var shardMass = Mathf.Max(0.02f, m_Body.mass / (n * n * n));
        var cell = bounds.size / n;

        // Hide the original right away (also ends any grab on it), then remove it.
        gameObject.SetActive(false);

        for (var x = 0; x < n; x++)
        for (var y = 0; y < n; y++)
        for (var z = 0; z < n; z++)
        {
            if (Random.value < 0.25f)
                continue; // fewer, more uneven pieces look less like a grid
            var center = bounds.min + Vector3.Scale(cell, new Vector3(x + 0.5f, y + 0.5f, z + 0.5f));
            var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shard.name = name + " shard";
            shard.layer = gameObject.layer;
            shard.transform.SetPositionAndRotation(center, Random.rotationUniform.Slerp01(0.3f));
            shard.transform.localScale = Vector3.Scale(cell, new Vector3(Random.Range(0.5f, 1f), Random.Range(0.5f, 1f), Random.Range(0.5f, 1f)));
            if (material != null)
                shard.GetComponent<MeshRenderer>().sharedMaterial = material;
            shard.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var shardCollider = shard.GetComponent<Collider>();
            foreach (var c in ignoredColliders)
                if (c != null)
                    Physics.IgnoreCollision(shardCollider, c);

            var rb = shard.AddComponent<Rigidbody>();
            rb.mass = shardMass;
            rb.linearVelocity = velocity;
            rb.angularVelocity = angular;
            rb.AddExplosionForce(burstForce * shardMass * 60f, impactPoint, Mathf.Max(0.5f, cell.magnitude * n), 0.1f);

            shard.AddComponent<ShardFade>().lifetime = shardLifetime * Random.Range(0.85f, 1.15f);
            Track(shard);
        }

        NoiseSystem.Emit(transform.position, 12f); // the Crawler hears this
        GameAudio.At(breakSound, transform.position, 1f, 20f);
        Narrator.SayOnce(narrationId);
        onBreak.Invoke();
        Destroy(gameObject);
    }

    static void Track(GameObject shard)
    {
        s_Shards.Enqueue(shard);
        while (s_Shards.Count > k_MaxShards)
        {
            var oldest = s_Shards.Dequeue();
            if (oldest != null)
                Destroy(oldest);
        }
    }
}

/// <summary>Shrinks a shard away after a while, then deletes it.</summary>
public class ShardFade : MonoBehaviour
{
    public float lifetime = 6f;
    const float k_ShrinkTime = 1f;

    float m_Born;
    Vector3 m_StartScale;

    void Start()
    {
        m_Born = Time.time;
        m_StartScale = transform.localScale;
    }

    void Update()
    {
        var age = Time.time - m_Born;
        if (age < lifetime)
            return;
        var t = (age - lifetime) / k_ShrinkTime;
        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }
        transform.localScale = m_StartScale * (1f - t);
    }
}

static class QuaternionExtensions
{
    /// <summary>Blend from identity towards this rotation (0 = none, 1 = full).</summary>
    public static Quaternion Slerp01(this Quaternion q, float t) => Quaternion.Slerp(Quaternion.identity, q, t);
}
