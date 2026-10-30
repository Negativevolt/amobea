using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// PC TEST MODE. When no headset is connected (or Mode = Always PC), the VR rig becomes a keyboard-and-mouse
/// first-person player:
///   WASD move · Shift run (noisy!) · mouse look · Left click torch on/off · E use / pick up / drop ·
///   Right click throw · Esc frees the mouse.
/// The torch lies on the table by the lift: pick it up with E (it stays in your hand). Put on a headset (Mode = Auto) and the normal VR
/// controls take over again with no changes.
/// </summary>
[RequireComponent(typeof(XROrigin))]
public class DesktopPlayer : MonoBehaviour
{
    public enum Mode { Auto, AlwaysPC, AlwaysVR }
    public Mode mode = Mode.Auto;
    public float walkSpeed = 1.8f, runSpeed = 3.4f, mouseSensitivity = 0.1f, eyeHeight = 1.65f, reach = 3.0f;
    [Tooltip("C toggles crouch: lower eyes, slow and quiet steps.")]
    public float crouchEyeHeight = 1.0f, crouchSpeed = 0.9f;

    XROrigin m_Origin;
    CharacterController m_CC;
    Transform m_Cam;
    float m_Pitch, m_FallSpeed, m_OffsetY, m_EyeY;
    bool m_Crouching;
    static float s_ShakeAmount, s_ShakeEnd, s_ShakeLength = 1f;
    static float s_PunchDegrees, s_PunchStart, s_PunchLength = 1f;
    float m_BaseFov = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { s_ShakeAmount = 0f; s_ShakeEnd = 0f; s_ShakeLength = 1f; s_PunchDegrees = 0f; s_PunchStart = -10f; s_PunchLength = 1f; }

    /// <summary>
    /// Snaps the view in (field of view narrows by <paramref name="degrees"/>) and lets it ease back out over
    /// <paramref name="seconds"/>: a jump scare's lurch on a monitor (PC test mode only; never in VR).
    /// </summary>
    public static void Punch(float degrees, float seconds)
    {
        s_PunchDegrees = degrees;
        s_PunchStart = Time.time;
        s_PunchLength = Mathf.Max(0.05f, seconds);
    }

    /// <summary>Shakes the camera (PC test mode only; never in VR, it makes people sick).</summary>
    public static void Shake(float amount, float seconds)
    {
        s_ShakeAmount = amount;
        s_ShakeLength = Mathf.Max(0.01f, seconds);
        s_ShakeEnd = Time.time + seconds;
    }
    Rigidbody m_Held;
    PSXFlashlight m_Torch;
    bool m_TorchHeld;
    string m_Prompt;
    bool m_Active;

    IEnumerator Start()
    {
        if (PlaytestDriver.Running)
            mode = Mode.AlwaysPC; // the playtest bot uses keyboard mode (this run only, nothing is saved)
        yield return null;
        // Is a headset connected? OpenXR (Quest Link / Air Link) can take a few seconds to start its session,
        // so if an XR loader is running we wait for the headset instead of falling back to PC mode too early.
        var loader = UnityEngine.XR.Management.XRGeneralSettings.Instance != null && UnityEngine.XR.Management.XRGeneralSettings.Instance.Manager != null
            ? UnityEngine.XR.Management.XRGeneralSettings.Instance.Manager.activeLoader : null;
        var headset = HeadsetPresent();
        for (var t = 0f; loader != null && !headset && t < 6f; t += Time.unscaledDeltaTime)
        {
            yield return null;
            headset = HeadsetPresent();
        }
        WriteXRStatus(loader, headset);
        if (mode == Mode.AlwaysVR || (mode == Mode.Auto && (headset || loader != null)))
        {
            PlayerRig.DesktopMode = false;
            // A real headset is here: the XR Interaction / Device Simulator (fake keyboard headset) would override its
            // tracking and freeze the view, so switch it off wherever it is (scene or auto-spawned).
            if (headset)
                foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (t != null && t.parent == null && (t.name.Contains("XR Interaction Simulator") || t.name.Contains("XR Device Simulator")))
                    {
                        Debug.Log("[DesktopPlayer] Real headset found: switching off " + t.name);
                        t.gameObject.SetActive(false);
                    }
            enabled = false;
            yield break;
        }

        m_Active = true;
        PlayerRig.DesktopMode = true;
        m_Origin = GetComponent<XROrigin>();
        m_Cam = m_Origin.Camera.transform;

        // Stop headset tracking and VR-only parts from fighting the keyboard.
        foreach (var tpd in GetComponentsInChildren<UnityEngine.InputSystem.XR.TrackedPoseDriver>(true))
            tpd.enabled = false;
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t != transform && (t.name == "Left Hand" || t.name == "Rigth Hand" || t.name == "Right Hand"))
                t.gameObject.SetActive(false);
        foreach (var b in GetComponentsInChildren<MonoBehaviour>(true))
        {
            var ns = b.GetType().Namespace ?? "";
            if (ns.Contains("Locomotion"))
                b.enabled = false;
        }
        foreach (var root in gameObject.scene.GetRootGameObjects())
            if (root.name.Contains("XR Interaction Simulator") || root.name.Contains("XR Device Simulator"))
                root.SetActive(false);

