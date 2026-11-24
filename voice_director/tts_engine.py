#!/usr/bin/env python3
"""
Locked V6 Streaming TTS Engine for KoiSync.VoiceDirector (ZH + JA ONLY):
  - Chinese ('zh'): Qwen3-TTS-12Hz-0.6B-CustomVoice (`Serena` + exact locked 17_Serena instruct + '呼' sanitizer)
                    ~2,397 MB VRAM, ~475 ms TTFA, ~1.64x realtime!
  - Japanese ('ja'): Qwen3-TTS-12Hz-0.6B-Base (`_anchor_04_JA_clean_mono.wav` + 36-clip Falafel Master Centroid)
                     ~2,634 MB VRAM, ~560 ms TTFA, ~1.55x realtime!
Maintains ONE active 0.6B model in GPU VRAM at a time (~2.5 GB total) with clean hot-swap between ZH and JA.
"""
from __future__ import annotations

import gc
import io
import json
import math
import os
import pathlib
import re
import threading
import time
import urllib.parse
import urllib.request
from typing import Callable, Dict, List, Optional

DIR_HERE = pathlib.Path(__file__).resolve().parent
CUSTOM_MODELS_DIR = DIR_HERE / "custom_models"
LAB_DIR = pathlib.Path(r"E:\project\c#\KoiSync\artifacts\voice_lab")
HF_CACHE = LAB_DIR / "hf_cache"
PACK_DIR = LAB_DIR / "falafel_pack"

MODELS_README_TEXT = """=============================================================
KoiSync.VoiceDirector · 自定义训练语音模型放入说明
=============================================================

把你自己训练好的语音模型或参考音色放到本目录即可，然后在设置页点击「刷新模型列表」选择启用：

【方式 A：放入完整训练/微调好的 Qwen3-TTS 模型文件夹】
  新建一个子文件夹，例如：
    custom_models/my_trained_voice/
      ├── config.json
      ├── model.safetensors  (或其他权重文件)
      └── model_profile.json (可选，用于指定默认配置)

  model_profile.json 示例：
  {
    "name": "我的自定义中文声线",
    "language": "zh",
    "mode": "custom_voice",
    "speaker": "Serena",
    "instruct": "温柔轻声贴耳说话"
  }
  （若训练的是 Base/克隆模型，将 "mode" 设为 "voice_clone"，并放入 "ref_wav" 与 "ref_text"）

【方式 B：放入自定义音色参考音频（零样本克隆，最简单！）】
  直接把 3~12 秒干净人声 WAV 放进本目录，可选配同名 .txt 写音频里说的话（和可选同名 .pt 音色向量）：
    custom_models/my_waifu.wav
    custom_models/my_waifu.txt   (可选，内容为音频台词，提升克隆相似度)
    custom_models/my_waifu.pt    (可选，预先提取的说话人 centroid 向量)

放好后无需重启，在控制台「设置 / 模型配置」页点击「刷新模型列表」并选中即可热切换！
"""


def ensure_custom_models_dir() -> pathlib.Path:
    CUSTOM_MODELS_DIR.mkdir(parents=True, exist_ok=True)
    readme = CUSTOM_MODELS_DIR / "README_模型放入说明.txt"
    if not readme.is_file():
        try:
            readme.write_text(MODELS_README_TEXT, encoding="utf-8")
        except Exception:
            pass
    return CUSTOM_MODELS_DIR


def _normalize_lang(lang: Optional[str]) -> str:
    s = str(lang or "zh").strip().lower()
    if s.startswith("ja") or s.startswith("jp"):
        return "ja"
    if s.startswith("en"):
        return "en"
    return "zh"


def _lang_display_name(lang: str) -> str:
    return {"zh": "Chinese", "ja": "Japanese", "en": "English"}.get(lang, "Chinese")


