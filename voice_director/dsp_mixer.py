#!/usr/bin/env python3
"""
Real-Time 3-Track KU100 Stereo Audio Mixer (WASAPI via sounddevice) with Exact V6 DSP Chain:
  - Track 1: Pure zero-word KU100 stereo wet thrust/grind/sprint SFX (tier auto-selected from KoiSync speed)
  - Track 2: Pure zero-word KU100 stereo breathing/moaning bed (tier auto-selected from gauge + speed + climax)
             with exact V6 `-16 dB` fast-attack / smooth-release pause-bloom sidechain ducking!
  - Track 3: Real-time chunk-streamed TTS voice (`17_Serena` ZH / `04_JA` Falafel JA) with causal
             V6 `asmr_proximity_eq` (+3.2dB@165Hz, -2.2dB@5.2kHz, +3.0dB@7.8kHz, soft tanh) + binaural pan drift!
  - Live Lip-Sync RMS output (`current_lip_rms` in 0..1) computed directly from Track 3 + subtle Track 2!
"""
from __future__ import annotations

import collections
import math
import random
import threading
import time
from typing import Deque, List, Optional

import numpy as np
from scipy import signal

from .asset_pack import VerifiedAssetBank


def _make_biquad(sr: int, f0: float, gain_db: float, q: float, mode: str):
    A = 10.0 ** (gain_db / 40.0)
    w0 = 2.0 * math.pi * f0 / sr
    cosw = math.cos(w0)
    sinw = math.sin(w0)
    alpha = sinw / (2.0 * q)
    if mode == "lowshelf":
        sq = 2.0 * math.sqrt(A) * alpha
        b0 = A * ((A + 1) - (A - 1) * cosw + sq)
        b1 = 2 * A * ((A - 1) - (A + 1) * cosw)
        b2 = A * ((A + 1) - (A - 1) * cosw - sq)
        a0 = (A + 1) + (A - 1) * cosw + sq
        a1 = -2 * ((A - 1) + (A + 1) * cosw)
        a2 = (A + 1) + (A - 1) * cosw - sq
    elif mode == "highshelf":
        sq = 2.0 * math.sqrt(A) * alpha
        b0 = A * ((A + 1) + (A - 1) * cosw + sq)
        b1 = -2 * A * ((A - 1) + (A + 1) * cosw)
        b2 = A * ((A + 1) - (A - 1) * cosw - sq)
        a0 = (A + 1) - (A - 1) * cosw + sq
        a1 = 2 * ((A - 1) - (A + 1) * cosw)
        a2 = (A + 1) - (A - 1) * cosw - sq
    else:
        b0 = 1 + alpha * A
        b1 = -2 * cosw
        b2 = 1 - alpha * A
        a0 = 1 + alpha / A
        a1 = -2 * cosw
        a2 = 1 - alpha / A
    b = np.array([b0 / a0, b1 / a0, b2 / a0], dtype=np.float64)
    a = np.array([1.0, a1 / a0, a2 / a0], dtype=np.float64)
    return b, a


