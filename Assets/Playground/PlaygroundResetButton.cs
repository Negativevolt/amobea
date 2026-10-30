using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>Grabbing or poking this object resets the Destruction Playground it belongs to.</summary>
[RequireComponent(typeof(XRSimpleInteractable))]
public class PlaygroundResetButton : MonoBehaviour
{
    void OnEnable()
    {
        GetComponent<XRSimpleInteractable>().selectEntered.AddListener(OnPressed);
    }

    void OnDisable()
    {
        GetComponent<XRSimpleInteractable>().selectEntered.RemoveListener(OnPressed);
    }

    void OnPressed(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs _)
    {
        var playground = GetComponentInParent<DestructionPlayground>();
        if (playground != null)
            playground.Invoke(nameof(DestructionPlayground.Rebuild), 0.05f);
    }
}
