using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

/// <summary>
/// Tools > Horror Pack > Build Dark Corridor: builds a creepy corridor from the modular Horror Pack
/// pieces (2.5 m grid) in the open scene. Everything ends up under one "Dark Corridor" object you
/// can move, edit or delete (Ctrl+Z undoes it).
/// </summary>
static class CorridorBuilder
{
    const string k_Models = "Assets/HorrorPack/Models/";
    const float k_Tile = 2.5f;
    const float k_WallHeight = 2.769f;
    const float k_WallThick = 0.2f;

    [MenuItem("Tools/Horror Pack/Build Dark Corridor")]
    public static GameObject Build() => Build(new Vector3(0f, 0f, 4f), 6);

    public static GameObject Build(Vector3 start, int length)
    {
        var old = GameObject.Find("Dark Corridor");
        if (old != null)
            Undo.DestroyObjectImmediate(old);

        var root = new GameObject("Dark Corridor").transform;
        root.position = Vector3.zero;
        var structure = Child(root, "Structure");
        var props = Child(root, "Props");
        var lights = Child(root, "Lights");

        var half = k_Tile / 2f;
        var endZ = start.z + length * k_Tile;

        for (var i = 0; i < length; i++)
        {
            var cz = start.z + half + i * k_Tile;

            // Floor + ceiling
            var floor = Spawn("Floor1UnitWood01", "Floor1UnitWood01", structure, new Vector3(start.x, 0f, cz), -0.01f, 0f);
            MakeTeleportable(floor);
            Spawn("Ceiling1Unit", "Ceiling1Unit", structure, new Vector3(start.x, 0f, cz), k_WallHeight, 0f);

            // Left wall (doorway on tile 2)
            if (i == 2)
            {
                Spawn("Wall1UnitDoorWay", "Wall1UnitDoorFrame.001", structure, new Vector3(start.x - half - k_WallThick / 2f, 0f, cz), 0f, 90f);
                Spawn("Door03", "Door.002", structure, new Vector3(start.x - half - k_WallThick / 2f, 0f, cz), 0f, 90f);
            }
            else
            {
                Spawn("Wall1UnitEmpty", "Wall1UnitEmpty", structure, new Vector3(start.x - half - k_WallThick / 2f, 0f, cz), 0f, 90f);
            }

            // Right wall
            Spawn("Wall1UnitEmpty", "Wall1UnitEmpty", structure, new Vector3(start.x + half + k_WallThick / 2f, 0f, cz), 0f, 90f);
        }

        // End wall with a closed door
        Spawn("Wall1UnitDoorFrame", "Wall1UnitDoorFrame", structure, new Vector3(start.x, 0f, endZ + k_WallThick / 2f), 0f, 0f);
        Spawn("Door01", "Door", structure, new Vector3(start.x, 0f, endZ + k_WallThick / 2f), 0f, 0f);

        // ---------------------------------------------------------------- props
        Spawn("TrashBag01", "TrashBag01", props, new Vector3(start.x + 0.75f, 0f, start.z + 0.9f), 0f, 30f);
        Spawn("CardboardBox02", "CardboardBox02", props, new Vector3(start.x - 0.8f, 0f, start.z + 3.3f), 0f, 10f);
        Spawn("CardboardBox01", "CardboardBox01", props, new Vector3(start.x - 0.85f, 0.34f, start.z + 3.3f), 0.34f, -15f);
        Spawn("RoomRug", "RoomRug", props, new Vector3(start.x, 0f, start.z + 6.25f), 0f, 90f);
        Spawn("Newspaper", "Newspaper", props, new Vector3(start.x + 0.4f, 0f, start.z + 9.5f), 0.002f, 25f);
        Spawn("GrandfatherClock", "GrandfatherClock", props, new Vector3(start.x - half + 0.35f, 0f, start.z + 11.0f), 0f, 90f);
        var chair = Spawn("ChairRocking", "ChairRocking", props, new Vector3(start.x + 0.3f, 0f, endZ - 1.3f), 0f, 180f);
        var chairTop = chair != null ? Bounds(chair).center.y : 0.45f;
        Spawn("Teddybear", "Teddybear", props, new Vector3(start.x + 0.3f, 0f, endZ - 1.35f), chairTop - 0.05f, 180f);
        Spawn("KidsDrawing", "KidsDrawing", props, new Vector3(start.x - half + 0.02f, 0f, start.z + 8.4f), 1.35f, 90f);

        // ---------------------------------------------------------------- lights
        // Flickering ceiling lamp in the middle
        var midZ = start.z + k_Tile * Mathf.Floor(length / 2f) - half;
        Spawn("CeliningLight01", "CeliningLight01", props, new Vector3(start.x, 0f, midZ), k_WallHeight - 0.26f, 0f);
        var ceiling = PointLight(lights, "Ceiling Lamp (flickering)", new Vector3(start.x, k_WallHeight - 0.4f, midZ),
            new Color(1f, 0.82f, 0.5f), 2.2f, 6f, LightShadows.Hard);
        ceiling.gameObject.AddComponent<FlickerLight>();

        // Dying red wall lamp near the far end
        var sconceZ = endZ - 3.75f;
        Spawn("WallLight01", "WallLight01", props, new Vector3(start.x + half - 0.12f, 0f, sconceZ), 1.75f, -90f);
        var red = PointLight(lights, "Wall Lamp (dying)", new Vector3(start.x + half - 0.35f, 1.85f, sconceZ),
            new Color(0.9f, 0.18f, 0.1f), 0.9f, 3.5f, LightShadows.None);
        var f = red.gameObject.AddComponent<FlickerLight>();
        f.buzz = 0.35f;
        f.blackoutsPerSecond = 1.2f;

        Undo.RegisterCreatedObjectUndo(root.gameObject, "Build Dark Corridor");
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        Selection.activeGameObject = root.gameObject;
        return root.gameObject;
    }

    // ---------------------------------------------------------------- helpers
    static Transform Child(Transform parent, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    /// <summary>
    /// Instantiates a Horror Pack model, turns it by <paramref name="yaw"/>, then moves it so its bounds are
    /// centred on <paramref name="center"/> (x/z) with the bottom at <paramref name="bottomY"/>.
    /// </summary>
    static GameObject Spawn(string folder, string file, Transform parent, Vector3 center, float bottomY, float yaw)
    {
        var path = k_Models + folder + "/" + file + ".fbx";
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model == null)
        {
            Debug.LogWarning("[Corridor] Missing model " + path);
            return null;
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation;
        var b = Bounds(go);
        go.transform.position += new Vector3(center.x - b.center.x, bottomY - b.min.y, center.z - b.center.z);
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        return go;
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs)
            b.Encapsulate(r.bounds);
        return b;
    }

    static void MakeTeleportable(GameObject floor)
    {
        if (floor == null)
            return;
        var area = floor.AddComponent<TeleportationArea>();
        area.interactionLayers = InteractionLayerMask.GetMask("Teleport");
    }

    static Light PointLight(Transform parent, string name, Vector3 pos, Color color, float intensity, float range, LightShadows shadows)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = shadows;
        return l;
    }
}
