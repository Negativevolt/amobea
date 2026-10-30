using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Fires its event a few seconds after Play() (for chaining scares).</summary>
public class DelayedEvent : MonoBehaviour
{
    public float delay = 1f;
    public UnityEvent onFire = new UnityEvent();

    public void Play() => StartCoroutine(Run());

    IEnumerator Run()
    {
        yield return new WaitForSeconds(delay);
        onFire.Invoke();
    }
}
