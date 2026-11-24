#!/usr/bin/env python3
"""
Comprehensive Live Unity E2E Verification for KoiSync.MainGame v0.2.1:
  A. koisync_get_config / koisync_set_config (configurable outerCeilingL0 + live persist)
  B. sonyu_4 (Cowgirl) Full-Body 3D Procedural Waist-Grind (Circle & Figure8) + 4-phase visual sheet
     + auto-detected Female(Auto) active mover + loop=true + live timeline_tune
  C. Upgraded yellow-band nine_shallow_one_deep (3-shallow-1-deep)
  D. sonyu_0 (Missionary) Auto Male(Auto) active mover detection & 3D pelvis grind
  E. Male & Female Facial Expression Director (happy_trance, ecstasy_melt, pain_reluctant, pain_wince)
     + 2x2 visual expression sheet + clean release!

Runs 100% in the isolated runtime on port 41299 so it NEVER touches Master's real game on 41289.
"""
from __future__ import annotations

import base64
import json
import math
import os
import pathlib
import re
import shutil
import subprocess
import sys
import time
from typing import Any

from PIL import Image, ImageDraw

PROJECT_ROOT = pathlib.Path(r"E:\project\c#\KoiSync")
CASE_ROOT = PROJECT_ROOT / r"artifacts\main-game-visual-v020"
RUNTIME = CASE_ROOT / "runtime"
EXE = RUNTIME / "Koikatu.exe"
CFG_PATH = RUNTIME / r"BepInEx\config\local.koisync.maingame.cfg"
OUT_DIR = CASE_ROOT / "v021-e2e"
PYTHON = pathlib.Path(r"E:\project\c#\KoikatuModFactory\.venv\Scripts\python.exe")
MCP_SCRIPT = PROJECT_ROOT / r"mcp\koisync_director_mcp.py"
CARD = r"E:\Galgames\kk\UserData\chara\female\qinnyann\长夜.png"
ISO_PORT = 41299


class McpSession:
    def __init__(self, port: int) -> None:
        env = dict(os.environ)
        env["KOISYNC_DIRECTOR_URL"] = f"http://127.0.0.1:{port}"
        self.proc = subprocess.Popen(
            [str(PYTHON), "-X", "utf8", str(MCP_SCRIPT)],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            env=env,
        )
        self.req_id = 0
        self.call_raw("initialize", {"protocolVersion": "2025-11-25"})
        self.notify("notifications/initialized")

    def notify(self, method: str, params: dict[str, Any] | None = None) -> None:
        msg: dict[str, Any] = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            msg["params"] = params
        assert self.proc.stdin is not None
        self.proc.stdin.write((json.dumps(msg) + "\n").encode("utf-8"))
        self.proc.stdin.flush()

    def call_raw(self, method: str, params: dict[str, Any]) -> dict[str, Any]:
        self.req_id += 1
        msg = {"jsonrpc": "2.0", "id": self.req_id, "method": method, "params": params}
        assert self.proc.stdin is not None and self.proc.stdout is not None
        self.proc.stdin.write((json.dumps(msg, ensure_ascii=False) + "\n").encode("utf-8"))
        self.proc.stdin.flush()
        line = self.proc.stdout.readline()
        if not line:
            raise RuntimeError("MCP stdout closed")
        resp = json.loads(line.decode("utf-8"))
        if "error" in resp:
            raise RuntimeError(f"JSON-RPC error: {resp['error']}")
        return resp["result"]

    def tool(self, name: str, arguments: dict[str, Any] | None = None) -> dict[str, Any]:
        res = self.call_raw("tools/call", {"name": name, "arguments": arguments or {}})
        if res.get("isError"):
            raise RuntimeError(f"Tool {name} failed: {res.get('content')}")
        return res

    def state(self) -> dict[str, Any]:
        return self.tool("koisync_status")["structuredContent"]

    def close(self) -> None:
        try:
            if self.proc.stdin:
                self.proc.stdin.close()
            self.proc.wait(timeout=3)
        except Exception:
            self.proc.kill()


def capture_png(mcp: McpSession, path: pathlib.Path, width: int = 640, height: int = 360) -> dict[str, Any]:
    res = mcp.tool("koisync_screenshot", {"width": width, "height": height, "includeImage": True})
    img = [c for c in res["content"] if c.get("type") == "image"][0]
    path.write_bytes(base64.b64decode(img["data"]))
    return res["structuredContent"]


