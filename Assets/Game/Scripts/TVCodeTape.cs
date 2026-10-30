using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Room 305: the tape plays. Static roars, the four digits of the stairwell code flash up one by one,
/// then the figure on the tape comes out of the screen into your face.
/// </summary>
public class TVCodeTape : MonoBehaviour
{
    public TVStatic tv;
    public TextMesh digits;
    public string code = "0317";
    public string itemWhenDone = "code_seen";
    [Tooltip("What comes out of the screen at the end.")]
    public Monster monster = Monster.Tentacle;
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
        var pos = digits != null ? digits.transform.position : transform.position;
        GameAudio.At("static_burst", pos, 1f, 12f, 0f);
        NoiseSystem.Emit(pos, 10f);
        yield return new WaitForSeconds(1.2f);
        if (digits != null)
        {
            for (var i = 0; i < code.Length; i++)
            {
                if (tv != null) tv.On = false;
                digits.text = code[i].ToString();
                GameAudio.At("keypad_beep", pos, 0.6f, 8f, 0f);
                yield return new WaitForSeconds(0.9f);
                digits.text = "";
                if (tv != null) tv.On = true;
                yield return new WaitForSeconds(0.35f);
            }
            if (tv != null) tv.On = false;
            digits.text = code;
            yield return new WaitForSeconds(2.2f);
            digits.text = "";
            if (tv != null) tv.On = true;
        }
        GameFlow.AddItem(itemWhenDone);
        Narrator.SayOnce("N1_CODE");
        yield return new WaitForSeconds(1.0f);
        // ...and then it comes out of the screen.
        GameAudio.At("static_burst", pos, 1f, 12f, 0f);
        if (monster == Monster.Tentacle && PlayerRig.Head != null)
        {
            // tentacles burst out of the screen at you, then the thing itself
            var outDir = PlayerRig.Head.position - pos;
            ScareDirector.TentacleBurst(pos, outDir, 1.1f, Mathf.Min(1.7f, outDir.magnitude - 0.35f));
            yield return new WaitForSeconds(1.5f);
        }
        ScareDirector.InFace(monster, 1.0f);
        onFinished.Invoke();
    }
}
