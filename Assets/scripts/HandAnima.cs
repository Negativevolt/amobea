using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Hand poses: squeeze grip and the hand closes into a fist (trigger curls the index finger).
/// Reads the input actions, and the controller directly as a backup, so the fist always works.
/// While you hold something the hand stays closed around it.
/// </summary>
public class HandAnima : MonoBehaviour
{
    public InputActionProperty triggervalue;
    public InputActionProperty gripvalue;
    public Animator handanimator;
    [Tooltip("How fast the fingers close and open.")]
    public float speed = 12f;

    static readonly List<UnityEngine.XR.InputDevice> s_Devices = new List<UnityEngine.XR.InputDevice>();
    XRNode m_Node;
    NearFarInteractor m_Interactor;
    float m_Grip, m_Trigger;

    void Start()
    {
        m_Node = XRNode.RightHand;
        for (var t = transform; t != null; t = t.parent)
            if (t.name.ToLower().Contains("left")) { m_Node = XRNode.LeftHand; break; }
        if (handanimator == null)
            handanimator = GetComponentInChildren<Animator>();
        Enable(triggervalue);
        Enable(gripvalue);
    }

    static void Enable(InputActionProperty p)
    {
        try { if (p.action != null && !p.action.enabled) p.action.Enable(); } catch { }
    }

    float Read(InputActionProperty p, InputFeatureUsage<float> usage)
    {
        var v = 0f;
        try { if (p.action != null) v = p.action.ReadValue<float>(); } catch { }
        InputDevices.GetDevicesAtXRNode(m_Node, s_Devices);
        foreach (var d in s_Devices)
            if (d.TryGetFeatureValue(usage, out var f))
                v = Mathf.Max(v, f);
        return v;
    }

    void Update()
    {
        if (handanimator == null)
            return;
        if (m_Interactor == null && transform.parent != null)
            m_Interactor = transform.parent.GetComponentInChildren<NearFarInteractor>(true);
        var grip = Read(gripvalue, UnityEngine.XR.CommonUsages.grip);
        var trigger = Read(triggervalue, UnityEngine.XR.CommonUsages.trigger);
        if (m_Interactor != null && m_Interactor.hasSelection)
            grip = 1f; // closed around whatever you're holding
        m_Grip = Mathf.MoveTowards(m_Grip, grip, speed * Time.deltaTime);
        m_Trigger = Mathf.MoveTowards(m_Trigger, trigger, speed * Time.deltaTime);
        handanimator.SetFloat("Grip", m_Grip);
        handanimator.SetFloat("Trigger", m_Trigger);
    }
}
