using UnityEngine;

/// <summary>
/// Footstep sounds that match the floor (wood, stone, carpet). Old floorboards creak now and then,
/// and some boards ALWAYS creak (same spot every time); a creak is loud enough for the Crawler to hear.
/// Added to the player automatically; works in VR (real walking + smooth move) and in PC test mode.
/// </summary>
public class Footsteps : MonoBehaviour
{
    public float stepLength = 0.65f, runSpeed = 2.6f;
    public float walkVolume = 0.55f, runVolume = 0.85f;
    [Range(0f, 1f)] public float randomCreakChance = 0.12f;
    [Tooltip("Roughly 1 in this many floorboards always creaks.")]
    public int creakyBoardEvery = 6;

    Vector3 m_Last;
    float m_Travel, m_LastStep, m_LastCreak;
    Vector2Int m_LastBoard;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var host = new GameObject("Footsteps").AddComponent<Footsteps>();
        DontDestroyOnLoad(host.gameObject);
    }

    void Start() => m_Last = PlayerRig.Feet;

    void Update()
    {
        if (PlayerRig.Head == null)
            return;
        var feet = PlayerRig.Feet;
        var delta = feet - m_Last;
        delta.y = 0f;
        m_Last = feet;
        var dist = delta.magnitude;
        if (dist > 1.5f || Time.deltaTime <= 0f)
            return; // teleported
        m_Travel += dist;
        if (m_Travel < stepLength)
            return;
        m_Travel = 0f;
        var speed = stepLength / Mathf.Max(0.05f, Time.time - m_LastStep);
        m_LastStep = Time.time;
        var running = speed > runSpeed;

        var surface = Surface(feet);
        var vol = (running ? runVolume : walkVolume) * (PlayerRig.IsHiding ? 0.4f : 1f) * (PlayerRig.IsCrouching ? 0.45f : 1f);
        GameAudio.At("step_" + surface, feet + Vector3.up * 0.05f, vol, 12f, 0.08f);

        if (surface != "wood" || Time.time - m_LastCreak < 1.2f)
            return;
        var board = new Vector2Int(Mathf.FloorToInt(feet.x / 0.7f), Mathf.FloorToInt(feet.z / 0.7f));
        var alwaysCreaks = board != m_LastBoard && Hash(board) % creakyBoardEvery == 0;
        m_LastBoard = board;
        if (alwaysCreaks || Random.value < randomCreakChance * (running ? 2f : PlayerRig.IsCrouching ? 0.3f : 1f))
        {
            m_LastCreak = Time.time;
            GameAudio.At("creak_floor", feet, alwaysCreaks ? 0.90f : 0.70f, 14f, 0.12f);
            if (!PlayerRig.IsHiding)
                NoiseSystem.Emit(feet, alwaysCreaks ? 6f : 4f);
        }
    }

    static int Hash(Vector2Int v)
    {
        unchecked
        {
            var h = v.x * 73856093 ^ v.y * 19349663;
            return Mathf.Abs(h);
        }
    }

    /// <summary>Looks at the floor under the player: its material/object name decides the sound.</summary>
    static string Surface(Vector3 feet)
    {
        if (Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out var hit, 1.5f, ~0, QueryTriggerInteraction.Ignore))
        {
            var r = hit.collider.GetComponentInParent<Renderer>() ?? hit.collider.GetComponentInChildren<Renderer>();
            var name = (hit.collider.name + " " + (r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "")).ToLowerInvariant();
            if (name.Contains("carpet") || name.Contains("rug") || name.Contains("bed"))
                return "carpet";
            if (name.Contains("stone") || name.Contains("concrete") || name.Contains("tile") || name.Contains("brick") || name.Contains("metal"))
                return "stone";
            if (name.Contains("wood") || name.Contains("plank") || name.Contains("parquet") || name.Contains("board"))
                return "wood";
        }
        // Unknown floor: the apartment and nursery have wooden floors, the basement is concrete.
        var flow = GameFlow.Instance;
        return flow != null && flow.CurrentStage == 2 ? "stone" : "wood";
    }
}
