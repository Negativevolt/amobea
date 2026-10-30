using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The tentacle thing. Built from code (no model file): a wet dark mass with a cluster of pale eyes, a mouth ringed
/// with teeth that opens and shuts, and writhing low-poly tentacles. The tentacles can reach for the player's head and
/// wrap around the edges of your view, or (body hidden) burst out of a wall, a TV, a window or the ceiling.
/// </summary>
public class TentacleBeast : MonoBehaviour
{
    [Tooltip("How fast the tentacles writhe.")] public float writhe = 1f;
    [Tooltip("0 = writhing freely, 1 = reaching for the target.")] [Range(0f, 1f)] public float reach;
    [Tooltip("0 = pulled in, 1 = fully out (for bursting out and pulling back).")] [Range(0f, 1f)] public float extend = 1f;
    [Tooltip("What the tentacles reach for (usually the player's head).")] public Transform target;
    [Tooltip("Tentacles never reach further than this from the body (stops them at a window, a TV...).")] public float maxReach = 2.2f;
    [Tooltip("Wrap around the head (in your face) instead of pointing at it.")] public bool wrapAround = true;
    [Range(0f, 1f)] public float mouthOpen;

    class Tentacle
    {
        public Mesh mesh;
        public Vector3 baseLocal, dirLocal;
        public float length, radius, phase, speed;
        public Vector2 wrap; // where around the head the tip goes
        public Vector3[] verts, normals;
    }

    const int k_Sides = 5, k_Rings = 14;
    readonly List<Tentacle> m_Tentacles = new List<Tentacle>();
    Transform m_Maw, m_Jaw;
    static Material s_Skin, s_Eye, s_Mouth, s_Tooth;
    int[] m_Tris;

    public Vector3 FaceWorld => m_Maw != null ? m_Maw.position : transform.position;

    // ------------------------------------------------------------------ building
    static Material Mat(ref Material m, Color c, float smooth, Color emission)
    {
        if (m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        if (emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
        }
        return m;
    }

    /// <summary>Makes one. body = false: only tentacles (they come out of something).</summary>
    public static TentacleBeast Create(bool body, int count = 9, float length = 1.35f)
    {
        var go = new GameObject(body ? "Tentacle Beast" : "Tentacles");
        var b = go.AddComponent<TentacleBeast>();
        b.Build(body, count, length);
        return b;
    }

    void Build(bool body, int count, float length)
    {
        var skin = Mat(ref s_Skin, new Color(0.09f, 0.06f, 0.075f), 0.62f, Color.black);
        var rng = new System.Random(GetInstanceID());
        float R() => (float)rng.NextDouble();

        if (body)
        {
            // lumpy body
            var blob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(blob.GetComponent<Collider>());
            blob.name = "Body";
            blob.transform.SetParent(transform, false);
            blob.transform.localScale = new Vector3(0.62f, 0.55f, 0.5f);
            var mf = blob.GetComponent<MeshFilter>();
            var mesh = Instantiate(mf.sharedMesh);
            var v = mesh.vertices;
            for (var i = 0; i < v.Length; i++)
            {
                var n = v[i].normalized;
                var bump = Mathf.Sin(n.x * 9f + 1.3f) * Mathf.Sin(n.y * 7f) * Mathf.Sin(n.z * 8f + 0.6f);
                v[i] *= 1f + bump * 0.18f;
            }
            mesh.vertices = v;
            mesh.RecalculateNormals();
            mf.sharedMesh = mesh;
            blob.GetComponent<MeshRenderer>().sharedMaterial = skin;

            // eyes: too many, not quite symmetric
            var eye = Mat(ref s_Eye, new Color(0.9f, 0.88f, 0.7f), 0.9f, new Color(1f, 0.95f, 0.75f) * 1.6f);
            for (var i = 0; i < 7; i++)
            {
                var e = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(e.GetComponent<Collider>());
                e.name = "Eye";
                e.transform.SetParent(transform, false);
                var a = (i - 3) * 0.32f + (R() - 0.5f) * 0.15f;
                var up = 0.05f + R() * 0.17f;
                e.transform.localPosition = new Vector3(Mathf.Sin(a) * 0.24f, up, Mathf.Cos(a) * 0.22f + 0.02f);
                e.transform.localScale = Vector3.one * (0.035f + R() * 0.04f);
                e.GetComponent<MeshRenderer>().sharedMaterial = eye;
            }
            // mouth with a ring of teeth
            m_Maw = new GameObject("Maw").transform;
            m_Maw.SetParent(transform, false);
            m_Maw.localPosition = new Vector3(0f, -0.1f, 0.24f);
            var hole = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(hole.GetComponent<Collider>());
            hole.transform.SetParent(m_Maw, false);
            hole.transform.localScale = new Vector3(0.22f, 0.14f, 0.06f);
            hole.GetComponent<MeshRenderer>().sharedMaterial = Mat(ref s_Mouth, new Color(0.02f, 0f, 0f), 0.3f, new Color(0.25f, 0.02f, 0.02f));
            m_Jaw = hole.transform;
            var tooth = Mat(ref s_Tooth, new Color(0.85f, 0.82f, 0.7f), 0.5f, Color.black);
            for (var i = 0; i < 12; i++)
            {
                var t = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(t.GetComponent<Collider>());
                t.transform.SetParent(hole.transform, false);
                var a = i / 12f * Mathf.PI * 2f;
                t.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.42f, Mathf.Sin(a) * 0.42f, 0.35f);
                t.transform.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 90f);
                t.transform.localScale = new Vector3(0.12f, 0.28f, 0.2f);
                t.GetComponent<MeshRenderer>().sharedMaterial = tooth;
            }
        }
        else
        {
            m_Maw = new GameObject("Origin").transform;
            m_Maw.SetParent(transform, false);
        }

