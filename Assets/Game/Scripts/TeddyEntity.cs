using System.Collections;
using UnityEngine;

/// <summary>
/// The Teddy: sits still next to the TV. The VHS tape is hidden inside it.
/// Interaction is handled by the sibling TeddyInteractable component — this class
/// just runs the scare sequence and tracks state.
/// </summary>
public class TeddyEntity : EntityBase
{
    [Tooltip("The VHS tape hidden inside the bear — revealed after the scare.")]
    public GameObject hiddenTape;
    [Tooltip("Where the bear's face is, in its own space (the Teddybear model looks down its -Z axis).")]
    public Vector3 faceLocal = new Vector3(0f, 0.36f, -0.14f);
    [Tooltip("How far in front of your eyes the face stops in the lunge (metres).")]
    public float faceDistance = 0.3f;

    bool m_ScareTriggered;

    // Turns the bear so its face (local -Z) looks along -dir, i.e. at someone standing at the far end of dir.
    static Quaternion FaceToward(Vector3 dirToViewer, Vector3 up) =>
        Quaternion.LookRotation(-dirToViewer, up);

    // Bear pose that puts its face at facePos looking straight at the eyes.
    void PlaceFace(Vector3 facePos, Transform head)
    {
        var rot = FaceToward(head.position - facePos, Vector3.up);
        transform.SetPositionAndRotation(facePos - rot * faceLocal, rot);
    }

    public void TriggerHeadScare()
    {
        if (m_ScareTriggered) return;
        m_ScareTriggered = true;
        StartCoroutine(HeadScareRoutine());
    }

    IEnumerator HeadScareRoutine()
    {
        var head = PlayerRig.Head;
        if (head == null) yield break;

        // Lock in the "face toward player" rotation — reused throughout so orientation is always correct
        var toPlayer = head.position - transform.position;
        toPlayer.y = 0f;
        var faceRot = toPlayer.sqrMagnitude > 0.01f
            ? FaceToward(toPlayer.normalized, Vector3.up)
            : transform.rotation;
        var awayRot = faceRot * Quaternion.Euler(0f, 180f, 0f);

        // Snap to face-player first so we start from a known orientation
        transform.rotation = faceRot;

        // --- ACT 1: atmosphere ---
        ScareDirector.KillLights(transform.position, 8f, 5f);
        ScareDirector.Duck(5f, 0.02f);
        GameAudio.At("static_burst", transform.position, 0.35f, 8f);
        yield return new WaitForSeconds(0.5f);

        // --- ACT 2: whole bear slowly turns its back to the player (no rig = no head bone) ---
        GameAudio.At("distant_creak", transform.position, 1f, 6f, 0f);
        for (var elapsed = 0f; elapsed < 1.3f; elapsed += Time.deltaTime)
        {
            var k = Mathf.SmoothStep(0f, 1f, elapsed / 1.3f);
            transform.rotation = Quaternion.Slerp(faceRot, awayRot, k);
            yield return null;
        }
        transform.rotation = awayRot;
        GameAudio.At("distant_knock", transform.position, 0.9f, 5f, 0f); // click as it stops
        ScareDirector.Haptics(0.3f, 0.1f);

        // --- ACT 3: holds — back to you, completely still, uncomfortable silence ---
        yield return new WaitForSeconds(Random.Range(1.6f, 2.4f));

        // --- ACT 4: SNAP back + full audio hit ---
        transform.rotation = faceRot; // instant snap back to face you
        GameAudio.Flat("stinger_face", 1f);
        GameAudio.Flat("stinger_jump", 1f);
        GameAudio.Flat("static_burst", 0.7f);
        GameAudio.At("crawler_screech", transform.position, 1f, 10f);
        ScareDirector.Haptics(1f, 0.6f);
        DesktopPlayer.Shake(0.16f, 1.2f);
        DesktopPlayer.Punch(22f, 1.4f);
        PlayerHealth.TakeScareDamage();

        var lamp = new GameObject("Bear Face Light").AddComponent<Light>();
        lamp.type      = LightType.Point;
        lamp.range     = 2.2f;
        lamp.color     = new Color(1f, 0.85f, 0.7f);
        lamp.shadows   = LightShadows.None;

        // --- ACT 5: LUNGE: the face flies up to your eyes, wherever you look ---
        var startFace = transform.TransformPoint(faceLocal);
        for (var elapsed = 0f; elapsed < 0.1f; elapsed += Time.deltaTime)
        {
            head = PlayerRig.Head;
            if (head == null) break;
            var facePos = Vector3.Lerp(startFace, head.position + head.forward * faceDistance, elapsed / 0.1f);
            PlaceFace(facePos, head);
            lamp.transform.position = facePos - Vector3.up * 0.15f; // lit from below
            lamp.intensity = Random.value < 0.15f ? 1f : Random.Range(4f, 9f);
            yield return null;
        }

        // Hold in your face, eye to eye, twitching
        for (var elapsed = 0f; elapsed < 1.1f; elapsed += Time.deltaTime)
        {
            head = PlayerRig.Head;
            if (head == null) break;
            var jitter = elapsed > 0.15f ? (Vector3)Random.insideUnitSphere * 0.03f : Vector3.zero;
            var facePos = head.position + head.forward * faceDistance + jitter;
            PlaceFace(facePos, head);
            lamp.transform.position = facePos - Vector3.up * 0.15f;
            lamp.intensity = Random.value < 0.12f ? 0.5f : Random.Range(4f, 10f);
            yield return null;
        }

        Object.Destroy(lamp.gameObject);
        yield return ScreenFade.FadeTo(1f, 0.04f);

        if (hiddenTape != null)
            hiddenTape.SetActive(true);
        HideBear(); // renderers + collider off — keeps GO active so coroutine finishes

        yield return new WaitForSeconds(0.5f);
        yield return ScreenFade.FadeTo(0f, 0.9f);
    }

    // Hide without SetActive(false) so the coroutine keeps running on this MonoBehaviour
    void HideBear()
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            r.enabled = false;
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
        var ti = GetComponent<TeddyInteractable>();
        if (ti != null) ti.enabled = false;
    }

    public override void ResetEntity()
    {
        m_ScareTriggered = false;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            r.enabled = true;
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = true;
        var ti = GetComponent<TeddyInteractable>();
        if (ti != null) ti.enabled = true;
        if (hiddenTape != null)
            hiddenTape.SetActive(false);
        base.ResetEntity();
    }
}
