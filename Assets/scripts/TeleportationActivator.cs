using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class TeleportationActivator : MonoBehaviour
{
    public XRRayInteractor teleportInteractor;
    public InputActionProperty teleportActivatorAction;

    void Start()
    {
        if (teleportInteractor != null)
            teleportInteractor.gameObject.SetActive(false);
        teleportActivatorAction.action.performed += Action_performed;
    }

    void OnDestroy()
    {
        if (teleportActivatorAction.action != null)
            teleportActivatorAction.action.performed -= Action_performed;
    }

    private void Action_performed(InputAction.CallbackContext obj)
    {
        if (teleportInteractor != null)
            teleportInteractor.gameObject.SetActive(true);
    }

    void Update()
    {
        if (teleportInteractor != null && teleportActivatorAction.action.WasReleasedThisFrame())
            teleportInteractor.gameObject.SetActive(false);
    }
}
