using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Runs the game: the three stages, items the player is carrying, moving between stages,
/// getting caught (back to the stage start) and the ending.
/// </summary>
public class GameFlow : MonoBehaviour
{
    public static GameFlow Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    [Tooltip("Where the player starts each stage (index 0 = stage 1).")]
    public Transform[] stageSpawns = new Transform[3];

    [Tooltip("Everything belonging to each stage. Only the current stage is switched on (saves performance).")]
    public GameObject[] stageRoots = new GameObject[3];

    [Tooltip("Navigation meshes rebuilt when the game starts (for the Crawler and the Mother).")]
    public NavMeshSurface[] navMeshes;

    [Tooltip("Narration line played when each stage begins.")]
    public string[] stageIntroLines = { "N1_01", "N2_01", "N3_01" };

    [Tooltip("Fog thickness per stage (the hotel corridor is a bit clearer so you can see down it).")]
    public float[] stageFog = { 0.055f, 0.085f, 0.075f };

    [Tooltip("Lights switched on when the basement fuse box is fixed.")]
    public Light[] basementPowerLights;

    public int CurrentStage { get; private set; } = 1;

    readonly HashSet<string> m_Items = new HashSet<string>();
    /// <summary>Public accessor for the player's inventory items.</summary>
    public HashSet<string> Items => m_Items;
    bool m_Busy;

    void Awake() => Instance = this;

    IEnumerator Start()
    {
        // No walking off before the opening instruction (N1_01) has played; it releases the hold when it ends.
        Narrator.HoldPlayer();

        foreach (var s in navMeshes)
            if (s != null && s.navMeshData == null) // baked in the editor (Tools > Horror Game > Bake Navigation); only built here as a fallback
                s.BuildNavMesh();

        if (stageSpawns.Length > 0 && stageSpawns[0] != null)
        {
            // wait for the PC / VR rig to finish setting up, then put the player at the start
            yield return null;
            yield return null;
            yield return null;
            PlayerRig.TeleportTo(stageSpawns[0]);
        }
        else if (stageSpawns.Length > 0 && stageSpawns[0] == null && PlayerRig.Head != null)
        {
            // Stage 1 starts wherever the player rig stands.
            var start = new GameObject("Stage 1 Spawn (runtime)").transform;
            start.position = PlayerRig.Feet;
            start.rotation = Quaternion.Euler(0f, PlayerRig.Head.eulerAngles.y, 0f);
            stageSpawns[0] = start;
        }

        SetActiveStage(1);
        foreach (var l in basementPowerLights)
            if (l != null) l.enabled = false;

        yield return new WaitForSeconds(2f);
        Narrator.SayOnce(stageIntroLines[0]);
        StartCoroutine(Stage1ScareSchedule());
        StartCoroutine(Stage2ScareSchedule());
    }

    // ------------------------------------------------------------ items
    public static void AddItem(string id)
    {
        if (Instance != null && !string.IsNullOrEmpty(id))
            Instance.m_Items.Add(id);
    }

    public static bool HasItem(string id) => Instance != null && Instance.m_Items.Contains(id);

    public static void RemoveItem(string id)
    {
        if (Instance != null && !string.IsNullOrEmpty(id))
            Instance.m_Items.Remove(id);
    }

    // ------------------------------------------------------------ stages
    public void GoToStage(int stage)
    {
        StartCoroutine(StageRoutine(stage));
    }

    IEnumerator StageRoutine(int stage)
    {
        // If the player is being caught right now, finish that first instead of dropping the door.
        while (m_Busy)
            yield return null;
        m_Busy = true;
        yield return ScreenFade.FadeTo(1f, 0.8f);
        SetActiveStage(stage);
        Narrator.OnStageChanged();
        PlayerRig.TeleportTo(stageSpawns[stage - 1]);
        yield return new WaitForSeconds(0.6f);
        yield return ScreenFade.FadeTo(0f, 1.2f);
        Narrator.SayOnce(stageIntroLines[stage - 1]);
        m_Busy = false;
    }

    void SetActiveStage(int stage)
    {
        CurrentStage = stage;
        if (stageFog != null && stage - 1 < stageFog.Length && stageFog[stage - 1] > 0f)
            RenderSettings.fogDensity = stageFog[stage - 1];
        for (var i = 0; i < stageRoots.Length; i++)
            if (stageRoots[i] != null)
                stageRoots[i].SetActive(i == stage - 1);
    }

