#!/usr/bin/env python3
"""
KoiSync AI Director MCP Server (stdio JSON-RPC 2.0).

Connects Antigravity / any MCP host to the local loopback HTTP/JSON bridge
inside KoiSync.MainGame (default http://127.0.0.1:41289), enabling high-level
scene observation, Koikatsu H-mode control, 60Hz SR6 feel/rhythm choreography,
and real-time EdgeGuard safety control.
"""
from __future__ import annotations

import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from typing import Any

VERSIONS = ("2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05")
DEFAULT_URL = os.environ.get("KOISYNC_DIRECTOR_URL", "http://127.0.0.1:41289").rstrip("/")
DEFAULT_STUDIO_URL = os.environ.get("KOISYNC_STUDIO_URL", "http://127.0.0.1:41290").rstrip("/")
DEFAULT_VOICE_URL = os.environ.get("KOISYNC_VOICE_URL", "http://127.0.0.1:41295").rstrip("/")


class DirectorBridgeError(RuntimeError):
    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code
        self.message = message


class DirectorClient:
    """Reusable HTTP client for KoiSync.MainGame's loopback DirectorBridge (127.0.0.1)."""

    def __init__(self, base_url: str = DEFAULT_URL, timeout: float = 5.0) -> None:
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout

    def request(self, method: str, path: str, payload: dict[str, Any] | None = None,
                query: dict[str, str] | None = None) -> dict[str, Any]:
        url = self.base_url + path
        if query:
            qs = urllib.parse.urlencode({k: v for k, v in query.items() if v is not None and v != ""})
            if qs:
                url += "?" + qs
        data = None
        headers = {"Accept": "application/json"}
        if payload is not None:
            data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
            headers["Content-Type"] = "application/json; charset=utf-8"
        req = urllib.request.Request(url, data=data, headers=headers, method=method)
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                raw = resp.read().decode("utf-8", errors="replace")
                return json.loads(raw)
        except urllib.error.HTTPError as exc:
            raw = exc.read().decode("utf-8", errors="replace")
            try:
                parsed = json.loads(raw)
                msg = parsed.get("error", raw)
            except Exception:
                msg = raw or str(exc)
            raise DirectorBridgeError(f"HTTP_{exc.code}", msg) from exc
        except urllib.error.URLError as exc:
            raise DirectorBridgeError(
                "BRIDGE_UNREACHABLE",
                f"无法连接 KoiSync AI 导演本地桥接 ({self.base_url})：请确认 Koikatsu 正在运行且已在「同步偏好」勾选允许本地 AI 导演接入。底层原因: {exc.reason}",
            ) from exc


def _tool(name: str, description: str, properties: dict[str, Any], required: tuple[str, ...] = ()) -> dict[str, Any]:
    return {
        "name": name,
        "description": description,
        "inputSchema": {
            "type": "object",
            "properties": properties,
            "required": list(required),
            "additionalProperties": False,
        },
    }


STEP_SCHEMA: dict[str, Any] = {
    "type": "object",
    "properties": {
        "name": {"type": "string", "description": "Readable step label (e.g. '深插冲刺', '外沿磨蹭寸止', '悬停微颤')."},
        "durationSeconds": {"type": "number", "minimum": 0.2, "maximum": 180, "description": "Step duration in seconds."},
        "rampSeconds": {"type": "number", "minimum": 0.05, "maximum": 10, "description": "Smooth transition ramp in seconds (default 0.6)."},
        "mode": {"type": "string", "enum": ["envelope", "hold", "passthrough"], "description": "envelope=remap live strokes to [l0Min..l0Max]; hold=smoothly hover at holdL0; passthrough=normal game sync."},
        "l0Min": {"type": "number", "minimum": 0, "maximum": 92, "description": "Deepest device L0 bound (0=deepest, 80=golden outer edge; accepts 0..1 or 0..100)."},
        "l0Max": {"type": "number", "minimum": 5, "maximum": 95, "description": "Shallowest device L0 bound (default 80; accepts 0..1 or 0..100)."},
        "holdL0": {"type": "number", "minimum": 0, "maximum": 95, "description": "Target device L0 position when mode='hold' (e.g. 75 for outer tease, 12 for deep press)."},
        "intensityScale": {"type": "number", "minimum": 0.15, "maximum": 1.4, "description": "Amplitude & secondary-axis multiplier (default 1.0)."},
        "flutterHz": {"type": "number", "minimum": 0, "maximum": 8.0, "description": "Optional additive sine flutter frequency in Hz (0=off)."},
        "flutterAmp": {"type": "number", "minimum": 0, "maximum": 18, "description": "Optional additive sine flutter amplitude in L0 units (0..18, or 0..0.18)."},
        "gameStage": {"type": "string", "description": "Optional native Koikatsu H stage to switch on step entry (e.g. WLoop, SLoop, OLoop, Idle, A_WLoop, A_SLoop)."},
        "gameSpeed": {"type": "number", "minimum": 0, "maximum": 1, "description": "Optional native Koikatsu H speed (0.0=slowest .. 1.0=fastest) held during this step."},
        "feelPreset": {"type": "string", "enum": ["Raw", "Balanced", "Intense"], "description": "Optional FeelTranslator preset to switch on step entry."},
        "nonShaftMode": {"type": "string", "enum": ["Foreplay", "FullSync", "Silent"], "description": "Optional non-shaft feel layering mode."},
    },
    "additionalProperties": False,
}

