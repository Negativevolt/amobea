#!/usr/bin/env python3
"""
Generates the narrator's voice (Marsh, the old night guard on the walkie-talkie) for every line in
Assets/Game/Scripts/Narrator.cs, into Assets/Resources/Narration/<id>.wav.

Uses Kokoro (free, open source, runs offline). One-time setup, then run it whenever the lines change:

    mkdir -p ~/.cache/kokoro-tts && cd ~/.cache/kokoro-tts
    uv venv --python 3.12 venv && uv pip install --python venv/bin/python kokoro-onnx soundfile
    curl -LO https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/kokoro-v1.0.onnx
    curl -LO https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/voices-v1.0.bin

    ~/.cache/kokoro-tts/venv/bin/python Tools/Narration/generate_voices.py            # every line
    ~/.cache/kokoro-tts/venv/bin/python Tools/Narration/generate_voices.py N1_01 N3_06 # just these

Needs ffmpeg (pitch and loudness). The radio sound is added in the game (Narrator's audio filters), so the files are clean.
"""
import os
import re
import subprocess
import sys
import tempfile

import numpy as np
import soundfile as sf
from kokoro_onnx import Kokoro

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SCRIPT = os.path.join(ROOT, "Assets/Game/Scripts/Narrator.cs")
OUT = os.path.join(ROOT, "Assets/Resources/Narration")
MODELS = os.path.expanduser("~/.cache/kokoro-tts")

# Marsh: a tired, older British man who's been alone in that building for thirty years. Mostly George (mature),
# a little Lewis for weight; slow, with long gaps between sentences, pitched a touch lower.
VOICE = {"bm_george": 0.7, "bm_lewis": 0.3}
SPEED = 0.88
PITCH = 0.95          # 1 = unchanged
SENTENCE_PAUSE = 0.45  # seconds between sentences
ELLIPSIS_PAUSE = 0.9   # "..." in a line: he stops and listens

# Slower still where he's frightened or close to the player.
SLOWER = {"N1_03": 0.8, "N2_04": 0.8, "N3_04": 0.78, "N3_06": 0.82, "N2_TALL": 0.84}

# Written for subtitles, said like a person would say it.
SPOKEN = [
    (r"(?<![A-Za-z])'|'(?![A-Za-z])", ""),  # quote marks, not the ones in "don't"
    (r"\bYou'll\b", "You will"),             # Kokoro says "yowel"
    (r"\b305\b", "three oh five"),
    (r"\b302\b", "three oh two"),
]


def lines():
    src = open(SCRIPT, encoding="utf-8").read()
    body = src[src.index("Lines = new Dictionary"):]
    body = body[:body.index("};")]
    return re.findall(r'\{\s*"([A-Z0-9_]+)"\s*,\s*"((?:[^"\\]|\\.)*)"\s*\}', body)


def spoken(text):
    for pattern, repl in SPOKEN:
        text = re.sub(pattern, repl, text)
    return text


def main():
    wanted = set(sys.argv[1:])
    kokoro = Kokoro(os.path.join(MODELS, "kokoro-v1.0.onnx"), os.path.join(MODELS, "voices-v1.0.bin"))
    style = sum(kokoro.get_voice_style(name) * w for name, w in VOICE.items())
    os.makedirs(OUT, exist_ok=True)
    todo = [(i, t) for i, t in lines() if not wanted or i in wanted]
    for line_id, text in todo:
        speed = SLOWER.get(line_id, SPEED)
        rate, parts = 24000, []
        for i, chunk in enumerate(c.strip() for c in spoken(text).split("...")):
            if i > 0:
                parts.append(np.zeros(int(rate * ELLIPSIS_PAUSE), np.float32))
            if chunk:
                audio, rate = kokoro.create(chunk, voice=style, speed=speed, lang="en-gb", sentence_pause=SENTENCE_PAUSE)
                parts.append(audio)
        # a breath of silence before he speaks and after, so the radio click doesn't eat the words
        audio = np.concatenate([np.zeros(int(rate * 0.15), np.float32), *parts, np.zeros(int(rate * 0.35), np.float32)])
        with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
            sf.write(tmp.name, audio, rate)
        dest = os.path.join(OUT, line_id + ".wav")
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", tmp.name,
                        "-af", f"rubberband=pitch={PITCH},loudnorm=I=-16:TP=-1.5:LRA=11",
                        "-ar", str(rate), "-ac", "1", "-c:a", "pcm_s16le", dest], check=True)
        os.remove(tmp.name)
        print(f"{line_id}: {len(audio) / rate:.1f} s")
    print(f"{len(todo)} lines -> {os.path.relpath(OUT, ROOT)}")


if __name__ == "__main__":
    main()
