using UnityEngine;

/// <summary>
/// The player's own body (PSX Casual Male by vinrax). Follows the headset: the body stands under your head and turns
/// with it, the arms reach for the controllers (two-bone IK), the knees bend when you crouch and the legs step when you
/// move. The head and the model's hands are hidden (the camera sits in the head; the animated VR hands are shown instead).
/// In PC mode the arms just hang.
/// </summary>
[DefaultExecutionOrder(1000)]
public class PlayerBody : MonoBehaviour
{
    [Tooltip("The model (child) with the bones.")]
    public Transform model;
    [Tooltip("How far the neck sits behind and below your eyes (metres).")]
    public float neckBack = 0.11f, neckDown = 0.13f;
    [Tooltip("How fast the body turns to follow your head.")]
    public float turnSpeed = 6f;
    [Tooltip("Hide the head, neck and shoulders so none of them block the camera when you look down. The arms, hands, chest and legs still show.")]
    public bool hideUpperBody = true;
    [Tooltip("VR: use the body's own low-poly hands (they follow the controllers and make a fist on grip) instead of the old smooth hands.")]
    public bool useModelHands = true;
    [Tooltip("How far the fingers curl for a full fist (degrees).")]
    public float fistAngle = 85f;

    Transform m_Pelvis, m_Head, m_Neck;
    Transform m_LUpper, m_LFore, m_LHand, m_RUpper, m_RFore, m_RHand;
    Transform m_LThigh, m_LShin, m_LFoot, m_RThigh, m_RShin, m_RFoot;
    Transform m_LFinger, m_LThumb1, m_LThumb2, m_RFinger, m_RThumb1, m_RThumb2;
    float m_LGrip, m_RGrip;
    bool m_HandsSetUp;
    Quaternion m_HeadRestLocalRot = Quaternion.identity;
    Transform m_LTarget, m_RTarget;
    Transform[] m_Bones;
    Quaternion[] m_Rest;
    float m_EyeHeight = -1f, m_ModelEye, m_Yaw, m_StepPhase;
    Vector3 m_LastPos;
    float m_CalibrateUntil;

    void Start()
    {
        if (model == null && transform.childCount > 0)
            model = transform.GetChild(0);
        if (model == null)
        {
            enabled = false;
            return;
        }
        foreach (var c in model.GetComponentsInChildren<Collider>(true))
            Destroy(c);
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (r is SkinnedMeshRenderer smr)
                smr.updateWhenOffscreen = true; // it's always right under the camera
        }
        m_Pelvis = Find("pelvis"); m_Head = Find("head"); m_Neck = Find("neck");
        m_LUpper = Find("upper_arm_left"); m_LFore = Find("forearm_left"); m_LHand = Find("hand_left");
        m_RUpper = Find("upper_arm_right"); m_RFore = Find("forearm_right"); m_RHand = Find("hand_right");
        m_LThigh = Find("thigh_left"); m_LShin = Find("shin.L"); m_LFoot = Find("foot_left");
        m_RThigh = Find("thigh_right"); m_RShin = Find("shin.R"); m_RFoot = Find("foot_right");
        m_LFinger = Find("f_middle.01.L"); m_LThumb1 = Find("thumb.01.L"); m_LThumb2 = Find("thumb.02.L");
        m_RFinger = Find("f_middle.01.R"); m_RThumb1 = Find("thumb.01.R"); m_RThumb2 = Find("thumb.02.R");
        // whichever way the model was exported, turn it so it faces forward (its right shoulder on our right)
        if (m_LUpper != null && m_RUpper != null)
        {
            transform.rotation = Quaternion.identity;
            var right = m_RUpper.position - m_LUpper.position;
            right.y = 0f;
            if (right.sqrMagnitude > 1e-6f)
                model.rotation = Quaternion.FromToRotation(right.normalized, Vector3.right) * model.rotation;
            model.localPosition = Vector3.zero;
        }
        m_Bones = model.GetComponentsInChildren<Transform>(true);
        m_Rest = new Quaternion[m_Bones.Length];
        for (var i = 0; i < m_Bones.Length; i++)
            m_Rest[i] = m_Bones[i].localRotation;

