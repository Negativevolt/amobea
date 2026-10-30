using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// A small smash-it-up playground:
///   - a table with glass bottles, vases, throwing balls and a hammer
///   - a stack of wooden crates (left)
///   - a brick wall (right)
/// Everything can be grabbed with the grip button. Bottles, vases, crates and bricks
/// shatter when they hit something hard enough. Grab the red RESET button on the table
/// (or press P on the keyboard) to put everything back.
///
/// EDITING IN THE SCENE: use the menu Tools > Destruction Playground > Add To Scene.
/// That creates every prop as a normal scene object you can see, move, duplicate or
/// delete in the editor (move the "Destruction Playground" object to move it all).
///
/// If a scene listed in <see cref="autoScenes"/> has no playground saved in it, one is
/// built automatically in front of the player when you press Play.
/// </summary>
public class DestructionPlayground : MonoBehaviour
{
    /// <summary>Scenes where a playground is created automatically if none is saved in the scene.</summary>
    public static readonly string[] autoScenes = { }; // the game no longer uses the test playground

    [Tooltip("How far in front of the player the auto-built table stands (metres).")]
    public float distance = 1.4f;

    [Tooltip("Off = the props are saved in the scene (made with Tools > Destruction Playground > Add To Scene). " +
             "On = the props are generated when Play starts.")]
    public bool buildOnStart = true;

    Transform m_Root;
    XROrigin m_Origin;

