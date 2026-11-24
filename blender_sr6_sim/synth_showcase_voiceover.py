#!/usr/bin/env python3
"""
Synthesizes studio-clean Mandarin narration for the KoiSync Project Showcase Video
using local offline Qwen3-TTS-12Hz-0.6B-CustomVoice (speaker='uncle_fu') on RTX 4070.
Outputs:
  - renders/_build/voiceover_master.wav (24kHz mono normalized, natural pauses)
  - renders/_build/timeline_manifest.json (exact start/end timestamps per sentence & slide)
"""
from __future__ import annotations

import json
import os
import pathlib
import time
import wave

import numpy as np
import torch

os.environ["HF_HOME"] = r"E:\project\c#\KoiSync\artifacts\voice_lab\hf_cache"
os.environ["HUGGINGFACE_HUB_CACHE"] = r"E:\project\c#\KoiSync\artifacts\voice_lab\hf_cache\hub"
os.environ["HF_HUB_OFFLINE"] = "1"
os.environ["TRANSFORMERS_OFFLINE"] = "1"

from faster_qwen3_tts import FasterQwen3TTS

BUILD_DIR = pathlib.Path(r"E:\project\c#\KoiSync\blender_sr6_sim\renders\_build")
BUILD_DIR.mkdir(parents=True, exist_ok=True)

SPEAKER = "uncle_fu"
INSTRUCT = "清晰沉稳的科技项目展示旁白，普通话标准，吐字干脆，语速自然专业，客观克制。"