        m_CC = GetComponent<CharacterController>();
        if (m_CC == null)
            m_CC = gameObject.AddComponent<CharacterController>();
        m_CC.height = 1.8f;
        m_CC.radius = 0.25f;
        m_CC.center = new Vector3(0f, 0.9f, 0f);
        m_CC.enabled = true;

        var offset = m_Origin.CameraFloorOffsetObject != null ? m_Origin.CameraFloorOffsetObject.transform : m_Cam.parent;
        m_OffsetY = offset != null ? offset.localPosition.y : 0f;
        m_EyeY = eyeHeight;
        m_Cam.localPosition = new Vector3(0f, eyeHeight - m_OffsetY, 0f);
        m_Cam.localRotation = Quaternion.identity;

        // The torch lies on the console table by the lift: walk up and pick it up (E). It stays in your hand.
        m_Torch = FindAnyObjectByType<PSXFlashlight>();
        if (m_Torch != null)
            m_Torch.SetOn(true, false); // already on, so you can spot it glowing on the table

        LockMouse(true);
        Narrator.Hint("PC TEST: WASD move, Shift run, C crouch, mouse look, E use/pick up (grab the torch on the table), Left click torch on/off, Right click throw");
        yield return new WaitForSeconds(9f);
        Narrator.SayOnce("N1_02");
    }

    static bool HeadsetPresent()
    {
        if (UnityEngine.XR.XRSettings.isDeviceActive)
            return true;
        var heads = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(UnityEngine.XR.InputDeviceCharacteristics.HeadMounted, heads);
        return heads.Count > 0;
    }

    /// <summary>Writes what XR found (loader, headset, controllers) next to the playtest files, to debug VR.</summary>
    static void WriteXRStatus(UnityEngine.XR.Management.XRLoader loader, bool headset)
    {
        try
        {
            var all = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
            UnityEngine.XR.InputDevices.GetDevices(all);
            var text = "time " + System.DateTime.Now + "\nloader " + (loader != null ? loader.name : "none")
                       + "\nheadset " + headset + "\ndevice active " + UnityEngine.XR.XRSettings.isDeviceActive
                       + "\ndevices:\n" + string.Join("\n", all.ConvertAll(d => "  " + d.name + " [" + d.characteristics + "]"));
            // Write next to the project (two levels up from Assets/) so it never ends up inside Assets/
            var dir = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", ".."));
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "xr_status.txt"), text);
        }
        catch { }
    }

    void EquipTorch()
    {
        var grab = m_Torch.GetComponent<XRGrabInteractable>();
        if (grab != null) grab.enabled = false;
        var rb = m_Torch.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
        }
        foreach (var c in m_Torch.GetComponentsInChildren<Collider>())
            c.enabled = false;
        m_Torch.transform.SetParent(m_Cam, false);
        m_Torch.transform.localPosition = new Vector3(0.2f, -0.2f, 0.35f);
        m_Torch.transform.localRotation = Quaternion.Euler(1f, -3f, 0f);
        m_Torch.SetOn(true, false);
        m_TorchHeld = true;
        GameFlow.AddItem("torch");
        Narrator.SayOnce("N1_02");
    }

    static void LockMouse(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    void Update()
    {
        if (!m_Active)
            return;
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null)
            return;

        if (kb.escapeKey.wasPressedThisFrame)
            LockMouse(Cursor.lockState != CursorLockMode.Locked);
        if (Cursor.lockState != CursorLockMode.Locked && mouse.leftButton.wasPressedThisFrame)
            LockMouse(true);

        // Look
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            var d = mouse.delta.ReadValue() * mouseSensitivity;
            transform.Rotate(0f, d.x, 0f, Space.World);
            m_Pitch = Mathf.Clamp(m_Pitch - d.y, -80f, 80f);
            m_Cam.localRotation = Quaternion.Euler(m_Pitch, 0f, 0f);
        }

        // Move
        var input = Vector2.zero;
        if (kb.wKey.isPressed) input.y += 1f;
        if (kb.sKey.isPressed) input.y -= 1f;
        if (kb.dKey.isPressed) input.x += 1f;
        if (kb.aKey.isPressed) input.x -= 1f;
        if (PlayerRig.MovementLocked) input = Vector2.zero;
        // Crouch (C toggles). Can't stand up under something low.
        if (kb.cKey.wasPressedThisFrame)
        {
            if (!m_Crouching)
                m_Crouching = true;
            else if (!Physics.SphereCast(transform.position + Vector3.up * 0.5f, 0.2f, Vector3.up, out _, 1.2f, ~0, QueryTriggerInteraction.Ignore))
                m_Crouching = false;
            else
                Narrator.Hint("Not enough room to stand.");
        }
        PlayerRig.IsCrouching = m_Crouching;
        m_EyeY = Mathf.MoveTowards(m_EyeY, m_Crouching ? crouchEyeHeight : eyeHeight, 4f * Time.deltaTime);
        var camPos = m_Cam.localPosition;
        camPos.y = m_EyeY - m_OffsetY;
        camPos.x = 0f;
        camPos.z = 0f;
        if (Time.time < s_ShakeEnd)
            camPos += Random.insideUnitSphere * s_ShakeAmount * ((s_ShakeEnd - Time.time) / s_ShakeLength);
        m_Cam.localPosition = camPos;
        var camera = m_Cam.GetComponent<Camera>();
        if (camera != null)
        {
            if (m_BaseFov < 0f)
                m_BaseFov = camera.fieldOfView;
            // in hard within ~0.06 s, then ease back out
            var pt = Time.time - s_PunchStart;
            var punch = pt < 0f || pt > s_PunchLength ? 0f : pt < 0.06f ? pt / 0.06f : 1f - Mathf.SmoothStep(0f, 1f, (pt - 0.06f) / (s_PunchLength - 0.06f));
            camera.fieldOfView = m_BaseFov - s_PunchDegrees * punch;
        }
        var ccHeight = m_Crouching ? 1.15f : 1.8f;
        if (!Mathf.Approximately(m_CC.height, ccHeight))
        {
            m_CC.height = ccHeight;
            m_CC.center = new Vector3(0f, ccHeight / 2f, 0f);
        }

        var speed = m_Crouching ? crouchSpeed : kb.leftShiftKey.isPressed ? runSpeed : walkSpeed;
        var move = (transform.forward * input.y + transform.right * input.x);
        if (move.sqrMagnitude > 1f) move.Normalize();
        m_FallSpeed = m_CC.isGrounded ? -1f : m_FallSpeed - 9.81f * Time.deltaTime;
        if (m_CC.enabled)
            m_CC.Move((move * speed + Vector3.up * m_FallSpeed) * Time.deltaTime);

        // Torch
        if (m_Torch != null && m_TorchHeld && m_Held == null && Cursor.lockState == CursorLockMode.Locked && mouse.leftButton.wasPressedThisFrame)
            m_Torch.Toggle();

        // Look at / use things (with aim assist: you only need to look roughly at an item)
        m_Prompt = null;
        Interactable target = null;
        Rigidbody grabbable = null;
        PlaygroundResetButton reset = null;
        m_HasTarget = false;
        if (m_Held == null)
        {
            var best = FindTarget();
            if (best != null)
            {
                m_HasTarget = true;
                if (m_Torch != null && !m_TorchHeld && best.transform.IsChildOf(m_Torch.transform))
                {
                    m_Prompt = "E: Pick up torch";
                    if (kb.eKey.wasPressedThisFrame)
                    {
                        EquipTorch();
                        return;
                    }
                }
                else
                {
                    target = best.GetComponentInParent<Interactable>();
                    reset = target == null ? best.GetComponentInParent<PlaygroundResetButton>() : null;
                    if (target != null)
                        m_Prompt = "E: " + target.prompt;
                    else if (reset != null)
                        m_Prompt = "E: Reset playground";
                    else
                    {
                        grabbable = best.attachedRigidbody;
                        m_Prompt = "E: Pick up";
                    }
                }
            }
        }
        else
            m_Prompt = "E: Drop    Right click: Throw";

        if (kb.eKey.wasPressedThisFrame)
        {
            if (m_Held != null) Release(Vector3.zero);
            else if (target != null) target.Interact();
            else if (reset != null) reset.GetComponentInParent<DestructionPlayground>()?.Rebuild();
            else if (grabbable != null) Hold(grabbable);
        }
        if (m_Held != null && mouse.rightButton.wasPressedThisFrame)
            Release(m_Cam.forward * 7f + Vector3.up * 1.5f);
    }

    bool m_HasTarget;
    readonly Collider[] m_Near = new Collider[64];

    /// <summary>
    /// Best thing to use/pick up: the collider closest to the centre of the screen (within ~14 degrees),
    /// in reach and not behind a wall. Small things like keys and batteries no longer need pixel-perfect aim.
    /// </summary>
    Collider FindTarget()
    {
        var eye = m_Cam.position;
        var fwd = m_Cam.forward;
        // 1) exactly under the crosshair wins
        if (Physics.Raycast(eye, fwd, out var hit, reach, ~0, QueryTriggerInteraction.Ignore) && Usable(hit.collider))
            return hit.collider;
        // 2) otherwise the usable thing nearest to where you're looking
        Collider best = null;
        var bestScore = float.MaxValue;
        var n = Physics.OverlapSphereNonAlloc(eye + fwd * reach * 0.5f, reach * 0.6f, m_Near, ~0, QueryTriggerInteraction.Collide);
        for (var i = 0; i < n; i++)
        {
            var c = m_Near[i];
            if (!Usable(c))
                continue;
            var point = c.bounds.center;
            var to = point - eye;
            var dist = to.magnitude;
            if (dist > reach + 0.3f || dist < 0.05f)
                continue;
            var angle = Vector3.Angle(fwd, to);
            if (angle > 14f)
                continue;
            // line of sight: nothing solid in between (the item itself is fine)
            if (Physics.Raycast(eye, to / dist, out var block, dist - 0.05f, ~0, QueryTriggerInteraction.Ignore) && !SameThing(block.collider, c))
                continue;
            var score = angle + dist * 2f;
            if (score < bestScore)
            {
                bestScore = score;
                best = c;
            }
        }
        return best;
    }

    static bool SameThing(Collider a, Collider b)
    {
        if (a == b)
            return true;
        if (a.attachedRigidbody != null && a.attachedRigidbody == b.attachedRigidbody)
            return true;
        var ia = a.GetComponentInParent<Interactable>();
        return ia != null && ia == b.GetComponentInParent<Interactable>();
    }

    bool Usable(Collider c)
    {
        if (c == null || !c.enabled)
            return false;
        if (m_Torch != null && c.transform.IsChildOf(m_Torch.transform))
            return !m_TorchHeld; // the torch on the table can be picked up
        var it = c.GetComponentInParent<Interactable>();
        if (it != null)
            return it.isActiveAndEnabled;
        if (c.GetComponentInParent<PlaygroundResetButton>() != null)
            return true;
        var rb = c.attachedRigidbody;
        return !c.isTrigger && rb != null && !rb.isKinematic && rb.mass < 20f;
    }

    void FixedUpdate()
    {
        if (m_Held == null)
            return;
        var hold = m_Cam.position + m_Cam.forward * 0.75f - m_Cam.up * 0.15f;
        var v = (hold - m_Held.worldCenterOfMass) / Time.fixedDeltaTime * 0.8f;
        m_Held.linearVelocity = Vector3.ClampMagnitude(v, 12f);
        m_Held.angularVelocity *= 0.6f;
        if (Vector3.Distance(hold, m_Held.worldCenterOfMass) > 2.5f)
            Release(Vector3.zero); // got stuck behind something
    }

    void Hold(Rigidbody rb)
    {
        m_Held = rb;
        rb.useGravity = false;
        rb.linearDamping = 5f;
        foreach (var c in rb.GetComponentsInChildren<Collider>())
            Physics.IgnoreCollision(c, m_CC, true);
    }

    void Release(Vector3 velocity)
    {
        var rb = m_Held;
        m_Held = null;
        rb.useGravity = true;
        rb.linearDamping = 0f;
        rb.linearVelocity = velocity;
        foreach (var c in rb.GetComponentsInChildren<Collider>())
            Physics.IgnoreCollision(c, m_CC, false);
        if (velocity.sqrMagnitude > 0f)
            NoiseSystem.Emit(rb.position, 4f);
    }

    void OnGUI()
    {
        if (!m_Active)
            return;
        var c = new Vector2(Screen.width / 2f, Screen.height / 2f);
        var size = m_HasTarget ? 8f : 4f;
        GUI.color = m_HasTarget ? new Color(0.95f, 0.88f, 0.65f, 0.95f) : new Color(1f, 1f, 1f, 0.6f);
        GUI.DrawTexture(new Rect(c.x - size / 2f, c.y - size / 2f, size, size), Texture2D.whiteTexture);
        if (!string.IsNullOrEmpty(m_Prompt))
        {
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Max(18, Screen.height / 40), fontStyle = FontStyle.Bold };
            GUI.color = new Color(0.95f, 0.88f, 0.65f, 0.9f);
            GUI.Label(new Rect(c.x - 200f, c.y + 20f, 400f, 30f), m_Prompt, style);
        }
    }
}
