using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// Self-bootstrapping VR setup. Fires after scene load — no GameObject attachment needed.
/// Handles the two things VRHandGrab does NOT cover:
///   • SphereCollider + "Player" tag on Main Camera (used by physics proximity queries)
///   • CharacterController sizing (keeps the player on geometry)
/// Hand setup (grabbing, guide lines, highlights) is handled by VRHandGrab.
/// </summary>
public static class VRPlayerSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var origin = Object.FindAnyObjectByType<XROrigin>();
        if (origin == null) return;

        SetupHead(origin);
        FixCharacterController(origin.gameObject);
    }

    static void SetupHead(XROrigin origin)
    {
        var cam = origin.Camera;
        if (cam == null) return;

        if (cam.GetComponent<SphereCollider>() == null)
        {
            var sc       = cam.gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius    = 0.18f;
            sc.center    = Vector3.zero;
        }

        if (cam.gameObject.CompareTag("Untagged"))
            cam.gameObject.tag = "Player";
    }

    static void FixCharacterController(GameObject root)
    {
        var cc = root.GetComponent<CharacterController>();
        if (cc == null) cc = root.AddComponent<CharacterController>();

        if (cc.height < 0.5f || cc.height > 2.5f) cc.height = 1.8f;
        if (cc.radius < 0.1f)                      cc.radius = 0.2f;
        cc.center     = new Vector3(0f, cc.height * 0.5f, 0f);
        cc.stepOffset = Mathf.Min(cc.stepOffset, cc.height - 0.01f);
        cc.skinWidth  = 0.02f;
        cc.enabled    = true;
    }
}
