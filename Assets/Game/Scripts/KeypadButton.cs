using System.Collections;
using UnityEngine;

/// <summary>One button on a <see cref="Keypad"/> ('0'-'9', 'C' = clear, 'E' = enter). Sinks in when pressed.</summary>
public class KeypadButton : Interactable
{
    public Keypad keypad;
    public char key = '0';

    Vector3 m_Rest;

    void Start() => m_Rest = transform.localPosition;

    public override void Interact()
    {
        if (keypad == null)
            keypad = GetComponentInParent<Keypad>();
        if (keypad != null)
            keypad.Press(key);
        StopAllCoroutines();
        StartCoroutine(Sink());
    }

    IEnumerator Sink()
    {
        transform.localPosition = m_Rest + Vector3.forward * 0.006f; // the keypad's forward points into the wall
        yield return new WaitForSeconds(0.12f);
        transform.localPosition = m_Rest;
    }
}
