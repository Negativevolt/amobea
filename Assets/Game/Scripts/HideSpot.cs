using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>Standing inside this box hides the player from the Crawler.</summary>
[RequireComponent(typeof(BoxCollider))]
public class HideSpot : MonoBehaviour
{
    static readonly List<HideSpot> s_All = new List<HideSpot>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { s_All.Clear(); }
    BoxCollider m_Box;

    void OnEnable() { s_All.Add(this); m_Box = GetComponent<BoxCollider>(); m_Box.isTrigger = true; }
    void OnDisable() => s_All.Remove(this);

    void Update()
    {
        if (s_All.Count == 0 || s_All[0] != this)
            return; // the first spot updates the shared flag for everyone
        var head = PlayerRig.Head;
        var hiding = false;
        if (head != null)
        {
            foreach (var s in s_All)
            {
                var local = s.transform.InverseTransformPoint(head.position) - s.m_Box.center;
                var half = s.m_Box.size * 0.5f;
                if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z)
                    hiding = true;
            }
        }
        if (hiding && !PlayerRig.IsHiding)
            Narrator.SayOnce("N2_04");
        PlayerRig.IsHiding = hiding;
    }
}
