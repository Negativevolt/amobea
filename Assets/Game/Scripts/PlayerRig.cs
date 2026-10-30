using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>Quick access to the player (works in VR and in PC mode).</summary>
public static class PlayerRig
{
    static XROrigin s_Origin;
    static Transform s_Head;

    /// <summary>True while the player stands inside a hiding spot (closet). The Crawler can't find them.</summary>
    public static bool IsHiding;
    /// <summary>Crouching (PC mode, C key): quieter footsteps.</summary>
    public static bool IsCrouching;

    /// <summary>True when playing with keyboard and mouse instead of a headset.</summary>
    public static bool DesktopMode;

    /// <summary>While true the player can look around but not walk (set by the Narrator during instructions).</summary>
    public static bool MovementLocked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { s_Origin = null; s_Head = null; IsHiding = false; IsCrouching = false; DesktopMode = false; MovementLocked = false; }

    public static XROrigin Origin
    {
        get
        {
            if (s_Origin == null)
                s_Origin = Object.FindAnyObjectByType<XROrigin>();
            return s_Origin;
        }
    }

    public static Transform Head
    {
        get
        {
            if (s_Head == null)
            {
                var o = Origin;
                if (o != null && o.Camera != null)
                    s_Head = o.Camera.transform;
                else if (Camera.main != null)
                    s_Head = Camera.main.transform;
            }
            return s_Head;
        }
    }

    /// <summary>Point on the floor under the player's head.</summary>
    public static Vector3 Feet
    {
        get
        {
            var h = Head;
            if (h == null)
                return Vector3.zero;
            var y = Origin != null ? Origin.transform.position.y : h.position.y - 1.6f;
            return new Vector3(h.position.x, y, h.position.z);
        }
    }

    /// <summary>Moves the whole rig so the player's head ends up above <paramref name="spot"/>, facing its forward.</summary>
    public static void TeleportTo(Transform spot)
    {
        var o = Origin;
        if (o == null || spot == null)
            return;
        var cc = o.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        var yaw = Quaternion.Euler(0f, spot.eulerAngles.y, 0f);
        var headYaw = Head != null ? Quaternion.Euler(0f, Head.eulerAngles.y - o.transform.eulerAngles.y, 0f) : Quaternion.identity;
        o.transform.rotation = yaw * Quaternion.Inverse(headYaw);
        var offset = Head != null ? Head.position - o.transform.position : Vector3.zero;
        offset.y = 0f;
        o.transform.position = spot.position - offset;

        if (cc != null)
        {
            // Before the headset is tracked the VR body can be measured as almost 0 m tall; Unity then refuses to
            // switch the controller back on if its step height is bigger than the body ("Step Offset must be less..."),
            // and the player can't move at all. Keep the step height valid.
            // Unity's actual limit: stepOffset < height + radius (the capsule top to bottom NOT counting both caps).
            var maxStep = Mathf.Max(0f, cc.height + cc.radius - 0.01f);
            if (cc.stepOffset > maxStep)
                cc.stepOffset = maxStep;
            cc.enabled = true;
        }
    }
}

/// <summary>Things that make noise call this; the Crawler listens.</summary>
public static class NoiseSystem
{
    /// <summary>(position, how far away it can be heard in metres)</summary>
    public static event System.Action<Vector3, float> Heard;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Heard = null; }

    public static void Emit(Vector3 position, float radius) => Heard?.Invoke(position, radius);
}
