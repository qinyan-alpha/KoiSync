#!/usr/bin/env python3
"""
Builds the clean, typography-first Apple/Linear-style 1920x1080 @ 60fps
KoiSync Project Showcase Video using ONLY:
  - Backdrop: 06_Act4_KK_CampusDaily_1080p60.mp4 (softly blurred & darkened sunlit campus, top-cropped clean)
  - Single Robot Demo: 04_Act2_SplitSync_CubeRod_VEX6_1080p60.mp4 (framed inside Slides 02, 03, 06)
  - Narrator Audio: renders/_build/voiceover_master.wav + timeline_manifest.json

Generates:
  - renders/KoiSync_Project_Showcase_1080p60.mp4         (Full narrated 1080p60 H.264 MP4)
  - renders/KoiSync_Project_Showcase_Silent_1080p60.mp4  (Clean silent track if user wants to record own voice)
"""
from __future__ import annotations

import json
import pathlib
import subprocess
import sys
import time

from PIL import Image, ImageDraw, ImageFont

RENDERS_DIR = pathlib.Path(r"E:\project\c#\KoiSync\blender_sr6_sim\renders")
BUILD_DIR = RENDERS_DIR / "_build"
SLIDES_DIR = BUILD_DIR / "slides"
SUBS_DIR = BUILD_DIR / "subs"
SLIDES_DIR.mkdir(parents=True, exist_ok=True)
SUBS_DIR.mkdir(parents=True, exist_ok=True)

CAMPUS_MP4 = RENDERS_DIR / "06_Act4_KK_CampusDaily_1080p60.mp4"
VEX6_MP4 = RENDERS_DIR / "04_Act2_SplitSync_CubeRod_VEX6_1080p60.mp4"
VOICE_WAV = BUILD_DIR / "voiceover_master.wav"
MANIFEST_JSON = BUILD_DIR / "timeline_manifest.json"

FONT_BOLD = r"C:\Windows\Fonts\msyhbd.ttc"
FONT_REG = r"C:\Windows\Fonts\msyh.ttc"
FONT_MONO = r"C:\Windows\Fonts\consolab.ttf"

W, H = 1920, 1080

# Unified Engineering Palette (matching VEX-6 ceramic white + cyan + safety orange)
C_GLASS = (11, 16, 26, 212)
C_GLASS_HI = (17, 25, 40, 228)
C_GLASS_SUB = (8, 12, 20, 222)
C_BORDER = (110, 210, 255, 76)
C_BORDER_STRONG = (72, 214, 255, 190)
C_ORANGE_BORDER = (255, 136, 56, 170)
C_CYAN = (72, 214, 255, 255)
C_ORANGE = (255, 136, 56, 255)
C_WHITE = (246, 250, 255, 255)
C_MUTED = (182, 198, 220, 255)
C_DIM = (128, 146, 172, 255)

# Exact PIP video window boxes for slides that embed the single VEX-6 demo clip: (x, y, w, h, src_seek_s)
VEX6_BOXES = {
    "slide_02": (992, 196, 840, 472, 1.0),
    "slide_03": (744, 174, 1088, 612, 0.0),
    "slide_06": (916, 194, 916, 516, 16.0),
}


def f_bold(sz: int):
    return ImageFont.truetype(FONT_BOLD, sz)


def f_reg(sz: int):
    return ImageFont.truetype(FONT_REG, sz)


def f_mono(sz: int):
    return ImageFont.truetype(FONT_MONO, sz)


def rrect(draw: ImageDraw.ImageDraw, box, radius=18, fill=C_GLASS, outline=C_BORDER, width=2):
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)


def wrap_text(draw: ImageDraw.ImageDraw, text: str, font, max_w: int):
    lines = []
    for para in text.split("\n"):
        cur = ""
        for ch in para:
            cand = cur + ch
            if draw.textlength(cand, font=font) <= max_w:
                cur = cand
            else:
                if cur:
                    lines.append(cur)
                cur = ch
        if cur:
            lines.append(cur)
    return lines


def draw_wrapped(draw: ImageDraw.ImageDraw, x: int, y: int, text: str, font, max_w: int, fill=C_MUTED, line_gap=10):
    lines = wrap_text(draw, text, font, max_w)
    cy = y
    for ln in lines:
        draw.text((x, cy), ln, font=font, fill=fill)
        bbox = draw.textbbox((x, cy), ln, font=font)
        cy = bbox[3] + line_gap
    return cy


