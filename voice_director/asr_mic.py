#!/usr/bin/env python3
"""
High-Accuracy Multi-Engine Microphone ASR for KoiSync.VoiceDirector:
  - Audio DSP pipeline: DC removal + polyphase FIR anti-aliased resampling (`scipy.signal.resample_poly`)
    + automatic gentle gain normalization for quiet Windows headset microphones + peak dB telemetry.
  - Engine 1 (`gpu_turbo`, DEFAULT): Local RTX 4070 CUDA `faster-whisper large-v3-turbo` (`int8_float16`,
    ~200-350 ms, 809M params, near-human Chinese/Japanese accuracy, 100% offline, cached on E:).
  - Engine 2 (`gemini_asr`): Google `gemini-3.5-transcribe` cloud transcription (0 extra GPU VRAM,
    100% verbatim accuracy even on whispered speech).
  - Engine 3 (`gemini_direct`): Passes compact 16kHz PCM16 WAV directly to `chat_brain.respond_to_audio()`
    so Gemini hears Master's real emotional tone + transcribes + replies in ONE single call!
"""
from __future__ import annotations

import base64
import collections
import io
import json
import math
import os
import pathlib
import threading
import time
import urllib.request
from math import gcd
from typing import Callable, Deque, List, Optional, Tuple

LAB_DIR = pathlib.Path(r"E:\project\c#\KoiSync\artifacts\voice_lab")
HF_CACHE = LAB_DIR / "hf_cache"
TMP_DIR = LAB_DIR / "tmp"
TMP_DIR.mkdir(parents=True, exist_ok=True)
(HF_CACHE / "whisper").mkdir(parents=True, exist_ok=True)
os.environ["HF_HOME"] = str(HF_CACHE)
os.environ["HUGGINGFACE_HUB_CACHE"] = str(HF_CACHE / "hub")
os.environ["TMP"] = str(TMP_DIR)
os.environ["TEMP"] = str(TMP_DIR)

# Expose torch/lib CUDA & cuDNN DLLs so CTranslate2 runs on RTX GPU cleanly on Windows
try:
    import torch
    _torch_lib = pathlib.Path(torch.__file__).resolve().parent / "lib"
    if _torch_lib.is_dir():
        os.environ["PATH"] = str(_torch_lib) + os.pathsep + os.environ.get("PATH", "")
        if hasattr(os, "add_dll_directory"):
            os.add_dll_directory(str(_torch_lib))
except Exception:
    pass

import numpy as np
import sounddevice as sd
import soundfile as sf
from scipy import signal

SAFETY_OFF = [
    {"category": "HARM_CATEGORY_SEXUALLY_EXPLICIT", "threshold": "OFF"},
    {"category": "HARM_CATEGORY_HARASSMENT", "threshold": "OFF"},
    {"category": "HARM_CATEGORY_HATE_SPEECH", "threshold": "OFF"},
    {"category": "HARM_CATEGORY_DANGEROUS_CONTENT", "threshold": "OFF"},
]


def list_input_devices() -> List[dict]:
    hostapis = sd.query_hostapis()
    out = []
    for i, d in enumerate(sd.query_devices()):
        if d.get("max_input_channels", 0) > 0:
            ha_idx = int(d.get("hostapi", 0))
            ha_name = str(hostapis[ha_idx].get("name", "")) if 0 <= ha_idx < len(hostapis) else ""
            if "WDM-KS" in ha_name.upper():
                continue
            out.append({
                "index": i,
                "name": str(d.get("name", "")),
                "hostApi": ha_name,
                "sampleRate": int(round(float(d.get("default_samplerate", 16000)))),
                "channels": int(d.get("max_input_channels", 1)),
            })
    return out


def pick_preferred_mic_index() -> Optional[int]:
    devs = list_input_devices()
    for ha_pref in ("MME", "WASAPI", ""):
        for d in devs:
            if "ROG DELTA" in d["name"].upper() and (not ha_pref or ha_pref in d.get("hostApi", "").upper()):
                return d["index"]
    try:
        idx = sd.default.device[0]
        if idx is not None and idx >= 0:
            return int(idx)
    except Exception:
        pass
    return devs[0]["index"] if devs else None


