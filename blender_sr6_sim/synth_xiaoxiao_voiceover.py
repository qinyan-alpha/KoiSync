#!/usr/bin/env python3
"""
Synthesizes the full KoiSync Project Showcase narration using the exact same
Microsoft Edge Neural TTS voice & pacing as Codex's reference deliverable:
  voice = 'zh-CN-XiaoxiaoNeural', rate = '+6%', loudnorm = I=-16:TP=-1.5:LRA=7

Outputs:
  - renders/_build/voiceover_master.wav      (48kHz PCM16 normalized)
  - renders/_build/timeline_manifest.json    (exact start/end timestamps per sentence & slide)
  - renders/KoiSync_AI中文配音.wav            (standalone master voiceover WAV)
  - renders/KoiSync_中文字幕.srt              (standalone SRT subtitle file)
"""
from __future__ import annotations

import asyncio
import json
import pathlib
import shutil
import subprocess
import time

import edge_tts

RENDERS_DIR = pathlib.Path(r"E:\project\c#\KoiSync\blender_sr6_sim\renders")
BUILD_DIR = RENDERS_DIR / "_build"
CLIPS_DIR = BUILD_DIR / "xiaoxiao_clips"
CLIPS_DIR.mkdir(parents=True, exist_ok=True)

VOICE = "zh-CN-XiaoxiaoNeural"
RATE = "+6%"

# (slide_id, subtitle_and_tts_text, pause_after_s)
SCRIPT_LINES = [
    # Slide 1: Opening Title & Overview
    ("slide_01", "如果你玩过恋活，肯定知道社区里的角色卡和动作已经非常丰富。", 0.25),
    ("slide_01", "这套项目实现了恋活与现实桌面多轴外设的实时动作同步。", 0.25),
    ("slide_01", "不仅能正向驱动硬件，还支持通过 SR6 反向运动控制角色动作，并开放了 AI 语音与 MCP 接口。", 0.60),

    # Slide 2: Hardware Primer
    ("slide_02", "先用十秒简单介绍下桌面同步外设的分类。", 0.25),
    ("slide_02", "一类是像 Handy 这样的单轴设备，只负责基础的上下往复运动。", 0.25),
    ("slide_02", "另一类是像 SR6 这样的六轴机械结构，可同时输出上下、前后、左右位移，以及旋转、侧倾、俯仰六个自由度。", 0.28),
    ("slide_02", "为便于合规展示，本片统一以六轴工业机械臂承接真实六轴数据进行演示。", 0.60),

    # Slide 3: Main Game Real-Time Sync & Reverse Control
    ("slide_03", "传统联动方案需要作者逐帧手动画轨迹脚本，一旦切换动作或节奏就无法对齐。", 0.25),
    ("slide_03", "KoiSync 直接读取游戏内空间运动数据，无需预制脚本，每一帧实时解算成六轴控制指令。", 0.25),
    ("slide_03", "右侧演示中，参考几何体的深浅位移与姿态倾角，与六轴机械臂保持逐帧零延迟咬合。", 0.28),
    ("slide_03", "同时系统支持反向控制，通过 SR6 的六轴运动轨迹反驱恋活本体角色动作，打破原版固定循环。", 0.60),

    # Slide 4: Studio Scene Card Timeline Auto-Adaptation
    ("slide_04", "除了恋活本体，这套方案同样覆盖了社区里任意包含 Timeline 动画的 Studio 场景卡。", 0.25),
    ("slide_04", "无论原作者是否考虑过外设支持，只需指定参考目标，系统即可顺着 Timeline 逐帧生成多轴信号。", 0.25),
    ("slide_04", "底层内置物理行程保护与平滑限幅算法，在保留立体体感的同时防止硬件超程卡死，兼容 SR6 与 Handy。", 0.60),

    # Slide 5: AI Voice Control & Open MCP Interface
    ("slide_05", "在动作同步的基础上，项目进一步集成了本地 AI 语音控制，并对外开放了标准 AI MCP 接口。", 0.28),
    ("slide_05", "通过 AI MCP 接口，外部模型可直接切换恋活本体动作、实时换人，以及更换角色服装状态。", 0.28),
    ("slide_05", "同时可以直接调用 SR6 的完整运动范围，对恋活本体原生动作进行实时扩展与重新编排。", 0.25),
    ("slide_05", "得益于标准 MCP 协议，任何支持 MCP 的大模型与 Agent 客户端，均可直接接入读取状态并下发控制。", 0.60),

    # Slide 6: 6-DOF Mathematical Trajectory Engine
    ("slide_06", "为了充分释放六个自由度的空间表达能力，系统内置了一套六轴数学轨迹引擎。", 0.25),
    ("slide_06", "涵盖虚拟支点行波、变螺距双螺旋、六维黄金比例利萨如、潮汐拍频微颤，与平滑混沌吸引子五大轨迹族。", 0.25),
    ("slide_06", "所有曲线均可实时映射到六轴外设并反向驱动角色姿态，并支持通过 MCP 接口随时切换。", 0.60),

    # Slide 7: Closing Summary (Strictly professional, zero audience CTA)
    ("slide_07", "从恋活本体动作扩展、任意 Studio 场景卡的 Timeline 自动适配，再到 AI 实时语音和开放 MCP 接口——", 0.20),
    ("slide_07", "这就是我最近折腾出来的整套联动方案。", 1.10),
]


