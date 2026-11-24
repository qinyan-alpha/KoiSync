from __future__ import annotations

import hashlib
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


OUTPUT = Path(r"E:\project\c#\KoiSync\artifacts\scene-preview\v036-thrust-20261024-v0135-capture")
REPORT = OUTPUT / "runtime.json"


def font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    for path in (r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\segoeui.ttf"):
        try:
            return ImageFont.truetype(path, size=size)
        except OSError:
            pass
    return ImageFont.load_default()


def phase_label(value: str) -> str:
    return {
        "shallow_start": "浅入起点",
        "inward": "向内",
        "deepest": "最深点",
        "outward": "回程",
        "shallow_return": "浅入返回",
    }[value]


def contact_sheet(frames: list[dict]) -> Path:
    columns, rows = 4, 2
    cell_w, cell_h = 420, 450
    canvas = Image.new("RGB", (columns * cell_w, rows * cell_h), (17, 20, 27))
    draw = ImageDraw.Draw(canvas)
    title_font, label_font = font(25), font(19)
    for index, frame in enumerate(frames):
        col, row = index % columns, index // columns
        left, top = col * cell_w, row * cell_h
        image = Image.open(Path(frame["contact_path"])).convert("RGB")
        image = image.resize((400, 400), Image.Resampling.LANCZOS)
        canvas.paste(image, (left + 10, top + 42))
        axes = frame["script_axes_percent"]
        heading = f"{index + 1}  {frame['time']:.3f}s  {phase_label(frame['phase'])}"
        detail = f"L0 {axes[0]:02d}%   L1 {axes[1]:02d}%   L2 {axes[2]:02d}%   R2 {axes[5]:02d}%"
        draw.text((left + 10, top + 5), heading, font=title_font, fill=(245, 245, 250))
        draw.rectangle((left + 10, top + 409, left + 410, top + 442), fill=(11, 14, 20))
        draw.text((left + 20, top + 413), detail, font=label_font, fill=(174, 222, 238))
    path = OUTPUT / "contact_cycle_7frames.png"
    canvas.save(path, optimize=True)
    return path


def context_strip(frames: list[dict]) -> Path:
    selected = (frames[0], frames[3], frames[6])
    cell_w, image_h, header_h = 600, 338, 55
    canvas = Image.new("RGB", (cell_w * 3, image_h + header_h), (17, 20, 27))
    draw = ImageDraw.Draw(canvas)
    title_font, detail_font = font(24), font(18)
    for index, frame in enumerate(selected):
        left = index * cell_w
        image = Image.open(Path(frame["context_path"])).convert("RGB")
        image = image.resize((cell_w, image_h), Image.Resampling.LANCZOS)
        canvas.paste(image, (left, header_h))
        axes = frame["script_axes_percent"]
        draw.text((left + 12, 4), f"{frame['time']:.3f}s  {phase_label(frame['phase'])}",
                  font=title_font, fill=(245, 245, 250))
        draw.text((left + 12, 31), f"L0={axes[0]}%  R2={axes[5]}%  fixed rod=178 mm",
                  font=detail_font, fill=(174, 222, 238))
    path = OUTPUT / "context_keyframes_3frames.png"
    canvas.save(path, optimize=True)
    return path


def write_manifest(paths: list[Path]) -> Path:
    rows = []
    for path in paths:
        data = path.read_bytes()
        rows.append(f"{hashlib.sha256(data).hexdigest().upper()}  {path.name}  {len(data)} bytes")
    path = OUTPUT / "screenshots.sha256.txt"
    path.write_text("\n".join(rows) + "\n", encoding="utf-8")
    return path


def main() -> None:
    report = json.loads(REPORT.read_text(encoding="utf-8"))
    if report.get("status") != "passed" or len(report.get("frames", [])) != 7:
        raise RuntimeError("The Studio capture report is not a seven-frame pass")
    sheets = [contact_sheet(report["frames"]), context_strip(report["frames"])]
    write_manifest(sheets)
    for path in sheets:
        print(f"{path.name}: {path.stat().st_size} bytes")


if __name__ == "__main__":
    main()
