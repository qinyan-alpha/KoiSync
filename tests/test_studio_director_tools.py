#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""End-to-end verification tests for Studio auto-segmentation & Studio MCP tools."""
from __future__ import annotations

import base64
import json
import os
import sys
import tempfile
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "mcp"))
sys.path.insert(0, str(ROOT / "tools"))

import koisync_director_mcp as mcp_mod
import studio_auto_segment as seg_mod


def test_mcp_studio_tools():
    received = []
    tiny_png = base64.b64encode(b"\x89PNG\r\n\x1a\nfake").decode("ascii")

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, fmt, *args):
            pass

        def do_GET(self):
            received.append(("GET", self.path, None))
            if self.path == "/state":
                body = {"ok": True, "host": "KoiSync.Studio", "clock": {"available": True, "time": 21.7, "duration": 128.9}}
            elif self.path == "/studio/segments":
                body = {"ok": True, "source": "director_json", "segments": [{"id": "seg_01", "role": "intro"}]}
            else:
                self.send_response(404)
                self.end_headers()
                return
            raw = json.dumps(body).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(raw)))
            self.end_headers()
            self.wfile.write(raw)

        def do_POST(self):
            ln = int(self.headers.get("Content-Length", "0"))
            payload = json.loads(self.rfile.read(ln).decode("utf-8")) if ln > 0 else {}
            received.append(("POST", self.path, payload))
            if self.path == "/studio/screenshot":
                body = {"ok": True, "path": "C:/tmp/f.png", "time": payload.get("time", 0), "width": 640, "height": 360, "bytes": 12, "base64Png": tiny_png}
            else:
                body = {"ok": True, "path": self.path, "echo": payload}
            raw = json.dumps(body).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(raw)))
            self.end_headers()
            self.wfile.write(raw)

    srv = HTTPServer(("127.0.0.1", 0), Handler)
    port = srv.server_address[1]
    t = threading.Thread(target=srv.serve_forever, daemon=True)
    t.start()
    os.environ["KOISYNC_STUDIO_URL"] = f"http://127.0.0.1:{port}"
    try:
        dummy_main = mcp_mod.DirectorClient("http://127.0.0.1:41289")
        # Verify all 9 Studio tools exist in TOOLS registry
        expected = [
            "koisync_studio_status",
            "koisync_studio_segments",
            "koisync_studio_play_segment",
            "koisync_studio_speed",
            "koisync_studio_seek",
            "koisync_studio_climax",
            "koisync_studio_release",
            "koisync_studio_record",
            "koisync_studio_load_scene",
            "koisync_studio_step_bake",
            "koisync_studio_feel",
            "koisync_studio_bake_scripts",
            "koisync_studio_screenshot",
        ]
        for tool_name in expected:
            assert tool_name in mcp_mod.TOOL_MAP, f"Missing MCP tool: {tool_name}"

        st, _ = mcp_mod.invoke_tool("koisync_studio_status", {}, dummy_main)
        assert st["ok"] and st["host"] == "KoiSync.Studio"

        segs, _ = mcp_mod.invoke_tool("koisync_studio_segments", {"action": "list"}, dummy_main)
        assert segs["ok"] and len(segs["segments"]) == 1

        mcp_mod.invoke_tool("koisync_studio_segments", {"action": "reload"}, dummy_main)
        mcp_mod.invoke_tool("koisync_studio_segments", {"action": "save", "segments": [{"id": "seg_01", "start": 0, "end": 10}]}, dummy_main)
        mcp_mod.invoke_tool("koisync_studio_play_segment", {"segment": "seg_02", "mode": "loop", "speed": 1.2, "freezeCamera": True}, dummy_main)
        mcp_mod.invoke_tool("koisync_studio_speed", {"speed": 0.8}, dummy_main)
        mcp_mod.invoke_tool("koisync_studio_seek", {"time": 42.5, "play": True}, dummy_main)
        mcp_mod.invoke_tool("koisync_studio_climax", {"speed": 1.0, "pauseAtEnd": True}, dummy_main)
        mcp_mod.invoke_tool("koisync_studio_release", {"resumePlay": True, "speed": 1.0}, dummy_main)
        mcp_mod.invoke_tool("koisync_studio_record", {"action": "start_full", "speed": 1.0}, dummy_main)
        mcp_mod.invoke_tool("koisync_studio_load_scene", {"path": "E:/Galgames/kk/UserData/Studio/scene/test.png"}, dummy_main)
        mcp_mod.invoke_tool("koisync_studio_step_bake", {"action": "start", "stepSeconds": 0.05}, dummy_main)
        mcp_mod.invoke_tool("koisync_studio_feel", {"preset": "balanced"}, dummy_main)
        mcp_mod.invoke_tool("koisync_studio_bake_scripts", {"action": "start", "feelPreset": "balanced", "stepSeconds": 0.02}, dummy_main)

        shot, content = mcp_mod.invoke_tool("koisync_studio_screenshot", {"time": 25.0, "includeImage": True}, dummy_main)
        assert shot["ok"] and shot["time"] == 25.0 and "base64Png" not in shot
        assert len(content) == 2 and content[1]["type"] == "image" and content[1]["data"] == tiny_png
    finally:
        srv.shutdown()
        srv.server_close()

    print(f"PASS test_mcp_studio_tools ({len(received)} HTTP calls verified)")


def test_l0_changepoint_and_silverwolf():
    # Synthetic L0 funscript: 0..30s slow 0.5Hz, 30..60s fast 2.0Hz
    actions = []
    for i in range(0, 600):
        t = i * 0.1
        if t < 30.0:
            pos = 50 + 35 * (1 if int(t * 1.0) % 2 == 0 else -1)
        else:
            pos = 50 + 40 * (1 if int(t * 4.0) % 2 == 0 else -1)
        actions.append((t, float(pos)))
    cuts = seg_mod.detect_l0_change_points(actions, 0.0, 60.0)
    assert any(26.0 <= c <= 34.0 for c in cuts), f"Expected change-point near 30s, got {cuts}"

    # Real Silver Wolf card test if present on machine
    card = Path(r"E:\Galgames\kk\UserData\Studio\scene\MastaPOV\2023_0726_0212_58_942.png")
    if card.exists():
        plan = seg_mod.build_director_plan(card, None)
        assert plan["schema"] == "koisync.studio.director.v1"
        assert 128.0 <= plan["duration"] <= 130.0
        assert len(plan["segments"]) >= 6, f"Expected >=6 segments, got {len(plan['segments'])}"
        roles = [s["role"] for s in plan["segments"]]
        assert "climax" in roles, f"Expected climax auto-detection in Silver Wolf card, got roles={roles}"
        loopable = [s for s in plan["segments"] if s["loopable"]]
        assert len(loopable) >= 3, f"Expected >=3 loopable segments, got {len(loopable)}"
        print(f"PASS Silver Wolf real-card test: {len(plan['segments'])} segments, {len(loopable)} loopable, roles={roles}")
    else:
        print("SKIP Silver Wolf card (not present on this disk)")

    print("PASS test_l0_changepoint_and_silverwolf")


if __name__ == "__main__":
    test_mcp_studio_tools()
    test_l0_changepoint_and_silverwolf()
    print("ALL STUDIO DIRECTOR PYTHON TESTS PASSED")
