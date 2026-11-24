#!/usr/bin/env python3
"""
Live Unity E2E Verification of KoiSync's Timeline-Curve Pose Variant Engine
(koisync_director_timeline -> PhaseDepthMap -> 60Hz Animator Layer-0 Scrubbing + 6-Axis SR6 Grind).
Verifies on sonyu_4 (Cowgirl) that ACTUAL in-game bone depth (depthMm) AND SR6 L0/R0/R1/R2
obey tip_rub (3.2 Hz shallow), nine_shallow_one_deep (shallow->full plunge), and deep_grind (deep+twist/sway)!
"""
from __future__ import annotations

import base64
import json
import pathlib
import shutil
import subprocess
import sys
import time
from typing import Any

PROJECT_ROOT = pathlib.Path(r"E:\project\c#\KoiSync")
CASE_ROOT = PROJECT_ROOT / r"artifacts\main-game-visual-v020"
RUNTIME = CASE_ROOT / "runtime"
EXE = RUNTIME / "Koikatu.exe"
OUT_DIR = CASE_ROOT / "timeline-e2e"
PYTHON = pathlib.Path(r"E:\project\c#\KoikatuModFactory\.venv\Scripts\python.exe")
MCP_SCRIPT = PROJECT_ROOT / r"mcp\koisync_director_mcp.py"
CARD = r"E:\Galgames\kk\UserData\chara\female\qinnyann\长夜.png"


