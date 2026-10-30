using UnityEngine;

/// <summary>
/// Footsteps the Crawler can hear: every 0.7 m walked makes a quiet noise (heard 3.5 m away),
/// running makes a loud one (heard 10 m away). Works in VR and in PC mode.
/// </summary>
public class PlayerNoise : MonoBehaviour
{
    public float walkRadius = 3.5f, runRadius = 10f, runSpeed = 2.6f, stepLength = 0.7f;

    Vector3 m_Last;
    float m_Travel, m_LastStep;

    void Update()
    {
        var feet = PlayerRig.Feet;
        var delta = feet - m_Last;
        delta.y = 0f;
        m_Last = feet;
        var dist = delta.magnitude;
        if (dist > 2f || Time.deltaTime <= 0f)
            return; // teleport, not a step
        m_Travel += dist;
        if (m_Travel < stepLength)
            return;
        m_Travel = 0f;
        var speed = stepLength / Mathf.Max(0.05f, Time.time - m_LastStep); // average speed over the step
        m_LastStep = Time.time;
        NoiseSystem.Emit(feet, PlayerRig.IsHiding ? 0f : PlayerRig.IsCrouching ? walkRadius * 0.4f : speed > runSpeed ? runRadius : walkRadius);
    }
}