TIMELINE_STEP_SCHEMA: dict[str, Any] = {
    "type": "object",
    "properties": {
        "preset": {
            "type": "string",
            "enum": ["tip_rub", "deep_grind", "nine_shallow_one_deep", "tease_plunge", "edge_hover", "turbo_sprint", "climax_ejaculation", "organic", "custom"],
            "description": "Built-in pose variant preset: tip_rub=shallow 3.2Hz glans friction; deep_grind=buried deep bounce + 3D waist grind; nine_shallow_one_deep=N yellow-band shallow strokes + 1 full-stroke plunge; tease_plunge=fast deep slam + agonizing slow pullout; edge_hover=tip hover + slow twist; turbo_sprint=break native speed cap up to 3.4Hz; climax_ejaculation=in-place climax sprint -> deep ejaculation pulse + native particle/siru + decaying 3D body spasms -> afterglow; organic=Markov stochastic rhythm smoothly weaving contrasting modes; custom=full manual curve. Tip: chain 3-6 steps in steps[] or enable organicRandom for richer live sessions!",
        },
        "name": {"type": "string", "description": "Optional custom Chinese/English display label."},
        "durationSeconds": {"type": "number", "minimum": 0.5, "maximum": 600, "description": "Duration of this variant step in seconds (default 12s)."},
        "rampSeconds": {"type": "number", "minimum": 0.05, "maximum": 5, "description": "Smooth blend ramp into this step (default 0.45s)."},
        "loop": {"type": "boolean", "description": "If true, loops continuously until AI sends the next command or user stops (prevents timeout mid-conversation!)."},
        "depthMin": {"type": "number", "minimum": 0, "maximum": 95, "description": "Animation shallowest sub-arc depth (0..1 or 0..100, 0=pose shallowest tip)."},
        "depthMax": {"type": "number", "minimum": 3, "maximum": 100, "description": "Animation deepest sub-arc depth (0..1 or 0..100, 100=pose deepest bottom-out)."},
        "l0Min": {"type": "number", "minimum": 0, "maximum": 94, "description": "Paired SR6 deepest L0 (0=deepest, outerCeilingL0=outer cup edge)."},
        "l0Max": {"type": "number", "minimum": 5, "maximum": 99, "description": "Paired SR6 shallowest L0 (defaults to configured outerCeilingL0)."},
        "freqHz": {"type": "number", "minimum": 0.15, "maximum": 5.0, "description": "Stroke frequency in Hz (breaks native Koikatsu clip speed limit!)."},
        "freqEndHz": {"type": "number", "minimum": 0.15, "maximum": 5.0, "description": "Optional ending Hz to linearly accelerate/decelerate across the step."},
        "inwardRatio": {"type": "number", "minimum": 0.12, "maximum": 0.88, "description": "Fraction of stroke moving inward (0.18=fast slam + slow agonizing pullout; 0.80=slow slide in + fast snap out)."},
        "pauseBottomSeconds": {"type": "number", "minimum": 0, "maximum": 5, "description": "Dwell pause at deepest point each cycle."},
        "pauseTopSeconds": {"type": "number", "minimum": 0, "maximum": 5, "description": "Dwell pause at shallowest tip each cycle."},
        "shallowCount": {"type": "integer", "minimum": 1, "maximum": 20, "description": "Number of shallow strokes before each deep plunge (nine_shallow_one_deep, default 9; set 3 for 三浅一深)."},
        "shallowBaseDepth": {"type": "number", "minimum": -1, "maximum": 70, "description": "Optional starting depth for the N shallow strokes (-1=use depthMin; 0..70 allows shallow strokes around any base depth)."},
        "shallowDepthMax": {"type": "number", "minimum": 8, "maximum": 95, "description": "Max animation depth of shallow strokes (default 44 for yellow-band travel)."},
        "shallowL0Min": {"type": "number", "minimum": 10, "maximum": 95, "description": "SR6 L0 floor during shallow strokes (default outerCeilingL0 - 46 for yellow-band travel)."},
        "deepHoldSeconds": {"type": "number", "minimum": 0, "maximum": 5, "description": "Bottom grind dwell after the full-stroke deep plunge (default 0.95s)."},
        "twistAmp": {"type": "number", "minimum": 0, "maximum": 45, "description": "SR6 R0 corkscrew twist & 3D pelvis yaw amplitude (0..45 or 0..0.45)."},
        "grindAmp": {"type": "number", "minimum": 0, "maximum": 42, "description": "Full-body 3D waist-grind & SR6 R1/R2/L1/L2 amplitude (0..42 or 0..0.42)."},
        "grindHz": {"type": "number", "minimum": 0.2, "maximum": 4.0, "description": "3D waist-grind cycle frequency in Hz (default 1.25)."},
        "grindPattern": {
            "type": "string",
            "enum": ["auto", "circle", "figure8", "rock_fb", "corkscrew"],
            "description": "3D full-body pelvic grind trajectory: circle=360-deg sensual circular grind; figure8=belly-dance figure-8 hip sway; rock_fb=anterior-posterior pelvic rock; corkscrew=deep axial twist.",
        },
        "mover": {
            "type": "string",
            "enum": ["auto", "female", "male", "both"],
            "description": "Which actor leads the 3D full-body waist grind: auto=auto-detect active mover from 1-cycle pelvis excursion (Female on cowgirl, Male on missionary); female/male/both=explicit override.",
        },
        "bodyGrindScale": {"type": "number", "minimum": -1, "maximum": 1.6, "description": "3D procedural pelvis/spine/limb-IK waist-grind intensity (-1=auto from grindAmp, 0=disable 3D bone motion, 0.2..1.6=explicit scale)."},
        "pingPong": {"type": "boolean", "description": "Keep sub-arc strokes on one continuous monotonic animation branch (zero pose pop)."},
        "organicRandom": {"type": "boolean", "description": "Enable Markov stochastic phrase transitions that organically weave contrasting rhythm styles, depths, speeds, and grind patterns."},
        "organicIntensity": {"type": "number", "minimum": 0, "maximum": 100, "description": "Energy level for organicRandom (0..1 or 0..100, default 55)."},
        "organicSeed": {"type": "integer", "description": "Optional integer RNG seed for reproducible organicRandom sequences (0=auto)."},
        "spasmAmp": {"type": "number", "minimum": 0, "maximum": 1.5, "description": "Additive 3D pelvic/spine body-spasm shudder amplitude (0=off, 1.0=strong climax spasm)."},
    },
    "additionalProperties": False,
}

AXIS_CONFIG_SCHEMA: dict[str, Any] = {
    "type": "object",
    "properties": {
        "min": {"type": "number", "minimum": 0, "maximum": 100, "description": "Lower output limit (0..1 or 0..100)."},
        "max": {"type": "number", "minimum": 0, "maximum": 100, "description": "Upper output limit (0..1 or 0..100)."},
        "gain": {"type": "number", "minimum": 0, "maximum": 20, "description": "Axis gain multiplier (default 1.0)."},
        "enabled": {"type": "boolean", "description": "Whether this axis is sent to the device."},
    },
    "additionalProperties": False,
}