def scan_voice_models() -> List[Dict[str, str]]:
    ensure_custom_models_dir()
    items: List[Dict[str, str]] = [
        {
            "id": "builtin:zh_serena",
            "name": "内置中文 · 17_Serena (锁定贴耳)",
            "kind": "builtin",
            "language": "zh",
            "path": "Qwen3-TTS-12Hz-0.6B-CustomVoice",
        },
        {
            "id": "builtin:ja_falafel",
            "name": "内置日语 · 04_JA Falafel (锁定克隆)",
            "kind": "builtin",
            "language": "ja",
            "path": "Qwen3-TTS-12Hz-0.6B-Base",
        },
        {
            "id": "builtin:en_serena",
            "name": "内置英语 · English Serena (Soft Whisper)",
            "kind": "builtin",
            "language": "en",
            "path": "Qwen3-TTS-12Hz-0.6B-CustomVoice",
        },
    ]

    # 1) User-trained model directories containing config.json or *.safetensors
    for child in sorted(CUSTOM_MODELS_DIR.iterdir()):
        if child.is_dir():
            has_weights = (child / "config.json").is_file() or any(child.glob("*.safetensors")) or any(child.glob("*.pt"))
            if not has_weights:
                continue
            prof: dict = {}
            pf = child / "model_profile.json"
            if pf.is_file():
                try:
                    prof = json.loads(pf.read_text(encoding="utf-8"))
                except Exception:
                    prof = {}
            lang = _normalize_lang(prof.get("language", "zh"))
            items.append({
                "id": f"custom_dir:{child.name}",
                "name": str(prof.get("name") or f"用户训练模型 · {child.name}"),
                "kind": "custom_dir",
                "language": lang,
                "path": str(child),
                "mode": str(prof.get("mode", "custom_voice")),
                "speaker": str(prof.get("speaker", LOCKED_ZH_SPEAKER)),
                "instruct": str(prof.get("instruct", LOCKED_ZH_INSTRUCT)),
                "ref_wav": str(prof.get("ref_wav", "")),
                "ref_text": str(prof.get("ref_text", "")),
            })
        elif child.is_file() and child.suffix.lower() == ".wav":
            stem = child.stem
            low_stem = stem.lower()
            txt_p = child.with_suffix(".txt")
            pt_p = child.with_suffix(".pt")
            ref_txt = txt_p.read_text(encoding="utf-8", errors="replace").strip() if txt_p.is_file() else ""
            if "ja" in low_stem or "jp" in low_stem:
                lang = "ja"
            elif "en" in low_stem or "eng" in low_stem:
                lang = "en"
            else:
                lang = "zh"
            items.append({
                "id": f"custom_wav:{child.name}",
                "name": f"自定义音色克隆 · {stem}",
                "kind": "custom_wav",
                "language": lang,
                "path": str(child),
                "ref_text": ref_txt,
                "centroid_pt": str(pt_p) if pt_p.is_file() else "",
            })
    return items

# Force all HuggingFace / PyTorch caches onto E: drive (NEVER write large files to C:)
os.environ["HF_HOME"] = str(HF_CACHE)
os.environ["HUGGINGFACE_HUB_CACHE"] = str(HF_CACHE / "hub")
os.environ["TORCH_HOME"] = str(LAB_DIR / "torch_cache")
os.environ["HF_HUB_OFFLINE"] = "1"
os.environ["TRANSFORMERS_OFFLINE"] = "1"

import numpy as np
import soundfile as sf
import torch
from scipy.signal import resample_poly
from faster_qwen3_tts import FasterQwen3TTS

# Exact locked V6 style anchors (ZH / JA / EN!)
LOCKED_ZH_SPEAKER = "Serena"
LOCKED_ZH_INSTRUCT = "用极度温柔妩媚、贴耳轻声细语且带着明显急促喘息和哽咽尾音的语气说话，断断续续，充满沉浸感"
LOCKED_EN_SPEAKER = "Serena"
LOCKED_EN_INSTRUCT = "Speak in an ultra-soft, intimate, breathy close-ear whisper with gentle pauses and warm affectionate emotion."

LOCKED_JA_ANCHOR_WAV = PACK_DIR / "audition_v6_locked" / "_anchor_04_JA_clean_mono.wav"
LOCKED_JA_ANCHOR_TXT = "あっ、だめっ……はぁっ……そんな速く突かれたらっ……んっ……もう、イッちゃいそう……っ……止めないで……"
LOCKED_JA_CENTROID_PT = PACK_DIR / "embeddings" / "falafel_centroid_spk.pt"


