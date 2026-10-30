using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Tools > Horror Game > Build Game: builds "THE MARIGOLD" into "Main VR".
///   Stage 1 - Hotel, 3rd floor: wallpapered corridor, room 305 full of VHS tapes, the stairwell keypad (code on a tape).
///   Stage 2 - Service basement: concrete corridor with the pale man at the end, then the fuse box room + the Crawler.
///   Stage 3 - The owner's flat: a dark wooden passage, the nursery, the last tape and the Mother.
/// Nobody dies: monsters lunge into your face (ScareDirector). Breakable props everywhere.
/// The old test playground and corridor are kept (switched off) under "Old Test Area (disabled)".
/// Safe to run again: everything it built is rebuilt.
/// </summary>
static class GameBuilder
{
    const string k_Models = "Assets/HorrorPack/Models/";
    const string k_Mats = "Assets/HorrorPack/Materials/";
    const string k_GameMats = "Assets/Game/Materials/";
    const string k_GameTex = "Assets/Game/Textures/";
    const float T = 2.5f;          // modular tile size of the pack's walls
    const float H = 2.769f;        // pack wall height
    const float W = 0.2f;          // pack wall thickness
    const float HH = 2.7f;         // hotel ceiling height
    const float WT = 0.12f;        // hotel wall thickness

    static readonly Vector3 k_Stage1 = new Vector3(-60f, 0f, 0f);
    static readonly Vector3 k_Stage2 = new Vector3(60f, 0f, 0f);
    static readonly Vector3 k_Stage3 = new Vector3(120f, 0f, 0f);

    static Font s_Font;

    [MenuItem("Tools/Horror Game/Build Game")]
    public static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var log = new List<string>();
        // Rebuilding recreates the three levels from scratch, which wipes hand edits made inside them.
        // Ask first (when someone is at the editor), and always keep a copy of the scene as it was.
        if (!Application.isBatchMode && !EditorUtility.DisplayDialog("Rebuild the levels?",
                "This recreates Stage 1, 2 and 3 from scratch. Anything you moved or added inside them by hand will be replaced.\n\nA backup of the current scene is saved first (Downloads/HorrorPack_src/_tools/scene_backups).",
                "Rebuild", "Cancel"))
            return;
        try
        {
            var dir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads", "HorrorPack_src", "_tools", "scene_backups");
            System.IO.Directory.CreateDirectory(dir);
            if (!string.IsNullOrEmpty(scene.path))
                System.IO.File.Copy(scene.path, System.IO.Path.Combine(dir, scene.name + " " + System.DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".unity"), true);
        }
        catch (System.Exception e) { Debug.LogWarning("[Horror Game] Couldn't back up the scene: " + e.Message); }
        AssetDatabase.Refresh();
        s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // keep the torch, retire the old test area
        var torch = Object.FindAnyObjectByType<PSXFlashlight>(FindObjectsInactive.Include);
        if (torch != null)
            torch.transform.SetParent(null, true);
        RetireOldArea(log);

        foreach (var name in new[] { "Stage 1 - Hotel", "Stage 2 - Basement", "Stage 3 - Nursery", "Stage 3 - Train", "Game Flow", "NavMesh Stage 2", "NavMesh Stage 3", "Scare Director" })
        {
            var old = GameObject.Find(name);
            if (old != null) Object.DestroyImmediate(old);
        }

        var flow = new GameObject("Game Flow").AddComponent<GameFlow>();
        ScareTemplates(log);
        var stage1 = Stage1(flow, torch, log, out var spawn1);
        var stage2 = Stage2(flow, log, out var spawn2, out var nav2, out var powerLights);
        var stage3 = Stage3(flow, log, out var spawn3, out var nav3);

        flow.stageRoots = new[] { stage1, stage2, stage3 };
        flow.stageSpawns = new[] { spawn1, spawn2, spawn3 };
        flow.navMeshes = new[] { nav2, nav3 };
        flow.basementPowerLights = powerLights;

        PlayerAndPerformance(log);
        foreach (var e in Object.FindObjectsByType<EntityBase>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (e.transform.Find("Model") != null)
                log.Add(FixFacing(e));

        log.Add("Breakable props: " + Object.FindObjectsByType<Destructible>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add(PatchTentacleScares());
        EditorSceneManager.SaveScene(scene);
        log.Add(NavBake.BakeAll());
        Debug.Log("[Horror Game] Built THE MARIGOLD.\n" + string.Join("\n", log));
    }

    /// <summary>
    /// Adds the scripted Crawler / Mother scenes (no roaming monsters) to levels that already exist, without rebuilding
    /// them, so hand-made changes to the levels are kept. Safe to run twice.
    /// </summary>
    public static string PatchScriptedScenes()
    {
        var log = new List<string>();
        Transform Root(string n)
        {
            var g = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(x => x.name == n);
            return g != null ? g.transform : null;
        }
        void RemoveDead(UnityEventBase ev)
        {
            for (var i = ev.GetPersistentEventCount() - 1; i >= 0; i--)
                if (ev.GetPersistentTarget(i) == null)
                    UnityEventTools.RemovePersistentListener(ev, i);
        }

        // ---- Stage 2: the Crawler scene
        var r2 = Root("Stage 2 - Basement");
        if (r2 != null && r2.Find("Scares/Scene - lights die (battery 1)") == null)
        {
            foreach (var e in r2.GetComponentsInChildren<CrawlerEntity>(true))
                Object.DestroyImmediate(e.gameObject);
            foreach (var t in r2.GetComponentsInChildren<Transform>(true).Where(t => t != null && t.name == "Crawler Patrol").ToList())
                Object.DestroyImmediate(t.gameObject);
            var R = r2.position;
            const float CH = 2.8f;
            var scares = r2.Find("Scares") ?? Child(r2, "Scares");
            var bat1 = r2.GetComponentsInChildren<Pickup>(true).FirstOrDefault(p => p.itemId == "battery_1");
            if (bat1 != null)
            {
                RemoveDead(bat1.onPicked);
                var bpos = bat1.transform.position;
                var dieOut = Scare(scares, "Scene - lights die (battery 1)", ScareEvent.Kind.Blackout, bpos);
                dieOut.seconds = 2.6f; dieOut.radius = 9f; dieOut.delay = 0.6f;
                var skitter = Scare(scares, "Scene - skittering in the dark", ScareEvent.Kind.Sound, R + new Vector3(-4.4f, 0.3f, 18.4f));
                skitter.sound = "crawler_skitter"; skitter.delay = 1.2f; skitter.volume = 0.9f;
                var crouched = Scare(scares, "Scene - it's by the table", ScareEvent.Kind.Appear, R + new Vector3(-4.5f, 0f, 18.3f));
                crouched.monster = Monster.Crawler; crouched.delay = 3.3f; crouched.seconds = 1.6f; crouched.narrationId = "N2_04";
                var breath = Scare(scares, "Scene - breathing", ScareEvent.Kind.Sound, R + new Vector3(-4.5f, 0.4f, 18.3f));
                breath.sound = "crawler_breath"; breath.delay = 3.3f; breath.volume = 1f;
                UnityEventTools.AddPersistentListener(bat1.onPicked, dieOut.Play);
                UnityEventTools.AddPersistentListener(bat1.onPicked, skitter.Play);
                UnityEventTools.AddPersistentListener(bat1.onPicked, crouched.Play);
                UnityEventTools.AddPersistentListener(bat1.onPicked, breath.Play);
                EditorUtility.SetDirty(bat1);
            }
            var duct = Zone("Trigger - junction", scares, R + new Vector3(0f, 1.5f, 13.2f), new Vector3(2.2f, 3f, 0.8f), null);
            var ductSound = Scare(scares, "Sound - crawling in the ducts", ScareEvent.Kind.Sound, R + new Vector3(1.5f, CH - 0.1f, 15f));
            ductSound.sound = "crawler_skitter"; ductSound.delay = 0.8f; ductSound.volume = 0.55f;
            UnityEventTools.AddPersistentListener(duct.onEnter, ductSound.Play);
            log.Add("Stage 2: Crawler removed, battery / junction scenes added.");
        }

        // ---- Stage 3: the Mother scenes
        var r3 = Root("Stage 3 - Train");
        if (r3 != null && r3.Find("Scares/Scene - she is at the front door") == null)
        {
            foreach (var e in r3.GetComponentsInChildren<MotherEntity>(true))
                Object.DestroyImmediate(e.gameObject);
            var R = r3.position;
            const float L = 19.8f;
            var scares = r3.Find("Scares") ?? Child(r3, "Scares");
            var tvScare = r3.GetComponentsInChildren<TVScare>(true).FirstOrDefault();
            var lamps = r3.GetComponentsInChildren<Light>(true).Where(l => l.name == "Corridor Lamp").ToList();
            var lampFlickers = new List<FlickerLight>();
            foreach (var l in lamps)
            {
                var fl = l.GetComponent<FlickerLight>();
                if (fl == null) { fl = l.gameObject.AddComponent<FlickerLight>(); fl.buzz = 0f; fl.blackoutsPerSecond = 0f; }
                lampFlickers.Add(fl);
            }
            var allDark = Scare(scares, "Scene - corridor goes dark (after the tape)", ScareEvent.Kind.Blackout, R + new Vector3(0f, 2f, L / 2f));
            allDark.lights = lampFlickers.ToArray();
            allDark.radius = L; allDark.seconds = 2.2f; allDark.delay = 0.5f; allDark.narrationId = "N3_04";
            var atDoor = Scare(scares, "Scene - she is at the front door", ScareEvent.Kind.Appear, R + new Vector3(0f, 0f, L - 0.9f));
            atDoor.monster = Monster.Mother; atDoor.delay = 2.8f; atDoor.seconds = 3f; atDoor.narrationId = "N3_03";
            var music = Scare(scares, "Scene - music box at the door", ScareEvent.Kind.Sound, R + new Vector3(0f, 1.2f, L - 0.9f));
            music.sound = "musicbox"; music.delay = 2.8f; music.volume = 0.7f;
            if (tvScare != null)
            {
                RemoveDead(tvScare.onFinished);
                UnityEventTools.AddPersistentListener(tvScare.onFinished, allDark.Play);
                UnityEventTools.AddPersistentListener(tvScare.onFinished, atDoor.Play);
                UnityEventTools.AddPersistentListener(tvScare.onFinished, music.Play);
                EditorUtility.SetDirty(tvScare);
            }
            var nearDoor = Zone("Trigger - near the front door", scares, R + new Vector3(0f, 1.5f, L - 2.6f), new Vector3(1.3f, 3f, 0.7f), null);
            nearDoor.requiredItem = "tape_watched";
            var whisper = Scare(scares, "Scene - whisper behind you", ScareEvent.Kind.Sound, R + new Vector3(0f, 1.6f, L - 3.6f));
            whisper.sound = "whisper"; whisper.volume = 1f;
            var behind = Scare(scares, "Scene - she is behind you", ScareEvent.Kind.LookBehind, R);
            behind.monster = Monster.Mother; behind.delay = 0.3f; behind.seconds = 5f;
            UnityEventTools.AddPersistentListener(nearDoor.onEnter, whisper.Play);
            UnityEventTools.AddPersistentListener(nearDoor.onEnter, behind.Play);
            log.Add("Stage 3: Mother removed, tape / door scenes added (" + lamps.Count + " corridor lamps).");
        }
        return string.Join("\n", log);
    }

    /// <summary>
    /// Stages 1 and 2 only (stage 3 is being reworked): tentacles slap the hotel window, drop from the tunnel ceiling,
    /// the tentacle thing is behind you after the third battery. Also dims the hotel lights. Edits the existing levels in
    /// place (keeps hand changes) and is safe to run twice.
    /// </summary>
    public static string PatchTentacleScares()
    {
        var log = new List<string>();
        Transform Root(string n)
        {
            var g = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(x => x.name == n);
            return g != null ? g.transform : null;
        }
        var r1 = Root("Stage 1 - Hotel");
        if (r1 != null)
        {
            var scares = r1.Find("Scares") ?? Child(r1, "Scares");
            if (scares.Find("Scene - tentacles at the window") == null)
            {
                const float L = 22f;
                var zone = scares.GetComponentsInChildren<TriggerZone>(true).FirstOrDefault(z => z.name == "Trigger - window hands");
                var burst = Scare(scares, "Scene - tentacles at the window", ScareEvent.Kind.TentacleBurst, r1.position + new Vector3(0f, 1.45f, L + 0.3f));
                burst.transform.rotation = Quaternion.LookRotation(Vector3.back);
                burst.seconds = 1.7f; burst.reachLimit = 0.32f; burst.delay = 0.35f;
                if (zone != null)
                {
                    UnityEventTools.AddPersistentListener(zone.onEnter, burst.Play);
                    EditorUtility.SetDirty(zone);
                }
                log.Add("Stage 1: tentacles slap the window" + (zone != null ? "" : " (window trigger NOT found)") + ".");
            }
            if (r1.Find("_lights dimmed") == null)
            {
                var n = 0;
                foreach (var l in r1.GetComponentsInChildren<Light>(true))
                {
                    l.intensity *= 0.6f;
                    EditorUtility.SetDirty(l);
                    n++;
                }
                new GameObject("_lights dimmed").transform.SetParent(r1, false);
                log.Add("Stage 1: " + n + " lights dimmed to 60%.");
            }
        }
        var r2 = Root("Stage 2 - Basement");
        if (r2 != null)
        {
            var scares = r2.Find("Scares") ?? Child(r2, "Scares");
            var R = r2.position;
            const float CH = 2.8f;
            if (scares.Find("Scene - tentacles from the ceiling") == null)
            {
                var zone = scares.GetComponentsInChildren<TriggerZone>(true).FirstOrDefault(z => z.name == "Trigger - junction");
                var drop = Scare(scares, "Scene - tentacles from the ceiling", ScareEvent.Kind.TentacleBurst, R + new Vector3(0f, CH - 0.02f, 14.6f));
                drop.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                drop.seconds = 1.8f; drop.reachLimit = 1.3f; drop.delay = 1.6f;
                if (zone != null)
                {
                    UnityEventTools.AddPersistentListener(zone.onEnter, drop.Play);
                    EditorUtility.SetDirty(zone);
                }
                log.Add("Stage 2: tentacles drop from the junction ceiling" + (zone != null ? "" : " (junction trigger NOT found)") + ".");
            }
            if (scares.Find("Scene - the tentacle thing behind you") == null)
            {
                var bat3 = r2.GetComponentsInChildren<Pickup>(true).FirstOrDefault(p => p.itemId == "battery_3");
                var behind = Scare(scares, "Scene - the tentacle thing behind you", ScareEvent.Kind.LookBehind, R);
                behind.monster = Monster.Tentacle; behind.delay = 2.5f; behind.seconds = 6f;
                if (bat3 != null)
                {
                    UnityEventTools.AddPersistentListener(bat3.onPicked, behind.Play);
                    EditorUtility.SetDirty(bat3);
                }
                log.Add("Stage 2: the tentacle thing is behind you after battery 3" + (bat3 != null ? "" : " (battery 3 NOT found)") + ".");
            }
        }
        return string.Join("\n", log);
    }

    static void RetireOldArea(List<string> log)
    {
        var old = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "Old Test Area (disabled)");
        if (old == null)
            old = new GameObject("Old Test Area (disabled)");
        var apartment = GameObject.Find("Stage 1 - Apartment");
        foreach (var n in new[] { "Destruction Playground", "Dark Corridor", "Plane" })
        {
            Transform t = apartment != null ? apartment.transform.Find(n) : null;
            if (t == null) { var go = GameObject.Find(n); t = go != null ? go.transform : null; }
            if (t != null) t.SetParent(old.transform, true);
        }
        if (apartment != null)
            Object.DestroyImmediate(apartment); // the old scares/markers built on top of it
        old.SetActive(false);
        log.Add("Old playground / corridor moved into 'Old Test Area (disabled)' (switched off, not deleted).");
    }

