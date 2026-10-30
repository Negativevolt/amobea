using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>An item to collect (key, battery, tape). Disappears into the player's pockets.</summary>
public class Pickup : Interactable
{
    public string itemId = "item";
    [Tooltip("Narration line (from Narrator.Lines) to say when picked up.")]
    public string narrationId;
    public UnityEvent onPicked = new UnityEvent();

    public override void Interact()
    {
        GameFlow.AddItem(itemId);
        var sound = itemId.StartsWith("battery") ? "pickup_battery" : itemId == "vhs" ? "pickup_tape" : "pickup_key";
        GameAudio.At(sound, transform.position, 0.8f, 6f);
        Narrator.SayOnce(narrationId);
        onPicked.Invoke();
        gameObject.SetActive(false);
    }
}