# Each item: (slide_id, subtitle_text, tts_text, pause_after_s)
SCRIPT_LINES = [
    # Slide 1: Opening Title & Overview
    ("slide_01",
     "如果你玩过恋活，肯定知道社区里的角色卡和动作已经非常丰富。",
     "如果你玩过恋活，肯定知道社区里的角色卡和动作已经非常丰富。", 0.30),
    ("slide_01",
     "这套项目实现了恋活与现实桌面多轴外设的实时动作同步。",
     "这套项目实现了恋活与现实桌面多轴外设的实时动作同步。", 0.28),
    ("slide_01",
     "不仅能正向驱动硬件，还支持通过 SR6 反向运动控制角色动作，并开放了 AI 语音与 MCP 接口。",
     "不仅能正向驱动硬件，还支持通过 S R 6 反向运动控制角色动作，并开放了 AI 语音与 M C P 接口。", 0.65),

    # Slide 2: Hardware Primer (Single-Axis vs 6-DOF)
    ("slide_02",
     "先用十秒简单介绍下桌面同步外设的分类。",
     "先用十秒简单介绍下桌面同步外设的分类。", 0.28),
    ("slide_02",
     "一类是像 Handy 这样的单轴设备，只负责基础的上下往复运动。",
     "一类是像 Handy 这样的单轴设备，只负责基础的上下往复运动。", 0.30),
    ("slide_02",
     "另一类是像 SR6 这样的六轴机械结构，可同时输出上下、前后、左右位移以及旋转、侧倾、俯仰六个自由度。",
     "另一类是像 S R 6 这样的六轴机械结构，可同时输出上下、前后、左右位移，以及旋转、侧倾、俯仰六个自由度。", 0.32),
    ("slide_02",
     "为便于合规展示，本片统一以六轴工业机械臂承接真实六轴数据进行演示。",
     "为便于合规展示，本片统一以六轴工业机械臂承接真实六轴数据进行演示。", 0.65),

    # Slide 3: Main Game Real-Time Sync & Reverse Control
    ("slide_03",
     "传统联动方案需要作者逐帧手动画轨迹脚本，一旦切换动作或节奏就无法对齐。",
     "传统联动方案需要作者逐帧手动画轨迹脚本，一旦切换动作或节奏就无法对齐。", 0.30),
    ("slide_03",
     "KoiSync 直接读取游戏内空间运动数据，无需预制脚本，每一帧实时解算成六轴控制指令。",
     "KoiSync 直接读取游戏内空间运动数据，无需预制脚本，每一帧实时解算成六轴控制指令。", 0.30),
    ("slide_03",
     "右侧演示中，参考几何体的深浅位移与姿态倾角，与六轴机械臂保持逐帧零延迟咬合。",
     "右侧演示中，参考几何体的深浅位移与姿态倾角，与六轴机械臂保持逐帧零延迟咬合。", 0.32),
    ("slide_03",
     "同时系统支持反向控制：通过 SR6 的六轴运动轨迹反驱恋活本体角色动作，打破原版固定循环。",
     "同时系统支持反向控制，通过 S R 6 的六轴运动轨迹反驱恋活本体角色动作，打破原版固定循环。", 0.65),

    # Slide 4: Arbitrary Studio Scene Card Timeline Adaptation
    ("slide_04",
     "除了恋活本体，这套方案同样覆盖了社区里任意包含 Timeline 动画的 Studio 场景卡。",
     "除了恋活本体，这套方案同样覆盖了社区里任意包含 Timeline 动画的 Studio 场景卡。", 0.30),
    ("slide_04",
     "无论原作者是否考虑过外设支持，只需指定参考目标，系统即可顺着 Timeline 逐帧生成多轴信号。",
     "无论原作者是否考虑过外设支持，只需指定参考目标，系统即可顺着 Timeline 逐帧生成多轴信号。", 0.30),
    ("slide_04",
     "底层内置物理行程保护与平滑限幅算法，在保留立体体感的同时防止硬件超程卡死，兼容 SR6 与 Handy。",
     "底层内置物理行程保护与平滑限幅算法，在保留立体体感的同时防止硬件超程卡死，兼容 S R 6 与 Handy。", 0.65),

    # Slide 5: AI Voice Control & Open MCP Interface
    ("slide_05",
     "在动作同步的基础上，项目进一步集成了本地 AI 语音控制，并对外开放了标准 AI MCP 接口。",
     "在动作同步的基础上，项目进一步集成了本地 AI 语音控制，并对外开放了标准 AI M C P 接口。", 0.32),
    ("slide_05",
     "通过 AI MCP 接口，外部模型可直接切换恋活本体动作、实时换人以及更换角色服装状态。",
     "通过 AI M C P 接口，外部模型可直接切换恋活本体动作、实时换人，以及更换角色服装状态。", 0.32),
    ("slide_05",
     "同时可以直接调用 SR6 的完整运动范围，对恋活本体原生动作进行实时扩展与重新编排。",
     "同时可以直接调用 S R 6 的完整运动范围，对恋活本体原生动作进行实时扩展与重新编排。", 0.30),
    ("slide_05",
     "得益于标准 MCP 协议，任何支持 MCP 的大模型与 Agent 客户端均可直接接入读取状态并下发控制。",
     "得益于标准 M C P 协议，任何支持 M C P 的大模型与 Agent 客户端，均可直接接入读取状态并下发控制。", 0.65),

    # Slide 6: 6-DOF Mathematical Trajectory Engine
    ("slide_06",
     "为了充分释放六个自由度的空间表达能力，系统内置了一套六轴数学轨迹引擎。",
     "为了充分释放六个自由度的空间表达能力，系统内置了一套六轴数学轨迹引擎。", 0.30),
    ("slide_06",
     "涵盖虚拟支点行波、变螺距双螺旋、六维黄金比例利萨如、潮汐拍频微颤与平滑混沌吸引子五大轨迹族。",
     "涵盖虚拟支点行波、变螺距双螺旋、六维黄金比例利萨如、潮汐拍频微颤，与平滑混沌吸引子五大轨迹族。", 0.30),
    ("slide_06",
     "所有曲线均可实时映射到六轴外设并反向驱动角色姿态，并支持通过 MCP 接口随时切换。",
     "所有曲线均可实时映射到六轴外设并反向驱动角色姿态，并支持通过 M C P 接口随时切换。", 0.65),

    # Slide 7: Closing Summary (Strictly professional, zero audience CTA)
    ("slide_07",
     "从恋活本体动作扩展、任意 Studio 场景卡的 Timeline 自动适配，再到 AI 实时语音和开放 MCP 接口——",
     "从恋活本体动作扩展、任意 Studio 场景卡的 Timeline 自动适配，再到 AI 实时语音和开放 M C P 接口，", 0.25),
    ("slide_07",
     "这就是我最近折腾出来的整套联动方案。",
     "这就是我最近折腾出来的整套联动方案。", 1.20),
]


def _latest_snapshot(model_dir_name: str) -> str:
    root = pathlib.Path(r"E:\project\c#\KoiSync\artifacts\voice_lab\hf_cache\hub") / model_dir_name / "snapshots"
    snaps = sorted([p for p in root.iterdir() if p.is_dir()])
    return str(snaps[-1])


def trim_silence(wav: np.ndarray, sr: int, thresh: float = 0.012, pad_s: float = 0.05) -> np.ndarray:
    idx = np.where(np.abs(wav) > thresh)[0]
    if len(idx) == 0:
        return wav
    pad = int(pad_s * sr)
    s = max(0, int(idx[0]) - pad)
    e = min(len(wav), int(idx[-1]) + pad)
    return wav[s:e]