TOOLS: list[dict[str, Any]] = [
    _tool(
        "koisync_status",
        "Read live Koikatsu H-mode telemetry and KoiSync state: current pose, stage, female/male gauges, animation frequency, 6-axis L0..R2 values, FeelTranslator band, available stages, and active AI Director / EdgeGuard status.",
        {},
    ),
    _tool(
        "koisync_list_poses",
        "List available H poses in the current Koikatsu H scene (native + kPlug). Use query or mode filter (e.g. 'sonyu', 'houshi', 'aibu', 'lesbian', 'masturbation') to find pose keys before calling koisync_switch_pose.",
        {
            "query": {"type": "string", "description": "Substring filter matching pose key, Chinese name, or Japanese game name."},
            "mode": {"type": "string", "description": "Optional H mode filter (e.g. sonyu, houshi, aibu, masturbation, lesbian)."},
            "includeUnavailable": {"type": "boolean", "description": "If true, include poses not selectable in the current actor count/mode."},
        },
    ),
    _tool(
        "koisync_set_feel",
        "Switch KoiSync's global FeelTranslator preset (Raw / Balanced / Intense) and/or non-shaft foreplay layering mode (Foreplay / FullSync / Silent).",
        {
            "feelPreset": {"type": "string", "enum": ["Raw", "Balanced", "Intense"]},
            "nonShaftMode": {"type": "string", "enum": ["Foreplay", "FullSync", "Silent"]},
        },
    ),
    _tool(
        "koisync_set_stage",
        "Switch the live Koikatsu H animation stage natively via HActionBase.SetPlay (e.g. WLoop, SLoop, OLoop, Idle, A_WLoop, A_SLoop) with optional speed latch.",
        {
            "stage": {"type": "string", "minLength": 1, "description": "Target stage name (check availableStages from koisync_status)."},
            "speed": {"type": "number", "minimum": 0, "maximum": 1, "description": "Optional H speed 0.0..1.0 to latch simultaneously."},
            "holdSeconds": {"type": "number", "minimum": 1, "maximum": 180, "description": "How long to maintain the latched speed (default 20s)."},
        },
        ("stage",),
    ),
    _tool(
        "koisync_set_speed",
        "Smoothly set and hold Koikatsu H animation speed (0.0=slow .. 1.0=fast), or release the speed latch back to manual mouse/UI control.",
        {
            "speed": {"type": "number", "minimum": 0, "maximum": 1, "description": "Target speed 0.0..1.0."},
            "holdSeconds": {"type": "number", "minimum": 1, "maximum": 180, "description": "Latch duration in seconds (default 25s)."},
            "release": {"type": "boolean", "description": "Set true to release the speed latch immediately."},
        },
    ),
    _tool(
        "koisync_switch_pose",
        "Switch Koikatsu H scene to another compatible pose by key (obtained from koisync_list_poses). Supports both native poses and kPlug extra poses.",
        {
            "key": {"type": "string", "minLength": 1, "description": "Pose key such as sonyu_0, sonyu_4, houshi_7, aibu_1, kplug_khs_f_80."},
            "anal": {"type": "boolean", "description": "If true, enter anal variant when supported."},
            "autoInsert": {"type": "boolean", "description": "If true (default), automatically trigger insert/start for sonyu/houshi poses."},
        },
        ("key",),
    ),
    _tool(
        "koisync_game_action",
        "Trigger a native Koikatsu H scene interaction: insert, insert_anal, pull, finish_inside, finish_outside, finish_same, orgasm_weak, orgasm_strong, finish_drink, finish_vomit, lock_gauges, unlock_gauges, set_gauges, or aibu_attach.",
        {
            "action": {
                "type": "string",
                "enum": [
                    "insert", "insert_anal", "pull",
                    "finish_inside", "finish_outside", "finish_same", "finish_same_weak", "finish_same_strong",
                    "orgasm_weak", "orgasm_strong", "finish_drink", "finish_vomit",
                    "lock_gauges", "unlock_gauges", "set_gauges", "aibu_attach",
                ],
            },
            "female": {"type": "number", "minimum": 0, "maximum": 100, "description": "Target female gauge 0..100 when action='set_gauges'."},
            "male": {"type": "number", "minimum": 0, "maximum": 100, "description": "Target male gauge 0..100 when action='set_gauges'."},
        },
        ("action",),
    ),
    _tool(
        "koisync_director_override",
        "Apply a smooth 60Hz SR6 feel/envelope override or teasing hold in KoiSync's DirectorModulator (always rate-limited by SixAxisDrive safety caps and auto-expiring via ttlSeconds).",
        {
            **STEP_SCHEMA["properties"],
            "ttlSeconds": {"type": "number", "minimum": 0.5, "maximum": 300, "description": "Dead-man watchdog timeout in seconds (default 45s) before smoothly returning to game sync."},
        },
    ),
    _tool(
        "koisync_director_sequence",
        "Run a multi-step 60Hz timed choreography (e.g. edging, slow buildup, tease pause, climax sprint) inside Unity. Each step can coordinate both Koikatsu game stage/speed AND SR6 L0 bounds/hold/flutter smoothly without LLM latency.",
        {
            "name": {"type": "string", "description": "Choreography title displayed in KoiSync UI (e.g. '星光寸止两分钟')."},
            "steps": {"type": "array", "items": STEP_SCHEMA, "minItems": 1, "maxItems": 24},
        },
        ("steps",),
    ),
    _tool(
        "koisync_director_edge_guard",
        "Arm or disarm the 60Hz real-time EdgeGuard gauge brake. When armed, the instant female or male gauge reaches the threshold, Unity automatically brakes into a slow outer-tease window with zero LLM latency.",
        {
            "enabled": {"type": "boolean", "description": "Enable or disable EdgeGuard."},
            "femaleThreshold": {"type": "number", "minimum": 40, "maximum": 99, "description": "Female gauge threshold (0..100, default 85)."},
            "maleThreshold": {"type": "number", "minimum": 40, "maximum": 99, "description": "Male gauge threshold (0..100, default 85)."},
            "brakeDurationSeconds": {"type": "number", "minimum": 1.5, "maximum": 30, "description": "How long each edging brake holds before resuming (default 7s)."},
            "cooldownSeconds": {"type": "number", "minimum": 0.5, "maximum": 30, "description": "Cooldown after brake ends before re-triggering (default 3s)."},
            "brakeL0Min": {"type": "number", "minimum": 0, "maximum": 90, "description": "Outer tease L0 minimum during brake (default 52)."},
            "brakeL0Max": {"type": "number", "minimum": 5, "maximum": 95, "description": "Outer tease L0 maximum during brake (default 80)."},
            "brakeStage": {"type": "string", "description": "Game stage during brake (default 'WLoop')."},
            "brakeSpeed": {"type": "number", "minimum": 0, "maximum": 1, "description": "Game speed during brake (default 0.18)."},
        },
        ("enabled",),
    ),
    _tool(
        "koisync_director_timeline",
        "BREAK Koikatsu's rigid linear animation loop! Scans the active pose's exact 1-cycle phase-to-depth curve AND active mover (Female vs Male), scrubs ALL actors' Layer-0 Animator in 60Hz frame-lock, drives full-body 3D procedural pelvis/waist/spine/limb-IK waist-grind (circle, figure8, rock_fb, corkscrew), and outputs paired 6-axis SR6 waveforms. Supports loop=true for continuous playback and optional paired expressionPreset.",
        {
            **TIMELINE_STEP_SCHEMA["properties"],
            "expressionPreset": {
                "type": "string",
                "enum": ["happy_trance", "heart_trance", "sweet_smile", "shy_tease", "ecstasy_melt", "pain_reluctant", "pain_wince", "dominant_smug", "climax", "release"],
                "description": "Optional facial expression preset to activate simultaneously with this Timeline variant.",
            },
            "expressionTarget": {
                "type": "string",
                "enum": ["both", "female", "male", "female0", "female1", "male0", "male1"],
                "description": "Target actor(s) for expressionPreset (default 'both').",
            },
            "steps": {
                "type": "array",
                "items": TIMELINE_STEP_SCHEMA,
                "minItems": 1,
                "maxItems": 16,
                "description": "Optional multi-step choreography chaining multiple Timeline variants seamlessly.",
            },
        },
    ),
    _tool(
        "koisync_director_timeline_tune",
        "Live non-interrupting real-time parameter tuning while a Timeline variant is running! Smoothly adjusts frequency, depth bounds, shallow travel, L0 bounds, twist/grind amplitude, grindPattern, mover, bodyGrindScale, loop, or expressionPreset WITHOUT resetting animation phase.",
        {
            **{k: v for k, v in TIMELINE_STEP_SCHEMA["properties"].items() if k != "preset"},
            "expressionPreset": {
                "type": "string",
                "enum": ["happy_trance", "heart_trance", "sweet_smile", "shy_tease", "ecstasy_melt", "pain_reluctant", "pain_wince", "dominant_smug", "climax", "release"],
                "description": "Optional facial expression preset switch without interrupting motion.",
            },
            "expressionTarget": {
                "type": "string",
                "enum": ["both", "female", "male", "female0", "female1", "male0", "male1"],
                "description": "Target actor(s) for expressionPreset (default 'both').",
            },
        },
    ),
    _tool(
        "koisync_director_expression",
        (
            "Direct Male and/or Female facial expressions at 60Hz with rich scenario presets AND smooth thrust/breathing rhythm coupling! "
            "Presets: happy_trance (joyful smiling + warm blushing + dreamy half-lidded trance), sweet_smile (bright loving smile), "
            "shy_tease (bashful blushing), ecstasy_melt (deep dreamy trance + trembling pupils), pain_reluctant (rough/intense roleplay: "
            "female anguished/pained/tearful + male stern/dominant), pain_wince (tightly squeezed > < eyes + streaming tears), "
            "dominant_smug (playful confident smirk), climax (peak release), or custom Eyebrow/Eyes/Mouth pattern IDs + blush/tears/open ranges."
        ),
        {
            "target": {
                "type": "string",
                "enum": ["both", "female", "male", "female0", "female1", "male0", "male1"],
                "description": "Which actor(s) to control (default 'both'; presets like pain_reluctant automatically assign complementary Female & Male moods when target='both').",
            },
            "preset": {
                "type": "string",
                "enum": ["happy_trance", "heart_trance", "sweet_smile", "shy_tease", "ecstasy_melt", "pain_reluctant", "pain_wince", "dominant_smug", "climax", "custom", "release"],
                "description": "Scenario expression preset (or 'release' to hand expressions back to Koikatsu's native HMotionEyeNeck).",
            },
            "release": {"type": "boolean", "description": "If true, release facial expression override back to native game control."},
            "eyebrowPtn": {"type": "integer", "minimum": 0, "maximum": 16, "description": "Custom KK eyebrow pattern ID (0=def, 1=angry, 2=troubled/pleasure, 10=serious, 11=anguished, 13=soft droop, 14=smug)."},
            "eyesPtn": {"type": "integer", "minimum": 0, "maximum": 38, "description": "Custom KK cha_eyeset_00 ID (0=def, 2=joyful smile, 4=warm smile, 7=yearning trance, 8=shy, 9=glare, 12=pained, 16=crying, 20=smug, 25=heart pupils, 35=tightly squeezed > <)."},
            "mouthPtn": {"type": "integer", "minimum": 0, "maximum": 42, "description": "Custom KK mouth pattern ID (0=def, 1=smile, 3=blissful smile, 5=flustered gasp, 11=clenched strained, 12=pained grimace, 14=panting overwhelmed, 19=smirk)."},
            "eyebrowOpenMin": {"type": "number", "minimum": 0, "maximum": 100, "description": "Eyebrow openness at thrust peak (0..1 or 0..100)."},
            "eyebrowOpenMax": {"type": "number", "minimum": 0, "maximum": 100, "description": "Eyebrow openness at rest/shallow (0..1 or 0..100)."},
            "eyeOpenMin": {"type": "number", "minimum": 0, "maximum": 100, "description": "Eye openness at deep peak (lower = dreamier half-lidded or tighter wince)."},
            "eyeOpenMax": {"type": "number", "minimum": 0, "maximum": 100, "description": "Eye openness at shallow/rest."},
            "mouthOpenMin": {"type": "number", "minimum": 0, "maximum": 100, "description": "Mouth openness minimum (0..1 or 0..100)."},
            "mouthOpenMax": {"type": "number", "minimum": 0, "maximum": 100, "description": "Mouth openness maximum at thrust peak (0..1 or 0..100)."},
            "blush": {"type": "number", "minimum": 0, "maximum": 100, "description": "Cheek blush intensity (0..1 or 0..100)."},
            "tears": {"type": "integer", "minimum": 0, "maximum": 3, "description": "Tear level: 0=none, 1=glistening, 2=medium droplets, 3=streaming."},
            "hideHighlight": {"type": "boolean", "description": "If true, dims eye highlights (vacant trance or pained gaze)."},
            "eyeShake": {"type": "boolean", "description": "If true, enables Koikatsu trembling pupil effect."},
            "blink": {"type": "boolean", "description": "Enable/disable automatic blinking."},
            "rhythmSync": {"type": "boolean", "description": "If true (default), smoothly pulses eye/mouth opening and blush in sync with live thrust & waist-grind depth."},
            "lookEyes": {"type": "string", "enum": ["auto", "front", "camera", "partner", "away"], "description": "Optional gaze target override ('auto' preserves native HMotionEyeNeck gaze)."},
        },
    ),
    _tool(
        "koisync_get_config",
        "Read ALL live KoiSync MainGame settings (outerCeilingL0 cup anti-slip limit, playerLengthMm, deviceStrokeMm, gameLengthMm, lengthAdaptation, feelPreset, nonShaftMode, smoothingMs, outputRatePercentPerSecond, sendDelayMs, previewEnabled, xray, softRotation, and per-axis L0..R2 min/max/gain/enabled).",
        {},
    ),
    _tool(
        "koisync_set_config",
        "Modify ANY subset of KoiSync MainGame settings live and persist them to disk (BepInEx config & active action profile)! Use this to tune outerCeilingL0 (e.g. 85 or 90 instead of default 80), stroke lengths, smoothing, output rate cap, feel preset, preview/xray, soft rotation, or per-axis L0..R2 limits/gains.",
        {
            "outerCeilingL0": {"type": "number", "minimum": 55, "maximum": 99, "description": "Cup anti-slip outer L0 ceiling in % (55..99, default 80; also accepts 0.55..0.99)."},
            "playerLengthMm": {"type": "number", "minimum": 1, "maximum": 500, "description": "Player usable length in mm."},
            "deviceStrokeMm": {"type": "number", "minimum": 20, "maximum": 500, "description": "Device L0 full stroke in mm."},
            "gameLengthMm": {"type": "number", "minimum": 20, "maximum": 500, "description": "Active pose interaction length in mm (saved to current action profile)."},
            "lengthAdaptation": {"type": "boolean", "description": "Enable/disable independent length adaptation."},
            "feelPreset": {"type": "string", "enum": ["Raw", "Balanced", "Intense"]},
            "nonShaftMode": {"type": "string", "enum": ["Foreplay", "FullSync", "Silent"]},
            "smoothingMs": {"type": "number", "minimum": 0, "maximum": 1000, "description": "Real-time output smoothing in ms."},
            "outputRatePercentPerSecond": {"type": "number", "minimum": 0, "maximum": 2000, "description": "Output speed cap in %/s (e.g. 450..850)."},
            "sendDelayMs": {"type": "number", "minimum": 0, "maximum": 250, "description": "Transport send delay in ms."},
            "previewEnabled": {"type": "boolean"},
            "xray": {"type": "boolean"},
            "showEntryMarkers": {"type": "boolean"},
            "softRotation": {
                "type": "object",
                "properties": {
                    "R0": {"type": "boolean"},
                    "R1": {"type": "boolean"},
                    "R2": {"type": "boolean"},
                },
                "additionalProperties": False,
            },
            "axes": {
                "type": "object",
                "properties": {
                    "L0": AXIS_CONFIG_SCHEMA,
                    "L1": AXIS_CONFIG_SCHEMA,
                    "L2": AXIS_CONFIG_SCHEMA,
                    "R0": AXIS_CONFIG_SCHEMA,
                    "R1": AXIS_CONFIG_SCHEMA,
                    "R2": AXIS_CONFIG_SCHEMA,
                },
                "additionalProperties": False,
            },
            "save": {"type": "boolean", "description": "Persist updated settings to BepInEx config / action profile (default true)."},
        },
    ),
    _tool(
        "koisync_director_gauges",
        (
            "Take AI Director ownership of Koikatsu's in-game female/male excitement gauges so the game's fixed timer "
            "does NOT prematurely jump into OLoop or climax against the user's real pacing. Smoothly ramps gauges at 60Hz "
            "to match the player's actual chat/voice feedback (or set managed=false to hand gauges back to the game)."
        ),
        {
            "managed": {"type": "boolean", "description": "If true (default), lock Koikatsu's auto-tick and ramp gauges smoothly under AI control; if false, unlock gauges back to game."},
            "female": {"type": "number", "minimum": 0, "maximum": 100, "description": "Optional smooth target female gauge (0..100)."},
            "male": {"type": "number", "minimum": 0, "maximum": 100, "description": "Optional smooth target male gauge (0..100). Keep <= 62 in houshi until you intentionally want OLoop."},
            "rampSeconds": {"type": "number", "minimum": 0, "maximum": 60, "description": "Smooth transition ramp duration in seconds (default 3.0s; 0 for immediate)."},
            "preventAutoOLoop": {"type": "boolean", "description": "If true (default), automatically clamps unspecified gauges below Koikatsu's auto-OLoop trigger (62% in houshi / 94% in sonyu)."},
        },
    ),
    _tool(
        "koisync_director_release",
        "Release AI Director control back to normal game sync (smooth fade by default, or immediate abort).",
        {
            "immediate": {"type": "boolean", "description": "If true, immediately abort override, body grind, and EdgeGuard brake."},
            "fadeSeconds": {"type": "number", "minimum": 0.05, "maximum": 5.0, "description": "Smooth fade-out duration when immediate=false (default 0.6s)."},
            "keepGaugeControl": {"type": "boolean", "description": "If true, keep AI gauge lock/governor active while releasing motion overrides."},
            "keepExpression": {"type": "boolean", "description": "If true, keep active facial expression preset while releasing motion overrides."},
        },
    ),
    _tool(
        "koisync_screenshot",
        "Capture a compact PNG snapshot from Koikatsu's main camera so the AI can visually observe the current scene composition and framing.",
        {
            "width": {"type": "integer", "minimum": 160, "maximum": 1280, "description": "Output width (default 640)."},
            "height": {"type": "integer", "minimum": 90, "maximum": 720, "description": "Output height (default 360)."},
            "includeImage": {"type": "boolean", "description": "If true (default), return inline MCP image content in addition to saved file path."},
        },
    ),
    _tool(
        "koisync_director_audio",
        "Toggle native Koikatsu H-scene audio mute gates (HSeCtrl wet/thrust SFX and/or HVoiceCtrl voice) with safety TTL watchdog, typically used while KoiSync.VoiceDirector handles KU100 binaural audio.",
        {
            "muteNativeSe": {"type": "boolean", "description": "Mute Koikatsu's native HSeCtrl thrust/wet SFX."},
            "muteNativeVoice": {"type": "boolean", "description": "Mute Koikatsu's native HVoiceCtrl voice playback."},
            "ttlSeconds": {"type": "number", "minimum": 1, "maximum": 300, "description": "Safety watchdog TTL in seconds before auto-unmuting (default 15s)."},
        },
    ),
    _tool(
        "koisync_voice_status",
        "Read live status from the local KoiSync.VoiceDirector KU100 binaural audio sidecar (http://127.0.0.1:41295): active language (zh/ja), loaded TTS engine, Track 1 wet SFX tier, Track 2 KU100 breath tier, speaking state, and mix gains.",
        {},
    ),
    _tool(
        "koisync_voice_config",
        "Configure the local KoiSync.VoiceDirector sidecar live: switch language ('zh'=locked 17_Serena style, 'ja'=locked 04_JA Falafel style), adjust voice/master gains, toggle brief native voice ducking while speaking (default ON), or toggle optional KU100 background beds (default OFF so native game audio stays untouched).",
        {
            "enabled": {"type": "boolean", "description": "Enable/pause VoiceDirector output."},
            "language": {"type": "string", "enum": ["zh", "ja"], "description": "Active voice language ('zh'=Chinese Serena, 'ja'=Japanese Falafel 04_JA)."},
            "masterGain": {"type": "number", "minimum": 0, "maximum": 2.0, "description": "Master output gain (default 0.92)."},
            "voiceGain": {"type": "number", "minimum": 0, "maximum": 2.0, "description": "Track 3 TTS voice gain (default 1.05)."},
            "duckNativeVoiceOnSpeak": {"type": "boolean", "description": "Temporarily duck native Koikatsu HVoiceCtrl ONLY while AI is speaking (default true; preserves native kissing/chest/H audio!)."},
            "bgBedsEnabled": {"type": "boolean", "description": "Optional KU100 Track 1/Track 2 background beds (default false; keep off to let Koikatsu handle native SFX/moans)."},
            "sfxGain": {"type": "number", "minimum": 0, "maximum": 1.0, "description": "Optional Track 1 KU100 wet thrust SFX gain (only when bgBedsEnabled=true)."},
            "breathGain": {"type": "number", "minimum": 0, "maximum": 1.0, "description": "Optional Track 2 KU100 breath/moan bed gain (only when bgBedsEnabled=true)."},
            "muteNativeSe": {"type": "boolean", "description": "Force-mute Koikatsu's native HSeCtrl continuously (default false)."},
            "muteNativeVoice": {"type": "boolean", "description": "Force-mute Koikatsu's native HVoiceCtrl continuously (default false)."},
        },
    ),
    _tool(
        "koisync_speak",
        "Synthesize and stream a breathy ASMR line directly into headphones (~500ms TTFA) via KoiSync.VoiceDirector using the locked V6 style ('zh'=17_Serena or 'ja'=04_JA Falafel), briefly ducking native HVoiceCtrl while speaking and driving live in-game lip-sync!",
        {
            "text": {"type": "string", "minLength": 1, "description": "Spoken Chinese or Japanese line (use ellipses …… for natural breath pauses)."},
            "language": {"type": "string", "enum": ["zh", "ja"], "description": "Optional language override ('zh' or 'ja'; defaults to sidecar active language)."},
            "intensity": {"type": "string", "enum": ["auto", "soft", "mid", "high"], "description": "Optional emotional intensity hint (default 'auto' from live game telemetry)."},
            "interrupt": {"type": "boolean", "description": "If true (default), smoothly cut any currently playing line and speak immediately."},
        },
        ("text",),
    ),
    _tool(
        "koisync_chat",
        "Send a live user utterance to KoiSync.VoiceDirector's Smart Director Brain, generating an immediate context-aware reply, speaking it through the locked V6 voice, and automatically executing 6-axis/timeline/clothes/kPlug commands.",
        {
            "text": {"type": "string", "minLength": 1, "description": "What the user said (Chinese or Japanese)."},
            "language": {"type": "string", "enum": ["zh", "ja"], "description": "Optional response language ('zh' or 'ja')."},
            "executeActions": {"type": "boolean", "description": "Whether to execute suggested KoiSync actions automatically when in H-scene."},
        },
        ("text",),
    ),
    _tool(
        "koisync_clothes",
        "Change clothing state (undress/half/dress/single part) on a main female or kPlug harem guest in H-scene.",
        {
            "mode": {"type": "string", "enum": ["undress", "half", "dress", "part", "dress_part", "half_part", "toggle_part"], "description": "Clothing state change mode."},
            "part": {"type": "integer", "minimum": 0, "maximum": 7, "description": "Optional single part index (0=top,1=bot,2=bra,3=shorts,4=gloves,5=panst,6=socks,7=shoes)."},
            "name": {"type": "string", "description": "Optional character name."},
        },
        ("mode",),
    ),
    _tool(
        "koisync_outfit",
        "Switch a character's outfit coordinate slot (0=School01, 1=School02, 2=Gym, 3=Swim, 4=Club, 5=Plain, 6=Pajamas).",
        {
            "slot": {"type": "integer", "minimum": 0, "maximum": 6, "description": "Coordinate slot index (0..6)."},
            "name": {"type": "string", "description": "Optional character name."},
        },
        ("slot",),
    ),
    _tool(
        "koisync_list_characters",
        "List active main females, kPlug invited harem guests, and invitable story/card characters.",
        {
            "query": {"type": "string", "description": "Optional character name filter."},
        },
    ),
    _tool(
        "koisync_characters",
        "Control kPlug & native character operations: open/close invite panel, invite a character card/story girl, swap main guest, replace main female body card, focus camera on character, or control guest behaviors (masturbate/striptease/dance/dismiss).",
        {
            "action": {"type": "string", "enum": ["open_invite_panel", "close_invite_panel", "invite", "swap_main_guest", "replace_body", "focus", "behavior"], "description": "Character operation."},
            "name": {"type": "string", "description": "Target character name."},
            "behavior": {"type": "string", "enum": ["masturbate", "stop_masturbate", "striptease", "dance", "stop_dance", "dismiss"], "description": "Guest behavior when action='behavior'."},
            "style": {"type": "string", "description": "Optional kPlug Onanism style (Lay/Stand/Sit/Crouch/BreastMassage/OnChair/OnDesk)."},
        },
        ("action",),
    ),
    # ------------------------------------------------------------------
    # KoiSync.Studio AI Director Tools (CharaStudio loopback 127.0.0.1:41290)
    # ------------------------------------------------------------------
    _tool(
        "koisync_studio_status",
        "Get live KoiSync.Studio state from CharaStudio (127.0.0.1:41290): active scene card, Timeline clock/speed, current director loop segment, recording state, actors, and live 6-axis SR6 output.",
        {},
    ),
    _tool(
        "koisync_studio_segments",
        "List, reload, rebuild from attachment chapters, or save/update labeled Studio director segments (.director.json) for the loaded CharaStudio scene.",
        {
            "action": {"type": "string", "enum": ["list", "reload", "rebuild_chapters", "save"], "description": "Operation to perform (default 'list')."},
            "segments": {
                "type": "array",
                "description": "Segment array when action='save' (each item: id, label, role, notes, start, end, loopStart, loopEnd, loopable, frequencyHz, amplitudePercent, seamErrorCm, defaultSpeed).",
                "items": {"type": "object"},
            },
            "save": {"type": "boolean", "description": "Persist to <scene>.director.json on disk when action='save' (default true)."},
        },
    ),
    _tool(
        "koisync_studio_play_segment",
        "Play or seamlessly loop a Studio scene segment by id, 1-based index, role ('intro'/'foreplay'/'oral'/'hand'/'slow'/'medium'/'fast'/'climax'/'afterglow'), or label substring, with live speed control and optional camera freeze.",
        {
            "segment": {"type": "string", "minLength": 1, "description": "Target segment id (e.g. 'seg_02'), index ('2'), role ('slow'/'fast'), or label substring."},
            "mode": {"type": "string", "enum": ["loop", "once"], "description": "Playback mode: 'loop' (default, wraps seamlessly at loopEnd -> loopStart) or 'once'."},
            "speed": {"type": "number", "minimum": 0.25, "maximum": 2.5, "description": "Timeline playback speed multiplier (default segment defaultSpeed or 1.0)."},
            "freezeCamera": {"type": "boolean", "description": "Temporarily pause Timeline camera tracks during loop playback to eliminate camera-pan jumps (default false)."},
            "durationSeconds": {"type": "number", "minimum": 0, "maximum": 600, "description": "Optional loop TTL in seconds before transitioning to nextSegment or linear playback (0 = infinite)."},
            "nextSegment": {"type": "string", "description": "Optional next segment id/role to transition into when mode='once' or durationSeconds expires."},
            "pauseAtEnd": {"type": "boolean", "description": "When mode='once' finishes with no nextSegment, pause at end instead of continuing linearly."},
        },
        ("segment",),
    ),
    _tool(
        "koisync_studio_speed",
        "Adjust CharaStudio Timeline playback speed live (0.25x .. 2.5x) without interrupting current segment loop.",
        {
            "speed": {"type": "number", "minimum": 0.25, "maximum": 2.5, "description": "Target Timeline playbackSpeed multiplier."},
        },
        ("speed",),
    ),
    _tool(
        "koisync_studio_seek",
        "Safely seek CharaStudio Timeline to a specific timestamp (automatically restoring t=0 visibility of late climax/prop tracks so backward jumps leave zero residue).",
        {
            "time": {"type": "number", "minimum": 0, "description": "Target timestamp in seconds."},
            "play": {"type": "boolean", "description": "Whether to play immediately after seeking."},
            "releaseLoop": {"type": "boolean", "description": "Release any active segment loop clamp (default true)."},
        },
        ("time",),
    ),
    _tool(
        "koisync_studio_climax",
        "Jump directly to the scene's climax segment, play it through once, and optionally flow into the afterglow segment or pause.",
        {
            "segment": {"type": "string", "description": "Optional explicit climax segment id/label (auto-detects role='climax' when omitted)."},
            "speed": {"type": "number", "minimum": 0.25, "maximum": 2.5, "description": "Playback speed during climax (default 1.0)."},
            "nextSegment": {"type": "string", "description": "Optional follow-up segment after climax (defaults to 'afterglow' if present)."},
            "pauseAtEnd": {"type": "boolean", "description": "Pause when climax/afterglow finishes (default true)."},
        },
    ),
    _tool(
        "koisync_studio_release",
        "Release Studio segment loop clamping, unfreeze any frozen camera tracks, restore playback speed, and return normal linear Timeline control to the user.",
        {
            "resumePlay": {"type": "boolean", "description": "Continue playing linearly after release (default true)."},
            "speed": {"type": "number", "minimum": 0.25, "maximum": 2.5, "description": "Playback speed to restore (default 1.0)."},
        },
    ),
    _tool(
        "koisync_studio_record",
        "Automate 6-axis .funscript recording in CharaStudio ('start_full' rewinds to 0s, enables auto-analysis, records the full scene, and auto-saves at end).",
        {
            "action": {"type": "string", "enum": ["start_full", "start", "stop"], "description": "Recording command (default 'start_full')."},
            "speed": {"type": "number", "minimum": 0.25, "maximum": 2.5, "description": "Optional playback speed during recording (default 1.0)."},
            "enableAnalysis": {"type": "boolean", "description": "Enable auto camera-pair / chapter analysis before recording (default true)."},
        },
        ("action",),
    ),
    _tool(
        "koisync_studio_load_scene",
        "Load a Koikatsu Studio scene card (.png) programmatically inside running CharaStudio (127.0.0.1:41290).",
        {
            "path": {"type": "string", "minLength": 1, "description": "Absolute path to the Studio scene .png file."},
        },
        ("path",),
    ),
    _tool(
        "koisync_studio_step_bake",
        "Run or query CharaStudio's offline zero-AI fast StepBake prescanner: steps real FinalIK/BP bones frame-by-frame, runs chapter-level majority voting (anti vaginal/anal flip + review flags), computes per-chapter depth [P05..P95], detects 3-signal climax candidates, and writes .director.json + .stepbake.json.",
        {
            "action": {"type": "string", "enum": ["start", "status", "cancel"], "description": "StepBake action (default 'start')."},
            "stepSeconds": {"type": "number", "minimum": 0.02, "maximum": 0.25, "description": "Timeline step interval per frame in seconds (default 0.05 = 20Hz)."},
        },
    ),
    _tool(
        "koisync_studio_feel",
        "Switch KoiSync.Studio's MainGame-aligned 3-tier feel preset ('raw' = 1:1 geometry, 'balanced' = chapter-calibrated golden window, 'intense' = full dynamic expansion).",
        {
            "preset": {"type": "string", "enum": ["raw", "balanced", "intense"], "description": "Target feel preset."},
        },
        ("preset",),
    ),
    _tool(
        "koisync_studio_bake_scripts",
        "Deterministically bake 50Hz 6-axis .funscript files (.funscript, .surge, .sway, .twist, .roll, .pitch) from live FinalIK/BP/OCIItem shafts using locked chapters and selected feel preset.",
        {
            "action": {"type": "string", "enum": ["start", "cancel"], "description": "Action (default 'start')."},
            "feelPreset": {"type": "string", "enum": ["raw", "balanced", "intense"], "description": "Optional feel preset override before baking."},
            "stepSeconds": {"type": "number", "minimum": 0.015, "maximum": 0.1, "description": "Frame interval in seconds (default 0.02 = 50Hz)."},
            "prescanFirst": {"type": "boolean", "description": "Run StepBake prescan first before baking scripts (default false; auto-prescans if no chapters exist)."},
        },
    ),
    _tool(
        "koisync_studio_screenshot",
        "Capture a clean main-camera PNG frame from CharaStudio (without UI overlays), optionally seeking safely to a specific timestamp first for visual segment tagging.",
        {
            "time": {"type": "number", "minimum": 0, "description": "Optional timestamp in seconds to pause & seek to before capturing."},
            "width": {"type": "integer", "minimum": 160, "maximum": 1280, "description": "Output width (default 640)."},
            "height": {"type": "integer", "minimum": 90, "maximum": 720, "description": "Output height (default 360)."},
            "includeImage": {"type": "boolean", "description": "Return inline PNG image content block for vision inspection (default true)."},
            "savePath": {"type": "string", "description": "Optional explicit .png path on disk."},
            "settleFrames": {"type": "integer", "minimum": 0, "maximum": 10, "description": "Frames to wait after seeking so FK/IK/DynamicBone settle (default 2)."},
        },
    ),
]
TOOL_MAP = {row["name"]: row for row in TOOLS}