    // ================================================================== STAGE 1: THE MARIGOLD, FLOOR 3
    static GameObject Stage1(GameFlow flow, PSXFlashlight torch, List<string> log, out Transform spawn)
    {
        var root = new GameObject("Stage 1 - Hotel").transform;
        root.position = k_Stage1;
        var structure = Child(root, "Structure");
        var props = Child(root, "Props");
        var lightsRoot = Child(root, "Lights");
        var scares = Child(root, "Scares");
        var R = root.position;
        const float L = 22f;      // corridor length
        const float X = 1.1f;     // half width

        var wallpaper = GMat("hotel_wallpaper", "wallpaper_floral");
        var wainscot = GMat("hotel_wainscot", "wainscot");
        var parquet = GMat("hotel_parquet", "parquet", smooth: 0.25f);
        var ceiling = GMat("hotel_ceiling", "ceiling_popcorn");
        var trim = GMat("hotel_trim", "wainscot", new Color(0.55f, 0.45f, 0.4f));
        var brass = Mat("brass");
        var metalDark = GMat("metal_dark", null, new Color(0.12f, 0.12f, 0.12f), smooth: 0.4f);
        var tubeGlow = GMat("tube_glow", null, new Color(0.9f, 1f, 0.95f), emission: new Color(0.9f, 1f, 0.95f) * 1.6f);
        var darkRoom = GMat("dark_room", "concrete_wall", new Color(0.15f, 0.15f, 0.15f));
        var white = Mat("paint_white_peel");

        // ---- shell: floor / ceiling in 5.5 m pieces (so each piece only gets the lamps near it)
        for (var z = 0f; z < L; z += 5.5f)
        {
            var len = Mathf.Min(5.5f, L - z);
            Box(structure, "Floor", R + new Vector3(0f, -0.05f, z + len / 2f), new Vector3(2 * X + 2 * WT, 0.1f, len), parquet, 1.0f);
            Box(structure, "Ceiling", R + new Vector3(0f, HH + 0.05f, z + len / 2f), new Vector3(2 * X + 2 * WT, 0.1f, len), ceiling, 1.5f);
        }
        var leftDoors = new[] { (3f, "302"), (8f, "304"), (13f, "306"), (18f, "308") };
        var rightDoors = new[] { (4.5f, "301"), (9.5f, "303"), (14.5f, "305"), (19.5f, "307") };
        const float stairZ = 20.8f;
        var leftOpenings = leftDoors.Select(d => Door(d.Item1)).Append(Door(stairZ)).ToArray();
        var rightOpenings = rightDoors.Select(d => Door(d.Item1)).ToArray();
        Wall(structure, "Wall L", true, R.x - X - WT / 2f, R.z, R.z + L, WT, Offset(leftOpenings, R.z), wallpaper, wainscot, trim, HH);
        Wall(structure, "Wall R", true, R.x + X + WT / 2f, R.z, R.z + L, WT, Offset(rightOpenings, R.z), wallpaper, wainscot, trim, HH);
        Wall(structure, "Wall Lift End", false, R.z - WT / 2f, R.x - X - WT, R.x + X + WT, WT, new[] { new Opening(R.x, 1.1f, 0f, 2.15f, false) }, wallpaper, wainscot, trim, HH);
        Wall(structure, "Wall Window End", false, R.z + L + WT / 2f, R.x - X - WT, R.x + X + WT, WT, new[] { new Opening(R.x, 1.2f, 0.85f, 2.05f, false) }, wallpaper, wainscot, trim, HH);

        // ---- doors (pack doors on hinges, numbered, all locked except 305)
        var doors = new Dictionary<string, HauntedDoor>();
        foreach (var (z, n) in leftDoors)
            doors[n] = HotelDoor(structure, n, true, R + new Vector3(0f, 0f, z), n == "305" ? 80f : 0f, brass);
        foreach (var (z, n) in rightDoors)
            doors[n] = HotelDoor(structure, n, false, R + new Vector3(0f, 0f, z), n == "305" ? 80f : 0f, brass);
        var stairDoor = HotelDoor(structure, "Stairwell", true, R + new Vector3(0f, 0f, stairZ), 0f, brass, "Door04", "Door.001");
        foreach (var kv in doors)
        {
            if (kv.Key == "305") continue;
            var use = kv.Value.gameObject.AddComponent<UseTarget>();
            use.requiredItems = new[] { "never" };
            use.prompt = "Room " + kv.Key;
            use.onlyOnce = false;
            use.lockedHint = kv.Key == "302" ? "Locked. Something on the other side is breathing." : kv.Key == "306" ? "Locked. It's cold to touch." : "Locked.";
        }
        var stairUse = stairDoor.gameObject.AddComponent<UseTarget>();
        stairUse.requiredItems = new[] { "never" };
        stairUse.onlyOnce = false;
        stairUse.prompt = "Stairwell";
        stairUse.lockedHint = "Locked. There's a keypad next to it.";

        // dark little spaces behind the doors that open (306, stairwell) so you don't see into the void
        DarkCloset(structure, "306 (dark)", R + new Vector3(-X - WT - 0.9f, 0f, 13f), new Vector3(1.8f, HH, 1.6f), darkRoom);
        DarkCloset(structure, "Stairwell (dark)", R + new Vector3(-X - WT - 1.1f, 0f, stairZ), new Vector3(2.2f, HH, 1.8f), darkRoom);
        for (var i = 0; i < 5; i++) // stairs going down into the dark
            Box(structure, "Stair", R + new Vector3(-X - WT - 0.6f - i * 0.28f, -0.12f - i * 0.18f, stairZ), new Vector3(0.28f, 0.18f, 1.2f), darkRoom, 1f, false);

        // ---- the lift at the start (dead) + signs
        var lift = GMat("lift_doors", "elevator", smooth: 0.5f);
        Box(structure, "Lift Door L", R + new Vector3(-0.275f, 1.075f, -0.04f), new Vector3(0.54f, 2.15f, 0.04f), lift, 1f, true, true, true);
        Box(structure, "Lift Door R", R + new Vector3(0.275f, 1.075f, -0.04f), new Vector3(0.54f, 2.15f, 0.04f), lift, 1f, true, true, true);
        Box(structure, "Lift Frame Top", R + new Vector3(0f, 2.2f, 0.005f), new Vector3(1.3f, 0.1f, 0.05f), brass, 1f, false);
        Box(structure, "Lift Frame L", R + new Vector3(-0.6f, 1.08f, 0.005f), new Vector3(0.1f, 2.16f, 0.05f), brass, 1f, false);
        Box(structure, "Lift Frame R", R + new Vector3(0.6f, 1.08f, 0.005f), new Vector3(0.1f, 2.16f, 0.05f), brass, 1f, false);
        Box(structure, "Lift Button", R + new Vector3(0.82f, 1.2f, 0.01f), new Vector3(0.06f, 0.1f, 0.02f), GMat("lift_button", null, new Color(1f, 0.6f, 0.2f), emission: new Color(1f, 0.5f, 0.15f) * 0.8f), 1f, false);
        Sign(structure, "Sign Marigold", R + new Vector3(0f, 2.42f, 0.012f), new Vector2(0.8f, 0.3f), Facing.PosZ, GMat("sign_marigold", "sign_marigold"));

        // ---- window at the end: the city at night, rain
        var city = GMat("night_city", "night_city", emission: new Color(0.75f, 0.75f, 0.8f));
        Box(structure, "Window - city", R + new Vector3(0f, 1.45f, L + 0.35f), new Vector3(1.8f, 1.6f, 0.02f), city, 1f, false, true, true);
        Box(structure, "Window Sill", R + new Vector3(0f, 0.82f, L - 0.04f), new Vector3(1.4f, 0.06f, 0.2f), white, 1f);
        Box(structure, "Window Bar V", R + new Vector3(0f, 1.45f, L + 0.02f), new Vector3(0.05f, 1.2f, 0.05f), white, 1f, false);
        Box(structure, "Window Bar H", R + new Vector3(0f, 1.6f, L + 0.02f), new Vector3(1.2f, 0.05f, 0.05f), white, 1f, false);
        var hands = Box(structure, "Handprints (hidden)", R + new Vector3(0.15f, 1.45f, L + 0.05f), new Vector3(0.7f, 0.7f, 0.005f), GMat("decal_hands", "decal_hands", cutout: true), 1f, false, false, true);
        hands.SetActive(false);
        var windowLight = PointLight(lightsRoot, "Window Light", R + new Vector3(0f, 1.5f, L - 0.6f), new Color(0.45f, 0.42f, 0.75f), 0.7f, 3.5f, LightShadows.None);
        windowLight.gameObject.AddComponent<LoopSound>().clip = "rain_window";

        // ---- fluorescent tubes (picture 1). #2 flickers, #3 will burst, #4 near the end is unstable.
        var tubes = new List<(Light light, Renderer tube)>();
        foreach (var z in new[] { 3.0f, 8.5f, 14.0f, 19.5f })
        {
            Box(props, "Tube Fixture", R + new Vector3(0f, HH - 0.03f, z), new Vector3(0.32f, 0.06f, 1.2f), metalDark, 1f, false);
            var tube = Box(props, "Tube", R + new Vector3(0f, HH - 0.07f, z), new Vector3(0.22f, 0.025f, 1.1f), tubeGlow, 1f, false).GetComponent<Renderer>();
            var l = PointLight(lightsRoot, "Tube Light", R + new Vector3(0f, HH - 0.3f, z), new Color(0.82f, 0.95f, 0.88f), 1.5f, 5.8f, LightShadows.None);
            tubes.Add((l, tube));
        }
        var f2 = tubes[1].light.gameObject.AddComponent<FlickerLight>(); f2.buzz = 0.08f; f2.blackoutsPerSecond = 0.15f;
        var f3 = tubes[2].light.gameObject.AddComponent<FlickerLight>(); f3.buzz = 0.05f; f3.blackoutsPerSecond = 0.05f;
        var f4 = tubes[3].light.gameObject.AddComponent<FlickerLight>(); f4.buzz = 0.2f; f4.blackoutsPerSecond = 0.4f;
        var burst = tubes[2].light.gameObject.AddComponent<LightBurst>();
        burst.lightSource = tubes[2].light;
        burst.tube = tubes[2].tube;

        // ---- dust in the air (picture 1)
        Dust(root, R + new Vector3(0f, 1.4f, L / 2f), new Vector3(2.1f, 2.6f, L));

        // ---- start: console table by the lift with the torch
        spawn = Marker("Stage 1 Spawn", root, R + new Vector3(0f, 0f, 1.1f), 0f);
        var console = Spawn("Table02", "Table02", props, R + new Vector3(-0.7f, 0f, 2.0f), 0f, 90f);
        AgainstWall(console, R.x - X, true);
        var top = Bounds(console).max.y;
        var cb = Bounds(console);
        if (torch != null)
        {
            torch.transform.SetParent(props, true);
            torch.transform.position = new Vector3(cb.center.x, top + 0.04f, cb.center.z - 0.15f);
            torch.transform.rotation = Quaternion.Euler(0f, 100f, 90f);
            var rb = torch.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;
            log.Add("Stage 1: torch placed on the console table by the lift.");
        }
        Spawn("Telephone", "Telephone", props, new Vector3(cb.center.x, 0f, cb.center.z + 0.25f), top, 80f, null, false);
        Breakable("Vase", "Vase", props, new Vector3(cb.center.x, 0f, cb.max.z - 0.12f), top, 0f, 1.2f, 2.5f, "ceramic_break");

        // ---- corridor dressing (all pack models) + breakables
        var chair = Spawn("Chair", "Chair", props, R + new Vector3(0.8f, 0f, 6.8f), 0f, -90f);
        AgainstWall(chair, R.x + X, false);
        NoBlock(chair);
        // a room-service tray someone left outside 302
        var trayZ = R.z + 3.9f;
        var trayX = R.x - X + 0.3f;
        Breakable("Plate", "Plate", props, new Vector3(trayX, 0f, trayZ), 0.002f, 0f, 0.4f, 2.2f, "ceramic_break");
        Breakable("Plate", "Plate", props, new Vector3(trayX + 0.05f, 0f, trayZ + 0.25f), 0.002f, 30f, 0.4f, 2.2f, "ceramic_break");
        Breakable("Teacup", "Teacup", props, new Vector3(trayX + 0.2f, 0f, trayZ - 0.15f), 0.002f, 70f, 0.2f, 2.0f, "ceramic_break");
        Breakable("Glass01", "Glass01", props, new Vector3(trayX + 0.25f, 0f, trayZ + 0.1f), 0.002f, 0f, 0.2f, 1.8f, "vase_shatter");
        Breakable("BottleWine01", "BottleWine01", props, new Vector3(trayX + 0.05f, 0f, trayZ + 0.5f), 0.002f, 0f, 0.8f, 2.2f, "vase_shatter");
        var stool = Spawn("Footstool01", "Footstool01", props, R + new Vector3(0.8f, 0f, 16.9f), 0f, 0f);
        AgainstWall(stool, R.x + X, false);
        var sb = Bounds(stool);
        Breakable("Vase", "Vase", props, new Vector3(sb.center.x, 0f, sb.center.z), sb.max.y, 40f, 1.2f, 2.5f, "ceramic_break");
        Spawn("Newspaper", "Newspaper", props, R + new Vector3(0.25f, 0f, 10.4f), 0.002f, 25f, null, false);
        Spawn("Envelope", "Envelope", props, R + new Vector3(-0.6f, 0f, 3.4f), 0.002f, 60f, null, false);
        Spawn("TrashBag01", "TrashBag01", props, R + new Vector3(0.85f, 0f, 0.5f), 0f, 0f);
        foreach (var (z, x) in new[] { (21.3f, 0.75f), (21.4f, 0.35f), (21.25f, 0.75f) })
            Breakable("CardboardBox01", "CardboardBox01", props, R + new Vector3(x, 0f, z), 0f, Random.Range(-15f, 15f), 3f, 4f, "cardboard_break");
        foreach (var z in new[] { 6f, 16f })
            Spawn("SmokeDetector", "SmokeDetector", props, R + new Vector3(0f, 0f, z), HH - 0.05f, 0f, null, false);
        foreach (var (z, left) in new[] { (5.6f, true), (11.8f, false), (16.2f, true), (2.2f, false) })
            WallThing("WallPhotoframe01", props, R, X, z, 1.45f, left);
        Sign(props, "Poster - missing", R + new Vector3(X - 0.005f, 1.45f, 12.4f), new Vector2(0.42f, 0.63f), Facing.NegX, GMat("poster_missing", "poster_missing"));
        Sign(props, "Handprints", R + new Vector3(-X + 0.005f, 1.05f, 3.75f), new Vector2(0.45f, 0.45f), Facing.PosX, GMat("decal_hands", "decal_hands", cutout: true));

        // ---- room 305: the VHS room (picture 4)
        var vcrUse = Room305(root, structure, props, lightsRoot, R, X, out var tvTape, out var codeDecal, out var tapePickup);

        // ---- stairwell keypad + signs
        Sign(structure, "Sign Staff Only", R + new Vector3(-X + 0.006f, 2.3f, stairZ), new Vector2(0.75f, 0.24f), Facing.PosX, GMat("sign_staff", "sign_staff_stairs"));
        Sign(structure, "Sign Exit", R + new Vector3(-X + 0.08f, 2.55f, stairZ), new Vector2(0.36f, 0.14f), Facing.PosX, GMat("sign_exit", "sign_exit", emission: new Color(0.6f, 1f, 0.6f)));
        var keypad = BuildKeypad(structure, R + new Vector3(-X + 0.015f, 1.3f, stairZ - 0.75f), Quaternion.LookRotation(Vector3.left), "0317");

        // ================= scares (in order along the corridor)
        // the lift dings as you arrive
        var arrive = Zone("Trigger - arrive", scares, R + new Vector3(0f, 1.5f, 1.1f), new Vector3(2.2f, 3f, 1.4f), null);
        var ding = Scare(scares, "Scare - lift ding", ScareEvent.Kind.Sound, R + new Vector3(0f, 1.5f, 0f));
        ding.sound = "elevator_ding"; ding.delay = 0.3f;
        UnityEventTools.AddPersistentListener(arrive.onEnter, ding.Play);

        // 1) pounding on 302 as you pass it
        var z1 = Zone("Trigger - 302 knock", scares, R + new Vector3(0f, 1.5f, 2.2f), new Vector3(2.2f, 3f, 0.8f), null);
        UnityEventTools.AddPersistentListener(z1.onEnter, doors["302"].Knock);
        var n105 = Scare(scares, "Narration - 302", ScareEvent.Kind.Nothing, R);
        n105.narrationId = "N1_05"; n105.delay = 2.6f;
        UnityEventTools.AddPersistentListener(z1.onEnter, n105.Play);

        // 2) the lamp above you dies and something crosses the far end of the corridor
        var z2 = Zone("Trigger - figure crosses", scares, R + new Vector3(0f, 1.5f, 7.4f), new Vector3(2.2f, 3f, 0.8f), null);
        var black2 = Scare(scares, "Scare - lamp dies", ScareEvent.Kind.Blackout, tubes[1].light.transform.position);
        black2.lights = new[] { f2 }; black2.seconds = 2.2f;
        var cross = Scare(scares, "Scare - figure crosses", ScareEvent.Kind.FigureCross, R);
        cross.from = Marker("Cross From", scares, R + new Vector3(-X + 0.1f, 0f, 18.6f), 90f);
        cross.to = Marker("Cross To", scares, R + new Vector3(X - 0.1f, 0f, 19.2f), 90f);
        cross.speed = 1.5f; cross.delay = 0.3f; cross.narrationId = null;
        var n104 = Scare(scares, "Narration - lights", ScareEvent.Kind.Nothing, R);
        n104.narrationId = "N1_04"; n104.delay = 3f;
        foreach (var e in new UnityAction[] { black2.Play, cross.Play, n104.Play })
            UnityEventTools.AddPersistentListener(z2.onEnter, e);

        // 3) 306 creaks open on its own; something whispers in the dark behind it
        var z3 = Zone("Trigger - 306 opens", scares, R + new Vector3(0f, 1.5f, 11.2f), new Vector3(2.2f, 3f, 0.8f), null);
        UnityEventTools.AddPersistentListener(z3.onEnter, doors["306"].CreakOpen);
        var whisper = Scare(scares, "Scare - whisper in 306", ScareEvent.Kind.Sound, R + new Vector3(-X - 1f, 1.5f, 13f));
        whisper.sound = "whisper"; whisper.delay = 3f; whisper.volume = 0.9f;
        UnityEventTools.AddPersistentListener(z3.onEnter, whisper.Play);

        // 4) (after the tape) coming out of 305: 306 slams, the tube above bursts
        var z4 = Zone("Trigger - leaving 305", scares, R + new Vector3(0.3f, 1.5f, 14.5f), new Vector3(1.6f, 3f, 1.4f), null);
        z4.requiredItem = "code_seen";
        UnityEventTools.AddPersistentListener(z4.onEnter, doors["306"].Slam);
        var burstLater = Child(scares, "Scare - tube bursts").gameObject.AddComponent<DelayedEvent>();
        burstLater.delay = 0.7f;
        UnityEventTools.AddPersistentListener(burstLater.onFire, burst.Play);
        UnityEventTools.AddPersistentListener(z4.onEnter, burstLater.Play);

        // 5) near the end: hands slap the outside of the window (third floor...), 308 rattles
        var z5 = Zone("Trigger - window hands", scares, R + new Vector3(0f, 1.5f, 17.6f), new Vector3(2.2f, 3f, 0.8f), null);
        z5.requiredItem = "code_seen";
        var showHands = Scare(scares, "Scare - handprints on the window", ScareEvent.Kind.Show, R + new Vector3(0f, 1.5f, L));
        showHands.objects = new[] { hands }; showHands.sound = "knock_loud";
        UnityEventTools.AddPersistentListener(z5.onEnter, showHands.Play);
        var rattle = Child(scares, "Scare - 308 rattles").gameObject.AddComponent<DelayedEvent>();
        rattle.delay = 3.2f;
        UnityEventTools.AddPersistentListener(rattle.onFire, doors["308"].Rattle);
        UnityEventTools.AddPersistentListener(z5.onEnter, rattle.Play);

        // 6) the right code: door swings open, every light dies, and it's in your face. Then down the stairs.
        UnityEventTools.AddPersistentListener(keypad.onUnlocked, stairDoor.SwingOpen);
        var n107 = Scare(scares, "Narration - door open", ScareEvent.Kind.Nothing, R);
        n107.narrationId = "N1_07"; n107.delay = 0.3f;
        var blackAll = Scare(scares, "Scare - all lights die", ScareEvent.Kind.Blackout, R + new Vector3(0f, 1.5f, L / 2f));
        blackAll.radius = 40f; blackAll.seconds = 3f; blackAll.delay = 0.9f;
        var face = Scare(scares, "Sound - something runs down the corridor in the dark", ScareEvent.Kind.Sound, R + new Vector3(0f, 0.3f, 10f));
        face.sound = "knock_loud"; face.delay = 1.6f; face.volume = 0.9f;
        var down = Child(scares, "Go downstairs").gameObject.AddComponent<DelayedEvent>();
        down.delay = 5f;
        UnityEventTools.AddIntPersistentListener(down.onFire, flow.GoToStage, 2);
        foreach (var e in new UnityAction[] { n107.Play, blackAll.Play, face.Play, down.Play })
            UnityEventTools.AddPersistentListener(keypad.onUnlocked, e);

        // room 305 scares: something stands behind you after you take the tape; the code gets written on the wall
        // suspense: a whisper right behind you... nothing there. (The big hit is saved for the TV.)
        var behind = Scare(scares, "Sound - whisper behind you in 305", ScareEvent.Kind.Sound, R + new Vector3(1.9f, 1.6f, 14.5f));
        behind.sound = "whisper"; behind.delay = 3.5f; behind.volume = 0.9f;
        UnityEventTools.AddPersistentListener(tapePickup.onPicked, behind.Play);
        var steps = Scare(scares, "Sound - footsteps in the corridor", ScareEvent.Kind.Sound, R + new Vector3(0f, 0.2f, 17f));
        steps.sound = "creak_floor"; steps.delay = 6.5f; steps.volume = 0.8f;
        UnityEventTools.AddPersistentListener(tapePickup.onPicked, steps.Play);
        var showCode = Scare(scares, "Scare - code on the wall", ScareEvent.Kind.Show, codeDecal.transform.position);
        showCode.objects = new[] { codeDecal };
        UnityEventTools.AddPersistentListener(tvTape.onFinished, showCode.Play);
        UnityEventTools.AddPersistentListener(vcrUse.onUse, tvTape.Play);

        log.Add("Stage 1: Marigold corridor (8 rooms, lift, window), room 305 VHS room, keypad 0317, 8 scares.");
        return root.gameObject;
    }

