using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// PS1-style low-resolution rendering: draws the game at a fraction of the screen resolution and
/// scales it up with hard, unfiltered pixels. Turn this component off to get full resolution back.
/// Only changes settings while the game is running and restores them afterwards.
/// </summary>
public class PSXLook : MonoBehaviour
{
    [Tooltip("Fraction of full resolution to render at. 0.5 = half (chunky), 1 = off.")]
    [Range(0.25f, 1f)] public float renderScale = 0.5f;

    [Tooltip("Use hard nearest-neighbour pixels when scaling up (the PS1 look).")]
    public bool chunkyPixels = true;

    UniversalRenderPipelineAsset m_Asset;
    float m_OldScale;
    UpscalingFilterSelection m_OldFilter;

    void OnEnable()
    {
        m_Asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (m_Asset == null)
            return;
        m_OldScale = m_Asset.renderScale;
        m_OldFilter = m_Asset.upscalingFilter;
        Apply();
    }

    void OnValidate()
    {
        if (isActiveAndEnabled && m_Asset != null)
            Apply();
    }

    void Apply()
    {
        m_Asset.renderScale = renderScale;
        if (chunkyPixels)
            m_Asset.upscalingFilter = UpscalingFilterSelection.Point;
    }

    void OnDisable()
    {
        if (m_Asset == null)
            return;
        m_Asset.renderScale = m_OldScale;
        m_Asset.upscalingFilter = m_OldFilter;
    }
}