def draw_pill(draw: ImageDraw.ImageDraw, x: int, y: int, text: str, font=None, accent=C_CYAN, fill=(16, 28, 44, 225)):
    font = font or f_bold(22)
    tw = draw.textlength(text, font=font)
    pw = int(tw + 38)
    ph = 44
    rrect(draw, (x, y, x + pw, y + ph), radius=22, fill=fill, outline=(accent[0], accent[1], accent[2], 145), width=2)
    draw.text((x + 19, y + 8), text, font=font, fill=C_WHITE)
    return x + pw + 14


def draw_header(draw: ImageDraw.ImageDraw, slide_num: int, total: int, section_tag: str, add_scrim: bool = True):
    # Subtle full-screen dark scrim so foreground typography & cyan subtitles stay 100% razor-legible over sunlit campus!
    if add_scrim:
        draw.rectangle((0, 0, W, H), fill=(6, 10, 18, 92))
    # Subtle top bar
    rrect(draw, (76, 36, W - 76, 94), radius=14, fill=(10, 15, 24, 198), outline=(120, 200, 255, 58), width=1)
    # Glowing indicator dot
    draw.ellipse((102, 59, 114, 71), fill=C_CYAN)
    draw.text((128, 52), "KOISYNC", font=f_mono(24), fill=C_WHITE)
    draw.text((242, 54), "//  " + section_tag, font=f_reg(20), fill=C_MUTED)

    # Progress bars on top right
    bar_w, bar_gap = 26, 8
    total_bw = total * bar_w + (total - 1) * bar_gap
    bx0 = W - 104 - total_bw
    for i in range(1, total + 1):
        bx = bx0 + (i - 1) * (bar_w + bar_gap)
        col = C_CYAN if i == slide_num else ((95, 165, 205, 130) if i < slide_num else (90, 105, 130, 80))
        draw.rounded_rectangle((bx, 62, bx + bar_w, 69), radius=3, fill=col)
    num_str = f"{slide_num:02d} / {total:02d}"
    nw = draw.textlength(num_str, font=f_mono(20))
    draw.text((bx0 - nw - 22, 54), num_str, font=f_mono(20), fill=C_CYAN)


def draw_video_frame_chrome(draw: ImageDraw.ImageDraw, box, label_left: str, label_right: str):
    x, y, w, h, _ = box
    # Punch 100% transparent hole so VEX-6 PIP video shines at full crystal-clear brightness!
    draw.rectangle((x, y, x + w, y + h), fill=(0, 0, 0, 0))
    # Crisp glowing outer frame around transparent PIP hole
    draw.rounded_rectangle((x - 4, y - 4, x + w + 4, y + h + 4), radius=14, fill=None, outline=C_BORDER_STRONG, width=3)
    # Sleek caption footer card directly below the video window
    fy = y + h + 14
    rrect(draw, (x - 2, fy, x + w + 2, fy + 52), radius=10, fill=(10, 16, 26, 224), outline=C_BORDER, width=1)
    draw.ellipse((x + 18, fy + 20, x + 28, fy + 30), fill=C_ORANGE)
    draw.text((x + 40, fy + 12), label_left, font=f_bold(20), fill=C_WHITE)
    rw = draw.textlength(label_right, font=f_mono(18))
    draw.text((x + w - rw - 18, fy + 15), label_right, font=f_mono(18), fill=C_CYAN)


def make_slide_01() -> Image.Image:
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    draw_header(d, 1, 7, "项目概览 · PROJECT OVERVIEW")

    # Central clean hero glass card
    rrect(d, (112, 186, W - 112, 830), radius=26, fill=(10, 15, 25, 206), outline=C_BORDER, width=2)
    # Subtle top orange/cyan dual accent stripe
    d.rounded_rectangle((164, 240, 244, 247), radius=3, fill=C_CYAN)
    d.rounded_rectangle((254, 240, 296, 247), radius=3, fill=C_ORANGE)
    d.text((164, 264), "KOISYNC · 6-DOF MOTION & AI MCP ARCHITECTURE", font=f_mono(22), fill=C_CYAN)

    d.text((164, 326), "恋活实时六轴同步与 AI 控制方案", font=f_bold(68), fill=C_WHITE)
    d.text((164, 424), "正向实时驱动桌面多轴外设  ·  SR6 反向运动控制角色动作  ·  开放标准 AI MCP 接口", font=f_reg(30), fill=C_MUTED)

    # 4 crisp pillar cards inside hero
    pillars = [
        ("01", "实时六轴联动", "无需手工预制轨迹脚本\n逐帧解算 L0–L2 / R0–R2", C_CYAN),
        ("02", "SR6 反向驱动", "通过六轴运动轨迹反驱\n扩展恋活本体原生动作", C_ORANGE),
        ("03", "Studio Timeline", "兼容社区任意场景卡\n自动追踪并保护物理行程", C_CYAN),
        ("04", "AI 语音 & MCP", "支持切换动作 / 换人 / 换装\n任意大模型 Agent 直连", C_ORANGE),
    ]
    cw, ch, gap = 368, 224, 24
    x0 = 164
    y0 = 516
    for idx, (num, title, desc, acc) in enumerate(pillars):
        cx = x0 + idx * (cw + gap)
        rrect(d, (cx, y0, cx + cw, y0 + ch), radius=18, fill=C_GLASS_HI, outline=(acc[0], acc[1], acc[2], 110), width=2)
        d.text((cx + 26, y0 + 22), num, font=f_mono(24), fill=acc)
        d.text((cx + 26, y0 + 62), title, font=f_bold(30), fill=C_WHITE)
        draw_wrapped(d, cx + 26, y0 + 116, desc, f_reg(22), cw - 48, fill=C_MUTED, line_gap=8)

    return im


