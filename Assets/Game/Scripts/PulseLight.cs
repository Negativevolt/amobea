using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>Faint pulsing glow so small items (batteries, tape) can be spotted in the dark.</summary>
[RequireComponent(typeof(Light))]
public class PulseLight : MonoBehaviour
{
    public float speed = 2f;
    Light m_L;
    float m_Base;

    void Awake() { m_L = GetComponent<Light>(); m_Base = m_L.intensity; }
    void Update() => m_L.intensity = m_Base * (0.55f + 0.45f * Mathf.Sin(Time.time * speed));
}
