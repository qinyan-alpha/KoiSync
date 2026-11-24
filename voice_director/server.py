#!/usr/bin/env python3
"""
KoiSync.VoiceDirector v0.3.0 Local HTTP Sidecar (127.0.0.1:41295):
  - Mix-coexistence audio policy (default): NEVER mutes or covers Koikatsu's native HSeCtrl or HVoiceCtrl.
  - Full Smart Parameter & Character Director:
      * Reads live /state, /config (6-axis gains/enabled/smoothing), /poses, /game/characters (clothes/outfits/kPlug guests/cards)
      * Executes fine-grained `CMD:{...}` JSON patches: 6-axis config, seamless timelineTune, preset timelines,
        pose switch, clothes undress/dress, outfit slot switch, kPlug invite/panel/swap/body-replace/focus/masturbation,
        expression, and persistent long-term preference memory (`memory_store.json`)!
  - Context-Aware Proactive Check-In Engine (toggleable, non-nagging, waits for Master's confirmation before big moves).
  - High-Accuracy ASR (RTX 4070 CUDA `large-v3-turbo` / `gemini_direct` / `gemini_asr`) + Locked V6 Streaming TTS (ZH/JA).
"""
from __future__ import annotations

import argparse
import json
import os
import pathlib
import signal
import socket
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import List, Optional

if os.name == "nt":
    import msvcrt
else:
    msvcrt = None

ROOT_DIR = pathlib.Path(__file__).resolve().parent.parent
if str(ROOT_DIR) not in sys.path:
    sys.path.insert(0, str(ROOT_DIR))

LAB_DIR = ROOT_DIR / "artifacts" / "voice_lab"
TMP_DIR = LAB_DIR / "tmp"
TMP_DIR.mkdir(parents=True, exist_ok=True)
os.environ.setdefault("HF_HOME", str(LAB_DIR / "hf_cache"))
os.environ.setdefault("HUGGINGFACE_HUB_CACHE", str(LAB_DIR / "hf_cache" / "hub"))
os.environ.setdefault("TORCH_HOME", str(LAB_DIR / "torch_cache"))
os.environ["TMP"] = str(TMP_DIR)
os.environ["TEMP"] = str(TMP_DIR)

UI_HTML_PATH = pathlib.Path(__file__).resolve().parent / "console_ui.html"

import sounddevice as sd

from voice_director.asr_mic import LocalWhisperAsr, list_input_devices, pcm16k_to_wav_bytes
from voice_director.asset_pack import VerifiedAssetBank
from voice_director.autonomy_engine import HeroineAutonomyEngine
from voice_director.chat_brain import CONFIG_PATH, CoPlayChatBrain, default_empty_chat_config, save_chat_config
from voice_director.dsp_mixer import RealtimeBinauralMixer
from voice_director.tts_engine import CUSTOM_MODELS_DIR, LockedV6TtsEngine, ensure_custom_models_dir

OUTFIT_SLOT_LABELS = {0: "校服(第1套)", 1: "第2套校服", 2: "体操服", 3: "泳装", 4: "社团服", 5: "私服", 6: "睡衣"}


def _norm_lang(lang: Optional[str]) -> str:
    s = str(lang or "zh").strip().lower()
    if s.startswith("ja") or s.startswith("jp"):
        return "ja"
    if s.startswith("en"):
        return "en"
    return "zh"


class KoiSyncBridgeClient:
    def __init__(self, base_url: str) -> None:
        self.base_url = base_url.rstrip("/")

    def get_json(self, path: str, timeout: float = 0.25) -> Optional[dict]:
        try:
            req = urllib.request.Request(self.base_url + path, method="GET")
            with urllib.request.urlopen(req, timeout=timeout) as r:
                return json.loads(r.read().decode("utf-8", errors="replace"))
        except Exception:
            return None

    def post_json(self, path: str, payload: dict, timeout: float = 0.55) -> Optional[dict]:
        try:
            data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
            req = urllib.request.Request(
                self.base_url + path,
                data=data,
                headers={"Content-Type": "application/json; charset=utf-8"},
                method="POST",
            )
            with urllib.request.urlopen(req, timeout=timeout) as r:
                return json.loads(r.read().decode("utf-8", errors="replace"))
        except Exception:
            return None


def _is_koikatsu_process_running() -> bool:
    """Checks whether Koikatu.exe or KoikatsuSunshine.exe is currently alive on Windows."""
    if os.name != "nt":
        return True
    try:
        out = subprocess.check_output(
            ["tasklist", "/NH", "/FO", "CSV"],
            stderr=subprocess.DEVNULL,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
            timeout=1.5,
        ).decode("utf-8", errors="ignore").lower()
        return ("koikatu.exe" in out) or ("koikatsusunshine.exe" in out)
    except Exception:
        return True