def make_slide_02() -> Image.Image:
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    draw_header(d, 2, 7, "桌面外设分类 · SINGLE-AXIS VS 6-DOF")

    # Left column header
    d.text((88, 134), "桌面同步外设分类", font=f_bold(46), fill=C_WHITE)
    d.text((88, 198), "单轴线性往复  vs  六自由度空间复合运动", font=f_reg(24), fill=C_CYAN)

    # Card 1: Single Axis (Handy)
    rrect(d, (88, 256, 940, 464), radius=20, fill=C_GLASS, outline=C_BORDER, width=2)
    d.text((120, 282), "01 · 单轴同步外设", font=f_bold(32), fill=C_WHITE)
    draw_pill(d, 430, 280, "典型代表：Handy", f_bold(20), accent=C_DIM)
    draw_wrapped(d, 120, 344, "仅控制单一竖直方向上下往复（L0 轴），结构简单，适合基础节奏同步。", f_reg(24), 795, fill=C_MUTED, line_gap=8)
    draw_pill(d, 120, 404, "L0 · 上下行程 (Stroke)", f_bold(20), accent=C_CYAN)

    # Card 2: Six Axis (SR6)
    rrect(d, (88, 492, 940, 822), radius=20, fill=C_GLASS_HI, outline=C_ORANGE_BORDER, width=2)
    d.text((120, 518), "02 · 六轴机械外设", font=f_bold(32), fill=C_WHITE)
    draw_pill(d, 430, 516, "典型代表：SR6 / OSR", f_bold(20), accent=C_ORANGE)
    draw_wrapped(d, 120, 580, "具备完整的三维空间平移与三维姿态旋转能力，同步还原复杂立体轨迹。", f_reg(24), 790, fill=C_MUTED, line_gap=8)

    px = 120
    for tag in ("L0 上下", "L1 前后", "L2 左右"):
        px = draw_pill(d, px, 654, tag, f_bold(21), accent=C_CYAN)
    px = 120
    for tag in ("R0 旋转", "R1 侧倾", "R2 俯仰"):
        px = draw_pill(d, px, 712, tag, f_bold(21), accent=C_ORANGE)
    d.text((120, 774), "* 合规演示说明：右侧由工业六轴机械臂（VEX-6）承接真实六轴信号实时展示", font=f_reg(19), fill=C_DIM)

    # Right PIP Video Chrome
    draw_video_frame_chrome(d, VEX6_BOXES["slide_02"], "VEX-6 六轴工业机械臂实时演示", "REAL 6-DOF SIGNAL · 60 FPS")
    return im


