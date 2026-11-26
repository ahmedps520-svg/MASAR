"""Checks a MASAR smoke-test recording (-masarRecordAudio): the engine's pitch against the rpm in the log.

Usage: py -3 -I tools/audio_pitch_check.py <recording.wav> <player.log> <cylinders>

The smoke test logs the car (rpm, speed) every 10 s of the autopilot drive, and the recording starts
with the drive, so each log line gives a moment to check. The pitch is the first strong peak of the
autocorrelation over 0.3 s; the expected pitch is the firing frequency rpm x cylinders / 120.
"""
import re
import sys
import wave

import numpy as np

sys.stdout.reconfigure(encoding="utf-8")
wav_path, log_path, cyl = sys.argv[1], sys.argv[2], int(sys.argv[3])

with wave.open(wav_path, "rb") as w:
    sr = w.getframerate()
    x = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float64) / 32768.0
print(f"{wav_path}: {len(x) / sr:.1f} s at {sr} Hz, rms {np.sqrt(np.mean(x * x)):.4f}, peak {np.max(np.abs(x)):.3f}")

points = []
for line in open(log_path, encoding="utf-8", errors="replace"):
    m = re.search(r"car t=(\d+): .* speed ([\d.]+) km/h gear (-?\d+) rpm (\d+)", line)
    if m:
        points.append((float(m.group(1)), float(m.group(2)), int(m.group(3)), float(m.group(4))))


def pitch(seg, lo=15.0, hi=1500.0):
    seg = seg - seg.mean()
    n = len(seg)
    spec = np.fft.rfft(seg, 2 * n)
    ac = np.fft.irfft(spec * np.conj(spec))[:n]
    if ac[0] <= 0:
        return -1.0
    ac /= ac[0]
    lmin, lmax = int(sr / hi), int(sr / lo)
    for lag in range(lmin + 1, lmax):
        if ac[lag] > 0.3 and ac[lag] >= ac[lag - 1] and ac[lag] >= ac[lag + 1]:
            a, b, c = ac[lag - 1], ac[lag], ac[lag + 1]
            d = (a - c) / (2 * (a - 2 * b + c)) if (a - 2 * b + c) != 0 else 0.0
            return sr / (lag + d)
    return -1.0


ok = 0
checked = 0
for t, kmh, gear, rpm in points:
    i0 = int((t + 0.05) * sr)
    seg = x[i0:i0 + int(0.3 * sr)]
    if len(seg) < int(0.25 * sr) or rpm < 300:
        continue
    f = pitch(seg)
    exp = rpm * cyl / 120.0
    err = (f - exp) / exp * 100 if f > 0 else float("nan")
    good = f > 0 and abs(err) <= 5.0
    checked += 1
    ok += good
    print(f"t={t:5.0f}s  {kmh:5.1f} km/h gear {gear:2d}  rpm {rpm:5.0f}  expected {exp:6.1f} Hz  measured {f:6.1f} Hz  ({err:+.1f} %) {'ok' if good else 'OFF'}")
print(f"{ok} of {checked} moments within 5 %")
