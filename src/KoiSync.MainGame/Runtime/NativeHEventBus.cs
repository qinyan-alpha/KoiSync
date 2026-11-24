using System;
using System.Collections.Generic;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.MainGame
{
    internal sealed class NativeHEvent
    {
        public long Id;
        public double RealtimeSec;
        public string Type = "";
        public string Mode = "";
        public string Stage = "";
        public string PoseKey = "";
        public string PoseName = "";
        public string Family = "";
        public string Facing = "";
        public bool IsDirectPenetration;
        public bool IsInserted;
        public string Detail = "";
    }

    /// <summary>
    /// Ring-buffered native Koikatsu H-Scene Event Bus driven directly by Harmony Postfix hooks
    /// on HSceneProc.ChangeAnimator, HActionBase.SetPlay, HFlag.Add* climax commits,
    /// ChaControl clothes/outfit changes, plus per-frame player-click & gauge-threshold monitors.
    /// </summary>
    internal sealed class NativeHEventBus
    {
        private const int MaxEvents = 160;
        private readonly object syncRoot = new object();
        private readonly List<NativeHEvent> events = new List<NativeHEvent>(MaxEvents);
        private long nextEventId = 1;

        internal string LastSetPlayState { get; private set; }
        internal double LastSetPlayAt { get; private set; }
        internal double LastDirectorClickAt { get; private set; }
        internal HFlag.ClickKind LastDirectorClickKind { get; private set; }
        internal long LatestEventId { get { lock (syncRoot) return nextEventId - 1; } }

        // Edge-detection trackers for frame-polled native fields
        private bool prevInHScene;
        private bool prevFemale70, prevFemale92, prevMale70, prevMale92;
        private bool prevKiss;
        private string prevTouch = "none";
        private HFlag.ClickKind prevClick = HFlag.ClickKind.none;

        internal NativeHEventBus()
        {
            LastSetPlayState = "";
        }

        internal void ResetSession()
        {
            LastSetPlayState = "";
            LastSetPlayAt = 0;
            prevFemale70 = prevFemale92 = prevMale70 = prevMale92 = false;
            prevKiss = false;
            prevTouch = "none";
            prevClick = HFlag.ClickKind.none;
        }

        internal void MarkDirectorClick(HFlag.ClickKind kind)
        {
            LastDirectorClickKind = kind;
            LastDirectorClickAt = Time.realtimeSinceStartup;
        }

        internal void RecordSetPlay(string stateName)
        {
            if (string.IsNullOrEmpty(stateName)) return;
            LastSetPlayState = stateName;
            LastSetPlayAt = Time.realtimeSinceStartup;
        }

        internal NativeHEvent Emit(Plugin host, string type, string detail, string overrideStage = null, PoseCatalogEntry poseInfo = null)
        {
            string mode = host != null && host.Flags != null ? host.Flags.mode.ToString() : "none";
            string stage = !string.IsNullOrEmpty(overrideStage)
                ? overrideStage
                : (host != null ? host.GetLiveFemaleAnimStateName() : "");
            string poseKey = poseInfo != null ? poseInfo.Key : (host != null && host.Profile != null ? (host.Profile.Key ?? "") : "");
            string poseName = poseInfo != null ? poseInfo.VariantLabel : (host != null && host.Profile != null ? (host.Profile.Name ?? "") : "");
            string family = poseInfo != null ? poseInfo.Family : "";
            string facing = poseInfo != null ? poseInfo.Facing : "";
            bool isDirPen = poseInfo != null ? poseInfo.IsDirectPenetration : (host != null && host.IsCurrentPoseDirectPenetration());
            bool isIns = host != null && host.IsDirectPenetrativeInserted();

            var ev = new NativeHEvent
            {
                RealtimeSec = Math.Round(Time.realtimeSinceStartup, 3),
                Type = type ?? "",
                Mode = mode,
                Stage = stage ?? "",
                PoseKey = poseKey,
                PoseName = poseName,
                Family = family,
                Facing = facing,
                IsDirectPenetration = isDirPen,
                IsInserted = isIns,
                Detail = detail ?? ""
            };

            lock (syncRoot)
            {
                ev.Id = nextEventId++;
                events.Add(ev);
                if (events.Count > MaxEvents)
                    events.RemoveRange(0, events.Count - MaxEvents);
            }
            return ev;
        }

        internal List<NativeHEvent> QuerySince(long sinceId, int limit = 32)
        {
            var res = new List<NativeHEvent>();
            int cap = Math.Max(1, Math.Min(128, limit));
            lock (syncRoot)
            {
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i].Id > sinceId)
                    {
                        res.Add(events[i]);
                        if (res.Count >= cap) break;
                    }
                }
            }
            return res;
        }

        internal List<NativeHEvent> Recent(int count = 5)
        {
            var res = new List<NativeHEvent>();
            lock (syncRoot)
            {
                int start = Math.Max(0, events.Count - Math.Max(1, count));
                for (int i = start; i < events.Count; i++) res.Add(events[i]);
            }
            return res;
        }

        internal void OnNativeSetPlay(Plugin host, string prevState, string nextState)
        {
            if (string.IsNullOrEmpty(nextState)) return;
            RecordSetPlay(nextState);

            string st = nextState;
            string prev = prevState ?? "";

            if (IsInsertStart(st))
            {
                Emit(host, "InsertStarted", "原生开始插入动作: " + st, st);
                return;
            }
            if (IsInsertIdle(st))
            {
                if (IsInsertStart(prev) || IsIdle(prev) || prev.Length == 0)
                    Emit(host, "InsertCompleted", "已完成插入并进入待动状态: " + st, st);
                else
                    Emit(host, "LoopPaused", "律动暂停回到插入待机: " + st, st);
                return;
            }
            if (IsLoopTier(st))
            {
                Emit(host, "LoopTierChanged", "进入律动档位: " + st, st);
                return;
            }
            if (IsClimaxStart(st))
            {
                // Yield any active Timeline, SR6 Math Lab, or gauge lock immediately so native climax animation & FinishGaugeDown run cleanly!
                if (host != null) host.OnNativeClimaxStartedCleanup(st);
                Emit(host, "ClimaxStarted", "原生高潮/射精开始: " + st, st);
                return;
            }
            if (IsAfterglow(st))
            {
                if (host != null) host.OnNativeAfterglowEnteredCleanup();
                Emit(host, "AfterglowEntered", "进入原生事后余韵（敏感度条已清零）: " + st, st);
                return;
            }
            if (IsIdle(st) && !IsIdle(prev) && prev.Length > 0)
            {
                Emit(host, "PulledOut", "已退回未插入待机状态: " + st, st);
            }
        }

        internal void ObserveFrame(Plugin host)
        {
            if (host == null) return;
            bool inH = host.Scene != null && !host.Scene.isEnd && host.Flags != null;
            if (inH != prevInHScene)
            {
                prevInHScene = inH;
                if (inH)
                {
                    ResetSession();
                    Emit(host, "SceneEntered", "进入本体 H 场景");
                }
                else
                {
                    Emit(host, "SceneExited", "离开本体 H 场景");
                    ResetSession();
                }
            }
            if (!inH) return;

            var flags = host.Flags;

            // 1) Detect player manual clicks (distinct from our own director-issued clicks!)
            var curClick = flags.click;
            if (curClick != HFlag.ClickKind.none && curClick != prevClick)
            {
                bool wasOurs = curClick == LastDirectorClickKind && (Time.realtimeSinceStartup - LastDirectorClickAt) < 0.20;
                if (!wasOurs)
                {
                    Emit(host, "PlayerManualClick", "玩家手动操作: " + curClick.ToString());
                    if (IsYieldClick(curClick))
                    {
                        if (host.MathEngine.Active)
                            host.DirectorStopMathLab("检测到玩家手动操作(" + curClick + ")，自动退让 SR6 数学实验台");
                        if (host.Timeline.Active || host.HasFrozenTimelineActors)
                            host.DirectorStopTimeline("检测到玩家手动操作(" + curClick + ")，自动退让时间轴接管");
                    }
                }
            }
            prevClick = curClick;

            // 2) Rising-edge gauge threshold events (70% native warning, 92% edge zone)
            float gf = flags.gaugeFemale;
            float gm = flags.gaugeMale;
            if (gf >= 70f && !prevFemale70) { prevFemale70 = true; Emit(host, "GaugeThreshold", "女方兴奋达到70%"); }
            else if (gf < 58f) prevFemale70 = false;
            if (gf >= 92f && !prevFemale92) { prevFemale92 = true; Emit(host, "GaugeThreshold", "女方兴奋逼近临界92%"); }
            else if (gf < 80f) prevFemale92 = false;

            if (gm >= 70f && !prevMale70) { prevMale70 = true; Emit(host, "GaugeThreshold", "男方兴奋达到70%"); }
            else if (gm < 58f) prevMale70 = false;
            if (gm >= 92f && !prevMale92) { prevMale92 = true; Emit(host, "GaugeThreshold", "男方兴奋逼近临界92%"); }
            else if (gm < 80f) prevMale92 = false;

            // 3) Kiss & Aibu touch-area changes
            try
            {
                var hand = RuntimeAccess.Read(host.Scene, "hand") as HandCtrl;
                if (hand != null)
                {
                    bool kiss = hand.IsKissAction();
                    string touch = hand.SelectKindTouch.ToString();
                    if (kiss != prevKiss)
                    {
                        prevKiss = kiss;
                        Emit(host, "TouchChanged", kiss ? "开始深吻" : "结束深吻");
                    }
                    if (!string.Equals(touch, prevTouch, StringComparison.Ordinal))
                    {
                        prevTouch = touch;
                        if (!string.Equals(touch, "none", StringComparison.OrdinalIgnoreCase))
                            Emit(host, "TouchChanged", "抚摸部位切换: " + touch);
                    }
                }
            }
            catch { }
        }

        private static bool IsYieldClick(HFlag.ClickKind c)
        {
            return c == HFlag.ClickKind.pull ||
                   c == HFlag.ClickKind.inside ||
                   c == HFlag.ClickKind.outside ||
                   c == HFlag.ClickKind.sameS ||
                   c == HFlag.ClickKind.sameW ||
                   c == HFlag.ClickKind.orgS ||
                   c == HFlag.ClickKind.orgW ||
                   c == HFlag.ClickKind.drink ||
                   c == HFlag.ClickKind.vomit ||
                   c == HFlag.ClickKind.modeChange ||
                   c == HFlag.ClickKind.actionChange ||
                   c == HFlag.ClickKind.end;
        }

        private static bool IsIdle(string s)
        {
            return string.Equals(s, "Idle", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(s, "A_Idle", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(s, "D_Idle", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInsertStart(string s)
        {
            return string.Equals(s, "Insert", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(s, "A_Insert", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(s, "D_Insert", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInsertIdle(string s)
        {
            return string.Equals(s, "InsertIdle", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(s, "A_InsertIdle", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(s, "D_InsertIdle", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLoopTier(string s)
        {
            return string.Equals(s, "WLoop", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(s, "A_WLoop", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(s, "D_WLoop", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(s, "SLoop", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(s, "A_SLoop", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(s, "D_SLoop", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(s, "OLoop", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(s, "A_OLoop", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(s, "D_OLoop", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsClimaxStart(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return s.IndexOf("IN_Start", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   s.IndexOf("OUT_Start", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   s.IndexOf("Orgasm_Start", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   s.IndexOf("Drink_Start", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   s.IndexOf("Vomit_Start", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsAfterglow(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return s.EndsWith("IN_A", StringComparison.OrdinalIgnoreCase) ||
                   s.EndsWith("OUT_A", StringComparison.OrdinalIgnoreCase) ||
                   s.EndsWith("Orgasm_A", StringComparison.OrdinalIgnoreCase) ||
                   s.EndsWith("Drink_A", StringComparison.OrdinalIgnoreCase) ||
                   s.EndsWith("Vomit_A", StringComparison.OrdinalIgnoreCase);
        }
    }
}