class StatefulVoicePolish:
    """Causal chunk-by-chunk equivalent of V6 `asmr_proximity_eq()` + smooth binaural pan/ITD drift."""

    def __init__(self, sr_in: int = 24000, sr_out: int = 44100) -> None:
        self.sr_in = sr_in
        self.sr_out = sr_out
        self.sos_hp = signal.butter(2, 70.0, btype="highpass", fs=sr_in, output="sos")
        self.b_lo, self.a_lo = _make_biquad(sr_in, 165.0, +3.2, 0.75, "lowshelf")
        self.b_de, self.a_de = _make_biquad(sr_in, 5200.0, -2.2, 2.0, "peak")
        self.b_hi, self.a_hi = _make_biquad(sr_in, 7800.0, +3.0, 0.8, "highshelf")
        self.reset()

    def reset(self, phase0: Optional[float] = None) -> None:
        self.zi_hp = signal.sosfilt_zi(self.sos_hp) * 0.0
        self.zi_lo = signal.lfilter_zi(self.b_lo, self.a_lo) * 0.0
        self.zi_de = signal.lfilter_zi(self.b_de, self.a_de) * 0.0
        self.zi_hi = signal.lfilter_zi(self.b_hi, self.a_hi) * 0.0
        self.sample_idx = 0
        self.phase0 = phase0 if phase0 is not None else random.uniform(-0.8, 0.8)
        self.prev_chunk_tail = np.zeros(32, dtype=np.float32)

    def process_chunk(self, mono_24k: np.ndarray, is_first: bool = False) -> np.ndarray:
        x = np.asarray(mono_24k, dtype=np.float32).reshape(-1)
        if len(x) == 0:
            return np.zeros((0, 2), dtype=np.float32)
        y, self.zi_hp = signal.sosfilt(self.sos_hp, x, zi=self.zi_hp)
        y, self.zi_lo = signal.lfilter(self.b_lo, self.a_lo, y, zi=self.zi_lo)
        y, self.zi_de = signal.lfilter(self.b_de, self.a_de, y, zi=self.zi_de)
        y, self.zi_hi = signal.lfilter(self.b_hi, self.a_hi, y, zi=self.zi_hi)
        # Gentle warmth soft-saturation matching V6 `17_Serena`
        y = (np.tanh(y.astype(np.float32) * 1.15) / 1.15).astype(np.float32)
        # Normalize hot peaks gently without hard-clipping
        pk = float(np.max(np.abs(y)))
        if pk > 0.85:
            y *= 0.85 / pk
        if is_first:
            f = min(int(0.012 * self.sr_in), len(y))
            if f > 1:
                y[:f] *= np.linspace(0.0, 1.0, f, dtype=np.float32)

        # Resample 24kHz -> sr_out cleanly
        if self.sr_out != self.sr_in:
            n_out = max(1, int(round(len(y) * self.sr_out / float(self.sr_in))))
            t_in = np.linspace(0.0, 1.0, len(y), endpoint=False)
            t_out = np.linspace(0.0, 1.0, n_out, endpoint=False)
            y_out = np.interp(t_out, t_in, y).astype(np.float32)
        else:
            y_out = y

        # Smooth binaural constant-power pan + subtle 0.35ms ITD ear drift
        n = len(y_out)
        t = (np.arange(n, dtype=np.float32) + self.sample_idx) / float(self.sr_out)
        self.sample_idx += n
        pan = 0.50 * np.sin(2.0 * math.pi * 0.11 * t + self.phase0)
        angle = (pan + 1.0) * (math.pi * 0.25)
        gL = np.cos(angle)
        gR = np.sin(angle)

        # Concatenate previous 32 samples for click-free ITD interpolation across chunk boundaries
        padded = np.concatenate([self.prev_chunk_tail, y_out])
        pad_len = len(self.prev_chunk_tail)
        max_delay = min(pad_len - 1, max(1, int(self.sr_out * 0.00035)))
        idx = np.arange(n, dtype=np.float32) + pad_len
        idxL = idx - np.maximum(0.0, pan) * max_delay
        idxR = idx - np.maximum(0.0, -pan) * max_delay
        L = np.interp(idxL, np.arange(len(padded), dtype=np.float32), padded).astype(np.float32) * gL
        R = np.interp(idxR, np.arange(len(padded), dtype=np.float32), padded).astype(np.float32) * gR
        if len(y_out) >= pad_len:
            self.prev_chunk_tail = y_out[-pad_len:].copy()
        else:
            self.prev_chunk_tail = padded[-pad_len:].copy()
        return np.stack([L, R], axis=1)


