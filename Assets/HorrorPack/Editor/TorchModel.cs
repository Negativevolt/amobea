using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates a small low-poly pocket torch (about 18 cm) out of 8-sided tubes:
/// yellow plastic body, black grip band and tail cap, dark metal head and a glass lens.
/// Uses the shared Horror Pack materials, flat PS1-style shading, beam along +Z.
/// Meshes are saved in Assets/HorrorPack/Models/Torch.
/// </summary>
static class TorchModel
{
    const int k_Segments = 8;
    const string k_Folder = "Assets/HorrorPack/Models/Torch";
    const string k_Materials = "Assets/HorrorPack/Materials/";

    public static GameObject Create()
    {
        Directory.CreateDirectory(k_Folder);
        var root = new GameObject("Torch Model");

        Part(root, "Tail Cap", Tube(0.0155f, 0.0160f, -0.090f, -0.074f, true, true), "plastic_black");
        Part(root, "Body", Tube(0.0160f, 0.0165f, -0.074f, 0.050f, false, false), "plastic_yellow");
        Part(root, "Grip Band", Tube(0.0172f, 0.0172f, -0.055f, -0.010f, false, false), "plastic_black");
        Part(root, "Head", Tube(0.0165f, 0.0225f, 0.050f, 0.082f, true, false), "metal_dark");
        Part(root, "Lens", Tube(0.0205f, 0.0205f, 0.082f, 0.085f, true, true), "glass");
        Part(root, "Switch", Box(new Vector3(0.010f, 0.006f, 0.014f), new Vector3(0f, 0.0185f, 0.030f)), "plastic_black");
        Part(root, "Lanyard Loop", Tube(0.004f, 0.004f, -0.098f, -0.090f, true, true), "metal_dark");

        AssetDatabase.SaveAssets();
        return root;
    }

    static void Part(GameObject parent, string name, Mesh mesh, string material)
    {
        mesh.name = "Torch " + name;
        var path = k_Folder + "/Torch_" + name.Replace(" ", "") + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(mesh, existing);
            mesh = existing;
        }
        else
        {
            AssetDatabase.CreateAsset(mesh, path);
        }

        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(k_Materials + material + ".mat");
    }

    // ------------------------------------------------------------------ mesh building (flat shaded)
    class Builder
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> t = new List<int>();

        /// <summary>Adds a flat polygon (vertices in counter-clockwise order seen from outside).</summary>
        public void Face(params Vector3[] pts)
        {
            var normal = Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]).normalized;
            var start = v.Count;
            foreach (var p in pts)
            {
                v.Add(p);
                n.Add(normal);
                uv.Add(BoxUV(p, normal));
            }
            // Unity front faces: the side the normal points to (normal = cross(b - a, c - a)).
            for (var i = 1; i < pts.Length - 1; i++)
            {
                t.Add(start);
                t.Add(start + i);
                t.Add(start + i + 1);
            }
        }

        static Vector2 BoxUV(Vector3 p, Vector3 normal)
        {
            const float scale = 4f; // the 128px textures repeat every 25 cm
            var a = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
            if (a.x >= a.y && a.x >= a.z) return new Vector2(p.z, p.y) * scale;
            if (a.y >= a.z) return new Vector2(p.x, p.z) * scale;
            return new Vector2(p.x, p.y) * scale;
        }

        public Mesh ToMesh()
        {
            var m = new Mesh();
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>Tube along Z from z0 (radius r0) to z1 (radius r1).</summary>
    static Mesh Tube(float r0, float r1, float z0, float z1, bool cap0, bool cap1)
    {
        var b = new Builder();
        var ring0 = new Vector3[k_Segments];
        var ring1 = new Vector3[k_Segments];
        for (var i = 0; i < k_Segments; i++)
        {
            var a = (i + 0.5f) * Mathf.PI * 2f / k_Segments;
            ring0[i] = new Vector3(Mathf.Cos(a) * r0, Mathf.Sin(a) * r0, z0);
            ring1[i] = new Vector3(Mathf.Cos(a) * r1, Mathf.Sin(a) * r1, z1);
        }
        for (var i = 0; i < k_Segments; i++)
        {
            var j = (i + 1) % k_Segments;
            b.Face(ring0[i], ring0[j], ring1[j], ring1[i]); // outward-facing side
        }
        if (cap0)
        {
            var c = new Vector3[k_Segments];
            for (var i = 0; i < k_Segments; i++) c[i] = ring0[k_Segments - 1 - i]; // faces -Z
            b.Face(c);
        }
        if (cap1)
        {
            var c = new Vector3[k_Segments];
            for (var i = 0; i < k_Segments; i++) c[i] = ring1[i]; // faces +Z
            b.Face(c);
        }
        return b.ToMesh();
    }

    static Mesh Box(Vector3 size, Vector3 center)
    {
        var b = new Builder();
        var h = size * 0.5f;
        Vector3 P(float x, float y, float z) => center + new Vector3(x * h.x, y * h.y, z * h.z);
        b.Face(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));     // +Z
        b.Face(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1)); // -Z
        b.Face(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1));     // +X
        b.Face(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1)); // -X
        b.Face(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1));     // +Y
        b.Face(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1)); // -Y
        return b.ToMesh();
    }
}
