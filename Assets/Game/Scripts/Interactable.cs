using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Something the player can use: press E on it (PC test), or in VR reach for it / point the guide line at it and press
/// GRIP or TRIGGER.
/// </summary>
public abstract class Interactable : MonoBehaviour
{
    [Tooltip("Shown when looking at it (PC test) or pointing a VR hand at it.")]
    public string prompt = "Use";

    XRBaseInteractable m_Xr;
    float m_NextVRUse;

    protected virtual void Awake()
    {
        m_Xr = GetComponent<XRBaseInteractable>();
        if (m_Xr == null)
            m_Xr = gameObject.AddComponent<XRSimpleInteractable>();
        m_Xr.selectEntered.AddListener(OnSelect);
        m_Xr.activated.AddListener(OnActivate);
    }

    void OnSelect(SelectEnterEventArgs _) => UseFromVR();

    // TRIGGER on something the hand already grips was already used by the grip.
    void OnActivate(ActivateEventArgs _)
    {
        if (!m_Xr.isSelected)
            UseFromVR();
    }

    // Presses that land together (grip + trigger, or both hands) count once, so a keypad key never types twice.
    void UseFromVR()
    {
        if (Time.unscaledTime < m_NextVRUse)
            return;
        m_NextVRUse = Time.unscaledTime + 0.25f;
        Interact();
    }

    /// <summary>Lets VR hands point at / grab it, or not (a door that has been opened for good stops glowing).</summary>
    protected void SetVRUsable(bool usable)
    {
        if (m_Xr != null)
            m_Xr.enabled = usable;
    }

    public abstract void Interact();
}
