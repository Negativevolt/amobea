using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Room 306 (stage 1). The door creaks open by itself as you pass and something whispers inside. Step in and:
/// the door slams shut behind you, your torch dies, a breath right at your ear... then the Watcher is in your face.
/// Afterwards the door creaks open again so you can get out.
/// Built when the scene loads, next to the dark space behind "Door 306" (made by the Horror Game builder).
/// </summary>
public class Room306Scare : MonoBehaviour
{
    [Tooltip("Who's waiting in the dark.")]
    public Monster monster = Monster.Watcher;
    [Tooltip("Seconds in the dark between the door slamming and the face.")]
    public float darkness = 1.6f;
    [Tooltip("How long it stays in your face.")]
    public float hold = 1.1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (FindAnyObjectByType<Room306Scare>() != null)
            return;
        HauntedDoor door = null;
        foreach (var d in FindObjectsByType<HauntedDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (d.name == "Door 306")
                door = d;
        Transform floor = null;
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == "306 (dark) floor")
                floor = t;
        if (door == null || floor == null)
            return; // not the hotel scene

        // the box covers the dark room but not its doorway, so you only set it off by really stepping in
        var go = new GameObject("Scare - something in 306");
        go.transform.SetParent(floor.parent, true);
        // "in" = from the door hinge toward the room, squared to the room's walls (the hinge is off to one side)
        var inward = floor.position - door.transform.position;
        inward = Mathf.Abs(inward.x) > Mathf.Abs(inward.z) ? new Vector3(Mathf.Sign(inward.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(inward.z));
        go.transform.SetPositionAndRotation(new Vector3(floor.position.x, floor.position.y + 1.5f, floor.position.z) + inward * 0.2f,
            Quaternion.LookRotation(inward));
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(1.4f, 3f, 1.2f); // 1.2 deep: starts ~0.5 m past the doorway
        var zone = go.AddComponent<TriggerZone>();
        var scare = go.AddComponent<Room306Scare>();
        scare.m_Door = door;
        zone.onEnter.AddListener(scare.Play);
    }

    HauntedDoor m_Door;

    public void Play() => StartCoroutine(Run());

    IEnumerator Run()
    {
        // silence, then the door slams behind you
        ScareDirector.Duck(darkness + 1.5f, 0.03f);
        yield return new WaitForSeconds(0.45f);
        if (m_Door != null)
            m_Door.Slam();
        ScareDirector.Haptics(0.5f, 0.15f);

        // pitch black: the torch dies, something breathes right next to you
        var torch = EntityBase.Torch;
        if (torch != null)
            torch.ForceBlackout(darkness + hold + 1f);
        ScareDirector.KillLights(transform.position, 6f, darkness + hold + 1f);
        yield return new WaitForSeconds(0.5f);
        var head = PlayerRig.Head;
        if (head != null)
            GameAudio.At("crawler_breath", head.position + head.right * 0.35f - head.forward * 0.1f, 0.9f, 4f);
        yield return new WaitForSeconds(Mathf.Max(0f, darkness - 0.5f));

        // there it is
        yield return ScareDirector.InFace(monster, hold, instant: true);

        // let them out
        yield return new WaitForSeconds(1.2f);
        if (m_Door != null)
            m_Door.CreakOpen();
    }
}
