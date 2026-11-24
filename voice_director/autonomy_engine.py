#!/usr/bin/env python3
"""
KoiSync.VoiceDirector — Phase 2 Heroine Autonomy Engine (纯女方主动 / 协同提议引擎)

Inspired by kPlug.CmpH.AI_Main (thinkDelay + chain anti-spam + InDecision window)
and upgraded with LLM full-context choreography & 4 independent non-mutually-exclusive permissions:
  1. allowAutoPose        (自主换体位 / 自主插入)
  2. allowAutoClimaxEdge  (自主控射与寸止)
  3. allowAutoClothes     (自主脱衣换装)
  4. allowAutoKPlugSwap   (自主召唤或轮换 kPlug 副角色)
"""
from __future__ import annotations

import random
import time
from typing import Dict, List, Optional, Tuple


VALID_MODES = ("player", "copilot", "female_active")
POSE_COOLDOWN_SEC = 45.0
CLOTHES_COOLDOWN_SEC = 38.0
SWAP_COOLDOWN_SEC = 60.0
CLIMAX_GAME_ACTIONS = {
    "seamless_climax", "climax", "finish_inside", "finish_outside",
    "finish_same", "finish_same_strong", "finish_same_weak",
    "orgasm_weak", "orgasm_strong", "finish_drink", "finish_vomit",
}


