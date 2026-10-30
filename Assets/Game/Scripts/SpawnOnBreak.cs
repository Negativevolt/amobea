using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>When this breakable object shatters, something drops out of it (e.g. the key inside a vase).</summary>
[RequireComponent(typeof(Destructible))]
public class SpawnOnBreak : MonoBehaviour
{
    public GameObject prefab;
    public string itemId = "room_key";
    public string prompt = "Pick up key";
    public string narrationId = "N1_KEY";

    void Awake() => GetComponent<Destructible>().onBreak.AddListener(Spawn);

    void Spawn()
    {
        if (prefab == null)
            return;
        var go = Instantiate(prefab, transform.position + Vector3.up * 0.05f, Quaternion.Euler(0f, Random.value * 360f, 0f));
        go.name = itemId;
        foreach (var mc in go.GetComponentsInChildren<MeshCollider>())
            Destroy(mc);
        var col = go.AddComponent<SphereCollider>();
        col.radius = 0.12f;
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.1f;
        var pickup = go.AddComponent<Pickup>();
        pickup.itemId = itemId;
        pickup.prompt = prompt;
        pickup.narrationId = narrationId;
        var glow = new GameObject("Glint").AddComponent<Light>();
        glow.transform.SetParent(go.transform, false);
        glow.type = LightType.Point;
        glow.range = 0.7f;
        glow.intensity = 0.6f;
        glow.color = new Color(1f, 0.85f, 0.4f);
    }
}