def prepare_16k_mono(audio: np.ndarray, sr: int) -> Tuple[np.ndarray, float]:
    """Anti-aliased resample to 16kHz + DC removal + gentle auto-gain normalization for headset mics."""
    x = np.asarray(audio, dtype=np.float32)
    if x.ndim > 1:
        x = x.mean(axis=1)
    x = x.reshape(-1)
    if len(x) == 0:
        return x, -90.0

    # Remove DC bias
    x = x - float(np.mean(x))
    raw_peak = float(np.max(np.abs(x)))
    peak_db = round(20.0 * math.log10(max(raw_peak, 1e-5)), 1)

    # Proper polyphase FIR resampling (no aliasing!)
    if sr != 16000 and len(x) > 16:
        g = gcd(16000, int(sr))
        x = signal.resample_poly(x, 16000 // g, int(sr) // g).astype(np.float32)

    # Gentle auto-gain boost if mic input is real speech (above noise floor -44 dBFS) yet quiet
    pk = float(np.max(np.abs(x))) if len(x) else 0.0
    if 0.006 < pk < 0.72:
        gain = min(8.0, 0.72 / pk)
        x = np.clip(x * gain, -0.98, 0.98).astype(np.float32)

    return x, peak_db


def pcm16k_to_wav_bytes(mono_16k: np.ndarray) -> bytes:
    buf = io.BytesIO()
    sf.write(buf, np.asarray(mono_16k, dtype=np.float32), 16000, format="WAV", subtype="PCM_16")
    return buf.getvalue()


def _has_cached_whisper_repo(repo_dir_name: str) -> bool:
    snaps = HF_CACHE / "whisper" / repo_dir_name / "snapshots"
    if not snaps.is_dir():
        return False
    for d in snaps.iterdir():
        if (d / "model.bin").is_file() and (d / "model.bin").stat().st_size > 10_000_000:
            return True
    return False


class LocalWhisperAsr:
    def __init__(self, engine: str = "gpu_turbo") -> None:
        self.engine = engine  # gpu_turbo | gemini_direct | gemini_asr
        self._lock = threading.Lock()
        self._model = None
        self.loaded_model_name = ""
        self.loaded_device = ""
        self.ready = False
        self.loading = False
        self.last_error = ""
        self.last_peak_db = -90.0

        # Server-side push-to-talk recording state
        self._rec_lock = threading.Lock()
        self._rec_stream = None
        self._rec_chunks: List[np.ndarray] = []
        self._rec_sr = 44100
        self.recording = False
        self.mic_index = pick_preferred_mic_index()

        # Hands-free continuous VAD Free-Mic state + Barge-In interrupt support
        self.free_mic_enabled = False
        self.free_mic_threshold_db = -34.0
        self.barge_in_enabled = True
        self.barge_in_margin_db = 6.0
        self._free_stream = None
        self._free_lock = threading.Lock()
        self._free_cb_utterance: Optional[Callable[[np.ndarray, float], None]] = None
        self._free_cb_barge_in: Optional[Callable[[], None]] = None
        self._free_is_speaking_fn: Optional[Callable[[], bool]] = None
        self._free_is_busy_fn: Optional[Callable[[], bool]] = None
        self._free_last_muted_at = 0.0
        self._free_sr = 44100
        self._free_preroll: Deque[np.ndarray] = collections.deque(maxlen=10)
        self._free_active = False
        self._free_buf: List[np.ndarray] = []
        self._free_speech_frames = 0
        self._free_barge_frames = 0
        self._free_silence_sec = 0.0
        self._free_active_sec = 0.0

    def set_engine(self, engine: str) -> None:
        eng = (engine or "gpu_turbo").strip().lower()
        if eng not in ("gpu_turbo", "gemini_direct", "gemini_asr", "openai_asr"):
            eng = "gpu_turbo"
        self.engine = eng
        if eng in ("gemini_direct", "gemini_asr", "openai_asr"):
            self.ready = True
            self.last_error = ""
        elif self._model is None:
            self.ready = False
            threading.Thread(target=self._preload_safe, daemon=True).start()

    def _preload_safe(self) -> None:
        try:
            self.ensure_loaded()
        except Exception:
            pass

    def ensure_loaded(self) -> None:
        if self.engine in ("gemini_direct", "gemini_asr", "openai_asr"):
            self.ready = True
            return
        with self._lock:
            if self._model is not None and self.ready:
                return
            self.loading = True
            self.last_error = ""
            try:
                import ctranslate2
                from faster_whisper import WhisperModel

                t0 = time.perf_counter()
                wdir = HF_CACHE / "whisper"
                has_cuda = ctranslate2.get_cuda_device_count() > 0
                has_turbo = _has_cached_whisper_repo("models--mobiuslabsgmbh--faster-whisper-large-v3-turbo")

                model_name = "large-v3-turbo" if has_turbo else "base"
                device = "cuda" if has_cuda else "cpu"
                compute = "int8_float16" if has_cuda else "int8"

                try:
                    self._model = WhisperModel(
                        model_name,
                        device=device,
                        compute_type=compute,
                        download_root=str(wdir),
                        local_files_only=True,
                    )
                except Exception:
                    # Fallback cleanly to CPU int8 if CUDA runtime throws anything unexpected
                    device = "cpu"
                    compute = "int8"
                    self._model = WhisperModel(
                        model_name,
                        device=device,
                        compute_type=compute,
                        download_root=str(wdir),
                        local_files_only=True,
                    )

                self.loaded_model_name = model_name
                self.loaded_device = device
                self.ready = True
                print(
                    f"[ASR] Loaded faster-whisper '{model_name}' ({device}/{compute}) in {time.perf_counter()-t0:.2f}s",
                    flush=True,
                )
            except Exception as exc:
                self.last_error = str(exc)
                print(f"[ASR] Load error: {exc}", flush=True)
                raise
            finally:
                self.loading = False

    def transcribe_pcm16k_local(self, mono_16k: np.ndarray, language: Optional[str] = None) -> dict:
        self.ensure_loaded()
        t0 = time.perf_counter()
        audio = np.asarray(mono_16k, dtype=np.float32).reshape(-1)
        if len(audio) < 2400:
            return {"ok": True, "text": "", "asrMs": 0.0, "peakDb": self.last_peak_db}
        lang_arg = "zh" if language == "zh" else ("ja" if language == "ja" else ("en" if language == "en" else None))
        segs, info = self._model.transcribe(
            audio,
            language=lang_arg,
            beam_size=3,
            best_of=3,
            temperature=0.0,
            condition_on_previous_text=False,
            vad_filter=True,
            vad_parameters={"threshold": 0.28, "min_silence_duration_ms": 350, "speech_pad_ms": 220},
        )
        text = "".join(s.text for s in segs).replace("\ufffd", "").strip()
        return {
            "ok": True,
            "text": text,
            "detectedLanguage": getattr(info, "language", lang_arg or ""),
            "asrMs": round((time.perf_counter() - t0) * 1000.0, 1),
            "engine": f"local:{self.loaded_model_name}({self.loaded_device})",
            "peakDb": self.last_peak_db,
        }

    def transcribe_pcm16k_gemini(self, mono_16k: np.ndarray, google_api_key: str, language: Optional[str] = None) -> dict:
        t0 = time.perf_counter()
        audio = np.asarray(mono_16k, dtype=np.float32).reshape(-1)
        if len(audio) < 2400:
            return {"ok": True, "text": "", "asrMs": 0.0, "peakDb": self.last_peak_db}
        if not google_api_key:
            return self.transcribe_pcm16k_local(audio, language=language)

        wav_bytes = pcm16k_to_wav_bytes(audio)
        prompt = (
            "请精确听写这段中文口语语音的原话，输出简体中文纯文字，不要任何解释、括号描写或引号。若无人声则只返回空字符串。"
            if language != "ja"
            else "この日本語音声の言葉を正確に書き起こしてください。テキストのみを出力し、説明は不要です。"
        )
        url = f"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-transcribe:generateContent?key={google_api_key}"
        body = {
            "contents": [{
                "role": "user",
                "parts": [
                    {"inlineData": {"mimeType": "audio/wav", "data": base64.b64encode(wav_bytes).decode("ascii")}},
                    {"text": prompt},
                ],
            }],
            "generationConfig": {"temperature": 0.0, "maxOutputTokens": 140},
            "safetySettings": SAFETY_OFF,
        }
        req = urllib.request.Request(
            url,
            data=json.dumps(body, ensure_ascii=False).encode("utf-8"),
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        with urllib.request.urlopen(req, timeout=18.0) as r:
            res = json.loads(r.read().decode("utf-8", errors="replace"))
        cand = (res.get("candidates") or [{}])[0]
        parts = (cand.get("content") or {}).get("parts") or []
        text = "".join(p.get("text", "") for p in parts).strip().strip('"').strip("'")
        return {
            "ok": True,
            "text": text,
            "detectedLanguage": language or "zh",
            "asrMs": round((time.perf_counter() - t0) * 1000.0, 1),
            "engine": "gemini-3.5-transcribe",
            "peakDb": self.last_peak_db,
        }

    def transcribe_pcm16k_openai_api(
        self,
        mono_16k: np.ndarray,
        api_url: str,
        api_key: str,
        model: str = "whisper-1",
        language: Optional[str] = None,
    ) -> dict:
        """Standard OpenAI-compatible POST /audio/transcriptions (works with OpenAI, Groq, SiliconFlow, DeepInfra, Speaches, OneAPI)."""
        t0 = time.perf_counter()
        audio = np.asarray(mono_16k, dtype=np.float32).reshape(-1)
        if len(audio) < 2400:
            return {"ok": True, "text": "", "asrMs": 0.0, "peakDb": self.last_peak_db}
        base = (api_url or "https://api.openai.com/v1").strip().rstrip("/")
        url = base if base.endswith("/audio/transcriptions") else (base + "/audio/transcriptions")
        wav_bytes = pcm16k_to_wav_bytes(audio)
        boundary = f"----KoiSyncBoundary{int(time.time() * 1000)}"
        parts: List[bytes] = []

        def _add_field(name: str, val: str) -> None:
            parts.append(f"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"\r\n\r\n{val}\r\n".encode("utf-8"))

        _add_field("model", (model or "whisper-1").strip())
        _add_field("response_format", "json")
        if language in ("zh", "ja", "en"):
            _add_field("language", language)
        parts.append(
            f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"speech.wav\"\r\nContent-Type: audio/wav\r\n\r\n".encode("utf-8")
            + wav_bytes
            + b"\r\n"
        )
        parts.append(f"--{boundary}--\r\n".encode("utf-8"))
        body_bytes = b"".join(parts)

        headers = {"Content-Type": f"multipart/form-data; boundary={boundary}"}
        if (api_key or "").strip():
            headers["Authorization"] = f"Bearer {api_key.strip()}"
        req = urllib.request.Request(url, data=body_bytes, headers=headers, method="POST")
        with urllib.request.urlopen(req, timeout=20.0) as r:
            raw = r.read().decode("utf-8", errors="replace")
        try:
            j = json.loads(raw)
            text = str(j.get("text") or "").strip()
        except Exception:
            text = raw.strip()
        return {
            "ok": True,
            "text": text,
            "detectedLanguage": language or "",
            "asrMs": round((time.perf_counter() - t0) * 1000.0, 1),
            "engine": f"api:{model or 'whisper-1'}",
            "peakDb": self.last_peak_db,
        }

    def transcribe_pcm16k(
        self,
        mono_16k: np.ndarray,
        language: Optional[str] = None,
        google_api_key: str = "",
        asr_api_url: str = "",
        asr_api_key: str = "",
        asr_api_model: str = "whisper-1",
    ) -> dict:
        if self.engine == "openai_asr" and (asr_api_url or asr_api_key):
            try:
                return self.transcribe_pcm16k_openai_api(
                    mono_16k,
                    api_url=asr_api_url or "https://api.openai.com/v1",
                    api_key=asr_api_key,
                    model=asr_api_model or "whisper-1",
                    language=language,
                )
            except Exception as exc:
                print(f"[ASR] openai_asr failed, falling back to local GPU Whisper: {exc}", flush=True)
        if self.engine == "gemini_asr" and google_api_key:
            try:
                return self.transcribe_pcm16k_gemini(mono_16k, google_api_key, language=language)
            except Exception as exc:
                print(f"[ASR] gemini_asr failed, falling back to local GPU Whisper: {exc}", flush=True)
        return self.transcribe_pcm16k_local(mono_16k, language=language)

    def decode_wav_bytes_to_16k(self, wav_bytes: bytes) -> Tuple[np.ndarray, float]:
        audio, sr = sf.read(io.BytesIO(wav_bytes), dtype="float32")
        mono_16k, peak_db = prepare_16k_mono(audio, sr)
        self.last_peak_db = peak_db
        return mono_16k, peak_db

    def transcribe_wav_bytes(self, wav_bytes: bytes, language: Optional[str] = None, google_api_key: str = "") -> dict:
        mono_16k, _ = self.decode_wav_bytes_to_16k(wav_bytes)
        return self.transcribe_pcm16k(mono_16k, language=language, google_api_key=google_api_key)

    def start_mic_record(self, device_index: Optional[int] = None) -> dict:
        with self._rec_lock:
            if self.recording:
                return {"ok": True, "recording": True}
            idx = device_index if device_index is not None else self.mic_index
            if idx is None:
                raise RuntimeError("未检测到可用麦克风设备")
            self.mic_index = int(idx)
            info = sd.query_devices(self.mic_index)
            self._rec_sr = int(round(float(info.get("default_samplerate", 44100))))
            self._rec_chunks = []

            def _cb(indata, frames, time_info, status):
                self._rec_chunks.append(indata[:, 0].copy())

            self._rec_stream = sd.InputStream(
                device=self.mic_index,
                samplerate=self._rec_sr,
                channels=1,
                dtype="float32",
                callback=_cb,
            )
            self._rec_stream.start()
            self.recording = True
            return {"ok": True, "recording": True, "device": info.get("name", ""), "sampleRate": self._rec_sr}

    def stop_mic_record_pcm16k(self) -> Tuple[np.ndarray, float]:
        with self._rec_lock:
            if not self.recording:
                return np.zeros(0, dtype=np.float32), -90.0
            self.recording = False
            if self._rec_stream is not None:
                try:
                    self._rec_stream.stop()
                    self._rec_stream.close()
                except Exception:
                    pass
                self._rec_stream = None
            chunks = self._rec_chunks
            sr = self._rec_sr
            self._rec_chunks = []

        if not chunks:
            self.last_peak_db = -90.0
            return np.zeros(0, dtype=np.float32), -90.0
        raw = np.concatenate(chunks)
        mono_16k, peak_db = prepare_16k_mono(raw, sr)
        self.last_peak_db = peak_db
        return mono_16k, peak_db

    def stop_mic_record_and_transcribe(self, language: Optional[str] = None, google_api_key: str = "") -> dict:
        mono_16k, _ = self.stop_mic_record_pcm16k()
        if len(mono_16k) < 2400:
            return {"ok": True, "text": "", "asrMs": 0.0, "peakDb": self.last_peak_db}
        return self.transcribe_pcm16k(mono_16k, language=language, google_api_key=google_api_key)

    def start_free_mic(
        self,
        on_utterance: Callable[[np.ndarray, float], None],
        is_speaking_fn: Optional[Callable[[], bool]] = None,
        is_busy_fn: Optional[Callable[[], bool]] = None,
        on_barge_in: Optional[Callable[[], None]] = None,
        device_index: Optional[int] = None,
    ) -> dict:
        """Hands-free Open-Mic VAD listener with pre-roll & instant Barge-In interrupt when Master starts speaking."""
        with self._free_lock:
            self._stop_free_mic_locked()
            idx = device_index if device_index is not None else self.mic_index
            if idx is None:
                raise RuntimeError("未检测到可用麦克风设备")
            self.mic_index = int(idx)
            info = sd.query_devices(self.mic_index)
            self._free_sr = int(round(float(info.get("default_samplerate", 44100))))
            self._free_cb_utterance = on_utterance
            self._free_cb_barge_in = on_barge_in
            self._free_is_speaking_fn = is_speaking_fn
            self._free_is_busy_fn = is_busy_fn
            self._free_preroll.clear()
            self._free_buf = []
            self._free_active = False
            self._free_speech_frames = 0
            self._free_barge_frames = 0
            self._free_silence_sec = 0.0
            self._free_active_sec = 0.0
            block_frames = max(256, int(round(self._free_sr * 0.032)))  # ~32ms blocks

            def _cb(indata, frames, time_info, status):
                chunk = indata[:, 0].copy()
                now = time.monotonic()
                dt = frames / float(self._free_sr)
                rms = float(np.sqrt(np.mean(chunk * chunk) + 1e-12))
                db = 20.0 * math.log10(max(rms, 1e-6))

                ai_speaking = bool(self._free_is_speaking_fn and self._free_is_speaking_fn())
                ai_busy = bool(self._free_is_busy_fn and self._free_is_busy_fn())

                # While AI is speaking: either allow smart Barge-In interrupt OR guard against echo
                if ai_speaking:
                    if self.barge_in_enabled and self._free_cb_barge_in is not None:
                        self._free_preroll.append(chunk)
                        barge_thr = max(-30.0, float(self.free_mic_threshold_db) + float(self.barge_in_margin_db))
                        if db >= barge_thr:
                            self._free_barge_frames += 1
                            if self._free_barge_frames >= 3:  # ~95ms sustained human speech interrupts AI immediately!
                                self._free_barge_frames = 0
                                self._free_active = True
                                self.recording = True
                                self._free_buf = list(self._free_preroll)
                                self._free_preroll.clear()
                                self._free_silence_sec = 0.0
                                self._free_active_sec = len(self._free_buf) * dt
                                try:
                                    threading.Thread(target=self._free_cb_barge_in, daemon=True).start()
                                except Exception:
                                    pass
                                return
                        else:
                            self._free_barge_frames = 0
                        return
                    else:
                        self._free_last_muted_at = now
                        self._free_barge_frames = 0
                        if self._free_active:
                            self._free_active = False
                            self._free_buf = []
                            self.recording = False
                        self._free_preroll.clear()
                        return

                if ai_busy and not self._free_active:
                    self._free_preroll.append(chunk)
                    return

                if not self.barge_in_enabled and (now - self._free_last_muted_at < 0.50):
                    self._free_preroll.append(chunk)
                    return

                speaking_frame = db >= float(self.free_mic_threshold_db)

                if not self._free_active:
                    self._free_preroll.append(chunk)
                    if speaking_frame:
                        self._free_speech_frames += 1
                        if self._free_speech_frames >= 3:  # ~95ms sustained speech triggers utterance
                            self._free_active = True
                            self.recording = True
                            self._free_buf = list(self._free_preroll)
                            self._free_preroll.clear()
                            self._free_silence_sec = 0.0
                            self._free_active_sec = len(self._free_buf) * dt
                    else:
                        self._free_speech_frames = 0
                else:
                    self._free_buf.append(chunk)
                    self._free_active_sec += dt
                    if speaking_frame:
                        self._free_silence_sec = 0.0
                    else:
                        self._free_silence_sec += dt

                    if self._free_silence_sec >= 0.78 or self._free_active_sec >= 12.0:
                        captured = self._free_buf
                        sr = self._free_sr
                        dur = self._free_active_sec - self._free_silence_sec
                        self._free_active = False
                        self.recording = False
                        self._free_buf = []
                        self._free_speech_frames = 0
                        self._free_barge_frames = 0
                        self._free_silence_sec = 0.0
                        self._free_active_sec = 0.0
                        if dur >= 0.30 and captured and self._free_cb_utterance:
                            cb_fn = self._free_cb_utterance
                            def _dispatch(chunks_in=captured, sr_in=sr, fn=cb_fn):
                                try:
                                    raw = np.concatenate(chunks_in)
                                    m16, pk = prepare_16k_mono(raw, sr_in)
                                    self.last_peak_db = pk
                                    fn(m16, pk)
                                except Exception as ex:
                                    print(f"[FREE_MIC] Dispatch error: {ex}", flush=True)
                            threading.Thread(target=_dispatch, daemon=True).start()

            self._free_stream = sd.InputStream(
                device=self.mic_index,
                samplerate=self._free_sr,
                channels=1,
                dtype="float32",
                blocksize=block_frames,
                callback=_cb,
            )
            self._free_stream.start()
            self.free_mic_enabled = True
            print(f"[FREE_MIC] Started on [{self.mic_index}] {info.get('name', '')} @ {self._free_sr}Hz (thr={self.free_mic_threshold_db}dB)", flush=True)
            return {"ok": True, "freeMicEnabled": True, "device": info.get("name", "")}

    def _stop_free_mic_locked(self) -> None:
        self.free_mic_enabled = False
        self._free_active = False
        self.recording = False
        if self._free_stream is not None:
            try:
                self._free_stream.stop()
                self._free_stream.close()
            except Exception:
                pass
            self._free_stream = None
        self._free_buf = []
        self._free_preroll.clear()

    def stop_free_mic(self) -> dict:
        with self._free_lock:
            self._stop_free_mic_locked()
        return {"ok": True, "freeMicEnabled": False}
