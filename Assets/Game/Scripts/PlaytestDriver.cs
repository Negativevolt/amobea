using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Automatic playtest of THE MARIGOLD in PC mode. Runs when Unity is started with -autoPlaytest, or when the file
/// Downloads/HorrorPack_src/_tools/autoplaytest.flag exists when Play starts (the flag is then deleted).
/// Walks the whole game, logs [Playtest] PASS/FAIL and saves what the player sees as screenshots.
/// </summary>
public class PlaytestDriver : MonoBehaviour
{
    const string k_Root = @"C:\Users\VISHESH\Downloads\HorrorPack_src\_tools\";
    const string k_Shots = k_Root + "playtest";
    int m_Fails;
    float m_Started;
    StreamWriter m_Log;

    /// <summary>True while the bot is playing (it always plays in PC mode, even in the VR-only project).</summary>
    public static bool Running;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Running = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var flag = k_Root + "autoplaytest.flag";
        var byFlag = File.Exists(flag);
        if (byFlag) File.Delete(flag);
        if (byFlag || System.Environment.GetCommandLineArgs().Contains("-autoPlaytest"))
        {
            Running = true;
            new GameObject("Playtest Driver").AddComponent<PlaytestDriver>();
        }
    }

    void Log(string msg)
    {
        Debug.Log("[Playtest] " + msg);
        m_Log?.WriteLine(System.DateTime.Now.ToString("HH:mm:ss") + " " + msg);
        m_Log?.Flush();
    }

    void Check(bool ok, string what)
    {
        if (!ok) m_Fails++;
        Log((ok ? "PASS  " : "FAIL  ") + what);
    }

    void OnLog(string msg, string stack, LogType type)
    {
        if ((type == LogType.Exception || type == LogType.Error) && !msg.StartsWith("[Playtest]"))
            Log("ERROR " + msg.Split('\n')[0] + " | " + (stack ?? "").Split('\n').FirstOrDefault());
    }

    void OnDestroy() => Application.logMessageReceived -= OnLog;

    IEnumerator Start()
    {
        m_Started = Time.realtimeSinceStartup;
        Directory.CreateDirectory(k_Shots);
        foreach (var f in Directory.GetFiles(k_Shots, "*.png")) File.Delete(f);
        m_Log = new StreamWriter(k_Root + "playtest_result.txt", false);
        Application.logMessageReceived += OnLog;
        var flow = FindAnyObjectByType<GameFlow>();
        yield return new WaitForSeconds(4f);
        Check(flow != null, "Game Flow exists");
        Check(PlayerRig.DesktopMode, "PC mode switched on (no headset)");
        var torch = FindAnyObjectByType<PSXFlashlight>();
        Check(torch != null && torch.transform.parent != PlayerRig.Head, "torch starts on the table, not in your hand");
        if (torch != null)
        {
            var dp = FindAnyObjectByType<DesktopPlayer>();
            dp.SendMessage("EquipTorch"); // same as looking at it and pressing E
        }
        yield return null;
        Check(torch != null && torch.IsOn && torch.transform.parent == PlayerRig.Head, "picked up the torch, it's on");
        var spawn1 = flow != null ? flow.stageSpawns[0] : null;
        Check(spawn1 != null && Vector3.Distance(PlayerRig.Feet, spawn1.position) < 1.5f, "player starts at the hotel lift");
        var breakables = FindObjectsByType<Destructible>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Check(breakables.Length >= 15, "breakable props in the game: " + breakables.Length);
        Shot("01_hotel_start");

        // ---------------- Stage 1: the corridor
        var R = spawn1 != null ? spawn1.position - new Vector3(0f, 0f, 1.1f) : Vector3.zero;
        var vase = breakables.FirstOrDefault(b => b.gameObject.activeInHierarchy && Vector3.Distance(b.transform.position, R) < 4f);
        if (vase != null)
        {
            Look(vase.transform.position);
            yield return new WaitForSeconds(0.3f);
            Shot("02_console_table");
            var before = FindObjectsByType<ShardFade>(FindObjectsSortMode.None).Length;
            vase.Break(vase.transform.position);
            yield return new WaitForSeconds(0.2f);
            Check(FindObjectsByType<ShardFade>(FindObjectsSortMode.None).Length > before, "smashing a vase makes shards");
        }
        for (var z = 1.6f; z <= 12.2f; z += 0.9f)
        {
            MovePlayer(R + new Vector3(0f, 0f, z), 0f);
            yield return new WaitForSeconds(0.45f);
            if (Mathf.Abs(z - 2.5f) < 0.45f) { yield return new WaitForSeconds(0.6f); Shot("03_knocking_302"); }
            if (Mathf.Abs(z - 7.9f) < 0.45f) { yield return new WaitForSeconds(1.6f); Shot("04_figure_crosses"); }
        }
        Look(R + new Vector3(-1.5f, 1.4f, 13f));
        yield return new WaitForSeconds(2.5f);
        Shot("05_306_open");

        // ---------------- room 305
        MovePlayer(R + new Vector3(2.2f, 0f, 14.5f), 90f);
        yield return new WaitForSeconds(0.6f);
        Look(R + new Vector3(4.6f, 1.0f, 14.6f));
        yield return new WaitForSeconds(0.4f);
        Shot("06_vhs_room");
        var tape = FindObjectsByType<Pickup>(FindObjectsSortMode.None).FirstOrDefault(p => p.itemId == "tape_305");
        Check(tape != null, "the labelled tape is in room 305");
        if (tape != null) tape.Interact();
        Check(GameFlow.HasItem("tape_305"), "picked up the tape");
        yield return new WaitForSeconds(4.2f);
        Shot("07_whisper_behind");
        Check(!ScareDirector.Busy, "taking the tape builds suspense (no lunge yet)");
        var vcr = FindObjectsByType<UseTarget>(FindObjectsSortMode.None).FirstOrDefault(u => u.requiredItems.Contains("tape_305"));
        Check(vcr != null, "VCR in room 305");
        Look(R + new Vector3(4.6f, 1.0f, 14.7f));
        if (vcr != null) vcr.Interact();
        yield return new WaitForSeconds(2.0f);
        Shot("08_tv_code");
        for (var t = 0f; t < 12f && !GameFlow.HasItem("code_seen"); t += 0.25f)
            yield return new WaitForSeconds(0.25f);
        Check(GameFlow.HasItem("code_seen"), "the tape showed the code");
        yield return new WaitForSeconds(1.15f);
        Shot("09_tv_face");
        yield return new WaitForSeconds(3f);
        Look(R + new Vector3(3.4f, 1.2f, 12.6f));
        yield return new WaitForSeconds(0.2f);
        Shot("10_code_on_wall");

        // ---------------- leaving 305, end of the corridor, keypad
        MovePlayer(R + new Vector3(0.3f, 0f, 14.5f), 0f);
        yield return new WaitForSeconds(1.5f);
        Shot("11_after_slam");
        MovePlayer(R + new Vector3(0f, 0f, 17.6f), 0f);
        yield return new WaitForSeconds(0.6f);
        Look(R + new Vector3(0f, 1.45f, 22f));
        yield return new WaitForSeconds(0.3f);
        Shot("12_window_hands");
        var keypad = FindAnyObjectByType<Keypad>();
        Check(keypad != null, "stairwell keypad exists");
        if (keypad != null)
        {
            MovePlayer(new Vector3(keypad.transform.position.x + 0.9f, 0f, keypad.transform.position.z), -90f);
            yield return new WaitForSeconds(0.3f);
            Look(keypad.transform.position);
            yield return new WaitForSeconds(0.3f);
            Shot("13_keypad");
            foreach (var c in "1234") keypad.Press(c);
            yield return new WaitForSeconds(1.2f);
            Check(flow.CurrentStage == 1, "wrong code keeps the door shut");
            foreach (var c in "0317") { keypad.Press(c); yield return new WaitForSeconds(0.15f); }
            yield return new WaitForSeconds(1.0f);
            Shot("14_door_open");
            yield return new WaitForSeconds(1.15f);
            Shot("15_stairs_face");
        }
        for (var t = 0f; t < 8f && flow.CurrentStage != 2; t += 0.25f)
            yield return new WaitForSeconds(0.25f);
        Check(flow.CurrentStage == 2, "the code opened the stairwell -> stage 2 (basement)");
        yield return new WaitForSeconds(2.5f);
        Shot("16_basement_corridor");

        // ---------------- Stage 2
        var spawn2 = flow.stageSpawns[1];
        for (var dz = 0.5f; dz < 9.5f && !ScareDirector.Busy; dz += 0.5f)
        {
            MovePlayer(spawn2.position + new Vector3(0f, 0f, dz), 0f);
            yield return new WaitForSeconds(0.35f);
        }
        yield return new WaitForSeconds(0.12f);
        Shot("17_pale_man_face");
        Check(ScareDirector.Busy, "the man at the end of the corridor lunges at you");
        yield return new WaitForSeconds(2.5f);
        MovePlayer(spawn2.position + new Vector3(0f, 0f, 12.4f), 0f);
        yield return new WaitForSeconds(0.5f);
        Shot("17b_junction");
        Check(FindAnyObjectByType<CrawlerEntity>() == null && FindAnyObjectByType<MotherEntity>() == null, "no roaming monsters (scripted scenes only)");

        var batteries = FindObjectsByType<Pickup>(FindObjectsSortMode.None).Where(p => p.itemId.StartsWith("battery")).OrderBy(p => p.itemId).ToList();
        Check(batteries.Count == 3, "three batteries placed (" + batteries.Count + ")");
        foreach (var b in batteries)
        {
            MovePlayer(b.transform.position + Vector3.back * 0.8f, 0f);
            yield return new WaitForSeconds(0.3f);
            b.Interact();
            if (b.itemId == "battery_1")
            {
                yield return new WaitForSeconds(0.4f);
                yield return new WaitForSeconds(3.2f);
                Shot("18_crawler_by_the_table");
                yield return new WaitForSeconds(2.5f);
            }
            if (b.itemId == "battery_2")
                yield return new WaitForSeconds(9f); // let the look-behind scare time out
        }
        Check(GameFlow.HasItem("battery_1") && GameFlow.HasItem("battery_2") && GameFlow.HasItem("battery_3"), "collected all three batteries");
        var fuse = FindObjectsByType<UseTarget>(FindObjectsSortMode.None).FirstOrDefault(u => u.requiredItems.Contains("battery_3"));
        if (fuse != null) MovePlayer(fuse.transform.position + new Vector3(-1.0f, 0f, 0f), 90f);
        if (fuse != null) fuse.Interact();
        Check(GameFlow.HasItem("power"), "fuse box fixed, power on");
        yield return new WaitForSeconds(1.5f);
        yield return new WaitForSeconds(0.2f);
        Shot("19_power_on");
        yield return new WaitForSeconds(1.5f);
        var exit2 = FindObjectsByType<UseTarget>(FindObjectsSortMode.None).FirstOrDefault(u => u.requiredItems.Contains("power"));
        if (exit2 != null) exit2.Interact();
        for (var t = 0f; t < 6f && flow.CurrentStage != 3; t += 0.25f)
            yield return new WaitForSeconds(0.25f);
        Check(flow.CurrentStage == 3, "metal door led to stage 3 (the owner's flat)");
        yield return new WaitForSeconds(2.5f);
        Shot("20_wooden_passage");

        // ---------------- Stage 3: the train
        var spawn3 = flow.stageSpawns[2];
        Shot("20_train_corridor");
        for (var dz = 0.5f; dz < 10.5f; dz += 0.8f)
        {
            MovePlayer(spawn3.position + new Vector3(0f, 0f, dz), 0f);
            yield return new WaitForSeconds(0.35f);
        }
        yield return new WaitForSeconds(0.4f);
        Shot("21_train_far_lamp");
        var tape3 = FindObjectsByType<Pickup>(FindObjectsSortMode.None).FirstOrDefault(p => p.itemId == "vhs");
        Check(tape3 != null, "tape is in the teddy");
        if (tape3 != null)
        {
            MovePlayer(new Vector3(spawn3.position.x + 1.0f, 0f, tape3.transform.position.z - 0.6f), 90f);
            Look(tape3.transform.position);
            yield return new WaitForSeconds(0.3f);
            tape3.Interact();
        }
        Shot("21b_tape_on_bunk");
        yield return new WaitForSeconds(2f);
        var vcr3 = FindObjectsByType<UseTarget>(FindObjectsSortMode.None).FirstOrDefault(u => u.requiredItems.Contains("vhs"));
        if (vcr3 != null)
        {
            MovePlayer(new Vector3(spawn3.position.x + 1.05f, 0f, vcr3.transform.position.z - 0.7f), 90f);
            Look(vcr3.transform.position + Vector3.up * 0.3f);
            vcr3.Interact();
        }
        yield return new WaitForSeconds(1.2f);
        Shot("22_tv_static");
        for (var t = 0f; t < 6f && !GameFlow.HasItem("tape_watched"); t += 0.25f)
            yield return new WaitForSeconds(0.25f);
        Check(GameFlow.HasItem("tape_watched"), "watched the tape");
        yield return new WaitForSeconds(1.5f);
        Shot("23_mother_face");
        yield return new WaitForSeconds(3f);
        Shot("24_mother_at_the_door");

        var exit3 = FindObjectsByType<UseTarget>(FindObjectsSortMode.None).FirstOrDefault(u => u.requiredItems.Contains("tape_watched"));
        if (exit3 != null)
        {
            MovePlayer(exit3.transform.position + Vector3.back * 1.2f, 0f);
            yield return new WaitForSeconds(0.3f);
            exit3.Interact();
        }
        yield return new WaitForSeconds(3f);

        Log(m_Fails == 0 ? "ALL CHECKS PASSED" : m_Fails + " CHECK(S) FAILED");
        Finish(m_Fails == 0 ? 0 : 1);
    }

    void Finish(int code)
    {
        m_Log?.Close();
        m_Log = null;
#if UNITY_EDITOR
        if (Application.isBatchMode)
            UnityEditor.EditorApplication.Exit(code);
        else
            UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    void Update()
    {
        if (m_Started > 0f && Time.realtimeSinceStartup - m_Started > 300f)
        {
            Log("TIMEOUT");
            m_Started = 0f;
            Finish(2);
        }
    }

    static void MovePlayer(Vector3 feet, float yaw)
    {
        var spot = new GameObject("tp").transform;
        feet.y = 0f; // every floor in the game is at height 0
        spot.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
        PlayerRig.TeleportTo(spot);
        Destroy(spot.gameObject);
    }

    static void Look(Vector3 target)
    {
        var head = PlayerRig.Head;
        var origin = PlayerRig.Origin;
        if (head == null || origin == null) return;
        var to = target - head.position;
        var flat = new Vector3(to.x, 0f, to.z);
        if (flat.sqrMagnitude > 0.001f)
            origin.transform.rotation = Quaternion.LookRotation(flat) * Quaternion.Inverse(Quaternion.Euler(0f, head.localEulerAngles.y, 0f));
        var pitch = -Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg;
        head.localRotation = Quaternion.Euler(pitch, head.localEulerAngles.y, 0f);
    }

    static void Shot(string name)
    {
        var cam = PlayerRig.Head != null ? PlayerRig.Head.GetComponent<Camera>() : Camera.main;
        if (cam == null) return;
        var rt = new RenderTexture(960, 540, 24);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
        tex.Apply();
        File.WriteAllBytes(Path.Combine(k_Shots, name + ".png"), tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = prev;
        Destroy(rt);
        Destroy(tex);
    }
}