def invoke_tool(name: str, arguments: dict[str, Any], client: DirectorClient) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    args = dict(arguments or {})
    if name == "koisync_status":
        res = client.request("GET", "/state")
    elif name == "koisync_list_poses":
        query = {
            "q": str(args.get("query", "")),
            "mode": str(args.get("mode", "")),
            "all": "1" if args.get("includeUnavailable") else "0",
        }
        res = client.request("GET", "/poses", query=query)
    elif name == "koisync_set_feel":
        res = client.request("POST", "/feel", payload=args)
    elif name == "koisync_set_stage":
        res = client.request("POST", "/game/stage", payload=args)
    elif name == "koisync_set_speed":
        res = client.request("POST", "/game/speed", payload=args)
    elif name == "koisync_switch_pose":
        res = client.request("POST", "/game/pose", payload=args)
    elif name == "koisync_game_action":
        res = client.request("POST", "/game/action", payload=args)
    elif name == "koisync_director_override":
        res = client.request("POST", "/director/override", payload=args)
    elif name == "koisync_director_sequence":
        res = client.request("POST", "/director/sequence", payload=args)
    elif name == "koisync_director_edge_guard":
        res = client.request("POST", "/director/edge_guard", payload=args)
    elif name == "koisync_director_timeline":
        res = client.request("POST", "/director/timeline", payload=args)
    elif name == "koisync_director_timeline_tune":
        res = client.request("POST", "/director/timeline/tune", payload=args)
    elif name == "koisync_director_expression":
        res = client.request("POST", "/director/expression", payload=args)
    elif name == "koisync_get_config":
        res = client.request("GET", "/config")
    elif name == "koisync_set_config":
        res = client.request("POST", "/config", payload=args)
    elif name == "koisync_director_gauges":
        res = client.request("POST", "/director/gauges", payload=args)
    elif name == "koisync_director_release":
        res = client.request("POST", "/director/release", payload=args)
    elif name == "koisync_director_audio":
        res = client.request("POST", "/director/audio", payload=args)
    elif name == "koisync_voice_status":
        vclient = DirectorClient(os.environ.get("KOISYNC_VOICE_URL", DEFAULT_VOICE_URL), timeout=8.0)
        res = vclient.request("GET", "/status")
    elif name == "koisync_voice_config":
        vclient = DirectorClient(os.environ.get("KOISYNC_VOICE_URL", DEFAULT_VOICE_URL), timeout=30.0)
        res = vclient.request("POST", "/config", payload=args)
    elif name == "koisync_speak":
        vclient = DirectorClient(os.environ.get("KOISYNC_VOICE_URL", DEFAULT_VOICE_URL), timeout=30.0)
        res = vclient.request("POST", "/speak", payload=args)
    elif name == "koisync_chat":
        vclient = DirectorClient(os.environ.get("KOISYNC_VOICE_URL", DEFAULT_VOICE_URL), timeout=35.0)
        res = vclient.request("POST", "/chat", payload=args)
    elif name == "koisync_clothes":
        res = client.request("POST", "/game/clothes", payload=args)
    elif name == "koisync_outfit":
        res = client.request("POST", "/game/outfit", payload=args)
    elif name == "koisync_list_characters":
        res = client.request("GET", "/game/characters", query={"q": str(args.get("query", ""))})
    elif name == "koisync_characters":
        res = client.request("POST", "/game/characters", payload=args)
    elif name == "koisync_screenshot":
        include_img = bool(args.get("includeImage", True))
        payload = {
            "width": int(args.get("width", 640)),
            "height": int(args.get("height", 360)),
            "includeBase64": include_img,
        }
        res = client.request("POST", "/game/screenshot", payload=payload)
        b64 = res.pop("base64Png", None)
        content: list[dict[str, Any]] = [
            {"type": "text", "text": json.dumps(res, ensure_ascii=False, allow_nan=False)}
        ]
        if include_img and isinstance(b64, str) and b64:
            content.append({"type": "image", "data": b64, "mimeType": "image/png"})
        return res, content
    elif name.startswith("koisync_studio_"):
        sclient = DirectorClient(os.environ.get("KOISYNC_STUDIO_URL", DEFAULT_STUDIO_URL), timeout=12.0)
        if name == "koisync_studio_status":
            res = sclient.request("GET", "/state")
        elif name == "koisync_studio_segments":
            action = str(args.get("action", "list")).lower()
            if action == "list":
                res = sclient.request("GET", "/studio/segments")
            elif action == "reload":
                res = sclient.request("POST", "/studio/segments/reload", payload={})
            elif action == "rebuild_chapters":
                res = sclient.request("POST", "/studio/chapters/rebuild", payload={})
            elif action == "save":
                res = sclient.request("POST", "/studio/segments", payload=args)
            else:
                raise DirectorBridgeError("INVALID_ACTION", f"Unknown segments action: {action}")
        elif name == "koisync_studio_play_segment":
            res = sclient.request("POST", "/studio/play_segment", payload=args)
        elif name == "koisync_studio_speed":
            res = sclient.request("POST", "/studio/speed", payload=args)
        elif name == "koisync_studio_seek":
            res = sclient.request("POST", "/studio/seek", payload=args)
        elif name == "koisync_studio_climax":
            res = sclient.request("POST", "/studio/climax", payload=args)
        elif name == "koisync_studio_release":
            res = sclient.request("POST", "/studio/release", payload=args)
        elif name == "koisync_studio_record":
            res = sclient.request("POST", "/studio/record", payload=args)
        elif name == "koisync_studio_load_scene":
            res = sclient.request("POST", "/studio/load_scene", payload=args)
        elif name == "koisync_studio_step_bake":
            res = sclient.request("POST", "/studio/step_bake", payload=args)
        elif name == "koisync_studio_feel":
            res = sclient.request("POST", "/studio/feel", payload=args)
        elif name == "koisync_studio_bake_scripts":
            res = sclient.request("POST", "/studio/bake_scripts", payload=args)
        elif name == "koisync_studio_screenshot":
            include_img = bool(args.get("includeImage", True))
            payload: dict[str, Any] = {
                "width": int(args.get("width", 640)),
                "height": int(args.get("height", 360)),
                "includeBase64": include_img,
            }
            if "time" in args and args["time"] is not None:
                payload["time"] = float(args["time"])
            if "savePath" in args and args["savePath"]:
                payload["savePath"] = str(args["savePath"])
            if "settleFrames" in args and args["settleFrames"] is not None:
                payload["settleFrames"] = int(args["settleFrames"])
            res = sclient.request("POST", "/studio/screenshot", payload=payload)
            b64 = res.pop("base64Png", None)
            content = [{"type": "text", "text": json.dumps(res, ensure_ascii=False, allow_nan=False)}]
            if include_img and isinstance(b64, str) and b64:
                content.append({"type": "image", "data": b64, "mimeType": "image/png"})
            return res, content
        else:
            raise DirectorBridgeError("UNKNOWN_TOOL", f"Unknown studio tool: {name}")
    else:
        raise DirectorBridgeError("UNKNOWN_TOOL", f"Unknown tool: {name}")

    return res, [{"type": "text", "text": json.dumps(res, ensure_ascii=False, allow_nan=False)}]