    // ---------------------------------------------------------------- auto setup

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (System.Array.IndexOf(autoScenes, scene.name) < 0)
            return;
        if (FindAnyObjectByType<DestructionPlayground>(FindObjectsInactive.Include) != null)
            return; // the scene already has one (e.g. saved with Add To Scene)
        new GameObject("Destruction Playground").AddComponent<DestructionPlayground>();
    }

    // ---------------------------------------------------------------- lifecycle

    IEnumerator Start()
    {
        m_Origin = FindAnyObjectByType<XROrigin>();

        // Shards and props shouldn't shove the player around.
        Destructible.ignoredColliders.Clear();
        if (m_Origin != null)
            Destructible.ignoredColliders.AddRange(m_Origin.GetComponentsInChildren<CharacterController>(true));

        if (!buildOnStart)
        {
            // Props are already in the scene: just finish the runtime-only setup.
            m_Root = transform.Find("Props");
            IgnorePlayer(transform);
            yield break;
        }

        // Wait until the headset is tracking, so we know where the player is looking.
        var cam = m_Origin != null && m_Origin.Camera != null ? m_Origin.Camera : Camera.main;
        var waited = 0f;
        while (cam != null && cam.transform.localPosition == Vector3.zero && waited < 2f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
        yield return null;

        PlaceInFrontOfPlayer();
        Build();
        IgnorePlayer(transform);
    }

    void Update()
    {
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.pKey.wasPressedThisFrame)
            Rebuild();
    }

    /// <summary>Put everything back the way it started.</summary>
    public void Rebuild()
    {
        if (!buildOnStart)
        {
            // Saved-in-scene props: reload the scene to restore them exactly as you placed them.
            SceneManager.LoadScene(gameObject.scene.buildIndex >= 0 ? gameObject.scene.buildIndex : SceneManager.GetActiveScene().buildIndex);
            return;
        }

        if (m_Root != null)
        {
            m_Root.gameObject.SetActive(false); // so the floor check doesn't land on the old table
            Destroy(m_Root.gameObject);
        }
        PlaceInFrontOfPlayer();
        Build();
        IgnorePlayer(transform);
    }

    void PlaceInFrontOfPlayer()
    {
        var cam = m_Origin != null && m_Origin.Camera != null ? m_Origin.Camera : Camera.main;
        var basePose = cam != null ? cam.transform : (m_Origin != null ? m_Origin.transform : transform);

        var forward = Vector3.ProjectOnPlane(basePose.forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f && m_Origin != null)
            forward = Vector3.ProjectOnPlane(m_Origin.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f)
            forward = Vector3.forward;

        var spot = basePose.position + forward.normalized * distance;
        transform.SetPositionAndRotation(new Vector3(spot.x, FloorHeight(spot), spot.z),
            Quaternion.LookRotation(forward.normalized, Vector3.up));
    }

    /// <summary>Height of the floor below <paramref name="near"/>, ignoring the player and loose props.</summary>
    public static float FloorHeight(Vector3 near)
    {
        var best = float.MaxValue;
        var floor = near.y;
        foreach (var hit in Physics.RaycastAll(near + Vector3.up * 3f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.rigidbody != null || hit.collider.GetComponentInParent<XROrigin>() != null
                || hit.collider.GetComponentInParent<DestructionPlayground>() != null)
                continue;
            if (hit.distance < best)
            {
                best = hit.distance;
                floor = hit.point.y;
            }
        }
        return floor;
    }

    static void IgnorePlayer(Transform root)
    {
        foreach (var c in root.GetComponentsInChildren<Collider>(true))
            foreach (var ignored in Destructible.ignoredColliders)
                if (ignored != null)
                    Physics.IgnoreCollision(c, ignored);
    }

    // ---------------------------------------------------------------- layout

    /// <summary>Creates all the props under a "Props" child. Works in Play mode and in the editor.</summary>
    public Transform Build()
    {
        m_Root = new GameObject("Props").transform;
        m_Root.SetParent(transform, false);

        // Table (static)
        const float tableHeight = 0.75f;
        Prop("Table", PrimitiveType.Cube, new Vector3(0f, tableHeight - 0.03f, 0f), new Vector3(1.4f, 0.06f, 0.6f), "PG_Table");
        Prop("Table Leg", PrimitiveType.Cube, new Vector3(-0.62f, (tableHeight - 0.06f) / 2f, -0.22f), new Vector3(0.06f, tableHeight - 0.06f, 0.06f), "PG_Table");
        Prop("Table Leg", PrimitiveType.Cube, new Vector3(0.62f, (tableHeight - 0.06f) / 2f, -0.22f), new Vector3(0.06f, tableHeight - 0.06f, 0.06f), "PG_Table");
        Prop("Table Leg", PrimitiveType.Cube, new Vector3(-0.62f, (tableHeight - 0.06f) / 2f, 0.22f), new Vector3(0.06f, tableHeight - 0.06f, 0.06f), "PG_Table");
        Prop("Table Leg", PrimitiveType.Cube, new Vector3(0.62f, (tableHeight - 0.06f) / 2f, 0.22f), new Vector3(0.06f, tableHeight - 0.06f, 0.06f), "PG_Table");
        var top = tableHeight + 0.002f;

        // Glass bottles (break easily)
        for (var i = 0; i < 4; i++)
        {
            var bottle = Prop("Glass Bottle", PrimitiveType.Cylinder, new Vector3(-0.5f + i * 0.13f, top + 0.13f, 0.12f), new Vector3(0.07f, 0.13f, 0.07f), "PG_Glass");
            MakeDynamic(bottle, 0.35f);
            MakeBreakable(bottle, 2.2f, 3);
        }

        // Vases
        for (var i = 0; i < 2; i++)
        {
            var vase = Prop("Vase", PrimitiveType.Sphere, new Vector3(0.05f + i * 0.2f, top + 0.08f, 0.12f), new Vector3(0.16f, 0.16f, 0.16f), "PG_Vase");
            MakeDynamic(vase, 0.6f);
            MakeBreakable(vase, 2.5f, 3);
        }

        // Throwing balls (don't break)
        for (var i = 0; i < 3; i++)
        {
            var ball = Prop("Ball", PrimitiveType.Sphere, new Vector3(-0.45f + i * 0.14f, top + 0.06f, -0.15f), Vector3.one * 0.11f, "PG_Ball");
            MakeDynamic(ball, 0.45f);
        }

        // Hammer (doesn't break)
        BuildHammer(new Vector3(0.35f, top + 0.04f, -0.12f));

        // Reset button on the front-right of the table
        BuildResetButton(new Vector3(0.6f, top + 0.03f, -0.22f));

        // Crate stack (left)
        const float crate = 0.4f;
        var crateBase = new Vector3(-1.4f, 0f, 0.6f);
        Crate(crateBase + new Vector3(-0.21f, crate / 2f, 0f));
        Crate(crateBase + new Vector3(0.21f, crate / 2f, 0f));
        Crate(crateBase + new Vector3(0f, crate * 1.5f + 0.005f, 0f));

        // Brick wall (right)
        const int columns = 6, rows = 6;
        var brick = new Vector3(0.24f, 0.11f, 0.12f);
        var wallBase = new Vector3(1.0f, 0f, 0.9f);
        for (var row = 0; row < rows; row++)
        {
            var offset = row % 2 == 0 ? 0f : brick.x / 2f;
            for (var col = 0; col < columns; col++)
            {
                var b = Prop("Brick", PrimitiveType.Cube,
                    wallBase + new Vector3(col * (brick.x + 0.004f) + offset, brick.y / 2f + row * (brick.y + 0.002f), 0f),
                    brick, "PG_Brick");
                MakeDynamic(b, 1.2f);
                MakeBreakable(b, 5f, 2);
            }
        }

        return m_Root;
    }

    void Crate(Vector3 localPos)
    {
        var c = Prop("Crate", PrimitiveType.Cube, localPos, Vector3.one * 0.4f, "PG_Wood");
        MakeDynamic(c, 4f);
        MakeBreakable(c, 4f, 3);
    }

    void BuildHammer(Vector3 localPos)
    {
        var hammer = new GameObject("Hammer");
        hammer.transform.SetParent(m_Root, false);
        hammer.transform.localPosition = localPos;
        hammer.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); // lying on the table

        var handle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        handle.name = "Handle";
        handle.transform.SetParent(hammer.transform, false);
        handle.transform.localScale = new Vector3(0.035f, 0.17f, 0.035f);
        Paint(handle, "PG_Handle");

        var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
        head.name = "Head";
        head.transform.SetParent(hammer.transform, false);
        head.transform.localPosition = new Vector3(0f, 0.17f, 0f);
        head.transform.localScale = new Vector3(0.18f, 0.07f, 0.07f);
        Paint(head, "PG_Metal");

        MakeDynamic(hammer, 1.5f);
    }

    void BuildResetButton(Vector3 localPos)
    {
        var button = Prop("RESET (grab me)", PrimitiveType.Cylinder, localPos, new Vector3(0.08f, 0.02f, 0.08f), "PG_Reset");
        var body = button.AddComponent<Rigidbody>();
        body.isKinematic = true;
        button.AddComponent<XRSimpleInteractable>();
        button.AddComponent<PlaygroundResetButton>(); // hooks the grab up at runtime
    }

    // ---------------------------------------------------------------- helpers

    GameObject Prop(string name, PrimitiveType type, Vector3 localPos, Vector3 size, string material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(m_Root, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        if (material != null)
            Paint(go, material);
        return go;
    }

    static void Paint(GameObject go, string material)
    {
        var mat = Resources.Load<Material>(material);
        if (mat != null)
            go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static void MakeDynamic(GameObject go, float mass)
    {
        var body = go.GetComponent<Rigidbody>();
        if (body == null)
            body = go.AddComponent<Rigidbody>();
        body.mass = mass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var grab = go.AddComponent<XRGrabInteractable>();
        grab.movementType = XRBaseInteractable.MovementType.VelocityTracking; // held objects still collide, so you can smash things
        grab.throwOnDetach = true;
        grab.useDynamicAttach = true; // hold it where you grabbed it
    }

    static void MakeBreakable(GameObject go, float breakSpeed, int pieces)
    {
        var d = go.AddComponent<Destructible>();
        d.breakSpeed = breakSpeed;
        d.piecesPerAxis = pieces;
    }
}
