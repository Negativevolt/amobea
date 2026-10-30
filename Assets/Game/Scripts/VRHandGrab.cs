using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

/// <summary>
/// VR hands. Each controller does all of its grabbing and using through ONE interactor (the XRI Near-Far Interactor
/// under the hand), so a GRIP press always takes exactly one thing, and each hand holds its own (two hands, two things).
///   • Reach: anything within <see cref="grabRadius"/> of the hand.
///   • Point: a guide line runs from the hand to anything usable up to <see cref="pointDistance"/> away. GRIP then uses
///     it, or pulls a loose prop into the hand. TRIGGER also uses things and presses keypad buttons.
/// Added automatically to the Left Hand and Right Hand controller objects whenever a scene loads.
/// </summary>
public class VRHandGrab : MonoBehaviour
{
    [Tooltip("How close (metres) the hand must be to grab something directly.")]
    public float grabRadius = 0.12f;
    [Tooltip("How far (metres) the guide line reaches: point at things up to this far away to use or grab them.")]
    public float pointDistance = 3f;
    [Tooltip("Colour of the guide line while it points at something usable.")]
    public Color lineColor = new Color(1f, 0.82f, 0.5f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // Every scene load (also a reload), add VRHandGrab to both hand controller objects of the XR rig.
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var origin = Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (origin == null)
            return;
        foreach (var t in origin.GetComponentsInChildren<Transform>(true))
            if ((t.name == "Left Hand" || t.name == "Rigth Hand" || t.name == "Right Hand") && t.GetComponent<VRHandGrab>() == null)
                t.gameObject.AddComponent<VRHandGrab>();

        // The body's fist closes ~3 cm above the controller's pointing origin: hold the torch there, not under the knuckles.
        var torch = Object.FindAnyObjectByType<PSXFlashlight>(FindObjectsInactive.Include);
        var grab = torch != null ? torch.GetComponent<XRGrabInteractable>() : null;
        if (grab != null && grab.attachTransform != null && grab.attachTransform != torch.transform)
            grab.attachTransform.localPosition += Vector3.down * k_TorchInFist;
    }

    const float k_TorchInFist = 0.03f;

    NearFarInteractor m_Interactor;

    void Start()
    {
        m_Interactor = GetComponentInChildren<NearFarInteractor>(true);
        if (m_Interactor == null)
        {
            Debug.LogWarning("[VRHandGrab] No Near-Far Interactor under " + name + "; this hand can't grab.");
            enabled = false;
            return;
        }
        SetUpInteractor(m_Interactor);
        m_Interactor.selectEntered.AddListener(OnGrabbed);

        // The poke interactors (fingertip pressing) do nothing in this game (nothing has a poke filter), and their
        // fingertip point glows: switch them off completely.
        foreach (var poke in GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.XRPokeInteractor>(true))
            poke.gameObject.SetActive(false);
    }

    void OnGrabbed(SelectEnterEventArgs args)
    {
        if (!(args.interactableObject is Component c) || c == null)
            return;
        // A prop grabbed from across the room flies into the hand; a bump on the way mustn't smash it (breaking is loud).
        if (c.TryGetComponent<Destructible>(out var d) && Vector3.Distance(c.transform.position, transform.position) > grabRadius + 0.15f)
            d.Disarm(1f);
        // The torch is a tool you carry: once taken it stays in that hand for good. GRIP no longer lets go of it,
        // and the other hand can't take it.
        if (c.GetComponent<PSXFlashlight>() != null && !m_Interactor.isPerformingManualInteraction)
        {
            m_Torch = args.interactableObject;
            s_TorchHand = m_Interactor;
            m_Interactor.StartManualInteraction(m_Torch); // held as if GRIP were never let go
            if (m_Torch is XRBaseInteractable torch)
            {
                torch.selectFilters.Remove(s_OnlyTorchHand);
                torch.selectFilters.Add(s_OnlyTorchHand);
            }
            if (!s_TorchHintShown)
            {
                s_TorchHintShown = true;
                Narrator.Hint("The torch stays in this hand now. TRIGGER or X: on/off. Use your other hand for everything else.");
            }
        }
    }

