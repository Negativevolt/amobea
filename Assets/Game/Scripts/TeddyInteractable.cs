using UnityEngine;

/// <summary>
/// Sits on the same GameObject as TeddyEntity and plugs it into the standard
/// Interactable system (DesktopPlayer crosshair + "E: Examine" prompt, VR grip select).
/// </summary>
public class TeddyInteractable : Interactable
{
    TeddyEntity m_Teddy;

    protected override void Awake()
    {
        base.Awake(); // registers XRSimpleInteractable + selectEntered hook
        m_Teddy = GetComponent<TeddyEntity>();
    }

    public override void Interact() => m_Teddy?.TriggerHeadScare();
}
