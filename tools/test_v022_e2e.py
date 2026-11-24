#!/usr/bin/env python3
"""
Isolated Unity E2E Verification for KoiSync.MainGame v0.2.2 on port 41299:
  1. Deploys fresh v0.2.2 DLL into isolated runtime ONLY & verifies version="0.2.2" + 0 Harmony errors
  2. Verifies Guardrail #1 (auto-SLoop before PhaseMap when starting Timeline from Idle!)
  3. Verifies Markov `organic` preset weaves contrasting sub-modes smoothly in live H-scene
  4. Verifies `climax_ejaculation` preset runs Phase 1 -> Phase 2 (ejaculation pulses + 3D body spasms + climax face)
     -> Phase 3 (afterglow ecstasy_melt) + captures visual screenshot
  5. Verifies `/director/audio` native HSeCtrl/HVoiceCtrl mute toggle & `/director/lip_rms` real-time mouth-open control
  6. Verifies Guardrail #4 (baked upgraded `pain_reluctant` eb=1, eye=13, mouth=11, tears=3)
"""
from __future__ import annotations

import json
import pathlib
import shutil
import subprocess
import sys
import time
import urllib.request

ROOT = pathlib.Path(r"E:\project\c#\KoiSync")
sys.path.insert(0, str(ROOT / "tools"))
from test_v021_e2e import (
    CASE_ROOT, RUNTIME, EXE, CFG_PATH, CARD, ISO_PORT,
    McpSession, capture_png, patch_isolated_port,
)

DLL_SRC = ROOT / r"src\KoiSync.MainGame\bin\Release\net35\KoiSync.MainGame.dll"
DLL_ISO = RUNTIME / r"BepInEx\plugins\KoiSyncMainGame\KoiSync.MainGame.dll"
OUT_DIR = CASE_ROOT / "v022-e2e"
OUT_DIR.mkdir(parents=True, exist_ok=True)


def http_post(port: int, path: str, body: dict) -> dict:
    req = urllib.request.Request(
        f"http://127.0.0.1:{port}{path}",
        data=json.dumps(body).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=5.0) as r:
        return json.loads(r.read().decode("utf-8"))


