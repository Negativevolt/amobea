using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Lets both controllers grab things with the GRIP button.
/// Adds an XR Direct Interactor to each tracked hand of the XR rig when a scene loads
/// (only if the hand can't grab yet: no Direct or Near-Far Interactor), bound to the XRI Default Input Actions
/// "Select" (grip) actions.
/// </summary>
public static class HandGrabSetup
{
    /// <summary>Set to false before a scene loads to turn the automatic setup off.</summary>
    public static bool enabled = true;

    const float k_GrabRadius = 0.08f;
    static readonly Vector3 k_GrabOffset = new Vector3(0f, -0.01f, 0.03f); // a little in front of the controller grip

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!enabled)
            return;

        var origin = Object.FindAnyObjectByType<XROrigin>();
        if (origin == null)
            return;

        var actionManager = Object.FindAnyObjectByType<InputActionManager>();
        if (actionManager == null || actionManager.actionAssets == null)
        {
            Debug.LogWarning("[HandGrabSetup] No Input Action Manager with action assets found; grab not set up.");
            return;
        }

        foreach (var driver in origin.GetComponentsInChildren<TrackedPoseDriver>(true))
        {
            if (origin.Camera != null && driver.gameObject == origin.Camera.gameObject)
                continue;

            var side = GetSide(driver);
            if (side == null)
                continue;

            // Already grabs (set up by hand, or the rig's Near-Far Interactor): a second interactor on the same hand
            // would grab a second object with the same press.
            if (driver.GetComponentInChildren<XRDirectInteractor>(true) != null || driver.GetComponentInChildren<NearFarInteractor>(true) != null)
                continue;

            var select = FindAction(actionManager, $"XRI {side} Interaction/Select");
            var selectValue = FindAction(actionManager, $"XRI {side} Interaction/Select Value");
            if (select == null)
            {
                Debug.LogWarning($"[HandGrabSetup] Couldn't find 'XRI {side} Interaction/Select'; {driver.name} can't grab.");
                continue;
            }

            AddDirectInteractor(driver.transform, side, select, selectValue);
        }
    }

    static string GetSide(TrackedPoseDriver driver)
    {
        var action = driver.positionInput.action;
        var mapName = action != null && action.actionMap != null ? action.actionMap.name : string.Empty;
        var text = (mapName + " " + driver.name).ToLowerInvariant();
        if (text.Contains("left"))
            return "Left";
        if (text.Contains("right") || text.Contains("rigth"))
            return "Right";
        return null;
    }

    static InputAction FindAction(InputActionManager manager, string path)
    {
        foreach (var asset in manager.actionAssets)
        {
            if (asset == null)
                continue;
            var action = asset.FindAction(path);
            if (action != null)
                return action;
        }
        return null;
    }

    static void AddDirectInteractor(Transform hand, string side, InputAction select, InputAction selectValue)
    {
        // Build inactive so Awake runs after everything is configured.
        var go = new GameObject($"{side} Grab Interactor");
        go.SetActive(false);
        go.transform.SetParent(hand, false);
        go.transform.localPosition = k_GrabOffset;

        var sphere = go.AddComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.radius = k_GrabRadius;

        var body = go.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;

        var interactor = go.AddComponent<XRDirectInteractor>();
        var reader = interactor.selectInput;
        reader.inputSourceMode = XRInputButtonReader.InputSourceMode.InputActionReference;
        reader.inputActionReferencePerformed = InputActionReference.Create(select);
        if (selectValue != null)
            reader.inputActionReferenceValue = InputActionReference.Create(selectValue);

        go.SetActive(true);
    }
}