class VoiceDirectorApp:
    def __init__(
        self,
        koisync_url: str,
        initial_lang: str = "zh",
        preload_tts: bool = True,
        preload_asr: bool = True,
        headless: bool = False,
        auto_exit_on_game_close: bool = True,
    ) -> None:
        self.koisync = KoiSyncBridgeClient(koisync_url)
        self.headless = headless
        self.auto_exit_on_game_close = bool(auto_exit_on_game_close and not headless)
        self._ever_connected_to_game = False
        self._disconnected_since = 0.0
        self._auto_exit_triggered = False

        if headless:
            self.out_sr = 48000
            self.device_name = "headless"
        else:
            dev = sd.query_devices(kind="output")
            self.out_sr = int(round(float(dev.get("default_samplerate", 48000))))
            self.device_name = str(dev.get("name", "default"))

        print(f"[AUDIO] Output device: {self.device_name} @ {self.out_sr} Hz", flush=True)
        self.assets = VerifiedAssetBank(sr_out=self.out_sr)

        self.mixer = RealtimeBinauralMixer(self.assets, sr=self.out_sr)
        self.mixer.voice_gain = 0.88
        self.brain = CoPlayChatBrain()
        self.autonomy = HeroineAutonomyEngine(self.brain.cfg)
        self._last_autonomy_sync_at = 0.0
        self.voice_input_enabled = bool(self.brain.cfg.get("voiceInputEnabled", True))
        self.silent_mode = bool(self.brain.cfg.get("silentMode", False))
        init_voice_model = str(self.brain.cfg.get("activeVoiceModel") or "")
        self.language = _norm_lang(initial_lang)
        self.tts = LockedV6TtsEngine(initial_language=self.language, initial_model_id=init_voice_model)
        self.asr = LocalWhisperAsr(engine=str(self.brain.cfg.get("asrEngine") or "gpu_turbo"))
        self.asr.barge_in_enabled = bool(self.brain.cfg.get("bargeInEnabled", True))

        # Mix-coexistence defaults (NEVER mute or cover Koikatsu's native sound or voices!)
        self.bg_beds_enabled = False
        self.mute_native_se = False
        self.mute_native_voice = False
        self.duck_native_voice_on_speak = False

        # Proactive check-in: ONLY on climax/ejaculation/finite-action completion OR 10-minute (600s) silence timeout!
        self.proactive_enabled = True
        self.proactive_idle_sec = 600.0
        self._chat_busy = False
        self._last_user_activity_at = time.monotonic()
        self._last_proactive_at = 0.0
        self._proactive_streak = 0
        self._entered_h_at = 0.0
        self._prev_orgasm_f = -1
        self._prev_climax_done = -1
        self._prev_tl_active = False
        self._prev_tl_looping = True
        self._prev_stage = ""
        self._last_native_event_id = 0
        self.recent_native_events: List[dict] = []
        self._pending_proactive_reason = ""
        self._pending_proactive_due = 0.0

        self._stop_event = threading.Event()
        self._speak_thread: Optional[threading.Thread] = None
        self._speak_lock = threading.Lock()
        self._chat_lock = threading.Lock()
        self._last_speak_result: dict = {}

        # Latest UI sync fields (read by in-game Unity HUD & Web Console)
        self.last_user_text = ""
        self.last_reply = ""
        self.last_action_summary = ""
        self.last_proactive_reply = ""

        # Live telemetry snapshots from KoiSync.MainGame
        self.game_connected = False
        self.in_h_scene = False
        self.game_ai_blocked = False
        self._shutdown_callback = None
        self.last_game_state: Optional[dict] = None
        self.last_config_state: Optional[dict] = None
        self.live_poses: List[dict] = []
        self.last_chars_state: Optional[dict] = None
        self.live_pose_name = ""
        self.live_stage = ""
        self.live_is_inserted = False
        self.live_is_kiss = False
        self.live_touch_area = "none"
        self.live_speed = 0.0
        self.live_gauge_f = 0.0
        self.live_climax_phase = 0
        self.active_t1_tier: Optional[str] = None
        self.active_t2_tier: Optional[str] = None

        self._last_cfg_poll = 0.0
        self._last_poses_poll = 0.0
        self._last_chars_poll = 0.0
        self._last_mute_heartbeat = 0.0
        self._last_sent_mute_se: Optional[bool] = None
        self._last_sent_mute_voice: Optional[bool] = None
        self._native_voice_ducked_now = False
        self._speaking_ended_at = 0.0
        self._was_speaking_lip = False

        if not headless:
            self.mixer.start_audio()
        tts_prov = str(self.brain.cfg.get("ttsProvider") or "local").strip().lower()
        if preload_tts and not self.silent_mode and tts_prov == "local":
            threading.Thread(target=self._preload_tts_safe, daemon=True).start()
        if preload_asr and self.voice_input_enabled and self.asr.engine == "gpu_turbo":
            threading.Thread(target=self._preload_asr_safe, daemon=True).start()

        self._poll_thread = threading.Thread(target=self._telemetry_loop, daemon=True)
        self._poll_thread.start()

    def _preload_tts_safe(self) -> None:
        try:
            self.tts.ensure_loaded(self.language)
        except Exception as exc:
            print(f"[TTS] Background preload failed (will retry on demand): {exc}", flush=True)

    def _preload_asr_safe(self) -> None:
        try:
            self.asr.ensure_loaded()
        except Exception as exc:
            print(f"[ASR] Background preload failed (will retry on demand): {exc}", flush=True)

    def _push_native_audio_gate(self, mute_se: bool, mute_voice: bool, force: bool = False) -> None:
        now = time.monotonic()
        changed = (mute_se != self._last_sent_mute_se) or (mute_voice != self._last_sent_mute_voice)
        heartbeat_due = (mute_se or mute_voice) and (now - self._last_mute_heartbeat >= 2.5)
        if not (force or changed or heartbeat_due):
            return
        self._last_sent_mute_se = mute_se
        self._last_sent_mute_voice = mute_voice
        self._last_mute_heartbeat = now
        self._native_voice_ducked_now = bool(mute_voice and not self.mute_native_voice)
        self.koisync.post_json(
            "/director/audio",
            {
                "muteNativeSe": bool(mute_se),
                "muteNativeVoice": bool(mute_voice),
                "ttlSeconds": 6.0 if (mute_se or mute_voice) else 1.5,
            },
        )

    def _on_barge_in(self) -> None:
        print("[BARGE_IN] Detected user speech while AI speaking -> interrupting AI TTS immediately!", flush=True)
        self.stop_speaking()

    def _on_free_mic_utterance(self, mono_16k, peak_db: float) -> None:
        if not self.voice_input_enabled or self._chat_busy:
            return
        if self.mixer.is_speaking:
            self.stop_speaking()
        try:
            self.chat_from_pcm16k(mono_16k, peak_db, language=self.language)
        except Exception as exc:
            print(f"[FREE_MIC] Utterance chat failed: {exc}", flush=True)

    def _sync_autonomy_to_game(self, force: bool = False) -> None:
        if not self.game_connected:
            return
        now = time.monotonic()
        if not force and (now - self._last_autonomy_sync_at) < 5.0:
            return
        self._last_autonomy_sync_at = now
        self.koisync.post_json("/director/autonomy", self.autonomy.to_csharp_payload(), timeout=0.6)

    def _maybe_trigger_proactive(self, now: float) -> None:
        if (
            not self.in_h_scene
            or self.game_ai_blocked
            or not bool(self.brain.cfg.get("executeActions", True))
            or self._chat_busy
            or self.mixer.is_speaking
            or self.asr.recording
        ):
            return
        tts_prov = str(self.brain.cfg.get("ttsProvider") or "local").strip().lower()
        if (not self.silent_mode) and tts_prov == "local" and (not self.tts.ready):
            return

        since_user = now - self._last_user_activity_at

        # A) Phase 2: Pure Female-Initiative / Copilot autonomous beat!
        if self.autonomy.mode in ("copilot", "female_active"):
            fire, _arc = self.autonomy.should_fire_beat(now, self.last_game_state or {}, since_user)
            if fire:
                self._last_proactive_at = now
                threading.Thread(target=self.trigger_autonomy_beat, args=("",), daemon=True).start()
                return

        if not self.proactive_enabled or self.silent_mode:
            return

        reason = ""
        # B) Event-driven trigger: climax/ejaculation or finite action finished!
        if self._pending_proactive_reason and now >= self._pending_proactive_due:
            reason = self._pending_proactive_reason
            self._pending_proactive_reason = ""
            self._pending_proactive_due = 0.0
        else:
            # C) Long 10-minute (default 600s) silence timeout ONLY!
            since_pro = now - self._last_proactive_at if self._last_proactive_at > 0 else 9999.0
            if since_user >= self.proactive_idle_sec and since_pro >= self.proactive_idle_sec and self._proactive_streak < 1:
                reason = f"已经安静了约{int(since_user // 60)}分钟，请结合当前场景与长期记忆轻声问候主人，温柔询问下一步想怎么玩（不要擅自切动作）"

        if not reason:
            return
        self._last_proactive_at = now
        self._proactive_streak += 1
        threading.Thread(target=self.trigger_proactive, args=(reason,), daemon=True).start()

    def _telemetry_loop(self) -> None:
        while not self._stop_event.is_set():
            st = self.koisync.get_json("/state", timeout=0.22)
            now = time.monotonic()
            if st and st.get("ok"):
                self.game_connected = True
                self._ever_connected_to_game = True
                self._disconnected_since = 0.0
                self.last_game_state = st
                self.game_ai_blocked = bool(st.get("aiTakeoverBlocked", False))
                was_in_h = self.in_h_scene
                in_h = bool(st.get("inHScene", False))
                self.in_h_scene = in_h
                if in_h and not was_in_h:
                    self._entered_h_at = now
                    self._last_user_activity_at = now
                    self._proactive_streak = 0
                    self.autonomy.reset_timers(quick_first_beat=(self.autonomy.mode != "player"))
                    self._sync_autonomy_to_game(force=True)
                    self._prev_orgasm_f = int(st.get("orgasmFemaleCount", 0) or 0)
                    self._prev_climax_done = int(st.get("climaxCompletedCount", 0) or 0)
                    self._prev_tl_active = False
                    self._prev_tl_looping = True
                    self._prev_stage = ""
                    self._pending_proactive_reason = ""
                    self._last_poses_poll = 0.0
                    self._last_chars_poll = 0.0

                spd = float(st.get("estSpeedUnitsPerSec", 0.0) or 0.0)
                gf = float(st.get("gaugeFemale", 0.0) or 0.0)
                eng = float(st.get("engagement", 0.0) or 0.0)
                stage = str(st.get("stage", "") or "")
                tl = st.get("timeline") or {}
                cphase = int(tl.get("climaxPhase", 0) or 0)
                tl_active = bool(tl.get("active", False))
                tl_looping = bool(tl.get("looping", False))
                org_f = int(st.get("orgasmFemaleCount", 0) or 0)
                climax_done = int(st.get("climaxCompletedCount", 0) or 0)

                # Consume real native Koikatsu H-Event stream from NativeHEventBus!
                if in_h:
                    latest_ev_id = int(st.get("latestEventId", 0) or 0)
                    if latest_ev_id < self._last_native_event_id:
                        self._last_native_event_id = 0
                    if latest_ev_id > self._last_native_event_id:
                        ev_res = self.koisync.get_json(f"/events?since={self._last_native_event_id}&limit=24", timeout=0.25)
                        new_evs = (ev_res or {}).get("events") if (ev_res and ev_res.get("ok")) else (st.get("recentEvents") or [])
                        for ev in new_evs:
                            eid = int(ev.get("id", 0) or 0)
                            if eid <= self._last_native_event_id:
                                continue
                            self._last_native_event_id = eid
                            self.recent_native_events.append(ev)
                            if len(self.recent_native_events) > 20:
                                self.recent_native_events = self.recent_native_events[-20:]
                            ev_type = str(ev.get("type") or "")
                            if ev_type == "ClimaxCommitted":
                                self._pending_proactive_reason = f"[原生H事件:{ev_type}] 刚刚完成了一次高潮射精（{ev.get('detail','')}），进入温柔余韵，请贴耳轻声关心主人刚才舒不舒服，并询问接下来想歇一会儿、换姿势还是继续"
                                self._pending_proactive_due = now + 1.8
                            elif ev_type == "AfterglowEntered" and not self._pending_proactive_reason:
                                self._pending_proactive_reason = f"[原生H事件:{ev_type}] 刚刚进入事后余韵，请贴耳轻声关心主人感受并询问接下来想怎么玩"
                                self._pending_proactive_due = now + 2.0
                            elif ev_type == "SceneEntered":
                                self._last_poses_poll = 0.0
                                self._last_chars_poll = 0.0
                    # Fallback detection for finite non-looping timeline completion
                    if self._prev_tl_active and (not self._prev_tl_looping) and (not tl_active) and climax_done == self._prev_climax_done:
                        self._pending_proactive_reason = "刚刚设定的这一段节奏动作已经完成，请温柔问问主人感觉怎么样、接下来想换什么节奏或姿势"
                        self._pending_proactive_due = now + 1.2
                    self._prev_orgasm_f = org_f
                    self._prev_climax_done = climax_done
                    self._prev_tl_active = tl_active
                    self._prev_tl_looping = tl_looping
                    self._prev_stage = stage

                self.live_pose_name = str(st.get("poseVariantLabel") or st.get("poseName") or st.get("poseKey") or "")
                self.live_stage = stage
                # STRICT: live_is_inserted ONLY reflects direct genital penetration (isDirectPenetration)!
                self.live_is_inserted = bool(st.get("isDirectPenetration", st.get("isInserted", False)))
                self.live_is_kiss = bool(st.get("isKiss", False))
                self.live_touch_area = str(st.get("touchArea", "none") or "none")
                self.live_speed = spd
                self.live_gauge_f = gf
                self.live_climax_phase = cphase

                # Periodic sync of 6-axis config, full classified pose catalog, and kPlug characters/clothes
                if now - self._last_cfg_poll >= 2.0:
                    self._last_cfg_poll = now
                    cfg_st = self.koisync.get_json("/config", timeout=0.25)
                    if cfg_st and cfg_st.get("ok"):
                        self.last_config_state = cfg_st
                if in_h and now - self._last_poses_poll >= 10.0:
                    self._last_poses_poll = now
                    p_res = self.koisync.get_json("/poses/catalog", timeout=0.40)
                    if p_res and p_res.get("ok") and isinstance(p_res.get("poses"), list):
                        self.live_poses = p_res["poses"]
                if in_h and now - self._last_chars_poll >= 3.5:
                    self._last_chars_poll = now
                    c_res = self.koisync.get_json("/game/characters", timeout=0.35)
                    if c_res and c_res.get("ok"):
                        self.last_chars_state = c_res

                if not in_h or not self.mixer.enabled:
                    self.active_t1_tier = None
                    self.active_t2_tier = None
                    self.mixer.set_scene_beds(None, 0.0, None, 0.0)
                    if self._last_sent_mute_se or self._last_sent_mute_voice:
                        self._push_native_audio_gate(False, False, force=True)
                    if self._was_speaking_lip:
                        self.koisync.post_json("/director/lip_rms", {"clear": True})
                        self._was_speaking_lip = False
                else:
                    if not self.bg_beds_enabled:
                        self.active_t1_tier = None
                        self.active_t2_tier = None
                        self.mixer.set_scene_beds(None, 0.0, None, 0.0)
                    else:
                        idle_stage = ("Idle" in stage or stage.startswith("state_")) and not tl_active
                        moving = (spd >= 12.0 or tl_active) and not idle_stage
                        if cphase in (1, 2):
                            t1_tier, t1_sc = "fast", 1.12
                            t2_tier, t2_sc = "high", 1.00
                        elif cphase == 3:
                            t1_tier, t1_sc = "mid", 0.55
                            t2_tier, t2_sc = "soft", 0.95
                        elif not moving:
                            t1_tier, t1_sc = None, 0.0
                            t2_tier, t2_sc = "soft", 0.85 if eng > 0.1 else 0.55
                        elif spd >= 165.0:
                            t1_tier, t1_sc = "fast", min(1.25, 0.80 + (spd - 165.0) / 300.0)
                            t2_tier, t2_sc = "high", 1.10
                        else:
                            t1_tier, t1_sc = "mid", min(1.05, 0.55 + spd / 180.0)
                            t2_tier = "high" if gf >= 78.0 else ("mid" if (spd >= 40.0 or gf >= 35.0) else "soft")
                            t2_sc = 0.95 if t2_tier == "mid" else (1.05 if t2_tier == "high" else 0.85)
                        self.active_t1_tier = t1_tier
                        self.active_t2_tier = t2_tier
                        self.mixer.set_scene_beds(t1_tier, t1_sc, t2_tier, t2_sc)

                    speaking_now = bool(self.mixer.is_speaking)
                    want_mute_se = bool(self.mute_native_se and self.mixer.enabled)
                    if self.mute_native_voice and self.mixer.enabled:
                        want_mute_voice = True
                        self._speaking_ended_at = 0.0
                    elif self.duck_native_voice_on_speak and self.mixer.enabled:
                        if speaking_now:
                            want_mute_voice = True
                            self._speaking_ended_at = 0.0
                        elif self._native_voice_ducked_now:
                            if self._speaking_ended_at <= 0.0:
                                self._speaking_ended_at = now
                            want_mute_voice = (now - self._speaking_ended_at) < 0.25
                        else:
                            want_mute_voice = False
                    else:
                        want_mute_voice = False
                    self._push_native_audio_gate(want_mute_se, want_mute_voice)

                    lip = self.mixer.current_lip_rms
                    if speaking_now or (self.bg_beds_enabled and lip > 0.03):
                        self.koisync.post_json("/director/lip_rms", {"rms": round(lip, 3), "ttlSeconds": 0.22})
                        self._was_speaking_lip = True
                    elif self._was_speaking_lip:
                        self.koisync.post_json("/director/lip_rms", {"clear": True})
                        self._was_speaking_lip = False

                    self._maybe_trigger_proactive(now)
            else:
                self.game_connected = False
                self.in_h_scene = False
                self.live_is_inserted = False
                self._last_native_event_id = 0
                self.last_game_state = None
                self.live_pose_name = ""
                self.live_stage = ""
                self.live_is_kiss = False
                self.live_touch_area = "none"
                self._last_sent_mute_se = None
                self._last_sent_mute_voice = None
                self._native_voice_ducked_now = False
                self._was_speaking_lip = False
                self.active_t1_tier = None
                self.active_t2_tier = None
                self.mixer.set_scene_beds(None, 0.0, None, 0.0)
                # Auto-shutdown background AI service when Koikatsu exits (frees CUDA VRAM cleanly!)
                if self.auto_exit_on_game_close and self._ever_connected_to_game and (not self._auto_exit_triggered):
                    if self._disconnected_since <= 0.0:
                        self._disconnected_since = now
                    elif (now - self._disconnected_since) >= 2.5:
                        if not _is_koikatsu_process_running():
                            self._auto_exit_triggered = True
                            print("[VOICE_DIRECTOR] Koikatsu game exited; auto-shutting down AI service to free VRAM.", flush=True)
                            if callable(self._shutdown_callback):
                                threading.Thread(target=self._shutdown_callback, daemon=True).start()
                            return
                        else:
                            # Game still alive (e.g. loading scene); re-check in 2.5s
                            self._disconnected_since = now

            time.sleep(0.04)

    def status_dict(self) -> dict:
        tts_prov = str(self.brain.cfg.get("ttsProvider") or "local").strip().lower()
        return {
            "ok": True,
            "version": "0.4.2",
            "mode": "silent_action" if self.silent_mode else (
                "mix_coexist" if (not self.bg_beds_enabled and not self.duck_native_voice_on_speak and not self.mute_native_voice) else (
                    "duck_on_speak" if not self.bg_beds_enabled else "full_binaural_beds"
                )
            ),
            "device": self.device_name,
            "sampleRate": self.out_sr,
            "enabled": self.mixer.enabled,
            "language": self.language,
            "voiceInputEnabled": self.voice_input_enabled,
            "silentMode": self.silent_mode,
            "bargeInEnabled": self.asr.barge_in_enabled,
            "ttsProvider": tts_prov,
            "activeVoiceModel": self.tts.active_model_id,
            "customModelsDir": str(CUSTOM_MODELS_DIR),
            "configPath": str(CONFIG_PATH),
            "ttsReady": True if (tts_prov != "local") else self.tts.ready,
            "ttsLoading": self.tts.loading,
            "ttsError": self.tts.last_error,
            "ttsLoadedLanguage": self.tts.active_lang,
            "ttsVramMB": self.tts.vram_mb,
            "lastTtfaMs": self.tts.last_ttfa_ms,
            "lastRtf": self.tts.last_rtf,
            "asrEngine": self.asr.engine,
            "asrModel": self.asr.loaded_model_name,
            "asrDevice": self.asr.loaded_device,
            "asrReady": self.asr.ready,
            "asrLoading": self.asr.loading,
            "asrRecording": self.asr.recording,
            "asrMicIndex": self.asr.mic_index,
            "asrError": self.asr.last_error,
            "lastPeakDb": self.asr.last_peak_db,
            "freeMicEnabled": self.asr.free_mic_enabled,
            "freeMicThresholdDb": self.asr.free_mic_threshold_db,
            "proactiveEnabled": self.proactive_enabled,
            "proactiveIdleSec": self.proactive_idle_sec,
            "gameConnected": self.game_connected,
            "inHScene": self.in_h_scene,
            "liveIsInserted": self.live_is_inserted,
            "livePoseName": self.live_pose_name,
            "liveStage": self.live_stage,
            "liveIsKiss": self.live_is_kiss,
            "liveTouchArea": self.live_touch_area,
            "liveSpeed": round(self.live_speed, 1),
            "liveGaugeFemale": round(self.live_gauge_f, 1),
            "liveClimaxPhase": self.live_climax_phase,
            "bgBedsEnabled": self.bg_beds_enabled,
            "duckNativeVoiceOnSpeak": self.duck_native_voice_on_speak,
            "nativeVoiceDuckedNow": self._native_voice_ducked_now,
            "isSpeaking": self.mixer.is_speaking,
            "lipRms": round(self.mixer.current_lip_rms, 3),
            "gains": {
                "master": self.mixer.master_gain,
                "sfx": self.mixer.sfx_gain,
                "breath": self.mixer.breath_gain,
                "voice": self.mixer.voice_gain,
            },
            "muteNativeSe": self.mute_native_se,
            "muteNativeVoice": self.mute_native_voice,
            "lastUserText": self.last_user_text,
            "lastReply": self.last_reply,
            "lastActionSummary": self.last_action_summary,
            "lastProactiveReply": self.last_proactive_reply,
            "memories": self.brain.list_memories(),
            "chatConfig": self.brain.public_config(),
            "characters": self.last_chars_state,
            "autonomy": self.autonomy.status_dict(
                kplug_girl_think_paused=bool(((self.last_game_state or {}).get("autonomy") or {}).get("kPlugGirlThinkPaused", False))
            ),
        }

    def update_config(self, body: dict) -> dict:
        cfg_dirty = False
        if "executeActions" in body:
            ex = bool(body["executeActions"])
            self.brain.cfg["executeActions"] = ex
            cfg_dirty = True
            if not ex:
                self.stop_speaking()
                self._pending_proactive_reason = ""
                self._pending_proactive_due = 0.0
        if self.autonomy.update_config(body, self.brain.cfg):
            cfg_dirty = True
            self._sync_autonomy_to_game(force=True)
        if "enabled" in body:
            self.mixer.enabled = bool(body["enabled"])
        if "voiceInputEnabled" in body:
            self.voice_input_enabled = bool(body["voiceInputEnabled"])
            self.brain.cfg["voiceInputEnabled"] = self.voice_input_enabled
            cfg_dirty = True
            if not self.voice_input_enabled:
                self.asr.stop_free_mic()
            elif not self.asr.ready and not self.asr.loading and self.asr.engine == "gpu_turbo":
                threading.Thread(target=self._preload_asr_safe, daemon=True).start()
        if "silentMode" in body:
            self.silent_mode = bool(body["silentMode"])
            self.brain.cfg["silentMode"] = self.silent_mode
            cfg_dirty = True
            tts_prov = str(self.brain.cfg.get("ttsProvider") or "local").strip().lower()
            if self.silent_mode:
                self.stop_speaking()
            elif tts_prov == "local" and not self.tts.ready and not self.tts.loading:
                threading.Thread(target=self._preload_tts_safe, daemon=True).start()
        if "activeVoiceModel" in body and str(body["activeVoiceModel"] or "").strip():
            sel = self.tts.select_model(str(body["activeVoiceModel"]).strip(), preload=False)
            if sel.get("ok"):
                self.brain.cfg["activeVoiceModel"] = self.tts.active_model_id
                self.language = self.tts.active_lang or self.language
                cfg_dirty = True
                tts_prov = str(self.brain.cfg.get("ttsProvider") or "local").strip().lower()
                if not self.silent_mode and tts_prov == "local":
                    threading.Thread(target=self._preload_tts_safe, daemon=True).start()
        if "bargeInEnabled" in body:
            self.asr.barge_in_enabled = bool(body["bargeInEnabled"])
            self.brain.cfg["bargeInEnabled"] = self.asr.barge_in_enabled
            cfg_dirty = True
        if "asrEngine" in body:
            self.asr.set_engine(str(body["asrEngine"]))
            self.brain.cfg["asrEngine"] = self.asr.engine
            cfg_dirty = True
        if "freeMicThresholdDb" in body:
            self.asr.free_mic_threshold_db = max(-52.0, min(-12.0, float(body["freeMicThresholdDb"])))
        if "freeMicEnabled" in body:
            want_free = bool(body["freeMicEnabled"]) and self.voice_input_enabled
            if want_free:
                dev_i = body.get("deviceIndex")
                self.asr.start_free_mic(
                    on_utterance=self._on_free_mic_utterance,
                    is_speaking_fn=lambda: bool(self.mixer.is_speaking),
                    is_busy_fn=lambda: bool(self._chat_busy),
                    on_barge_in=self._on_barge_in,
                    device_index=dev_i,
                )
            else:
                self.asr.stop_free_mic()
        if "proactiveEnabled" in body:
            self.proactive_enabled = bool(body["proactiveEnabled"])
            self._proactive_streak = 0
        if "proactiveIdleSec" in body:
            self.proactive_idle_sec = max(60.0, min(3600.0, float(body["proactiveIdleSec"])))
        if "bgBedsEnabled" in body:
            self.bg_beds_enabled = bool(body["bgBedsEnabled"])
            if not self.bg_beds_enabled:
                self.active_t1_tier = None
                self.active_t2_tier = None
                self.mixer.set_scene_beds(None, 0.0, None, 0.0)
        if "duckNativeVoiceOnSpeak" in body:
            self.duck_native_voice_on_speak = bool(body["duckNativeVoiceOnSpeak"])
        if "masterGain" in body:
            self.mixer.master_gain = max(0.0, min(2.0, float(body["masterGain"])))
        if "sfxGain" in body:
            self.mixer.sfx_gain = max(0.0, min(1.0, float(body["sfxGain"])))
        if "breathGain" in body:
            self.mixer.breath_gain = max(0.0, min(1.0, float(body["breathGain"])))
        if "voiceGain" in body:
            self.mixer.voice_gain = max(0.0, min(2.0, float(body["voiceGain"])))
        if "muteNativeSe" in body:
            self.mute_native_se = bool(body["muteNativeSe"])
        if "muteNativeVoice" in body:
            self.mute_native_voice = bool(body["muteNativeVoice"])
        if "language" in body:
            lang = _norm_lang(body["language"])
            self.language = lang
            if self.tts.active_model_id.startswith("builtin:"):
                self.tts.active_model_id = {"zh": "builtin:zh_serena", "ja": "builtin:ja_falafel", "en": "builtin:en_serena"}[lang]
                self.brain.cfg["activeVoiceModel"] = self.tts.active_model_id
                cfg_dirty = True
            if not self.silent_mode and str(self.brain.cfg.get("ttsProvider") or "local").lower() == "local":
                threading.Thread(target=self.tts.ensure_loaded, args=(lang,), daemon=True).start()
        if cfg_dirty:
            save_chat_config(self.brain.cfg)
        if self.game_connected:
            eff_voice_mute = bool(
                self.mixer.enabled
                and (self.mute_native_voice or (self.duck_native_voice_on_speak and self.mixer.is_speaking))
            )
            self._push_native_audio_gate(bool(self.mute_native_se and self.mixer.enabled), eff_voice_mute, force=True)
        return self.status_dict()

    def _resolve_pose_key(self, name_or_key: str) -> str:
        q = (name_or_key or "").strip()
        if not q:
            return ""
        ql = q.lower()
        # 1) Exact key match
        for p in self.live_poses:
            if str(p.get("key", "")).lower() == ql:
                return str(p["key"])
        # 2) Sort poses face-to-face first (facingRank, classicRank, id)
        explicit_back = any(w in q for w in ("背面", "反向", "后背", "後背", "背身", "后入", "バック", "狗爬"))
        ranked = sorted(
            self.live_poses,
            key=lambda x: (
                0 if (explicit_back and x.get("facing") == "back") else (int(x.get("facingRank", 1)) if not explicit_back else 2),
                int(x.get("classicRank", 2)),
                int(x.get("id", 999)),
            ),
        )
        # 3) Exact variantLabel / name / gameName match
        for p in ranked:
            for fld in ("variantLabel", "name", "gameName"):
                if str(p.get(fld, "")).strip().lower() == ql:
                    return str(p["key"])
        # 4) Substring match on variantLabel / name / gameName (face-to-face prioritized!)
        for p in ranked:
            hay = f"{p.get('variantLabel','')} {p.get('name','')} {p.get('gameName','')}".lower()
            if ql in hay:
                return str(p["key"])
        # 5) Family / Simplified <-> Japanese alias fallback
        alias_map = (
            (("正常", "传教"), ("正常位/传教士", "正常位", "開脚正常位", "机寝位", "椅子対面")),
            (("骑乘", "乘骑", "騎乗", "女上位", "上位", "坐上来", "骑上去", "cowgirl"), ("骑乘位/女上位", "騎乗位", "ソファ騎乗位")),
            (("后背", "後背", "背后", "后入", "doggy"), ("后背位", "後背位", "バック", "椅子バック", "壁バック", "机バック")),
            (("面对面", "对面"), ("面对面/站立与坐姿", "対面", "椅子対面", "駅弁", "正常位")),
            (("侧卧", "侧位", "側位"), ("侧位", "側位", "机側位", "寝バック")),
            (("立位", "站立", "站着"), ("面对面/站立与坐姿", "立位", "駅弁", "壁対面", "壁バック")),
            (("爱抚", "前戏"), ("爱抚", "愛撫")),
            (("口交", "深喉"), ("口交/深喉", "フェラ")),
            (("乳交",), ("乳交", "パイズリ")),
            (("手交", "足交", "素股", "侍奉"), ("手交/爱抚侍奉", "足交/素股", "手コキ")),
        )
        for keys, targets in alias_map:
            if any(k in q for k in keys):
                for term in targets:
                    for p in ranked:
                        if term == p.get("family") or term in str(p.get("variantLabel", "")) or term in str(p.get("name", "")):
                            return str(p["key"])
        return q

    def execute_smart_command(self, cmd: Optional[dict], execute_game: bool = True, proactive_only: bool = False, autonomous_beat: bool = False) -> dict:
        if not isinstance(cmd, dict) or not cmd:
            return {"executed": False, "summary": ""}

        # Defensive normalization in case an LLM accidentally nests timelineTune/smoothingMs inside config or axes
        cmd = dict(cmd)
        if isinstance(cmd.get("config"), dict):
            cfg_copy = dict(cmd["config"])
            for top_k in ("timelineTune", "timeline", "mathLab", "autonomy", "clothes", "outfit", "chara", "switchPose", "autoInsert", "gameAction", "expr", "remember"):
                if top_k in cfg_copy and top_k not in cmd:
                    cmd[top_k] = cfg_copy.pop(top_k)
            if isinstance(cfg_copy.get("axes"), dict):
                ax_copy = dict(cfg_copy["axes"])
                for cfg_k in ("smoothingMs", "feelPreset", "outerCeilingL0", "nonShaftMode"):
                    if cfg_k in ax_copy and cfg_k not in cfg_copy:
                        cfg_copy[cfg_k] = ax_copy.pop(cfg_k)
                cfg_copy["axes"] = ax_copy
            cmd["config"] = cfg_copy

        # Standard proactive check-ins NEVER alter motion/pose/clothes; autonomous beats pass through the
        # 4 independent non-mutually-exclusive permission gates (allowAutoPose/ClimaxEdge/Clothes/KPlugSwap)!
        if autonomous_beat:
            cmd, _blocked = self.autonomy.filter_autonomous_cmd(cmd)
        elif proactive_only:
            cmd = {"expr": cmd.get("expr", "none")}

        notes: List[str] = []
        executed = False

        # 1) Long-term preference memory always saves even outside H-scene!
        rem = str(cmd.get("remember") or "").strip()
        if rem:
            if self.brain.add_memory(rem):
                notes.append("记下偏好")

        # 1.5) Voice-agreed Autonomy Mode & 4 independent permissions update (works even before entering H-scene!)
        aut_patch = cmd.get("autonomy")
        if isinstance(aut_patch, dict) and aut_patch and not proactive_only and not autonomous_beat:
            if self.autonomy.update_config(aut_patch, self.brain.cfg):
                save_chat_config(self.brain.cfg)
            self._sync_autonomy_to_game(force=True)
            m_lbl = {"player": "听从主导", "copilot": "协同提议", "female_active": "纯女方主导"}.get(self.autonomy.mode, self.autonomy.mode)
            aut_bits = [f"主权:{m_lbl}"]
            for k, lbl in (
                ("allowAutoPose", "换位"),
                ("allowAutoClimaxEdge", "控射寸止"),
                ("allowAutoClothes", "换装"),
                ("allowAutoKPlugSwap", "拉人"),
            ):
                if k in aut_patch:
                    aut_bits.append(f"{'开' if aut_patch[k] else '关'}{lbl}")
            notes.append("·".join(aut_bits))
            executed = True

        in_game = execute_game and self.game_connected and self.in_h_scene and (not self.game_ai_blocked)
        if not in_game:
            if self.game_ai_blocked and execute_game:
                notes.append("AI接管已在游戏内断开")
                return {"executed": bool(rem), "summary": " · ".join(notes)}
            # Summarize intended adjustments for standalone preview clarity
            if cmd.get("config"):
                notes.append("六轴参数(待连游戏)")
            if cmd.get("gameAction"):
                notes.append(f"动作:{cmd['gameAction']}(待连游戏)")
            if cmd.get("timeline") or cmd.get("timelineTune"):
                notes.append("节奏微调(待连游戏)")
            if cmd.get("clothes"):
                notes.append("脱穿衣(待连游戏)")
            if cmd.get("outfit"):
                notes.append("换装(待连游戏)")
            if cmd.get("switchPose"):
                notes.append(f"切体位:{cmd['switchPose']}(待连游戏)")
            if cmd.get("chara"):
                notes.append(f"角色:{cmd['chara'].get('action')}(待连游戏)")
            return {"executed": bool(rem), "summary": " · ".join(notes)}

        # 2) Release director override if requested
        if cmd.get("release"):
            self.koisync.post_json("/director/release", {"fadeSeconds": 0.6})
            notes.append("释放接管")
            executed = True

        # 3) Six-axis / smoothing / feel config patch
        cfg_patch = cmd.get("config")
        if isinstance(cfg_patch, dict) and cfg_patch:
            payload = dict(cfg_patch)
            payload.setdefault("save", True)
            res = self.koisync.post_json("/config", payload)
            if res and res.get("ok"):
                self.last_config_state = res
                executed = True
                ax = cfg_patch.get("axes")
                if isinstance(ax, dict):
                    for k, v in ax.items():
                        if isinstance(v, dict):
                            if v.get("enabled") is False:
                                notes.append(f"关{k}")
                            elif v.get("enabled") is True:
                                notes.append(f"开{k}")
                            elif "gain" in v:
                                notes.append(f"{k}×{v['gain']}")
                if "smoothingMs" in cfg_patch:
                    notes.append(f"平滑{int(float(cfg_patch['smoothingMs']))}ms")
                if "feelPreset" in cfg_patch:
                    notes.append(f"体感:{cfg_patch['feelPreset']}")

        # 4) Switch pose: Option B (default autoInsert=False -> prepare pose in Idle & await confirmation;
        #    autoInsert=True ONLY when explicitly requested!)
        switched_pose = False
        sp_raw = cmd.get("switchPose")
        sp = ""
        auto_insert = bool(cmd.get("autoInsert", False))
        anal_flag = bool(cmd.get("anal", False))
        if isinstance(sp_raw, dict):
            sp = str(sp_raw.get("key") or sp_raw.get("pose") or "").strip()
            if "autoInsert" in sp_raw:
                auto_insert = bool(sp_raw.get("autoInsert"))
            if "anal" in sp_raw:
                anal_flag = bool(sp_raw.get("anal"))
        elif sp_raw:
            sp = str(sp_raw).strip()

        if sp:
            key = self._resolve_pose_key(sp)
            if key:
                r = self.koisync.post_json(
                    "/game/pose",
                    {"key": key, "anal": anal_flag, "autoInsert": auto_insert, "startMotion": auto_insert},
                    timeout=1.5,
                )
                if r and r.get("ok"):
                    disp = str(r.get("resolvedName") or sp)
                    notes.append(f"{'切体位并插入' if auto_insert else '切体位(待机)'}:{disp}")
                    executed = True
                    switched_pose = True

        # 4.5) Direct native H gameAction (start_insert / aibu_attach / start_motion / finish_inside / etc.)
        ga = str(cmd.get("gameAction") or "").strip()
        if ga:
            # Skip redundant start_insert if switchPose already queued IE_AutoInsertAndStart
            if not (switched_pose and ga.lower() in ("start_insert", "insert", "start_motion", "wloop")):
                r = self.koisync.post_json("/game/action", {"action": ga}, timeout=1.2)
                if r and r.get("ok"):
                    ga_labels = {
                        "start_insert": "立即插入开始",
                        "insert": "立即插入开始",
                        "aibu_attach": "开始爱抚",
                        "start_motion": "开始律动",
                        "finish_inside": "体内射精",
                        "finish_outside": "体外射精",
                        "pull_out": "抽出",
                    }
                    notes.append(ga_labels.get(ga.lower(), f"动作:{ga}"))
                    executed = True

        # 5) Timeline launch or seamless live tune — STRICT INSERTION GUARDRAIL!
        # Insertion-only timeline variants MUST NEVER fire while not inserted (or right after a fresh switchPose).
        tl = cmd.get("timeline")
        tl_tune = cmd.get("timelineTune")
        tl_active = bool(((self.last_game_state or {}).get("timeline") or {}).get("active", False))
        can_run_timeline = bool(self.live_is_inserted) and not switched_pose

        if isinstance(tl, dict) and tl:
            if not can_run_timeline:
                print(f"[DIRECTOR] Guardrail blocked timeline={tl.get('preset')} (isInserted={self.live_is_inserted}, switchedPose={switched_pose})", flush=True)
            else:
                preset_str = str(tl.get("preset", "") or "").lower()
                if preset_str.startswith("climax"):
                    r = self.koisync.post_json("/game/action", {"action": "seamless_climax"}, timeout=1.2)
                    if r and r.get("ok"):
                        notes.append("原生无缝高潮射精")
                        executed = True
                else:
                    payload = dict(tl)
                    payload.setdefault("durationSeconds", 45.0)
                    payload.setdefault("loop", True)
                    r = self.koisync.post_json("/director/timeline", payload)
                    if r and r.get("ok"):
                        if "shallowCount" in payload:
                            notes.append(f"{payload['shallowCount']}浅{payload.get('deepCount', 1)}深")
                        elif payload.get("preset") == "waist_only" or "holdDepth" in payload:
                            notes.append(f"定深摆腰:{payload.get('grindPattern', 'line_fb')}")
                        else:
                            notes.append(f"节奏:{payload.get('preset', 'custom')}")
                        executed = True
        elif isinstance(tl_tune, dict) and tl_tune:
            if not can_run_timeline:
                print(f"[DIRECTOR] Guardrail blocked timelineTune (isInserted={self.live_is_inserted}, switchedPose={switched_pose})", flush=True)
            else:
                if tl_active:
                    r = self.koisync.post_json("/director/timeline/tune", tl_tune)
                else:
                    # Smart preset inference when tuning while native animation runs (use custom, not organic, so edits take effect immediately)
                    inferred = "shallow_deep" if ("shallowCount" in tl_tune or "deepCount" in tl_tune) else (
                        "waist_only" if "holdDepth" in tl_tune else "custom"
                    )
                    payload = {"preset": inferred, "durationSeconds": 45.0, "loop": True, **tl_tune}
                    r = self.koisync.post_json("/director/timeline", payload)
                if r and r.get("ok"):
                    bits = []
                    if "shallowCount" in tl_tune:
                        bits.append(f"{tl_tune['shallowCount']}浅{tl_tune.get('deepCount', 1)}深")
                    if "freqHz" in tl_tune:
                        bits.append(f"频{tl_tune['freqHz']}Hz")
                    if "depthMin" in tl_tune or "depthMax" in tl_tune or "holdDepth" in tl_tune:
                        bits.append("调深浅")
                    if "twistAmp" in tl_tune and float(tl_tune["twistAmp"]) <= 0.05:
                        bits.append("停旋转")
                    if "grindPattern" in tl_tune:
                        bits.append(f"轨迹:{tl_tune['grindPattern']}")
                    if "grindAmp" in tl_tune:
                        bits.append(f"摆幅{tl_tune['grindAmp']}")
                    notes.append(" · ".join(bits) if bits else "微调节奏")
                    executed = True
        elif "gameSpeed" in cmd and cmd["gameSpeed"] is not None:
            spd = max(0.0, min(1.0, float(cmd["gameSpeed"])))
            self.koisync.post_json("/game/speed", {"speed": spd, "holdSeconds": 25.0})
            notes.append(f"原生速{int(spd*100)}%")
            executed = True

        # 5.5) Phase 3 SR6 6-DOF Mathematical Limit Trajectory Engine — STRICT DIRECT-PENETRATION GUARDRAIL!
        mlab = cmd.get("mathLab")
        if isinstance(mlab, dict) and mlab:
            if mlab.get("stop"):
                r = self.koisync.post_json("/director/math_lab", {"stop": True})
                if r and r.get("ok"):
                    notes.append("停止SR6数学轨迹")
                    executed = True
            elif not can_run_timeline:
                print(f"[DIRECTOR] Guardrail blocked mathLab={mlab.get('family')} (isInserted={self.live_is_inserted}, switchedPose={switched_pose})", flush=True)
            else:
                r = self.koisync.post_json("/director/math_lab", mlab)
                if r and r.get("ok"):
                    notes.append(f"SR6数学:{mlab.get('family', 'virtual_pivot')}")
                    executed = True

        # 6) Clothes undress / half-undress / dress / single-piece or multi-piece control
        cloth = cmd.get("clothes")
        if isinstance(cloth, dict) and cloth:
            payload_c = dict(cloth)
            # Accept friendly aliases like piece/item -> part
            if "part" not in payload_c and "piece" in payload_c:
                payload_c["part"] = payload_c.pop("piece")
            if "part" not in payload_c and "item" in payload_c:
                payload_c["part"] = payload_c.pop("item")
            r = self.koisync.post_json("/game/clothes", payload_c, timeout=1.0)
            if r and r.get("ok"):
                notes.append(f"服装:{r.get('detail', payload_c.get('mode'))}")
                executed = True
                self._last_chars_poll = 0.0

        # 7) Outfit slot switch (第一套/第二套/校服/泳装/睡衣...)
        outf = cmd.get("outfit")
        if isinstance(outf, dict) and outf:
            slot = int(outf.get("slot", 0))
            r = self.koisync.post_json("/game/outfit", outf, timeout=1.5)
            if r and r.get("ok"):
                notes.append(f"换装:{r.get('detail', OUTFIT_SLOT_LABELS.get(slot, slot))}")
                executed = True

        # 8) kPlug & native character commands (invite / panel / swap / replace_body / focus / behavior)
        ch = cmd.get("chara")
        if isinstance(ch, dict) and ch and ch.get("action"):
            r = self.koisync.post_json("/game/characters", ch, timeout=2.5)
            if r and r.get("ok"):
                act_name = str(ch.get("action"))
                who = str(r.get("character") or ch.get("name") or "")
                label_map = {
                    "open_invite_panel": "打开换人面板",
                    "close_invite_panel": "关闭换人面板",
                    "invite": f"kPlug邀请:{who}",
                    "swap_main_guest": f"切主角:{who}",
                    "replace_body": f"换角色身体:{who}",
                    "focus": f"定位镜头:{who}",
                    "behavior": f"行为:{ch.get('behavior', '')}({who})",
                }
                notes.append(label_map.get(act_name, act_name))
                executed = True
                self._last_chars_poll = 0.0

        # 9) Expression
        expr = str(cmd.get("expr") or "none").strip().lower()
        if expr and expr != "none" and not cmd.get("release"):
            self.koisync.post_json("/director/expression", {"target": "both", "preset": expr})
            executed = True

        return {"executed": executed, "summary": " · ".join(notes)}

    def execute_quick_preset(self, action: str) -> dict:
        act = (action or "").strip().lower()
        mapping = {
            "organic": {"timeline": {"preset": "organic", "durationSeconds": 45.0, "loop": True}},
            "deep_grind": {"timeline": {"preset": "deep_grind", "durationSeconds": 25.0, "loop": True}},
            "nine_shallow": {"timeline": {"preset": "nine_shallow_one_deep", "durationSeconds": 28.0, "loop": True}},
            "tip_rub": {"timeline": {"preset": "tip_rub", "durationSeconds": 30.0, "loop": True}},
            "tease_plunge": {"timeline": {"preset": "tease_plunge", "durationSeconds": 30.0, "loop": True}},
            "turbo_sprint": {"timeline": {"preset": "turbo_sprint", "durationSeconds": 20.0, "loop": True}},
            "climax": {"timeline": {"preset": "climax_ejaculation", "durationSeconds": 14.0, "loop": False}},
            "start_insert": {"gameAction": "start_insert"},
            "aibu_attach": {"gameAction": "aibu_attach"},
            "release": {"release": True},
        }
        if act not in mapping:
            return {"ok": False, "error": f"未知快捷指令: {act}"}
        if not self.game_connected or not self.in_h_scene:
            return {"ok": False, "executed": False, "error": "游戏尚未进入 H 场景"}
        if "timeline" in mapping[act] and not self.live_is_inserted:
            return {"ok": False, "executed": False, "error": "当前尚未插入，插入专属节奏不能在未插入时触发（请先点击一键插入开始）"}
        res = self.execute_smart_command(mapping[act], execute_game=True)
        return {"ok": True, **res}

    def manual_state_dict(self) -> dict:
        cfg = self.last_config_state
        if self.game_connected and cfg is None:
            c = self.koisync.get_json("/config", timeout=0.6)
            if c and c.get("ok"):
                self.last_config_state = c
                cfg = c
        chars = self.last_chars_state
        if self.game_connected and self.in_h_scene and chars is None:
            ch = self.koisync.get_json("/game/characters", timeout=0.8)
            if ch and ch.get("ok"):
                self.last_chars_state = ch
                chars = ch
        return {
            "ok": True,
            "gameConnected": self.game_connected,
            "inHScene": self.in_h_scene,
            "isInserted": self.live_is_inserted,
            "state": self.last_game_state or {},
            "config": cfg or {},
            "poses": self.live_poses or [],
            "characters": chars or {},
        }

    def manual_proxy(self, endpoint: str, body: dict, timeout: float = 1.8) -> dict:
        if not self.game_connected:
            return {"ok": False, "error": "尚未连接到游戏内 KoiSync (127.0.0.1:41289)"}
        if endpoint in ("/director/timeline", "/director/timeline/tune", "/director/math_lab") and not self.live_is_inserted and not body.get("stop"):
            return {"ok": False, "error": "当前未处于直接性器官插入态：SR6 数学轨迹与插入变式仅限直接插入态使用"}
        res = self.koisync.post_json(endpoint, body, timeout=timeout)
        if res is None:
            return {"ok": False, "error": f"请求 {endpoint} 超时或游戏未响应"}
        if endpoint == "/config" and res.get("ok"):
            self.last_config_state = res
        elif endpoint == "/game/characters" and res.get("ok"):
            self._last_chars_poll = 0.0
        return res

    def speak(
        self,
        text: str,
        language: Optional[str] = None,
        intensity: str = "auto",
        interrupt: bool = True,
        wait: bool = False,
    ) -> dict:
        lang = _norm_lang(language or self.language)
        self.language = lang
        if self.silent_mode:
            return {"ok": True, "silent": True, "language": lang, "text": text, "ttfaMs": 0.0}
        if intensity == "auto":
            if self.live_climax_phase in (1, 2) or self.live_speed >= 165.0 or self.live_gauge_f >= 78.0:
                eff_int = "high"
            elif self.live_speed >= 25.0 or self.live_gauge_f >= 30.0:
                eff_int = "mid"
            else:
                eff_int = "soft"
        else:
            eff_int = intensity

        if interrupt:
            self.tts.cancel_active()
            self.mixer.begin_voice_stream(interrupt=True)

        first_chunk_evt = threading.Event()
        res_holder: dict = {}
        api_cfg = self.brain.get_tts_api_config()

        def _worker():
            try:
                with self._speak_lock:
                    self.mixer.begin_voice_stream(interrupt=interrupt)

                    def _on_chunk(chunk_24k, sr, is_first):
                        if is_first and self.game_connected and self.in_h_scene and self.duck_native_voice_on_speak and self.mixer.enabled:
                            self._speaking_ended_at = 0.0
                            self._push_native_audio_gate(bool(self.mute_native_se and self.mixer.enabled), True)
                        self.mixer.push_voice_chunk_24k(chunk_24k, is_first=is_first)
                        if is_first:
                            first_chunk_evt.set()

                    res = self.tts.stream_speak(text, lang, eff_int, _on_chunk, api_cfg=api_cfg)
                    res_holder.update(res)
                    self._last_speak_result = res
            except Exception as exc:
                err_res = {"ok": False, "error": str(exc)}
                res_holder.update(err_res)
                self._last_speak_result = err_res
                print(f"[TTS] speak worker error: {exc}", flush=True)
            finally:
                first_chunk_evt.set()

        th = threading.Thread(target=_worker, daemon=True)
        self._speak_thread = th
        th.start()
        if wait:
            th.join(timeout=45.0)
            return {**res_holder, "intensity": eff_int}
        first_chunk_evt.wait(timeout=20.0)
        if res_holder.get("ok") is False:
            return {
                "ok": False,
                "streaming": False,
                "error": res_holder.get("error", "TTS failed"),
                "language": lang,
                "intensity": eff_int,
                "text": text,
            }
        return {
            "ok": True,
            "streaming": True,
            "language": lang,
            "intensity": eff_int,
            "text": text,
            "ttfaMs": self.tts.last_ttfa_ms,
        }

    def chat(
        self,
        user_text: str,
        language: Optional[str] = None,
        execute_actions: Optional[bool] = None,
        wait: bool = False,
        is_proactive: bool = False,
        is_autonomous_beat: bool = False,
    ) -> dict:
        with self._chat_lock:
            self._chat_busy = True
            try:
                if not is_proactive and not is_autonomous_beat:
                    self._last_user_activity_at = time.monotonic()
                    self._proactive_streak = 0
                    self.autonomy.on_user_spoke()

                lang = _norm_lang(language or self.language)
                self.language = lang
                do_exec = self.brain.cfg.get("executeActions", True) if execute_actions is None else bool(execute_actions)

                # ACTION-FIRST EXECUTION: Line-1 CMD arrives first -> immediately move device/game BEFORE speaking!
                cmd_state = {"fired_cmd": None, "res": {"executed": False, "summary": ""}}
                tts_state = {"started": False, "res": None, "thread": None}

                def _on_cmd_ready(early_cmd: dict) -> None:
                    if not isinstance(early_cmd, dict) or not early_cmd:
                        return
                    if cmd_state["fired_cmd"] == early_cmd:
                        return
                    cmd_state["fired_cmd"] = dict(early_cmd)
                    cmd_state["res"] = self.execute_smart_command(
                        early_cmd,
                        execute_game=do_exec,
                        proactive_only=is_proactive,
                        autonomous_beat=is_autonomous_beat,
                    )

                def _on_reply_ready(spoken: str) -> None:
                    if self.silent_mode or tts_state["started"] or not spoken:
                        return
                    tts_state["started"] = True
                    def _run_tts():
                        tts_state["res"] = self.speak(spoken, language=lang, intensity="auto", interrupt=True, wait=wait)
                    th = threading.Thread(target=_run_tts, daemon=True)
                    tts_state["thread"] = th
                    th.start()

                brain_out = self.brain.respond(
                    user_text,
                    lang,
                    self.last_game_state,
                    config_state=self.last_config_state,
                    poses=self.live_poses,
                    chars=self.last_chars_state,
                    on_cmd_ready=_on_cmd_ready,
                    on_reply_ready=(None if self.silent_mode else _on_reply_ready),
                )
                reply = str(brain_out.get("reply", "")).strip()
                cmd = brain_out.get("cmd") if isinstance(brain_out.get("cmd"), dict) else {}

                if cmd and cmd != cmd_state["fired_cmd"]:
                    cmd_state["fired_cmd"] = dict(cmd)
                    cmd_state["res"] = self.execute_smart_command(
                        cmd,
                        execute_game=do_exec,
                        proactive_only=is_proactive,
                        autonomous_beat=is_autonomous_beat,
                    )
                exec_res = cmd_state["res"]

                speak_res = {}
                if self.silent_mode:
                    speak_res = {"ok": True, "silent": True, "ttfaMs": 0.0}
                elif tts_state["thread"] is not None:
                    tts_state["thread"].join(timeout=25.0)
                    speak_res = tts_state["res"] or {}
                elif reply:
                    speak_res = self.speak(reply, language=lang, intensity="auto", interrupt=True, wait=wait)

                if not is_proactive and not is_autonomous_beat:
                    self.last_user_text = user_text
                else:
                    self.last_proactive_reply = reply
                self.last_reply = reply
                self.last_action_summary = exec_res.get("summary", "")

                return {
                    "ok": True,
                    "userText": "" if (is_proactive or is_autonomous_beat) else user_text,
                    "reply": reply,
                    "cmd": cmd,
                    "actionExecuted": exec_res.get("executed", False),
                    "actionSummary": exec_res.get("summary", ""),
                    "expression": cmd.get("expr", "none"),
                    "language": lang,
                    "provider": brain_out.get("provider", "builtin"),
                    "brainMs": brain_out.get("brainMs", 0.0),
                    "ttfaMs": speak_res.get("ttfaMs", self.tts.last_ttfa_ms),
                    "usage": brain_out.get("usage", self.brain.last_usage),
                    "memories": self.brain.list_memories(),
                }
            finally:
                self._chat_busy = False

    def trigger_proactive(self, reason: str = "") -> dict:
        title = str(self.brain.cfg.get("userTitle") or "主人").strip() or "主人"
        prompt = f"[系统主动触发: {reason or f'请结合当前场景、在场副角色与长期记忆，温柔地主动问候{title}并给出1-2个贴心微调/体位/互动建议，等对方同意再行动'}]"
        return self.chat(prompt, language=self.language, is_proactive=True)

    def trigger_autonomy_beat(self, reason: str = "") -> dict:
        if self.autonomy.mode == "player":
            return {"ok": False, "skipped": True, "error": "Autonomy mode is player-lead; switch to copilot or female_active first"}
        prompt = self.autonomy.build_beat_instruction(
            self.last_game_state or {},
            self.live_poses,
            self.last_chars_state,
            self.recent_native_events,
            override_reason=reason,
        )
        return self.chat(prompt, language=self.language, is_autonomous_beat=True)

    def chat_from_pcm16k(
        self,
        mono_16k,
        peak_db: float,
        language: Optional[str] = None,
        execute_actions: Optional[bool] = None,
    ) -> dict:
        lang = _norm_lang(language or self.language)
        self.language = lang
        if len(mono_16k) < 2400:
            return {"ok": True, "userText": "", "asrMs": 0.0, "peakDb": peak_db}

        if self.asr.engine == "gemini_direct" and self.brain.get_google_key():
            try:
                with self._chat_lock:
                    self._chat_busy = True
                    try:
                        self._last_user_activity_at = time.monotonic()
                        self._proactive_streak = 0
                        self.autonomy.on_user_spoke()
                        wav_bytes = pcm16k_to_wav_bytes(mono_16k)
                        out = self.brain.respond_to_audio(
                            wav_bytes, lang, self.last_game_state,
                            config_state=self.last_config_state, poses=self.live_poses, chars=self.last_chars_state,
                        )
                        heard = str(out.get("userText", "") or "").strip()
                        reply = str(out.get("reply", "") or "").strip()
                        cmd = out.get("cmd") if isinstance(out.get("cmd"), dict) else {}
                        if not heard and not reply:
                            return {"ok": True, "userText": "", "asrMs": 0.0, "peakDb": peak_db}
                        # ACTION-FIRST: Execute device/game action BEFORE speaking!
                        do_exec = self.brain.cfg.get("executeActions", True) if execute_actions is None else bool(execute_actions)
                        exec_res = self.execute_smart_command(cmd, execute_game=do_exec)
                        speak_res = {"ok": True, "silent": True, "ttfaMs": 0.0} if self.silent_mode else (self.speak(reply, language=lang, intensity="auto", interrupt=True) if reply else {})
                        self.last_user_text = heard
                        self.last_reply = reply
                        self.last_action_summary = exec_res.get("summary", "")
                        return {
                            "ok": True,
                            "userText": heard or "(语音直听)",
                            "reply": reply,
                            "cmd": cmd,
                            "actionExecuted": exec_res.get("executed", False),
                            "actionSummary": exec_res.get("summary", ""),
                            "expression": cmd.get("expr", "none"),
                            "language": lang,
                            "provider": out.get("provider", "gemini-direct-audio"),
                            "asrEngine": "gemini_direct",
                            "asrMs": 0.0,
                            "brainMs": out.get("brainMs", 0.0),
                            "ttfaMs": speak_res.get("ttfaMs", self.tts.last_ttfa_ms),
                            "peakDb": peak_db,
                            "usage": out.get("usage", self.brain.last_usage),
                            "memories": self.brain.list_memories(),
                        }
                    finally:
                        self._chat_busy = False
            except Exception as exc:
                print(f"[ASR] gemini_direct failed ({exc}); falling back to 2-stage ASR+Brain", flush=True)

        asr_url, asr_key, asr_model = self.brain.get_asr_api_params()
        tr = self.asr.transcribe_pcm16k(
            mono_16k,
            language=lang,
            google_api_key=self.brain.get_google_key(),
            asr_api_url=asr_url,
            asr_api_key=asr_key,
            asr_api_model=asr_model,
        )
        txt = str(tr.get("text", "")).strip()
        if not txt:
            return {**tr, "userText": "", "peakDb": peak_db}
        chat_res = self.chat(user_text=txt, language=lang, execute_actions=execute_actions)
        chat_res["asrMs"] = tr.get("asrMs", 0.0)
        chat_res["asrEngine"] = tr.get("engine", self.asr.engine)
        chat_res["peakDb"] = peak_db
        return chat_res

    def models_list_dict(self) -> dict:
        ensure_custom_models_dir()
        return {
            "ok": True,
            "activeVoiceModel": self.tts.active_model_id,
            "ttsReady": self.tts.ready,
            "ttsLoading": self.tts.loading,
            "ttsError": self.tts.last_error,
            "modelsDir": str(CUSTOM_MODELS_DIR),
            "configPath": str(CONFIG_PATH),
            "models": self.tts.list_models(),
        }

    def open_models_dir(self) -> dict:
        d = ensure_custom_models_dir()
        try:
            subprocess.Popen(["explorer.exe", str(d)])
            return {"ok": True, "path": str(d)}
        except Exception as exc:
            return {"ok": False, "path": str(d), "error": str(exc)}

    def open_config_file(self) -> dict:
        if not CONFIG_PATH.is_file():
            save_chat_config(self.brain.cfg)
        try:
            subprocess.Popen(["explorer.exe", f"/select,{CONFIG_PATH}"])
            return {"ok": True, "path": str(CONFIG_PATH)}
        except Exception as exc:
            return {"ok": False, "path": str(CONFIG_PATH), "error": str(exc)}

    def read_raw_config_dict(self) -> dict:
        # Expose editable config JSON (API keys shown blank unless user types new ones, with masked hints)
        editable = dict(self.brain.cfg)
        for k in ("deepseekApiKey", "openaiApiKey", "claudeApiKey", "googleApiKey", "customApiKey", "asrApiKey", "ttsApiKey"):
            editable[k] = ""
        return {
            "ok": True,
            "configPath": str(CONFIG_PATH),
            "modelsDir": str(CUSTOM_MODELS_DIR),
            "jsonText": json.dumps(editable, ensure_ascii=False, indent=2),
            "public": self.brain.public_config(),
        }

    def write_raw_config_text(self, json_text: str, reset_empty: bool = False) -> dict:
        if reset_empty:
            fresh = default_empty_chat_config()
            self.brain.cfg = fresh
            save_chat_config(fresh)
            self.voice_input_enabled = bool(fresh.get("voiceInputEnabled", True))
            self.silent_mode = bool(fresh.get("silentMode", False))
            self.tts.active_model_id = str(fresh.get("activeVoiceModel") or "builtin:zh_serena")
            return {"ok": True, "config": self.brain.public_config(), "detail": "已恢复空白默认配置"}
        try:
            parsed = json.loads(json_text)
            if not isinstance(parsed, dict):
                return {"ok": False, "error": "配置必须是 JSON 对象"}
        except Exception as exc:
            return {"ok": False, "error": f"JSON 语法错误: {exc}"}
        # Preserve existing saved API keys if blank in raw editor unless explicit clearKeys=true
        merged = dict(self.brain.cfg)
        for k, v in parsed.items():
            if k.endswith("ApiKey") and not str(v or "").strip():
                continue
            merged[k] = v
        self.brain.cfg = merged
        save_chat_config(merged)
        self.voice_input_enabled = bool(merged.get("voiceInputEnabled", True))
        self.silent_mode = bool(merged.get("silentMode", False))
        if not self.voice_input_enabled:
            self.asr.stop_free_mic()
        if self.silent_mode:
            self.stop_speaking()
        if "asrEngine" in merged:
            self.asr.set_engine(str(merged["asrEngine"]))
        if "bargeInEnabled" in merged:
            self.asr.barge_in_enabled = bool(merged["bargeInEnabled"])
        if self.autonomy.update_config(merged, self.brain.cfg):
            self._sync_autonomy_to_game(force=True)
        vm = str(merged.get("activeVoiceModel") or "").strip()
        if vm:
            self.tts.select_model(vm, preload=False)
        return {"ok": True, "config": self.brain.public_config(), "detail": "配置文件已保存并生效"}

    def stop_speaking(self) -> dict:
        self.tts.cancel_active()
        self.mixer.clear_voice()
        if self.game_connected:
            self.koisync.post_json("/director/lip_rms", {"clear": True})
            self._was_speaking_lip = False
            if not self.mute_native_voice:
                self._push_native_audio_gate(bool(self.mute_native_se and self.mixer.enabled), False, force=True)
        return {"ok": True, "stopped": True}

    def shutdown(self) -> None:
        self._stop_event.set()
        self.asr.stop_free_mic()
        self.tts.cancel_active()
        self.mixer.stop_audio()
        self.koisync.post_json("/director/audio", {"muteNativeSe": False, "muteNativeVoice": False, "ttlSeconds": 1.0})
        self.koisync.post_json("/director/lip_rms", {"clear": True})