def ffprobe_dur(path: pathlib.Path) -> float:
    out = subprocess.check_output(
        ["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(path)],
        text=True,
    ).strip()
    return float(out)


def srt_time(v: float) -> str:
    ms = int(round(v * 1000))
    return f"{ms // 3600000:02d}:{(ms // 60000) % 60:02d}:{(ms // 1000) % 60:02d},{ms % 1000:03d}"


async def synth_all():
    t0 = time.perf_counter()
    print(f"[XIAOXIAO] Synthesizing {len(SCRIPT_LINES)} lines ({VOICE}, rate={RATE})...", flush=True)
    lead_in_s = 0.45
    cur_t = lead_in_s
    entries = []
    slide_spans = {}
    clip_paths = []

    for idx, (slide_id, text, pause_s) in enumerate(SCRIPT_LINES):
        mp3_path = CLIPS_DIR / f"line_{idx:02d}.mp3"
        # Clean dash pause for TTS natural reading
        tts_text = text.replace("——", "，")
        for attempt in range(3):
            try:
                await edge_tts.Communicate(tts_text, VOICE, rate=RATE).save(str(mp3_path))
                break
            except Exception as e:
                if attempt == 2:
                    raise
                print(f"  retry {attempt+1} for line {idx}: {e}", flush=True)
                await asyncio.sleep(1.0)

        dur = ffprobe_dur(mp3_path)
        start_t = cur_t
        end_t = start_t + dur
        entries.append({
            "index": idx,
            "slide": slide_id,
            "subtitle": text,
            "start": round(start_t, 3),
            "end": round(end_t, 3),
            "duration": round(dur, 3),
        })
        clip_paths.append((mp3_path, start_t))

        if slide_id not in slide_spans:
            slide_spans[slide_id] = [start_t, end_t + pause_s]
        else:
            slide_spans[slide_id][1] = end_t + pause_s

        cur_t = end_t + pause_s
        print(f"  [{idx+1:02d}/{len(SCRIPT_LINES)}] {slide_id} {dur:4.2f}s | {text[:28]}...", flush=True)

    total_dur = round(cur_t, 3)

    # Mix all clips cleanly at exact millisecond offsets with Codex's exact loudnorm target (-16 LUFS / -1.5 dBTP)
    cmd = ["ffmpeg", "-y", "-v", "error"]
    filters = []
    labels = []
    for i, (mp3_p, st) in enumerate(clip_paths):
        cmd.extend(["-i", str(mp3_p)])
        delay_ms = int(round(st * 1000))
        filters.append(f"[{i}:a]aresample=48000,adelay={delay_ms}:all=1[a{i}]")
        labels.append(f"[a{i}]")
    filters.append(
        "".join(labels)
        + f"amix=inputs={len(clip_paths)}:normalize=0,apad,atrim=duration={total_dur:.3f},loudnorm=I=-16:TP=-1.5:LRA=7[aout]"
    )
    wav_master = BUILD_DIR / "voiceover_master.wav"
    cmd.extend(["-filter_complex", ";".join(filters), "-map", "[aout]", "-ac", "1", "-ar", "48000", "-c:a", "pcm_s16le", str(wav_master)])
    subprocess.run(cmd, check=True)

    # Copy standalone master WAV + SRT to renders/
    shutil.copy2(wav_master, RENDERS_DIR / "KoiSync_AI中文配音.wav")
    srt_txt = "".join(
        f"{i+1}\n{srt_time(e['start'])} --> {srt_time(e['end'])}\n{e['subtitle']}\n\n"
        for i, e in enumerate(entries)
    )
    (RENDERS_DIR / "KoiSync_中文字幕.srt").write_text(srt_txt, encoding="utf-8-sig")

    # Build contiguous slide timeline
    slide_order = ["slide_01", "slide_02", "slide_03", "slide_04", "slide_05", "slide_06", "slide_07"]
    contiguous_slides = []
    prev_end = 0.0
    for i, sid in enumerate(slide_order):
        s_end = total_dur if i == len(slide_order) - 1 else round(slide_spans[sid][1] - 0.15, 3)
        contiguous_slides.append({
            "id": sid,
            "start": round(prev_end, 3),
            "end": round(s_end, 3),
            "duration": round(s_end - prev_end, 3),
        })
        prev_end = s_end

    manifest = {
        "sample_rate": 48000,
        "total_duration": total_dur,
        "speaker": f"{VOICE} ({RATE})",
        "slides": contiguous_slides,
        "subtitles": entries,
    }
    (BUILD_DIR / "timeline_manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"[XIAOXIAO] DONE in {time.perf_counter()-t0:.1f}s! Total duration = {total_dur:.2f}s ({total_dur/60:.2f} min)", flush=True)
    for sl in contiguous_slides:
        print(f"  SLIDE {sl['id']}: {sl['start']:6.2f}s -> {sl['end']:6.2f}s ({sl['duration']:5.2f}s)", flush=True)


if __name__ == "__main__":
    asyncio.run(synth_all())