def make_slide_03() -> Image.Image:
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    draw_header(d, 3, 7, "恋活本体双向联动 · REAL-TIME SYNC & REVERSE CONTROL")

    d.text((88, 130), "恋活本体双向实时联动", font=f_bold(44), fill=C_WHITE)
    d.text((88, 190), "无预制脚本 · 正向驱动硬件 · SR6 反向控制角色", font=f_reg(22), fill=C_CYAN)

    cards = [
        ("01", "正向实时六轴解算", "直接读取游戏空间位移与姿态，每帧实时换算六轴控制指令，切换动作无需重新手写脚本。", C_CYAN),
        ("02", "SR6 反向控制角色动作", "支持以 SR6 六轴运动轨迹反向驱动恋活本体角色姿态，突破原生动画固定速度循环。", C_ORANGE),
        ("03", "全轴解耦与动作扩展", "实时叠加定深微动、快慢变速与多轴复合波形，显著扩展本体可玩深度。", C_CYAN),
    ]
    cy = 246
    for num, title, body, acc in cards:
        rrect(d, (88, cy, 704, cy + 186), radius=18, fill=C_GLASS_HI, outline=(acc[0], acc[1], acc[2], 125), width=2)
        d.text((116, cy + 20), num, font=f_mono(22), fill=acc)
        d.text((160, cy + 16), title, font=f_bold(28), fill=C_WHITE)
        draw_wrapped(d, 116, cy + 68, body, f_reg(22), 560, fill=C_MUTED, line_gap=8)
        cy += 206

    draw_video_frame_chrome(d, VEX6_BOXES["slide_03"], "左：空间参考几何体  ｜  右：VEX-6 六轴机械臂 1:1 零延迟咬合", "FRAME-LOCKED 6DOF · 0ms")
    return im


def make_slide_04() -> Image.Image:
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    draw_header(d, 4, 7, "STUDIO 场景卡适配 · TIMELINE AUTO-ADAPTATION")

    d.text((104, 138), "任意 Studio 场景卡 Timeline 自动适配", font=f_bold(52), fill=C_WHITE)
    d.text((104, 210), "社区现成场景卡无需作者额外适配，直接沿 Timeline 轨迹实时输出多轴信号", font=f_reg(26), fill=C_CYAN)

    # 3-stage architecture flow diagram (Y = 286 .. 550)
    stages = [
        ("INPUT", "任意 Studio 场景卡", "包含 Timeline 动画轨道\n不要求原作者预留外设数据", C_CYAN),
        ("CORE ENGINE", "KoiSync.Studio 自动解算", "选定参考目标 · 实时采样\n自动章节过渡与平滑滤波", C_ORANGE),
        ("HARDWARE", "SR6 / Handy 实时输出", "物理安全行程自动保护\nUDP / 串口 / Intiface 直连", C_CYAN),
    ]
    sw, sh = 500, 246
    gap = 106
    sx0 = 104
    sy = 288
    for i, (tag, title, desc, acc) in enumerate(stages):
        sx = sx0 + i * (sw + gap)
        rrect(d, (sx, sy, sx + sw, sy + sh), radius=22, fill=C_GLASS_HI, outline=(acc[0], acc[1], acc[2], 150), width=2)
        d.text((sx + 32, sy + 26), tag, font=f_mono(20), fill=acc)
        d.text((sx + 32, sy + 64), title, font=f_bold(34), fill=C_WHITE)
        draw_wrapped(d, sx + 32, sy + 124, desc, f_reg(24), sw - 64, fill=C_MUTED, line_gap=10)
        # Arrow connector to next box
        if i < 2:
            ax1 = sx + sw + 16
            ax2 = sx + sw + gap - 18
            ay = sy + sh // 2
            d.line((ax1, ay, ax2 - 12, ay), fill=C_CYAN, width=4)
            d.polygon([(ax2, ay), (ax2 - 18, ay - 11), (ax2 - 18, ay + 11)], fill=C_CYAN)

    # Bottom 3 engineering guarantee cards (Y = 582 .. 830)
    specs = [
        ("零手工打轴成本", "导入即用，自动顺着 Timeline 每一帧生成实时六轴轨迹，也可一键导出标准 6 轴 .funscript。"),
        ("物理行程安全保护", "针对 Studio 夸张镜头与大位移自动完成比例缩放与限速保护，兼顾立体体感与硬件安全。"),
        ("多协议硬件通用", "同一套实时解算内核同时覆盖 SR6 六轴机械结构与 Handy 单轴线性外设。"),
    ]
    bw, bh, bgap = 552, 232, 28
    for i, (title, body) in enumerate(specs):
        bx = 104 + i * (bw + bgap)
        rrect(d, (bx, 584, bx + bw, 584 + bh), radius=18, fill=C_GLASS, outline=C_BORDER, width=2)
        d.rounded_rectangle((bx + 28, 614, bx + 36, 650), radius=4, fill=C_ORANGE if i == 1 else C_CYAN)
        d.text((bx + 50, 612), title, font=f_bold(28), fill=C_WHITE)
        draw_wrapped(d, bx + 28, 670, body, f_reg(22), bw - 56, fill=C_MUTED, line_gap=10)

    return im