        // model's eye height in its own rest pose (feet on the ground)
        transform.localScale = Vector3.one;
        var rs = model.GetComponentsInChildren<Renderer>();
        var height = 1.75f;
        if (rs.Length > 0)
        {
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            height = b.max.y - transform.position.y;
        }
        m_ModelEye = Mathf.Max(0.05f, height * 0.93f); // eyes sit at ~93% of body height
        m_CalibrateUntil = Time.time + 3f;

        // Head: hidden from your own view (scaled tiny), but still rotates with the camera so the body looks right
        // in mirrors, shadows, and to other viewers.
        m_HeadRestLocalRot = m_Head != null ? m_Head.localRotation : Quaternion.identity;
        if (hideUpperBody && m_Head != null)
            m_Head.localScale = Vector3.one * 0.001f; // the head, nothing else - shrinking the shoulders also shrinks the arms
        if (!useModelHands)
        {
            if (m_LHand != null) m_LHand.localScale = Vector3.one * 0.001f;
            if (m_RHand != null) m_RHand.localScale = Vector3.one * 0.001f;
        }
        Application.onBeforeRender += Solve;
    }

    void OnDestroy() => Application.onBeforeRender -= Solve;

    Transform Find(string bone)
    {
        foreach (var t in model.GetComponentsInChildren<Transform>(true))
            if (t.name == bone)
                return t;
        return null;
    }

    Transform HandTarget(bool left)
    {
        var origin = PlayerRig.Origin;
        if (origin == null) return null;
        foreach (var t in origin.GetComponentsInChildren<Transform>())
            if (left ? t.name == "Left Hand" : (t.name == "Rigth Hand" || t.name == "Right Hand"))
                return t;
        return null;
    }

    void LateUpdate() => Solve();

    void Solve()
    {
        var head = PlayerRig.Head;
        var origin = PlayerRig.Origin;
        if (head == null || origin == null || model == null)
            return;
        var floor = origin.transform.position.y;
        var eye = head.position.y - floor;

        // standing eye height: learn it during the first seconds (tallest seen), PC mode uses its fixed eye height
        if (m_EyeHeight < 0f || (Time.time < m_CalibrateUntil && eye > m_EyeHeight))
            m_EyeHeight = Mathf.Clamp(eye, 1.3f, 2.0f);
        var scale = m_EyeHeight / m_ModelEye;
        transform.localScale = Vector3.one * scale;

        // reset the pose every frame (no drift)
        for (var i = 0; i < m_Bones.Length; i++)
            if (m_Bones[i] != null)
                m_Bones[i].localRotation = m_Rest[i];

        // body yaw follows the head
        var fwd = head.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude > 0.001f)
        {
            var target = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            m_Yaw = Mathf.LerpAngle(m_Yaw, target, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
            if (Mathf.Abs(Mathf.DeltaAngle(m_Yaw, target)) > 60f)
                m_Yaw = target - Mathf.Sign(Mathf.DeltaAngle(m_Yaw, target)) * 60f;
        }
        var yaw = Quaternion.Euler(0f, m_Yaw, 0f);
        transform.rotation = yaw;

        // Head bone: mirror the camera's full rotation (so the body "looks around" with you).
        // The head is a child of the body, so we rotate it in the body's local space.
        if (m_Head != null)
        {
            // where the camera looks, expressed in the body's space (undo body yaw first)
            var localLook = Quaternion.Inverse(yaw) * head.rotation;
            // the head bone's rest pose faces... whatever the model exported; apply the look rotation on top.
            m_Head.localRotation = m_HeadRestLocalRot * localLook;
        }

        // stand under the head: crouching lowers the whole body, the knees bend to keep the feet on the floor
        var drop = Mathf.Clamp(m_EyeHeight - eye, 0f, m_EyeHeight * 0.55f);
        var neckPoint = head.position - yaw * Vector3.forward * neckBack;
        transform.position = new Vector3(neckPoint.x, floor - drop, neckPoint.z);
        var headOffset = m_Head != null ? m_Head.position - transform.position : Vector3.up * m_EyeHeight;
        transform.position += new Vector3(neckPoint.x - transform.position.x - headOffset.x, 0f, neckPoint.z - transform.position.z - headOffset.z);

        // legs: planted feet when crouching, a simple step swing when walking
        var moved = transform.position - m_LastPos;
        moved.y = 0f;
        m_LastPos = transform.position;
        var speed = Time.deltaTime > 0f ? moved.magnitude / Time.deltaTime : 0f;
        var stride = Mathf.Clamp01(speed / 1.5f);
        m_StepPhase += speed * Time.deltaTime * 5.5f / Mathf.Max(0.5f, scale);
        var swing = Mathf.Sin(m_StepPhase) * 0.18f * stride * m_EyeHeight;
        var lift = 0.06f * stride * m_EyeHeight;
        var moveDir = moved.sqrMagnitude > 1e-6f ? moved.normalized : yaw * Vector3.forward;
        Leg(m_LThigh, m_LShin, m_LFoot, floor, moveDir * swing, Mathf.Max(0f, Mathf.Sin(m_StepPhase)) * lift, yaw);
        Leg(m_RThigh, m_RShin, m_RFoot, floor, -moveDir * swing, Mathf.Max(0f, -Mathf.Sin(m_StepPhase)) * lift, yaw);

        // arms reach for the controllers (VR only)
        if (!PlayerRig.DesktopMode)
        {
            if (m_LTarget == null) m_LTarget = HandTarget(true);
            if (m_RTarget == null) m_RTarget = HandTarget(false);
            if (useModelHands && !m_HandsSetUp)
                SetUpHands();
            Arm(m_LUpper, m_LFore, m_LHand, m_LTarget, yaw, -1f);
            Arm(m_RUpper, m_RFore, m_RHand, m_RTarget, yaw, 1f);
            if (useModelHands)
            {
                m_LGrip = Mathf.MoveTowards(m_LGrip, Grip(UnityEngine.XR.XRNode.LeftHand, m_LTarget), 12f * Time.deltaTime);
                m_RGrip = Mathf.MoveTowards(m_RGrip, Grip(UnityEngine.XR.XRNode.RightHand, m_RTarget), 12f * Time.deltaTime);
                Hand(m_LHand, m_LTarget, m_LFinger, m_LThumb1, m_LThumb2, m_LGrip, true);
                Hand(m_RHand, m_RTarget, m_RFinger, m_RThumb1, m_RThumb2, m_RGrip, false);
            }
        }
    }

    void Arm(Transform upper, Transform fore, Transform hand, Transform target, Quaternion yaw, float side)
    {
        if (upper == null || fore == null || hand == null || target == null || !target.gameObject.activeInHierarchy)
            return;
        // the wrist sits a little behind the controller
        var wrist = target.position - target.forward * 0.07f;
        var hint = upper.position + yaw * new Vector3(side * 0.35f, -0.6f, -0.25f); // elbows out and down
        TwoBone(upper, fore, hand, wrist, hint);
    }

    /// <summary>Hide the old smooth hand models; the body's hands take over. (The guide lines are VRHandGrab's.)</summary>
    void SetUpHands()
    {
        var origin = PlayerRig.Origin;
        if (origin == null || m_LTarget == null || m_RTarget == null)
            return;
        m_HandsSetUp = true;
        foreach (var t in origin.GetComponentsInChildren<Transform>(true))
            if (t.name == "Left Hand Model" || t.name == "Right Hand Model")
                t.gameObject.SetActive(false);
    }

    static readonly System.Collections.Generic.List<UnityEngine.XR.InputDevice> s_Devices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();

    static float Grip(UnityEngine.XR.XRNode node, Transform handRoot)
    {
        var v = 0f;
        UnityEngine.XR.InputDevices.GetDevicesAtXRNode(node, s_Devices);
        foreach (var d in s_Devices)
            if (d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out var g))
                v = Mathf.Max(v, g);
        if (handRoot != null)
        {
            var nf = handRoot.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor>();
            if (nf != null && nf.hasSelection) v = 1f; // closed around what you're holding
        }
        return v;
    }

    /// <summary>Turns the model's hand to match the controller (fingers forward, thumb on top) and curls it into a fist.</summary>
    void Hand(Transform hand, Transform target, Transform finger, Transform thumb1, Transform thumb2, float grip, bool left)
    {
        if (hand == null || target == null || !target.gameObject.activeInHierarchy)
            return;
        if (finger != null)
        {
            var fingerDir = finger.position - hand.position;
            if (fingerDir.sqrMagnitude > 1e-8f)
                hand.rotation = Quaternion.FromToRotation(fingerDir, target.forward) * hand.rotation;
        }
        if (thumb1 != null)
        {
            var th = Vector3.ProjectOnPlane(thumb1.position - hand.position, target.forward);
            var want = Vector3.ProjectOnPlane(target.up, target.forward);
            if (th.sqrMagnitude > 1e-8f && want.sqrMagnitude > 1e-8f)
                hand.rotation = Quaternion.FromToRotation(th, want) * hand.rotation;
        }
        // fingers sweep in toward the palm (thumb on top, palm facing inward)
        var palm = left ? target.right : -target.right;
        Curl(finger, hand.position, palm - target.forward * 0.3f, fistAngle * grip);
        Curl(thumb1, hand.position, palm - target.up * 0.4f, 35f * grip);
        Curl(thumb2, thumb1 != null ? thumb1.position : hand.position, palm - target.up * 0.6f, 45f * grip);
    }

    static void Curl(Transform bone, Vector3 from, Vector3 toward, float angle)
    {
        if (bone == null || angle <= 0.01f)
            return;
        var dir = bone.position - from;
        if (dir.sqrMagnitude < 1e-10f)
            dir = bone.up;
        var axis = Vector3.Cross(dir.normalized, toward.normalized);
        if (axis.sqrMagnitude < 1e-8f)
            return;
        bone.rotation = Quaternion.AngleAxis(angle, axis.normalized) * bone.rotation;
    }

    void Leg(Transform thigh, Transform shin, Transform foot, float floor, Vector3 swing, float lift, Quaternion yaw)
    {
        if (thigh == null || shin == null || foot == null)
            return;
        var restFoot = foot.position; // rest pose (already reset this frame)
        var target = new Vector3(restFoot.x, floor + (restFoot.y - transform.position.y), restFoot.z) + swing + Vector3.up * lift;
        var hint = shin.position + yaw * Vector3.forward * 0.5f; // knees forward
        TwoBone(thigh, shin, foot, target, hint);
    }

    /// <summary>Bends a two-bone chain (shoulder-elbow-wrist / hip-knee-ankle) so the end reaches the target.</summary>
    static void TwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint)
    {
        var la = Vector3.Distance(a.position, b.position);
        var lb = Vector3.Distance(b.position, c.position);
        var toTarget = target - a.position;
        var lt = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(la - lb) + 0.001f, la + lb - 0.001f);
        var dir = toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : Vector3.down;
        var cosA = Mathf.Clamp((la * la + lt * lt - lb * lb) / (2f * la * lt), -1f, 1f);
        var bend = Vector3.ProjectOnPlane(hint - a.position, dir);
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.down, dir);
        bend.Normalize();
        var elbow = a.position + (dir * cosA + bend * Mathf.Sqrt(1f - cosA * cosA)) * la;
        a.rotation = Quaternion.FromToRotation(b.position - a.position, elbow - a.position) * a.rotation;
        var end = a.position + dir * lt;
        b.rotation = Quaternion.FromToRotation(c.position - b.position, end - b.position) * b.rotation;
    }
}