class RpcError(Exception):
    def __init__(self, code: int, message: str) -> None:
        self.code = code
        self.message = message


def _dispatch(method: str, params: dict[str, Any], client: DirectorClient) -> dict[str, Any]:
    if method == "initialize":
        client_version = params.get("protocolVersion")
        if not isinstance(client_version, str):
            raise RpcError(-32602, "protocolVersion required")
        return {
            "protocolVersion": client_version if client_version in VERSIONS else VERSIONS[0],
            "capabilities": {"tools": {"listChanged": False}},
            "serverInfo": {"name": "koisync-director", "version": "0.2.2"},
        }
    if method == "ping":
        return {}
    if method == "tools/list":
        return {"tools": TOOLS}
    if method == "tools/call":
        name = params.get("name")
        arguments = params.get("arguments", {})
        if name not in TOOL_MAP or not isinstance(arguments, dict):
            raise RpcError(-32602, "Unknown tool or invalid arguments")
        try:
            structured, content = invoke_tool(name, arguments, client)
        except Exception as exc:
            err = {"ok": False, "code": getattr(exc, "code", "TOOL_FAILED"), "message": str(exc)}
            return {
                "content": [{"type": "text", "text": json.dumps(err, ensure_ascii=False)}],
                "isError": True,
            }
        return {"content": content, "structuredContent": structured, "isError": False}
    raise RpcError(-32601, f"Method not found: {method}")