def make_handler(app: VoiceDirectorApp):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, fmt, *args):
            pass

        def _send(self, code: int, obj: dict) -> None:
            raw = json.dumps(obj, ensure_ascii=False).encode("utf-8")
            self.send_response(code)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(raw)))
            self.end_headers()
            self.wfile.write(raw)

        def _send_html(self, code: int, html_bytes: bytes) -> None:
            self.send_response(code)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(html_bytes)))
            self.end_headers()
            self.wfile.write(html_bytes)

        def _read_bytes(self) -> bytes:
            ln = int(self.headers.get("Content-Length", "0") or "0")
            return self.rfile.read(ln) if ln > 0 else b""

        def _read_json(self) -> dict:
            raw = self._read_bytes()
            if not raw:
                return {}
            return json.loads(raw.decode("utf-8", errors="replace"))

        def do_GET(self):
            parsed = urllib.parse.urlsplit(self.path)
            p = parsed.path.rstrip("/") or "/"
            if p in ("/", "/health", "/status"):
                self._send(200, app.status_dict())
            elif p == "/ui":
                if UI_HTML_PATH.is_file():
                    self._send_html(200, UI_HTML_PATH.read_bytes())
                else:
                    self._send(404, {"ok": False, "error": "console_ui.html missing"})
            elif p == "/mics":
                self._send(200, {"ok": True, "activeIndex": app.asr.mic_index, "devices": list_input_devices()})
            elif p == "/chat/config":
                self._send(200, {"ok": True, "config": app.brain.public_config()})
            elif p == "/chat/models":
                self._send(200, app.brain.fetch_available_models())
            elif p == "/memory":
                self._send(200, {"ok": True, "memories": app.brain.list_memories()})
            elif p == "/manual/state":
                self._send(200, app.manual_state_dict())
            elif p == "/models/list":
                self._send(200, app.models_list_dict())
            elif p == "/config/raw":
                self._send(200, app.read_raw_config_dict())
            else:
                self._send(404, {"ok": False, "error": f"Unknown GET {p}"})

        def do_POST(self):
            parsed = urllib.parse.urlsplit(self.path)
            p = parsed.path.rstrip("/") or "/"
            qs = urllib.parse.parse_qs(parsed.query)
            try:
                if p == "/mic/upload":
                    if not app.voice_input_enabled:
                        self._send(200, {"ok": False, "error": "语音输入已在设置中关闭"})
                        return
                    wav_bytes = self._read_bytes()
                    if not wav_bytes:
                        self._send(400, {"ok": False, "error": "empty audio body"})
                        return
                    lang = (qs.get("language") or [app.language])[0]
                    mono_16k, peak_db = app.asr.decode_wav_bytes_to_16k(wav_bytes)
                    if (qs.get("transcribe_only") or ["0"])[0] == "1":
                        tr = app.asr.transcribe_pcm16k(mono_16k, language=lang, google_api_key=app.brain.get_google_key())
                        self._send(200, {**tr, "userText": tr.get("text", ""), "peakDb": peak_db})
                        return
                    self._send(200, app.chat_from_pcm16k(mono_16k, peak_db, language=lang))
                    return

                body = self._read_json()
                if p == "/config":
                    self._send(200, app.update_config(body))
                elif p == "/config/raw":
                    self._send(200, app.write_raw_config_text(str(body.get("jsonText", "")), reset_empty=bool(body.get("resetEmpty", False))))
                elif p == "/chat/config":
                    cfg_pub = app.brain.update_config(body)
                    if "asrEngine" in body:
                        app.asr.set_engine(str(body["asrEngine"]))
                    if "bargeInEnabled" in body:
                        app.asr.barge_in_enabled = bool(body["bargeInEnabled"])
                    if "voiceInputEnabled" in body:
                        app.voice_input_enabled = bool(body["voiceInputEnabled"])
                        if not app.voice_input_enabled:
                            app.asr.stop_free_mic()
                    if "silentMode" in body:
                        app.silent_mode = bool(body["silentMode"])
                        if app.silent_mode:
                            app.stop_speaking()
                    if "activeVoiceModel" in body and str(body["activeVoiceModel"] or "").strip():
                        app.tts.select_model(str(body["activeVoiceModel"]).strip(), preload=False)
                    self._send(200, {"ok": True, "config": cfg_pub})
                elif p == "/chat/models":
                    self._send(200, app.brain.fetch_available_models(body))
                elif p == "/chat/test":
                    self._send(200, app.brain.test_connection())
                elif p == "/chat/clear":
                    app.brain.clear_history()
                    self._send(200, {"ok": True, "cleared": True})
                elif p == "/models/select":
                    mid = str(body.get("id", "")).strip()
                    res = app.update_config({"activeVoiceModel": mid})
                    self._send(200, {"ok": True, "activeVoiceModel": app.tts.active_model_id, "status": res})
                elif p == "/models/open_dir":
                    self._send(200, app.open_models_dir())
                elif p == "/models/open_config":
                    self._send(200, app.open_config_file())
                elif p == "/memory":
                    act = str(body.get("action", "")).lower()
                    if act == "clear":
                        app.brain.clear_memories()
                    elif act == "delete":
                        app.brain.delete_memory(int(body.get("index", -1)))
                    elif act == "add":
                        app.brain.add_memory(str(body.get("note", "")))
                    self._send(200, {"ok": True, "memories": app.brain.list_memories()})
                elif p == "/proactive":
                    res = app.trigger_proactive(str(body.get("reason", "")))
                    self._send(200, res)
                elif p == "/autonomy":
                    res = app.update_config(body)
                    self._send(200, {"ok": True, "autonomy": res.get("autonomy", {}), "status": res})
                elif p == "/autonomy/beat":
                    res = app.trigger_autonomy_beat(str(body.get("reason", "")))
                    self._send(200, res)
                elif p == "/chat":
                    text = str(body.get("text", "")).strip()
                    if not text:
                        self._send(400, {"ok": False, "error": "text is required"})
                        return
                    res = app.chat(
                        user_text=text,
                        language=body.get("language"),
                        execute_actions=body.get("executeActions"),
                        wait=bool(body.get("wait", False)),
                    )
                    self._send(200, res)
                elif p == "/action":
                    self._send(200, app.execute_quick_preset(str(body.get("action", ""))))
                elif p == "/mic/start":
                    if not app.voice_input_enabled:
                        self._send(200, {"ok": False, "error": "语音输入已在设置中关闭（可在设置页重新开启）"})
                        return
                    # Immediately interrupt AI speech when user starts talking!
                    if app.asr.barge_in_enabled:
                        app.stop_speaking()
                    app._last_user_activity_at = time.monotonic()
                    self._send(200, app.asr.start_mic_record(device_index=body.get("deviceIndex")))
                elif p == "/mic/stop":
                    if not app.voice_input_enabled:
                        self._send(200, {"ok": False, "error": "语音输入已在设置中关闭"})
                        return
                    lang = body.get("language") or app.language
                    mono_16k, peak_db = app.asr.stop_mic_record_pcm16k()
                    self._send(200, app.chat_from_pcm16k(mono_16k, peak_db, language=lang, execute_actions=body.get("executeActions")))
                elif p == "/speak":
                    text = str(body.get("text", "")).strip()
                    if not text:
                        self._send(400, {"ok": False, "error": "text is required"})
                        return
                    self._send(200, app.speak(
                        text=text,
                        language=body.get("language"),
                        intensity=str(body.get("intensity", "auto")),
                        interrupt=bool(body.get("interrupt", True)),
                        wait=bool(body.get("wait", False)),
                    ))
                elif p == "/stop":
                    self._send(200, app.stop_speaking())
                elif p == "/shutdown":
                    self._send(200, {"ok": True, "shuttingDown": True})
                    if callable(app._shutdown_callback):
                        threading.Thread(target=app._shutdown_callback, daemon=True).start()
                elif p == "/manual/config":
                    self._send(200, app.manual_proxy("/config", body))
                elif p == "/manual/timeline":
                    self._send(200, app.manual_proxy("/director/timeline", body))
                elif p == "/manual/timeline_tune":
                    self._send(200, app.manual_proxy("/director/timeline/tune", body))
                elif p == "/manual/math_lab":
                    self._send(200, app.manual_proxy("/director/math_lab", body))
                elif p == "/manual/pose":
                    if "key" in body:
                        body = {**body, "key": app._resolve_pose_key(str(body.get("key", "")))}
                    self._send(200, app.manual_proxy("/game/pose", body))
                elif p == "/manual/clothes":
                    self._send(200, app.manual_proxy("/game/clothes", body))
                elif p == "/manual/outfit":
                    self._send(200, app.manual_proxy("/game/outfit", body))
                elif p == "/manual/characters":
                    self._send(200, app.manual_proxy("/game/characters", body, timeout=2.5))
                elif p == "/manual/expression":
                    self._send(200, app.manual_proxy("/director/expression", body))
                elif p == "/manual/game_action":
                    self._send(200, app.manual_proxy("/game/action", body))
                elif p == "/manual/speed":
                    self._send(200, app.manual_proxy("/game/speed", body))
                elif p == "/manual/release":
                    self._send(200, app.manual_proxy("/director/release", body or {"fadeSeconds": 0.6}))
                else:
                    self._send(404, {"ok": False, "error": f"Unknown POST {p}"})
            except Exception as exc:
                self._send(500, {"ok": False, "error": str(exc)})

    return Handler


