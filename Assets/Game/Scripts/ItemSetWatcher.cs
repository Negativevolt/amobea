using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>Collects all the batteries: says a hint when the last one is found.</summary>
public class ItemSetWatcher : MonoBehaviour
{
    public string[] items;
    public string narrationWhenComplete;
    bool m_Done;

    void Update()
    {
        if (m_Done)
            return;
        foreach (var i in items)
            if (!GameFlow.HasItem(i))
                return;
        m_Done = true;
        Narrator.SayOnce(narrationWhenComplete);
    }
}