    static UseTarget Room305(Transform root, Transform structure, Transform props, Transform lightsRoot, Vector3 R, float X, out TVCodeTape tvTape, out GameObject codeDecal, out Pickup tapePickup)
    {
        var x0 = R.x + X + WT;      // inside face of the corridor's right wall
        var x1 = R.x + 4.9f;
        var z0 = R.z + 12.6f;
        var z1 = R.z + 16.6f;
        var green = Mat("wallpaper_green");
        var wainscot = GMat("hotel_wainscot", "wainscot");
        var trim = GMat("hotel_trim", "wainscot", new Color(0.55f, 0.45f, 0.4f));
        var parquet = GMat("hotel_parquet", "parquet", smooth: 0.25f);
        var ceiling = GMat("hotel_ceiling", "ceiling_popcorn");
        var shelfMat = Mat("wood_dark");

        Box(structure, "305 Floor", new Vector3((x0 + x1) / 2f, -0.05f, (z0 + z1) / 2f), new Vector3(x1 - x0 + 0.3f, 0.1f, z1 - z0 + 0.3f), parquet, 1f);
        Box(structure, "305 Ceiling", new Vector3((x0 + x1) / 2f, HH + 0.05f, (z0 + z1) / 2f), new Vector3(x1 - x0 + 0.3f, 0.1f, z1 - z0 + 0.3f), ceiling, 1.5f);
        Wall(structure, "305 Wall Far", true, x1 + WT / 2f, z0 - WT, z1 + WT, WT, new Opening[0], green, wainscot, trim, HH);
        Wall(structure, "305 Wall S", false, z0 - WT / 2f, x0, x1, WT, new Opening[0], green, wainscot, trim, HH);
        Wall(structure, "305 Wall N", false, z1 + WT / 2f, x0, x1, WT, new Opening[0], green, wainscot, trim, HH);

        // desk against the far wall, shelves of tapes above it
        var desk = Spawn("Table01", "Table01", props, new Vector3(x1 - 0.5f, 0f, R.z + 14.6f), 0f, 90f);
        AgainstWall(desk, x1, false);
        var db = Bounds(desk);
        var top = db.max.y;
        var tv = Spawn("TV", "TV", props, new Vector3(db.center.x + 0.05f, 0f, db.center.z + 0.2f), top, 0f);
        FaceTowards(tv, Vector3.left);  // screen faces the room (it was facing the wall)
        var vcr = Spawn("VCR", "VCR", props, new Vector3(db.center.x - 0.05f, 0f, db.center.z - 0.45f), top + 0.002f, 0f);
        FaceTowards(vcr, Vector3.left);
        var tvbox = Bounds(tv);
        if (Bounds(vcr).max.z > tvbox.min.z - 0.03f)
            vcr.transform.position += new Vector3(0f, 0f, (tvbox.min.z - 0.03f) - Bounds(vcr).max.z);
        ClearStatic(vcr);
        for (var s = 0; s < 2; s++)
        {
            var y = 1.5f + s * 0.42f;
            Box(props, "Shelf", new Vector3(x1 - 0.15f, y, db.center.z), new Vector3(0.3f, 0.03f, 2.2f), shelfMat, 0.8f);
            for (var i = 0; i < 9; i++)
            {
                var tape = Spawn("VHSTape", "VHSTape", props, new Vector3(x1 - 0.15f, 0f, db.center.z - 0.95f + i * 0.21f + Random.Range(-0.02f, 0.02f)), y + 0.015f, Random.Range(-8f, 8f) + (i % 3 == 0 ? 90f : 0f), null, false);
                foreach (var c in tape.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            }
        }
        // VHS clutter on the desk (picture 4)
        for (var i = 0; i < 7; i++)
        {
            var p = new Vector3(Random.Range(db.min.x + 0.1f, db.max.x - 0.1f), 0f, Random.Range(db.min.z + 0.1f, db.center.z - 0.55f));
            if (i > 3) p.z = Random.Range(db.center.z + 0.6f, db.max.z - 0.1f);
            Spawn(i % 3 == 0 ? "Cassette" : "VHSTape", i % 3 == 0 ? "Cassette" : "VHSTape", props, p, top + 0.002f, Random.Range(0f, 360f), null, false);
        }
        Spawn("Newspaper", "Newspaper", props, new Vector3(db.center.x - 0.15f, 0f, db.max.z - 0.25f), top + 0.003f, 20f, null, false);
        Spawn("Letter", "Letter", props, new Vector3(db.min.x + 0.15f, 0f, db.center.z + 0.65f), top + 0.003f, 75f, null, false);
        Breakable("CoffeeMug", "CoffeeMug", props, new Vector3(db.min.x + 0.12f, 0f, db.min.z + 0.2f), top, 30f, 0.3f, 2f, "ceramic_break");
        Breakable("Glass02", "Glass02", props, new Vector3(db.min.x + 0.15f, 0f, db.max.z - 0.15f), top, 0f, 0.2f, 1.8f, "vase_shatter");
        Breakable("Ashtray", "Ashtray", props, new Vector3(db.center.x - 0.2f, 0f, db.max.z - 0.4f), top, 0f, 0.4f, 2.2f, "ceramic_break");
        Breakable("BottleWine01", "BottleWine01", props, new Vector3(db.max.x - 0.12f, 0f, db.max.z - 0.12f), top, 0f, 0.8f, 2.2f, "vase_shatter");
        var lamp = Spawn("TableLamp", "TableLamp", props, new Vector3(db.center.x + 0.05f, 0f, db.min.z + 0.15f), top, -60f);
        PointLight(lightsRoot, "305 Lamp (green)", Bounds(lamp).center + Vector3.up * 0.2f, new Color(0.55f, 0.85f, 0.35f), 1.4f, 4.6f, LightShadows.None);
        var chair = Spawn("Chair", "Chair", props, new Vector3(db.min.x - 0.1f, 0f, db.max.z + 0.45f), 0f, 135f); // beside the desk, not in the path
        ClearStatic(chair);
        NoBlock(chair);
        Spawn("CouchSingle", "CouchSingle", props, new Vector3(x0 + 0.6f, 0f, z1 - 0.6f), 0f, 135f);
        Breakable("CardboardBox02", "CardboardBox02", props, new Vector3(x0 + 0.5f, 0f, z0 + 0.5f), 0f, 20f, 3f, 4f, "cardboard_break");
        Breakable("CardboardBox01", "CardboardBox01", props, new Vector3(x0 + 1.1f, 0f, z0 + 0.45f), 0f, -10f, 3f, 4f, "cardboard_break");
        Sign(props, "Poster - no smoking", new Vector3(x0 + 1.4f, 1.55f, z0 + 0.005f), new Vector2(0.4f, 0.4f), Facing.PosZ, GMat("poster_nosmoking", "poster_nosmoking"));
        Sign(props, "Poster - movie", new Vector3(x0 + 1.6f, 1.5f, z1 - 0.005f), new Vector2(0.42f, 0.63f), Facing.NegZ, GMat("poster_movie", "poster_movie"));
        codeDecal = Sign(props, "0317 on the wall (hidden)", new Vector3(x0 + 2.2f, 1.2f, z0 + 0.006f), new Vector2(1.0f, 0.5f), Facing.PosZ, GMat("decal_code", "decal_code", cutout: true));
        codeDecal.SetActive(false);

        // the labelled tape
        var special = Spawn("VHSTape", "VHSTape", props, new Vector3(db.min.x + 0.2f, 0f, db.center.z + 0.1f), top + 0.003f, 15f, null, false);
        foreach (var c in special.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        var sb = Bounds(special);
        special.AddComponent<BoxCollider>().size = special.transform.InverseTransformVector(sb.size + Vector3.one * 0.02f).Abs();
        special.GetComponent<BoxCollider>().center = special.transform.InverseTransformPoint(sb.center);
        Sign(special.transform, "Label", new Vector3(sb.center.x, sb.max.y + 0.002f, sb.center.z), new Vector2(0.16f, 0.04f), Facing.PosY, GMat("vhs_label", "vhs_label"));
        tapePickup = special.AddComponent<Pickup>();
        tapePickup.itemId = "tape_305";
        tapePickup.prompt = "Take tape '305 - 3:17'";
        tapePickup.narrationId = "N1_TAPE";
        var glow = PointLight(special.transform, "Glow", sb.center + Vector3.up * 0.15f, new Color(0.8f, 0.95f, 1f), 0.5f, 0.8f, LightShadows.None);
        glow.gameObject.AddComponent<PulseLight>();

        // the TV: green static, the code, then the face
        var screen = ScreenOf(tv);
        var tvGlow = PointLight(lightsRoot, "TV Glow", Bounds(tv).center + Vector3.left * 0.6f, new Color(0.5f, 1f, 0.45f), 0.8f, 2.8f, LightShadows.None);
        var stat = tv.AddComponent<TVStatic>();
        stat.screen = screen;
        stat.glow = tvGlow;
        var digitsGo = new GameObject("Code Digits");
        digitsGo.transform.SetParent(tv.transform, true);
        var scb = screen != null ? screen.bounds : Bounds(tv);
        digitsGo.transform.position = new Vector3(scb.min.x - 0.012f, scb.center.y, scb.center.z);
        digitsGo.transform.rotation = Quaternion.LookRotation(Vector3.right);
        var digits = Text(digitsGo, "", 0.018f, new Color(0.85f, 1f, 0.75f));
        tvTape = tv.AddComponent<TVCodeTape>();
        tvTape.tv = stat;
        tvTape.digits = digits;

        var use = vcr.AddComponent<UseTarget>();
        use.requiredItems = new[] { "tape_305" };
        use.prompt = "Play tape";
        use.useSound = "pickup_tape";
        use.lockedHint = "An old VCR. One of the tapes on the desk is labelled.";

        Zone("Trigger - room 305", root.Find("Scares"), new Vector3(x0 + 0.6f, 1.5f, R.z + 14.5f), new Vector3(1f, 3f, 1.6f), "N1_06");
        return use;
    }

    static Keypad BuildKeypad(Transform parent, Vector3 pos, Quaternion rot, string code)
    {
        // rot: the keypad's forward points INTO the wall (the way you look at it)
        var root = new GameObject("Stairwell Keypad");
        root.transform.SetParent(parent, true);
        root.transform.SetPositionAndRotation(pos, rot);
        var body = GMat("keypad_body", null, new Color(0.2f, 0.2f, 0.22f), smooth: 0.4f);
        var keyMat = Mat("plastic_beige");
        var screenMat = GMat("keypad_screen", null, new Color(0.02f, 0.05f, 0.02f));
        LocalBox(root.transform, "Body", new Vector3(0f, 0f, 0f), new Vector3(0.14f, 0.22f, 0.025f), body, true);
        LocalBox(root.transform, "Screen", new Vector3(0f, 0.075f, -0.014f), new Vector3(0.11f, 0.036f, 0.006f), screenMat, false);
        var disp = new GameObject("Display");
        disp.transform.SetParent(root.transform, false);
        disp.transform.localPosition = new Vector3(0f, 0.075f, -0.0185f);
        var keypad = root.AddComponent<Keypad>();
        keypad.code = code;
        keypad.display = Text(disp, "----", 0.0055f, new Color(0.3f, 1f, 0.4f));
        var rows = new[] { "123", "456", "789", "C0E" };
        for (var r = 0; r < 4; r++)
            for (var c = 0; c < 3; c++)
            {
                var key = rows[r][c];
                var b = LocalBox(root.transform, "Key " + key, new Vector3((c - 1) * 0.038f, 0.03f - r * 0.033f, -0.018f), new Vector3(0.03f, 0.026f, 0.012f), keyMat, true);
                ClearStatic(b);
                var kb = b.AddComponent<KeypadButton>();
                kb.keypad = keypad;
                kb.key = key;
                kb.prompt = key == 'C' ? "Clear" : key == 'E' ? "Enter" : "Press " + key;
                var label = new GameObject("Label");
                label.transform.SetParent(b.transform, false);
                label.transform.localPosition = new Vector3(0f, 0f, -0.0065f);
                Text(label, key == 'E' ? "OK" : key.ToString(), 0.0035f, new Color(0.1f, 0.1f, 0.1f));
            }
        ClearStatic(root);
        return keypad;
    }

    // ================================================================== STAGE 2: SERVICE TUNNELS (picture 2)
    // Corridor A (spawn -> the pale man) -> junction -> storage room (W) / boiler room (E) -> corridor B -> tunnel door.
    static GameObject Stage2(GameFlow flow, List<string> log, out Transform spawn, out NavMeshSurface nav, out Light[] powerLights)
    {
        var root = new GameObject("Stage 2 - Basement").transform;
        root.position = k_Stage2;
        var structure = Child(root, "Structure");
        var props = Child(root, "Props");
        var lightsRoot = Child(root, "Lights");
        var scares = Child(root, "Scares");
        var R = root.position;
        const float CH = 2.8f;
        var concrete = GMat("concrete_wall", "concrete_wall");
        var concreteFloor = GMat("concrete_floor", "concrete_floor");
        var rust = GMat("barrel_rust", "barrel_rust", smooth: 0.2f);
        var pallet = GMat("pallet_wood", "pallet_wood");
        var none = new Opening[0];

        // ---- floors + ceilings
        void Area(string name, float x0, float x1, float z0, float z1)
        {
            Slab(structure, name + " Floor", R, x0, x1, z0, z1, -0.05f, concreteFloor, 2f);
            Slab(structure, name + " Ceiling", R, x0, x1, z0, z1, CH + 0.05f, concrete, 2.5f);
        }
        Area("Corridor A", -1.1f, 1.1f, 0f, 12f);
        Area("Junction", -6f, 6f, 12f, 14.5f);
        Area("Storage", -6f, -2f, 14.5f, 20f);
        Area("Boiler Room", 2f, 6f, 14.5f, 20f);
        Area("Corridor B", -1.1f, 1.1f, 14.5f, 24f);

        // ---- walls (all concrete, axis aligned)
        void WZ(string n, float x, float z0, float z1, params Opening[] o) => Wall(structure, n, true, R.x + x, R.z + z0, R.z + z1, WT, Offset(o, R.z), concrete, null, rust, CH);
        void WX(string n, float z, float x0, float x1, params Opening[] o) => Wall(structure, n, false, R.z + z, R.x + x0, R.x + x1, WT, Offset(o, R.x), concrete, null, rust, CH);
        WZ("A West", -1.1f - WT / 2f, 0f, 12f);
        WZ("A East", 1.1f + WT / 2f, 0f, 12f);
        WX("A South", -WT / 2f, -1.1f - WT, 1.1f + WT, new Opening(0f, 1.0f, 0f, 2.12f, true));
        WX("Junction South W", 12f + WT / 2f, -6f - WT, -1.1f - WT);
        WX("Junction South E", 12f + WT / 2f, 1.1f + WT, 6f + WT);
        WX("Junction North", 14.5f - WT / 2f, -6f - WT, 6f + WT, new Opening(-4f, 1.4f, 0f, 2.2f, true), new Opening(0f, 2.2f, 0f, CH, false), new Opening(4f, 1.4f, 0f, 2.2f, true));
        WZ("Junction West + Storage West", -6f - WT / 2f, 12f, 20f);
        WZ("Junction East + Boiler East", 6f + WT / 2f, 12f, 20f);
        WX("Storage North", 20f + WT / 2f, -6f - WT, -2f + WT);
        WZ("Storage East", -2f + WT / 2f, 14.5f, 20f);
        WX("Boiler North", 20f + WT / 2f, 2f - WT, 6f + WT);
        WZ("Boiler West", 2f - WT / 2f, 14.5f, 20f);
        WZ("B West", -1.1f - WT / 2f, 14.5f, 24f);
        WZ("B East", 1.1f + WT / 2f, 14.5f, 24f);
        WX("B North", 24f + WT / 2f, -1.1f - WT, 1.1f + WT, new Opening(0f, 1.0f, 0f, 2.12f, true));

        // doors: the stairs you came down (behind you) and the tunnel door (needs power)
        var stairsDoor = DoorInWall(structure, "Door04", "Door.001", R + new Vector3(0f, 0f, -WT / 2f));
        var stairsUse = stairsDoor.AddComponent<UseTarget>();
        stairsUse.requiredItems = new[] { "never" };
        stairsUse.onlyOnce = false;
        stairsUse.prompt = "Stairs";
        stairsUse.lockedHint = "It's jammed. Only one way now.";
        var exit = DoorInWall(structure, "Door04", "Door.001", R + new Vector3(0f, 0f, 24f + WT / 2f));
        var exitUse = exit.AddComponent<UseTarget>();
        exitUse.requiredItems = new[] { "power" };
        exitUse.prompt = "Open tunnel door";
        exitUse.lockedHint = "No power. The fuse box is dead.";
        UnityEventTools.AddIntPersistentListener(exitUse.onUse, flow.GoToStage, 3);
        spawn = Marker("Stage 2 Spawn", root, R + new Vector3(0f, 0f, 0.9f), 0f);

        // ---- corridor A (picture 2): pipes, chains, pallets, barrels, and him at the end
        foreach (var z in new[] { 1.5f, 4.5f, 7.5f, 10.5f, 16f, 19f, 22f })
            for (var k = 0; k < 2; k++)
                Pipe(props, rust, new Vector3(R.x + 1.1f - 0.16f - k * 0.18f, CH - 0.16f, R.z + z), 3f, 0.11f - k * 0.03f);
        foreach (var z in new[] { 2.6f, 7.0f, 10.2f })
            Spawn("ChainlinkHook", "ChainlinkHook", props, R + new Vector3(Random.Range(-0.3f, 0.3f), 0f, z), CH - 0.5f, Random.Range(0f, 180f));
        Pallet(props, pallet, R + new Vector3(-1.1f + 0.12f, 0f, 3.4f), true);
        Pallet(props, pallet, R + new Vector3(-1.1f + 0.2f, 0f, 4.0f), true);
        Pallet(props, pallet, R + new Vector3(1.1f - 0.12f, 0f, 8.2f), false);
        Barrel(props, rust, R + new Vector3(-0.62f, 0f, 6.2f));
        Barrel(props, rust, R + new Vector3(0.64f, 0f, 9.6f));
        Barrel(props, rust, R + new Vector3(0.6f, 0f, 1.6f));
        var bulb = PointLight(lightsRoot, "Corridor Bulb", R + new Vector3(0f, CH - 0.35f, 2.4f), new Color(0.75f, 0.8f, 0.85f), 1.1f, 5f, LightShadows.None);
        var bf = bulb.gameObject.AddComponent<FlickerLight>(); bf.buzz = 0.15f; bf.blackoutsPerSecond = 0.25f;
        Spawn("Bulb", "Bulb", props, R + new Vector3(0f, 0f, 2.4f), CH - 0.25f, 0f, null, false);
        var far = PointLight(lightsRoot, "Corridor Far Light", R + new Vector3(0f, CH - 0.4f, 10.4f), new Color(0.65f, 0.65f, 0.7f), 0.6f, 3.8f, LightShadows.None);
        far.gameObject.AddComponent<FlickerLight>().blackoutsPerSecond = 0.4f;
        Spawn("Bulb", "Bulb", props, R + new Vector3(0f, 0f, 10.4f), CH - 0.25f, 0f, null, false);

        var tall = Entity<WatcherEntity>("Watcher", root, R + new Vector3(0f, 0f, 11.2f), 180f, null);
        tall.startHidden = false;
        tall.faceScareOnApproach = true;
        tall.vanishDistance = 4f;
        Zone("Trigger - the man at the end", scares, R + new Vector3(0f, 1.5f, 3.5f), new Vector3(2.2f, 3f, 0.8f), "N2_TALL");

        // ---- junction: red emergency light, boxes
        var red = PointLight(lightsRoot, "Emergency Light (red)", R + new Vector3(0f, CH - 0.3f, 13.25f), new Color(0.9f, 0.15f, 0.08f), 1.0f, 7f, LightShadows.None);
        var rf = red.gameObject.AddComponent<FlickerLight>(); rf.buzz = 0.3f; rf.blackoutsPerSecond = 0.5f;
        var redBurst = red.gameObject.AddComponent<LightBurst>();
        redBurst.deadSeconds = 12f;
        Breakable("CardboardBox01", "CardboardBox01", props, R + new Vector3(-5.3f, 0f, 12.6f), 0f, 15f, 3f, 4f, "cardboard_break");
        Breakable("CardboardBox02", "CardboardBox02", props, R + new Vector3(-5.4f, 0f, 13.7f), 0f, -10f, 3f, 4f, "cardboard_break");
        Spawn("TrashBag01", "TrashBag01", props, R + new Vector3(5.3f, 0f, 12.7f), 0f, 0f);
        Barrel(props, rust, R + new Vector3(5.4f, 0f, 13.8f));

        // ---- storage room (west)
        var table = Spawn("Table02", "Table02", props, R + new Vector3(-4.2f, 0f, 19.1f), 0f, 0f);
        var tb = Bounds(table);
        Spawn("Chainsaw", "Chainsaw", props, new Vector3(tb.center.x - 0.3f, 0f, tb.center.z), tb.max.y, 30f, null, false);
        Spawn("Hacksaw", "Hacksaw", props, new Vector3(tb.center.x + 0.3f, 0f, tb.center.z - 0.1f), tb.max.y, 80f, null, false);
        Breakable("BottleWine01", "BottleWine01", props, new Vector3(tb.min.x + 0.15f, 0f, tb.center.z + 0.1f), tb.max.y, 0f, 0.8f, 2.2f, "vase_shatter");
        Breakable("Glass03", "Glass03", props, new Vector3(tb.max.x - 0.15f, 0f, tb.center.z + 0.1f), tb.max.y, 0f, 0.2f, 1.8f, "vase_shatter");
        var box1 = Spawn("CardboardBox02", "CardboardBox02", props, R + new Vector3(-2.7f, 0f, 19.2f), 0f, 10f);
        Breakable("CardboardBox01", "CardboardBox01", props, R + new Vector3(-2.65f, 0f, 18.2f), 0f, -20f, 3f, 4f, "cardboard_break");
        Spawn("BoxSpring", "BoxSpring", props, R + new Vector3(-5.6f, 0f, 18.6f), 0f, 90f);
        HideCloset(root, props, R + new Vector3(-6f + 0.36f, 0f, 15.6f), 90f);
        PointLight(lightsRoot, "Storage Light", R + new Vector3(-4f, CH - 0.4f, 17.2f), new Color(0.7f, 0.72f, 0.75f), 0.5f, 4.5f, LightShadows.None)
            .gameObject.AddComponent<FlickerLight>().blackoutsPerSecond = 0.2f;

        // ---- boiler room (east): the old boiler glowing orange
        var boiler = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        boiler.name = "Boiler";
        boiler.transform.SetParent(props, true);
        boiler.transform.position = R + new Vector3(4.6f, 1.1f, 18.7f);
        boiler.transform.localScale = new Vector3(1.4f, 1.1f, 1.4f);
        boiler.GetComponent<Renderer>().sharedMaterial = rust;
        GameObjectUtility.SetStaticEditorFlags(boiler, StaticEditorFlags.BatchingStatic);
        Pipe(props, rust, R + new Vector3(4.6f, 2.45f, 18.7f), 0.7f, 0.18f, true);
        Box(props, "Boiler Hatch (glowing)", R + new Vector3(4.6f, 0.55f, 18.0f), new Vector3(0.35f, 0.25f, 0.03f), GMat("boiler_glow", null, new Color(1f, 0.45f, 0.1f), emission: new Color(1f, 0.4f, 0.08f) * 2f), 1f, false);
        var glow = PointLight(lightsRoot, "Boiler Glow", R + new Vector3(4.4f, 0.7f, 17.6f), new Color(1f, 0.5f, 0.2f), 1.3f, 5f, LightShadows.None);
        var gf = glow.gameObject.AddComponent<FlickerLight>(); gf.buzz = 0.25f; gf.blackoutsPerSecond = 0f;
        var stand = Spawn("NightStandIsolated", "NightStandIsolated", props, R + new Vector3(2.45f, 0f, 19.4f), 0f, -90f);
        Barrel(props, rust, R + new Vector3(5.5f, 0f, 15.1f));
        Barrel(props, rust, R + new Vector3(4.8f, 0f, 15.0f));
        Spawn("ChainlinkHook", "ChainlinkHook", props, R + new Vector3(3.2f, 0f, 16.5f), CH - 0.5f, 40f);
        foreach (var p in new[] { new Vector3(2.6f, 0f, 15.2f), new Vector3(2.9f, 0f, 15.5f) })
            Throwable("BeerCan", "BeerCan", props, R + p, 0.002f, 0.15f);

        // ---- corridor B: lockers, the last battery, fuse box by the tunnel door
        HideCloset(root, props, R + new Vector3(-1.1f + 0.36f, 0f, 17.8f), 90f);
        var trash = Spawn("TrashBag01", "TrashBag01", props, R + new Vector3(0.72f, 0f, 21.2f), 0f, 0f);
        Breakable("BottleWine01.001", "BottleWine01.001", props, R + new Vector3(-0.8f, 0f, 22.6f), 0.002f, 0f, 0.8f, 2.2f, "vase_shatter");
        Throwable("BeerCan", "BeerCan", props, R + new Vector3(-0.7f, 0f, 21.0f), 0.002f, 0.15f);
        var fuse = Spawn("DoorKeypad", "DoorKeypad.001", props, R + new Vector3(1.1f - 0.12f, 0f, 23.2f), 1.15f, -90f);
        ClearStatic(fuse);
        var fuseUse = fuse.AddComponent<UseTarget>();
        fuseUse.requiredItems = new[] { "battery_1", "battery_2", "battery_3" };
        fuseUse.prompt = "Fix fuse box";
        fuseUse.lockedHint = "The fuse box needs three batteries.";
        UnityEventTools.AddPersistentListener(fuseUse.onUse, flow.PowerOn);

        // ---- batteries
        var bat1 = Battery("battery_1", props, new Vector3(Bounds(box1).center.x, Bounds(box1).max.y, Bounds(box1).center.z), "N2_02");
        var bat2 = Battery("battery_2", props, new Vector3(Bounds(stand).center.x, Bounds(stand).max.y, Bounds(stand).center.z), "N2_03");
        var bat3 = Battery("battery_3", props, Bounds(trash).center + new Vector3(-0.45f, -Bounds(trash).extents.y, 0f), null);
        var set = root.gameObject.AddComponent<ItemSetWatcher>();
        set.items = new[] { "battery_1", "battery_2", "battery_3" };
        set.narrationWhenComplete = "N2_BATT";

        // ---- lights that come on with the power
        var lamps = new List<Light>();
        foreach (var p in new[] { new Vector3(0f, 0f, 13.25f), new Vector3(0f, 0f, 19f), new Vector3(0f, 0f, 22.8f), new Vector3(4f, 0f, 16.4f) })
        {
            Spawn("CeliningLight01", "CeliningLight01", props, R + p, CH - 0.26f, 0f);
            lamps.Add(PointLight(lightsRoot, "Power Lamp", R + p + Vector3.up * (CH - 0.45f), new Color(1f, 0.85f, 0.6f), 1.5f, 6f, LightShadows.None));
        }
        powerLights = lamps.ToArray();

        // ---- no roaming monsters: a small scripted scene instead.
        //      Taking the first battery: the lights die, something skitters in the dark, the lights come back and the
        //      Crawler is crouched by the table staring at you... then it's gone.
        var bpos = Bounds(box1).center;
        var dieOut = Scare(scares, "Scene - lights die (battery 1)", ScareEvent.Kind.Blackout, bpos);
        dieOut.seconds = 2.6f; dieOut.radius = 9f; dieOut.delay = 0.6f;
        var skitter = Scare(scares, "Scene - skittering in the dark", ScareEvent.Kind.Sound, R + new Vector3(-4.4f, 0.3f, 18.4f));
        skitter.sound = "crawler_skitter"; skitter.delay = 1.2f; skitter.volume = 0.9f;
        var crouched = Scare(scares, "Scene - it's by the table", ScareEvent.Kind.Appear, R + new Vector3(-4.5f, 0f, 18.3f));
        crouched.monster = Monster.Crawler; crouched.delay = 3.3f; crouched.seconds = 1.6f; crouched.narrationId = "N2_04";
        var breath = Scare(scares, "Scene - breathing", ScareEvent.Kind.Sound, R + new Vector3(-4.5f, 0.4f, 18.3f));
        breath.sound = "crawler_breath"; breath.delay = 3.3f; breath.volume = 1f;
        UnityEventTools.AddPersistentListener(bat1.onPicked, dieOut.Play);
        UnityEventTools.AddPersistentListener(bat1.onPicked, skitter.Play);
        UnityEventTools.AddPersistentListener(bat1.onPicked, crouched.Play);
        UnityEventTools.AddPersistentListener(bat1.onPicked, breath.Play);
        // something crawling through the ceiling ducts above the junction (sound only)
        var duct = Zone("Trigger - junction", scares, R + new Vector3(0f, 1.5f, 13.2f), new Vector3(2.2f, 3f, 0.8f), null);
        var ductSound = Scare(scares, "Sound - crawling in the ducts", ScareEvent.Kind.Sound, R + new Vector3(1.5f, CH - 0.1f, 15f));
        ductSound.sound = "crawler_skitter"; ductSound.delay = 0.8f; ductSound.volume = 0.55f;
        UnityEventTools.AddPersistentListener(duct.onEnter, ductSound.Play);

        // ---- suspense, not scares: sounds first
        var arrive = Zone("Trigger - arrive", scares, R + new Vector3(0f, 1.5f, 0.9f), new Vector3(2.2f, 3f, 1.2f), null);
        var clang = Scare(scares, "Sound - something drops far away", ScareEvent.Kind.Sound, R + new Vector3(4f, 1f, 18f));
        clang.sound = "barrel_clang"; clang.delay = 6f; clang.volume = 0.7f;
        UnityEventTools.AddPersistentListener(arrive.onEnter, clang.Play);
        UnityEventTools.AddPersistentListener(bat3.onPicked, redBurst.Play);
        var scream = Scare(scares, "Sound - a scream beyond the tunnel door", ScareEvent.Kind.Sound, R + new Vector3(0f, 1.5f, 26f));
        scream.sound = "mother_scream"; scream.delay = 2.5f; scream.volume = 0.45f;
        UnityEventTools.AddPersistentListener(fuseUse.onUse, scream.Play);

        nav = NavVolume("NavMesh Stage 2", R + new Vector3(0f, 1f, 12f), new Vector3(13f, 3f, 26f));
        log.Add("Stage 2: service tunnels (corridor with the pale man, junction, storage room, boiler room, corridor to the tunnel door), 3 batteries, fuse box, 2 lockers, the Crawler scene (no roaming monsters).");
        return root.gameObject;
    }

    static void Barrel(Transform parent, Material mat, Vector3 floor)
    {
        var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        barrel.name = "Barrel";
        barrel.transform.SetParent(parent, true);
        barrel.transform.position = floor + Vector3.up * 0.45f;
        barrel.transform.localScale = new Vector3(0.58f, 0.45f, 0.58f);
        barrel.GetComponent<Renderer>().sharedMaterial = mat;
        GameObjectUtility.SetStaticEditorFlags(barrel, StaticEditorFlags.BatchingStatic);
    }

    static void Pipe(Transform parent, Material mat, Vector3 center, float length, float thickness, bool vertical = false)
    {
        var pipe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pipe.name = "Pipe";
        Object.DestroyImmediate(pipe.GetComponent<Collider>());
        pipe.transform.SetParent(parent, true);
        pipe.transform.position = center;
        pipe.transform.rotation = vertical ? Quaternion.identity : Quaternion.Euler(90f, 0f, 0f);
        pipe.transform.localScale = new Vector3(thickness, length / 2f, thickness);
        pipe.GetComponent<Renderer>().sharedMaterial = mat;
        GameObjectUtility.SetStaticEditorFlags(pipe, StaticEditorFlags.BatchingStatic);
    }

    /// <summary>A floor or ceiling slab cut into pieces of at most 5 m (so each piece only gets nearby lamps).</summary>
    static void Slab(Transform parent, string name, Vector3 R, float x0, float x1, float z0, float z1, float y, Material mat, float tile)
    {
        for (var x = x0; x < x1 - 0.001f; x += 5f)
            for (var z = z0; z < z1 - 0.001f; z += 5f)
            {
                var xe = Mathf.Min(x1, x + 5f);
                var ze = Mathf.Min(z1, z + 5f);
                Box(parent, name, R + new Vector3((x + xe) / 2f, y, (z + ze) / 2f), new Vector3(xe - x + 0.25f, 0.1f, ze - z + 0.25f), mat, tile);
            }
    }

    /// <summary>A pack door set into a doorway of one of the box walls (wall running along X), sized to the doorway.</summary>
    static GameObject DoorInWall(Transform parent, string folder, string file, Vector3 at, float width = 1.0f, float height = 2.1f)
    {
        var wrap = new GameObject(folder + " (door)");
        wrap.transform.SetParent(parent, true);
        wrap.transform.position = at;
        var door = Spawn(folder, file, wrap.transform, at, 0f, 0f, folder == "Door01" ? Mat("wood_dark") : null, false);
        var b = Bounds(door);
        if (b.size.x < b.size.z) // make it span the doorway along X
        {
            Object.DestroyImmediate(door);
            door = Spawn(folder, file, wrap.transform, at, 0f, 90f, folder == "Door01" ? Mat("wood_dark") : null, false);
            b = Bounds(door);
        }
        wrap.transform.localScale = new Vector3(width / Mathf.Max(0.1f, b.size.x), height / Mathf.Max(0.1f, b.size.y), Mathf.Clamp(0.06f / Mathf.Max(0.01f, b.size.z), 0.5f, 2f));
        b = Bounds(door);
        door.transform.position += new Vector3(at.x - b.center.x, -b.min.y, at.z - b.center.z);
        foreach (var c in door.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        b = Bounds(door);
        var col = wrap.AddComponent<BoxCollider>();
        col.center = wrap.transform.InverseTransformPoint(b.center);
        col.size = wrap.transform.InverseTransformVector(b.size).Abs();
        ClearStatic(wrap);
        return wrap;
    }

    static void HideCloset(Transform root, Transform props, Vector3 pos, float yaw)
    {
        var closet = Spawn("Closet01Isolated", "Closet01Isolated", props, pos, 0f, yaw);
        foreach (var shelf in closet.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("ClosetShelf")).ToList())
            Object.DestroyImmediate(shelf.gameObject);
        var hide = new GameObject("Hide Spot");
        hide.transform.SetParent(root, false);
        hide.transform.SetPositionAndRotation(Bounds(closet).center, Quaternion.Euler(0f, yaw, 0f));
        var box = hide.AddComponent<BoxCollider>();
        box.size = new Vector3(0.9f, 2f, 0.6f);
        hide.AddComponent<HideSpot>();
    }

    static void Pallet(Transform parent, Material mat, Vector3 bottom, bool leanOnWest)
    {
        var p = new GameObject("Pallet").transform;
        p.SetParent(parent, true);
        p.position = bottom;
        for (var i = 0; i < 5; i++)
            LocalBox(p, "Slat", new Vector3(0f, 0.05f + i * 0.24f, 0f), new Vector3(0.03f, 0.1f, 1.0f), mat, false);
        foreach (var z in new[] { -0.45f, 0f, 0.45f })
            LocalBox(p, "Runner", new Vector3(0.05f, 0.55f, z), new Vector3(0.07f, 1.1f, 0.09f), mat, false);
        p.rotation = Quaternion.Euler(0f, 0f, leanOnWest ? 12f : -12f);
        p.position = bottom + Vector3.up * 0.02f;
        var col = p.gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0.03f, 0.6f, 0f);
        col.size = new Vector3(0.15f, 1.2f, 1.0f);
    }

    static Pickup Battery(string id, Transform parent, Vector3 bottom, string narration)
    {
        var go = Spawn("Battery", "Battery", parent, bottom, bottom.y + 0.002f, Random.Range(0f, 360f), null, false);
        go.transform.localScale *= 1.5f;
        foreach (var mc in go.GetComponentsInChildren<MeshCollider>())
            Object.DestroyImmediate(mc);
        var col = go.AddComponent<BoxCollider>();
        col.size = Vector3.one * 0.12f;
        col.center = Vector3.up * 0.05f;
        var p = go.AddComponent<Pickup>();
        p.itemId = id;
        p.prompt = "Take battery";
        p.narrationId = narration;
        var l = new GameObject("Glow").AddComponent<Light>();
        l.transform.SetParent(go.transform, false);
        l.transform.localPosition = Vector3.up * 0.15f;
        l.type = LightType.Point;
        l.range = 0.9f;
        l.intensity = 0.7f;
        l.color = new Color(0.55f, 1f, 0.6f);
        l.gameObject.AddComponent<PulseLight>();
        return p;
    }

    // ================================================================== STAGE 3: THE NIGHT TRAIN (picture 3)
    // An old wooden sleeper carriage parked in the tunnel under the hotel: a narrow corridor (windows onto the black
    // tunnel on the left), seven compartments on the right. You come in at the back; the way out is the front door.
    static GameObject Stage3(GameFlow flow, List<string> log, out Transform spawn, out NavMeshSurface nav)
    {
        var root = new GameObject("Stage 3 - Train").transform;
        root.position = k_Stage3;
        var structure = Child(root, "Structure");
        var props = Child(root, "Props");
        var lightsRoot = Child(root, "Lights");
        var scares = Child(root, "Scares");
        var R = root.position;
        const float L = 19.8f;     // carriage length
        const float CW = 0.65f;    // half the corridor width
        const float XO = 2.85f;    // outer wall of the compartments
        const float TH = 2.35f;    // ceiling
        const float CL = 2.8f;     // compartment length
        var planks = GMat("planks_dark", "planks_dark");
        var floorWood = Mat("wood_floor");
        var trim = Mat("wood_dark");
        var seat = Mat("fabric_red");
        var brass = Mat("brass");
        var tunnel = GMat("tunnel_wall", "concrete_wall", new Color(0.25f, 0.25f, 0.27f));
        var lampGlow = GMat("train_lamp", null, new Color(1f, 0.8f, 0.5f), emission: new Color(1f, 0.7f, 0.35f) * 1.4f);

        Slab(structure, "Carriage Floor", R, -CW - WT, XO + WT, 0f, L, -0.05f, floorWood, 1f);
        Slab(structure, "Carriage Ceiling", R, -CW - WT, XO + WT, 0f, L, TH + 0.05f, planks, 1.2f);

        // corridor wall with windows onto the tunnel (left)
        var windows = new List<Opening>();
        for (var z = 1.6f; z < L - 0.5f; z += CL)
            windows.Add(new Opening(z, 0.8f, 1.0f, 1.65f, false));
        Wall(structure, "Corridor Windows Wall", true, R.x - CW - WT / 2f, R.z, R.z + L, WT, Offset(windows.ToArray(), R.z), planks, null, trim, TH);
        Box(structure, "Tunnel wall (outside the windows)", R + new Vector3(-CW - 1.1f, 1.2f, L / 2f), new Vector3(0.2f, 3f, L + 4f), tunnel, 2f, false);
        // compartment wall with doorways (right), partitions, outer wall with small windows
        var doorways = new List<Opening>();
        var outerWindows = new List<Opening>();
        for (var i = 0; i < 7; i++)
        {
            var zc = 0.2f + CL * i + CL / 2f;
            doorways.Add(new Opening(zc - 0.5f, 0.75f, 0f, 2.0f, true));
            outerWindows.Add(new Opening(zc + 0.2f, 0.8f, 1.0f, 1.6f, false));
        }
        Wall(structure, "Compartment Wall", true, R.x + CW + WT / 2f, R.z, R.z + L, WT, Offset(doorways.ToArray(), R.z), planks, null, trim, TH);
        Wall(structure, "Outer Wall", true, R.x + XO + WT / 2f, R.z, R.z + L, WT, Offset(outerWindows.ToArray(), R.z), planks, null, trim, TH);
        for (var i = 0; i <= 7; i++)
            Wall(structure, "Partition", false, R.z + 0.2f + CL * i, R.x + CW + WT, R.x + XO, 0.08f, new Opening[0], planks, null, null, TH);
        Box(structure, "Tunnel wall (outside, right)", R + new Vector3(XO + 1.2f, 1.2f, L / 2f), new Vector3(0.2f, 3f, L + 4f), tunnel, 2f, false);
        // end walls: you came in at the back; the front door is the way out
        Wall(structure, "Back Wall", false, R.z - WT / 2f, R.x - CW - WT, R.x + XO + WT, WT, new[] { new Opening(R.x, 0.8f, 0f, 2.0f, true) }, planks, null, trim, TH);
        Wall(structure, "Front Wall", false, R.z + L + WT / 2f, R.x - CW - WT, R.x + XO + WT, WT, new[] { new Opening(R.x, 0.8f, 0f, 2.0f, true) }, planks, null, trim, TH);
        var back = DoorInWall(structure, "Door01", "Door", R + new Vector3(0f, 0f, -WT / 2f), 0.8f, 2.0f);
        var backUse = back.AddComponent<UseTarget>();
        backUse.requiredItems = new[] { "never" };
        backUse.onlyOnce = false;
        backUse.prompt = "Back door";
        backUse.lockedHint = "It locked behind you.";
        var front = DoorInWall(structure, "Door01", "Door", R + new Vector3(0f, 0f, L + WT / 2f), 0.8f, 2.0f);
        var frontUse = front.AddComponent<UseTarget>();
        frontUse.requiredItems = new[] { "tape_watched" };
        frontUse.prompt = "ESCAPE";
        frontUse.lockedHint = "Not yet. Something on this train wants to be seen first.";
        UnityEventTools.AddPersistentListener(frontUse.onUse, flow.Win);
        spawn = Marker("Stage 3 Spawn", root, R + new Vector3(0f, 0f, 0.8f), 0f);

        // corridor: brass hand rail under the windows, small wall lamps (warm), two of them failing
        Box(props, "Hand Rail", R + new Vector3(-CW + 0.04f, 0.95f, L / 2f), new Vector3(0.03f, 0.03f, L - 0.4f), brass, 1f, false);
        var corridorLamps = new List<Light>();
        foreach (var z in new[] { 2.9f, 8.5f, 14.1f, 18.8f })
        {
            Box(props, "Wall Lamp", R + new Vector3(-CW + 0.04f, 1.95f, z), new Vector3(0.06f, 0.16f, 0.12f), lampGlow, 1f, false);
            corridorLamps.Add(PointLight(lightsRoot, "Corridor Lamp", R + new Vector3(-CW + 0.25f, 1.9f, z), new Color(1f, 0.72f, 0.42f), 0.9f, 3.8f, LightShadows.None));
        }
        var f1 = corridorLamps[1].gameObject.AddComponent<FlickerLight>(); f1.blackoutsPerSecond = 0.3f;
        var f2 = corridorLamps[2].gameObject.AddComponent<FlickerLight>(); f2.blackoutsPerSecond = 0.6f; f2.buzz = 0.2f;

        // compartments: benches facing each other, luggage racks; some special
        for (var i = 0; i < 7; i++)
        {
            var z0 = R.z + 0.2f + CL * i;
            var cx = R.x + (CW + XO) / 2f + 0.06f;
            var inner = XO - CW - WT;
            if (i != 4) // compartment 5 is a sleeper
            {
                foreach (var (zz, back2) in new[] { (z0 + 0.35f, z0 + 0.12f), (z0 + CL - 0.35f, z0 + CL - 0.12f) })
                {
                    Box(props, "Bench", new Vector3(cx + 0.25f, 0.22f, zz), new Vector3(inner - 0.6f, 0.44f, 0.5f), seat, 0.6f);
                    Box(props, "Bench Back", new Vector3(cx + 0.25f, 0.75f, back2), new Vector3(inner - 0.6f, 0.65f, 0.12f), seat, 0.6f, false);
                }
            }
            Box(props, "Luggage Rack", new Vector3(R.x + XO - 0.2f, 1.85f, z0 + CL / 2f), new Vector3(0.35f, 0.03f, CL - 0.3f), brass, 1f, false);
        }

        // compartment 1: dining table with breakables
        var c1 = R.z + 0.2f + CL / 2f;
        var tableTop = Box(props, "Fold-down Table", R + new Vector3(XO - 0.3f, 0.72f, c1 - R.z), new Vector3(0.5f, 0.04f, 0.7f), trim, 1f);
        var ty = 0.74f;
        Breakable("Plate", "Plate", props, R + new Vector3(XO - 0.3f, 0f, c1 - R.z - 0.15f), ty, 0f, 0.4f, 2.2f, "ceramic_break");
        Breakable("Teacup", "Teacup", props, R + new Vector3(XO - 0.35f, 0f, c1 - R.z + 0.15f), ty, 40f, 0.2f, 2f, "ceramic_break");
        Breakable("BottleWine01", "BottleWine01", props, R + new Vector3(XO - 0.2f, 0f, c1 - R.z + 0.22f), ty, 0f, 0.8f, 2.2f, "vase_shatter");
        Breakable("Glass01", "Glass01", props, R + new Vector3(XO - 0.18f, 0f, c1 - R.z - 0.05f), ty, 0f, 0.2f, 1.8f, "vase_shatter");

        // compartment 3: the family compartment, a little TV + VCR facing the doorway, a teddy on the bench
        var c3 = R.z + 0.2f + CL * 2f + CL / 2f;
        var tvStand = Box(props, "TV Stand", R + new Vector3(XO - 0.3f, 0.35f, c3 - R.z), new Vector3(0.5f, 0.7f, 0.6f), trim, 1f);
        var tv = Spawn("TV", "TV", props, R + new Vector3(XO - 0.3f, 0f, c3 - R.z + 0.05f), 0.7f, 0f);
        FaceTowards(tv, Vector3.left);
        var vcr = Spawn("VCR", "VCR", props, R + new Vector3(XO - 0.32f, 0f, c3 - R.z - 0.02f), 0.7f + 0.002f, 0f);
        FaceTowards(vcr, Vector3.left);
        var tvb = Bounds(tv);
        vcr.transform.position += new Vector3(0f, 0f, (tvb.min.z - 0.02f) - Bounds(vcr).max.z); // VCR beside the TV, not inside it
        ClearStatic(vcr);
        var screen = ScreenOf(tv);
        var tvScare = tv.AddComponent<TVScare>();
        tvScare.screen = screen;
        var vcrUse = vcr.AddComponent<UseTarget>();
        vcrUse.requiredItems = new[] { "vhs" };
        vcrUse.prompt = "Insert tape";
        vcrUse.useSound = "pickup_tape";
        vcrUse.lockedHint = "An old VCR. It needs a tape.";
        vcrUse.narrationId = "N3_02";
        UnityEventTools.AddPersistentListener(vcrUse.onUse, tvScare.Play);
        // the Teddybear model faces its -Z: turn it round so it sits facing out of the bench, not into the backrest
        Spawn("Teddybear", "Teddybear", props, R + new Vector3(1.9f, 0f, c3 - R.z - CL / 2f + 0.35f), 0.44f, 180f, null, false);
        Sign(props, "Kid's drawing", R + new Vector3(CW + WT + 0.005f, 1.45f, c3 - R.z + 0.7f), new Vector2(0.35f, 0.3f), Facing.PosX, Mat("kids_drawing"));
        PointLight(lightsRoot, "Family Compartment Lamp", R + new Vector3(XO - 0.6f, 1.9f, c3 - R.z), new Color(1f, 0.75f, 0.45f), 0.7f, 3f, LightShadows.None);

        // compartment 5: sleeper bunk, the last tape lying on the bed (glowing)
        var c5 = R.z + 0.2f + CL * 4f + CL / 2f;
        var bed = Box(props, "Bunk", R + new Vector3(XO - 0.62f, 0.45f, c5 - R.z + 0.35f), new Vector3(1.2f, 0.12f, 1.9f), Mat("mattress") ?? seat, 0.8f);
        Box(props, "Bunk Base", R + new Vector3(XO - 0.62f, 0.2f, c5 - R.z + 0.35f), new Vector3(1.2f, 0.4f, 1.9f), trim, 1f);
        Box(props, "Upper Bunk", R + new Vector3(XO - 0.62f, 1.45f, c5 - R.z + 0.35f), new Vector3(1.2f, 0.1f, 1.9f), trim, 1f);
        Spawn("Pillow", "Pillow", props, R + new Vector3(XO - 0.62f, 0f, c5 - R.z + 1.05f), 0.51f, 90f, null, false);
        var bb = Bounds(bed);
        var tape = Spawn("VHSTape", "VHSTape", props, new Vector3(bb.center.x - 0.1f, 0f, bb.center.z - 0.2f), bb.max.y + 0.003f, 25f, null, false);
        foreach (var c in tape.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        var tapeB = Bounds(tape);
        var tcol = tape.AddComponent<BoxCollider>();
        tcol.center = tape.transform.InverseTransformPoint(tapeB.center);
        tcol.size = tape.transform.InverseTransformVector(tapeB.size + Vector3.one * 0.03f).Abs();
        var tapePick = tape.AddComponent<Pickup>();
        tapePick.itemId = "vhs";
        tapePick.prompt = "Take the tape";
        var tg = PointLight(tape.transform, "Glow", tapeB.center + Vector3.up * 0.15f, new Color(0.7f, 0.8f, 1f), 0.5f, 0.8f, LightShadows.None);
        tg.gameObject.AddComponent<PulseLight>();

        // compartment 7: luggage (breakable boxes)
        var c7 = R.z + 0.2f + CL * 6f + CL / 2f;
        Breakable("CardboardBox01", "CardboardBox01", props, R + new Vector3(XO - 0.45f, 0f, c7 - R.z - 0.1f), 0.44f, 10f, 3f, 4f, "cardboard_break");
        Breakable("CardboardBox02", "CardboardBox02", props, R + new Vector3(XO - 0.5f, 0f, c7 - R.z + 0.5f), 0.44f, -15f, 3f, 4f, "cardboard_break");

        // ---- suspense first: the train creaks, something knocks on the windows from the tunnel,
        //      the far lamp dies and she steps out of a compartment, then the tape.
        var z1 = Zone("Trigger - knock on the window", scares, R + new Vector3(0f, 1.5f, 3.6f), new Vector3(1.3f, 3f, 0.7f), null);
        var knock = Scare(scares, "Sound - knocking on the glass from the tunnel", ScareEvent.Kind.Sound, R + new Vector3(-CW - 0.3f, 1.4f, 6.2f));
        knock.sound = "knock_loud"; knock.delay = 0.4f; knock.volume = 0.8f;
        UnityEventTools.AddPersistentListener(z1.onEnter, knock.Play);
        var z2 = Zone("Trigger - she steps out", scares, R + new Vector3(0f, 1.5f, 9.2f), new Vector3(1.3f, 3f, 0.7f), null);
        var dark = Scare(scares, "Scare - far lamp dies", ScareEvent.Kind.Blackout, corridorLamps[2].transform.position);
        dark.lights = new[] { f2 }; dark.seconds = 3f;
        var cross = Scare(scares, "Scare - she steps across the corridor", ScareEvent.Kind.FigureCross, R);
        cross.monster = Monster.Mother;
        cross.from = Marker("Mother Cross From", scares, R + new Vector3(CW + 0.7f, 0f, 15.2f), -90f);
        cross.to = Marker("Mother Cross To", scares, R + new Vector3(-CW + 0.15f, 0f, 15.6f), -90f);
        cross.speed = 0.9f; cross.delay = 0.4f;
        UnityEventTools.AddPersistentListener(z2.onEnter, dark.Play);
        UnityEventTools.AddPersistentListener(z2.onEnter, cross.Play);
        var slamFar = Scare(scares, "Sound - the front door bangs", ScareEvent.Kind.Sound, R + new Vector3(0f, 1.2f, L));
        slamFar.sound = "door_slam"; slamFar.volume = 0.8f;
        UnityEventTools.AddPersistentListener(tapePick.onPicked, slamFar.Play);

        // ---- no roaming Mother: after the tape the corridor goes dark, and when the lights return she is standing
        //      at the front door. Walk up to the door and she whispers right behind you.
        var allDark = Scare(scares, "Scene - corridor goes dark (after the tape)", ScareEvent.Kind.Blackout, R + new Vector3(0f, 2f, L / 2f));
        var lampFlickers = new List<FlickerLight>();
        foreach (var l in corridorLamps)
        {
            var fl = l.GetComponent<FlickerLight>();
            if (fl == null) { fl = l.gameObject.AddComponent<FlickerLight>(); fl.buzz = 0f; fl.blackoutsPerSecond = 0f; }
            lampFlickers.Add(fl);
        }
        allDark.lights = lampFlickers.ToArray();
        allDark.radius = L; allDark.seconds = 2.2f; allDark.delay = 0.5f; allDark.narrationId = "N3_04";
        var atDoor = Scare(scares, "Scene - she is at the front door", ScareEvent.Kind.Appear, R + new Vector3(0f, 0f, L - 0.9f));
        atDoor.monster = Monster.Mother; atDoor.delay = 2.8f; atDoor.seconds = 3f; atDoor.narrationId = "N3_03";
        var music = Scare(scares, "Scene - music box at the door", ScareEvent.Kind.Sound, R + new Vector3(0f, 1.2f, L - 0.9f));
        music.sound = "musicbox"; music.delay = 2.8f; music.volume = 0.7f;
        UnityEventTools.AddPersistentListener(tvScare.onFinished, allDark.Play);
        UnityEventTools.AddPersistentListener(tvScare.onFinished, atDoor.Play);
        UnityEventTools.AddPersistentListener(tvScare.onFinished, music.Play);
        var nearDoor = Zone("Trigger - near the front door", scares, R + new Vector3(0f, 1.5f, L - 2.6f), new Vector3(1.3f, 3f, 0.7f), null);
        nearDoor.requiredItem = "tape_watched";
        var whisper = Scare(scares, "Scene - whisper behind you", ScareEvent.Kind.Sound, R + new Vector3(0f, 1.6f, L - 3.6f));
        whisper.sound = "whisper"; whisper.volume = 1f;
        var behind = Scare(scares, "Scene - she is behind you", ScareEvent.Kind.LookBehind, R);
        behind.monster = Monster.Mother; behind.delay = 0.3f; behind.seconds = 5f;
        UnityEventTools.AddPersistentListener(nearDoor.onEnter, whisper.Play);
        UnityEventTools.AddPersistentListener(nearDoor.onEnter, behind.Play);

        nav = NavVolume("NavMesh Stage 3", R + new Vector3(1.1f, 1f, L / 2f), new Vector3(4.2f, 3f, L + 1f));
        log.Add("Stage 3: the night train (corridor, 7 compartments, family compartment with the TV, sleeper with the tape on the bunk), the Mother scenes (no roaming), front door exit.");
        return root.gameObject;
    }

    /// <summary>The TV's screen renderer (the CRT material), or its first renderer.</summary>
    static Renderer ScreenOf(GameObject tv) =>
        tv.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("crt"))
        ?? tv.GetComponentInChildren<Renderer>();

    /// <summary>
    /// Turns a TV / VCR so its front (the side its screen is on) faces 'dir'. Works out which way the model faces
    /// by where its screen sits relative to the whole model, so imported orientation doesn't matter.
    /// </summary>
    static void FaceTowards(GameObject go, Vector3 dir)
    {
        var center = Bounds(go).center;
        var screen = ScreenOf(go);
        Vector3 front;
        if (screen != null && Vector3.Distance(screen.bounds.center, center) > 0.01f)
        {
            front = screen.bounds.center - center;
            front.y = 0f;
        }
        else
            front = go.transform.forward;
        // snap to the nearest horizontal axis
        front = Mathf.Abs(front.x) > Mathf.Abs(front.z) ? new Vector3(Mathf.Sign(front.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(front.z));
        var angle = Vector3.SignedAngle(front, dir, Vector3.up);
        go.transform.RotateAround(center, Vector3.up, angle);
    }

    // ================================================================== scare templates
    static void ScareTemplates(List<string> log)
    {
        var dir = new GameObject("Scare Director").AddComponent<ScareDirector>();
        var names = new[] { "Watcher", "Crawler", "Mother" };
        dir.templates = new GameObject[3];
        for (var i = 0; i < 3; i++)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Entities/" + names[i] + "/" + names[i] + ".fbx");
            if (model == null) { log.Add("Scare template missing: " + names[i]); continue; }
            var root = new GameObject("Template " + names[i]);
            root.transform.SetParent(dir.transform, false);
            root.transform.position = new Vector3(0f, -50f, 0f);
            var m = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            m.name = "Model";
            m.transform.localPosition = Vector3.zero;
            m.transform.localRotation = Quaternion.identity;
            log.Add("Template " + FixFacing(root.transform, m.transform, names[i]));
            root.SetActive(false);
            dir.templates[i] = root;
        }
    }

    // ================================================================== player + performance
    static void PlayerAndPerformance(List<string> log)
    {
        var origin = Object.FindAnyObjectByType<XROrigin>();
        if (origin != null)
        {
            if (origin.GetComponent<DesktopPlayer>() == null) origin.gameObject.AddComponent<DesktopPlayer>();
            if (origin.GetComponent<PlayerNoise>() == null) origin.gameObject.AddComponent<PlayerNoise>();
            if (origin.Camera != null)
            {
                Undo.RecordObject(origin.Camera, "far clip");
                origin.Camera.farClipPlane = 30f; // fog hides everything past ~15 m anyway
                origin.Camera.nearClipPlane = 0.05f;
            }
        }

        var input = GameObject.Find("Input Test");
        if (input != null) input.SetActive(false); // it logged "Value: 0" every frame

        foreach (var asset in new[] { "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/Mobile_RPAsset.asset" })
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(asset);
            if (urp == null) continue;
            urp.shadowDistance = 18f;
            urp.maxAdditionalLightsCount = 4;
            EditorUtility.SetDirty(urp);
        }
        AssetDatabase.SaveAssets();
        log.Add("Performance: far clip 30 m, shadows 18 m, max 4 extra lights per object, only the current stage is active, static batching on level geometry, no realtime shadows on lamps.");
    }

