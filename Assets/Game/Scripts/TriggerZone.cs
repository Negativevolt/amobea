using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// An invisible box: when the player's head enters it, says a line and fires its event (once).
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class TriggerZone : MonoBehaviour
{
    public string narrationId;
    public bool once = true;
    [Tooltip("Only fires once the player has this item (empty = always).")]
    public string requiredItem;
    public UnityEvent onEnter = new UnityEvent();

    BoxCollider m_Box;
    bool m_Inside, m_Fired;

    void Awake()
    {
        m_Box = GetComponent<BoxCollider>();
        m_Box.isTrigger = true;
    }

    void Update()
    {
        var head = PlayerRig.Head;
        if (head == null)
            return;
        var local = transform.InverseTransformPoint(head.position) - m_Box.center;
        var half = m_Box.size * 0.5f;
        var inside = Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        if (inside && !m_Inside && !(once && m_Fired) && (string.IsNullOrEmpty(requiredItem) || GameFlow.HasItem(requiredItem)))
        {
            m_Fired = true;
            Narrator.SayOnce(narrationId);
            onEnter.Invoke();
        }
        m_Inside = inside;
    }

    void OnDrawGizmos()
    {
        var b = GetComponent<BoxCollider>();
        if (b == null) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.25f);
        Gizmos.DrawCube(b.center, b.size);
    }
}
