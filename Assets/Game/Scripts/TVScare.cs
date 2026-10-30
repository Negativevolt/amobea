using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Scare: the TV bursts into loud flashing static, then cuts to black. Fires onFinished afterwards.</summary>
public class TVScare : MonoBehaviour
{
    public Renderer screen;
    public float seconds = 3f;
    public string itemWhenDone = "tape_watched";
    public UnityEvent onFinished = new UnityEvent();

    bool m_Played;

    public void Play()
    {
        if (!m_Played)
            StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        m_Played = true;
        Material mat = null;
        if (screen != null)
        {
            mat = screen.material;
            mat.EnableKeyword("_EMISSION");
        }
        var glow = new GameObject("TV Glow").AddComponent<Light>();
        glow.transform.position = screen != null ? screen.bounds.center : transform.position;
        glow.type = LightType.Point;
        glow.range = 4f;
        glow.color = new Color(0.75f, 0.85f, 0.9f);
        NoiseSystem.Emit(glow.transform.position, 15f);
        var tvSound = GameAudio.At("tv_static", glow.transform.position, 1f, 15f, 0f);

        for (var t = 0f; t < seconds; t += Time.deltaTime)
        {
            var v = Random.value;
            glow.intensity = 1.5f + v * 3f;
            if (mat != null)
                mat.SetColor("_EmissionColor", Color.white * (0.5f + v * 2.5f));
            yield return null;
        }
        if (mat != null)
            mat.SetColor("_EmissionColor", Color.black);
        if (tvSound != null) tvSound.Stop(); // cuts to black, dead silence
        Destroy(glow.gameObject);
        GameFlow.AddItem(itemWhenDone);
        onFinished.Invoke();
    }
}