def make_slide_05() -> Image.Image:
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    draw_header(d, 5, 7, "AI 实时语音 & 开放 MCP 接口 · AI VOICE & MCP ARCHITECTURE")

    d.text((104, 134), "AI 实时语音控制与开放 MCP 接口", font=f_bold(52), fill=C_WHITE)
    d.text((104, 206), "将恋活本体控制与 SR6 六轴运动完整封装为标准 MCP 工具能力", font=f_reg(26), fill=C_CYAN)

    # Top 3 MCP capability cards (strictly technical, zero AI chat bubbles!)
    caps = [
        ("MCP CAPABILITY 01", "切换恋活本体动作", "通过语音或 MCP 接口，直接切换本体动作、节奏档位与播放阶段，无需手动翻找菜单。", C_CYAN),
        ("MCP CAPABILITY 02", "实时换人与换衣服", "开放角色与服装控制接口，支持按指令实时切换场景角色以及调整角色服装穿脱状态。", C_ORANGE),
        ("MCP CAPABILITY 03", "SR6 运动范围反向扩展", "直接调用 SR6 完整六轴物理行程与数学曲线，反向驱动并扩展恋活本体的原生动作表现。", C_CYAN),
    ]
    cw, ch, gap = 552, 290, 28
    for i, (tag, title, body, acc) in enumerate(caps):
        cx = 104 + i * (cw + gap)
        rrect(d, (cx, 276, cx + cw, 276 + ch), radius=20, fill=C_GLASS_HI, outline=(acc[0], acc[1], acc[2], 150), width=2)
        d.text((cx + 30, 304), tag, font=f_mono(19), fill=acc)
        d.text((cx + 30, 342), title, font=f_bold(32), fill=C_WHITE)
        draw_wrapped(d, cx + 30, 404, body, f_reg(23), cw - 60, fill=C_MUTED, line_gap=10)

    # Bottom standard MCP protocol bus diagram
    rrect(d, (104, 608, W - 104, 836), radius=22, fill=C_GLASS, outline=C_BORDER_STRONG, width=2)
    d.text((140, 634), "STANDARD MCP PROTOCOL BUS  ·  开放接入架构", font=f_bold(21), fill=C_CYAN)

    nodes = [
        (140, 684, 580, 796, "内置语音 / 任意 MCP 客户端", "本地语音识别 · 外部大模型 · Agent", C_CYAN),
        (680, 684, 1140, 796, "KoiSync AI MCP 接口层", "状态读取 · 动作/换人/换装 · 六轴曲线", C_ORANGE),
        (1240, 684, 1780, 796, "恋活本体 & 六轴外设", "角色姿态反向控制 · SR6 / Handy 联动", C_CYAN),
    ]
    for idx, (x1, y1, x2, y2, t1, t2, acc) in enumerate(nodes):
        rrect(d, (x1, y1, x2, y2), radius=16, fill=C_GLASS_HI, outline=(acc[0], acc[1], acc[2], 160), width=2)
        d.text((x1 + 24, y1 + 20), t1, font=f_bold(26), fill=C_WHITE)
        d.text((x1 + 24, y1 + 64), t2, font=f_reg(20), fill=C_MUTED)
        if idx < 2:
            ax1 = x2 + 14
            ax2 = nodes[idx + 1][0] - 14
            ay = (y1 + y2) // 2
            d.line((ax1, ay, ax2 - 12, ay), fill=C_CYAN, width=4)
            d.polygon([(ax2, ay), (ax2 - 16, ay - 10), (ax2 - 16, ay + 10)], fill=C_CYAN)

    return im


def make_slide_06() -> Image.Image:
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    draw_header(d, 6, 7, "六轴数学轨迹引擎 · 6-DOF MATH MANIFOLD ENGINE")

    d.text((88, 130), "六轴数学轨迹引擎", font=f_bold(46), fill=C_WHITE)
    d.text((88, 192), "以解析数学流形释放六自由度极限，支持实时热切换与反向驱动", font=f_reg(22), fill=C_CYAN)

    manifolds = [
        ("01", "虚拟支点行波", "Virtual Pivot Traveling Wave · 支点沿轴线游走", C_CYAN),
        ("02", "变螺距双螺旋", "Variable-Pitch Double Helix · 升降与扭转动态耦合", C_ORANGE),
        ("03", "6D 黄金比例利萨如", "Golden-Ratio 6D Lissajous · 无理数频率永不重复", C_CYAN),
        ("04", "潮汐拍频微颤", "Tidal Beat-Frequency Flutter · 宏观起伏叠加高频微震", C_ORANGE),
        ("05", "平滑混沌吸引子", "Bounded Smooth Chaos · 零均值平滑非周期演化", C_CYAN),
    ]
    ry = 248
    for num, name, sub, acc in manifolds:
        rrect(d, (88, ry, 874, ry + 104), radius=16, fill=C_GLASS_HI, outline=(acc[0], acc[1], acc[2], 120), width=2)
        d.text((116, ry + 18), num, font=f_mono(24), fill=acc)
        d.text((166, ry + 14), name, font=f_bold(28), fill=C_WHITE)
        d.text((166, ry + 58), sub, font=f_reg(20), fill=C_MUTED)
        ry += 118

    draw_video_frame_chrome(d, VEX6_BOXES["slide_06"], "六轴复合曲线实时映射（定深微动 / 变速 / 螺旋波）", "C2 CONTINUOUS · 6-DOF")
    return im