def _acquire_single_instance_lock(host: str, port: int):
    """Strict OS-level single-instance guard BEFORE Whisper/TTS CUDA models load (prevents multi-CUDA VRAM freeze!)."""
    lock_path = TMP_DIR / f"voice_director_{port}.lock"
    lock_fh = None
    try:
        lock_fh = open(lock_path, "a+b")
        if msvcrt is not None:
            lock_fh.seek(0)
            msvcrt.locking(lock_fh.fileno(), msvcrt.LK_NBLCK, 1)
    except OSError:
        try:
            if lock_fh is not None:
                lock_fh.close()
        except Exception:
            pass
        return None

    # Double-check that the TCP port is genuinely free before loading multi-GB CUDA models
    probe = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        probe.bind((host, port))
    except OSError:
        probe.close()
        try:
            if msvcrt is not None:
                lock_fh.seek(0)
                msvcrt.locking(lock_fh.fileno(), msvcrt.LK_UNLCK, 1)
            lock_fh.close()
        except Exception:
            pass
        return None
    probe.close()

    try:
        lock_fh.seek(0)
        lock_fh.truncate()
        lock_fh.write(f"{os.getpid()}\n".encode("ascii"))
        lock_fh.flush()
    except Exception:
        pass
    return lock_fh


def _release_single_instance_lock(lock_fh) -> None:
    if lock_fh is None:
        return
    try:
        if msvcrt is not None:
            lock_fh.seek(0)
            msvcrt.locking(lock_fh.fileno(), msvcrt.LK_UNLCK, 1)
    except Exception:
        pass
    try:
        lock_fh.close()
    except Exception:
        pass