    /// <summary>Basement fuse box fixed: lights on, exit unlocked.</summary>
    public void PowerOn()
    {
        AddItem("power");
        foreach (var l in basementPowerLights)
            if (l != null) l.enabled = true;
        GameAudio.Flat("power_on", 0.9f);
        GameAudio.HumLights(basementPowerLights);
        Narrator.SayOnce("N2_06");
    }

    // ------------------------------------------------------------ caught / win
    public static void PlayerCaught()
    {
        if (Instance != null && !Instance.m_Busy)
            Instance.StartCoroutine(Instance.CaughtRoutine());
    }

    IEnumerator CaughtRoutine()
    {
        m_Busy = true;
        GameAudio.Flat("caught_sting", 1f);
        yield return ScreenFade.FadeTo(1f, 0.15f);
        Narrator.Say("CAUGHT");
        yield return new WaitForSeconds(1.5f);
        EntityBase.ResetAll();
        PlayerRig.TeleportTo(stageSpawns[CurrentStage - 1]);
        yield return new WaitForSeconds(0.5f);
        yield return ScreenFade.FadeTo(0f, 1f);
        m_Busy = false;
    }

    // Schedules two ambient Watcher sightings in Stage 1 so the player never feels safe.
    IEnumerator Stage1ScareSchedule()
    {
        // First sighting: Watcher glimpsed at the far end of the hallway ~50 s in
        yield return new WaitForSeconds(Random.Range(45f, 65f));
        if (CurrentStage != 1) yield break;
        var head = PlayerRig.Head;
        if (head != null)
        {
            var ahead = head.position + head.forward * Random.Range(10f, 16f);
            ScareDirector.Appear(Monster.Watcher, ahead + Vector3.up * 0f, 2.2f);
            Narrator.SayOnce("N1_04");
        }

        // Second sighting: LookBehind while searching for the tape — the hotel hallway is never empty
        yield return new WaitForSeconds(Random.Range(60f, 90f));
        if (CurrentStage != 1) yield break;
        ScareDirector.LookBehind(Monster.Watcher, 8f);
    }

    // Gives the basement one extra InFace after the second battery — the tension was earned.
    IEnumerator Stage2ScareSchedule()
    {
        yield return new WaitForSeconds(Random.Range(30f, 50f));
        while (CurrentStage < 2)
            yield return new WaitForSeconds(2f);
        if (CurrentStage != 2) yield break;
        yield return new WaitForSeconds(Random.Range(25f, 45f));
        if (CurrentStage != 2) yield break;
        ScareDirector.InFace(Monster.Crawler, 1.0f);
    }

    public void Win()
    {
        AddItem("won"); // nothing left to do (Objectives)
        StartCoroutine(WinRoutine());
    }

    IEnumerator WinRoutine()
    {
        while (m_Busy)
            yield return null;
        m_Busy = true;
        EntityBase.FreezeAll();
        GameAudio.Flat("win_outside", 0.8f);
        yield return ScreenFade.FadeTo(1f, 2f);
        Narrator.Say("N3_06");
        yield return null;
        while (Narrator.IsSpeaking)
            yield return null;
        yield return new WaitForSeconds(1f);
        Credits.Show();
    }
}

/// <summary>Fades the view to black and back (a black panel just in front of the eyes, works in VR too).</summary>
public class ScreenFade : MonoBehaviour
{
    static ScreenFade s_Instance;
    Material m_Mat;
    float m_Alpha;

    static ScreenFade Get()
    {
        if (s_Instance != null)
            return s_Instance;
        var head = PlayerRig.Head;
        if (head == null)
            return null;
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Screen Fade";
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(head, false);
        quad.transform.localPosition = new Vector3(0f, 0f, 0.12f);
        quad.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_ZTest", (float)CompareFunction.Always);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = 4000;
        var r = quad.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        s_Instance = quad.AddComponent<ScreenFade>();
        s_Instance.m_Mat = mat;
        s_Instance.Apply(0f);
        return s_Instance;
    }

    void Apply(float a)
    {
        m_Alpha = a;
        m_Mat.SetColor("_BaseColor", new Color(0f, 0f, 0f, a));
        GetComponent<MeshRenderer>().enabled = a > 0.001f;
    }

    /// <summary>Sets the fade at once, no transition (a hard cut).</summary>
    public static void Set(float alpha)
    {
        var f = Get();
        if (f != null)
            f.Apply(alpha);
    }

    public static IEnumerator FadeTo(float target, float seconds)
    {
        var f = Get();
        if (f == null)
            yield break;
        var start = f.m_Alpha;
        for (var t = 0f; t < seconds; t += Time.deltaTime)
        {
            f.Apply(Mathf.Lerp(start, target, t / seconds));
            yield return null;
        }
        f.Apply(target);
    }
}