def make_slide_07() -> Image.Image:
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    draw_header(d, 7, 7, "方案总结 · ARCHITECTURE SUMMARY")

    rrect(d, (160, 210, W - 160, 810), radius=28, fill=(10, 15, 25, 216), outline=C_BORDER_STRONG, width=2)
    d.text((W // 2 - draw_w(d, "KOISYNC // FULL-STACK SYSTEM", f_mono(22)) // 2, 276), "KOISYNC // FULL-STACK SYSTEM", font=f_mono(22), fill=C_CYAN)

    title = "恋活实时六轴同步与 AI 控制方案"
    d.text((W // 2 - draw_w(d, title, f_bold(62)) // 2, 332), title, font=f_bold(62), fill=C_WHITE)

    sub = "恋活本体双向扩展  ·  Studio Timeline 全场景适配  ·  AI 实时语音 & 开放 MCP 接口"
    d.text((W // 2 - draw_w(d, sub, f_reg(28)) // 2, 430), sub, font=f_reg(28), fill=C_MUTED)

    # Clean 4 summary badges
    items = [
        ("本体实时联动", "正向六轴 + SR6 反向控制"),
        ("Studio 通用适配", "任意 Timeline 场景卡"),
        ("AI MCP 开放协议", "动作 / 换人 / 换装 / 六轴"),
        ("数学轨迹引擎", "五大 6-DOF 连续流形"),
    ]
    bw, bh, gap = 344, 150, 26
    total_w = 4 * bw + 3 * gap
    bx0 = (W - total_w) // 2
    by = 538
    for idx, (k, v) in enumerate(items):
        bx = bx0 + idx * (bw + gap)
        acc = C_CYAN if idx % 2 == 0 else C_ORANGE
        rrect(d, (bx, by, bx + bw, by + bh), radius=18, fill=C_GLASS_HI, outline=(acc[0], acc[1], acc[2], 140), width=2)
        d.text((bx + 26, by + 32), k, font=f_bold(26), fill=C_WHITE)
        d.text((bx + 26, by + 84), v, font=f_reg(21), fill=acc)

    return im


def draw_w(d: ImageDraw.ImageDraw, txt: str, font) -> int:
    return int(d.textlength(txt, font=font))


def make_subtitle_strip(text: str) -> Image.Image:
    """Renders a crisp 1920x140 RGBA bottom subtitle pill centered cleanly."""
    im = Image.new("RGBA", (W, 140), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    font = f_bold(32)
    lines = wrap_text(d, text, font, max_w=1620)
    if len(lines) > 2:
        font = f_bold(28)
        lines = wrap_text(d, text, font, max_w=1680)
    max_lw = max(int(d.textlength(ln, font=font)) for ln in lines)
    line_h = 42 if len(lines) <= 2 else 36
    pad_x, pad_y = 34, 16
    box_w = max_lw + pad_x * 2
    box_h = len(lines) * line_h + pad_y * 2
    x1 = (W - box_w) // 2
    y1 = (140 - box_h) // 2
    rrect(d, (x1, y1, x1 + box_w, y1 + box_h), radius=16, fill=C_GLASS_SUB, outline=(110, 210, 255, 105), width=2)
    cy = y1 + pad_y
    for ln in lines:
        lw = int(d.textlength(ln, font=font))
        d.text(((W - lw) // 2, cy), ln, font=font, fill=C_WHITE)
        cy += line_h
    return im


def generate_all_assets(manifest: dict):
    makers = {
        "slide_01": make_slide_01,
        "slide_02": make_slide_02,
        "slide_03": make_slide_03,
        "slide_04": make_slide_04,
        "slide_05": make_slide_05,
        "slide_06": make_slide_06,
        "slide_07": make_slide_07,
    }
    for sid, fn in makers.items():
        img = fn()
        img.save(SLIDES_DIR / f"{sid}.png")
    for sub in manifest["subtitles"]:
        idx = sub["index"]
        img = make_subtitle_strip(sub["subtitle"])
        img.save(SUBS_DIR / f"sub_{idx:02d}.png")
    print(f"[ASSETS] Generated 7 keynote slide PNGs + {len(manifest['subtitles'])} subtitle PNGs.", flush=True)


def render_preview_composites(manifest: dict):
    """Generates 1 realistic preview PNG per slide over the real blurred campus + VEX-6 frames."""
    prev_dir = BUILD_DIR / "previews"
    prev_dir.mkdir(parents=True, exist_ok=True)
    campus_frame = prev_dir / "_campus_bg.png"
    vex6_frame = prev_dir / "_vex6_fg.png"
    # Extract clean top-cropped blurred campus background frame (hides bottom Koikatsu ADV dialog box!)
    subprocess.run([
        "ffmpeg", "-y", "-v", "error", "-ss", "00:00:18.0", "-i", str(CAMPUS_MP4),
        "-vf", "crop=1920:840:0:0,scale=1920:1080:flags=lanczos,gblur=sigma=12,eq=brightness=-0.22:saturation=0.82",
        "-frames:v", "1", str(campus_frame)
    ], check=True)
    subprocess.run([
        "ffmpeg", "-y", "-v", "error", "-ss", "00:00:16.0", "-i", str(VEX6_MP4),
        "-frames:v", "1", str(vex6_frame)
    ], check=True)

    bg_base = Image.open(campus_frame).convert("RGBA")
    vex_base = Image.open(vex6_frame).convert("RGBA")

    for sl in manifest["slides"]:
        sid = sl["id"]
        canvas = bg_base.copy()
        if sid in VEX6_BOXES:
            x, y, w, h, _ = VEX6_BOXES[sid]
            pip = vex_base.resize((w, h), Image.Resampling.LANCZOS)
            canvas.paste(pip, (x, y))
        slide_ov = Image.open(SLIDES_DIR / f"{sid}.png").convert("RGBA")
        canvas.alpha_composite(slide_ov, (0, 0))
        # Overlay first subtitle belonging to this slide
        first_sub = next((s for s in manifest["subtitles"] if s["slide"] == sid), None)
        if first_sub:
            sub_ov = Image.open(SUBS_DIR / f"sub_{first_sub['index']:02d}.png").convert("RGBA")
            canvas.alpha_composite(sub_ov, (0, H - 150))
        out_p = prev_dir / f"preview_{sid}.png"
        canvas.convert("RGB").save(out_p, quality=92)
    print(f"[PREVIEW] Saved 7 slide previews to {prev_dir}", flush=True)


def render_final_videos(manifest: dict):
    total_dur = float(manifest["total_duration"])
    out_voiced = RENDERS_DIR / "KoiSync_Project_Showcase_1080p60.mp4"
    out_silent = RENDERS_DIR / "KoiSync_Project_Showcase_Silent_1080p60.mp4"

    # Build clean FFmpeg command:
    # Input 0: Looped campus background (cropped top 840px to completely exclude Koikatsu ADV dialog box, scaled 1080p, gently blurred & darkened)
    # Input 1: Looped single VEX-6 demo clip (04_Act2_SplitSync_CubeRod_VEX6_1080p60.mp4)
    # Inputs 2..8: 7 Keynote Slide PNG overlays
    # Inputs 9..N: 23 Subtitle RGBA strip PNG overlays
    # Last Input: voiceover_master.wav
    cmd = [
        "ffmpeg", "-y", "-v", "warning",
        "-stream_loop", "-1", "-i", str(CAMPUS_MP4),
        "-stream_loop", "-1", "-i", str(VEX6_MP4),
    ]
    slide_order = [s["id"] for s in manifest["slides"]]
    for sid in slide_order:
        cmd.extend(["-i", str(SLIDES_DIR / f"{sid}.png")])
    for sub in manifest["subtitles"]:
        cmd.extend(["-i", str(SUBS_DIR / f"sub_{sub['index']:02d}.png")])
    audio_inp_idx = 2 + len(slide_order) + len(manifest["subtitles"])
    cmd.extend(["-i", str(VOICE_WAV)])

    fc = []
    # Base background stream (exact duration, 60fps, soft blurred dark sunlit campus — zero game UI box!)
    fc.append(
        f"[0:v]trim=0:{total_dur:.3f},setpts=PTS-STARTPTS,"
        f"crop=1920:840:0:0,scale=1920:1080:flags=lanczos,"
        f"gblur=sigma=12,eq=brightness=-0.22:saturation=0.82,fps=60,format=rgba[bg0]"
    )
    # Split VEX-6 single source into 3 scaled PIP streams for slide_02, slide_03, slide_06
    fc.append("[1:v]fps=60,format=rgba,split=3[v2src][v3src][v6src]")

    slide_map = {s["id"]: s for s in manifest["slides"]}
    cur = "bg0"
    for tag, sid in (("v2", "slide_02"), ("v3", "slide_03"), ("v6", "slide_06")):
        x, y, w, h, seek_s = VEX6_BOXES[sid]
        st, en = slide_map[sid]["start"], slide_map[sid]["end"]
        fc.append(f"[{tag}src]setpts=PTS-STARTPTS,scale={w}:{h}:flags=lanczos[{tag}pip]")
        nxt = f"after_{tag}"
        fc.append(f"[{cur}][{tag}pip]overlay=x={x}:y={y}:enable='between(t,{st:.3f},{en:.3f})':shortest=1[{nxt}]")
        cur = nxt

    # Overlay 7 Keynote Slide RGBA layers cleanly on exact chapter spans
    for i, sl in enumerate(manifest["slides"]):
        inp_i = 2 + i
        st, en = sl["start"], sl["end"]
        nxt = f"sl_{i}"
        fc.append(f"[{cur}][{inp_i}:v]overlay=x=0:y=0:enable='between(t,{st:.3f},{en:.3f})'[{nxt}]")
        cur = nxt

    # Overlay 23 Subtitle RGBA strips cleanly on exact sentence timestamps
    sub_base_idx = 2 + len(slide_order)
    for j, sub in enumerate(manifest["subtitles"]):
        inp_j = sub_base_idx + j
        st, en = sub["start"], sub["end"]
        nxt = f"sub_{j}"
        fc.append(f"[{cur}][{inp_j}:v]overlay=x=0:y={H - 150}:enable='between(t,{st:.3f},{en:.3f})'[{nxt}]")
        cur = nxt

    # Subtle 0.4s fade-in at start and 0.9s fade-out at end
    fade_out_st = max(0.0, total_dur - 0.9)
    fc.append(f"[{cur}]format=yuv420p,fade=t=in:st=0:d=0.4,fade=t=out:st={fade_out_st:.3f}:d=0.9[vout]")

    filter_script = BUILD_DIR / "filter_complex.txt"
    filter_script.write_text(";\n".join(fc), encoding="utf-8")

    cmd.extend([
        "-filter_complex_script", str(filter_script),
        "-map", "[vout]",
        "-map", f"{audio_inp_idx}:a",
        "-t", f"{total_dur:.3f}",
        "-c:v", "h264_nvenc", "-preset", "p4", "-cq", "18",
        "-c:a", "aac", "-b:a", "192k", "-ar", "48000",
        "-pix_fmt", "yuv420p", "-movflags", "+faststart",
        str(out_voiced),
    ])

    t0 = time.perf_counter()
    print(f"[VIDEO] Compositing {out_voiced.name} ({total_dur:.2f}s @ 1080p60 NVENC)...", flush=True)
    subprocess.run(cmd, check=True)
    dt = time.perf_counter() - t0
    print(f"[VIDEO] DONE {out_voiced.name} ({out_voiced.stat().st_size / 1e6:.2f} MB) in {dt:.1f}s!", flush=True)

    # Also produce zero-reencode stream-copied silent version in 0.5s (for optional self-voiceover!)
    subprocess.run([
        "ffmpeg", "-y", "-v", "error", "-i", str(out_voiced), "-an", "-c:v", "copy", "-movflags", "+faststart", str(out_silent)
    ], check=True)
    print(f"[VIDEO] DONE {out_silent.name} ({out_silent.stat().st_size / 1e6:.2f} MB)", flush=True)


def main():
    manifest = json.loads(MANIFEST_JSON.read_text(encoding="utf-8"))
    mode = sys.argv[1] if len(sys.argv) > 1 else "all"
    generate_all_assets(manifest)
    if mode in ("preview", "all"):
        render_preview_composites(manifest)
    if mode in ("render", "all"):
        render_final_videos(manifest)


if __name__ == "__main__":
    main()