def make_grid_2x2(items: list[tuple[pathlib.Path, str]], dest: pathlib.Path) -> None:
    imgs = [Image.open(p).convert("RGB") for p, _ in items]
    w, h = imgs[0].size
    canvas = Image.new("RGB", (w * 2, h * 2), (16, 20, 26))
    draw = ImageDraw.Draw(canvas)
    coords = [(0, 0), (w, 0), (0, h), (w, h)]
    for (img, (_, label)), (x, y) in zip(zip(imgs, items), coords):
        canvas.paste(img, (x, y))
        draw.rectangle([x + 6, y + 6, x + w - 6, y + 32], fill=(12, 16, 22))
        draw.text((x + 12, y + 12), label, fill=(100, 240, 210))
    canvas.save(dest)


def patch_isolated_port(port: int) -> str | None:
    if not CFG_PATH.exists():
        return None
    orig = CFG_PATH.read_text(encoding="utf-8", errors="replace")
    if re.search(r"^Port\s*=", orig, flags=re.M):
        patched = re.sub(r"^Port\s*=\s*\d+", f"Port = {port}", orig, flags=re.M)
    else:
        patched = orig + f"\n[Director]\nEnabled = true\nPort = {port}\n"
    CFG_PATH.write_text(patched, encoding="utf-8")
    return orig


