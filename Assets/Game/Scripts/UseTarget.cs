using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>A door, fuse box, VCR... Needs the listed items; does something when used.</summary>
public class UseTarget : Interactable
{
    public string[] requiredItems = new string[0];
    [Tooltip("Hint shown when the player doesn't have everything yet.")]
    public string lockedHint = "It won't budge.";
    public string narrationId;
    public bool onlyOnce = true;
    [Tooltip("Remove required items from inventory after use (e.g. fuse box consumes batteries).")]
    public bool consumeItems;
    [Tooltip("Sound when used. Empty = guessed from the object (wooden door, metal door, fuse box...).")]
    public string useSound;
    [Tooltip("Sound when it's locked / you're missing something. Empty = guessed.")]
    public string lockedSound;
    public UnityEvent onUse = new UnityEvent();

    bool m_Used;

    public override void Interact()
    {
        if (m_Used && onlyOnce)
            return;
        foreach (var item in requiredItems)
        {
            if (!GameFlow.HasItem(item))
            {
                Narrator.Hint(lockedHint);
                GameAudio.At(Guess(lockedSound, true), transform.position, 0.8f, 10f);
                return;
            }
        }
        m_Used = true;
        if (onlyOnce)
            SetVRUsable(false);
        if (consumeItems)
            foreach (var item in requiredItems)
                GameFlow.RemoveItem(item);
        GameAudio.At(Guess(useSound, false), transform.position, 0.9f, 15f, 0.03f);
        Narrator.SayOnce(narrationId);
        onUse.Invoke();
    }

    string Guess(string chosen, bool locked)
    {
        if (!string.IsNullOrEmpty(chosen))
            return chosen;
        var n = name.ToLowerInvariant();
        var isDoor = n.Contains("door") && !n.Contains("keypad");
        if (locked)
            return isDoor ? "door_locked" : null;
        if (n.Contains("keypad") || n.Contains("fuse"))
            return "fuse_spark";
        if (isDoor)
            return n.Contains(".001") || n.Contains("04") || n.Contains("metal") ? "door_metal_open" : "door_wood_open";
        if (n.Contains("vcr") || n.Contains("tv"))
            return "pickup_tape";
        return null;
    }
}
