using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;

/// <summary>
/// The VR button layout (Quest / Touch controllers):
///   Left stick   walk          · click it to run (loud: the Crawler hears you) until you stop
///   Right stick  turn
///   GRIP         grab / use    · TRIGGER  use, or torch on/off while holding it (VRHandGrab)
///   A            jump          · B  crouch on/off (slower, quieter; crouching for real works too)
///   X            torch on/off  · hold Y  show this card above your left hand
/// Added automatically to the XR rig whenever a scene loads. PC mode has its own keys (DesktopPlayer).
/// </summary>
public class VRControls : MonoBehaviour
{
    [Tooltip("Walking speed (m/s).")]
    public float walkSpeed = 2f;
    [Tooltip("Running speed (m/s), after clicking the left stick.")]
    public float runSpeed = 3.4f;
    [Tooltip("Speed while crouched (m/s).")]
    public float crouchSpeed = 1f;
    [Tooltip("How far crouch (B) lowers your view (metres).")]
    public float crouchDrop = 0.6f;
    [Tooltip("Jump height (metres).")]
    public float jumpHeight = 0.5f;
    [Tooltip("Your head this far below your standing height counts as crouching, button or not (metres).")]
    public float realCrouchDrop = 0.35f;

    const string k_Card =
        "<b>CONTROLS</b>\n" +
        "Left stick   walk  (click: run)\n" +
        "Right stick   turn\n" +
        "Grip   grab / use (hold to carry)\n" +
        "Torch   grip once to take, it stays in that hand\n" +
        "Trigger   use, torch on/off in hand\n" +
        "A   jump        B   crouch\n" +
        "X   torch on/off\n" +
        "Hold Y   this card";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var origin = Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (origin != null && origin.GetComponent<VRControls>() == null)
            origin.gameObject.AddComponent<VRControls>();
    }

    static readonly Dictionary<string, bool> s_WasDown = new Dictionary<string, bool>();

    Unity.XR.CoreUtils.XROrigin m_Origin;
    ContinuousMoveProvider m_Move;
    bool m_Running, m_CrouchToggle, m_HintShown;
    float m_StillFor, m_CrouchApplied, m_StandingEye = -1f;
    TextMesh m_Card;

    void Start()
    {
        m_Origin = GetComponent<Unity.XR.CoreUtils.XROrigin>();
        m_Move = GetComponentInChildren<ContinuousMoveProvider>(true);
        // Jump was switched off on the rig; A jumps again, but only a hop (this isn't a platformer).
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == "Jump" && t.GetComponent<JumpProvider>() != null)
            {
                t.GetComponent<JumpProvider>().jumpHeight = jumpHeight;
                t.gameObject.SetActive(true);
            }
    }

    void Update()
    {
        if (PlayerRig.DesktopMode)
            return; // keyboard and mouse: DesktopPlayer
        var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (!left.isValid && !right.isValid)
            return;

        if (!m_HintShown && Time.timeSinceLevelLoad > 12f)
        {
            m_HintShown = true;
            Narrator.Hint("Hold Y to see the controls.");
        }

        // X: torch on/off, wherever it is (like F in PC mode)
        if (Pressed(left, CommonUsages.primaryButton, "X"))
        {
            var torch = FindAnyObjectByType<PSXFlashlight>();
            if (torch != null)
                torch.Toggle();
        }

        // B: crouch on/off
        if (Pressed(right, CommonUsages.secondaryButton, "B"))
        {
            m_CrouchToggle = !m_CrouchToggle;
            if (m_CrouchToggle)
                m_Running = false;
        }

        // Left stick click: run until you let go of the stick
        left.TryGetFeatureValue(CommonUsages.primary2DAxis, out var stick);
        if (Pressed(left, CommonUsages.primary2DAxisClick, "LeftStick"))
        {
            m_Running = !m_Running;
            if (m_Running)
                m_CrouchToggle = false;
        }
        m_StillFor = stick.magnitude < 0.15f ? m_StillFor + Time.deltaTime : 0f;
        if (m_StillFor > 0.4f)
            m_Running = false;

        UpdateCrouch();
        if (m_Move != null)
            m_Move.moveSpeed = PlayerRig.IsCrouching ? crouchSpeed : m_Running ? runSpeed : walkSpeed;

        // Hold Y: controls card
        left.TryGetFeatureValue(CommonUsages.secondaryButton, out var y);
        ShowCard(y);
    }

    void UpdateCrouch()
    {
        // Crouch button: lower the view by moving the camera offset down (smoothly, so it doesn't jolt).
        var offset = m_Origin != null && m_Origin.CameraFloorOffsetObject != null ? m_Origin.CameraFloorOffsetObject.transform : null;
        var target = m_CrouchToggle ? -crouchDrop : 0f;
        var next = Mathf.MoveTowards(m_CrouchApplied, target, 2.5f * Time.deltaTime);
        if (offset != null && !Mathf.Approximately(next, m_CrouchApplied))
        {
            offset.localPosition += Vector3.up * (next - m_CrouchApplied);
            m_CrouchApplied = next;
        }

        // Crouching for real counts too: head well below your standing height.
        var head = PlayerRig.Head;
        var realCrouch = false;
        if (head != null && m_Origin != null)
        {
            var eye = head.position.y - m_Origin.transform.position.y - m_CrouchApplied;
            if (Time.timeSinceLevelLoad < 3f || eye > m_StandingEye)
                m_StandingEye = Mathf.Max(m_StandingEye, eye);
            realCrouch = m_StandingEye > 1f && eye < m_StandingEye - realCrouchDrop;
        }
        PlayerRig.IsCrouching = m_CrouchToggle || realCrouch;
    }

    static bool Pressed(InputDevice device, InputFeatureUsage<bool> usage, string key)
    {
        device.TryGetFeatureValue(usage, out var down);
        s_WasDown.TryGetValue(key, out var was);
        s_WasDown[key] = down;
        return down && !was;
    }

    void ShowCard(bool show)
    {
        var head = PlayerRig.Head;
        Transform hand = null;
        if (show)
            foreach (var t in GetComponentsInChildren<Transform>())
                if (t.name == "Left Hand") { hand = t; break; }
        if (!show || head == null || hand == null)
        {
            if (m_Card != null && m_Card.gameObject.activeSelf)
                m_Card.gameObject.SetActive(false);
            return;
        }
        if (m_Card == null)
        {
            var go = new GameObject("Controls Card");
            m_Card = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m_Card.font = font;
            var mat = WorldText.MaterialFor(font);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat != null ? mat : font.material;
            m_Card.fontSize = 80;
            m_Card.characterSize = 0.0022f;
            m_Card.anchor = TextAnchor.LowerCenter;
            m_Card.alignment = TextAlignment.Left;
            m_Card.richText = true;
            m_Card.color = new Color(0.95f, 0.88f, 0.65f);
            m_Card.text = k_Card;
        }
        var pos = hand.position + Vector3.up * 0.08f;
        m_Card.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - head.position, Vector3.up));
        if (!m_Card.gameObject.activeSelf)
            m_Card.gameObject.SetActive(true);
    }

    void OnDestroy()
    {
        if (m_Card != null)
            Destroy(m_Card.gameObject);
    }
}