def main() -> int:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    shutil.copy2(
        PROJECT_ROOT / r"src\KoiSync.MainGame\bin\Release\net35\KoiSync.MainGame.dll",
        RUNTIME / r"BepInEx\plugins\KoiSyncMainGame\KoiSync.MainGame.dll",
    )
    live_dll = PROJECT_ROOT / r"tests\KoiSync.MainGameLive\bin\Release\net35\KoiSync.MainGameLive.dll"
    if live_dll.exists():
        shutil.copy2(live_dll, RUNTIME / r"BepInEx\plugins\KoiSyncMainGame\KoiSync.MainGameLive.dll")

    orig_cfg = patch_isolated_port(ISO_PORT)
    log_path = OUT_DIR / "v021-e2e.log"
    snapshot = CASE_ROOT / "live.xml"
    t_start = time.time()

    proc = subprocess.Popen(
        [
            str(EXE),
            "--koisync-main-validation",
            f"--koisync-main-output={CASE_ROOT}",
            "-screen-fullscreen", "0",
            "-screen-width", "1280",
            "-screen-height", "720",
            "-logFile", str(log_path),
        ],
        cwd=str(RUNTIME),
    )
    print(f"[BOOT] Isolated Koikatu PID={proc.pid} on Director port {ISO_PORT}", flush=True)

    mcp: McpSession | None = None
    try:
        deadline = time.time() + 60
        while time.time() < deadline:
            if proc.poll() is not None:
                raise RuntimeError(f"Host exited early rc={proc.returncode}")
            if snapshot.exists() and snapshot.stat().st_mtime >= t_start:
                break
            time.sleep(0.25)
        else:
            raise RuntimeError("LiveProbe timeout")

        boot = subprocess.run(
            [str(PYTHON), "-X", "utf8", str(PROJECT_ROOT / r"tools\main-game-live.py"),
             "--boot", "--mode", "0", "--out", str(CASE_ROOT), "--card", CARD],
            check=False,
        )
        if boot.returncode != 0:
            raise RuntimeError("FreeH bootstrap failed")

        mcp = McpSession(ISO_PORT)
        tools = [t["name"] for t in mcp.call_raw("tools/list", {})["tools"]]
        print(f"[MCP] Loaded {len(tools)} tools: {', '.join(tools)}", flush=True)
        for req_tool in (
            "koisync_director_timeline",
            "koisync_director_timeline_tune",
            "koisync_director_expression",
            "koisync_get_config",
            "koisync_set_config",
            "koisync_director_gauges",
        ):
            assert req_tool in tools, f"Missing MCP tool {req_tool}"

        # === TEST A: Full Config Read/Write & Configurable OuterCeilingL0 ===
        print("\n=== TEST A: Config Read/Write & OuterCeilingL0 ===", flush=True)
        cfg0 = mcp.tool("koisync_get_config")["structuredContent"]
        print(f"  Initial config: outerCeilingL0={cfg0['outerCeilingL0']} smoothingMs={cfg0['smoothingMs']}", flush=True)
        cfg1 = mcp.tool("koisync_set_config", {"outerCeilingL0": 90, "smoothingMs": 18, "save": True})["structuredContent"]
        st_cfg = mcp.state()
        assert abs(cfg1["outerCeilingL0"] - 90.0) < 0.2 and abs(st_cfg["outerCeilingL0"] - 90.0) < 0.2
        assert abs(cfg1["smoothingMs"] - 18.0) < 0.2
        mcp.tool("koisync_set_config", {"outerCeilingL0": 80, "smoothingMs": 25, "save": True})
        print("  [PASS] Config read/write + outerCeilingL0 live update & restore verified!", flush=True)

        # === TEST B: Cowgirl (sonyu_4) 3D Full-Body Procedural Waist-Grind + Loop + Live Tune ===
        print("\n=== TEST B: Cowgirl (sonyu_4) 3D Waist-Grind + Loop + Tune ===", flush=True)
        mcp.tool("koisync_switch_pose", {"key": "sonyu_4", "autoInsert": True})
        time.sleep(2.4)
        mcp.tool("koisync_set_stage", {"stage": "SLoop", "speed": 0.70, "holdSeconds": 10})
        time.sleep(1.0)

        mcp.tool("koisync_director_timeline", {
            "preset": "deep_grind",
            "name": "骑乘深埋3D扭腰",
            "durationSeconds": 4.0,
            "loop": True,
            "grindPattern": "circle",
            "grindHz": 0.70,
            "bodyGrindScale": 1.25,
            "mover": "auto",
            "expressionPreset": "happy_trance",
            "expressionTarget": "both",
        })
        time.sleep(0.55)  # wait for 0.4s envelope fade-in

        phase_items: list[tuple[pathlib.Path, str]] = []
        xs, ys, zs, disps, yaws, drifts, entry_drifts, r2s = [], [], [], [], [], [], [], []
        resolved_mover = ""
        fem_share = 0.0
        for idx, deg in enumerate((0, 90, 180, 270)):
            st = mcp.state()
            pm = st["phaseMap"]
            bg = st["bodyGrind"]
            fem_share = pm["femaleMoverShare"]
            resolved_mover = bg["resolvedMover"]
            xs.append(bg["hipWorldX"])
            ys.append(bg["hipWorldY"])
            zs.append(bg["hipWorldZ"])
            disps.append(bg["hipDispMm"])
            yaws.append(bg["hipYawDeg"])
            drifts.append(bg["footDriftMm"])
            entry_drifts.append(bg.get("entryDriftMm", 0.0))
            r2s.append(st["axes"]["R2"])
            shot = OUT_DIR / f"cowgirl_grind_{deg:03d}.png"
            capture_png(mcp, shot)
            lbl = f"{deg}deg | {resolved_mover} hip={bg['hipDispMm']:.1f}mm entry={bg.get('entryDriftMm',0.0):.1f}mm yaw={bg['hipYawDeg']:+.1f}deg R2={st['axes']['R2']:.0f}"
            print(f"  [{idx+1}/4] {lbl} world=({bg['hipWorldX']:.3f},{bg['hipWorldY']:.3f},{bg['hipWorldZ']:.3f})", flush=True)
            phase_items.append((shot, lbl))
            time.sleep(0.34)  # quarter-cycle at 0.70 Hz (~0.357s)

        make_grid_2x2(phase_items, OUT_DIR / "cowgirl_grind_4phase.png")

        # Also sample 18 fast telemetry points across 1.5s to measure exact 3D circular orbit span
        for _ in range(18):
            st = mcp.state()
            bg = st["bodyGrind"]
            xs.append(bg["hipWorldX"])
            ys.append(bg["hipWorldY"])
            zs.append(bg["hipWorldZ"])
            disps.append(bg["hipDispMm"])
            yaws.append(bg["hipYawDeg"])
            drifts.append(bg["footDriftMm"])
            entry_drifts.append(bg.get("entryDriftMm", 0.0))
            r2s.append(st["axes"]["R2"])
            time.sleep(0.08)

        span_x = (max(xs) - min(xs)) * 1000.0
        span_z = (max(zs) - min(zs)) * 1000.0
        span_horiz = math.hypot(span_x, span_z)
        max_disp = max(disps)
        max_entry = max(entry_drifts)
        yaw_span = max(yaws) - min(yaws)
        avg_drift = sum(drifts) / max(1, len(drifts))
        print(f"  [COWGIRL 3D] share={fem_share:.3f} mover={resolved_mover} horizSpan={span_horiz:.1f}mm (X={span_x:.1f}, Z={span_z:.1f}) maxHipDisp={max_disp:.1f}mm maxEntryDrift={max_entry:.1f}mm yawSpan={yaw_span:.1f}deg R2=[{min(r2s):.1f}..{max(r2s):.1f}] foot={avg_drift:.1f}mm", flush=True)
        assert fem_share >= 0.62, f"Expected cowgirl femaleMoverShare>=0.62, got {fem_share}"
        assert "Female" in resolved_mover, f"Expected Female mover on cowgirl, got {resolved_mover}"
        assert max_disp >= 14.0 and span_horiz >= 20.0, f"Expected clear 3D pelvis circle (disp={max_disp}, span={span_horiz})"
        assert max_entry <= 16.0, f"Expected vagina entry pinned tightly on shaft (<=16mm), got {max_entry}mm"
        assert min(r2s) >= 20.0 and max(r2s) <= 80.0, f"Expected calibrated natural R2 pitch in [20..80], got [{min(r2s)}..{max(r2s)}]"
        assert yaw_span >= 14.0, f"Expected clear 3D pelvis yaw twist, got {yaw_span}deg"
        assert avg_drift <= 28.0, f"Expected 2-bone IK to anchor feet cleanly, got avgDrift={avg_drift}mm"

        # Live non-interrupting Timeline Tune (Circle -> Figure8, faster Hz, stays looping!)
        tuned = mcp.tool("koisync_director_timeline_tune", {
            "grindPattern": "figure8",
            "grindHz": 1.35,
            "bodyGrindScale": 1.15,
            "loop": True,
        })["structuredContent"]
        assert tuned["timeline"]["active"] is True
        assert tuned["timeline"]["looping"] is True
        assert tuned["timeline"]["grindPattern"] == "Figure8"
        print("  [PASS] Cowgirl 3D full-body waist grind + 2-bone IK + loop + live tune verified!", flush=True)

        # === TEST C: Upgraded Yellow-Band NineShallowOneDeep (3 shallow + 1 deep plunge) ===
        print("\n=== TEST C: Upgraded Yellow-Band 3-Shallow-1-Deep ===", flush=True)
        mcp.tool("koisync_director_timeline", {
            "preset": "nine_shallow_one_deep",
            "name": "黄区三浅一深",
            "durationSeconds": 6.0,
            "loop": True,
            "shallowCount": 3,
            "freqHz": 2.4,
            "deepHoldSeconds": 0.6,
        })
        t0 = time.time()
        shallow_depths, all_depths, all_l0s = [], [], []
        while time.time() - t0 < 2.8:
            st = mcp.state()
            d = st["timeline"]["depthNorm"]
            l0 = st["timeline"]["deviceL0"]
            all_depths.append(d)
            all_l0s.append(l0)
            if time.time() - t0 < 1.1:
                shallow_depths.append(d)
            time.sleep(0.06)
        print(f"  [3S1D] shallowMax={max(shallow_depths):.3f} overallMaxDepth={max(all_depths):.3f} minL0={min(all_l0s):.1f}", flush=True)
        assert max(shallow_depths) >= 0.30, f"Expected yellow-band shallow stroke >=0.30, got {max(shallow_depths)}"
        assert max(all_depths) >= 0.88 and min(all_l0s) <= 15.0, "Expected full deep plunge (depth>=0.88, L0<=15)"
        print("  [PASS] Yellow-band 3-shallow-1-deep verified!", flush=True)

        # === TEST D: Facial Expression Presets (Female & Male) + 2x2 Visual Contact Sheet ===
        print("\n=== TEST D: Male & Female Facial Expression Director (2x2 Visual Proof) ===", flush=True)
        # Keep cowgirl gentle pose with front face visible for crisp expression close-up
        mcp.tool("koisync_director_timeline", {
            "preset": "deep_grind",
            "durationSeconds": 10.0,
            "loop": True,
            "grindHz": 0.5,
            "bodyGrindScale": 0.25,
        })
        expr_cases = [
            ("happy_trance", "both", "happy_trance (开心+恍惚)"),
            ("heart_trance", "both", "heart_trance (爱心眼沉醉)"),
            ("pain_reluctant", "both", "pain_reluctant (痛苦+泪光)"),
            ("pain_wince", "both", "pain_wince (紧闭痛忍+泪流)"),
        ]
        expr_items: list[tuple[pathlib.Path, str]] = []
        for preset, target, title in expr_cases:
            mcp.tool("koisync_director_expression", {"preset": preset, "target": target, "lookEyes": "camera"})
            time.sleep(0.55)
            st = mcp.state()
            ex = st["expression"]
            shot = OUT_DIR / f"expr_{preset}.png"
            capture_png(mcp, shot)
            lbl = (
                f"{preset} | F:eb={ex.get('femaleEyebrowPtn')} eye={ex.get('femaleEyesPtn')} "
                f"m={ex.get('femaleMouthPtn')} bl={ex.get('femaleBlush')} tr={ex.get('femaleTears')} | "
                f"M:eb={ex.get('maleEyebrowPtn')} eye={ex.get('maleEyesPtn')} m={ex.get('maleMouthPtn')}"
            )
            print(f"  [EXPR] {lbl}", flush=True)
            assert ex["femaleActive"] is True and ex["femalePreset"] == preset
            assert ex["maleActive"] is True and ex["malePreset"] == preset
            if preset == "happy_trance":
                assert ex["femaleEyesPtn"] == 4 and ex["femaleMouthPtn"] == 3 and ex["femaleBlush"] >= 0.70
                assert ex["maleEyesPtn"] == 4 and ex["maleMouthPtn"] == 1
            elif preset == "heart_trance":
                assert ex["femaleEyesPtn"] == 25 and ex["femaleMouthPtn"] == 3 and ex["femaleBlush"] >= 0.85
            elif preset == "pain_reluctant":
                assert ex["femaleEyesPtn"] == 12 and ex["femaleMouthPtn"] == 12 and ex["femaleTears"] == 2
                assert ex["maleEyebrowPtn"] == 1 and ex["maleEyesPtn"] == 9
            elif preset == "pain_wince":
                assert ex["femaleEyesPtn"] == 35 and ex["femaleMouthPtn"] == 11 and ex["femaleTears"] == 3
            expr_items.append((shot, lbl))

        make_grid_2x2(expr_items, OUT_DIR / "expressions_2x2.png")
        print("  [PASS] All 4 Female & Male facial expression presets verified!", flush=True)

        # === TEST E: Missionary (sonyu_0) Auto Male Active Mover Detection ===
        print("\n=== TEST E: Missionary (sonyu_0) Auto Male(Auto) Active Mover ===", flush=True)
        mcp.tool("koisync_switch_pose", {"key": "sonyu_0", "autoInsert": True})
        time.sleep(2.4)
        mcp.tool("koisync_set_stage", {"stage": "SLoop", "speed": 0.70, "holdSeconds": 10})
        time.sleep(1.0)
        mcp.tool("koisync_director_timeline", {
            "preset": "deep_grind",
            "durationSeconds": 6.0,
            "loop": True,
            "grindPattern": "circle",
            "grindHz": 1.0,
            "bodyGrindScale": 1.2,
            "mover": "auto",
        })
        time.sleep(0.6)
        m_disps, m_yaws = [], []
        m_share = 1.0
        m_mover = ""
        for _ in range(12):
            st = mcp.state()
            m_share = st["phaseMap"]["femaleMoverShare"]
            m_mover = st["bodyGrind"]["resolvedMover"]
            m_disps.append(st["bodyGrind"]["hipDispMm"])
            m_yaws.append(st["bodyGrind"]["hipYawDeg"])
            time.sleep(0.08)
        capture_png(mcp, OUT_DIR / "missionary_male_grind.png")
        print(f"  [MISSIONARY 3D] femaleMoverShare={m_share:.3f} mover={m_mover} maxDisp={max(m_disps):.1f}mm yawSpan={max(m_yaws)-min(m_yaws):.1f}deg", flush=True)
        assert m_share <= 0.38, f"Expected missionary femaleMoverShare<=0.38, got {m_share}"
        assert "Male" in m_mover, f"Expected Male mover on missionary, got {m_mover}"
        assert max(m_disps) >= 15.0, f"Expected real Male 3D pelvis grind, got maxDisp={max(m_disps)}mm"
        print("  [PASS] Missionary auto Male(Auto) active mover verified!", flush=True)

        # === TEST F: Clean Release ===
        rel = mcp.tool("koisync_director_release", {"immediate": True})["structuredContent"]
        time.sleep(0.25)
        st_end = mcp.state()
        assert st_end["timeline"]["active"] is False
        assert st_end["bodyGrind"]["active"] is False
        assert st_end["expression"]["anyActive"] is False
        print("\n=== ALL v0.2.1 E2E CHECKS PASSED 100%! ===", flush=True)
        return 0
    finally:
        if mcp is not None:
            mcp.close()
        if proc.poll() is None:
            proc.terminate()
            try:
                proc.wait(timeout=8)
            except Exception:
                proc.kill()
        if orig_cfg is not None:
            CFG_PATH.write_text(orig_cfg, encoding="utf-8")


if __name__ == "__main__":
    raise SystemExit(main())