class HeroineAutonomyEngine:
    def __init__(self, cfg: dict) -> None:
        self.mode: str = str(cfg.get("autonomyMode") or "player").strip().lower()
        if self.mode not in VALID_MODES:
            self.mode = "player"
        self.allow_auto_pose: bool = bool(cfg.get("allowAutoPose", True))
        self.allow_auto_climax_edge: bool = bool(cfg.get("allowAutoClimaxEdge", True))
        self.allow_auto_clothes: bool = bool(cfg.get("allowAutoClothes", True))
        self.allow_auto_kplug_swap: bool = bool(cfg.get("allowAutoKPlugSwap", True))

        # Arc state & anti-spam cooldowns (initialize far in past so OS boot <60s never causes false cooldowns)
        self.arc_phase: str = "Warmup"
        self.next_decision_at: float = 0.0
        self.last_pose_switch_at: float = -1e9
        self.last_clothes_change_at: float = -1e9
        self.last_kplug_swap_at: float = -1e9
        self.last_edge_at: float = -1e9
        self.autonomous_beats: int = 0
        self.pending_proposal: str = ""
        self.pending_proposal_until: float = 0.0
        self._rng = random.Random()

    def update_config(self, body: dict, cfg: dict) -> bool:
        changed = False
        if "autonomyMode" in body or "mode" in body:
            raw = str(body.get("autonomyMode", body.get("mode", self.mode))).strip().lower()
            if raw in VALID_MODES and raw != self.mode:
                self.mode = raw
                cfg["autonomyMode"] = raw
                self.reset_timers(quick_first_beat=(raw != "player"))
                changed = True
        for key, attr in (
            ("allowAutoPose", "allow_auto_pose"),
            ("allowAutoClimaxEdge", "allow_auto_climax_edge"),
            ("allowAutoClothes", "allow_auto_clothes"),
            ("allowAutoKPlugSwap", "allow_auto_kplug_swap"),
        ):
            if key in body:
                val = bool(body[key])
                if getattr(self, attr) != val:
                    setattr(self, attr, val)
                    cfg[key] = val
                    changed = True
        return changed

    def to_csharp_payload(self) -> dict:
        return {
            "mode": self.mode,
            "allowAutoPose": self.allow_auto_pose,
            "allowAutoClimaxEdge": self.allow_auto_climax_edge,
            "allowAutoClothes": self.allow_auto_clothes,
            "allowAutoKPlugSwap": self.allow_auto_kplug_swap,
        }

    def status_dict(self, kplug_girl_think_paused: bool = False) -> dict:
        return {
            "mode": self.mode,
            "allowAutoPose": self.allow_auto_pose,
            "allowAutoClimaxEdge": self.allow_auto_climax_edge,
            "allowAutoClothes": self.allow_auto_clothes,
            "allowAutoKPlugSwap": self.allow_auto_kplug_swap,
            "arcPhase": self.arc_phase,
            "autonomousBeats": self.autonomous_beats,
            "pendingProposal": self.pending_proposal if time.monotonic() < self.pending_proposal_until else "",
            "kPlugGirlThinkPaused": bool(kplug_girl_think_paused),
        }

    def reset_timers(self, quick_first_beat: bool = False) -> None:
        now = time.monotonic()
        self.arc_phase = "Warmup"
        self.pending_proposal = ""
        self.pending_proposal_until = 0.0
        self.autonomous_beats = 0
        self.next_decision_at = now + (8.0 if quick_first_beat else self._roll_think_delay())

    def on_user_spoke(self) -> None:
        """Player voice ALWAYS takes immediate precedence; gently defer next autonomous beat."""
        now = time.monotonic()
        self.pending_proposal = ""
        self.pending_proposal_until = 0.0
        self.next_decision_at = max(self.next_decision_at, now + 14.0)

    def _roll_think_delay(self) -> float:
        if self.mode == "female_active":
            return self._rng.uniform(18.0, 30.0)
        return self._rng.uniform(26.0, 42.0)

    def _compute_arc(self, state: dict) -> str:
        stage = str(state.get("stage") or "")
        inserted = bool(state.get("isDirectPenetration", state.get("isInserted", False)))
        service = bool(state.get("isServiceActive", False))
        gm = float(state.get("gaugeMale", 0.0) or 0.0)
        gf = float(state.get("gaugeFemale", 0.0) or 0.0)
        hmode = str(state.get("hMode") or "")

        if stage.endswith("_A") or "IN_A" in stage or "OUT_A" in stage or "Orgasm_A" in stage or "Drink_A" in stage or "Vomit_A" in stage:
            return "Afterglow"
        if "IN_Start" in stage or "IN_Loop" in stage or "OUT_Start" in stage or "OUT_Loop" in stage or "Orgasm_Start" in stage or "Orgasm_Loop" in stage or stage in ("Drink", "Vomit"):
            return "ClimaxActive"
        if not inserted:
            if hmode.startswith("sonyu") and stage in ("Idle", "A_Idle", "D_Idle"):
                return "PosePrepare"
            if service or gm >= 22.0 or gf >= 22.0:
                return "ForeplayOrService"
            return "Warmup"
        # Directly inserted:
        if gm >= 84.0 or gf >= 88.0:
            if self.allow_auto_climax_edge and self.autonomous_beats >= 2 and (gm >= 90.0 or gf >= 92.0):
                return "ClimaxSprint"
            return "EdgeControl"
        return "InsertedExplore"

    def should_fire_beat(self, now: float, state: dict, since_user_sec: float) -> Tuple[bool, str]:
        if self.mode == "player":
            return False, ""
        if since_user_sec < 12.0:
            return False, ""
        if self.next_decision_at <= 0.0:
            self.next_decision_at = now + 12.0
            return False, ""
        if now < self.next_decision_at:
            return False, ""

        self.arc_phase = self._compute_arc(state)
        self.next_decision_at = now + self._roll_think_delay()
        self.autonomous_beats += 1
        return True, self.arc_phase

    def build_beat_instruction(
        self,
        state: dict,
        poses: List[dict],
        chars: Optional[dict],
        recent_events: List[dict],
        override_reason: str = "",
    ) -> str:
        now = time.monotonic()
        arc = self._compute_arc(state)
        self.arc_phase = arc
        pose_cd_ok = (now - self.last_pose_switch_at) >= POSE_COOLDOWN_SEC
        cloth_cd_ok = (now - self.last_clothes_change_at) >= CLOTHES_COOLDOWN_SEC
        swap_cd_ok = (now - self.last_kplug_swap_at) >= SWAP_COOLDOWN_SEC

        perms = [
            f"自主换体位/插入={'允许' if (self.allow_auto_pose and pose_cd_ok) else ('冷却中' if self.allow_auto_pose else '禁止')}",
            f"自主控射与寸止={'允许' if self.allow_auto_climax_edge else '禁止'}",
            f"自主脱衣换装={'允许' if (self.allow_auto_clothes and cloth_cd_ok) else ('冷却中' if self.allow_auto_clothes else '禁止')}",
            f"自主召唤/轮换kPlug角色={'允许' if (self.allow_auto_kplug_swap and swap_cd_ok) else ('冷却中' if self.allow_auto_kplug_swap else '禁止')}",
        ]

        # Highlight female-initiative face-to-face poses for female-active choreography
        fem_poses = [
            f"{p.get('variantLabel') or p.get('name')}({p.get('key')})"
            for p in (poses or [])
            if p.get("isDirectPenetration") and p.get("isFemaleInitiative")
        ][:5]
        fem_hint = ("；女方主导推荐体位: " + "、".join(fem_poses)) if fem_poses else ""

        guests = (chars or {}).get("haremGuests") or []
        guest_hint = f"；在场kPlug副角色={len(guests)}人" if guests else ""

        if self.mode == "copilot":
            return (
                f"[协同提议触发 · 当前阶段={arc} · 权限: {'，'.join(perms)}{fem_hint}{guest_hint}] "
                + (f"{override_reason}。" if override_reason else "")
                + "现在处于「协同提议模式」：请主动用温柔撒娇的语气向主人提出 1 个具体下一步建议（比如换骑乘位、脱掉上衣、加快节奏或寸止），"
                + "但**先只输出 expr，不要立刻执行换位或高潮**，等主人同意后再执行！"
            )

        # Pure Female-Initiative Mode (female_active)
        arc_guides = {
            "Warmup": "当前在暖场阶段：若允许脱衣可先主动褪去部分衣物并挑逗爱抚，或直接切入你喜欢的面对面/女主导侍奉或骑乘位准备主导全场。",
            "ForeplayOrService": "当前在升温前戏/侍奉阶段：若允许换体位且氛围已到位，可主动宣布由你主导，挑一个女上位骑乘或面对面插入体位（autoInsert=true）直接坐上来开始动！",
            "PosePrepare": "当前已在插入准备姿待机：若允许换体位/插入，主动贴耳预告并输出 gameAction:start_insert 由你主动坐下来开始动！",
            "InsertedExplore": "当前已直接插入：由你完全主导节奏！可主动切换更刺激的自由组合节奏（如 1浅1深 / 3浅1深 / 定深摆腰 / 深埋画圈 / 急顶慢拔，moverMode 优先由女方动），若允许换体位且冷却已好也可主动换女主导新体位。",
            "EdgeControl": "当前兴奋度已高（逼近临界）：若允许自主控射与寸止，你可以故意放慢做入口寸止或定深轻磨挑逗主人，不让主人太早结束！",
            "ClimaxSprint": "当前双槽饱满已到爆发节点：若允许自主控射，可以娇声预告要和主人一起去并直接触发 gameAction:seamless_climax！",
            "ClimaxActive": "当前正在高潮/射精演出中：请沉浸回应高潮的快感，不要打断演出或输出 timeline/switchPose。",
            "Afterglow": "刚刚高潮进入余韵：先贴耳温柔安抚回味；若允许 kPlug 轮换且在场有副角色，可以主动提议或切副角色接力，否则温柔歇息。",
        }
        return (
            f"[纯女方主动触发 · 当前弧线={arc} · 权限: {'，'.join(perms)}{fem_hint}{guest_hint}] "
            + (f"{override_reason}。" if override_reason else "")
            + arc_guides.get(arc, "请按你的性格主动推进当前互动。")
            + "严格遵守上述每项权限的允许/禁止状态，被禁止的指令字段绝对不要输出！若处于非直接插入态绝不输出 timeline。"
        )

    def filter_autonomous_cmd(self, cmd: Optional[dict]) -> Tuple[dict, List[str]]:
        """
        Hard permission guardrail applied to autonomous AI impulses!
        Each of the 4 permissions is independent and non-mutually-exclusive.
        Player-initiated voice commands bypass this filter and retain full authority.
        """
        if not isinstance(cmd, dict):
            return {"expr": "sweet_smile"}, []

        out = dict(cmd)
        blocked: List[str] = []

        # Autonomous impulses may NEVER self-modify autonomy mode or self-grant permissions!
        if "autonomy" in out:
            out.pop("autonomy", None)
            blocked.append("self_grant_autonomy")

        if self.mode == "copilot":
            # Copilot only proposes verbally; strip mutating actions until user agrees
            for k in ("switchPose", "autoInsert", "gameAction", "timeline", "timelineTune", "mathLab", "clothes", "outfit", "chara", "release"):
                if k in out:
                    out.pop(k, None)
            return out, blocked

        if self.mode != "female_active":
            return {"expr": out.get("expr", "sweet_smile")}, []

        now = time.monotonic()

        # Permission 1: allowAutoPose (+ cooldown)
        if "switchPose" in out:
            if not self.allow_auto_pose or (now - self.last_pose_switch_at) < POSE_COOLDOWN_SEC:
                out.pop("switchPose", None)
                out.pop("autoInsert", None)
                blocked.append("pose")
            else:
                self.last_pose_switch_at = now

        ga = str(out.get("gameAction") or "").strip().lower()
        if ga in ("start_insert", "insert", "insert_anal", "pull", "pull_out"):
            if not self.allow_auto_pose:
                out.pop("gameAction", None)
                blocked.append("pose_insert")

        # Permission 2: allowAutoClimaxEdge
        if ga in CLIMAX_GAME_ACTIONS:
            if not self.allow_auto_climax_edge:
                out.pop("gameAction", None)
                blocked.append("climax")
        for tl_key in ("timeline", "timelineTune"):
            if isinstance(out.get(tl_key), dict):
                pr = str(out[tl_key].get("preset") or "").lower()
                if pr.startswith("climax") or pr == "edge_hover":
                    if not self.allow_auto_climax_edge:
                        out.pop(tl_key, None)
                        blocked.append("climax_edge")

        # Permission 3: allowAutoClothes (+ cooldown)
        if "clothes" in out or "outfit" in out:
            if not self.allow_auto_clothes or (now - self.last_clothes_change_at) < CLOTHES_COOLDOWN_SEC:
                out.pop("clothes", None)
                out.pop("outfit", None)
                blocked.append("clothes")
            else:
                self.last_clothes_change_at = now

        # Permission 4: allowAutoKPlugSwap (+ cooldown)
        if "chara" in out:
            if not self.allow_auto_kplug_swap or (now - self.last_kplug_swap_at) < SWAP_COOLDOWN_SEC:
                out.pop("chara", None)
                blocked.append("kplug_swap")
            else:
                self.last_kplug_swap_at = now

        return out, blocked
