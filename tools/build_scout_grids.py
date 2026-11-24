#!/usr/bin/env python3
r"""
Stitch 8-angle camera-scout captures (A_sideR .. H_rearR) per pose into a labeled 4x2 scout_grid.png
with cyan shaft root->tip markers & yellow anatomical entry rings, so the reviewer can visually pick
or fine-tune an unobstructed @cam=s,i,u,d camera showing BOTH the penis AND the interaction opening.
"""

from __future__ import annotations

import argparse
import json
import math
import pathlib
import sys
from typing import Any

from PIL import Image, ImageDraw, ImageFont


def load_font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    for fp in (
        r"C:\Windows\Fonts\msyhbd.ttc",
        r"C:\Windows\Fonts\consolab.ttf",
        r"C:\Windows\Fonts\arialbd.ttf",
    ):
        if pathlib.Path(fp).exists():
            try:
                return ImageFont.truetype(fp, size)
            except Exception:
                pass
    return ImageFont.load_default()


FONT_SM = load_font(14)
FONT_LG = load_font(20)


def valid_pt(pt: Any, w: int, h: int) -> tuple[float, float] | None:
    if not isinstance(pt, list) or len(pt) < 3:
        return None
    x, y, z = float(pt[0]), float(pt[1]), float(pt[2])
    if z <= 0.01 or not (math.isfinite(x) and math.isfinite(y)):
        return None
    if x < -60 or x > w + 60 or y < -60 or y > h + 60:
        return None
    return (x, y)


def annotate_tile(img: Image.Image, row: dict[str, Any]) -> Image.Image:
    out = img.copy()
    draw = ImageDraw.Draw(out, "RGBA")
    w, h = out.size

    orig = valid_pt(row.get("orig"), w, h)
    end = valid_pt(row.get("end"), w, h)
    cal = valid_pt(row.get("cal"), w, h)

    # Subtle guide markers (small & semi-transparent so real anatomy stays crystal clear!)
    if orig and end:
        draw.line([orig, end], fill=(0, 235, 255, 165), width=2)
        draw.ellipse([orig[0] - 4, orig[1] - 4, orig[0] + 4, orig[1] + 4], fill=(0, 180, 255, 200), outline=(255, 255, 255, 200))
        d = 5
        draw.polygon([(end[0], end[1] - d), (end[0] + d, end[1]), (end[0], end[1] + d), (end[0] - d, end[1])],
                     fill=(0, 255, 220, 200), outline=(255, 255, 255, 200))
    if cal:
        draw.ellipse([cal[0] - 9, cal[1] - 9, cal[0] + 9, cal[1] + 9], outline=(255, 230, 50, 220), width=2)

    tag = row.get("tag", "")
    cam = row.get("cam", "")
    draw.rectangle([0, 0, w, 28], fill=(12, 15, 22, 195))
    draw.text((8, 5), f"{tag}   @cam={cam}", fill=(245, 248, 255), font=FONT_SM)
    return out


def build_grid(pose_dir: pathlib.Path) -> pathlib.Path | None:
    angles_file = pose_dir / "angles.jsonl"
    if not angles_file.exists():
        return None
    rows = [json.loads(line) for line in angles_file.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
    if not rows:
        return None

    tw, th = 480, 270
    cols = 4
    rows_cnt = max(1, math.ceil(len(rows) / cols))
    pad = 8
    hdr = 42
    W = pad + cols * (tw + pad)
    H = hdr + rows_cnt * (th + pad) + pad
    sheet = Image.new("RGB", (W, H), (14, 17, 24))
    draw = ImageDraw.Draw(sheet)
    draw.rectangle([0, 0, W, hdr - 4], fill=(24, 30, 44))
    draw.text((12, 9), f"CAMERA SCOUT: {pose_dir.name}   (Cyan=Penis Root->Tip, Yellow Ring=Opening Entry)", fill=(245, 248, 255), font=FONT_LG)

    for idx, row in enumerate(rows):
        p = pose_dir / f"{row['tag']}.png"
        if not p.exists():
            continue
        im = annotate_tile(Image.open(p).convert("RGB"), row).resize((tw, th), Image.Resampling.LANCZOS)
        r = idx // cols
        c = idx % cols
        x = pad + c * (tw + pad)
        y = hdr + r * (th + pad)
        sheet.paste(im, (x, y))

    out_path = pose_dir / "scout_grid.png"
    sheet.save(out_path, optimize=True)
    return out_path


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", type=pathlib.Path, required=True)
    args = ap.parse_args()
    count = 0
    for sub in sorted(args.root.iterdir()):
        if not sub.is_dir():
            continue
        out = build_grid(sub)
        if out:
            print(f"  [SCOUT] {sub.name} -> {out}")
            count += 1
    print(f"Built {count} scout grids under {args.root}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