class ExclusiveThreadingHTTPServer(ThreadingHTTPServer):
    allow_reuse_address = False


def main() -> int:
    ap = argparse.ArgumentParser(description="KoiSync.VoiceDirector v0.4.1 action-first, multi-provider & universal voice/TTS API sidecar")
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=41295)
    ap.add_argument("--koisync-url", default="http://127.0.0.1:41289")
    ap.add_argument("--lang", choices=["zh", "ja", "en"], default="zh")
    ap.add_argument("--no-preload", action="store_true")
    ap.add_argument("--no-asr-preload", action="store_true")
    ap.add_argument("--headless", action="store_true")
    ap.add_argument("--keep-alive", action="store_true", help="Do not auto-shutdown when Koikatsu exits")
    args = ap.parse_args()

    lock_fh = _acquire_single_instance_lock(args.host, args.port)
    if lock_fh is None:
        print(
            f"[VOICE_DIRECTOR] Another instance is already starting or listening on {args.host}:{args.port}. "
            "Refusing duplicate startup to protect GPU VRAM!",
            flush=True,
        )
        return 0

    try:
        app = VoiceDirectorApp(
            koisync_url=args.koisync_url,
            initial_lang=args.lang,
            preload_tts=not args.no_preload,
            preload_asr=not args.no_asr_preload,
            headless=args.headless,
            auto_exit_on_game_close=(not args.keep_alive),
        )
        httpd = ExclusiveThreadingHTTPServer((args.host, args.port), make_handler(app))

        def _trigger_shutdown(*_):
            threading.Thread(target=httpd.shutdown, daemon=True).start()

        app._shutdown_callback = _trigger_shutdown
        signal.signal(signal.SIGINT, _trigger_shutdown)
        signal.signal(signal.SIGTERM, _trigger_shutdown)

        print(
            f"[VOICE_DIRECTOR] v0.4.1 Listening on http://{args.host}:{args.port} | Live Console: http://{args.host}:{args.port}/ui "
            f"(pid={os.getpid()}, lang={args.lang}, silent={app.silent_mode}, voiceIn={app.voice_input_enabled}, bargeIn={app.asr.barge_in_enabled}, "
            f"brain={app.brain.cfg.get('provider')}:{app.brain.cfg.get('model')})",
            flush=True,
        )
        try:
            httpd.serve_forever()
        finally:
            app.shutdown()
            httpd.server_close()
            print("[VOICE_DIRECTOR] Clean shutdown complete.", flush=True)
    finally:
        _release_single_instance_lock(lock_fh)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