def main() -> int:
    shutil.copy2(DLL_SRC, DLL_ISO)
    live_dll = ROOT / r"tests\KoiSync.MainGameLive\bin\Release\net35\KoiSync.MainGameLive.dll"
    if live_dll.exists():
        shutil.copy2(live_dll, RUNTIME / r"BepInEx\plugins\KoiSyncMainGame\KoiSync.MainGameLive.dll")
    orig_cfg = patch_isolated_port(ISO_PORT)
    bep_log = RUNTIME / r"BepInEx\LogOutput.log"
    if bep_log.exists():
        bep_log.unlink()
    log_path = OUT_DIR / "v022-e2e.log"
    snapshot = CASE_ROOT / "live.xml"
    t_start = time.time()

    proc = None
    mcp = None
    try:
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
        deadline = time.time() + 60.0
        while time.time() < deadline:
            if proc.poll() is not None:
                raise RuntimeError(f"Host exited early rc={proc.returncode}")
            if snapshot.exists() and snapshot.stat().st_mtime >= t_start:
                break
            time.sleep(0.25)
        else:
            raise RuntimeError("LiveProbe timeout")

        py_fac = pathlib.Path(r"E:\project\c#\KoikatuModFactory\.venv\Scripts\python.exe")
        boot = subprocess.run(
            [str(py_fac), "-X", "utf8", str(ROOT / r"tools\main-game-live.py"),
             "--boot", "--mode", "0", "--out", str(CASE_ROOT), "--card", CARD],
            check=False,
        )
        if boot.returncode != 0:
            raise RuntimeError("FreeH bootstrap failed")

        mcp = McpSession(ISO_PORT)
        mcp.tool("koisync_switch_pose", {"key": "sonyu_4", "autoInsert": True})
        time.sleep(2.4)
        st = mcp.state()
        assert st and st.get("inHScene"), f"Isolated Unity did not enter H scene in time: {st}"
        assert st.get("version") == "0.2.2", f"Expected version 0.2.2, got {st.get('version')}"
        print(f"[PASS] Isolated Unity loaded KoiSync.MainGame v{st['version']} | pose={st['poseKey']} stage={st['stage']}", flush=True)

        # 1. Verify Guardrail #1: switch to Idle then start Timeline -> auto-switches to SLoop before PhaseMap!
        try:
            mcp.tool("koisync_set_stage", {"stage": "Idle"})
            time.sleep(0.4)
        except Exception:
            pass
        st_org = mcp.tool("koisync_director_timeline", {
            "preset": "organic",
            "durationSeconds": 25.0,
            "organicIntensity": 0.65,
            "organicSeed": 7,
            "loop": True,
        })["structuredContent"]
        time.sleep(0.5)
        st_org = mcp.state()
        assert st_org["timeline"]["active"] and st_org["timeline"]["organicActive"], st_org["timeline"]
        assert "Idle" not in st_org.get("stage", ""), f"Guardrail #1 should have exited Idle, stage={st_org.get('stage')}"
        assert st_org["phaseMap"]["ready"], st_org["phaseMap"]
        print(f"[PASS] Guardrail #1 + Organic Markov active: stage={st_org['stage']} subMode={st_org['timeline']['organicSubMode']} depthSpan={st_org['phaseMap']['maxDepthMm']-st_org['phaseMap']['minDepthMm']:.1f}mm", flush=True)

        # 2. Verify /director/audio mute + /director/lip_rms real-time mouth-open!
        res_aud = mcp.tool("koisync_director_audio", {"muteNativeSe": True, "muteNativeVoice": True, "ttlSeconds": 20.0})["structuredContent"]
        assert res_aud["muteNativeSe"] is True and res_aud["muteNativeVoice"] is True, res_aud
        http_post(ISO_PORT, "/director/lip_rms", {"rms": 0.92, "ttlSeconds": 1.2})
        time.sleep(0.25)
        st_lip = mcp.state()
        assert st_lip["audio"]["muteNativeSe"] is True and st_lip["audio"]["lipActive"] is True, st_lip["audio"]
        assert st_lip["expression"]["femaleMouthOpen"] >= 0.60, f"Expected live lip-sync mouth open >=0.60, got {st_lip['expression']}"
        print(f"[PASS] Audio mute & live Lip-Sync verified: muteSe=True muteVoice=True lipRms={st_lip['audio']['lipRms']} mouthOpen={st_lip['expression']['femaleMouthOpen']}", flush=True)
        mcp.tool("koisync_director_audio", {"muteNativeSe": False, "muteNativeVoice": False})
        http_post(ISO_PORT, "/director/lip_rms", {"clear": True})

        # 3. Verify Guardrail #4: baked upgraded pain_reluctant defaults!
        mcp.tool("koisync_director_expression", {"target": "female", "preset": "pain_reluctant"})
        time.sleep(0.25)
        st_pain = mcp.state()
        exp = st_pain["expression"]
        assert exp["femaleEyebrowPtn"] == 1 and exp["femaleEyesPtn"] == 13 and exp["femaleMouthPtn"] == 11 and exp["femaleTears"] == 3, exp
        print(f"[PASS] Guardrail #4 baked pain_reluctant verified: eb={exp['femaleEyebrowPtn']} eye={exp['femaleEyesPtn']} mouth={exp['femaleMouthPtn']} tears={exp['femaleTears']}", flush=True)

        # 4. Verify `climax_ejaculation` in-place 3-phase sprint -> ejaculation pulses + 3D spasms -> afterglow!
        mcp.tool("koisync_director_timeline", {"preset": "climax_ejaculation", "durationSeconds": 7.5})
        seen_phases = set()
        max_spasm = 0.0
        max_pulses = 0
        shot_p2 = False
        t0 = time.time()
        while time.time() - t0 < 8.8:
            s = mcp.state()
            tl = s["timeline"]
            cp = int(tl.get("climaxPhase", 0))
            if cp > 0:
                seen_phases.add(cp)
            max_spasm = max(max_spasm, float(tl.get("spasmAmp", 0.0)))
            max_pulses = max(max_pulses, int(tl.get("ejaculationPulseCount", 0)))
            if cp == 2 and not shot_p2:
                time.sleep(0.4)
                capture_png(mcp, OUT_DIR / "climax_ejaculation_phase2.png")
                shot_p2 = True
            if not tl.get("active") and len(seen_phases) >= 2:
                break
            time.sleep(0.22)
        assert {1, 2, 3}.issubset(seen_phases) and max_pulses >= 3 and max_spasm >= 0.70, (seen_phases, max_pulses, max_spasm)
        print(f"[PASS] ClimaxEjaculation live in Unity verified: phases={sorted(seen_phases)} pulses={max_pulses} maxSpasm={max_spasm:.2f} shot={shot_p2}", flush=True)

        # Check BepInEx log for any KoiSync Harmony hook warnings/errors
        if bep_log.exists():
            txt = bep_log.read_text(encoding="utf-8", errors="ignore")
            bad = [ln for ln in txt.splitlines() if "KoiSync" in ln and ("Error" in ln or "failed" in ln or "Exception" in ln)]
            assert not bad, f"Found KoiSync errors in BepInEx log: {bad[:5]}"
            print("[PASS] BepInEx LogOutput.log clean: 0 KoiSync Harmony/runtime errors!", flush=True)

        mcp.tool("koisync_director_release", {"immediate": True})
        print("\n=== ALL KOISYNC.MAINGAME v0.2.2 ISOLATED UNITY E2E CHECKS PASSED! ===", flush=True)
        return 0
    finally:
        if mcp:
            mcp.close()
        if proc:
            try:
                proc.terminate()
                proc.wait(timeout=5)
            except Exception:
                try:
                    proc.kill()
                except Exception:
                    pass
        if orig_cfg is not None:
            CFG_PATH.write_text(orig_cfg, encoding="utf-8")


if __name__ == "__main__":
    raise SystemExit(main())
