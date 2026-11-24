using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.MainGame
{
    internal sealed class DirectorAxisConfig
    {
        public double? Min;
        public double? Max;
        public double? Gain;
        public bool? Enabled;
    }

    internal sealed class DirectorConfigSnapshot
    {
        public double PlayerLengthMm;
        public double DeviceStrokeMm;
        public double GameLengthMm;
        public bool LengthAdaptation;
        public double OuterCeilingL0;
        public string FeelPreset;
        public string NonShaftMode;
        public double SmoothingMs;
        public double OutputRatePercentPerSecond;
        public double SendDelayMs;
        public bool PreviewEnabled;
        public bool XRay;
        public bool ShowEntryMarkers;
        public bool[] SoftRotation = new bool[3];
        public double[] AxisMin = new double[6];
        public double[] AxisMax = new double[6];
        public double[] AxisGain = new double[6];
        public bool[] AxisEnabled = new bool[6];
        public int DirectorPort;
    }

    internal sealed class DirectorConfigPatch
    {
        public double? PlayerLengthMm;
        public double? DeviceStrokeMm;
        public double? GameLengthMm;
        public bool? LengthAdaptation;
        public double? OuterCeilingL0;
        public FeelPreset? FeelPreset;
        public NonShaftFeelMode? NonShaftMode;
        public double? SmoothingMs;
        public double? OutputRatePercentPerSecond;
        public double? SendDelayMs;
        public bool? PreviewEnabled;
        public bool? XRay;
        public bool? ShowEntryMarkers;
        public bool?[] SoftRotation = new bool?[3];
        public DirectorAxisConfig[] Axes = new DirectorAxisConfig[6];
        public bool Save = true;
    }

    internal sealed class PoseSwitchOutcome
    {
        public PoseCatalogEntry Resolved;
        public bool IsExactMatch;
        public bool AutoInsert;
        public bool StartMotion;
        public readonly List<PoseCatalogEntry> Alternatives = new List<PoseCatalogEntry>();
    }

    public sealed partial class Plugin
    {
        private const BindingFlags DirectorFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        internal readonly DirectorModulator Director = new DirectorModulator();
        internal readonly PhaseDepthMap PhaseMap = new PhaseDepthMap();
        internal readonly TimelineEngine Timeline = new TimelineEngine();
        internal readonly SR6MathManifoldEngine MathEngine = new SR6MathManifoldEngine();
        internal readonly NativeHEventBus EventBus = new NativeHEventBus();
        private SR6MathFrame activeMathFrame;
        private double lastMathLabUpdate;
        internal SR6MathFrame ActiveMathFrame { get { return activeMathFrame; } }
        internal ProceduralBodyGrind BodyGrind;
        internal DirectorExpression Expressions;
        private DirectorBridge directorBridge;
        private DirectorApi directorApi;
        private Harmony directorHarmony;
        private static Plugin activeDirectorHost;
        private ConfigEntry<bool> directorEnabled;
        private ConfigEntry<int> directorPort;
        internal bool HasFrozenTimelineActors { get { return timelineActorsFrozen; } }

        // Maintained native game speed latch (Koikatsu decays speedCalc if only written once)
        private bool directorSpeedLatch;
        private double directorSpeedValue;
        private float directorSpeedUntil;

        // AI-owned arousal gauge governor (stops Koikatsu's fixed timer from prematurely triggering OLoop/orgasm)
        private bool directorGaugeManaged;
        private float directorTargetFemale, directorTargetMale;
        private float directorGaugeRampRate = 18f;
        private bool directorPreventAutoOLoop = true;
        private double lastGaugeUpdate;

        // Live Animator Layer-0 scrubbing state for Timeline variants
        private bool timelineActorsFrozen;
        private int timelineScrubHash;
        private double timelineLastPhase;
        private double lastTimelineUpdate;
        private float timelineKeepSpeed = 0.65f;
        private TimelineFrame activeTimelineFrame;
        private readonly Queue<Action> endOfFrameTasks = new Queue<Action>();

        // v0.2.2 Native H-scene audio mute gate (for KoiSync.VoiceDirector sidecar) + in-place climax tracker
        private bool muteNativeSe;
        private bool muteNativeVoice;
        private float audioMuteUntil;
        private int lastAppliedClimaxPhase;
        private int lastAppliedEjacPulse;

        // Hard AI external takeover kill-switch (blocks all mutating HTTP commands from VoiceDirector when disconnected in HUD!)
        internal bool AiTakeoverBlocked { get; private set; }
        // Tracks which gauges must be cleared cleanly to 0 on AfterglowEntered if native FinishGaugeDown was interrupted
        private bool pendingClearFemaleGaugeAfterClimax;
        private bool pendingClearMaleGaugeAfterClimax;

        internal bool MuteNativeSeActive { get { return muteNativeSe && (audioMuteUntil <= 0f || Time.realtimeSinceStartup < audioMuteUntil); } }
        internal bool MuteNativeVoiceActive { get { return muteNativeVoice && (audioMuteUntil <= 0f || Time.realtimeSinceStartup < audioMuteUntil); } }

        internal TimelineFrame ActiveTimelineFrame { get { return activeTimelineFrame; } }
        internal bool DirectorListening { get { return directorBridge != null && directorBridge.Listening; } }
        internal int DirectorActivePort { get { return directorBridge != null ? directorBridge.Port : (directorPort != null ? directorPort.Value : 41289); } }
        internal double DirectorPhaseTime { get { return phaseTime; } }
        internal double DirectorPhaseDuration { get { return phaseDuration; } }
        internal bool DirectorSpeedLatched { get { return directorSpeedLatch; } }
        internal double DirectorSpeedValue { get { return directorSpeedValue; } }
        internal bool DirectorGaugeManaged { get { return directorGaugeManaged; } }
        internal float DirectorTargetFemaleGauge { get { return directorTargetFemale; } }
        internal float DirectorTargetMaleGauge { get { return directorTargetMale; } }
        internal bool AnyDirectorActive { get { return Director.Active || Timeline.Active || MathEngine.Active || directorSpeedLatch || directorGaugeManaged || (Expressions != null && Expressions.AnyActive); } }

        internal string DirectorUiSummary()
        {
            if (MathEngine.Active) return "SR6数学:" + MathEngine.LastStatus;
            if (Timeline.Active) return Timeline.SummaryStatus();
            return Director.SummaryStatus();
        }

        private void InitializeDirector()
        {
            directorEnabled = Config.Bind("Director", "Enabled", true, "Allow local loopback AI Director MCP bridge on 127.0.0.1");
            directorPort = Config.Bind("Director", "Port", 41289, "Loopback TCP port for AI Director bridge (127.0.0.1 only)");
            BodyGrind = new ProceduralBodyGrind(this);
            Expressions = new DirectorExpression(this);
            activeDirectorHost = this;
            InstallDirectorHooks();
            directorApi = new DirectorApi(this);
            directorBridge = new DirectorBridge(req => directorApi.Handle(req), s => Logger.LogInfo(s));
            if (directorEnabled.Value) directorBridge.Start(directorPort.Value);
        }

        private void InstallDirectorHooks()
        {
            try
            {
                directorHarmony = new Harmony("local.koisync.maingame.director");
                var lookDanLate = typeof(Lookat_dan).GetMethod("LateUpdate", DirectorFlags);
                var lookDanPrefix = typeof(Plugin).GetMethod("OnLookatDanLateUpdatePrefix", DirectorFlags);
                if (lookDanLate != null && lookDanPrefix != null)
                    directorHarmony.Patch(lookDanLate, prefix: new HarmonyMethod(lookDanPrefix));

                var fbsLate = typeof(FaceBlendShape).GetMethod("LateUpdate", DirectorFlags);
                var fbsPrefix = typeof(Plugin).GetMethod("OnFaceBlendShapeLateUpdatePrefix", DirectorFlags);
                if (fbsLate != null && fbsPrefix != null)
                    directorHarmony.Patch(fbsLate, prefix: new HarmonyMethod(fbsPrefix));

                var hseProc = typeof(HSeCtrl).GetMethod("Proc", DirectorFlags);
                var hsePrefix = typeof(Plugin).GetMethod("OnHSeCtrlProcPrefix", DirectorFlags);
                if (hseProc != null && hsePrefix != null)
                    directorHarmony.Patch(hseProc, prefix: new HarmonyMethod(hsePrefix));

                var hvoicePlay = typeof(HVoiceCtrl).GetMethod("PlayVoice", DirectorFlags);
                var hvoicePrefix = typeof(Plugin).GetMethod("OnHVoiceCtrlPlayVoicePrefix", DirectorFlags);
                if (hvoicePlay != null && hvoicePrefix != null)
                    directorHarmony.Patch(hvoicePlay, prefix: new HarmonyMethod(hvoicePrefix));

                // Native H-Event Bus Harmony Postfix hooks (exact event-driven sync with Koikatsu H-engine!)
                var chgAnim = typeof(HSceneProc).GetMethod("ChangeAnimator", DirectorFlags);
                var chgAnimPost = typeof(Plugin).GetMethod("OnHSceneChangeAnimatorPostfix", DirectorFlags);
                if (chgAnim != null && chgAnimPost != null)
                    directorHarmony.Patch(chgAnim, postfix: new HarmonyMethod(chgAnimPost));

                var setPlay = typeof(HActionBase).GetMethod("SetPlay", DirectorFlags, null, new[] { typeof(string), typeof(bool) }, null);
                var setPlayPost = typeof(Plugin).GetMethod("OnHActionSetPlayPostfix", DirectorFlags);
                if (setPlay != null && setPlayPost != null)
                    directorHarmony.Patch(setPlay, postfix: new HarmonyMethod(setPlayPost));

                var climaxPost = typeof(Plugin).GetMethod("OnHFlagClimaxCommitPostfix", DirectorFlags);
                if (climaxPost != null)
                {
                    var hm = new HarmonyMethod(climaxPost);
                    string[] commitMethods = new[]
                    {
                        "AddSonyuInside", "AddSonyuOutside", "AddSonyuSame", "AddSonyuOrg",
                        "AddSonyuAnalInside", "AddSonyuAnalOutside", "AddSonyuAnalSame", "AddSonyuAnalOrg",
                        "AddHoushiInside", "AddHoushiOutside", "AddHoushiDrink", "AddHoushiVomit", "AddAibuOrg"
                    };
                    for (int i = 0; i < commitMethods.Length; i++)
                    {
                        var m = typeof(HFlag).GetMethod(commitMethods[i], DirectorFlags, null, Type.EmptyTypes, null);
                        if (m != null) directorHarmony.Patch(m, postfix: hm);
                    }
                }

                var coordChange = typeof(ChaControl).GetMethod("ChangeCoordinateType", DirectorFlags, null, new[] { typeof(ChaFileDefine.CoordinateType), typeof(bool) }, null);
                var coordPost = typeof(Plugin).GetMethod("OnChaChangeCoordinatePostfix", DirectorFlags);
                if (coordChange != null && coordPost != null)
                    directorHarmony.Patch(coordChange, postfix: new HarmonyMethod(coordPost));
            }
            catch (Exception ex)
            {
                Logger.LogWarning("KoiSync Director Harmony hooks failed (falling back to LateUpdate): " + ex.GetBaseException().Message);
            }
        }

        private static void OnHSceneChangeAnimatorPostfix(HSceneProc.AnimationListInfo _nextAinmInfo)
        {
            var host = activeDirectorHost;
            if (host == null || _nextAinmInfo == null) return;
            try
            {
                var def = host.Catalogue != null
                    ? host.Catalogue.Resolve(_nextAinmInfo.mode.ToString(), _nextAinmInfo.id, _nextAinmInfo.nameAnimation, host.Flags != null && host.Flags.isAnalPlay, ResourceIdentity(_nextAinmInfo))
                    : null;
                PoseCatalogEntry entry = def != null
                    ? PoseCatalogClassifier.Classify(def.Key, def.Name, _nextAinmInfo.nameAnimation, _nextAinmInfo.mode.ToString(), _nextAinmInfo.id, def.Effector.ToString(), def.Contact.ToString(), def.AnalAllowed, true, _nextAinmInfo.isFemaleInitiative)
                    : null;
                host.EventBus.RecordSetPlay("Idle");
                host.EventBus.Emit(host, "PoseReady", "体位已切换就绪: " + (entry != null ? entry.VariantLabel : (_nextAinmInfo.nameAnimation ?? "")), "Idle", entry);
            }
            catch { }
        }

        private static void OnHActionSetPlayPostfix(string _nextAnimation, bool __result)
        {
            var host = activeDirectorHost;
            if (host == null || string.IsNullOrEmpty(_nextAnimation)) return;
            try
            {
                string prev = host.EventBus.LastSetPlayState;
                host.EventBus.OnNativeSetPlay(host, prev, _nextAnimation);
            }
            catch { }
        }

        private static void OnHFlagClimaxCommitPostfix(MethodBase __originalMethod)
        {
            var host = activeDirectorHost;
            if (host == null || __originalMethod == null) return;
            try
            {
                string mName = __originalMethod.Name ?? "";
                host.ClimaxCompletedCount++;
                // Immediately release any director gauge lock so Koikatsu's native FinishGaugeDown can clear gauges cleanly!
                host.ReleaseDirectorGaugeControl();
                if (host.Flags != null)
                {
                    host.Flags.lockGugeFemale = false;
                    host.Flags.lockGugeMale = false;
                }
                bool isFemaleOrg = mName.IndexOf("Same", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   mName.IndexOf("Org", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isMaleEjac = mName.IndexOf("Inside", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  mName.IndexOf("Outside", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  mName.IndexOf("Same", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  mName.IndexOf("Drink", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  mName.IndexOf("Vomit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  mName.IndexOf("Houshi", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isFemaleOrg) host.pendingClearFemaleGaugeAfterClimax = true;
                if (isMaleEjac) host.pendingClearMaleGaugeAfterClimax = true;
                host.EventBus.Emit(host, "ClimaxCommitted", "原生高潮/射精完成: " + mName);
            }
            catch { }
        }

        internal void OnNativeClimaxStartedCleanup(string stageName)
        {
            if (MathEngine.Active)
                DirectorStopMathLab("原生高潮事件触发，自动交还动画控制权");
            if (Timeline.Active || HasFrozenTimelineActors)
                DirectorStopTimeline("原生高潮事件触发，自动交还动画控制权");
            DirectorReleaseSpeedLatch();
            ReleaseDirectorGaugeControl();
            if (Flags != null)
            {
                Flags.lockGugeFemale = false;
                Flags.lockGugeMale = false;
            }
            string s = stageName ?? "";
            if (s.IndexOf("WS_IN_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("SS_IN_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                pendingClearFemaleGaugeAfterClimax = true;
                pendingClearMaleGaugeAfterClimax = true;
            }
            else if (s.IndexOf("Orgasm_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                pendingClearFemaleGaugeAfterClimax = true;
            }
            else if (s.IndexOf("IN_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.IndexOf("OUT_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.IndexOf("Drink_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.IndexOf("Vomit_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                pendingClearMaleGaugeAfterClimax = true;
                if (Flags != null && Flags.gaugeFemale >= 95f)
                    pendingClearFemaleGaugeAfterClimax = true;
            }
        }

        internal void OnNativeAfterglowEnteredCleanup()
        {
            ReleaseDirectorGaugeControl();
            DirectorReleaseSpeedLatch();
            if (Flags != null)
            {
                Flags.lockGugeFemale = false;
                Flags.lockGugeMale = false;
                // Guarantee arousal gauges genuinely clear to 0 after climax/ejaculation instead of staying pinned at 92-100%!
                if (pendingClearMaleGaugeAfterClimax || Flags.gaugeMale >= 88f)
                    Flags.gaugeMale = 0f;
                if (pendingClearFemaleGaugeAfterClimax || Flags.gaugeFemale >= 88f)
                    Flags.gaugeFemale = 0f;
            }
            pendingClearFemaleGaugeAfterClimax = false;
            pendingClearMaleGaugeAfterClimax = false;
        }

        private static void OnChaChangeCoordinatePostfix(ChaControl __instance, ChaFileDefine.CoordinateType type)
        {
            var host = activeDirectorHost;
            if (host == null || __instance == null || host.Scene == null || host.Scene.isEnd) return;
            try
            {
                if (__instance.sex == 1)
                    host.EventBus.Emit(host, "OutfitChanged", GetChaName(__instance) + " 切换服装: " + type.ToString());
            }
            catch { }
        }

        private static bool OnHSeCtrlProcPrefix()
        {
            var host = activeDirectorHost;
            return host == null || !host.MuteNativeSeActive;
        }

        private static bool OnHVoiceCtrlPlayVoicePrefix()
        {
            var host = activeDirectorHost;
            return host == null || !host.MuteNativeVoiceActive;
        }

        internal void DirectorConfigureAudio(bool? muteSe, bool? muteVoice, double ttlSeconds)
        {
            if (muteSe.HasValue) muteNativeSe = muteSe.Value;
            if (muteVoice.HasValue) muteNativeVoice = muteVoice.Value;
            if (muteNativeSe || muteNativeVoice)
            {
                double safeTtl = Numbers.Clamp(Numbers.Finite(ttlSeconds) ? ttlSeconds : 15.0, 1.0, 300.0);
                audioMuteUntil = Time.realtimeSinceStartup + (float)safeTtl;
                SilenceNativeAudioNow(muteNativeSe, muteNativeVoice);
            }
            else
            {
                audioMuteUntil = 0f;
            }
        }

        private void MaintainAudioMuteWatchdog()
        {
            if (audioMuteUntil > 0f && Time.realtimeSinceStartup >= audioMuteUntil)
            {
                muteNativeSe = false;
                muteNativeVoice = false;
                audioMuteUntil = 0f;
            }
        }

        private void SilenceNativeAudioNow(bool stopSe, bool stopVoice)
        {
            try
            {
                if (stopSe)
                {
#if KKS
                    Manager.Sound.Stop(Manager.Sound.Type.GameSE3D);
#else
                    if (Singleton<Manager.Sound>.IsInstance())
                        Singleton<Manager.Sound>.Instance.Stop(Manager.Sound.Type.GameSE3D);
#endif
                    if (Scene != null)
                    {
                        var seObj = RuntimeAccess.Read(Scene, "se");
                        var dic = RuntimeAccess.Read(seObj, "dicLoop") as IDictionary;
                        if (dic != null)
                        {
                            foreach (DictionaryEntry entry in dic)
                            {
                                var src = entry.Value as AudioSource;
                                if (src != null && src.isPlaying) src.Stop();
                            }
                        }
                    }
                }
                if (stopVoice)
                {
#if KKS
                    Manager.Voice.StopAll(true);
#else
                    var voiceMgr = RuntimeAccess.Find("Manager.Voice");
                    if (voiceMgr != null)
                    {
                        var instProp = voiceMgr.BaseType != null ? voiceMgr.BaseType.GetProperty("Instance", DirectorFlags) : null;
                        var inst = instProp != null ? instProp.GetValue(null, null) : null;
                        var stopAll = inst != null ? voiceMgr.GetMethod("StopAll", DirectorFlags, null, Type.EmptyTypes, null) : null;
                        if (stopAll != null) stopAll.Invoke(inst, null);
                    }
#endif
                    var f0 = Actor(ActorSlot.Female0);
                    if (f0 != null && f0.asVoice != null && f0.asVoice.isPlaying) f0.asVoice.Stop();
                    var f1 = Actor(ActorSlot.Female1);
                    if (f1 != null && f1.asVoice != null && f1.asVoice.isPlaying) f1.asVoice.Stop();
                }
            }
            catch { }
        }

        private static void OnLookatDanLateUpdatePrefix()
        {
            var host = activeDirectorHost;
            if (host != null && host.BodyGrind != null)
                host.BodyGrind.ApplyForCurrentFrame();
        }

        private static void OnFaceBlendShapeLateUpdatePrefix(FaceBlendShape __instance)
        {
            var host = activeDirectorHost;
            if (host != null && host.Expressions != null)
                host.Expressions.ApplyForFaceBlendShape(__instance);
        }

        private void SetDirectorEnabled(bool enabled)
        {
            if (directorEnabled != null) directorEnabled.Value = enabled;
            if (enabled)
            {
                if (directorBridge != null && !directorBridge.Listening)
                    directorBridge.Start(directorPort != null ? directorPort.Value : 41289);
                MaintainKPlugGirlThinkArbitration();
            }
            else
            {
                AbortDirector("用户关闭 AI 导演接口");
                RestoreKPlugGirlThink();
                if (directorBridge != null) directorBridge.Stop();
            }
            Config.Save();
        }

        internal void AbortDirector(string reason)
        {
            if (autoInsertCoroutine != null) { try { StopCoroutine(autoInsertCoroutine); } catch { } autoInsertCoroutine = null; }
            if (seamlessClimaxCoroutine != null) { try { StopCoroutine(seamlessClimaxCoroutine); } catch { } seamlessClimaxCoroutine = null; }
            Director.AbortImmediate(reason);
            Timeline.Stop(reason);
            MathEngine.Stop(reason);
            activeTimelineFrame = new TimelineFrame { Active = false };
            activeMathFrame = new SR6MathFrame { Active = false };
            lastTimelineUpdate = 0;
            lastMathLabUpdate = 0;
            RestoreTimelineActors(true);
            if (BodyGrind != null) BodyGrind.Reset();
            if (Expressions != null) Expressions.ReleaseAll();
            directorSpeedLatch = false;
            directorSpeedUntil = 0;
            ReleaseDirectorGaugeControl();
            if (Flags != null)
            {
                Flags.lockGugeFemale = false;
                Flags.lockGugeMale = false;
                Flags.click = HFlag.ClickKind.none;
            }
        }

        internal void DisconnectAiTakeover(string reason = "已彻底断开 AI 接管")
        {
            AiTakeoverBlocked = true;
            AutonomyMode = "player";
            AbortDirector(reason);
            if (voiceClient != null)
            {
                voiceClient.PostJsonFireAndForget("/stop", "{}");
                voiceClient.PostJsonFireAndForget("/config", "{\"executeActions\":false,\"autonomyMode\":\"player\",\"proactiveEnabled\":false}");
            }
            uiNotice = reason;
            EventBus.Emit(this, "AiDisconnected", reason);
        }

        internal void ResumeAiTakeover()
        {
            AiTakeoverBlocked = false;
            if (voiceClient != null)
            {
                voiceClient.PostJsonFireAndForget("/config", "{\"executeActions\":true}");
            }
        }

        internal void DirectorConfigureGauges(bool managed, double? female, double? male, double rampSeconds, bool preventAutoOLoop)
        {
            if (!managed)
            {
                ReleaseDirectorGaugeControl();
                return;
            }
            if (Flags == null) throw new InvalidOperationException("当前不在本体 H 场景中");
            directorGaugeManaged = true;
            directorPreventAutoOLoop = preventAutoOLoop;
            bool isHoushi = Flags.mode.ToString().StartsWith("houshi", StringComparison.Ordinal);
            float fCur = Flags.gaugeFemale;
            float mCur = Flags.gaugeMale;
            float fTgt = female.HasValue ? (float)Numbers.Clamp(female.Value, 0.0, 100.0) : fCur;
            float mTgt = male.HasValue ? (float)Numbers.Clamp(male.Value, 0.0, 100.0) : mCur;
            if (preventAutoOLoop)
            {
                // Keep below Koikatsu's automatic OLoop cliff (70% in houshi, 95%+ in sonyu) unless AI explicitly allows climax
                float mCeil = isHoushi ? 62f : 94f;
                if (!male.HasValue && mTgt > mCeil) mTgt = mCeil;
                if (!female.HasValue && fTgt > 94f) fTgt = 94f;
            }
            directorTargetFemale = fTgt;
            directorTargetMale = mTgt;
            Flags.lockGugeFemale = true;
            Flags.lockGugeMale = true;
            if (rampSeconds <= 0.05)
            {
                Flags.gaugeFemale = fTgt;
                Flags.gaugeMale = mTgt;
                directorGaugeRampRate = 40f;
            }
            else
            {
                float maxDelta = Math.Max(Math.Abs(fTgt - fCur), Math.Abs(mTgt - mCur));
                directorGaugeRampRate = Math.Max(4f, maxDelta / (float)Numbers.Clamp(rampSeconds, 0.1, 60.0));
            }
            lastGaugeUpdate = Time.realtimeSinceStartup;
        }

        internal void ReleaseDirectorGaugeControl()
        {
            if (!directorGaugeManaged) return;
            directorGaugeManaged = false;
            lastGaugeUpdate = 0;
            if (Flags != null)
            {
                Flags.lockGugeFemale = false;
                Flags.lockGugeMale = false;
            }
        }

        private void MaintainDirectorGauges()
        {
            if (!directorGaugeManaged || Flags == null) return;
            Flags.lockGugeFemale = true;
            Flags.lockGugeMale = true;
            double now = Time.realtimeSinceStartup;
            float dt = lastGaugeUpdate > 0 ? (float)Numbers.Clamp(now - lastGaugeUpdate, 0.0, 0.1) : (1f / 60f);
            lastGaugeUpdate = now;
            float step = directorGaugeRampRate * dt;
            Flags.gaugeFemale = Mathf.MoveTowards(Flags.gaugeFemale, directorTargetFemale, step);
            Flags.gaugeMale = Mathf.MoveTowards(Flags.gaugeMale, directorTargetMale, step);
        }

        private void DisposeDirector()
        {
            AbortDirector("插件卸载");
            RestoreKPlugGirlThink();
            if (activeDirectorHost == this) activeDirectorHost = null;
            if (directorHarmony != null)
            {
                try { directorHarmony.UnpatchSelf(); } catch { }
                directorHarmony = null;
            }
            while (endOfFrameTasks.Count > 0)
            {
                try { endOfFrameTasks.Dequeue()(); } catch { }
            }
            if (directorBridge != null) directorBridge.Dispose();
            directorBridge = null;
            directorApi = null;
        }

        internal void EnqueueEndOfFrame(Action work)
        {
            if (work != null) endOfFrameTasks.Enqueue(work);
        }

        private void ProcessEndOfFrameWork()
        {
            int count = endOfFrameTasks.Count;
            for (int i = 0; i < count; i++)
            {
                var work = endOfFrameTasks.Dequeue();
                try { work(); } catch { }
            }
        }

        private void PumpDirector()
        {
            if (directorBridge != null && directorBridge.Listening) directorBridge.Pump(6);
            EventBus.ObserveFrame(this);
            EnsureKsPlugCatalogueSynced();
            MaintainKPlugGirlThinkArbitration();
            MaintainAudioMuteWatchdog();
            AdvanceMathLabInUpdate();
            AdvanceTimelineInUpdate();
            if (Expressions != null) Expressions.PrepareInUpdate();
            MaintainDirectorGauges();
            MaintainEdgeBrakeGauges();
            if (!Timeline.Active && !MathEngine.Active) MaintainDirectorGameSpeed();
        }

        // While EdgeGuard is actively braking, keep gauges capped below rollback ceiling
        // (and <= 60% in houshi so HHoushi does not bounce straight back into OLoop at >= 70%).
        private void MaintainEdgeBrakeGauges()
        {
            if (!Director.EdgeBrakeActive || Flags == null) return;
            var eg = Director.SnapshotEdgeGuard();
            float fCap = (float)Math.Max(20.0, eg.FemaleThreshold - 18.0);
            bool isHoushi = Flags.mode.ToString().StartsWith("houshi", StringComparison.Ordinal);
            float mCap = (float)Math.Max(20.0, isHoushi ? Math.Min(eg.MaleThreshold - 18.0, 60.0) : (eg.MaleThreshold - 18.0));
            if (Flags.gaugeFemale > fCap) Flags.gaugeFemale = fCap;
            if (Flags.gaugeMale > mCap) Flags.gaugeMale = mCap;
        }

        // Runs in Update() BEFORE Unity's internal Animator pass and BEFORE LateUpdate()
        // (ChaControl shapes, KKABMX, MotionIK, DynamicBone hair/skirt, BetterPenetration).
        internal int ClimaxCompletedCount { get; private set; }
        private Coroutine autoInsertCoroutine;

        private static readonly string[] KnownAnimStates = new[]
        {
            "Idle", "A_Idle", "D_Idle",
            "Insert", "A_Insert", "D_Insert",
            "InsertIdle", "A_InsertIdle", "D_InsertIdle",
            "WLoop", "A_WLoop", "D_WLoop",
            "SLoop", "A_SLoop", "D_SLoop",
            "OLoop", "A_OLoop", "D_OLoop",
            "IN_Start", "IN_Loop", "IN_A", "A_IN_Start", "A_IN_Loop", "A_IN_A",
            "OUT_Start", "OUT_Loop", "OUT_A", "M_OUT_Start", "M_OUT_Loop", "M_OUT_A",
            "A_OUT_Start", "A_OUT_Loop", "A_OUT_A",
            "Orgasm_Start", "Orgasm_Loop", "Orgasm_A", "A_Orgasm_Start", "A_Orgasm_Loop", "A_Orgasm_A",
            "Drink_Start", "Drink_Loop", "Drink_A", "Vomit_Start", "Vomit_Loop", "Vomit_A"
        };

        /// <summary>
        /// Reads the REAL live Unity Animator state name directly from Female0 (never lags behind pose switches
        /// when Binding/Stage has not rebuilt yet!).
        /// </summary>
        internal string GetLiveFemaleAnimStateName()
        {
            try
            {
                var fem = Actor(ActorSlot.Female0);
                if (fem != null && fem.animBody != null && fem.animBody.runtimeAnimatorController != null)
                {
                    var stInfo = fem.animBody.GetCurrentAnimatorStateInfo(0);
                    // 1) Check exact state name captured from HActionBase.SetPlay hook (handles kPlug/mod states too!)
                    string hooked = EventBus.LastSetPlayState;
                    if (!string.IsNullOrEmpty(hooked) && stInfo.IsName(hooked))
                        return hooked;
                    // 2) Check native Flags.nowAnimStateName
                    string flagSt = Flags != null ? (Flags.nowAnimStateName ?? "") : "";
                    if (!string.IsNullOrEmpty(flagSt) && stInfo.IsName(flagSt))
                        return flagSt;
                    // 3) Fallback known native state names
                    for (int i = 0; i < KnownAnimStates.Length; i++)
                    {
                        if (stInfo.IsName(KnownAnimStates[i])) return KnownAnimStates[i];
                    }
                }
            }
            catch { }
            if (!string.IsNullOrEmpty(EventBus.LastSetPlayState)) return EventBus.LastSetPlayState;
            if (!string.IsNullOrEmpty(Stage)) return Stage;
            return Flags != null ? (Flags.nowAnimStateName ?? "") : "";
        }

        private static bool IsUninsertedStateName(string st)
        {
            if (string.IsNullOrEmpty(st)) return true;
            return string.Equals(st, "Idle", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(st, "A_Idle", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(st, "D_Idle", StringComparison.OrdinalIgnoreCase) ||
                   st.StartsWith("OUT_", StringComparison.OrdinalIgnoreCase) ||
                   st.StartsWith("M_OUT_", StringComparison.OrdinalIgnoreCase) ||
                   st.StartsWith("A_OUT_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInsertTransitionName(string st)
        {
            return string.Equals(st, "Insert", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(st, "A_Insert", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(st, "D_Insert", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInsertIdleStateName(string st)
        {
            return string.Equals(st, "InsertIdle", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(st, "A_InsertIdle", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(st, "D_InsertIdle", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsActiveLoopStateName(string st)
        {
            return string.Equals(st, "WLoop", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(st, "A_WLoop", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(st, "D_WLoop", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(st, "SLoop", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(st, "A_SLoop", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(st, "D_SLoop", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(st, "OLoop", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(st, "A_OLoop", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(st, "D_OLoop", StringComparison.OrdinalIgnoreCase);
        }

        internal bool IsCurrentPoseDirectPenetration()
        {
            if (Scene == null || Scene.isEnd || Flags == null) return false;
            string mode = Flags.mode.ToString();
            if (!mode.StartsWith("sonyu", StringComparison.Ordinal)) return false;
            if (Profile != null)
            {
                if (Profile.Effector != ActionSource.Shaft) return false;
                var contact = Binding != null ? Binding.Contact : Profile.Contact;
                if (contact != NativeTarget.VAGINAL && contact != NativeTarget.ANAL) return false;
            }
            return true;
        }

        /// <summary>
        /// STRICT GUARDRAIL: True ONLY during direct genital penetration (sonyu / sonyu3P / sonyu3PMMF,
        /// Shaft -> VAGINAL/ANAL) when physically inserted (NOT Idle, NOT Insert clip, NOT OUT_*, NOT aibu, NOT houshi!).
        /// Protects both characters' bones & weights by confining Timeline / N-shallow-M-deep / SR6 Math Engine
        /// strictly to direct penetrative poses!
        /// </summary>
        internal bool IsDirectPenetrativeInserted()
        {
            if (!IsCurrentPoseDirectPenetration()) return false;
            string st = GetLiveFemaleAnimStateName();
            if (IsUninsertedStateName(st) || IsInsertTransitionName(st)) return false;
            return true;
        }

        /// <summary>
        /// True when currently in an active non-penetrative service loop (houshi oral/hand/breast/foot).
        /// Kept strictly separate from IsDirectPenetrativeInserted()!
        /// </summary>
        internal bool IsServiceMotionActive()
        {
            if (Scene == null || Scene.isEnd || Flags == null) return false;
            string mode = Flags.mode.ToString();
            if (!mode.StartsWith("houshi", StringComparison.Ordinal)) return false;
            string st = GetLiveFemaleAnimStateName();
            return !string.IsNullOrEmpty(st) && !string.Equals(st, "Idle", StringComparison.OrdinalIgnoreCase);
        }

        internal bool IsCurrentlyInserted()
        {
            return IsDirectPenetrativeInserted();
        }

        private void AdvanceMathLabInUpdate()
        {
            if (!MathEngine.Active)
            {
                activeMathFrame = new SR6MathFrame { Active = false };
                lastMathLabUpdate = 0;
                if (timelineActorsFrozen && !Timeline.Active) RestoreTimelineActors();
                return;
            }
            if (Scene == null || Scene.isEnd || Flags == null || Binding == null || Profile == null || !IsDirectPenetrativeInserted())
            {
                DirectorStopMathLab("退出直接插入状态，自动关闭 SR6 数学实验台");
                return;
            }
            if (Director.EdgeGuardEnabled && !Director.EdgeBrakeActive)
            {
                var eg = Director.SnapshotEdgeGuard();
                if (Flags.gaugeFemale >= eg.FemaleThreshold || Flags.gaugeMale >= eg.MaleThreshold)
                {
                    DirectorStopMathLab("防射守护接管，自动退出 SR6 数学实验台");
                    return;
                }
            }

            var mp = MathEngine.CurrentParams;
            float dynSpeed = Mathf.Clamp((float)(mp.FreqHz / 2.8), 0.25f, 1.0f);
            Flags.timeNoClick = 0f;
            Flags.speedCalc = dynSpeed;
            Flags.SpeedUpClick(0f, 1f);
            Flags.speedItem = Math.Max(Flags.speedItem, dynSpeed);
            if (Flags.gaugeFemale > 94f) Flags.gaugeFemale = 94f;
            if (Flags.gaugeMale > 94f) Flags.gaugeMale = 94f;

            double now = Time.realtimeSinceStartup;
            double dt = lastMathLabUpdate > 0 ? Numbers.Clamp(now - lastMathLabUpdate, 0.0, 0.05) : 1.0 / 60.0;
            lastMathLabUpdate = now;
            activeMathFrame = MathEngine.Step(dt, PhaseMap, Measured != null ? Measured.Normalized : null);
            if (activeMathFrame.Active)
            {
                ApplyTimelineScrub(activeMathFrame.ScrubPhase);
                if (!Director.EdgeGuardEnabled || !Director.EdgeBrakeActive)
                {
                    float stimPower = Mathf.Clamp((float)(mp.FreqHz * (0.45 + 0.55 * (mp.DepthMax - mp.DepthMin)) + mp.OrbitAmp * 1.15 + mp.HelixTwist * 0.95), 0.2f, 4.2f);
                    if (Flags.gaugeFemale < 92f) Flags.gaugeFemale = Mathf.Min(92f, Flags.gaugeFemale + stimPower * 1.45f * (float)dt);
                    if (Flags.gaugeMale < 92f) Flags.gaugeMale = Mathf.Min(92f, Flags.gaugeMale + stimPower * 1.25f * (float)dt);
                    if (directorGaugeManaged)
                    {
                        directorTargetFemale = Math.Max(directorTargetFemale, Flags.gaugeFemale);
                        directorTargetMale = Math.Max(directorTargetMale, Flags.gaugeMale);
                    }
                }
            }
        }

        internal void DirectorStartMathLab(SR6MathParams parameters)
        {
            if (Scene == null || Flags == null || Binding == null || Profile == null)
                throw new InvalidOperationException("当前不在有效的本体 H 动作中，无法启动 SR6 数学实验台");
            if (!IsDirectPenetrativeInserted())
                throw new InvalidOperationException("SR6 六自由度数学极限实验台仅限直接性器官插入态（sonyu*）使用，防止破坏非插入动作骨骼与权重");

            if (Timeline.Active) DirectorStopTimeline("切换至 SR6 数学实验台");

            // Ensure active looping stage before scanning PhaseMap so scrubbing never freezes on static InsertIdle!
            if (IsNonMotionStageName(Stage) || (PhaseMap.Ready && PhaseMap.DepthSpanMeters < 0.02))
            {
                string[] candidates = Flags.isAnalPlay
                    ? new[] { "A_SLoop", "A_WLoop", "SLoop", "WLoop", "Loop" }
                    : new[] { "SLoop", "WLoop", "MLoop", "Loop", "A_SLoop", "A_WLoop" };
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (TryDirectorSetStage(candidates[i]))
                    {
                        Stage = candidates[i];
                        int h = Animator.StringToHash(candidates[i]);
                        stageHash = h;
                        var anims = CollectParticipatingAnimators();
                        for (int k = 0; k < anims.Count; k++)
                        {
                            if (anims[k] != null && anims[k].HasState(0, h))
                            {
                                anims[k].Play(h, 0, 0f);
                                anims[k].Update(0f);
                            }
                        }
                        if (clock != null) clockState = clock.GetCurrentAnimatorStateInfo(clockLayer);
                        PhaseMap.Reset();
                        break;
                    }
                }
            }

            EnsurePhaseMapReady();
            Director.Release(0.1);
            directorSpeedLatch = false;
            timelineKeepSpeed = Mathf.Clamp(Flags.speedCalc > 0.25f ? Flags.speedCalc : 0.65f, 0.35f, 1.0f);
            timelineScrubHash = clockState.shortNameHash != 0 ? clockState.shortNameHash : Animator.StringToHash(Stage ?? "SLoop");
            if (!directorGaugeManaged)
                DirectorConfigureGauges(true, null, null, 0.3, true);

            var p = (parameters ?? new SR6MathParams()).Sanitized();
            p.OuterCeilingL0 = ActiveOuterCeilingL0;
            MathEngine.Start(p);
            lastMathLabUpdate = 0;
            uiNotice = string.Format(L("SR6 数学实验台已启动: {0}", "SR6 Math Lab started: {0}"), UiText.T(SR6MathManifoldEngine.FamilyDisplayName(p.Family)));
            EventBus.Emit(this, "MathLabStarted", uiNotice);
        }

        internal void DirectorTuneMathLab(Action<SR6MathParams> mutator)
        {
            MathEngine.Tune(mutator);
        }

        internal void DirectorStopMathLab(string reason = "SR6 数学实验台已停止")
        {
            bool wasActive = MathEngine.Active;
            MathEngine.Stop(reason);
            activeMathFrame = new SR6MathFrame { Active = false };
            lastMathLabUpdate = 0;
            if (!Timeline.Active)
            {
                RestoreTimelineActors();
                ReleaseDirectorGaugeControl();
            }
            if (BodyGrind != null && !Timeline.Active) BodyGrind.Reset();
            if (wasActive)
            {
                uiNotice = reason;
                EventBus.Emit(this, "MathLabStopped", reason);
            }
        }

        private void AdvanceTimelineInUpdate()
        {
            if (!Timeline.Active)
            {
                activeTimelineFrame = new TimelineFrame { Active = false };
                lastTimelineUpdate = 0;
                if (timelineActorsFrozen && !MathEngine.Active) RestoreTimelineActors();
                return;
            }
            if (Scene == null || Scene.isEnd || Flags == null || Binding == null || Profile == null || !IsDirectPenetrativeInserted())
            {
                DirectorStopTimeline("退出直接插入状态，自动结束时间轴变式");
                return;
            }
            // Real-time EdgeGuard check: if gauge threshold trips, immediately yield Timeline to EdgeGuard brake
            if (Director.EdgeGuardEnabled && !Director.EdgeBrakeActive)
            {
                var eg = Director.SnapshotEdgeGuard();
                if (Flags.gaugeFemale >= eg.FemaleThreshold || Flags.gaugeMale >= eg.MaleThreshold)
                {
                    DirectorStopTimeline("防射守护接管，自动退出时间轴变式");
                    return;
                }
            }
            // Keep native H-scene speed & sound/jiggle cadence synced with the Timeline's real physical frequency!
            double tlFreq, tlDMin, tlDMax, tlGrind, tlTwist;
            Timeline.SnapshotActiveParams(out tlFreq, out tlDMin, out tlDMax, out tlGrind, out tlTwist);
            float dynSpeed = Mathf.Clamp((float)(tlFreq / 3.0), 0.25f, 1.0f);
            Flags.timeNoClick = 0f;
            Flags.speedCalc = dynSpeed;
            Flags.SpeedUpClick(0f, 1f);
            Flags.speedItem = Math.Max(Flags.speedItem, dynSpeed);
            // Prevent non-climax Timeline variants from accidentally hitting 100% and triggering a conflicting native OLoop!
            if (Timeline.ActivePreset != TimelinePreset.ClimaxEjaculation)
            {
                if (Flags.gaugeFemale > 94f) Flags.gaugeFemale = 94f;
                if (Flags.gaugeMale > 94f) Flags.gaugeMale = 94f;
            }

            double now = Time.realtimeSinceStartup;
            double dt = lastTimelineUpdate > 0 ? Numbers.Clamp(now - lastTimelineUpdate, 0.0, 0.05) : 1.0 / 60.0;
            lastTimelineUpdate = now;
            activeTimelineFrame = Timeline.Step(dt, PhaseMap, Measured != null ? Measured.Normalized : null);
            if (activeTimelineFrame.Active)
            {
                ApplyTimelineScrub(activeTimelineFrame.ScrubPhase);
                // Feed realistic mechanical stimulation energy smoothly into native gauges (bounded below OLoop cliff)
                if (Timeline.ActivePreset != TimelinePreset.ClimaxEjaculation && (!Director.EdgeGuardEnabled || !Director.EdgeBrakeActive))
                {
                    float stimPower = Mathf.Clamp((float)(tlFreq * (0.45 + 0.55 * (tlDMax - tlDMin)) + tlGrind * 1.1 + tlTwist * 0.8), 0.15f, 4.0f);
                    if (Flags.gaugeFemale < 92f) Flags.gaugeFemale = Mathf.Min(92f, Flags.gaugeFemale + stimPower * 1.35f * (float)dt);
                    if (Flags.gaugeMale < 92f) Flags.gaugeMale = Mathf.Min(92f, Flags.gaugeMale + stimPower * 1.15f * (float)dt);
                    if (directorGaugeManaged)
                    {
                        directorTargetFemale = Math.Max(directorTargetFemale, Flags.gaugeFemale);
                        directorTargetMale = Math.Max(directorTargetMale, Flags.gaugeMale);
                    }
                }
                HandleClimaxTransitions(activeTimelineFrame);
            }
            else
            {
                bool finishedClimax = lastAppliedClimaxPhase > 0;
                lastAppliedClimaxPhase = 0;
                RestoreTimelineActors(true);
                if (BodyGrind != null) BodyGrind.Reset();
                if (finishedClimax)
                {
                    ClimaxCompletedCount++;
                    ReleaseDirectorGaugeControl();
                    if (Flags != null)
                    {
                        Flags.lockGugeFemale = false;
                        Flags.lockGugeMale = false;
                        // Properly clear both arousal gauges to 0 after climax/ejaculation!
                        Flags.gaugeFemale = 0f;
                        Flags.gaugeMale = 0f;
                    }
                    if (Expressions != null) Expressions.SetTarget("female", "sweet_smile");
                    string resumeStage = (Flags != null && Flags.isAnalPlay) ? "A_WLoop" : "WLoop";
                    if (!TryDirectorSetStage(resumeStage)) TryDirectorSetStage("WLoop");
                    DirectorSetSpeedLatch(0.28, 6.0);
                    uiNotice = L("高潮余韵结束，敏感度条已清零并恢复轻柔慢动", "Afterglow ended; gauges cleared and resumed gentle motion");
                }
            }
        }

        private void HandleClimaxTransitions(TimelineFrame tf)
        {
            if (tf.ClimaxPhase <= 0)
            {
                lastAppliedClimaxPhase = 0;
                return;
            }
            if (tf.ClimaxPhase != lastAppliedClimaxPhase)
            {
                lastAppliedClimaxPhase = tf.ClimaxPhase;
                if (tf.ClimaxPhase == 1)
                {
                    // Sprint into peak climax: intense face + build gauges smoothly toward 100
                    if (Expressions != null) Expressions.SetTarget("both", "climax");
                    if (Flags != null) DirectorConfigureGauges(true, 99.0, 99.0, 1.8, false);
                }
                else if (tf.ClimaxPhase == 2)
                {
                    // Peak ejaculation burst in-place: trigger native particles + siru + climax expression without leaving pose!
                    if (Expressions != null) Expressions.SetTarget("both", "climax");
                    if (Flags != null)
                    {
                        Flags.gaugeFemale = 100f;
                        Flags.gaugeMale = 100f;
                    }
                    TriggerInPlaceEjaculationVfx(true);
                    lastAppliedEjacPulse = tf.EjaculationPulseCount;
                }
                else if (tf.ClimaxPhase == 3)
                {
                    // Warm afterglow breathing tremble: smoothly drain arousal gauges down to 0 during afterglow!
                    if (Flags != null)
                        DirectorConfigureGauges(true, 0.0, 0.0, 2.2, false);
                    if (Expressions != null)
                    {
                        Expressions.SetTarget("female", "ecstasy_melt", spec =>
                        {
                            spec.Tears = 2;
                            spec.Blush = 0.96f;
                            spec.MouthOpenMin = 0.36f;
                            spec.MouthOpenMax = 0.72f;
                        });
                    }
                }
            }
            else if (tf.ClimaxPhase == 2 && tf.EjaculationPulseCount > lastAppliedEjacPulse)
            {
                lastAppliedEjacPulse = tf.EjaculationPulseCount;
                // Secondary ejaculation throb particle burst
                TriggerInPlaceEjaculationVfx(false);
            }
        }

        private void TriggerInPlaceEjaculationVfx(bool primaryBurst)
        {
            try
            {
                if (Scene != null)
                {
                    var pCtrl = RuntimeAccess.Read(Scene, "particle");
                    if (pCtrl != null)
                    {
                        var playMethod = pCtrl.GetType().GetMethod("Play", DirectorFlags, null, new[] { typeof(int) }, null);
                        var dic = RuntimeAccess.Read(pCtrl, "dicParticle") as IDictionary;
                        if (playMethod != null && dic != null)
                        {
                            int[] targets = primaryBurst ? new[] { 1, 0, 2 } : new[] { 1 };
                            foreach (int pid in targets)
                            {
                                if (dic.Contains(pid))
                                {
                                    try { playMethod.Invoke(pCtrl, new object[] { pid }); } catch { }
                                }
                            }
                        }
                    }
                }
                if (primaryBurst)
                {
                    var f0 = Actor(ActorSlot.Female0);
                    if (f0 != null)
                    {
                        byte cur = f0.GetSiruFlags(ChaFileDefine.SiruParts.SiruFrontDown);
                        byte next = (byte)Math.Min(2, cur + 1);
                        f0.SetSiruFlags(ChaFileDefine.SiruParts.SiruFrontDown, next);
                        var upd = typeof(ChaControl).GetMethod("UpdateSiru", DirectorFlags, null, new[] { typeof(bool) }, null);
                        if (upd != null) upd.Invoke(f0, new object[] { true });
                    }
                }
            }
            catch { }
        }

        private static bool IsNonMotionStageName(string stage)
        {
            if (string.IsNullOrEmpty(stage)) return true;
            if (stage.IndexOf("Idle", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (stage.StartsWith("state_", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal string CurrentPhaseMapIdentity()
        {
            return (Profile != null ? Profile.Key : "") + "|" + (Stage ?? "") + "|" + controllerId + "|" + stageHash;
        }

        private void ObserveNormalPhaseDepth(double progress)
        {
            if (Timeline.Active || transition || Measured == null || Binding == null || phaseDuration <= 0.05) return;
            if (!Numbers.Finite(progress)) return;
            PhaseMap.Observe(CurrentPhaseMapIdentity(), progress, Measured.TargetDepth);
        }

        private Coroutine seamlessClimaxCoroutine;

        /// <summary>
        /// Unified Native Climax Flow: smoothly exits any custom Timeline, sprints naturally into Koikatsu's
        /// real OLoop, then hands straight off to native sameS/inside/outside/orgS so climax -> ejaculation ->
        /// afterglow happens ONCE seamlessly without any double-climax or unnatural freeze!
        /// </summary>
        internal void DirectorTriggerSeamlessClimax(string finishMode = "sameS")
        {
            if (Scene == null || Scene.isEnd || Flags == null)
                throw new InvalidOperationException("当前不在本体 H 场景中");
            if (!IsDirectPenetrativeInserted() && !IsServiceMotionActive())
                throw new InvalidOperationException("当前尚未插入且不在侍奉律动中，无法触发高潮射精");
            if (seamlessClimaxCoroutine != null) { StopCoroutine(seamlessClimaxCoroutine); seamlessClimaxCoroutine = null; }
            seamlessClimaxCoroutine = StartCoroutine(IE_SeamlessNativeClimax(finishMode));
        }

        private IEnumerator IE_SeamlessNativeClimax(string finishMode)
        {
            // 1) Smoothly release custom timeline / SR6 math lab scrubbing so Koikatsu's real Animator owns climax & afterglow!
            if (MathEngine.Active)
                DirectorStopMathLab("无缝移交游戏原生高潮射精");
            if (Timeline.Active || timelineActorsFrozen)
                DirectorStopTimeline("无缝移交游戏原生高潮射精");
            if (BodyGrind != null) BodyGrind.Reset();
            RestoreTimelineActors(true);
            ReleaseDirectorGaugeControl();
            if (Expressions != null) Expressions.ReleaseAll();
            Director.Release(0.35);

            string m = (finishMode ?? "sameS").Trim().ToLowerInvariant();
            bool isHoushi = Flags.mode.ToString().StartsWith("houshi", StringComparison.Ordinal);

            // 1.5) If currently parked stationary in InsertIdle, kick into active SLoop first so HSonyu can reach OLoop & accept climax clicks!
            string initSt = GetLiveFemaleAnimStateName();
            if (IsInsertIdleStateName(initSt) || IsNonMotionStageName(initSt))
            {
                string kickStage = Flags.isAnalPlay ? "A_SLoop" : "SLoop";
                if (!TryDirectorSetStage(kickStage)) TryDirectorSetStage("WLoop");
                EventBus.MarkDirectorClick(HFlag.ClickKind.speedup);
                Flags.click = HFlag.ClickKind.speedup;
                yield return null;
            }

            // 2) Natural native pre-climax sprint (up to 6.5s) ramping gauges to 100% into real OLoop
            uiNotice = L("高潮冲刺中，无缝切入原生高潮射精...", "Climax sprint active; transitioning seamlessly to native climax...");
            float sprintEnd = Time.realtimeSinceStartup + 6.5f;
            while (Time.realtimeSinceStartup < sprintEnd && Scene != null && !Scene.isEnd && Flags != null)
            {
                string st = GetLiveFemaleAnimStateName();
                if (st.IndexOf("IN_Start", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    st.IndexOf("IN_Loop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    st.IndexOf("OUT_Start", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    st.IndexOf("Orgasm_Start", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    st.IndexOf("Drink_Start", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    seamlessClimaxCoroutine = null;
                    yield break;
                }
                Flags.lockGugeFemale = false;
                Flags.lockGugeMale = false;
                Flags.gaugeFemale = Mathf.MoveTowards(Flags.gaugeFemale, 100f, 65f * Time.deltaTime);
                Flags.gaugeMale = Mathf.MoveTowards(Flags.gaugeMale, 100f, 65f * Time.deltaTime);
                Flags.speedCalc = 0.96f;
                Flags.timeNoClick = 0f;
                Flags.SpeedUpClick(0f, 1f);
                if (st.IndexOf("OLoop", StringComparison.OrdinalIgnoreCase) >= 0 && Flags.gaugeMale >= 99f && Flags.gaugeFemale >= 99f)
                    break;
                yield return null;
            }
            if (Scene == null || Scene.isEnd || Flags == null) { seamlessClimaxCoroutine = null; yield break; }

            Flags.gaugeFemale = 100f;
            Flags.gaugeMale = 100f;
            Flags.voiceWait = false;

            // 3) Fire native climax/ejaculation click for up to 3.0s until Koikatsu transitions into *IN_Start / *OUT_Start / *Orgasm_Start!
            HFlag.ClickKind clickKind = HFlag.ClickKind.sameS;
            if (m == "outside" || m == "finish_outside") clickKind = HFlag.ClickKind.outside;
            else if (m == "inside" || m == "finish_inside") clickKind = isHoushi ? HFlag.ClickKind.drink : HFlag.ClickKind.inside;
            else if (m == "org" || m == "orgs" || m == "orgasm_strong") clickKind = HFlag.ClickKind.orgS;
            else if (isHoushi) clickKind = HFlag.ClickKind.drink;

            float fireEnd = Time.realtimeSinceStartup + 3.0f;
            while (Time.realtimeSinceStartup < fireEnd && Scene != null && !Scene.isEnd && Flags != null)
            {
                string st = GetLiveFemaleAnimStateName();
                if (st.IndexOf("IN_Start", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    st.IndexOf("IN_Loop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    st.IndexOf("OUT_Start", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    st.IndexOf("Orgasm_Start", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    st.IndexOf("Drink_Start", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    break;
                }
                Flags.voiceWait = false;
                Flags.gaugeFemale = 100f;
                Flags.gaugeMale = 100f;
                EventBus.MarkDirectorClick(clickKind);
                Flags.click = clickKind;
                yield return null;
            }
            DirectorReleaseSpeedLatch();
            ReleaseDirectorGaugeControl();
            if (Flags != null)
            {
                Flags.lockGugeFemale = false;
                Flags.lockGugeMale = false;
            }
            if (clickKind == HFlag.ClickKind.sameS || clickKind == HFlag.ClickKind.sameW)
            {
                pendingClearFemaleGaugeAfterClimax = true;
                pendingClearMaleGaugeAfterClimax = true;
            }
            else if (clickKind == HFlag.ClickKind.orgS || clickKind == HFlag.ClickKind.orgW)
            {
                pendingClearFemaleGaugeAfterClimax = true;
            }
            else
            {
                pendingClearMaleGaugeAfterClimax = true;
            }
            uiNotice = L("已无缝进入游戏原生高潮射精与余韵", "Transitioned seamlessly to native climax & afterglow");
            seamlessClimaxCoroutine = null;
        }

        internal void DirectorStartTimeline(string name, TimelineStep[] steps, bool loopSequence = false)
        {
            if (Scene == null || Flags == null || Binding == null || Profile == null)
                throw new InvalidOperationException("当前不在有效的本体 H 动作中，无法启动时间轴变式");
            if (!IsCurrentlyInserted())
                throw new InvalidOperationException("当前尚未插入（处于爱抚或未插入待机状态），不能触发插入专属动作变式；请先切换并插入体位");

            // Route ClimaxEjaculation directly through Koikatsu's seamless native climax -> ejaculation -> afterglow flow
            // so there is NEVER a split artificial climax followed by a second native climax!
            if (MathEngine.Active) DirectorStopMathLab("切换至时间轴变式");
            if (steps != null && steps.Length == 1 && steps[0] != null && steps[0].Preset == TimelinePreset.ClimaxEjaculation)
            {
                DirectorTriggerSeamlessClimax("sameS");
                return;
            }

            // Guardrail #1: If pose is parked in InsertIdle/state_* after insertion, switch into an active looping stage
            // BEFORE scanning PhaseMap so scrubbing never freezes on a static idle frame!
            if (IsNonMotionStageName(Stage) || (PhaseMap.Ready && PhaseMap.DepthSpanMeters < 0.02))
            {
                string[] candidates = Flags.isAnalPlay
                    ? new[] { "A_SLoop", "A_WLoop", "SLoop", "WLoop", "Loop" }
                    : new[] { "SLoop", "WLoop", "MLoop", "Loop", "A_SLoop", "A_WLoop" };
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (TryDirectorSetStage(candidates[i]))
                    {
                        Stage = candidates[i];
                        int h = Animator.StringToHash(candidates[i]);
                        stageHash = h;
                        var anims = CollectParticipatingAnimators();
                        for (int k = 0; k < anims.Count; k++)
                        {
                            if (anims[k] != null && anims[k].HasState(0, h))
                            {
                                anims[k].Play(h, 0, 0f);
                                anims[k].Update(0f);
                            }
                        }
                        if (clock != null) clockState = clock.GetCurrentAnimatorStateInfo(clockLayer);
                        PhaseMap.Reset();
                        break;
                    }
                }
            }

            EnsurePhaseMapReady();
            // Clear any conflicting simple override while Timeline takes full frame-locked control
            Director.Release(0.1);
            directorSpeedLatch = false;
            lastAppliedClimaxPhase = 0;
            timelineKeepSpeed = Mathf.Clamp(Flags.speedCalc > 0.25f ? Flags.speedCalc : 0.65f, 0.35f, 1.0f);
            timelineScrubHash = clockState.shortNameHash != 0 ? clockState.shortNameHash : Animator.StringToHash(Stage ?? "SLoop");
            // Automatically prevent Koikatsu's blind timer from hijacking the active Timeline variant into OLoop
            if (!directorGaugeManaged)
                DirectorConfigureGauges(true, null, null, 0.3, true);
            Timeline.Start(name, steps, loopSequence);
        }

        internal bool DirectorTuneTimeline(Action<TimelineStep> mutator, bool? loopSequence)
        {
            return Timeline.TuneActive(mutator, loopSequence);
        }

        internal void DirectorStopTimeline(string reason = "时间轴变式已停止")
        {
            Timeline.Stop(reason);
            activeTimelineFrame = new TimelineFrame { Active = false };
            lastTimelineUpdate = 0;
            RestoreTimelineActors();
            if (!MathEngine.Active) ReleaseDirectorGaugeControl();
        }

        internal void EnsurePhaseMapReady()
        {
            string id = CurrentPhaseMapIdentity();
            if (PhaseMap.Ready && PhaseMap.MoverMeasured && string.Equals(PhaseMap.Identity, id, StringComparison.Ordinal) && PhaseMap.Coverage >= 0.70)
                return;
            if (Binding == null || Profile == null || clock == null) return;

            // Instant synchronous 32-point scan of the active looping clip (~6 ms)
            // so Timeline variants know exact shallowest/deepest phases AND real Female vs Male pelvis excursion immediately!
            var anims = CollectParticipatingAnimators();
            if (anims.Count == 0) return;

            int hash = clockState.shortNameHash != 0 ? clockState.shortNameHash : Animator.StringToHash(Stage ?? "SLoop");
            var savedPhases = new float[anims.Count];
            var savedSpeeds = new float[anims.Count];
            for (int i = 0; i < anims.Count; i++)
            {
                var st = anims[i].GetCurrentAnimatorStateInfo(0);
                savedPhases[i] = st.normalizedTime - Mathf.Floor(st.normalizedTime);
                savedSpeeds[i] = anims[i].speed;
            }

            Transform femHip = FindActorHips(Actor(ActorSlot.Female0));
            Transform maleHip = FindActorHips(Actor(ActorSlot.Male));
            Vector3 femMin = Vector3.zero, femMax = Vector3.zero;
            Vector3 maleMin = Vector3.zero, maleMax = Vector3.zero;
            bool femHas = false, maleHas = false;

            PhaseMap.Reset(id);
            var entryOffset = Profile.EntryOffset(Binding.Contact);
            bool endpoints = Binding.UsesEndpoints(Profile);
            try
            {
                const int Samples = 32;
                for (int k = 0; k < Samples; k++)
                {
                    float p = (float)k / Samples;
                    for (int i = 0; i < anims.Count; i++)
                    {
                        if (!anims[i].HasState(0, hash)) continue;
                        anims[i].speed = 0f;
                        anims[i].Play(hash, 0, p);
                        anims[i].Update(0f);
                    }
                    var s = Binding.Sample(0.016, entryOffset);
                    var m = ActionMotion.Solve(s, Binding.Calibration, Profile.Mode, endpoints, Binding.AdaptivePrincipalAxis);
                    PhaseMap.Observe(id, p, m.TargetDepth);

                    if (femHip != null)
                    {
                        Vector3 fp = femHip.position;
                        if (!femHas) { femMin = femMax = fp; femHas = true; }
                        else { femMin = Vector3.Min(femMin, fp); femMax = Vector3.Max(femMax, fp); }
                    }
                    if (maleHip != null)
                    {
                        Vector3 mp = maleHip.position;
                        if (!maleHas) { maleMin = maleMax = mp; maleHas = true; }
                        else { maleMin = Vector3.Min(maleMin, mp); maleMax = Vector3.Max(maleMax, mp); }
                    }
                }
                bool femInit = Flags != null && Flags.nowAnimationInfo != null && Flags.nowAnimationInfo.isFemaleInitiative;
                double femSpan = femHas ? (femMax - femMin).magnitude : 0.0;
                double maleSpan = maleHas ? (maleMax - maleMin).magnitude : 0.0;
                PhaseMap.SetMoverExcursion(femSpan, maleSpan, femInit);
                PhaseMap.Compile();
            }
            finally
            {
                for (int i = 0; i < anims.Count; i++)
                {
                    if (anims[i] == null) continue;
                    if (anims[i].HasState(0, hash))
                    {
                        anims[i].Play(hash, 0, savedPhases[i]);
                        anims[i].Update(0f);
                    }
                    anims[i].speed = savedSpeeds[i] > 0.01f ? savedSpeeds[i] : 1f;
                }
            }
        }

        private static Transform FindActorHips(ChaControl actor)
        {
            if (actor == null) return null;
            var bones = GameBinding.Bones(actor.transform);
            return GameBinding.Get(bones, "cf_j_hips", "cm_J_hips");
        }

        internal DirectorConfigSnapshot DirectorReadConfig()
        {
            var snap = new DirectorConfigSnapshot
            {
                PlayerLengthMm = Math.Round(PlayerLength * 1000.0, 1),
                DeviceStrokeMm = Math.Round(DeviceStroke * 1000.0, 1),
                GameLengthMm = Math.Round((Profile != null ? Profile.GameLength : ShaftStrokeMapping.ReferenceInteraction) * 1000.0, 1),
                LengthAdaptation = lengthAdaptation != null && lengthAdaptation.Value,
                OuterCeilingL0 = Math.Round(ActiveOuterCeilingL0 * 100.0, 1),
                FeelPreset = ActiveFeelPreset.ToString(),
                NonShaftMode = ActiveNonShaftFeelMode.ToString(),
                SmoothingMs = Math.Round(smoothing != null ? smoothing.Value : 25.0, 1),
                OutputRatePercentPerSecond = Math.Round(outputRate != null ? outputRate.Value : 450.0, 1),
                SendDelayMs = Math.Round(sendDelay != null ? sendDelay.Value : 0.0, 1),
                PreviewEnabled = previewEnabled != null && previewEnabled.Value,
                XRay = xray != null && xray.Value,
                ShowEntryMarkers = showEntryMarkers != null && showEntryMarkers.Value,
                DirectorPort = DirectorActivePort
            };
            for (int i = 0; i < 3; i++) snap.SoftRotation[i] = softRotation[i] != null && softRotation[i].Value;
            for (int i = 0; i < 6; i++)
            {
                snap.AxisMin[i] = Math.Round(Drive.Minimum[i] * 100.0, 1);
                snap.AxisMax[i] = Math.Round(Drive.Maximum[i] * 100.0, 1);
                snap.AxisGain[i] = Math.Round(Drive.Gain[i], 3);
                snap.AxisEnabled[i] = Drive.Enabled[i];
            }
            return snap;
        }

        internal void DirectorApplyConfig(DirectorConfigPatch patch)
        {
            if (patch == null) return;
            bool profileDirty = false;
            bool responseChanged = false;
            bool axesChanged = false;

            if (patch.PlayerLengthMm.HasValue && playerLength != null)
                playerLength.Value = (float)Numbers.Clamp(patch.PlayerLengthMm.Value, 1.0, 500.0);
            if (patch.DeviceStrokeMm.HasValue && deviceStroke != null)
                deviceStroke.Value = (float)Numbers.Clamp(patch.DeviceStrokeMm.Value, 20.0, 500.0);
            if (patch.GameLengthMm.HasValue && Profile != null)
            {
                Profile.GameLength = Numbers.Clamp(patch.GameLengthMm.Value, 20.0, 500.0) / 1000.0;
                profileDirty = true;
            }
            if (patch.LengthAdaptation.HasValue && lengthAdaptation != null)
                lengthAdaptation.Value = patch.LengthAdaptation.Value;
            if (patch.OuterCeilingL0.HasValue)
            {
                double v = patch.OuterCeilingL0.Value;
                if (v > 1.0) v /= 100.0;
                ActiveOuterCeilingL0 = v;
            }
            if (patch.FeelPreset.HasValue) ActiveFeelPreset = patch.FeelPreset.Value;
            if (patch.NonShaftMode.HasValue) ActiveNonShaftFeelMode = patch.NonShaftMode.Value;
            if (patch.SmoothingMs.HasValue && smoothing != null)
            {
                smoothing.Value = (float)Numbers.Clamp(patch.SmoothingMs.Value, 0.0, 1000.0);
                responseChanged = true;
            }
            if (patch.OutputRatePercentPerSecond.HasValue && outputRate != null)
            {
                outputRate.Value = (float)Numbers.Clamp(patch.OutputRatePercentPerSecond.Value, 0.0, 2000.0);
                responseChanged = true;
            }
            if (patch.SendDelayMs.HasValue && sendDelay != null)
            {
                sendDelay.Value = (float)Numbers.Clamp(patch.SendDelayMs.Value, 0.0, 250.0);
                responseChanged = true;
            }
            if (patch.PreviewEnabled.HasValue && previewEnabled != null) previewEnabled.Value = patch.PreviewEnabled.Value;
            if (patch.XRay.HasValue && xray != null) xray.Value = patch.XRay.Value;
            if (patch.ShowEntryMarkers.HasValue && showEntryMarkers != null) showEntryMarkers.Value = patch.ShowEntryMarkers.Value;

            for (int i = 0; i < 3; i++)
            {
                if (patch.SoftRotation[i].HasValue && softRotation[i] != null)
                    softRotation[i].Value = patch.SoftRotation[i].Value;
            }
            Drive.SoftTwistLimit = softRotation[0] == null || softRotation[0].Value;
            Drive.SoftRollLimit = softRotation[1] == null || softRotation[1].Value;
            Drive.SoftPitchLimit = softRotation[2] == null || softRotation[2].Value;

            for (int i = 0; i < 6; i++)
            {
                var ax = patch.Axes[i];
                if (ax == null) continue;
                if (ax.Min.HasValue)
                {
                    double mn = ax.Min.Value > 1.0 ? ax.Min.Value / 100.0 : ax.Min.Value;
                    Drive.Minimum[i] = Numbers.Clamp(mn, 0.0, 1.0);
                    if (Drive.Maximum[i] < Drive.Minimum[i]) Drive.Maximum[i] = Drive.Minimum[i];
                    axesChanged = true;
                }
                if (ax.Max.HasValue)
                {
                    double mx = ax.Max.Value > 1.0 ? ax.Max.Value / 100.0 : ax.Max.Value;
                    Drive.Maximum[i] = Numbers.Clamp(mx, Drive.Minimum[i], 1.0);
                    axesChanged = true;
                }
                if (ax.Gain.HasValue)
                {
                    Drive.Gain[i] = Numbers.Clamp(ax.Gain.Value, 0.0, 20.0);
                    gainText[i] = Drive.Gain[i].ToString("G5", CultureInfo.InvariantCulture);
                    axesChanged = true;
                }
                if (ax.Enabled.HasValue)
                {
                    Drive.Enabled[i] = ax.Enabled.Value;
                    axesChanged = true;
                }
            }

            if (responseChanged) ApplyResponseSettings();
            if (axesChanged && patch.Save) SaveAxes();
            else if (axesChanged)
            {
                for (int i = 0; i < 6; i++)
                {
                    min[i].Value = (float)Drive.Minimum[i];
                    max[i].Value = (float)Drive.Maximum[i];
                    gain[i].Value = (float)Drive.Gain[i];
                    enabledAxes[i].Value = Drive.Enabled[i];
                }
            }
            if (profileDirty && patch.Save) SaveProfile();
            if (patch.Save) Config.Save();
            RefreshCalibrationText();
        }

        private List<Animator> CollectParticipatingAnimators()
        {
            var list = new List<Animator>();
            foreach (ActorSlot slot in Enum.GetValues(typeof(ActorSlot)))
            {
                var actor = Actor(slot);
                if (actor != null && actor.gameObject.activeInHierarchy && actor.animBody != null && actor.animBody.runtimeAnimatorController != null)
                {
                    if (!list.Contains(actor.animBody)) list.Add(actor.animBody);
                }
            }
            if (Binding != null && Binding.Clock != null && Binding.Clock.runtimeAnimatorController != null && !list.Contains(Binding.Clock))
                list.Add(Binding.Clock);
            return list;
        }

        private void ApplyTimelineScrub(double phase)
        {
            float p = (float)(phase - Math.Floor(phase));
            timelineLastPhase = p;
            var anims = CollectParticipatingAnimators();
            int hash = timelineScrubHash != 0 ? timelineScrubHash : (clockState.shortNameHash != 0 ? clockState.shortNameHash : Animator.StringToHash(Stage ?? "SLoop"));
            for (int i = 0; i < anims.Count; i++)
            {
                var a = anims[i];
                if (a == null) continue;
                int layer = (Binding != null && a == Binding.Clock) ? Math.Max(0, Binding.ClockLayer) : 0;
                int useHash = hash;
                if (!a.HasState(layer, useHash)) useHash = a.GetCurrentAnimatorStateInfo(layer).shortNameHash;
                a.speed = 0f;
                a.Play(useHash, layer, p);
                a.Update(0f);
            }
            timelineActorsFrozen = true;
        }

        internal void RestoreTimelineActors(bool force = false)
        {
            if (!timelineActorsFrozen && !force) return;
            timelineActorsFrozen = false;
            foreach (var a in CollectParticipatingAnimators())
            {
                if (a != null && (force || a.speed < 0.05f)) a.speed = 1f;
            }
        }

        private void MaintainDirectorGameSpeed()
        {
            if (!directorSpeedLatch || Flags == null || Scene == null) return;
            if (Time.realtimeSinceStartup >= directorSpeedUntil)
            {
                directorSpeedLatch = false;
                return;
            }
            float spd = (float)Numbers.Clamp(directorSpeedValue, 0.0, 1.0);
            Flags.speedCalc = spd;
            Flags.timeNoClick = 0f;
            Flags.SpeedUpClick(0f, 1f);
            Flags.speedHoushi = spd >= 0.55f ? 1 : 0;
            Flags.speedLesbian = 1.0f + spd * 1.35f;
            Flags.speedItem = spd;
        }

        private void ApplyDirectorEvent(DirectorActionEvent ev)
        {
            if (ev == null) return;
            if (ev.FeelPreset.HasValue || ev.NonShaftMode.HasValue)
                DirectorApplyFeel(ev.FeelPreset, ev.NonShaftMode);
            // Roll back gauges BEFORE switching stage so HHoushi/HSonyu accepts WLoop instead of
            // snapping straight back into OLoop when male gauge >= 70% (houshi) or 100%.
            if (Flags != null)
            {
                if (ev.RollbackFemaleMax.HasValue && Flags.gaugeFemale > ev.RollbackFemaleMax.Value)
                    Flags.gaugeFemale = (float)ev.RollbackFemaleMax.Value;
                if (ev.RollbackMaleMax.HasValue)
                {
                    bool isHoushi = Flags.mode.ToString().StartsWith("houshi", StringComparison.Ordinal);
                    double mCap = isHoushi ? Math.Min(ev.RollbackMaleMax.Value, 60.0) : ev.RollbackMaleMax.Value;
                    if (Flags.gaugeMale > mCap) Flags.gaugeMale = (float)mCap;
                }
            }
            if (!string.IsNullOrEmpty(ev.GameStage))
            {
                // Auto-select A_Idle when holding in anal play so visual posture matches SR6 pause 1:1
                string targetStage = ev.GameStage;
                if (targetStage == "Idle" && Flags != null && Flags.isAnalPlay && TryDirectorSetStage("A_Idle"))
                    targetStage = "";
                if (targetStage.Length > 0) TryDirectorSetStage(targetStage);
            }
            if (ev.GameSpeed.HasValue)
                DirectorSetSpeedLatch(ev.GameSpeed.Value, ev.SpeedHoldSeconds > 0 ? ev.SpeedHoldSeconds : 15.0);
        }

        internal List<HSceneProc.AnimationListInfo> DirectorGetActions()
        {
            RefreshCatalogue();
            return actions;
        }

        internal bool DirectorCanSelect(HSceneProc.AnimationListInfo info)
        {
            return CanSelect(info);
        }

        internal void DirectorApplyFeel(FeelPreset? preset, NonShaftFeelMode? nonShaft)
        {
            if (preset.HasValue)
            {
                ActiveFeelPreset = preset.Value;
                if (preset.Value != FeelPreset.Raw && outputRate.Value < (float)(FeelTranslator.RecommendedRateCap(preset.Value) * 100.0))
                {
                    outputRate.Value = (float)(FeelTranslator.RecommendedRateCap(preset.Value) * 100.0);
                    ApplyResponseSettings();
                }
            }
            if (nonShaft.HasValue) ActiveNonShaftFeelMode = nonShaft.Value;
            Config.Save();
        }

        internal void DirectorSetSpeedLatch(double speed, double holdSeconds)
        {
            directorSpeedValue = Numbers.Clamp(speed, 0.0, 1.0);
            directorSpeedUntil = Time.realtimeSinceStartup + (float)Numbers.Clamp(holdSeconds, 1.0, 180.0);
            directorSpeedLatch = true;
            MaintainDirectorGameSpeed();
        }

        internal void DirectorReleaseSpeedLatch()
        {
            directorSpeedLatch = false;
            directorSpeedUntil = 0;
        }

        internal bool TryDirectorSetStage(string stage)
        {
            if (Scene == null || Flags == null) return false;
            if (MathEngine.Active) DirectorStopMathLab("切换阶段，退出 SR6 数学实验台");
            if (Timeline.Active || timelineActorsFrozen) DirectorStopTimeline("切换阶段，退出时间轴变式");
            int hash = Animator.StringToHash(stage);
            var fem = Actor(ActorSlot.Female0);
            if (fem != null && fem.animBody != null && !fem.animBody.HasState(0, hash)) return false;

            bool usedNative = false;
            try
            {
                var lstProc = RuntimeAccess.Read(Scene, "lstProc") as IList;
                int mIdx = (int)Flags.mode;
                if (lstProc != null && mIdx >= 0 && mIdx < lstProc.Count && lstProc[mIdx] != null)
                {
                    var procObj = lstProc[mIdx];
                    var sp = procObj.GetType().GetMethod("SetPlay", DirectorFlags, null, new[] { typeof(string), typeof(bool) }, null);
                    if (sp != null) { sp.Invoke(procObj, new object[] { stage, false }); usedNative = true; }
                }
            }
            catch { }

            foreach (ActorSlot slot in Enum.GetValues(typeof(ActorSlot)))
            {
                var actor = Actor(slot);
                if (actor != null && actor.animBody != null && actor.animBody.HasState(0, hash))
                {
                    if (!usedNative || actor.animBody.GetCurrentAnimatorStateInfo(0).shortNameHash != hash)
                        actor.AnimPlay(stage);
                }
            }
            Flags.nowAnimStateName = stage;
            return true;
        }

        private static string[] ExpandPoseAliases(string query)
        {
            string q = (query ?? "").Trim();
            if (q.Length == 0) return new string[0];
            var list = new List<string> { q };
            if (q.IndexOf("正常", StringComparison.Ordinal) >= 0 || q.IndexOf("传教", StringComparison.Ordinal) >= 0)
                list.AddRange(new[] { "正常位", "開脚正常位", "机寝位", "椅子対面", "sonyu_0", "sonyu_1" });
            if (q.IndexOf("骑乘", StringComparison.Ordinal) >= 0 || q.IndexOf("乘骑", StringComparison.Ordinal) >= 0 ||
                q.IndexOf("騎乗", StringComparison.Ordinal) >= 0 || q.IndexOf("女上位", StringComparison.Ordinal) >= 0 ||
                q.IndexOf("上位", StringComparison.Ordinal) >= 0 || q.IndexOf("坐上来", StringComparison.Ordinal) >= 0 ||
                q.IndexOf("骑上去", StringComparison.Ordinal) >= 0 || q.IndexOf("cowgirl", StringComparison.OrdinalIgnoreCase) >= 0)
                list.AddRange(new[] { "騎乗位", "ソファ騎乗位", "sonyu_4", "sonyu_27", "sonyu3P_0", "椅子対面" });
            if (q.IndexOf("后背", StringComparison.Ordinal) >= 0 || q.IndexOf("後背", StringComparison.Ordinal) >= 0 || q.IndexOf("背后", StringComparison.Ordinal) >= 0 || q.IndexOf("后入", StringComparison.Ordinal) >= 0)
                list.AddRange(new[] { "後背位", "バック", "椅子バック", "壁バック", "机バック", "寝バック", "sonyu_2" });
            if (q.IndexOf("面对面", StringComparison.Ordinal) >= 0 || q.IndexOf("对面", StringComparison.Ordinal) >= 0 || q.IndexOf("対面", StringComparison.Ordinal) >= 0)
                list.AddRange(new[] { "対面", "椅子対面", "壁対面", "駅弁", "正常位" });
            if (q.IndexOf("侧卧", StringComparison.Ordinal) >= 0 || q.IndexOf("侧位", StringComparison.Ordinal) >= 0 || q.IndexOf("側位", StringComparison.Ordinal) >= 0)
                list.AddRange(new[] { "側位", "机側位", "寝バック", "sonyu_6" });
            if (q.IndexOf("立位", StringComparison.Ordinal) >= 0 || q.IndexOf("站立", StringComparison.Ordinal) >= 0 || q.IndexOf("站着", StringComparison.Ordinal) >= 0)
                list.AddRange(new[] { "立位", "駅弁", "壁対面", "壁バック", "sonyu_7" });
            if (q.IndexOf("爱抚", StringComparison.Ordinal) >= 0 || q.IndexOf("愛撫", StringComparison.Ordinal) >= 0 || q.IndexOf("前戏", StringComparison.Ordinal) >= 0)
                list.AddRange(new[] { "愛撫", "aibu_" });
            if (q.IndexOf("侍奉", StringComparison.Ordinal) >= 0 || q.IndexOf("口交", StringComparison.Ordinal) >= 0 || q.IndexOf("乳交", StringComparison.Ordinal) >= 0 || q.IndexOf("手交", StringComparison.Ordinal) >= 0)
                list.AddRange(new[] { "フェラ", "パイズリ", "手コキ", "houshi_" });
            return list.ToArray();
        }

        private static bool LooksLikeSonyuHint(string query)
        {
            string q = (query ?? "").Trim().ToLowerInvariant();
            if (q.Length == 0) return true;
            return q.StartsWith("sonyu") ||
                   q.Contains("正常") || q.Contains("骑乘") || q.Contains("乘骑") || q.Contains("騎乗") || q.Contains("女上位") ||
                   q.Contains("后背") || q.Contains("後背") || q.Contains("后入") || q.Contains("バック") ||
                   q.Contains("面对面") || q.Contains("対面") || q.Contains("侧") || q.Contains("側") ||
                   q.Contains("立位") || q.Contains("站立") || q.Contains("插入") || q.Contains("駅弁");
        }

        /// <summary>
        /// Builds the complete classified H-pose catalog for the current H-scene / H-point,
        /// covering all native, kPlug, and modded animations with family, Chinese variant label,
        /// facing priority (face-to-face first), and strict direct-penetration tagging.
        /// </summary>
        internal List<PoseCatalogEntry> DirectorBuildPoseCatalog(bool onlySelectable, out Dictionary<string, HSceneProc.AnimationListInfo> infoMap)
        {
            RefreshCatalogue();
            var entries = new List<PoseCatalogEntry>();
            infoMap = new Dictionary<string, HSceneProc.AnimationListInfo>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < actions.Count; i++)
            {
                var info = actions[i];
                bool selectable = CanSelect(info);
                if (onlySelectable && !selectable) continue;
                var def = ResolveActionDefinition(info, false);
                if (!seen.Add(def.Key)) continue;
                var entry = PoseCatalogClassifier.Classify(
                    def.Key,
                    def.Name,
                    info.nameAnimation ?? "",
                    info.mode.ToString(),
                    info.id,
                    def.Effector.ToString(),
                    def.Contact.ToString(),
                    def.AnalAllowed,
                    selectable,
                    info.isFemaleInitiative);
                entries.Add(entry);
                infoMap[entry.Key] = info;
            }
            entries.Sort((a, b) => PoseCatalogClassifier.CompareForPriority(a, b, true));
            return entries;
        }

        internal PoseCatalogEntry DirectorClassifyCurrentPose()
        {
            if (Flags == null || Flags.nowAnimationInfo == null) return null;
            var info = Flags.nowAnimationInfo;
            var def = Catalogue != null
                ? ResolveActionDefinition(info, Flags.isAnalPlay)
                : null;
            if (def == null) return null;
            return PoseCatalogClassifier.Classify(
                def.Key,
                def.Name,
                info.nameAnimation ?? "",
                info.mode.ToString(),
                info.id,
                def.Effector.ToString(),
                def.Contact.ToString(),
                def.AnalAllowed,
                true,
                info.isFemaleInitiative);
        }

        /// <summary>
        /// Resolves a pose using PoseCatalogClassifier (Option B: facing-priority classic match +
        /// sibling variant list + exact-match detection), falling back cleanly to alias lookup.
        /// </summary>
        internal HSceneProc.AnimationListInfo FindBestSelectablePose(string keyOrHint, bool preferSonyu, out string resolvedKey, out string resolvedName)
        {
            PoseResolveResult res;
            return FindBestSelectablePoseDetailed(keyOrHint, preferSonyu, out res, out resolvedKey, out resolvedName);
        }

        internal HSceneProc.AnimationListInfo FindBestSelectablePoseDetailed(string keyOrHint, bool preferSonyu, out PoseResolveResult resolveResult, out string resolvedKey, out string resolvedName)
        {
            resolvedKey = "";
            resolvedName = "";
            Dictionary<string, HSceneProc.AnimationListInfo> infoMap;
            var catalog = DirectorBuildPoseCatalog(true, out infoMap);
            resolveResult = PoseCatalogClassifier.ResolveQuery(catalog, keyOrHint, true, preferSonyu || LooksLikeSonyuHint(keyOrHint));
            if (resolveResult != null && resolveResult.BestMatch != null)
            {
                HSceneProc.AnimationListInfo matched;
                if (infoMap.TryGetValue(resolveResult.BestMatch.Key, out matched))
                {
                    resolvedKey = resolveResult.BestMatch.Key;
                    resolvedName = resolveResult.BestMatch.VariantLabel;
                    return matched;
                }
            }

            // Legacy alias fallback
            string[] aliases = ExpandPoseAliases(keyOrHint);
            for (int a = 0; a < aliases.Length; a++)
            {
                string term = aliases[a];
                for (int i = 0; i < catalog.Count; i++)
                {
                    var e = catalog[i];
                    if (e.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        e.VariantLabel.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        e.GameName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        e.Key.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        HSceneProc.AnimationListInfo matched;
                        if (infoMap.TryGetValue(e.Key, out matched))
                        {
                            resolvedKey = e.Key;
                            resolvedName = e.VariantLabel;
                            resolveResult = new PoseResolveResult { BestMatch = e, IsExactMatch = false, MatchedFamily = e.Family };
                            return matched;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Clears Koikatsu's voiceWait / entry-voice-300 blocker so HSceneProc.ChangeAnimator and
        /// HSonyu.Proc(Idle -> Insert -> InsertIdle -> WLoop) NEVER stall waiting for idle chatter!
        /// </summary>
        private void ClearBlockingVoiceWaitForInsert(bool detachAibuHand)
        {
            if (Flags == null || Scene == null) return;
            try
            {
                Flags.voiceWait = false;
                Flags.isDenialvoiceWait = false;
                if (Flags.voice != null && Flags.voice.playVoices != null)
                {
                    for (int i = 0; i < Flags.voice.playVoices.Length; i++)
                    {
                        if (Flags.voice.playVoices[i] == 300 || Flags.voice.playVoices[i] == 302 || Flags.voice.playVoices[i] == 200 || Flags.voice.playVoices[i] == 100)
                            Flags.voice.playVoices[i] = -1;
                    }
                }
                // If HVoiceCtrl.nowVoices[0].state != none (e.g. pose entry line 300), HSonyu.Proc IL_041C stalls
                // before SetPlay("Insert")! Stop the blocking idle/entry voice cleanly so insertion executes immediately.
                var hVoice = RuntimeAccess.Read(Scene, "voice");
                var nowVoices = RuntimeAccess.Read(hVoice, "nowVoices") as Array;
                if (nowVoices != null && nowVoices.Length > 0 && nowVoices.GetValue(0) != null)
                {
                    var v0 = nowVoices.GetValue(0);
                    var stField = v0.GetType().GetField("state", DirectorFlags);
                    if (stField != null)
                    {
                        int cur = Convert.ToInt32(stField.GetValue(v0));
                        if (cur != 0)
                        {
                            stField.SetValue(v0, Enum.ToObject(stField.FieldType, 0));
                            try
                            {
                                var mouthArr = Flags.transVoiceMouth;
#if KKS
                                if (mouthArr != null && mouthArr.Length > 0 && mouthArr[0] != null)
                                    Manager.Voice.Stop(mouthArr[0]);
#else
                                var voiceMgr = RuntimeAccess.Find("Manager.Voice");
                                if (mouthArr != null && mouthArr.Length > 0 && mouthArr[0] != null && voiceMgr != null)
                                {
                                    var instProp = voiceMgr.BaseType != null ? voiceMgr.BaseType.GetProperty("Instance", DirectorFlags) : null;
                                    var inst = instProp != null ? instProp.GetValue(null, null) : null;
                                    var stopM = inst != null ? voiceMgr.GetMethod("Stop", DirectorFlags, null, new[] { typeof(Transform) }, null) : null;
                                    if (stopM != null) stopM.Invoke(inst, new object[] { mouthArr[0] });
                                }
#endif
                            }
                            catch { }
                        }
                    }
                }
                if (detachAibuHand)
                {
                    var hand = RuntimeAccess.Read(Scene, "hand");
                    if (hand != null)
                    {
                        var det = hand.GetType().GetMethod("DetachAllItem", DirectorFlags, null, Type.EmptyTypes, null);
                        if (det != null) det.Invoke(hand, null);
                    }
                }
            }
            catch { }
        }

        private bool ForceNativeInsertPlay(bool anal)
        {
            if (Scene == null || Flags == null) return false;
            try
            {
                string insState = anal ? "A_Insert" : "Insert";
                var lstProc = RuntimeAccess.Read(Scene, "lstProc") as IList;
                int mIdx = (int)Flags.mode;
                bool played = false;
                if (lstProc != null && mIdx >= 0 && mIdx < lstProc.Count && lstProc[mIdx] != null)
                {
                    var procObj = lstProc[mIdx];
                    var sp = procObj.GetType().GetMethod("SetPlay", DirectorFlags, null, new[] { typeof(string), typeof(bool) }, null);
                    if (sp != null) { sp.Invoke(procObj, new object[] { insState, true }); played = true; }
                }
                if (!played) played = TryDirectorSetStage(insState);
                if (played)
                {
                    Flags.isAnalPlay = anal;
                    Flags.voiceWait = false;
                    try
                    {
                        if (anal) Flags.AddSonyuAnalPlay();
                        else Flags.AddSonyuKokanPlay();
                    }
                    catch { }
                    try
                    {
                        var spObj = RuntimeAccess.Read(Scene, "sprite");
                        var btnM = spObj != null ? spObj.GetType().GetMethod("SetSonyuActionButtonActive", DirectorFlags) : null;
                        if (btnM != null) btnM.Invoke(spObj, new object[] { false, -1 });
                    }
                    catch { }
                }
                return played;
            }
            catch { return false; }
        }

        internal void StopDirectorCoroutines()
        {
            if (autoInsertCoroutine != null) { try { StopCoroutine(autoInsertCoroutine); } catch { } autoInsertCoroutine = null; }
            if (seamlessClimaxCoroutine != null) { try { StopCoroutine(seamlessClimaxCoroutine); } catch { } seamlessClimaxCoroutine = null; }
        }

        internal void ResetDirectorSessionStats()
        {
            StopDirectorCoroutines();
            ClimaxCompletedCount = 0;
        }

        internal PoseSwitchOutcome DirectorSwitchPose(string key, bool anal, bool autoInsert, bool startMotion = true)
        {
            if (MathEngine.Active) DirectorStopMathLab(L("切换体位，退出 SR6 数学实验台", "Pose switched; stopped SR6 Math Lab"));
            if (Timeline.Active || timelineActorsFrozen) DirectorStopTimeline(L("切换体位，退出时间轴变式", "Pose switched; stopped Timeline variant"));
            StopDirectorCoroutines();

            PoseResolveResult res;
            string resolvedKey, resolvedName;
            var match = FindBestSelectablePoseDetailed(key, autoInsert, out res, out resolvedKey, out resolvedName);
            if (match == null) throw new ArgumentException("当前场景未找到可用体位: " + key);

            bool isKPlug = resolvedKey.StartsWith("kplug_", StringComparison.OrdinalIgnoreCase);
            bool needSwitch = !IsSameAnimationInfo(Flags.nowAnimationInfo, match);
            Flags.isAnalPlay = anal;
            if (needSwitch)
            {
                ClearBlockingVoiceWaitForInsert(Flags.mode == HFlag.EMode.aibu);
                if (isKPlug)
                {
                    TrySelectKPlugExtra(match);
                }
                else
                {
                    SelectAction(match);
                    // Invoke ChangeAnimator directly when already in the same mode OR if HSceneProc needs immediate handoff
                    if (Flags.nowAnimationInfo != null && Flags.nowAnimationInfo.mode == match.mode && !IsSameAnimationInfo(Flags.nowAnimationInfo, match))
                    {
                        var chg = typeof(HSceneProc).GetMethod("ChangeAnimator", DirectorFlags);
                        if (chg != null) { chg.Invoke(Scene, new object[] { match, false }); Flags.selectAnimationListInfo = null; }
                    }
                }
            }
            // Always run the settle coroutine (when autoInsert=false & startMotion=false it ONLY ensures ChangeAnimator settles into Idle!)
            autoInsertCoroutine = StartCoroutine(IE_AutoInsertAndStart(match, anal, autoInsert, startMotion, resolvedName));

            var outcome = new PoseSwitchOutcome
            {
                Resolved = res != null ? res.BestMatch : null,
                IsExactMatch = res != null && res.IsExactMatch,
                AutoInsert = autoInsert,
                StartMotion = startMotion
            };
            if (res != null && res.Alternatives != null)
            {
                for (int i = 0; i < res.Alternatives.Count; i++) outcome.Alternatives.Add(res.Alternatives[i]);
            }
            return outcome;
        }

        private IEnumerator IE_AutoInsertAndStart(HSceneProc.AnimationListInfo target, bool anal, bool autoInsert, bool startMotion, string displayName)
        {
            string modeStr = target != null ? target.mode.ToString() : (Flags != null ? Flags.mode.ToString() : "");

            // 1) Wait for HSceneProc.Update() to complete ChangeAnimator (and force-invoke if voiceWait stalled it)
            if (target != null)
            {
                float waitEnd = Time.realtimeSinceStartup + 2.5f;
                bool forcedChg = false;
                while (Time.realtimeSinceStartup < waitEnd && Flags != null && Scene != null && !Scene.isEnd)
                {
                    if (IsSameAnimationInfo(Flags.nowAnimationInfo, target))
                        break;
                    Flags.voiceWait = false;
                    Flags.isDenialvoiceWait = false;
                    if (!forcedChg && Time.realtimeSinceStartup > waitEnd - 1.95f)
                    {
                        forcedChg = true;
                        try
                        {
                            ClearBlockingVoiceWaitForInsert(true);
                            var chg = typeof(HSceneProc).GetMethod("ChangeAnimator", DirectorFlags);
                            if (chg != null) { chg.Invoke(Scene, new object[] { target, false }); Flags.selectAnimationListInfo = null; }
                        }
                        catch { }
                    }
                    yield return null;
                }
            }
            // Give Unity Animator 2 frames to settle into Idle
            yield return null;
            yield return null;
            if (Scene == null || Scene.isEnd || Flags == null) { autoInsertCoroutine = null; yield break; }

            modeStr = Flags.mode.ToString();

            // When prepare-only (autoInsert=false, startMotion=false), settle cleanly in Idle and stop!
            if (!autoInsert && !startMotion)
            {
                uiNotice = string.Format(L("已切至【{0}】待机，等待确认开始", "Switched to [{0}] standby; waiting to start"), string.IsNullOrEmpty(displayName) ? modeStr : displayName);
                autoInsertCoroutine = null;
                yield break;
            }

            // 2) If sonyu*, reliably drive Koikatsu's real HSonyu state machine: Idle -> Insert -> InsertIdle!
            if (autoInsert && modeStr.StartsWith("sonyu", StringComparison.Ordinal))
            {
                if (Flags.isInsertOK != null)
                {
                    for (int i = 0; i < Flags.isInsertOK.Length; i++) Flags.isInsertOK[i] = true;
                }
                Flags.isAnalInsertOK = true;
                Flags.isAnalPlay = anal;
                Flags.isDenialvoiceWait = false;
                Flags.voiceWait = false;

                // 2a) Drive Idle -> Insert (clear blocking entry voice 300 & pulse Flags.click each frame until Insert begins!)
                float idleStart = Time.realtimeSinceStartup;
                float idleLimit = idleStart + 3.0f;
                bool forcedPlay = false;
                while (Time.realtimeSinceStartup < idleLimit && Flags != null && Scene != null && !Scene.isEnd)
                {
                    string st = GetLiveFemaleAnimStateName();
                    if (!IsUninsertedStateName(st)) break;

                    ClearBlockingVoiceWaitForInsert(false);
                    var insClick = anal ? HFlag.ClickKind.insert_anal : HFlag.ClickKind.insert;
                    EventBus.MarkDirectorClick(insClick);
                    Flags.click = insClick;

                    if (!forcedPlay && Time.realtimeSinceStartup - idleStart > 1.35f)
                    {
                        forcedPlay = true;
                        ForceNativeInsertPlay(anal);
                    }
                    yield return null;
                }

                // 2b) Wait cleanly for Insert/A_Insert clip to finish and transition into InsertIdle (so SetInsertKokan runs!)
                float insClipLimit = Time.realtimeSinceStartup + 4.8f;
                while (Time.realtimeSinceStartup < insClipLimit && Flags != null && Scene != null && !Scene.isEnd)
                {
                    string st = GetLiveFemaleAnimStateName();
                    if (!IsInsertTransitionName(st) && !IsUninsertedStateName(st)) break;
                    Flags.voiceWait = false;
                    yield return null;
                }
                yield return new WaitForSeconds(0.18f);
            }
            else if (modeStr.StartsWith("houshi", StringComparison.Ordinal) && startMotion)
            {
                ClearBlockingVoiceWaitForInsert(false);
                EventBus.MarkDirectorClick(HFlag.ClickKind.speedup);
                Flags.click = HFlag.ClickKind.speedup;
                yield return new WaitForSeconds(0.30f);
            }
            else if (modeStr == "aibu" && startMotion)
            {
                TryAttachAibuItem();
                uiNotice = L("已开始温柔爱抚互动", "Started gentle foreplay");
                autoInsertCoroutine = null;
                yield break;
            }

            // 3) Drive InsertIdle -> WLoop using Koikatsu's real ClickKind.speedup (IL_0769), with SetPlay fallback!
            if (startMotion && Scene != null && !Scene.isEnd && Flags != null)
            {
                float moveLimit = Time.realtimeSinceStartup + 1.5f;
                while (Time.realtimeSinceStartup < moveLimit && Flags != null && Scene != null && !Scene.isEnd)
                {
                    string st = GetLiveFemaleAnimStateName();
                    if (IsActiveLoopStateName(st)) break;
                    Flags.voiceWait = false;
                    if (IsInsertIdleStateName(st))
                    {
                        EventBus.MarkDirectorClick(HFlag.ClickKind.speedup);
                        Flags.click = HFlag.ClickKind.speedup;
                        Flags.SpeedUpClick(0.35f, 1f);
                    }
                    yield return null;
                }

                string finalSt = GetLiveFemaleAnimStateName();
                if (!IsActiveLoopStateName(finalSt))
                {
                    string loopSt = (anal && modeStr.StartsWith("sonyu", StringComparison.Ordinal)) ? "A_WLoop" : "WLoop";
                    if (!TryDirectorSetStage(loopSt)) TryDirectorSetStage("SLoop");
                }
                DirectorSetSpeedLatch(0.45, 25.0);
                uiNotice = string.Format(L("已自动进入{0}并开始温柔律动", "Auto-entered {0} with gentle rhythm"), string.IsNullOrEmpty(displayName) ? L("插入状态", "insertion") : displayName);
            }
            autoInsertCoroutine = null;
        }

        internal void DirectorGameAction(string action, double? female, double? male)
        {
            switch (action)
            {
                case "insert":
                case "start_insert":
                    if (!Flags.mode.ToString().StartsWith("sonyu", StringComparison.Ordinal))
                    {
                        // Currently in aibu / houshi / etc.: automatically switch to a face-to-face selectable sonyu pose and insert!
                        DirectorSwitchPose("正常位", false, true, true);
                        break;
                    }
                    if (Flags.isInsertOK != null)
                    {
                        for (int i = 0; i < Flags.isInsertOK.Length; i++) Flags.isInsertOK[i] = true;
                    }
                    Flags.isAnalPlay = false;
                    ClearBlockingVoiceWaitForInsert(false);
                    EventBus.MarkDirectorClick(HFlag.ClickKind.insert);
                    Flags.click = HFlag.ClickKind.insert;
                    if (autoInsertCoroutine != null) StopCoroutine(autoInsertCoroutine);
                    autoInsertCoroutine = StartCoroutine(IE_AutoInsertAndStart(Flags.nowAnimationInfo, false, true, true, Profile != null ? Profile.Name : ""));
                    break;
                case "start_motion":
                    if (Flags.mode == HFlag.EMode.aibu) { TryAttachAibuItem(); break; }
                    if (IsDirectPenetrativeInserted())
                    {
                        string curSt = GetLiveFemaleAnimStateName();
                        if (IsInsertIdleStateName(curSt))
                        {
                            Flags.voiceWait = false;
                            EventBus.MarkDirectorClick(HFlag.ClickKind.speedup);
                            Flags.click = HFlag.ClickKind.speedup;
                        }
                        if (IsNonMotionStageName(curSt)) TryDirectorSetStage(Flags.isAnalPlay ? "A_WLoop" : "WLoop");
                        DirectorSetSpeedLatch(0.45, 25.0);
                    }
                    else if (Flags.mode.ToString().StartsWith("houshi", StringComparison.Ordinal))
                    {
                        ClearBlockingVoiceWaitForInsert(false);
                        EventBus.MarkDirectorClick(HFlag.ClickKind.speedup);
                        Flags.click = HFlag.ClickKind.speedup;
                        DirectorSetSpeedLatch(0.45, 25.0);
                    }
                    else if (Flags.mode.ToString().StartsWith("sonyu", StringComparison.Ordinal))
                    {
                        if (autoInsertCoroutine != null) StopCoroutine(autoInsertCoroutine);
                        autoInsertCoroutine = StartCoroutine(IE_AutoInsertAndStart(Flags.nowAnimationInfo, Flags.isAnalPlay, true, true, Profile != null ? Profile.Name : ""));
                    }
                    else
                    {
                        DirectorSwitchPose("正常位", false, true, true);
                    }
                    break;
                case "insert_anal":
                    if (!Flags.mode.ToString().StartsWith("sonyu", StringComparison.Ordinal))
                    {
                        DirectorSwitchPose("後背位", true, true, true);
                        break;
                    }
                    Flags.isAnalInsertOK = true;
                    Flags.isAnalPlay = true;
                    ClearBlockingVoiceWaitForInsert(false);
                    EventBus.MarkDirectorClick(HFlag.ClickKind.insert_anal);
                    Flags.click = HFlag.ClickKind.insert_anal;
                    if (autoInsertCoroutine != null) StopCoroutine(autoInsertCoroutine);
                    autoInsertCoroutine = StartCoroutine(IE_AutoInsertAndStart(Flags.nowAnimationInfo, true, true, true, Profile != null ? Profile.Name : ""));
                    break;
                case "pull":
                case "pull_out":
                    StopDirectorCoroutines();
                    if (MathEngine.Active) DirectorStopMathLab(L("抽出，退出 SR6 数学实验台", "Pulled out; stopped SR6 Math Lab"));
                    if (Timeline.Active || timelineActorsFrozen) DirectorStopTimeline(L("抽出，退出时间轴变式", "Pulled out; stopped Timeline variant"));
                    EventBus.MarkDirectorClick(HFlag.ClickKind.pull);
                    Flags.click = HFlag.ClickKind.pull;
                    break;
                case "seamless_climax":
                case "climax":
                    DirectorTriggerSeamlessClimax("sameS");
                    break;
                case "finish_inside":
                    DirectorTriggerSeamlessClimax("inside");
                    break;
                case "finish_outside":
                    DirectorTriggerSeamlessClimax("outside");
                    break;
                case "finish_same":
                case "finish_same_strong":
                    DirectorTriggerSeamlessClimax("sameS");
                    break;
                case "finish_same_weak":
                    StopDirectorCoroutines();
                    if (MathEngine.Active) DirectorStopMathLab("climax");
                    if (Timeline.Active || timelineActorsFrozen) DirectorStopTimeline("climax");
                    ReleaseDirectorGaugeControl();
                    EventBus.MarkDirectorClick(HFlag.ClickKind.sameW);
                    Flags.click = HFlag.ClickKind.sameW;
                    break;
                case "orgasm_weak":
                    StopDirectorCoroutines();
                    if (MathEngine.Active) DirectorStopMathLab("climax");
                    if (Timeline.Active || timelineActorsFrozen) DirectorStopTimeline("climax");
                    ReleaseDirectorGaugeControl();
                    EventBus.MarkDirectorClick(HFlag.ClickKind.orgW);
                    Flags.click = HFlag.ClickKind.orgW;
                    break;
                case "orgasm_strong":
                    StopDirectorCoroutines();
                    if (MathEngine.Active) DirectorStopMathLab("climax");
                    if (Timeline.Active || timelineActorsFrozen) DirectorStopTimeline("climax");
                    ReleaseDirectorGaugeControl();
                    EventBus.MarkDirectorClick(HFlag.ClickKind.orgS);
                    Flags.click = HFlag.ClickKind.orgS;
                    break;
                case "finish_drink":
                    StopDirectorCoroutines();
                    if (MathEngine.Active) DirectorStopMathLab("climax");
                    if (Timeline.Active || timelineActorsFrozen) DirectorStopTimeline("climax");
                    ReleaseDirectorGaugeControl();
                    EventBus.MarkDirectorClick(HFlag.ClickKind.drink);
                    Flags.click = HFlag.ClickKind.drink;
                    break;
                case "finish_vomit":
                    StopDirectorCoroutines();
                    if (MathEngine.Active) DirectorStopMathLab("climax");
                    if (Timeline.Active || timelineActorsFrozen) DirectorStopTimeline("climax");
                    ReleaseDirectorGaugeControl();
                    EventBus.MarkDirectorClick(HFlag.ClickKind.vomit);
                    Flags.click = HFlag.ClickKind.vomit;
                    break;
                case "lock_gauges":
                    Flags.lockGugeFemale = true;
                    Flags.lockGugeMale = true;
                    break;
                case "unlock_gauges":
                    Flags.lockGugeFemale = false;
                    Flags.lockGugeMale = false;
                    break;
                case "set_gauges":
                    if (female.HasValue) Flags.gaugeFemale = (float)Numbers.Clamp(female.Value, 0.0, 100.0);
                    if (male.HasValue) Flags.gaugeMale = (float)Numbers.Clamp(male.Value, 0.0, 100.0);
                    break;
                case "aibu_attach":
                    TryAttachAibuItem();
                    break;
                default:
                    throw new ArgumentException("Unsupported action: " + action);
            }
        }

        // Must be called at WaitForEndOfFrame so all LateUpdate() (ChaControl, KKABMX, MotionIK,
        // DynamicBone, BetterPenetration) and BeforeCamera() (SolveFrame, UpdatePreview) have completed,
        // while rendering Camera.main cleanly without KoiSync's IMGUI overlay obscuring the scene.
        internal byte[] DirectorCaptureScreenshot(int width, int height, out string filePath)
        {
            var cam = Camera.main;
            if (cam == null) throw new InvalidOperationException("当前场景无主摄像机");
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply(false, false);
                byte[] png = tex.EncodeToPNG();
                string dir = Path.Combine(DataRoot, "Director");
                Directory.CreateDirectory(dir);
                filePath = Path.Combine(dir, "latest_view.png");
                File.WriteAllBytes(filePath, png);
                return png;
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                Destroy(tex);
                rt.Release();
                Destroy(rt);
            }
        }

        private static bool IsSameAnimationInfo(HSceneProc.AnimationListInfo a, HSceneProc.AnimationListInfo b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.mode != b.mode || a.id != b.id) return false;
            return string.Equals(ResourceIdentity(a), ResourceIdentity(b), StringComparison.Ordinal);
        }

        private void TrySelectKPlugExtra(HSceneProc.AnimationListInfo info)
        {
            SelectKPlugExtraAnimation(info);
        }

        private void TryAttachAibuItem()
        {
            var hand = RuntimeAccess.Read(Scene, "hand");
            var areas = RuntimeAccess.Read(hand, "dicAreaLayerInfos") as Array;
            var items = RuntimeAccess.Read(hand, "dicItem") as IDictionary;
            if (areas == null || areas.Length < 3 || items == null) return;
            var layers = areas.GetValue(2) as IDictionary;
            if (layers == null) return;
            foreach (DictionaryEntry row in layers)
            {
                var ids = RuntimeAccess.Read(row.Value, "idObjects") as int[];
                if (ids == null || ids.Length == 0 || !items.Contains(ids[0])) continue;
                var item = items[ids[0]];
                int id = Convert.ToInt32(RuntimeAccess.Read(item, "idUse"));
                bool front = Flags.nowAnimationInfo.paramFemale.lstFrontAndBehind[0] == 0;
                hand.GetType().GetMethod("SetItem", DirectorFlags).Invoke(hand, new object[] { id, 2, row.Value, 0, HandCtrl.AibuColliderKind.kokan, front, false });
                hand.GetType().GetMethod("SetLayerWeight", DirectorFlags).Invoke(hand, new object[] { id, row.Value, front, 2 });
                var f = hand.GetType().GetField("actionUseItem", DirectorFlags);
                if (f != null) f.SetValue(hand, id);
                Flags.speedItem = 1f;
                return;
            }
        }
    }
}
