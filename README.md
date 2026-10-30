# Amobea

A first-person horror game made in Unity. Explore the house with a torch, solve its puzzles and stay quiet: the things inside can hear you.

**Amobea is played on PC with keyboard and mouse.** VR (OpenXR, e.g. Quest Link / Air Link) is supported as an optional mode.

## PC controls

| Key | Action |
|---|---|
| W A S D | Move |
| Mouse | Look around |
| Shift | Run (loud: monsters hear you) |
| C | Crouch (slower and quieter) |
| E | Use / pick up / drop |
| Left click | Torch on/off |
| Right click | Throw what you're holding |
| Esc | Free the mouse |

The torch is on the table by the lift. Pick it up with **E**.

## Running the project

1. Install **Unity 6000.3.23f1** (Unity 6.3) with Unity Hub.
2. Open this folder as a project in Unity Hub.
3. Open the scene `Assets/Main VR.unity` and press **Play**.

## Playing in VR (optional)

Select the XR Origin in `Main VR`, find the **Desktop Player** component and change **Mode**:

- **Always PC** (default): keyboard and mouse, even if a headset is connected.
- **Auto**: VR when a headset is connected, otherwise PC.
- **Always VR**: always use the headset.

VR controls (Quest / Touch controllers): left stick walk (click to run), right stick turn, grip grab/use, trigger use or torch, A jump, B crouch, X torch, hold Y to show the controls card.