    // If the torch is ever knocked out of the hand anyway, it goes straight back.
    void Update()
    {
        if (s_TorchHand != m_Interactor || m_Interactor.hasSelection || !(m_Torch is XRBaseInteractable torch) || torch == null || !torch.isActiveAndEnabled)
            return;
        m_Interactor.interactionManager.SelectEnter(m_Interactor, m_Torch);
    }

    static bool s_TorchHintShown;
    static NearFarInteractor s_TorchHand;
    IXRSelectInteractable m_Torch;

    // only the hand that took the torch can hold it
    static readonly UnityEngine.XR.Interaction.Toolkit.Filtering.XRSelectFilterDelegate s_OnlyTorchHand =
        new UnityEngine.XR.Interaction.Toolkit.Filtering.XRSelectFilterDelegate((interactor, _) => s_TorchHand == null || ReferenceEquals(interactor, s_TorchHand));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { s_TorchHintShown = false; s_TorchHand = null; }

    void SetUpInteractor(NearFarInteractor nf)
    {
        nf.enableNearCasting = true;
        nf.enableFarCasting = true;
        nf.farAttachMode = InteractorFarAttachMode.Near; // a prop you point at and grab flies into your hand
        nf.allowHoveredActivate = true;                  // TRIGGER uses whatever the hand points at
        if (nf.nearInteractionCaster is SphereInteractionCaster near)
            near.castRadius = grabRadius;
        if (nf.farInteractionCaster is CurveInteractionCaster far) // a straight line (the prefab's single curve segment)
        {
            far.castDistance = pointDistance;
            far.hitDetectionType = CurveInteractionCaster.HitDetectionType.ConeCast; // forgiving aim, but what you point straight at wins
            far.coneCastAngle = 6f;
        }

        var curve = nf.GetComponentInChildren<CurveVisualController>(true);
        if (curve != null)
            StyleGuideLine(curve);
    }

    /// <summary>
    /// The guide line: a short faint stub shows where the hand points; on something usable it reaches all the way to it
    /// in <see cref="lineColor"/>, brighter while held.
    /// </summary>
    void StyleGuideLine(CurveVisualController curve)
    {
        curve.maxVisualCurveDistance = pointDistance;
        curve.restingVisualLineLength = 0.12f;
        curve.lineDynamicsMode = LineDynamicsMode.RetractOnHitLoss;
        curve.extendLineToEmptyHit = false; // walls and floors don't get a line
        curve.customizeLinePropertiesForState = true;
        // a bit wider than XRI's defaults: the half-resolution PS1 look eats thin lines
        Style(curve.noValidHitProperties, Color.white, 0f, 0.22f, 0.003f, 0.002f);
        Style(curve.hoverHitProperties, lineColor, 0.25f, 0.95f, 0.004f, 0.006f);
        Style(curve.selectHitProperties, Color.Lerp(lineColor, Color.white, 0.5f), 0.4f, 1f, 0.005f, 0.007f);

        var line = curve.lineRenderer;
        var shader = Resources.Load<Shader>("Shaders/GameGuideLine");
        if (line != null && shader != null)
        {
            line.sharedMaterial = new Material(shader) { name = "Guide Line" };
            line.textureMode = LineTextureMode.Stretch;
        }
    }

    static void Style(LineProperties p, Color color, float startAlpha, float endAlpha, float startWidth, float endWidth)
    {
        if (p == null)
            return;
        p.adjustGradient = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(Mathf.Max(startAlpha, endAlpha * 0.8f), 0.3f), new GradientAlphaKey(endAlpha, 1f) });
        p.gradient = g;
        p.adjustWidth = true;
        p.starWidth = startWidth;
        p.endWidth = endWidth;
    }

    void OnDestroy()
    {
        if (m_Interactor != null)
            m_Interactor.selectEntered.RemoveListener(OnGrabbed);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 0.8f, 0.35f);
        Gizmos.DrawSphere(transform.position, grabRadius);
        Gizmos.color = new Color(0f, 1f, 0.8f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, grabRadius);
        Gizmos.DrawRay(transform.position, transform.forward * pointDistance);
    }
}