    public static string FixFacing(EntityBase e) => FixFacing(e.transform, e.transform.Find("Model"), e.name);

    /// <summary>
    /// Makes sure the monster's face points along its root's forward axis: compares where its front feature
    /// (Watcher eyes, Crawler mouth, Mother hair) sits relative to the body, and turns the model 180 degrees if needed.
    /// </summary>
    public static string FixFacing(Transform root, Transform model, string name)
    {
        var smr = model != null ? model.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        if (smr == null)
            return name + ": no skinned mesh";
        var mats = smr.sharedMaterials;
        var featureIndex = System.Array.FindIndex(mats, m => m != null && (m.name.Contains("eyes") || m.name.Contains("mouth") || m.name.Contains("hair")));
        if (featureIndex < 0)
            return name + ": no front feature found";
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var verts = baked.vertices;
        var all = Vector3.zero;
        foreach (var v in verts) all += v;
        all /= verts.Length;
        var feature = Vector3.zero;
        var idx = baked.GetTriangles(featureIndex);
        foreach (var i in idx) feature += verts[i];
        feature /= Mathf.Max(1, idx.Length);
        Object.DestroyImmediate(baked);
        var dir = smr.transform.TransformDirection(feature - all);
        dir.y = 0f;
        var dot = Vector3.Dot(dir.normalized, root.forward);
        if (dot < 0f)
        {
            model.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.localRotation;
            return name + ": was facing backwards - turned around";
        }
        return name + ": facing forward (ok)";
    }