def handle_line(raw_line: bytes, client: DirectorClient) -> dict[str, Any] | None:
    request_id: Any = None
    try:
        try:
            entry = json.loads(raw_line.decode("utf-8"))
        except (UnicodeError, ValueError) as exc:
            raise RpcError(-32700, "Parse error") from exc
        if not isinstance(entry, dict) or entry.get("jsonrpc") != "2.0" or not isinstance(entry.get("method"), str):
            raise RpcError(-32600, "Invalid request")
        if "id" not in entry:
            return None  # JSON-RPC notification (e.g. notifications/initialized)
        request_id = entry["id"]
        if type(request_id) not in (str, int):
            request_id = None
            raise RpcError(-32600, "Invalid request ID")
        params = entry.get("params", {})
        if not isinstance(params, dict):
            raise RpcError(-32602, "params must be an object")
        return {"jsonrpc": "2.0", "id": request_id, "result": _dispatch(entry["method"], params, client)}
    except RpcError as exc:
        return {"jsonrpc": "2.0", "id": request_id, "error": {"code": exc.code, "message": exc.message}}


def main() -> int:
    url = os.environ.get("KOISYNC_DIRECTOR_URL", DEFAULT_URL)
    client = DirectorClient(url)
    for line in sys.stdin.buffer:
        if not line.strip():
            continue
        response = handle_line(line, client)
        if response is not None:
            out = (json.dumps(response, ensure_ascii=False, allow_nan=False) + "\n").encode("utf-8")
            sys.stdout.buffer.write(out)
            sys.stdout.buffer.flush()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