        // tentacles
        m_Tris = new int[k_Sides * (k_Rings - 1) * 6 + k_Sides * 3];
        var ti = 0;
        for (var r = 0; r < k_Rings - 1; r++)
            for (var s = 0; s < k_Sides; s++)
            {
                int a = r * k_Sides + s, b = r * k_Sides + (s + 1) % k_Sides, c = a + k_Sides, d = b + k_Sides;
                m_Tris[ti++] = a; m_Tris[ti++] = c; m_Tris[ti++] = b;
                m_Tris[ti++] = b; m_Tris[ti++] = c; m_Tris[ti++] = d;
            }
        var tip = k_Rings * k_Sides;
        for (var s = 0; s < k_Sides; s++)
        {
            m_Tris[ti++] = (k_Rings - 1) * k_Sides + s; m_Tris[ti++] = tip; m_Tris[ti++] = (k_Rings - 1) * k_Sides + (s + 1) % k_Sides;
        }
        for (var i = 0; i < count; i++)
        {
            var go = new GameObject("Tentacle");
            go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = "tentacle" };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = skin;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var ang = i / (float)count * Mathf.PI * 2f + R() * 0.5f;
            Vector3 dir, basePos;
            if (body)
            {
                // from the underside and sides of the body, pointing out and forward
                dir = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang) * 0.6f - 0.35f, 0.55f + R() * 0.5f).normalized;
                basePos = Vector3.Scale(dir, new Vector3(0.27f, 0.22f, 0.2f));
            }
            else
            {
                // out of an opening: a tight bundle that spreads
                basePos = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * (0.05f + R() * 0.1f);
                dir = (Vector3.forward + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * (0.35f + R() * 0.3f)).normalized;
            }
            m_Tentacles.Add(new Tentacle
            {
                mesh = mesh,
                baseLocal = basePos,
                dirLocal = dir,
                length = length * (0.75f + R() * 0.45f),
                radius = (body ? 0.055f : 0.045f) * (0.8f + R() * 0.4f),
                phase = R() * 10f,
                speed = 0.8f + R() * 0.6f,
                wrap = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)),
                verts = new Vector3[k_Rings * k_Sides + 1],
                normals = new Vector3[k_Rings * k_Sides + 1],
            });
        }
        LateUpdate();
    }

    // ------------------------------------------------------------------ animation
    void LateUpdate()
    {
        var time = Time.time;
        if (m_Jaw != null)
        {
            var open = Mathf.Clamp01(mouthOpen + Mathf.Max(0f, Mathf.Sin(time * 7f * writhe)) * 0.15f);
            m_Jaw.localScale = new Vector3(0.22f + open * 0.05f, 0.08f + open * 0.2f, 0.06f);
        }
        var pts = new Vector3[k_Rings];
        foreach (var t in m_Tentacles)
        {
            var baseW = transform.TransformPoint(t.baseLocal);
            var dirW = transform.TransformDirection(t.dirLocal).normalized;
            var len = t.length * Mathf.Max(0.02f, extend);
            var u = Vector3.Cross(dirW, Mathf.Abs(dirW.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var v = Vector3.Cross(dirW, u);

            // where the tip wants to go when reaching
            var tipTarget = baseW + dirW * len;
            if (target != null && reach > 0f)
            {
                var aim = target.position;
                if (wrapAround)
                    aim += target.right * t.wrap.x * 0.28f + target.up * t.wrap.y * 0.22f - target.forward * 0.1f; // around your head
                else
                    aim += (target.right * t.wrap.x + target.up * t.wrap.y) * 0.12f;
                var to = aim - baseW;
                tipTarget = baseW + Vector3.ClampMagnitude(to, Mathf.Min(maxReach, len * 1.6f));
            }
            var ctrl = baseW + dirW * len * 0.45f;
            var amp = len * 0.22f * (1f - reach * 0.6f);
            var w = time * 2.4f * writhe * t.speed + t.phase;
            for (var i = 0; i < k_Rings; i++)
            {
                var k = i / (float)(k_Rings - 1);
                var free = baseW + dirW * len * k
                           + (u * Mathf.Sin(w + k * 4.5f) + v * Mathf.Cos(w * 0.8f + k * 5.5f)) * amp * k;
                var bez = (1 - k) * (1 - k) * baseW + 2f * (1 - k) * k * ctrl + k * k * tipTarget;
                bez += (u * Mathf.Sin(w * 1.6f + k * 7f) + v * Mathf.Cos(w * 1.3f + k * 6f)) * amp * 0.35f * k;
                pts[i] = Vector3.Lerp(free, bez, reach);
            }
            // tube
            var prevN = u;
            for (var i = 0; i < k_Rings; i++)
            {
                var tan = (i < k_Rings - 1 ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]).normalized;
                var n = Vector3.ProjectOnPlane(prevN, tan).normalized;
                if (n.sqrMagnitude < 1e-6f) n = Vector3.Cross(tan, Vector3.up).normalized;
                prevN = n;
                var bnorm = Vector3.Cross(tan, n);
                var k = i / (float)(k_Rings - 1);
                var rad = t.radius * Mathf.Pow(1f - k, 0.85f) + 0.004f;
                rad *= 1f + Mathf.Sin(w * 2f + k * 12f) * 0.12f; // pulsing
                for (var s = 0; s < k_Sides; s++)
                {
                    var a = s / (float)k_Sides * Mathf.PI * 2f;
                    var dir = n * Mathf.Cos(a) + bnorm * Mathf.Sin(a);
                    t.verts[i * k_Sides + s] = transform.InverseTransformPoint(pts[i] + dir * rad);
                    t.normals[i * k_Sides + s] = transform.InverseTransformDirection(dir);
                }
            }
            var tipEnd = pts[k_Rings - 1] + (pts[k_Rings - 1] - pts[k_Rings - 2]).normalized * 0.03f;
            t.verts[k_Rings * k_Sides] = transform.InverseTransformPoint(tipEnd);
            t.normals[k_Rings * k_Sides] = transform.InverseTransformDirection((pts[k_Rings - 1] - pts[k_Rings - 2]).normalized);
            t.mesh.vertices = t.verts;
            t.mesh.normals = t.normals;
            if (t.mesh.triangles.Length == 0) t.mesh.triangles = m_Tris;
            t.mesh.RecalculateBounds();
        }
    }
}