    // ================================================================== geometry helpers
    struct Opening
    {
        public float at, width, bottom, top;
        public bool door;
        public Opening(float at, float width, float bottom, float top, bool door)
        {
            this.at = at; this.width = width; this.bottom = bottom; this.top = top; this.door = door;
        }
    }

    static Opening Door(float z) => new Opening(z, 0.95f, 0f, 2.12f, true);

    static Opening[] Offset(Opening[] o, float by) => o.Select(x => new Opening(x.at + by, x.width, x.bottom, x.top, x.door)).ToArray();

    /// <summary>
    /// An axis-aligned wall from 'from' to 'to' (along Z when alongZ, else along X) at 'fixedCoord', with holes for
    /// doors/windows. With 'lower' set it gets wood panelling up to 0.95 m and a rail, wallpaper above (picture 1).
    /// Long runs are cut into pieces of at most 5 m so each piece only gets the lamps near it.
    /// </summary>
    static void Wall(Transform parent, string name, bool alongZ, float fixedCoord, float from, float to, float thickness, Opening[] openings, Material upper, Material lower, Material trim, float height)
    {
        const float rail = 0.95f, railH = 0.06f;
        void Piece(float s0, float s1, float y0, float y1, Material m, float extra = 0f, bool col = true)
        {
            if (s1 - s0 < 0.01f || y1 - y0 < 0.01f) return;
            var c = alongZ ? new Vector3(fixedCoord, (y0 + y1) / 2f, (s0 + s1) / 2f) : new Vector3((s0 + s1) / 2f, (y0 + y1) / 2f, fixedCoord);
            var size = alongZ ? new Vector3(thickness + extra, y1 - y0, s1 - s0) : new Vector3(s1 - s0, y1 - y0, thickness + extra);
            Box(parent, name, c, size, m, m == lower ? 1.0f : 1.2f, col);
        }
        void Solid(float s0, float s1)
        {
            for (var a = s0; a < s1 - 0.001f; a += 5f)
            {
                var b = Mathf.Min(s1, a + 5f);
                if (lower != null)
                {
                    Piece(a, b, 0f, rail, lower);
                    Piece(a, b, rail, rail + railH, trim ?? lower, 0.04f, false);
                    Piece(a, b, rail + railH, height, upper);
                }
                else
                    Piece(a, b, 0f, height, upper);
            }
        }
        var cursor = from;
        foreach (var o in openings.OrderBy(o => o.at))
        {
            var a = o.at - o.width / 2f;
            var b = o.at + o.width / 2f;
            Solid(cursor, a);
            Piece(a, b, o.top, height, upper);
            if (o.bottom > 0f)
            {
                if (lower != null && o.bottom > rail)
                {
                    Piece(a, b, 0f, rail, lower);
                    Piece(a, b, rail, o.bottom, upper);
                }
                else
                    Piece(a, b, 0f, o.bottom, lower ?? upper);
            }
            if (o.door && trim != null)
            {
                Piece(a - 0.07f, a, 0f, o.top + 0.07f, trim, 0.05f, false);
                Piece(b, b + 0.07f, 0f, o.top + 0.07f, trim, 0.05f, false);
                Piece(a - 0.07f, b + 0.07f, o.top, o.top + 0.07f, trim, 0.05f, false);
            }
            cursor = b;
        }
        Solid(cursor, to);
    }

