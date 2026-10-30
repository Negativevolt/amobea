using UnityEngine;

/// <summary>A looping 3D sound on this object (rain on a window, a buzzing sign...).</summary>
public class LoopSound : MonoBehaviour
{
    public string clip = "rain_window";
    public float volume = 0.6f;
    public float range = 10f;

    void Start() => GameAudio.Loop(clip, transform, volume, range);
}