class McpSession:
    def __init__(self) -> None:
        self.proc = subprocess.Popen(
            [str(PYTHON), "-X", "utf8", str(MCP_SCRIPT)],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
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

    def close(self) -> None:
        try:
            if self.proc.stdin:
                self.proc.stdin.close()
            self.proc.wait(timeout=3)
        except Exception:
            self.proc.kill()


def save_shot(mcp: McpSession, filename: pathlib.Path) -> int:
    res = mcp.tool("koisync_screenshot", {"width": 640, "height": 360, "includeImage": True})
    sc = res["structuredContent"]
    img = [c for c in res["content"] if c.get("type") == "image"][0]
    raw = base64.b64decode(img["data"])
    filename.write_bytes(raw)
    import numpy as np
    from PIL import Image
    arr = np.array(Image.open(filename))
    r, g, b = arr[:, :, 0], arr[:, :, 1], arr[:, :, 2]
    py = np.where((r > 200) & (g < 80) & (b > 90))[0]
    bot = int(py.max()) if len(py) else -1
    print(f"    [SHOT {filename.name}] depthMm={sc.get('depthMm')} visualPinkBotY={bot}px", flush=True)
    return bot


def main() -> int:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    shutil.copy2(
        PROJECT_ROOT / r"src\KoiSync.MainGame\bin\Release\net35\KoiSync.MainGame.dll",
        RUNTIME / r"BepInEx\plugins\KoiSyncMainGame\KoiSync.MainGame.dll",
    )
    live_dll = PROJECT_ROOT / r"tests\KoiSync.MainGameLive\bin\Release\net35\KoiSync.MainGameLive.dll"
    if live_dll.exists():
        shutil.copy2(live_dll, RUNTIME / r"BepInEx\plugins\KoiSyncMainGame\KoiSync.MainGameLive.dll")

    log_path = OUT_DIR / "timeline-e2e.log"
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
    print(f"[BOOT] Started isolated Koikatu PID={proc.pid}", flush=True)

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

        mcp = McpSession()
        tools = [t["name"] for t in mcp.call_raw("tools/list", {})["tools"]]
        print(f"[MCP] {len(tools)} tools loaded (has timeline: {'koisync_director_timeline' in tools})", flush=True)
        assert "koisync_director_timeline" in tools

        # Switch to Cowgirl (sonyu_4) SLoop
        mcp.tool("koisync_switch_pose", {"key": "sonyu_4", "autoInsert": True})
        time.sleep(2.4)
        mcp.tool("koisync_set_stage", {"stage": "SLoop", "speed": 0.70, "holdSeconds": 10})
        time.sleep(1.2)

        # Launch 3-variant Timeline showcase on live Cowgirl!
        res0 = mcp.tool("koisync_director_timeline", {
            "name": "骑乘位三变式实测",
            "steps": [
                {"preset": "tip_rub", "name": "高频磨龟头", "durationSeconds": 3.2, "freqHz": 3.2, "depthMin": 2, "depthMax": 24, "l0Min": 50, "l0Max": 80, "twistAmp": 24, "grindAmp": 14},
                {"preset": "nine_shallow_one_deep", "name": "三浅一深", "durationSeconds": 4.2, "shallowCount": 3, "freqHz": 2.4, "deepHoldSeconds": 0.8},
                {"preset": "deep_grind", "name": "深埋扭腰碾磨", "durationSeconds": 3.2, "depthMin": 80, "depthMax": 100, "l0Min": 0, "l0Max": 20, "twistAmp": 34, "grindAmp": 30, "grindHz": 1.4},
            ],
        })["structuredContent"]
        pm = res0["phaseMap"]
        print(f"[PHASEMAP] ready={pm['ready']} cov={pm['coverage']} depth={pm['minDepthMm']}..{pm['maxDepthMm']}mm shallowP={pm['shallowPhase']} deepP={pm['deepPhase']}", flush=True)
        assert pm["ready"] is True and (pm["maxDepthMm"] - pm["minDepthMm"]) >= 30.0

        min_d, max_d = pm["minDepthMm"], pm["maxDepthMm"]
        span_d = max_d - min_d

        buckets: dict[int, list[dict[str, Any]]] = {0: [], 1: [], 2: []}
        shot_tip_y = -1
        shot_deep_y = -1
        t0 = time.time()
        while time.time() - t0 < 11.8:
            st = mcp.tool("koisync_status")["structuredContent"]
            tl = st["timeline"]
            idx = tl["stepIndex"]
            if tl["active"] and idx in buckets:
                buckets[idx].append({
                    "depthMm": st.get("depthMm", 0.0),
                    "depthNorm": tl.get("depthNorm", 0.0),
                    "L0": st["axes"]["L0"],
                    "R0": st["axes"]["R0"],
                    "R1": st["axes"]["R1"],
                    "R2": st["axes"]["R2"],
                })
                if idx == 0 and shot_tip_y < 0 and len(buckets[0]) >= 5:
                    shot_tip_y = save_shot(mcp, OUT_DIR / "tl_01_tip_rub.png")
                elif idx == 2 and shot_deep_y < 0 and len(buckets[2]) >= 5:
                    shot_deep_y = save_shot(mcp, OUT_DIR / "tl_02_deep_grind.png")
            time.sleep(0.12)

        st_end = mcp.tool("koisync_status")["structuredContent"]

        for i, label in [(0, "TipRub 3.2Hz"), (1, "3-Shallow-1-Deep"), (2, "DeepGrind")]:
            rows = buckets[i]
            # skip first 2 transition frames for clean steady-state metrics
            steady = rows[2:] if len(rows) > 4 else rows
            dm = [r["depthMm"] for r in steady]
            l0s = [r["L0"] for r in steady]
            r0s = [r["R0"] for r in steady]
            r1s = [r["R1"] for r in steady]
            r2s = [r["R2"] for r in steady]
            print(
                f"[STEP {i+1} {label:16s}] n={len(rows):2d} "
                f"REAL_BONE_DEPTH={min(dm):5.1f}..{max(dm):5.1f}mm (vs full {min_d:.0f}..{max_d:.0f}mm) | "
                f"L0={min(l0s):4.1f}..{max(l0s):4.1f} | "
                f"R0={min(r0s):4.1f}..{max(r0s):4.1f} R1={min(r1s):4.1f}..{max(r1s):4.1f} R2={min(r2s):4.1f}..{max(r2s):4.1f}",
                flush=True,
            )

        # Assertions proving BOTH the on-screen 3D bones AND SR6 6-axes obeyed the curve!
        s0 = buckets[0][2:]
        s1 = buckets[1][2:]
        s2 = buckets[2][2:]
        # 1. TipRub real bone depth stays strictly in lower ~50% of full pose depth (shallow tip!) and SR6 L0 >= 44
        assert max(r["depthMm"] for r in s0) < min_d + span_d * 0.55, f"TipRub bones sank too deep: {s0}"
        assert min(r["L0"] for r in s0) >= 44.0, f"TipRub L0 went too deep: {s0}"
        # 2. 3-Shallow-1-Deep spans from shallow tip to full deep plunge on BOTH bones AND SR6 L0!
        assert (max(r["depthMm"] for r in s1) - min(r["depthMm"] for r in s1)) >= span_d * 0.65
        assert min(r["L0"] for r in s1) <= 18.0 and max(r["L0"] for r in s1) >= 65.0
        # 3. DeepGrind real bone depth stays locked in top 40% (deep!) AND SR6 L0 <= 30 AND R0/R1/R2 actively grinding!
        assert min(r["depthMm"] for r in s2) > min_d + span_d * 0.55, f"DeepGrind bones pulled too shallow: {s2}"
        assert max(r["L0"] for r in s2) <= 32.0
        assert (max(r["R0"] for r in s2) - min(r["R0"] for r in s2)) >= 25.0
        assert (max(r["R1"] for r in s2) - min(r["R1"] for r in s2)) >= 25.0
        # 4. Visual rendered depth check (rendered DeepGrind sleeve drops >= 40 px lower than TipRub!)
        assert shot_tip_y > 0 and shot_deep_y > 0 and (shot_deep_y - shot_tip_y) >= 40, (
            f"Visual rendered depth mismatch: tipY={shot_tip_y} deepY={shot_deep_y}"
        )
        print(f"[VISUAL] Rendered sleeve depth drop: TipRub y={shot_tip_y}px -> DeepGrind y={shot_deep_y}px (+{shot_deep_y - shot_tip_y}px)", flush=True)
        # 5. Clean auto-restore to normal animation
        assert st_end["timeline"]["active"] is False
        print(f"[RESTORE] timeline.active={st_end['timeline']['active']} status={st_end['timeline']['lastEvent']}", flush=True)
        print("\n=== ALL LIVE TIMELINE VARIANT E2E CHECKS PASSED 100%! ===", flush=True)
        return 0
    finally:
        if mcp is not None:
            mcp.close()
        try:
            proc.terminate()
            proc.wait(timeout=5)
        except Exception:
            try:
                proc.kill()
            except Exception:
                pass
        print(f"[CLEANUP] Stopped isolated PID={proc.pid}", flush=True)


if __name__ == "__main__":
    raise SystemExit(main())