    /// <summary>A pack door on a hinge in one of the corridor walls, with a brass number plate and handle.</summary>
    static HauntedDoor HotelDoor(Transform parent, string number, bool leftWall, Vector3 at, float startOpen, Material brass, string folder = "Door01", string file = "Door")
    {
        const float X = 1.1f, width = 0.95f, height = 2.1f;
        var wallX = at.x + (leftWall ? -X - WT / 2f : X + WT / 2f);
        var hinge = new GameObject("Door " + number).transform;
        hinge.SetParent(parent, true);
        hinge.position = new Vector3(wallX, 0f, at.z - width / 2f);
        var leaf = new GameObject("Leaf").transform;
        leaf.SetParent(hinge, false);
        var doorMat = folder == "Door01" ? Mat("wood_dark") : null; // dark wood hotel doors (picture 1)
        var model = Spawn(folder, file, leaf, new Vector3(wallX, 0f, at.z), 0f, 0f, doorMat, false);
        var b = Bounds(model);
        if (b.size.x > b.size.z)
        {
            Object.DestroyImmediate(model);
            model = Spawn(folder, file, leaf, new Vector3(wallX, 0f, at.z), 0f, 90f, doorMat, false);
            b = Bounds(model);
        }
        leaf.localScale = new Vector3(Mathf.Clamp(0.05f / Mathf.Max(0.01f, b.size.x), 0.5f, 2f), height / Mathf.Max(0.1f, b.size.y), width / Mathf.Max(0.1f, b.size.z));
        b = Bounds(model);
        model.transform.position += new Vector3(wallX - b.center.x, -b.min.y, hinge.position.z - b.min.z);
        ClearStatic(hinge.gameObject);
        // one simple box collider on the hinge (so the door can be used, and moves with it)
        foreach (var c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        b = Bounds(model);
        var dc = hinge.gameObject.AddComponent<BoxCollider>();
        dc.center = hinge.InverseTransformPoint(b.center);
        dc.size = new Vector3(Mathf.Max(0.06f, b.size.x), b.size.y, b.size.z);

        var face = leftWall ? 1f : -1f; // towards the corridor
        var frontX = wallX + face * 0.035f;
        var plate = Box(hinge, "Plate", new Vector3(frontX, 1.58f, at.z), new Vector3(0.01f, 0.08f, 0.17f), brass, 1f, false, false);
        var label = new GameObject("Number");
        label.transform.SetParent(plate.transform, true);
        label.transform.position = new Vector3(frontX + face * 0.007f, 1.58f, at.z);
        label.transform.rotation = Quaternion.LookRotation(new Vector3(-face, 0f, 0f));
        Text(label, number == "Stairwell" ? "" : number, 0.006f, new Color(0.12f, 0.08f, 0.04f));
        Box(hinge, "Handle", new Vector3(frontX + face * 0.01f, 0.98f, at.z + width / 2f - 0.1f), new Vector3(0.05f, 0.03f, 0.12f), brass, 1f, false, false);

        var hd = hinge.gameObject.AddComponent<HauntedDoor>();
        hd.openSign = leftWall ? -1f : 1f;
        hd.startOpen = startOpen;
        hinge.localRotation = Quaternion.Euler(0f, startOpen * hd.openSign, 0f);
        return hd;
    }

    static void DarkCloset(Transform parent, string name, Vector3 center, Vector3 size, Material mat)
    {
        Box(parent, name + " floor", center + new Vector3(0f, -0.05f, 0f), new Vector3(size.x, 0.1f, size.z), mat, 1.5f);
        Box(parent, name + " ceiling", center + new Vector3(0f, size.y + 0.05f, 0f), new Vector3(size.x, 0.1f, size.z), mat, 1.5f);
        Box(parent, name + " back", center + new Vector3(-size.x / 2f, size.y / 2f, 0f), new Vector3(0.1f, size.y, size.z), mat, 1.5f);
        Box(parent, name + " side", center + new Vector3(0f, size.y / 2f, -size.z / 2f), new Vector3(size.x, size.y, 0.1f), mat, 1.5f);
        Box(parent, name + " side", center + new Vector3(0f, size.y / 2f, size.z / 2f), new Vector3(size.x, size.y, 0.1f), mat, 1.5f);
    }

    static void Dust(Transform parent, Vector3 center, Vector3 size)
    {
        var go = new GameObject("Dust in the air");
        go.transform.SetParent(parent, true);
        go.transform.position = center;
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = 14f;
        main.startSpeed = 0.02f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.028f);
        main.startColor = new Color(0.9f, 0.88f, 0.8f, 0.55f);
        main.maxParticles = 260;
        main.gravityModifier = -0.0004f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission;
        em.rateOverTime = 18f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = size;
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.03f;
        noise.frequency = 0.2f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = DustMat();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    static Material DustMat()
    {
        var path = k_GameMats + "dust.mat";
        Directory.CreateDirectory(k_GameMats);
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var isNew = m == null;
        if (isNew) m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(k_GameTex + "dust.png"));
        m.SetColor("_BaseColor", new Color(0.7f, 0.68f, 0.6f, 0.6f));
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 2f); // additive
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.One);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = 3000;
        if (isNew) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
        return m;
    }

    enum Facing { PosX, NegX, PosZ, NegZ, PosY }

    /// <summary>A flat picture (sign, poster, decal) on a wall, showing the whole texture once.</summary>
    static GameObject Sign(Transform parent, string name, Vector3 center, Vector2 size, Facing facing, Material mat)
    {
        Vector3 s;
        switch (facing)
        {
            case Facing.PosX: case Facing.NegX: s = new Vector3(0.006f, size.y, size.x); break;
            case Facing.PosY: s = new Vector3(size.x, 0.004f, size.y); break;
            default: s = new Vector3(size.x, size.y, 0.006f); break;
        }
        var go = new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.position = center;
        go.AddComponent<MeshFilter>().sharedMesh = SignMesh(s, facing);
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    static Mesh SignMesh(Vector3 s, Facing facing)
    {
        // one quad facing the viewer, UVs 0..1 (readable, not mirrored)
        var h = s / 2f;
        Vector3 n, u, v;
        switch (facing)
        {
            case Facing.PosX: n = Vector3.right; u = Vector3.forward; v = Vector3.up; break;
            case Facing.NegX: n = Vector3.left; u = Vector3.back; v = Vector3.up; break;
            case Facing.PosZ: n = Vector3.forward; u = Vector3.left; v = Vector3.up; break;
            case Facing.NegZ: n = Vector3.back; u = Vector3.right; v = Vector3.up; break;
            default: n = Vector3.up; u = Vector3.right; v = Vector3.forward; break;
        }
        var hu = Mathf.Abs(Vector3.Dot(h, u)); var hv = Mathf.Abs(Vector3.Dot(h, v)); var hn = Mathf.Abs(Vector3.Dot(h, n));
        var c = n * hn;
        var mesh = new Mesh { name = "Sign" };
        mesh.vertices = new[] { c - u * hu - v * hv, c - u * hu + v * hv, c + u * hu + v * hv, c + u * hu - v * hv };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
        mesh.normals = new[] { n, n, n, n };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    /// <summary>A box with world-aligned tiling UVs (the texture repeats every 'tile' metres and lines up across pieces).</summary>
    static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material mat, float tile = 1.2f, bool collider = true, bool isStatic = true, bool stretch = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.position = center;
        go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(center, size, tile, stretch);
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (collider)
            go.AddComponent<BoxCollider>().size = size;
        if (isStatic)
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        return go;
    }

    /// <summary>A small box positioned and rotated inside a parent (keypad parts, pallets).</summary>
    static GameObject LocalBox(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, bool collider)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(Vector3.zero, size, 0.5f, true);
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (collider)
            go.AddComponent<BoxCollider>().size = size;
        return go;
    }

    static Mesh BoxMesh(Vector3 world, Vector3 size, float tile, bool stretch)
    {
        var h = size / 2f;
        var faces = new[]
        {
            (n: Vector3.right, u: Vector3.forward, v: Vector3.up),
            (n: Vector3.left, u: Vector3.back, v: Vector3.up),
            (n: Vector3.forward, u: Vector3.left, v: Vector3.up),
            (n: Vector3.back, u: Vector3.right, v: Vector3.up),
            (n: Vector3.up, u: Vector3.right, v: Vector3.forward),
            (n: Vector3.down, u: Vector3.right, v: Vector3.back),
        };
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        foreach (var f in faces)
        {
            var hn = Mathf.Abs(Vector3.Dot(h, f.n));
            var hu = Mathf.Abs(Vector3.Dot(h, f.u));
            var hv = Mathf.Abs(Vector3.Dot(h, f.v));
            var c = f.n * hn;
            var corners = new[] { c - f.u * hu - f.v * hv, c - f.u * hu + f.v * hv, c + f.u * hu + f.v * hv, c + f.u * hu - f.v * hv };
            var i0 = verts.Count;
            foreach (var p in corners)
            {
                verts.Add(p);
                norms.Add(f.n);
                if (stretch)
                    uvs.Add(new Vector2(Vector3.Dot(p, f.u) / (2f * Mathf.Max(hu, 1e-4f)) + 0.5f, Vector3.Dot(p, f.v) / (2f * Mathf.Max(hv, 1e-4f)) + 0.5f));
                else
                {
                    var wp = world + p;
                    uvs.Add(new Vector2(Vector3.Dot(wp, f.u) / tile, Vector3.Dot(wp, f.v) / tile));
                }
            }
            tris.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
        }
        var mesh = new Mesh { name = "Box" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    static TextMesh Text(GameObject go, string text, float charSize, Color color)
    {
        var tm = go.AddComponent<TextMesh>();
        tm.font = s_Font != null ? s_Font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
        tm.fontSize = 64;
        tm.characterSize = charSize;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
        tm.text = text;
        return tm;
    }

    /// <summary>Hotel material saved in Assets/Game/Materials (URP Lit, PS1 textures from Assets/Game/Textures).</summary>
    static Material GMat(string name, string tex, Color? tint = null, Color? emission = null, bool cutout = false, float smooth = 0.05f)
    {
        Directory.CreateDirectory(k_GameMats);
        var path = k_GameMats + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var isNew = m == null;
        if (isNew) m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        var t = tex != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(k_GameTex + tex + ".png") : null;
        if (tex != null && t == null) Debug.LogWarning("[Horror Game] Missing texture " + tex);
        m.SetTexture("_BaseMap", t);
        m.SetColor("_BaseColor", tint ?? Color.white);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", 0f);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            if (t != null) m.SetTexture("_EmissionMap", t);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        if (cutout)
        {
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.AlphaTest;
        }
        if (isNew) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Furniture you walk around should never trap you: no collision (you can still see it).</summary>
    static void NoBlock(GameObject go)
    {
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
    }

    static ScareEvent Scare(Transform parent, string name, ScareEvent.Kind kind, Vector3 at)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.position = at;
        var s = go.AddComponent<ScareEvent>();
        s.kind = kind;
        return s;
    }

    /// <summary>Pushes a prop back until it touches a wall at x = wallX.</summary>
    static void AgainstWall(GameObject go, float wallX, bool wallOnNegativeSide)
    {
        var b = Bounds(go);
        var dx = wallOnNegativeSide ? (wallX + 0.02f) - b.min.x : (wallX - 0.02f) - b.max.x;
        go.transform.position += new Vector3(dx, 0f, 0f);
    }

    static void WallThing(string model, Transform parent, Vector3 R, float X, float z, float y, bool left)
    {
        Spawn(model, model, parent, R + new Vector3(left ? -X + 0.04f : X - 0.04f, 0f, z), y, left ? 90f : -90f);
    }

    /// <summary>A pack prop that can be grabbed, thrown and smashed.</summary>
    static GameObject Breakable(string folder, string file, Transform parent, Vector3 center, float bottomY, float yaw, float mass, float breakSpeed, string sound, int pieces = 2)
    {
        var go = Throwable(folder, file, parent, center, bottomY, mass);
        if (go.name.StartsWith("MISSING")) return go;
        var d = go.AddComponent<Destructible>();
        d.breakSpeed = breakSpeed;
        d.piecesPerAxis = pieces;
        d.breakSound = sound;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation;
        return go;
    }

    /// <summary>A pack prop that can be grabbed and thrown (makes noise where it lands).</summary>
    static GameObject Throwable(string folder, string file, Transform parent, Vector3 center, float bottomY, float mass)
    {
        var go = Spawn(folder, file, parent, center, bottomY, 0f, null, false);
        if (go.name.StartsWith("MISSING")) return go;
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        var b = Bounds(go);
        var col = go.AddComponent<BoxCollider>();
        col.center = go.transform.InverseTransformPoint(b.center);
        col.size = go.transform.InverseTransformVector(b.size).Abs();
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        go.AddComponent<XRGrabInteractable>();
        return go;
    }

    static Vector3 Abs(this Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    // ================================================================== pack helpers
    static Transform Child(Transform parent, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static Transform Marker(string name, Transform parent, Vector3 pos, float yaw)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, true);
        t.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        return t;
    }

    static TriggerZone Zone(string name, Transform parent, Vector3 center, Vector3 size, string narration)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.position = center;
        var box = go.AddComponent<BoxCollider>();
        box.size = size;
        box.isTrigger = true;
        var z = go.AddComponent<TriggerZone>();
        z.narrationId = narration;
        return z;
    }

    static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>(k_Mats + name + ".mat");

    static GameObject Model(string folder, string file) => AssetDatabase.LoadAssetAtPath<GameObject>(k_Models + folder + "/" + file + ".fbx");

    static GameObject Spawn(string folder, string file, Transform parent, Vector3 center, float bottomY, float yaw, Material overrideMat = null, bool isStatic = true)
    {
        var model = Model(folder, file);
        if (model == null)
        {
            Debug.LogWarning("[Horror Game] Missing model " + folder + "/" + file);
            var missing = new GameObject("MISSING " + file);
            missing.transform.SetParent(parent, true);
            return missing;
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation;
        var b = Bounds(go);
        go.transform.position += new Vector3(center.x - b.center.x, bottomY - b.min.y, center.z - b.center.z);
        if (overrideMat != null)
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = r.sharedMaterials.Select(_ => overrideMat).ToArray();
        if (isStatic)
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        return go;
    }

    static void ClearStatic(GameObject go)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0)
            return new Bounds(go.transform.position, Vector3.one * 0.1f);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Light PointLight(Transform parent, string name, Vector3 pos, Color color, float intensity, float range, LightShadows shadows)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = shadows;
        return l;
    }

    /// <summary>Creates an entity: a root with the logic (+ NavMeshAgent) and the animated model as a child.</summary>
    static TEntity Entity<TEntity>(string name, Transform parent, Vector3 pos, float yaw, System.Action<NavMeshAgent> agentSetup) where TEntity : EntityBase
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, true);
        root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Entities/" + name + "/" + name + ".fbx");
        if (model != null)
        {
            var m = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            m.name = "Model";
            m.transform.localPosition = Vector3.zero;
            m.transform.localRotation = Quaternion.identity;
            foreach (var r in m.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = ShadowCastingMode.Off;
        }
        else
            Debug.LogWarning("[Horror Game] Missing entity model for " + name);
        if (agentSetup != null)
        {
            var agent = root.AddComponent<NavMeshAgent>();
            agent.baseOffset = 0f;
            agentSetup(agent);
        }
        return root.AddComponent<TEntity>();
    }

    static NavMeshSurface NavVolume(string name, Vector3 center, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.position = center;
        var s = go.AddComponent<NavMeshSurface>();
        s.collectObjects = CollectObjects.Volume;
        s.center = Vector3.zero;
        s.size = size;
        s.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        return s;
    }
}
