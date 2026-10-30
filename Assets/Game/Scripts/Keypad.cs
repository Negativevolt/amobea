using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// A door keypad. Press its buttons (VR: grab/select a button, PC: look at it + E), or on PC just type the
/// numbers on the keyboard while standing at it. The right code fires onUnlocked.
/// </summary>
public class Keypad : MonoBehaviour
{
    public string code = "0317";
    public TextMesh display;
    public string wrongHint = "Wrong code. The keypad beeps at you, judgmentally.";
    public UnityEvent onUnlocked = new UnityEvent();

    static readonly Key[] k_Digits = { Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
    static readonly Key[] k_Numpad = { Key.Numpad0, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9 };

    string m_Entered = "";
    bool m_Unlocked, m_Busy;

    void Start() => Show("----", new Color(0.3f, 1f, 0.4f));

    void Show(string text, Color c)
    {
        if (display == null) return;
        display.text = text;
        display.color = c;
    }

    public void Press(char key)
    {
        if (m_Unlocked || m_Busy)
            return;
        GameAudio.At("keypad_beep", transform.position, 0.7f, 5f, 0f);
        if (key == 'C')
        {
            m_Entered = "";
            Show("----", new Color(0.3f, 1f, 0.4f));
            return;
        }
        if (key == 'E')
        {
            Check();
            return;
        }
        if (m_Entered.Length >= code.Length)
            m_Entered = "";
        m_Entered += key;
        Show(m_Entered.PadRight(code.Length, '-'), new Color(0.3f, 1f, 0.4f));
        if (m_Entered.Length == code.Length)
            Check();
    }

    void Check()
    {
        if (m_Entered == code)
        {
            m_Unlocked = true;
            Show("OPEN", new Color(0.4f, 1f, 0.5f));
            GameAudio.At("keypad_ok", transform.position, 0.9f, 8f, 0f);
            onUnlocked.Invoke();
        }
        else
            StartCoroutine(Wrong());
    }

    IEnumerator Wrong()
    {
        m_Busy = true;
        Show("ERR", new Color(1f, 0.25f, 0.2f));
        GameAudio.At("keypad_error", transform.position, 0.9f, 8f, 0f);
        Narrator.Hint(wrongHint);
        yield return new WaitForSeconds(0.9f);
        m_Entered = "";
        Show("----", new Color(0.3f, 1f, 0.4f));
        m_Busy = false;
    }

    void Update()
    {
        // PC: type the code on the keyboard while standing at the keypad and looking at it.
        if (m_Unlocked || !PlayerRig.DesktopMode || Keyboard.current == null)
            return;
        var head = PlayerRig.Head;
        if (head == null || Vector3.Distance(head.position, transform.position) > 2f
            || Vector3.Angle(head.forward, transform.position - head.position) > 40f)
            return;
        var kb = Keyboard.current;
        for (var i = 0; i <= 9; i++)
        {
            var key = kb[k_Digits[i]];
            var pad = kb[k_Numpad[i]];
            if ((key != null && key.wasPressedThisFrame) || (pad != null && pad.wasPressedThisFrame))
                Press((char)('0' + i));
        }
        if (kb.backspaceKey.wasPressedThisFrame) Press('C');
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Press('E');
    }
}