def _decode_audio_bytes_to_24k(audio_bytes: bytes) -> np.ndarray:
    data, sr = sf.read(io.BytesIO(audio_bytes), dtype="float32")
    if data.ndim == 2:
        data = np.mean(data, axis=1)
    data = np.asarray(data, dtype=np.float32).reshape(-1)
    if sr != 24000 and len(data) > 0:
        g = math.gcd(int(sr), 24000)
        data = resample_poly(data, 24000 // g, int(sr) // g).astype(np.float32)
    return data


def sanitize_zh_text(text: str) -> str:
    """Prevent Serena from reading literal '呼' aloud as '喔呼'; replace with smooth breath pause."""
    t = text.strip()
    t = re.sub(r"呼[啊哈呀]?", "……", t)
    t = re.sub(r"…{3,}", "……", t)
    return t


def _latest_snapshot(repo_folder_name: str) -> str:
    snaps = sorted((HF_CACHE / "hub" / repo_folder_name / "snapshots").iterdir())
    if not snaps:
        raise FileNotFoundError(f"Offline model snapshot not found under {HF_CACHE / 'hub' / repo_folder_name}")
    return str(snaps[-1])


class LockedV6TtsEngine:
    def __init__(self, initial_language: str = "zh", initial_model_id: str = "") -> None:
        self._lock = threading.Lock()
        self._cancel_event = threading.Event()
        self.active_lang: Optional[str] = None
        norm_init = _normalize_lang(initial_language)
        default_id = {"zh": "builtin:zh_serena", "ja": "builtin:ja_falafel", "en": "builtin:en_serena"}[norm_init]
        self.active_model_id: str = initial_model_id or default_id
        self._loaded_model_id: Optional[str] = None
        self._loaded_meta: dict = {}
        self._model: Optional[FasterQwen3TTS] = None
        self._vcp: Optional[dict] = None
        self._vcp_ref_text: str = LOCKED_JA_ANCHOR_TXT
        self.last_ttfa_ms: float = 0.0
        self.last_rtf: float = 0.0
        self.vram_mb: float = 0.0
        self.loading: bool = False
        self.ready: bool = False
        self.last_error: str = ""
        self.initial_language = norm_init
        ensure_custom_models_dir()

    def list_models(self) -> List[Dict[str, str]]:
        return scan_voice_models()

    def select_model(self, model_id: str, preload: bool = False) -> dict:
        models = self.list_models()
        found = next((m for m in models if m["id"] == model_id), None)
        if not found:
            return {"ok": False, "error": f"未找到语音模型: {model_id}"}
        self.active_model_id = model_id
        self.active_lang = found.get("language", "zh")
        if preload:
            self.ensure_loaded(self.active_lang, model_id=model_id)
        return {"ok": True, "activeVoiceModel": self.active_model_id, "language": self.active_lang, "model": found}

    def unload(self) -> None:
        with self._lock:
            if self._model is not None:
                del self._model
                self._model = None
            self._vcp = None
            self._loaded_model_id = None
            self.ready = False
            gc.collect()
            if torch.cuda.is_available():
                torch.cuda.empty_cache()

    def ensure_loaded(self, lang: str, model_id: Optional[str] = None) -> None:
        lang = _normalize_lang(lang)
        target_id = model_id or self.active_model_id
        if target_id.startswith("builtin:"):
            target_id = {"zh": "builtin:zh_serena", "ja": "builtin:ja_falafel", "en": "builtin:en_serena"}[lang]
        with self._lock:
            if self._model is not None and self._loaded_model_id == target_id and self.ready:
                return
            self.loading = True
            self.ready = False
            self.last_error = ""
            try:
                # Reuse already-loaded CustomVoice weights when switching between builtin:zh_serena and builtin:en_serena!
                reuse_cv = (
                    self._model is not None
                    and self._loaded_model_id in ("builtin:zh_serena", "builtin:en_serena")
                    and target_id in ("builtin:zh_serena", "builtin:en_serena")
                )
                if not reuse_cv and self._model is not None:
                    del self._model
                    self._model = None
                    self._vcp = None
                    gc.collect()
                    if torch.cuda.is_available():
                        torch.cuda.empty_cache()

                models = scan_voice_models()
                meta = next((m for m in models if m["id"] == target_id), None)
                if meta is None:
                    target_id = {"zh": "builtin:zh_serena", "ja": "builtin:ja_falafel", "en": "builtin:en_serena"}[lang]
                    meta = next((m for m in models if m["id"] == target_id), models[0])

                torch.cuda.reset_peak_memory_stats()
                t0 = time.perf_counter()
                kind = meta.get("kind", "builtin")
                eff_lang = _normalize_lang(meta.get("language", lang))
                lang_label = _lang_display_name(eff_lang)

                if target_id in ("builtin:zh_serena", "builtin:en_serena"):
                    if not reuse_cv:
                        path = _latest_snapshot("models--Qwen--Qwen3-TTS-12Hz-0.6B-CustomVoice")
                        self._model = FasterQwen3TTS.from_pretrained(
                            path, device="cuda", dtype=torch.bfloat16, attn_implementation="sdpa", local_files_only=True
                        )
                    self._vcp = None
                    warm_txt = "Mmm... right here with you..." if eff_lang == "en" else "嗯……主人……"
                    warm_ins = LOCKED_EN_INSTRUCT if eff_lang == "en" else LOCKED_ZH_INSTRUCT
                    for _ in self._model.generate_custom_voice_streaming(
                        text=warm_txt, speaker=LOCKED_ZH_SPEAKER, language=lang_label,
                        instruct=warm_ins, max_new_tokens=56, chunk_size=8,
                    ):
                        pass
                elif target_id == "builtin:ja_falafel":
                    path = _latest_snapshot("models--Qwen--Qwen3-TTS-12Hz-0.6B-Base")
                    self._model = FasterQwen3TTS.from_pretrained(
                        path, device="cuda", dtype=torch.bfloat16, attn_implementation="sdpa", local_files_only=True
                    )
                    ckpt = torch.load(LOCKED_JA_CENTROID_PT, map_location="cpu", weights_only=False)
                    centroid = ckpt["centroid"].to(device="cuda", dtype=torch.bfloat16)
                    ain = self._model._load_ref_audio_with_silence(LOCKED_JA_ANCHOR_WAV, silence_secs=0.35)
                    items = self._model.model.create_voice_clone_prompt(
                        ref_audio=ain, ref_text=LOCKED_JA_ANCHOR_TXT, x_vector_only_mode=False
                    )
                    self._vcp = {
                        "ref_spk_embedding": [centroid],
                        "ref_code": [items[0].ref_code],
                        "x_vector_only_mode": [False],
                        "icl_mode": [True],
                    }
                    self._vcp_ref_text = LOCKED_JA_ANCHOR_TXT
                    for _ in self._model.generate_voice_clone_streaming(
                        text="んっ……先輩……", language="Japanese", voice_clone_prompt=self._vcp,
                        ref_text=self._vcp_ref_text, max_new_tokens=64, chunk_size=8,
                    ):
                        pass
                elif kind == "custom_dir":
                    model_dir = meta["path"]
                    self._model = FasterQwen3TTS.from_pretrained(
                        model_dir, device="cuda", dtype=torch.bfloat16, attn_implementation="sdpa", local_files_only=True
                    )
                    mode = str(meta.get("mode", "custom_voice")).lower()
                    if mode == "voice_clone" and meta.get("ref_wav"):
                        wav_p = pathlib.Path(meta["ref_wav"])
                        if not wav_p.is_absolute():
                            wav_p = pathlib.Path(model_dir) / wav_p
                        ref_txt = str(meta.get("ref_text") or "")
                        ain = self._model._load_ref_audio_with_silence(wav_p, silence_secs=0.35)
                        items = self._model.model.create_voice_clone_prompt(
                            ref_audio=ain, ref_text=ref_txt or "你好", x_vector_only_mode=(not bool(ref_txt))
                        )
                        self._vcp = {
                            "ref_spk_embedding": [items[0].ref_spk_embedding],
                            "ref_code": [items[0].ref_code],
                            "x_vector_only_mode": [not bool(ref_txt)],
                            "icl_mode": [bool(ref_txt)],
                        }
                        self._vcp_ref_text = ref_txt
                        for _ in self._model.generate_voice_clone_streaming(
                            text="嗯……", language=lang_label, voice_clone_prompt=self._vcp,
                            ref_text=self._vcp_ref_text, max_new_tokens=48, chunk_size=8,
                        ):
                            pass
                    else:
                        spk = str(meta.get("speaker") or LOCKED_ZH_SPEAKER)
                        ins = str(meta.get("instruct") or LOCKED_ZH_INSTRUCT)
                        for _ in self._model.generate_custom_voice_streaming(
                            text="嗯……", speaker=spk, language=lang_label,
                            instruct=ins, max_new_tokens=48, chunk_size=8,
                        ):
                            pass
                elif kind == "custom_wav":
                    path = _latest_snapshot("models--Qwen--Qwen3-TTS-12Hz-0.6B-Base")
                    self._model = FasterQwen3TTS.from_pretrained(
                        path, device="cuda", dtype=torch.bfloat16, attn_implementation="sdpa", local_files_only=True
                    )
                    wav_p = pathlib.Path(meta["path"])
                    ref_txt = str(meta.get("ref_text") or "")
                    ain = self._model._load_ref_audio_with_silence(wav_p, silence_secs=0.35)
                    items = self._model.model.create_voice_clone_prompt(
                        ref_audio=ain, ref_text=ref_txt or "你好", x_vector_only_mode=(not bool(ref_txt))
                    )
                    emb = items[0].ref_spk_embedding
                    pt_path = str(meta.get("centroid_pt") or "")
                    if pt_path and pathlib.Path(pt_path).is_file():
                        ckpt = torch.load(pt_path, map_location="cpu", weights_only=False)
                        if isinstance(ckpt, dict) and "centroid" in ckpt:
                            emb = ckpt["centroid"].to(device="cuda", dtype=torch.bfloat16)
                        elif isinstance(ckpt, torch.Tensor):
                            emb = ckpt.to(device="cuda", dtype=torch.bfloat16)
                    self._vcp = {
                        "ref_spk_embedding": [emb],
                        "ref_code": [items[0].ref_code],
                        "x_vector_only_mode": [not bool(ref_txt)],
                        "icl_mode": [bool(ref_txt)],
                    }
                    self._vcp_ref_text = ref_txt
                    for _ in self._model.generate_voice_clone_streaming(
                        text="嗯……", language=lang_label, voice_clone_prompt=self._vcp,
                        ref_text=self._vcp_ref_text, max_new_tokens=48, chunk_size=8,
                    ):
                        pass

                self.active_lang = eff_lang
                self.active_model_id = target_id
                self._loaded_model_id = target_id
                self._loaded_meta = meta
                self.vram_mb = round(torch.cuda.max_memory_allocated() / (1024 * 1024), 1)
                self.ready = True
                print(
                    f"[TTS] Loaded & warmed '{target_id}' ({eff_lang}) in {time.perf_counter()-t0:.2f}s | peakVRAM={self.vram_mb:.0f} MB",
                    flush=True,
                )
            except Exception as exc:
                self.last_error = str(exc)
                print(f"[TTS] Failed to load model '{target_id}': {exc}", flush=True)
                raise
            finally:
                self.loading = False

    def cancel_active(self) -> None:
        self._cancel_event.set()

    def stream_speak_api(
        self,
        text: str,
        lang: str,
        api_cfg: dict,
        on_chunk: Callable[[np.ndarray, int, bool], None],
    ) -> dict:
        """Universal TTS API synthesizer supporting OpenAI /audio/speech, ElevenLabs, GPT-SoVITS, Fish Audio & Custom HTTP."""
        self._cancel_event.clear()
        provider = str(api_cfg.get("ttsProvider") or "openai_tts").strip().lower()
        base_url = str(api_cfg.get("ttsBaseUrl") or "").strip().rstrip("/")
        api_key = str(api_cfg.get("ttsApiKey") or "").strip()
        model = str(api_cfg.get("ttsModel") or "gpt-4o-mini-tts").strip()
        voice = str(api_cfg.get("ttsVoice") or "shimmer").strip()
        speed = float(api_cfg.get("ttsSpeed") or 1.0)
        custom_tpl = str(api_cfg.get("ttsCustomJson") or "").strip()

        t0 = time.perf_counter()
        headers = {"Content-Type": "application/json"}
        if provider == "openai_tts":
            url = (base_url or "https://api.openai.com/v1")
            if not url.endswith("/audio/speech"):
                url = url + "/audio/speech"
            if api_key:
                headers["Authorization"] = f"Bearer {api_key}"
            body = {
                "model": model or "gpt-4o-mini-tts",
                "input": text,
                "voice": voice or "shimmer",
                "response_format": "wav",
                "speed": max(0.5, min(2.0, speed)),
            }
            if "4o-mini-tts" in body["model"]:
                body["instructions"] = LOCKED_EN_INSTRUCT if lang == "en" else LOCKED_ZH_INSTRUCT
            data_bytes = json.dumps(body, ensure_ascii=False).encode("utf-8")
        elif provider == "elevenlabs":
            vid = voice or "21m00Tcm4TlvDq8ikWAM"
            url = f"{base_url or 'https://api.elevenlabs.io/v1'}/text-to-speech/{urllib.parse.quote(vid)}?output_format=pcm_24000"
            if api_key:
                headers["xi-api-key"] = api_key
            body = {
                "text": text,
                "model_id": model or "eleven_multilingual_v2",
                "voice_settings": {"stability": 0.42, "similarity_boost": 0.78},
            }
            data_bytes = json.dumps(body, ensure_ascii=False).encode("utf-8")
        elif provider == "gpt_sovits":
            url = base_url or "http://127.0.0.1:9880/tts"
            if not url.endswith("/tts"):
                url = url + "/tts"
            body = {
                "text": text,
                "text_lang": lang,
                "ref_audio_path": voice,
                "prompt_lang": lang,
                "prompt_text": str(api_cfg.get("ttsPromptText") or ""),
                "media_type": "wav",
                "speed_factor": max(0.5, min(2.0, speed)),
            }
            data_bytes = json.dumps(body, ensure_ascii=False).encode("utf-8")
        elif provider == "fish_audio":
            url = (base_url or "https://api.fish.audio/v1/tts")
            if not url.endswith("/tts"):
                url = url + "/v1/tts"
            if api_key:
                headers["Authorization"] = f"Bearer {api_key}"
            body = {"text": text, "format": "wav"}
            if voice:
                body["reference_id"] = voice
            data_bytes = json.dumps(body, ensure_ascii=False).encode("utf-8")
        else:  # custom_http
            if not base_url:
                return {"ok": False, "error": "尚未填写自定义 TTS API URL"}
            url = base_url
            if api_key:
                headers["Authorization"] = f"Bearer {api_key}"
            if custom_tpl:
                rendered = custom_tpl.replace("{text}", text.replace('"', '\\"')).replace("{lang}", lang).replace("{voice}", voice)
                data_bytes = rendered.encode("utf-8")
            else:
                data_bytes = json.dumps({"text": text, "language": lang, "voice": voice, "model": model, "format": "wav"}, ensure_ascii=False).encode("utf-8")

        req = urllib.request.Request(url, data=data_bytes, headers=headers, method="POST")
        with urllib.request.urlopen(req, timeout=25.0) as r:
            raw_audio = r.read()
        if self._cancel_event.is_set():
            return {"ok": True, "cancelled": True, "ttfaMs": 0.0}

        if provider == "elevenlabs" and not raw_audio.startswith(b"RIFF"):
            pcm_i16 = np.frombuffer(raw_audio, dtype=np.int16)
            wav_24k = (pcm_i16.astype(np.float32) / 32768.0).reshape(-1)
        else:
            wav_24k = _decode_audio_bytes_to_24k(raw_audio)

        ttfa_ms = round((time.perf_counter() - t0) * 1000.0, 1)
        self.last_ttfa_ms = ttfa_ms
        # Stream into mixer in 120ms chunks so Barge-In interrupt can cut immediately!
        step = 2880
        first = True
        for off in range(0, len(wav_24k), step):
            if self._cancel_event.is_set():
                return {"ok": True, "cancelled": True, "ttfaMs": ttfa_ms}
            on_chunk(wav_24k[off : off + step], 24000, first)
            first = False
        dur = len(wav_24k) / 24000.0
        self.last_rtf = round((time.perf_counter() - t0) / max(0.01, dur), 3)
        return {"ok": True, "language": lang, "text": text, "ttfaMs": ttfa_ms, "audioSec": round(dur, 2), "provider": provider}

    def stream_speak(
        self,
        text: str,
        lang: Optional[str],
        intensity: str,
        on_chunk: Callable[[np.ndarray, int, bool], None],
        api_cfg: Optional[dict] = None,
    ) -> dict:
        use_lang = _normalize_lang(lang or self.active_lang or self.initial_language)
        if api_cfg and str(api_cfg.get("ttsProvider") or "local").strip().lower() != "local":
            try:
                return self.stream_speak_api(text.strip(), use_lang, api_cfg, on_chunk)
            except Exception as exc:
                self.last_error = f"TTS API 失败: {exc}"
                print(f"[TTS_API] Failed ({exc}), falling back to local engine", flush=True)

        self.ensure_loaded(use_lang)

        with self._lock:
            self._cancel_event.clear()
            clean_text = sanitize_zh_text(text) if use_lang == "zh" else text.strip()
            if not clean_text:
                return {"ok": False, "error": "empty text"}

            max_tok = min(260, max(96, len(clean_text) * (6 if use_lang == "zh" else (4 if use_lang == "en" else 5))))
            temp = 0.88 if use_lang == "zh" else (0.82 if intensity == "high" else 0.80)
            top_p = 0.92 if use_lang == "zh" else 0.91
            lang_label = _lang_display_name(use_lang)

            t0 = time.perf_counter()
            ttfa_ms = 0.0
            total_samples = 0
            sr_out = 24000
            chunk_idx = 0

            if self._vcp is not None:
                gen = self._model.generate_voice_clone_streaming(
                    text=clean_text,
                    language=lang_label,
                    voice_clone_prompt=self._vcp,
                    ref_text=self._vcp_ref_text,
                    max_new_tokens=max_tok,
                    temperature=temp,
                    top_p=top_p,
                    repetition_penalty=1.06,
                    chunk_size=8,
                )
            else:
                default_ins = LOCKED_EN_INSTRUCT if use_lang == "en" else LOCKED_ZH_INSTRUCT
                spk = str(self._loaded_meta.get("speaker") or LOCKED_ZH_SPEAKER)
                ins = str(self._loaded_meta.get("instruct") or default_ins)
                gen = self._model.generate_custom_voice_streaming(
                    text=clean_text,
                    speaker=spk,
                    language=lang_label,
                    instruct=ins,
                    max_new_tokens=max_tok,
                    temperature=temp,
                    top_p=top_p,
                    repetition_penalty=1.05,
                    chunk_size=8,
                )

            cancelled = False
            for audio_chunk, sr, _ in gen:
                if self._cancel_event.is_set():
                    cancelled = True
                    break
                if chunk_idx == 0:
                    ttfa_ms = (time.perf_counter() - t0) * 1000.0
                    self.last_ttfa_ms = round(ttfa_ms, 1)
                sr_out = sr
                arr = np.asarray(audio_chunk, dtype=np.float32)
                total_samples += len(arr)
                on_chunk(arr, sr_out, chunk_idx == 0)
                chunk_idx += 1

            elapsed = time.perf_counter() - t0
            dur = total_samples / float(sr_out)
            rtf = elapsed / max(0.01, dur)
            self.last_ttfa_ms = round(ttfa_ms, 1)
            self.last_rtf = round(rtf, 3)
            self.vram_mb = round(torch.cuda.max_memory_allocated() / (1024 * 1024), 1)
            return {
                "ok": True,
                "language": use_lang,
                "text": clean_text,
                "ttfaMs": self.last_ttfa_ms,
                "audioSec": round(dur, 2),
                "rtf": self.last_rtf,
                "cancelled": cancelled,
                "vramMB": self.vram_mb,
            }
