#!/usr/bin/env python3
"""
End-to-End live verification of the KoiSync AI Director MCP server
(koisync_director_mcp.py -> HTTP loopback 127.0.0.1:41289 -> KoiSync.MainGame inside Unity).
Runs exclusively against the isolated validation runtime and never touches the user's real game.
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
OUT_DIR = CASE_ROOT / "director-e2e"
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
            raise RuntimeError("MCP server closed stdout unexpectedly")
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


def sync_isolated_dlls() -> None:
    shutil.copy2(
        PROJECT_ROOT / r"src\KoiSync.MainGame\bin\Release\net35\KoiSync.MainGame.dll",
        RUNTIME / r"BepInEx\plugins\KoiSyncMainGame\KoiSync.MainGame.dll",
    )
    live_dll = PROJECT_ROOT / r"tests\KoiSync.MainGameLive\bin\Release\net35\KoiSync.MainGameLive.dll"
    if live_dll.exists():
        shutil.copy2(live_dll, RUNTIME / r"BepInEx\plugins\KoiSyncMainGame\KoiSync.MainGameLive.dll")


def main() -> int:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    sync_isolated_dlls()

    log_path = OUT_DIR / "director-e2e.log"
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
        # Wait for LiveProbe live.xml refresh
        deadline = time.time() + 60
        while time.time() < deadline:
            if proc.poll() is not None:
                raise RuntimeError(f"Isolated host exited early rc={proc.returncode}")
            if snapshot.exists() and snapshot.stat().st_mtime >= t_start:
                break
            time.sleep(0.25)
        else:
            raise RuntimeError("LiveProbe did not start within 60s")

        # Boot into FreeH Mode 0
        boot = subprocess.run(
            [str(PYTHON), "-X", "utf8", str(PROJECT_ROOT / r"tools\main-game-live.py"),
             "--boot", "--mode", "0", "--out", str(CASE_ROOT), "--card", CARD],
            check=False,
        )
        if boot.returncode != 0:
            raise RuntimeError("FreeH bootstrap failed")

        # Start real MCP server over stdio!
        mcp = McpSession()
        tools = mcp.call_raw("tools/list", {})["tools"]
        print(f"[MCP] Connected! Registered tools: {len(tools)}", flush=True)

        # 1. Status check
        st0 = mcp.tool("koisync_status")["structuredContent"]
        print(f"[1. STATUS] inHScene={st0['inHScene']} pose={st0.get('poseKey')} feel={st0.get('feelPreset')}/{st0.get('nonShaftMode')} dir={st0['director']['status']}", flush=True)
        assert st0["inHScene"] is True

        # 2. List poses (sonyu)
        poses = mcp.tool("koisync_list_poses", {"mode": "sonyu"})["structuredContent"]
        print(f"[2. POSES] Found {poses['count']} selectable sonyu poses (sample: {[p['key'] for p in poses['poses'][:5]]})", flush=True)
        assert poses["count"] >= 10

        # 3. Switch pose to sonyu_4 (cowgirl!) + autoInsert + SLoop + check R2 not pegged!
        mcp.tool("koisync_switch_pose", {"key": "sonyu_4", "autoInsert": True})
        time.sleep(2.4)
        mcp.tool("koisync_set_stage", {"stage": "SLoop", "speed": 0.75, "holdSeconds": 12})
        time.sleep(1.0)
        st_cg = mcp.tool("koisync_status")["structuredContent"]
        print(f"[3. POSE/STAGE] pose={st_cg.get('poseKey')} stage={st_cg.get('stage')} mapping={st_cg.get('mappingMode')} axes={st_cg.get('axes')} feel={st_cg.get('feelStatus')}", flush=True)
        assert st_cg.get("poseKey") == "sonyu_4", f"Expected sonyu_4, got {st_cg.get('poseKey')}"
        assert st_cg.get("stage") == "SLoop", f"Expected SLoop, got {st_cg.get('stage')}"

        # Capture screenshot via MCP!
        shot_res = mcp.tool("koisync_screenshot", {"width": 640, "height": 360, "includeImage": True})
        img_blocks = [c for c in shot_res["content"] if c.get("type") == "image"]
        assert len(img_blocks) == 1, "Expected inline PNG image block"
        img_bytes = base64.b64decode(img_blocks[0]["data"])
        (OUT_DIR / "mcp_live_view.png").write_bytes(img_bytes)
        print(f"[4. SCREENSHOT] Saved {len(img_bytes)} B inline MCP PNG -> {OUT_DIR / 'mcp_live_view.png'}", flush=True)

        # 5. Run 3-step Edging Choreography Sequence via MCP!
        mcp.tool("koisync_director_sequence", {
            "name": "星光寸止实测",
            "steps": [
                {"name": "深插冲刺", "durationSeconds": 2.6, "rampSeconds": 0.25, "mode": "envelope", "l0Min": 0, "l0Max": 78, "gameStage": "SLoop", "gameSpeed": 0.90, "feelPreset": "Balanced"},
                {"name": "外沿寸止", "durationSeconds": 2.6, "rampSeconds": 0.25, "mode": "envelope", "l0Min": 54, "l0Max": 80, "gameStage": "WLoop", "gameSpeed": 0.20},
                {"name": "悬停微颤", "durationSeconds": 2.0, "rampSeconds": 0.25, "mode": "hold", "holdL0": 74, "flutterHz": 4.0, "flutterAmp": 5},
            ],
        })
        step_samples: dict[int, list[float]] = {0: [], 1: [], 2: []}
        step_stages: dict[int, set[str]] = {0: set(), 1: set(), 2: set()}
        t0 = time.time()
        while time.time() - t0 < 8.4:
            st = mcp.tool("koisync_status")["structuredContent"]
            d = st["director"]
            idx = d["stepIndex"]
            l0 = st["axes"]["L0"]
            if idx in step_samples:
                step_samples[idx].append(l0)
                step_stages[idx].add(st.get("stage", ""))
            time.sleep(0.18)
        st_after = mcp.tool("koisync_status")["structuredContent"]
        for i in range(3):
            vals = step_samples[i]
            print(f"[5. SEQ STEP {i+1}] samples={len(vals)} L0={min(vals):.1f}..{max(vals):.1f} stages={sorted(step_stages[i])}", flush=True)
        print(f"[5. SEQ DONE] active={st_after['director']['active']} status={st_after['director']['status']}", flush=True)
        assert len(step_samples[0]) >= 3 and min(step_samples[0]) < 35.0, f"Step 1 didn't reach deep: {step_samples[0]}"
        assert len(step_samples[1]) >= 3 and min(step_samples[1][2:]) >= 45.0, f"Step 2 didn't stay in outer tease: {step_samples[1]}"
        assert len(step_samples[2]) >= 3 and min(step_samples[2][1:]) >= 62.0 and max(step_samples[2][1:]) <= 86.0, f"Step 3 didn't hold flutter near 74: {step_samples[2]}"

        # 6. Test 60Hz real-time EdgeGuard brake!
        mcp.tool("koisync_set_stage", {"stage": "SLoop", "speed": 0.85, "holdSeconds": 10})
        mcp.tool("koisync_game_action", {"action": "lock_gauges"})
        mcp.tool("koisync_game_action", {"action": "set_gauges", "female": 40, "male": 40})
        mcp.tool("koisync_director_edge_guard", {
            "enabled": True,
            "femaleThreshold": 85,
            "maleThreshold": 85,
            "brakeDurationSeconds": 4.0,
            "brakeL0Min": 55,
            "brakeL0Max": 80,
            "brakeStage": "WLoop",
            "brakeSpeed": 0.15,
        })
        time.sleep(0.3)
        # Push female gauge to 88 -> EdgeGuard must fire instantaneously!
        mcp.tool("koisync_game_action", {"action": "set_gauges", "female": 88})
        time.sleep(0.55)
        st_eg = mcp.tool("koisync_status")["structuredContent"]
        print(f"[6. EDGEGUARD] braking={st_eg['director']['edgeBraking']} triggers={st_eg['director']['edgeTriggerCount']} stage={st_eg.get('stage')} L0={st_eg['axes']['L0']} status={st_eg['director']['status']}", flush=True)
        assert st_eg["director"]["edgeBraking"] is True
        assert st_eg["director"]["edgeTriggerCount"] >= 1
        assert st_eg.get("stage") == "WLoop"
        assert st_eg["axes"]["L0"] >= 48.0

        # 7. Clean Release
        st_rel = mcp.tool("koisync_director_release", {"immediate": True})["structuredContent"]
        mcp.tool("koisync_director_edge_guard", {"enabled": False})
        print(f"[7. RELEASE] active={st_rel['director']['active']} status={st_rel['director']['status']}", flush=True)
        assert st_rel["director"]["active"] is False

        print("\n=== ALL 7 LIVE AI DIRECTOR MCP E2E CHECKS PASSED 100%! ===", flush=True)
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