def main():
    t0 = time.perf_counter()
    snap = _latest_snapshot("models--Qwen--Qwen3-TTS-12Hz-0.6B-CustomVoice")
    print(f"[TTS] Loading Qwen3-TTS-12Hz-0.6B-CustomVoice from {snap}...", flush=True)
    model = FasterQwen3TTS.from_pretrained(
        snap, device="cuda", dtype=torch.bfloat16, attn_implementation="sdpa", local_files_only=True
    )

    # Warmup
    for _ in model.generate_custom_voice_streaming(
        text="测试预热。", speaker=SPEAKER, language="Chinese", instruct=INSTRUCT, max_new_tokens=48, chunk_size=8
    ):
        pass
    print(f"[TTS] Model ready in {time.perf_counter() - t0:.1f}s. Synthesizing {len(SCRIPT_LINES)} lines...", flush=True)

    sr = 24000
    lead_in_s = 0.60
    chunks_all = [np.zeros(int(lead_in_s * sr), dtype=np.float32)]
    cur_t = lead_in_s

    entries = []
    slide_spans = {}

    for idx, (slide_id, sub_txt, tts_txt, pause_s) in enumerate(SCRIPT_LINES):
        t_line = time.perf_counter()
        parts = []
        for audio_chunk, chunk_sr, _timing in model.generate_custom_voice_streaming(
            text=tts_txt,
            speaker=SPEAKER,
            language="Chinese",
            instruct=INSTRUCT,
            max_new_tokens=360,
            chunk_size=12,
        ):
            sr = int(chunk_sr)
            arr = np.asarray(audio_chunk, dtype=np.float32).reshape(-1)
            parts.append(arr)
        wav = np.concatenate(parts) if parts else np.zeros(int(1.5 * sr), dtype=np.float32)
        wav = trim_silence(wav, sr, thresh=0.010, pad_s=0.04)

        # Normalize each sentence gently to consistent -18 dBFS RMS with soft ceiling
        rms = float(np.sqrt(np.mean(wav ** 2)) + 1e-8)
        target_rms = 0.14
        gain = min(3.0, target_rms / rms)
        wav = np.clip(wav * gain, -0.92, 0.92)

        # Tiny 12ms fade in/out to prevent clicks
        fade_n = min(int(0.012 * sr), len(wav) // 4)
        if fade_n > 1:
            ramp = np.linspace(0.0, 1.0, fade_n, dtype=np.float32)
            wav[:fade_n] *= ramp
            wav[-fade_n:] *= ramp[::-1]

        dur = len(wav) / float(sr)
        start_t = cur_t
        end_t = start_t + dur
        entries.append({
            "index": idx,
            "slide": slide_id,
            "subtitle": sub_txt,
            "start": round(start_t, 3),
            "end": round(end_t, 3),
            "duration": round(dur, 3),
        })
        if slide_id not in slide_spans:
            slide_spans[slide_id] = [start_t, end_t + pause_s]
        else:
            slide_spans[slide_id][1] = end_t + pause_s

        chunks_all.append(wav)
        chunks_all.append(np.zeros(int(pause_s * sr), dtype=np.float32))
        cur_t = end_t + pause_s
        print(f"  [{idx+1:02d}/{len(SCRIPT_LINES)}] {slide_id} {dur:4.2f}s (gen {time.perf_counter()-t_line:4.2f}s) | {sub_txt[:26]}...", flush=True)

    # Extend slide 1 start back to 0.0 and ensure seamless contiguous chapter spans!
    slide_order = ["slide_01", "slide_02", "slide_03", "slide_04", "slide_05", "slide_06", "slide_07"]
    contiguous_slides = []
    prev_end = 0.0
    for i, sid in enumerate(slide_order):
        s_end = cur_t if i == len(slide_order) - 1 else slide_spans[sid][1] - 0.15
        contiguous_slides.append({
            "id": sid,
            "start": round(prev_end, 3),
            "end": round(s_end, 3),
            "duration": round(s_end - prev_end, 3),
        })
        prev_end = s_end

    full_wav = np.concatenate(chunks_all)
    # Final peak normalize to -1.5 dBFS
    peak = float(np.max(np.abs(full_wav)) + 1e-8)
    full_wav = full_wav * (0.84 / peak)
    pcm16 = (np.clip(full_wav, -1.0, 1.0) * 32767.0).astype(np.int16)

    wav_path = BUILD_DIR / "voiceover_master.wav"
    with wave.open(str(wav_path), "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(sr)
        wf.writeframes(pcm16.tobytes())

    manifest = {
        "sample_rate": sr,
        "total_duration": round(len(full_wav) / float(sr), 3),
        "speaker": SPEAKER,
        "slides": contiguous_slides,
        "subtitles": entries,
    }
    man_path = BUILD_DIR / "timeline_manifest.json"
    man_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"[TTS] DONE! Total duration = {manifest['total_duration']:.2f}s -> {wav_path}", flush=True)
    for sl in contiguous_slides:
        print(f"  SLIDE {sl['id']}: {sl['start']:6.2f}s -> {sl['end']:6.2f}s ({sl['duration']:5.2f}s)", flush=True)


if __name__ == "__main__":
    main()