class TierLoopPlayer:
    """Click-free stereo loop player with smooth equal-power crossfade across tiers & clip rotation."""

    def __init__(self, clips_by_tier: dict, sr: int, rotate_sec: float = 11.0) -> None:
        self.clips = clips_by_tier
        self.sr = sr
        self.rotate_samples = int(rotate_sec * sr)
        self.cur_tier: Optional[str] = None
        self.cur_clip: Optional[np.ndarray] = None
        self.cur_pos = 0
        self.samples_on_clip = 0
        self.prev_clip: Optional[np.ndarray] = None
        self.prev_pos = 0
        self.xfade_rem = 0
        self.xfade_total = int(0.32 * sr)
        self.gain_smooth = 0.0

    def _pick_clip(self, tier: str) -> Optional[np.ndarray]:
        arr = self.clips.get(tier) or []
        if not arr:
            return None
        if len(arr) == 1:
            return arr[0]
        choice = random.choice(arr)
        if choice is self.cur_clip:
            choice = arr[(arr.index(choice) + 1) % len(arr)]
        return choice

    def _start_clip(self, tier: str) -> None:
        nxt = self._pick_clip(tier)
        if nxt is None:
            return
        self.prev_clip = self.cur_clip
        self.prev_pos = self.cur_pos
        self.cur_clip = nxt
        self.cur_pos = 0
        self.cur_tier = tier
        self.samples_on_clip = 0
        self.xfade_rem = self.xfade_total if self.prev_clip is not None else 0

    def _read_wrapped(self, clip: np.ndarray, pos: int, n: int) -> tuple[np.ndarray, int]:
        clen = len(clip)
        if clen == 0:
            return np.zeros((n, 2), dtype=np.float32), 0
        out = np.empty((n, 2), dtype=np.float32)
        filled = 0
        p = pos % clen
        while filled < n:
            take = min(n - filled, clen - p)
            out[filled:filled + take] = clip[p:p + take]
            filled += take
            p = (p + take) % clen
        return out, p

    def render(self, n: int, want_tier: Optional[str], target_gain: float) -> np.ndarray:
        if want_tier and want_tier != self.cur_tier:
            self._start_clip(want_tier)
        elif want_tier and self.cur_clip is not None and self.samples_on_clip >= self.rotate_samples and len(self.clips.get(want_tier, [])) > 1:
            self._start_clip(want_tier)

        # Smooth block-level gain slew (prevents zipper noise when entering/leaving H-scene or changing speed)
        g0 = self.gain_smooth
        eff_target = target_gain if want_tier and self.cur_clip is not None else 0.0
        alpha = 1.0 - math.exp(-n / max(1.0, 0.14 * self.sr))
        g1 = g0 + (eff_target - g0) * alpha
        self.gain_smooth = g1
        if g0 < 1e-4 and g1 < 1e-4:
            return np.zeros((n, 2), dtype=np.float32)

        cur_buf, self.cur_pos = self._read_wrapped(self.cur_clip, self.cur_pos, n)
        self.samples_on_clip += n

        if self.xfade_rem > 0 and self.prev_clip is not None:
            prev_buf, self.prev_pos = self._read_wrapped(self.prev_clip, self.prev_pos, n)
            k = min(n, self.xfade_rem)
            done = self.xfade_total - self.xfade_rem
            t = (np.arange(k, dtype=np.float32) + done) / float(max(1, self.xfade_total))
            theta = np.clip(t, 0.0, 1.0) * (0.5 * math.pi)
            w_new = np.sin(theta)[:, None]
            w_old = np.cos(theta)[:, None]
            cur_buf[:k] = cur_buf[:k] * w_new + prev_buf[:k] * w_old
            self.xfade_rem -= k
            if self.xfade_rem <= 0:
                self.prev_clip = None

        ramp = np.linspace(g0, g1, n, dtype=np.float32)[:, None]
        return cur_buf * ramp


class RealtimeBinauralMixer:
    """Thread-safe 3-track stereo mixer feeding sounddevice WASAPI output + live lip-sync RMS."""

    def __init__(self, assets: VerifiedAssetBank, sr: int) -> None:
        self.sr = sr
        self.assets = assets
        self.t1_player = TierLoopPlayer(assets.track1, sr, rotate_sec=9.0)
        self.t2_player = TierLoopPlayer(assets.track2, sr, rotate_sec=11.0)
        self.voice_polish = StatefulVoicePolish(24000, sr)

        self._lock = threading.Lock()
        self._voice_queue: Deque[np.ndarray] = collections.deque()
        self._voice_offset = 0

        # Live parameters updated by 60Hz telemetry & /config
        self.enabled = True
        self.master_gain = 0.92
        self.sfx_gain = 0.17
        self.breath_gain = 0.20
        self.voice_gain = 1.05

        self.want_t1_tier: Optional[str] = None
        self.want_t1_scale: float = 0.0
        self.want_t2_tier: Optional[str] = None
        self.want_t2_scale: float = 0.0

        # Sidechain envelope state (exact V6: 30ms attack, 190ms release, -16 dB duck during voice!)
        self._v_env = 0.0
        self.current_lip_rms = 0.0
        self.is_speaking = False
        self._stream = None
        self._running = False

    def start_audio(self) -> None:
        import sounddevice as sd
        if self._running:
            return
        self._stream = sd.OutputStream(
            samplerate=self.sr,
            channels=2,
            dtype="float32",
            blocksize=1024,
            latency="low",
            callback=self._callback,
        )
        self._stream.start()
        self._running = True

    def stop_audio(self) -> None:
        self._running = False
        if self._stream is not None:
            try:
                self._stream.stop()
                self._stream.close()
            except Exception:
                pass
            self._stream = None

    def set_scene_beds(self, t1_tier: Optional[str], t1_scale: float, t2_tier: Optional[str], t2_scale: float) -> None:
        with self._lock:
            self.want_t1_tier = t1_tier
            self.want_t1_scale = max(0.0, min(1.5, t1_scale))
            self.want_t2_tier = t2_tier
            self.want_t2_scale = max(0.0, min(1.5, t2_scale))

    def begin_voice_stream(self, interrupt: bool = True) -> None:
        with self._lock:
            if interrupt:
                self._voice_queue.clear()
                self._voice_offset = 0
            self.voice_polish.reset()

    def push_voice_chunk_24k(self, mono_24k: np.ndarray, is_first: bool = False) -> None:
        stereo = self.voice_polish.process_chunk(mono_24k, is_first=is_first)
        if len(stereo) == 0:
            return
        with self._lock:
            self._voice_queue.append(stereo)

    def clear_voice(self) -> None:
        with self._lock:
            self._voice_queue.clear()
            self._voice_offset = 0

    def _pull_voice(self, n: int) -> np.ndarray:
        out = np.zeros((n, 2), dtype=np.float32)
        filled = 0
        while filled < n and self._voice_queue:
            head = self._voice_queue[0]
            avail = len(head) - self._voice_offset
            take = min(n - filled, avail)
            out[filled:filled + take] = head[self._voice_offset:self._voice_offset + take]
            filled += take
            self._voice_offset += take
            if self._voice_offset >= len(head):
                self._voice_queue.popleft()
                self._voice_offset = 0
        return out

    def render_block(self, frames: int) -> np.ndarray:
        with self._lock:
            en = self.enabled
            mg = self.master_gain
            sg = self.sfx_gain * self.want_t1_scale
            bg = self.breath_gain * self.want_t2_scale
            vg = self.voice_gain
            t1_tier = self.want_t1_tier if en else None
            t2_tier = self.want_t2_tier if en else None
            v_buf = self._pull_voice(frames) * (vg if en else 0.0)
            speaking_now = bool(self._voice_queue) or float(np.max(np.abs(v_buf))) > 0.004

        # Fast-attack (30ms) / smooth-release (290ms) sidechain envelope on Track 3 voice
        # Prevents Track 2 from pumping abruptly during brief intra-sentence breath gaps!
        block_rms = float(np.sqrt(np.mean(v_buf * v_buf) + 1e-12))
        tau = 0.030 if block_rms > self._v_env else 0.290
        a = 1.0 - math.exp(-frames / max(1.0, tau * self.sr))
        self._v_env = self._v_env + (block_rms - self._v_env) * a
        gate = max(0.0, min(1.0, self._v_env / 0.032))

        # Exact V6 delicate ducking: -16 dB (0.16x) on Track 2 while speaking, blooms to 1.0x in pauses!
        duck_t2 = 1.0 - 0.84 * gate
        duck_t1 = 1.0 - 0.25 * gate

        t1_buf = self.t1_player.render(frames, t1_tier, sg * duck_t1)
        t2_buf = self.t2_player.render(frames, t2_tier, bg * duck_t2)

        mix = (v_buf + t2_buf + t1_buf) * mg
        # Transparent safety soft-clipper so master never digitally clips
        np.tanh(mix, out=mix)

        # Compute lip-sync RMS (dominated by voice when speaking, gentle breathing lip-motion otherwise!)
        t2_rms = float(np.sqrt(np.mean(t2_buf * t2_buf) + 1e-12))
        lip = min(1.0, block_rms * 5.2 + t2_rms * 2.2)
        self.current_lip_rms = lip
        self.is_speaking = speaking_now
        return mix

    def _callback(self, outdata, frames, time_info, status) -> None:
        outdata[:] = self.render_block(frames)
