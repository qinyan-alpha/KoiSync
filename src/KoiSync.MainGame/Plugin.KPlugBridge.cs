using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.MainGame
{
    internal sealed class MainFemaleSummary
    {
        public int Index;
        public string Name;
        public int OutfitSlot;
        public string OutfitName;
        public string ClothesState;
        public string ClothesDetail;
    }

    internal sealed class HaremGuestSummary
    {
        public int DealerId;
        public string Name;
        public bool Masturbating;
        public bool InOrgasm;
        public bool IsMale;
        public string ActiveAction;
    }

    internal sealed class CharacterCatalogSnapshot
    {
        public bool InHScene;
        public bool KPlugLoaded;
        public bool InvitePanelOpen;
        public readonly List<MainFemaleSummary> MainFemales = new List<MainFemaleSummary>();
        public readonly List<HaremGuestSummary> HaremGuests = new List<HaremGuestSummary>();
        public readonly List<string> InvitableStory = new List<string>();
        public readonly List<string> InvitableCards = new List<string>();
    }

    internal sealed class CharacterOpResult
    {
        public string Action;
        public string Character;
        public string Detail;
        public int Slot;
        public bool PanelOpen;
    }

    internal sealed class KsPlugExtraAnimMeta
    {
        public object AnimInfo;
        public HSceneProc.AnimationListInfo ListInfo;
        public string PlugName;
        public string DisplayName;
        public string Asset;
        public bool HasAnal;
    }

    public sealed partial class Plugin
    {
        private enum PlugBackend { None, KPlug, KsPlug }

        private static readonly string[] OutfitSlotNames = { "校服(第一套)", "第二套校服", "体操服", "泳装", "社团服", "私服", "睡衣" };
        private static readonly string[] ClothPartNames = { "上衣", "下衣", "胸罩", "内裤", "手套", "连裤袜", "袜子", "室内鞋", "室外鞋" };

        private PlugBackend plugBackend;
        private bool kPlugChecked;
        private Type kPlugCoreType;

        // Cached reflection types for KKS ksPlug v1.5+ (resolved lazily at runtime; zero compile-time dependency)
        private Type ksPlugCoreType;
        private Type ksPlugSettingsType;
        private Type ksPlugHCtrlType;
        private Type ksPlugHAnimsType;
        private Type ksPlugMenuPoseType;
        private Type ksPlugMenuInviteType;
        private Type ksPlugMenuCharaPoseInfoType;
        private Type ksPlugAIGuestType;
        private Type ksPlugAIMainType;
        private Type ksPlugGuestActionType;
        private Type ksPlugGuestActionTypeEnum;
        private Type ksPlugSpecialMoveType;
        private Type ksPlugMainActionOverType;
        private Type ksPlugAnimClipEnum;
        private Type ksPlugClothesType;
        private Type ksPlugChaListFileInfoType;

        // Phase 2: Pure Female-Initiative / Copilot Autonomy state + kPlug / ksPlug arbitration
        internal string AutonomyMode = "player"; // "player" | "copilot" | "female_active"
        internal bool AllowAutoPose = true;
        internal bool AllowAutoClimaxEdge = true;
        internal bool AllowAutoClothes = true;
        internal bool AllowAutoKPlugSwap = true;
        internal bool KPlugGirlThinkPaused { get; private set; }

        // KK kPlug saved arbitration state
        private bool? savedKPlugMainAiGirlThink;
        private bool? savedKPlugCfgGirlThink;
        private object savedKPlugMainAiRef;

        // KKS ksPlug saved arbitration state
        private bool? savedKsPlugMgThink;
        private bool? savedKsPlugGgThink;
        private bool ksPlugArbitrationSuspendedForConfig;

        // KKS ksPlug extra-animation metadata cache keyed by ResourceIdentity(AnimationListInfo)
        private readonly Dictionary<string, KsPlugExtraAnimMeta> ksPlugExtraAnimsByResource =
            new Dictionary<string, KsPlugExtraAnimMeta>(StringComparer.Ordinal);
        private bool ksPlugCataloguePopulated;
        private float nextKsPlugCatalogueRetryTime;

        private PlugBackend EnsurePlugBackend()
        {
            if (!kPlugChecked)
            {
                kPlugChecked = true;
#if KKS
                ksPlugCoreType = RuntimeAccess.Find("ksPlug.Core");
                if (ksPlugCoreType != null)
                {
                    plugBackend = PlugBackend.KsPlug;
                    CacheKsPlugTypes();
                    return plugBackend;
                }
                kPlugCoreType = RuntimeAccess.Find("kPlug.Core");
                if (kPlugCoreType != null) plugBackend = PlugBackend.KPlug;
#else
                kPlugCoreType = RuntimeAccess.Find("kPlug.Core");
                if (kPlugCoreType != null)
                {
                    plugBackend = PlugBackend.KPlug;
                    return plugBackend;
                }
                ksPlugCoreType = RuntimeAccess.Find("ksPlug.Core");
                if (ksPlugCoreType != null)
                {
                    plugBackend = PlugBackend.KsPlug;
                    CacheKsPlugTypes();
                }
#endif
            }
            return plugBackend;
        }

        private void CacheKsPlugTypes()
        {
            ksPlugSettingsType = RuntimeAccess.Find("ksPlug.Settings");
            ksPlugHCtrlType = RuntimeAccess.Find("ksPlug.HSession.HCtrl");
            ksPlugHAnimsType = RuntimeAccess.Find("ksPlug.HSession.HAnims");
            ksPlugMenuPoseType = RuntimeAccess.Find("ksPlug.Menu.MenuPose");
            ksPlugMenuInviteType = RuntimeAccess.Find("ksPlug.Menu.MenuInvite");
            ksPlugMenuCharaPoseInfoType = RuntimeAccess.Find("ksPlug.Menu.MenuChara+PoseInfo");
            ksPlugAIGuestType = RuntimeAccess.Find("ksPlug.Character.AIGuest");
            ksPlugAIMainType = RuntimeAccess.Find("ksPlug.Character.AIMain");
            ksPlugGuestActionType = RuntimeAccess.Find("ksPlug.HSession.GuestAction");
            ksPlugGuestActionTypeEnum = RuntimeAccess.Find("ksPlug.HSession.GuestAction+Type");
            ksPlugSpecialMoveType = RuntimeAccess.Find("ksPlug.HSession.GuestActions.SpecialMove");
            ksPlugMainActionOverType = RuntimeAccess.Find("ksPlug.HSession.MainActionOver");
            ksPlugAnimClipEnum = RuntimeAccess.Find("ksPlug.Character.Animations+Clip");
            ksPlugClothesType = RuntimeAccess.Find("ksPlug.Character.Clothes");
            ksPlugChaListFileInfoType = RuntimeAccess.Find("ksPlug.Character.ChaList+FileInfo");
        }

        internal bool IsKPlugLoaded { get { return EnsurePlugBackend() != PlugBackend.None; } }
        internal bool IsKsPlugBackend { get { return EnsurePlugBackend() == PlugBackend.KsPlug; } }
        internal string PlugBrandName { get { return IsKsPlugBackend ? "ksPlug" : "kPlug"; } }

        internal void DirectorConfigureAutonomy(string mode, bool? allowPose, bool? allowClimaxEdge, bool? allowClothes, bool? allowKPlugSwap)
        {
            if (!string.IsNullOrEmpty(mode))
            {
                string m = mode.Trim().ToLowerInvariant();
                if (m == "player" || m == "copilot" || m == "female_active")
                    AutonomyMode = m;
            }
            if (allowPose.HasValue) AllowAutoPose = allowPose.Value;
            if (allowClimaxEdge.HasValue) AllowAutoClimaxEdge = allowClimaxEdge.Value;
            if (allowClothes.HasValue) AllowAutoClothes = allowClothes.Value;
            if (allowKPlugSwap.HasValue) AllowAutoKPlugSwap = allowKPlugSwap.Value;

            if (DirectorListening || (directorEnabled != null && directorEnabled.Value))
                MaintainKPlugGirlThinkArbitration();
            else
                RestoreKPlugGirlThink();

            EventBus.Emit(this, "AutonomyChanged",
                "自主模式=" + AutonomyMode +
                " (换位=" + (AllowAutoPose ? "开" : "关") +
                ", 控射=" + (AllowAutoClimaxEdge ? "开" : "关") +
                ", 换装=" + (AllowAutoClothes ? "开" : "关") +
                ", 拉人=" + (AllowAutoKPlugSwap ? "开" : "关") +
                ", " + PlugBrandName + "主动=" + (KPlugGirlThinkPaused ? "已默认关闭" : "未接管") + ")");
        }

        /// <summary>
        /// Whenever AI MCP Director is enabled, ALWAYS pause native autonomous loops by default:
        /// - KK kPlug: AI_Main.girlThink &amp; Cfg.girlThink
        /// - KKS ksPlug: Settings.MgThink &amp; Settings.GgThink (plus canceling any in-flight AIMain decision,
        ///   and temporarily restoring while ksPlug's MenuConfig is open so closing config never writes false to settings.txt)
        /// Restores original values cleanly when AI MCP is disabled or H-scene ends.
        /// </summary>
        internal void MaintainKPlugGirlThinkArbitration()
        {
            bool mcpActive = DirectorListening || (directorEnabled != null && directorEnabled.Value);
            if (!mcpActive)
            {
                if (KPlugGirlThinkPaused || savedKsPlugMgThink.HasValue || savedKsPlugGgThink.HasValue)
                    RestoreKPlugGirlThink();
                return;
            }
            if (Scene == null || Scene.isEnd || !IsKPlugLoaded) return;

            var backend = EnsurePlugBackend();
            if (backend == PlugBackend.KsPlug)
            {
                MaintainKsPlugArbitration();
                return;
            }

            try
            {
                bool pausedAny = false;
                var mainAi = GetKPlugStatic("mainAi");
                if (mainAi != null)
                {
                    if (!savedKPlugMainAiGirlThink.HasValue || !ReferenceEquals(savedKPlugMainAiRef, mainAi))
                    {
                        var cur = RuntimeAccess.Read(mainAi, "girlThink");
                        if (cur is bool) savedKPlugMainAiGirlThink = (bool)cur;
                        savedKPlugMainAiRef = mainAi;
                    }
                    if (SetMemberValue(mainAi, "girlThink", false))
                        pausedAny = true;
                }
                var cfgObj = GetKPlugStatic("cfg");
                if (cfgObj != null)
                {
                    if (!savedKPlugCfgGirlThink.HasValue)
                    {
                        var cur = RuntimeAccess.Read(cfgObj, "girlThink");
                        if (cur is bool) savedKPlugCfgGirlThink = (bool)cur;
                    }
                    if (SetMemberValue(cfgObj, "girlThink", false))
                        pausedAny = true;
                }
                KPlugGirlThinkPaused = pausedAny;
            }
            catch { }
        }

        private void MaintainKsPlugArbitration()
        {
            if (ksPlugSettingsType == null) return;
            try
            {
                bool inConfig = Convert.ToBoolean(GetStaticMemberValue(ksPlugSettingsType, "InConfigMenu") ?? false);
                if (inConfig)
                {
                    // Temporarily restore user's original MgThink/GgThink while MenuConfig is open so
                    // Settings.Save() on config close never persists our temporary false override to settings.txt.
                    if (!ksPlugArbitrationSuspendedForConfig && (savedKsPlugMgThink.HasValue || savedKsPlugGgThink.HasValue))
                    {
                        if (savedKsPlugMgThink.HasValue)
                            SetStaticMemberValue(ksPlugSettingsType, "MgThink", savedKsPlugMgThink.Value);
                        if (savedKsPlugGgThink.HasValue)
                            SetStaticMemberValue(ksPlugSettingsType, "GgThink", savedKsPlugGgThink.Value);
                        ksPlugArbitrationSuspendedForConfig = true;
                    }
                    return;
                }

                bool initialPause = !savedKsPlugMgThink.HasValue && !savedKsPlugGgThink.HasValue;
                if (initialPause || ksPlugArbitrationSuspendedForConfig)
                {
                    var curMg = GetStaticMemberValue(ksPlugSettingsType, "MgThink");
                    if (curMg is bool) savedKsPlugMgThink = (bool)curMg;
                    var curGg = GetStaticMemberValue(ksPlugSettingsType, "GgThink");
                    if (curGg is bool) savedKsPlugGgThink = (bool)curGg;
                    ksPlugArbitrationSuspendedForConfig = false;
                    StopKsPlugInFlightMainAiDecisions();
                }

                bool pausedAny = false;
                if (SetStaticMemberValue(ksPlugSettingsType, "MgThink", false)) pausedAny = true;
                if (SetStaticMemberValue(ksPlugSettingsType, "GgThink", false)) pausedAny = true;
                KPlugGirlThinkPaused = pausedAny;
            }
            catch { }
        }

        private void StopKsPlugInFlightMainAiDecisions()
        {
            if (ksPlugAIMainType == null) return;
            try
            {
                object hCtrl;
                if (!IsKsPlugHCtrlReady(out hCtrl)) return;
                CancelKsPlugMainAiCore(ReadMemberValue(hCtrl, "MainGirl"));
                CancelKsPlugMainAiCore(ReadMemberValue(hCtrl, "SecondGirl"));
            }
            catch { }
        }

        private void CancelKsPlugMainAiCore(object chaCore)
        {
            if (!IsUnityAlive(chaCore)) return;
            var comp = chaCore as Component;
            if (comp == null) return;
            var aiMain = comp.GetComponent(ksPlugAIMainType) as MonoBehaviour;
            if (!IsUnityAlive(aiMain)) return;
            var mind = ReadMemberValue(aiMain, "Mind");
            if (mind == null) return;
            var inExecObj = ReadMemberValue(mind, "inExecute");
            if (inExecObj is bool && (bool)inExecObj)
            {
                aiMain.StopAllCoroutines();
                SetMemberValue(mind, "inExecute", false);
            }
        }

        internal void RestoreKPlugGirlThink()
        {
            if (!KPlugGirlThinkPaused &&
                !savedKPlugMainAiGirlThink.HasValue && !savedKPlugCfgGirlThink.HasValue &&
                !savedKsPlugMgThink.HasValue && !savedKsPlugGgThink.HasValue)
                return;

            try
            {
                if (EnsurePlugBackend() == PlugBackend.KsPlug)
                {
                    if (ksPlugSettingsType != null)
                    {
                        if (savedKsPlugMgThink.HasValue)
                            SetStaticMemberValue(ksPlugSettingsType, "MgThink", savedKsPlugMgThink.Value);
                        if (savedKsPlugGgThink.HasValue)
                            SetStaticMemberValue(ksPlugSettingsType, "GgThink", savedKsPlugGgThink.Value);
                    }
                }
                else
                {
                    var mainAi = GetKPlugStatic("mainAi") ?? savedKPlugMainAiRef;
                    if (mainAi != null && savedKPlugMainAiGirlThink.HasValue)
                        SetMemberValue(mainAi, "girlThink", savedKPlugMainAiGirlThink.Value);
                    var cfgObj = GetKPlugStatic("cfg");
                    if (cfgObj != null && savedKPlugCfgGirlThink.HasValue)
                        SetMemberValue(cfgObj, "girlThink", savedKPlugCfgGirlThink.Value);
                }
            }
            catch { }

            KPlugGirlThinkPaused = false;
            savedKPlugMainAiGirlThink = null;
            savedKPlugCfgGirlThink = null;
            savedKPlugMainAiRef = null;
            savedKsPlugMgThink = null;
            savedKsPlugGgThink = null;
            ksPlugArbitrationSuspendedForConfig = false;
        }

        internal void ResetKsPlugSessionCache()
        {
            ksPlugExtraAnimsByResource.Clear();
            ksPlugCataloguePopulated = false;
            nextKsPlugCatalogueRetryTime = 0f;
        }

        private Type GetKPlugCoreType()
        {
            EnsurePlugBackend();
            return kPlugCoreType;
        }

        private object GetKPlugStatic(string memberName)
        {
            var t = GetKPlugCoreType();
            return GetStaticMemberValue(t, memberName);
        }

        private object GetKsPlugDealer()
        {
            if (!IsKsPlugBackend) return null;
            var d = GetStaticMemberValue(ksPlugCoreType, "dealer");
            return IsUnityAlive(d) ? d : null;
        }

        private object GetKsPlugHCtrl()
        {
            if (!IsKsPlugBackend) return null;
            object h = GetStaticMemberValue(ksPlugHCtrlType, "Get");
            if (!IsUnityAlive(h))
            {
                var d = GetKsPlugDealer();
                if (d != null) h = ReadMemberValue(d, "HCtrl");
            }
            return IsUnityAlive(h) ? h : null;
        }

        private bool IsKsPlugHCtrlReady(out object hCtrl)
        {
            hCtrl = GetKsPlugHCtrl();
            if (hCtrl == null) return false;
            var initObj = ReadMemberValue(hCtrl, "IsInit");
            return initObj is bool && (bool)initObj;
        }

        private object GetKsPlugHMenu(string menuFieldName)
        {
            object hCtrl;
            if (!IsKsPlugHCtrlReady(out hCtrl)) return null;
            var menus = ReadMemberValue(hCtrl, "HMenus");
            if (menus == null) return null;
            var m = ReadMemberValue(menus, menuFieldName);
            return IsUnityAlive(m) ? m : null;
        }

        private static bool IsUnityAlive(object obj)
        {
            if (obj == null) return false;
            var uo = obj as UnityEngine.Object;
            if (!ReferenceEquals(uo, null)) return uo != null;
            return true;
        }

        private static object GetStaticMemberValue(Type t, string memberName)
        {
            if (t == null) return null;
            try
            {
                var f = t.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) return f.GetValue(null);
                var p = t.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (p != null) return p.GetValue(null, null);
            }
            catch { }
            return null;
        }

        private static bool SetStaticMemberValue(Type t, string memberName, object value)
        {
            if (t == null) return false;
            try
            {
                var f = t.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, value); return true; }
                var p = t.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (p != null && p.CanWrite) { p.SetValue(null, value, null); return true; }
            }
            catch { }
            return false;
        }

        private static object ReadMemberValue(object target, string memberName)
        {
            if (target == null) return null;
            var t = target.GetType();
            while (t != null)
            {
                try
                {
                    var f = t.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (f != null) return f.GetValue(target);
                    var p = t.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (p != null && p.CanRead && p.GetIndexParameters().Length == 0) return p.GetValue(target, null);
                }
                catch { }
                t = t.BaseType;
            }
            return null;
        }

        private static bool SetMemberValue(object target, string memberName, object value)
        {
            if (target == null) return false;
            var t = target.GetType();
            while (t != null)
            {
                try
                {
                    var f = t.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (f != null) { f.SetValue(target, value); return true; }
                    var p = t.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (p != null && p.CanWrite) { p.SetValue(target, value, null); return true; }
                }
                catch { }
                t = t.BaseType;
            }
            return false;
        }

        private static object InvokeMember(object target, string methodName, params object[] args)
        {
            if (target == null) return null;
            var t = target.GetType();
            while (t != null)
            {
                var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                for (int i = 0; i < methods.Length; i++)
                {
                    var m = methods[i];
                    if (m.Name != methodName) continue;
                    var ps = m.GetParameters();
                    if (ps.Length != args.Length) continue;
                    bool match = true;
                    for (int j = 0; j < ps.Length; j++)
                    {
                        if (args[j] == null) { if (ps[j].ParameterType.IsValueType) { match = false; break; } }
                        else if (!ps[j].ParameterType.IsInstanceOfType(args[j])) { match = false; break; }
                    }
                    if (match) return m.Invoke(target, args);
                }
                t = t.BaseType;
            }
            return null;
        }

        private static object InvokeStatic(Type t, string methodName, params object[] args)
        {
            if (t == null) return null;
            var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                var m = methods[i];
                if (m.Name != methodName) continue;
                var ps = m.GetParameters();
                if (ps.Length != args.Length) continue;
                bool match = true;
                for (int j = 0; j < ps.Length; j++)
                {
                    if (args[j] == null) { if (ps[j].ParameterType.IsValueType) { match = false; break; } }
                    else if (!ps[j].ParameterType.IsInstanceOfType(args[j])) { match = false; break; }
                }
                if (match) return m.Invoke(null, args);
            }
            return null;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Extra H-Animation Bridge (KK kPlug ExtraAnimManager + KKS ksPlug HAnimInfos)
        // ─────────────────────────────────────────────────────────────────────────────

        internal void EnsureKsPlugCatalogueSynced()
        {
            if (!IsKsPlugBackend || Scene == null || Scene.isEnd || ksPlugCataloguePopulated) return;
            float now = Time.realtimeSinceStartup;
            if (now < nextKsPlugCatalogueRetryTime) return;
            nextKsPlugCatalogueRetryTime = now + 0.35f;

            object hCtrl;
            if (!IsKsPlugHCtrlReady(out hCtrl)) return;
            var hAnimInfos = ReadMemberValue(hCtrl, "HAnimInfos") as IEnumerable;
            if (hAnimInfos == null) return;

            RefreshCatalogue();
            if (ksPlugCataloguePopulated && Flags != null && Flags.nowAnimationInfo != null)
            {
                string curRes = ResourceIdentity(Flags.nowAnimationInfo);
                if (ksPlugExtraAnimsByResource.ContainsKey(curRes))
                    actionIdentity = "";
            }
        }

        private void RefreshKPlugExtraAnimations(HashSet<string> seen)
        {
            var backend = EnsurePlugBackend();
            if (backend == PlugBackend.KPlug)
            {
                var extra = RuntimeAccess.Find("kPlug.CmpH.ExtraAnimManager");
                if (extra != null) AddActions(RuntimeAccess.Read(FindObjectOfType(extra), "extraAnims"), seen, 0);
                return;
            }
            if (backend != PlugBackend.KsPlug) return;

            PopulateKsPlugExtraCache(seen, true);
        }

        private void PopulateKsPlugExtraCache(HashSet<string> seen, bool appendToActions)
        {
            ksPlugExtraAnimsByResource.Clear();
            object hCtrl;
            if (!IsKsPlugHCtrlReady(out hCtrl))
            {
                if (appendToActions) ksPlugCataloguePopulated = false;
                return;
            }

            var hAnimInfos = ReadMemberValue(hCtrl, "HAnimInfos") as IEnumerable;
            if (hAnimInfos == null)
            {
                if (appendToActions) ksPlugCataloguePopulated = false;
                return;
            }

            var poseNames = GetStaticMemberValue(ksPlugMenuPoseType, "PoseNames") as IDictionary;
            int cachedCount = 0;

            foreach (var animInfo in hAnimInfos)
            {
                if (animInfo == null) continue;
                bool isExtra = Convert.ToBoolean(ReadMemberValue(animInfo, "isExtraAnim") ?? false);
                if (!isExtra) continue;

                var animList = ReadMemberValue(animInfo, "animList") as HSceneProc.AnimationListInfo;
                if (animList == null) continue;

                string plugName = Convert.ToString(ReadMemberValue(animInfo, "plugName")) ?? "";
                string asset = Convert.ToString(ReadMemberValue(animInfo, "asset")) ?? "";
                bool hasAnal = Convert.ToBoolean(ReadMemberValue(animInfo, "hasAnal") ?? false);
                string displayName = "";
                if (poseNames != null && plugName.Length > 0)
                {
                    try
                    {
                        if (poseNames.Contains(plugName))
                            displayName = Convert.ToString(poseNames[plugName]) ?? "";
                    }
                    catch { }
                }
                displayName = displayName.Trim().TrimEnd('*').Trim();
                if (displayName.Length == 0) displayName = plugName;

                string resId = ResourceIdentity(animList);
                ksPlugExtraAnimsByResource[resId] = new KsPlugExtraAnimMeta
                {
                    AnimInfo = animInfo,
                    ListInfo = animList,
                    PlugName = plugName,
                    DisplayName = displayName,
                    Asset = asset,
                    HasAnal = hasAnal
                };
                cachedCount++;

                if (appendToActions && seen != null)
                {
                    bool isAvail = true;
                    try
                    {
                        var availObj = InvokeStatic(ksPlugHAnimsType, "IsAvailableForSel", animInfo);
                        if (availObj is bool) isAvail = (bool)availObj;
                    }
                    catch { }

                    if (isAvail && seen.Add(resId))
                        actions.Add(animList);
                }
            }

            if (appendToActions)
                ksPlugCataloguePopulated = cachedCount > 0;
        }

        private void EnsureKsPlugExtraCachePopulated()
        {
            if (!IsKsPlugBackend || ksPlugExtraAnimsByResource.Count > 0) return;
            PopulateKsPlugExtraCache(null, false);
        }

        internal ActionDefinition ResolveActionDefinition(HSceneProc.AnimationListInfo info, bool anal)
        {
            if (info == null)
                return new ActionDefinition { Key = "unknown", Name = "", Effector = ActionSource.Unsupported, Reason = "无动画信息" };

            string resId = ResourceIdentity(info);
            var def = Catalogue.Resolve(info.mode.ToString(), info.id, info.nameAnimation, anal, resId);

            if (IsKsPlugBackend)
            {
                EnsureKsPlugExtraCachePopulated();
                KsPlugExtraAnimMeta meta;
                if (ksPlugExtraAnimsByResource.TryGetValue(resId, out meta) &&
                    !def.Key.StartsWith("kplug_", StringComparison.OrdinalIgnoreCase))
                {
                    string safePlug = !string.IsNullOrEmpty(meta.PlugName)
                        ? meta.PlugName
                        : (info.paramFemale != null && info.paramFemale.path != null && !string.IsNullOrEmpty(info.paramFemale.path.file)
                            ? info.paramFemale.path.file
                            : (info.mode + "_extra_" + info.id));
                    string label = !string.IsNullOrEmpty(meta.DisplayName) ? meta.DisplayName : safePlug;

                    def.AnalAllowed = def.AnalAllowed || meta.HasAnal;
                    RefineKsPlugExtraContact(info.mode.ToString(), safePlug, label, def);

                    bool isAnalFinal = anal && def.Contact == NativeTarget.VAGINAL && def.AnalAllowed;
                    if (isAnalFinal) def.Contact = NativeTarget.ANAL;

                    bool analKey = anal && def.Contact == NativeTarget.ANAL;
                    def.Key = "kplug_" + safePlug + (analKey ? "_anal" : "");
                    def.Name = "ksPlug · " + label;
                    def.Reason = "ksPlug 扩展动作自动分类";
                }
            }
            return def;
        }

        private static void RefineKsPlugExtraContact(string mode, string plugName, string displayName, ActionDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(mode)) return;
            if (!mode.StartsWith("houshi", StringComparison.OrdinalIgnoreCase)) return;

            string hint = ((plugName ?? "") + " " + (displayName ?? "")).ToLowerInvariant();
            if (hint.Contains("paizuri") || hint.Contains("boob") || hint.Contains("titjob"))
                def.Contact = NativeTarget.BREASTS;
            else if (hint.Contains("footjob") || hint.Contains("foot"))
                def.Contact = NativeTarget.BOTH_FEET;
            else if (hint.Contains("thighjob") || hint.Contains("sumata") || hint.Contains("intercrural"))
                def.Contact = NativeTarget.INTERCRURAL;
            else if (hint.Contains("fella") || hint.Contains("blowjob") || hint.Contains("_bj_") || hint.Contains("mouth") ||
                     hint.Contains("lick") || hint.Contains("rimjob") || hint.Contains("kiss") || hint.Contains("throat"))
                def.Contact = NativeTarget.ORAL;
            else if (hint.Contains("handjob") || hint.Contains("hj") || hint.Contains("knob"))
                def.Contact = NativeTarget.RIGHTHAND;
        }

        internal void SelectKPlugExtraAnimation(HSceneProc.AnimationListInfo info)
        {
            if (info == null) throw new ArgumentNullException("info");
            var backend = EnsurePlugBackend();
            if (backend == PlugBackend.KsPlug)
            {
                SelectKsPlugExtraAnimation(info);
                return;
            }

            var translator = RuntimeAccess.Find("kPlug.DefAndParse.kTranslator");
            var selectorType = RuntimeAccess.Find("kPlug.CmpH.AnimSelector");
            if (translator == null || selectorType == null) throw new InvalidOperationException("kPlug 扩展选择器不可用");
            var names = translator.GetMethod("AnimNameMapper_H", DirectorFlags).Invoke(null, null) as IDictionary;
            var selector = FindObjectOfType(selectorType);
            string name = names != null ? Convert.ToString(names[info.paramFemale.path.file]) : null;
            if (selector == null || string.IsNullOrEmpty(name)) throw new InvalidOperationException("kPlug 动作映射不可用");
            var field = selectorType.GetField("targetAnim", DirectorFlags);
            if (field != null) field.SetValue(selector, name);
            var coro = selectorType.GetMethod("IE_SetSelectedAnim", DirectorFlags).Invoke(selector, new object[] { name }) as IEnumerator;
            if (coro != null) StartCoroutine(coro);
        }

        private void SelectKsPlugExtraAnimation(HSceneProc.AnimationListInfo info)
        {
            EnsureKsPlugExtraCachePopulated();
            string resId = ResourceIdentity(info);
            object targetAnimInfo = null;

            KsPlugExtraAnimMeta meta;
            if (ksPlugExtraAnimsByResource.TryGetValue(resId, out meta) && meta != null)
                targetAnimInfo = meta.AnimInfo;

            if (targetAnimInfo == null)
            {
                object hCtrl;
                if (IsKsPlugHCtrlReady(out hCtrl))
                {
                    var list = ReadMemberValue(hCtrl, "HAnimInfos") as IEnumerable;
                    if (list != null)
                    {
                        foreach (var item in list)
                        {
                            var li = ReadMemberValue(item, "animList") as HSceneProc.AnimationListInfo;
                            if (li != null && (ReferenceEquals(li, info) || string.Equals(ResourceIdentity(li), resId, StringComparison.Ordinal)))
                            {
                                targetAnimInfo = item;
                                break;
                            }
                        }
                    }
                }
            }

            var menuPose = GetKsPlugHMenu("posSel");
            if (menuPose != null && targetAnimInfo != null)
            {
                try
                {
                    // relaunch=false so MenuPose.IE_SetAnim does not race with KoiSync's IE_AutoInsertAndStart.
                    var okObj = InvokeMember(menuPose, "SetSelectedAnim", targetAnimInfo, false);
                    if (okObj is bool && (bool)okObj)
                        return;
                }
                catch { }
            }

            // Fallback when MenuPose.CanChangePos() blocks (e.g. during OLoop) or posSel isn't ready:
            // HCtrl.DefineCurAnim() still detects HFlag.nowAnimationInfo change and applies IKS.SetExtraAnimIKS().
            SelectAction(info);
            if (Scene != null)
            {
                var chg = typeof(HSceneProc).GetMethod("ChangeAnimator", DirectorFlags);
                if (chg != null)
                {
                    chg.Invoke(Scene, new object[] { info, false });
                    if (Flags != null) Flags.selectAnimationListInfo = null;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Main Females & Harem Guests (Backend-Agnostic)
        // ─────────────────────────────────────────────────────────────────────────────

        private List<ChaControl> GetMainFemales()
        {
            var list = new List<ChaControl>();
            if (Scene == null) return list;
            var raw = RuntimeAccess.Read(Scene, "lstFemale") as IEnumerable;
            if (raw == null) return list;
            foreach (var item in raw)
            {
                var cha = item as ChaControl;
                if (cha != null) list.Add(cha);
            }
            return list;
        }

        private List<object> GetKPlugHaremGuests()
        {
            var list = new List<object>();
            var backend = EnsurePlugBackend();
            if (backend == PlugBackend.KsPlug)
            {
                var dealer = GetKsPlugDealer();
                if (dealer != null)
                {
                    var movers = ReadMemberValue(dealer, "ChaMovers") as IEnumerable;
                    if (movers != null)
                    {
                        foreach (var mover in movers)
                        {
                            if (!IsUnityAlive(mover)) continue;
                            var ai = ReadMemberValue(mover, "Ai");
                            if (IsUnityAlive(ai) && !list.Contains(ai)) list.Add(ai);
                        }
                    }
                    if (list.Count == 0 && ksPlugAIGuestType != null && Convert.ToBoolean(ReadMemberValue(dealer, "InH") ?? false))
                    {
                        var found = FindObjectsOfType(ksPlugAIGuestType);
                        for (int i = 0; i < found.Length; i++)
                        {
                            var ai = found[i];
                            if (IsUnityAlive(ai) && Convert.ToBoolean(ReadMemberValue(ai, "InH") ?? false) && !list.Contains(ai))
                                list.Add(ai);
                        }
                    }
                }
                return list;
            }

            if (backend == PlugBackend.KPlug)
            {
                var dealer = GetKPlugStatic("dealer");
                if (dealer == null) return list;
                var raw = RuntimeAccess.Read(dealer, "haremAI") as IEnumerable;
                if (raw == null) return list;
                foreach (var ai in raw) if (ai != null) list.Add(ai);
            }
            return list;
        }

        private bool IsGuestMale(object guestAi)
        {
            if (guestAi == null || !IsKsPlugBackend) return false;
            var isBoy = ReadMemberValue(guestAi, "IsBoy");
            if (isBoy is bool) return (bool)isBoy;
            var gt = ReadMemberValue(guestAi, "GuestTyp");
            return gt != null && Convert.ToInt32(gt) == 1;
        }

        private string GetGuestDisplayName(object guestAi, string fallback)
        {
            if (guestAi == null) return fallback ?? "";
            if (IsKsPlugBackend)
            {
                var cha = ReadMemberValue(guestAi, "Cha") as ChaControl;
                string nm = GetChaName(cha);
                if (nm.Length > 0) return nm;
                string aiName = Convert.ToString(ReadMemberValue(guestAi, "Name")) ?? "";
                return aiName.Length > 0 ? aiName.Trim() : (fallback ?? "");
            }
            string kName = Convert.ToString(RuntimeAccess.Read(guestAi, "CharaName")) ?? "";
            if (kName.Length > 0) return kName.Trim();
            var kCha = RuntimeAccess.Read(guestAi, "Cha") as ChaControl;
            string kChaName = GetChaName(kCha);
            return kChaName.Length > 0 ? kChaName : (fallback ?? "");
        }

        private HaremGuestSummary BuildGuestSummary(object ai, int fallbackIndex)
        {
            if (IsKsPlugBackend)
            {
                string gName = GetGuestDisplayName(ai, "");
                var chaCore = ReadMemberValue(ai, "ChaCore");
                int id = IsUnityAlive(chaCore) ? Convert.ToInt32(ReadMemberValue(chaCore, "CreationId") ?? fallbackIndex) : fallbackIndex;
                var gAct = ReadMemberValue(ai, "gAction");
                bool hasAct = IsUnityAlive(gAct);
                int actTypInt = hasAct ? Convert.ToInt32(ReadMemberValue(gAct, "Typ") ?? 0) : 0;
                string actTypName = hasAct ? (Convert.ToString(ReadMemberValue(gAct, "Typ")) ?? "") : "";
                bool mast = hasAct && actTypInt == 2; // GuestAction.Type.Mast == 2
                bool inOrg = false;
                if (hasAct)
                {
                    try
                    {
                        var orgObj = InvokeMember(gAct, "IsInOrgasmLoop");
                        if (orgObj is bool) inOrg = (bool)orgObj;
                    }
                    catch { }
                }
                return new HaremGuestSummary
                {
                    DealerId = id,
                    Name = gName,
                    Masturbating = mast,
                    InOrgasm = inOrg,
                    IsMale = IsGuestMale(ai),
                    ActiveAction = actTypName
                };
            }

            string kName = Convert.ToString(RuntimeAccess.Read(ai, "CharaName")) ?? "";
            var kCha = RuntimeAccess.Read(ai, "Cha") as ChaControl;
            if (kName.Length == 0) kName = GetChaName(kCha);
            bool kMast = RuntimeAccess.Read(ai, "onaCtrl") != null;
            return new HaremGuestSummary
            {
                DealerId = Convert.ToInt32(RuntimeAccess.Read(ai, "dealerId") ?? fallbackIndex),
                Name = kName,
                Masturbating = kMast,
                InOrgasm = Convert.ToBoolean(RuntimeAccess.Read(ai, "inOrgasm") ?? false),
                IsMale = false,
                ActiveAction = kMast ? "Mast" : ""
            };
        }

        internal List<HaremGuestSummary> GetKPlugGuestSnapshots()
        {
            var raw = GetKPlugHaremGuests();
            var snaps = new List<HaremGuestSummary>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
                snaps.Add(BuildGuestSummary(raw[i], i));
            return snaps;
        }

        internal string GetPrimaryFemaleName()
        {
            var fems = GetMainFemales();
            if (fems.Count == 0 || fems[0] == null || fems[0].fileParam == null) return "";
            return (fems[0].fileParam.fullname ?? "").Trim();
        }

        internal int GetPrimaryFemaleOutfitSlot()
        {
            var fems = GetMainFemales();
            if (fems.Count == 0 || fems[0] == null || fems[0].fileStatus == null) return 0;
            return fems[0].fileStatus.coordinateType;
        }

        internal int GetKPlugHaremCount()
        {
            return GetKPlugHaremGuests().Count;
        }

        private static string GetChaName(ChaControl cha)
        {
            if (cha == null || cha.fileParam == null) return "";
            return (cha.fileParam.fullname ?? "").Trim();
        }

        private static string GetChaFileName(ChaFileControl cf)
        {
            if (cf == null || cf.parameter == null) return "";
            return (cf.parameter.fullname ?? "").Trim();
        }

        private static bool NameMatches(string candidate, string query)
        {
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(query)) return false;
            string c = candidate.Replace(" ", "").Trim();
            string q = query.Replace(" ", "").Trim();
            if (c.Length == 0 || q.Length == 0) return false;
            return c.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   q.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private ChaControl ResolveTargetCharacter(int targetIndex, string targetName, out int mainFemaleIndex, out object matchedGuestAi)
        {
            mainFemaleIndex = -1;
            matchedGuestAi = null;
            var fems = GetMainFemales();
            var guests = GetKPlugHaremGuests();
            bool isKs = IsKsPlugBackend;

            if (!string.IsNullOrEmpty(targetName))
            {
                for (int i = 0; i < fems.Count; i++)
                {
                    if (NameMatches(GetChaName(fems[i]), targetName))
                    {
                        mainFemaleIndex = i;
                        return fems[i];
                    }
                }
                for (int i = 0; i < guests.Count; i++)
                {
                    var ai = guests[i];
                    string gName = Convert.ToString(isKs ? ReadMemberValue(ai, "Name") : RuntimeAccess.Read(ai, "CharaName")) ?? "";
                    var gCha = (isKs ? ReadMemberValue(ai, "Cha") : RuntimeAccess.Read(ai, "Cha")) as ChaControl;
                    if (NameMatches(gName, targetName) || NameMatches(GetChaName(gCha), targetName))
                    {
                        matchedGuestAi = ai;
                        return gCha;
                    }
                }
            }

            if (targetIndex >= 0 && targetIndex < fems.Count)
            {
                mainFemaleIndex = targetIndex;
                return fems[targetIndex];
            }
            if (fems.Count > 0)
            {
                mainFemaleIndex = 0;
                return fems[0];
            }
            return null;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Clothes & Outfit Controls
        // ─────────────────────────────────────────────────────────────────────────────

        internal static int[] ResolveClothParts(int partIndex, string partName, int[] explicitParts)
        {
            var list = new List<int>();
            if (explicitParts != null && explicitParts.Length > 0)
            {
                for (int i = 0; i < explicitParts.Length; i++)
                {
                    int p = Mathf.Clamp(explicitParts[i], 0, 8);
                    if (!list.Contains(p)) list.Add(p);
                }
                return list.ToArray();
            }
            if (partIndex >= 0)
            {
                list.Add(Mathf.Clamp(partIndex, 0, 8));
                return list.ToArray();
            }
            string s = (partName ?? "").Trim().ToLowerInvariant();
            if (s.Length == 0 || s == "all" || s == "全部" || s == "全身") return new int[0];

            // Convenience multi-part groups
            if (s.Contains("外衣") || s.Contains("外套套") || s == "outer") return new[] { 0, 1 };
            if (s.Contains("内衣裤") || s.Contains("贴身") || s == "underwear") return new[] { 2, 3 };
            if (s.Contains("上半身") || s == "upper") return new[] { 0, 2 };
            if (s.Contains("下半身") || s.Contains("下装") || s == "lower") return new[] { 1, 3, 5 };

            // Support compound strings like "上衣和胸罩" / "裙子+内裤"
            if (s.Contains("上衣") || s.Contains("外套") || s.Contains("衬衫") || s.Contains("衣服") || s.Contains("连衣裙") || s.Contains("裙装") || s == "top" || s.Contains("shirt") || s.Contains("jacket") || s.Contains("dress"))
                list.Add(0);
            if (s.Contains("下衣") || s.Contains("裙子") || s.Contains("短裙") || s.Contains("百褶裙") || s.Contains("裤子") || s == "bot" || s.Contains("bottom") || s.Contains("skirt") || s.Contains("pants"))
                list.Add(1);
            if (s.Contains("胸罩") || s.Contains("文胸") || s.Contains("奶罩") || s.Contains("胸衣") || s.Contains("内衣") || s == "bra")
                list.Add(2);
            if (s.Contains("内裤") || s.Contains("胖次") || s.Contains("底裤") || s.Contains("小裤裤") || s == "shorts" || s.Contains("panties") || s.Contains("panty"))
                list.Add(3);
            if (s.Contains("手套") || s.Contains("glove"))
                list.Add(4);
            if (s.Contains("连裤袜") || s.Contains("裤袜") || s.Contains("丝袜") || s.Contains("黑丝") || s.Contains("白丝") || s.Contains("渔网") || s.Contains("panst") || s.Contains("pantyhose") || s.Contains("tights") || s.Contains("stocking"))
                list.Add(5);
            if ((s.Contains("袜子") || s.Contains("长筒袜") || s.Contains("短袜") || s.Contains("过膝袜") || s.Contains("sock")) && !list.Contains(5))
                list.Add(6);
            if (s.Contains("鞋") || s.Contains("shoe") || s.Contains("boot"))
            {
                list.Add(7);
                list.Add(8);
            }

            int parsedNum;
            if (list.Count == 0 && int.TryParse(s, out parsedNum) && parsedNum >= 0 && parsedNum <= 8)
                list.Add(parsedNum);

            return list.ToArray();
        }

        private static string FormatClothStateZh(byte st)
        {
            if (st == 0) return "穿";
            if (st == 1 || st == 2) return "半脱";
            return "脱";
        }

        private static bool ApplySingleClothState(ChaControl cha, int p, string m, bool singleExplicit, out string appliedTag)
        {
            appliedTag = "";
            byte before = (cha.fileStatus != null && cha.fileStatus.clothesState != null && p < cha.fileStatus.clothesState.Length)
                ? cha.fileStatus.clothesState[p] : (byte)0;

            if (m == "dress" || m == "dress_part" || m == "on" || m == "wear" || m == "0")
            {
                cha.SetClothesState(p, 0, true);
                appliedTag = "穿上";
            }
            else if (m == "half2" || m == "pull_down" || m == "2")
            {
                if (cha.IsClothesStateType(p, 2)) { cha.SetClothesState(p, 2, true); appliedTag = "半脱(褪下)"; }
                else if (cha.IsClothesStateType(p, 1)) { cha.SetClothesState(p, 1, true); appliedTag = "半脱"; }
                else if (singleExplicit) { cha.SetClothesState(p, 3, true); appliedTag = "脱下(无半脱态)"; }
                else return false;
            }
            else if (m == "half" || m == "half_part" || m == "half1" || m == "1")
            {
                if (cha.IsClothesStateType(p, 1)) { cha.SetClothesState(p, 1, true); appliedTag = "半脱"; }
                else if (cha.IsClothesStateType(p, 2)) { cha.SetClothesState(p, 2, true); appliedTag = "半脱"; }
                else if (singleExplicit) { cha.SetClothesState(p, 3, true); appliedTag = "脱下(无半脱态)"; }
                else return false;
            }
            else if (m == "toggle" || m == "toggle_part" || m == "next")
            {
                cha.SetClothesStateNext(p);
                byte after = (cha.fileStatus != null && cha.fileStatus.clothesState != null && p < cha.fileStatus.clothesState.Length)
                    ? cha.fileStatus.clothesState[p] : (byte)0;
                appliedTag = FormatClothStateZh(after);
            }
            else
            {
                // Full undress in Koikatsu is state = 3 (NOT 2!)
                cha.SetClothesState(p, 3, true);
                appliedTag = "脱掉";
            }

            byte nowSt = (cha.fileStatus != null && cha.fileStatus.clothesState != null && p < cha.fileStatus.clothesState.Length)
                ? cha.fileStatus.clothesState[p] : before;
            return nowSt != before;
        }

        private static string LocalizeOutfitSlot(int slot)
        {
            switch (slot)
            {
                case 0: return L("校服(第一套)", "School (1)");
                case 1: return L("第二套校服", "School (2)");
                case 2: return L("体操服", "Gym");
                case 3: return L("泳装", "Swimsuit");
                case 4: return L("社团服", "Club");
                case 5: return L("私服", "Casual");
                case 6: return L("睡衣", "Sleepwear");
                default: return string.Format(L("套装 {0}", "Outfit {0}"), slot + 1);
            }
        }

        private static string LocalizeClothPart(int p)
        {
            switch (p)
            {
                case 0: return L("上衣", "Top");
                case 1: return L("下衣", "Bottom");
                case 2: return L("胸罩", "Bra");
                case 3: return L("内裤", "Panties");
                case 4: return L("手套", "Gloves");
                case 5: return L("连裤袜", "Pantyhose");
                case 6: return L("袜子", "Socks");
                case 7: return L("室内鞋", "Indoor shoes");
                case 8: return L("室外鞋", "Outdoor shoes");
                default: return string.Format(L("部位 {0}", "Part {0}"), p);
            }
        }

        private static string LocalizeClothTag(string zhTag)
        {
            switch (zhTag)
            {
                case "穿上": return L("穿上", "on");
                case "半脱": return L("半脱", "half-off");
                case "半脱(褪下)": return L("半脱(褪下)", "pulled down");
                case "脱下(无半脱态)": return L("脱下(无半脱态)", "removed");
                case "脱掉": return L("脱掉", "off");
                case "穿": return L("穿", "worn");
                case "脱": return L("脱", "off");
                default: return zhTag;
            }
        }

        internal CharacterOpResult DirectorSetClothes(int targetIndex, string targetName, string mode, int partIndex, string partName = null, int[] explicitParts = null)
        {
            if (Scene == null || Flags == null) throw new InvalidOperationException("当前不在本体 H 场景中");
            int mainIdx;
            object guestAi;
            var cha = ResolveTargetCharacter(targetIndex, targetName, out mainIdx, out guestAi);
            if (cha == null) throw new InvalidOperationException("未找到目标角色");

            string m = (mode ?? "undress").Trim().ToLowerInvariant();
            int[] parts = ResolveClothParts(partIndex, partName, explicitParts);
            string detail;
            string uiDetail;

            // IMPORTANT: NEVER call HSprite.OnClickAllCloth() — its IL checks Input.GetMouseButtonUp(0) and silently returns!
            // Always invoke ChaControl.SetClothesState / SetClothesStateAll directly with Koikatsu's real 4 states (0=穿, 1/2=半脱, 3=全脱).
            if (parts.Length > 0)
            {
                var descBits = new List<string>();
                var uiBits = new List<string>();
                for (int i = 0; i < parts.Length; i++)
                {
                    int p = parts[i];
                    string tag;
                    ApplySingleClothState(cha, p, m, true, out tag);
                    string pLabel = p >= 0 && p < ClothPartNames.Length ? ClothPartNames[p] : ("部位" + p);
                    descBits.Add(pLabel + "(" + tag + ")");
                    uiBits.Add(LocalizeClothPart(p) + "(" + LocalizeClothTag(tag) + ")");
                }
                detail = string.Join("、", descBits.ToArray());
                uiDetail = string.Join(UiText.Language == "zh" || UiText.Language == "zh-Hant" || UiText.Language == "ja" ? "、" : ", ", uiBits.ToArray());
            }
            else if (m == "dress" || m == "on" || m == "wear" || m == "0")
            {
                cha.SetClothesStateAll(0);
                detail = "全穿";
                uiDetail = L("全穿", "All dressed");
            }
            else if (m == "half" || m == "half1" || m == "half2" || m == "1" || m == "2")
            {
                // Whole-body half-undress: set every piece that supports half-state (1 or 2) to half-off while keeping socks/shoes intact!
                int changedCount = 0;
                int[] coreOrder = { 0, 1, 2, 3, 5 };
                for (int i = 0; i < coreOrder.Length; i++)
                {
                    int p = coreOrder[i];
                    if (!cha.IsClothes(p)) continue;
                    string tag;
                    if (ApplySingleClothState(cha, p, m, false, out tag)) changedCount++;
                }
                // Fallback if a custom modded outfit has zero half-state meshes on core pieces: remove outer top/bot while keeping underwear/socks
                if (changedCount == 0)
                {
                    if (cha.IsClothes(0)) cha.SetClothesState(0, 3, true);
                    if (cha.IsClothes(1)) cha.SetClothesState(1, 3, true);
                }
                detail = m == "half2" ? "全员半脱(褪下)" : "全员半脱";
                uiDetail = m == "half2" ? L("全员半脱(褪下)", "Half-undressed (pulled down)") : L("全员半脱", "Half-undressed");
            }
            else if (m == "toggle")
            {
                bool anyWorn = false;
                byte[] states = cha.fileStatus != null ? cha.fileStatus.clothesState : null;
                for (int p = 0; p < 4 && states != null && p < states.Length; p++)
                    if (cha.IsClothes(p) && states[p] < 3) { anyWorn = true; break; }
                cha.SetClothesStateAll(anyWorn ? (byte)3 : (byte)0);
                detail = anyWorn ? "全脱" : "全穿";
                uiDetail = anyWorn ? L("全脱", "All undressed") : L("全穿", "All dressed");
            }
            else
            {
                // Full undress = state 3!
                cha.SetClothesStateAll(3);
                detail = "全脱";
                uiDetail = L("全脱", "All undressed");
            }

            try { cha.UpdateClothesStateAll(); } catch { }
            try
            {
                var sprite = RuntimeAccess.Read(Scene, "sprite") as HSprite;
                if (mainIdx >= 0 && sprite != null) sprite.OnClothChange(mainIdx);
            }
            catch { }

            string who = GetChaName(cha);
            uiNotice = string.Format(L("AI 服装调节：{0} -> {1}", "AI clothes: {0} -> {1}"), who, uiDetail);
            EventBus.Emit(this, "ClothesChanged", who + " 服装调节: " + detail);
            return new CharacterOpResult { Action = "clothes", Character = who, Detail = detail };
        }

        internal CharacterOpResult DirectorSwitchOutfit(int targetIndex, string targetName, int slot)
        {
            if (Scene == null || Flags == null) throw new InvalidOperationException("当前不在本体 H 场景中");
            int mainIdx;
            object guestAi;
            var cha = ResolveTargetCharacter(targetIndex, targetName, out mainIdx, out guestAi);
            if (cha == null) throw new InvalidOperationException("未找到目标角色");

            int clamped = Mathf.Clamp(slot, 0, 6);
            // IMPORTANT: NEVER call HSprite.OnClickCoordinateChange() — its IL checks Input.GetMouseButtonUp(0) and silently returns!
            if (mainIdx >= 0)
            {
                cha.ChangeCoordinateTypeAndReload((ChaFileDefine.CoordinateType)clamped, true);
            }
            else if (IsKsPlugBackend && ksPlugClothesType != null)
            {
                try { InvokeStatic(ksPlugClothesType, "SetCoordinate", cha, clamped); }
                catch { cha.ChangeCoordinateTypeAndReload((ChaFileDefine.CoordinateType)clamped, true); }
            }
            else
            {
                var menuCharaType = RuntimeAccess.Find("kPlug.Menu.MenuChara");
                if (menuCharaType != null)
                {
                    try { InvokeStatic(menuCharaType, "SetCoordinate", cha, clamped); }
                    catch { cha.ChangeCoordinateTypeAndReload((ChaFileDefine.CoordinateType)clamped, true); }
                }
                else
                {
                    cha.ChangeCoordinateTypeAndReload((ChaFileDefine.CoordinateType)clamped, true);
                }
            }

            string slotName = clamped >= 0 && clamped < OutfitSlotNames.Length ? OutfitSlotNames[clamped] : ("套装" + (clamped + 1));
            uiNotice = string.Format(L("AI 换装：{0} -> {1}", "AI outfit: {0} -> {1}"), GetChaName(cha), LocalizeOutfitSlot(clamped));
            return new CharacterOpResult { Action = "outfit", Character = GetChaName(cha), Detail = slotName, Slot = clamped };
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Character Catalog & Card Lookup
        // ─────────────────────────────────────────────────────────────────────────────

        private static string[] CollectFemaleCardPngs(bool includeDefaultData)
        {
            var result = new List<string>();
            var roots = includeDefaultData
                ? new[] { Path.Combine(Paths.GameRootPath, "UserData/chara/female"), Path.Combine(Paths.GameRootPath, "DefaultData/chara/female") }
                : new[] { Path.Combine(Paths.GameRootPath, "UserData/chara/female") };

            for (int r = 0; r < roots.Length; r++)
            {
                string dir = roots[r];
                if (!Directory.Exists(dir)) continue;
                try
                {
                    var all = Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories);
                    for (int i = 0; i < all.Length; i++) result.Add(all[i]);
                }
                catch
                {
                    try
                    {
                        var top = Directory.GetFiles(dir, "*.png");
                        for (int i = 0; i < top.Length; i++) result.Add(top[i]);
                    }
                    catch { }
                }
            }
            return result.ToArray();
        }

        internal CharacterCatalogSnapshot DirectorListCharacters(string query)
        {
            var snap = new CharacterCatalogSnapshot
            {
                InHScene = Scene != null && Flags != null,
                KPlugLoaded = IsKPlugLoaded,
            };

            var fems = GetMainFemales();
            for (int i = 0; i < fems.Count; i++)
            {
                var c = fems[i];
                int slot = c.fileStatus != null ? c.fileStatus.coordinateType : 0;
                var states = c.fileStatus != null ? c.fileStatus.clothesState : null;
                byte topSt = (states != null && states.Length > 0) ? states[0] : (byte)0;
                var pieceDetails = new List<string>();
                for (int p = 0; p < ClothPartNames.Length && states != null && p < states.Length; p++)
                {
                    if (!c.IsClothes(p)) continue;
                    pieceDetails.Add(ClothPartNames[p] + ":" + FormatClothStateZh(states[p]));
                }
                snap.MainFemales.Add(new MainFemaleSummary
                {
                    Index = i,
                    Name = GetChaName(c),
                    OutfitSlot = slot,
                    OutfitName = slot >= 0 && slot < OutfitSlotNames.Length ? OutfitSlotNames[slot] : ("套装" + (slot + 1)),
                    ClothesState = topSt == 0 ? "dressed" : (topSt == 3 ? "undressed" : "half"),
                    ClothesDetail = string.Join(",", pieceDetails.ToArray()),
                });
            }

            var guestSnaps = GetKPlugGuestSnapshots();
            for (int i = 0; i < guestSnaps.Count; i++)
                snap.HaremGuests.Add(guestSnaps[i]);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var backend = EnsurePlugBackend();

            if (backend == PlugBackend.KsPlug)
            {
                var inviteMenu = GetKsPlugHMenu("invite");
                snap.InvitePanelOpen = inviteMenu != null && Convert.ToBoolean(ReadMemberValue(inviteMenu, "ShowMenu") ?? false);
                if (inviteMenu != null)
                {
                    var girlFiles = ReadMemberValue(inviteMenu, "girlFiles") as IEnumerable;
                    if (girlFiles != null)
                    {
                        foreach (var fi in girlFiles)
                        {
                            if (fi == null) continue;
                            bool isCreated = Convert.ToBoolean(ReadMemberValue(fi, "isCreated") ?? false);
                            if (isCreated) continue;

                            var heroine = ReadMemberValue(fi, "Save") as SaveData.Heroine;
                            var cf = ReadMemberValue(fi, "File") as ChaFileControl;
                            string cardPath = Convert.ToString(ReadMemberValue(fi, "cardPath")) ?? "";

                            if (heroine != null)
                            {
                                string nm = (heroine.Name ?? "").Trim();
                                if (nm.Length == 0) nm = GetChaFileName(cf);
                                if (nm.Length == 0 || !seen.Add(nm)) continue;
                                if (!string.IsNullOrEmpty(query) && !NameMatches(nm, query)) continue;
                                snap.InvitableStory.Add(nm);
                                if (snap.InvitableStory.Count >= 40) continue;
                            }
                            else
                            {
                                string nm = GetChaFileName(cf);
                                if (nm.Length == 0 && cardPath.Length > 0) nm = Path.GetFileNameWithoutExtension(cardPath);
                                if (nm.Length == 0 || !seen.Add(nm)) continue;
                                if (!string.IsNullOrEmpty(query) && !NameMatches(nm, query)) continue;
                                if (snap.InvitableCards.Count < 55) snap.InvitableCards.Add(nm);
                            }
                        }
                    }
                }
            }
            else if (backend == PlugBackend.KPlug)
            {
                var inviteUi = GetKPlugStatic("inviteUi");
                snap.InvitePanelOpen = inviteUi != null && Convert.ToBoolean(RuntimeAccess.Read(inviteUi, "showMenu") ?? false);

                if (inviteUi != null)
                {
                    var sg = RuntimeAccess.Read(inviteUi, "storyGirls") as IEnumerable;
                    if (sg != null)
                    {
                        foreach (var h in sg)
                        {
                            var heroine = h as SaveData.Heroine;
                            if (heroine == null) continue;
                            string nm = heroine.Name ?? "";
                            if (nm.Length == 0 || !seen.Add(nm)) continue;
                            if (!string.IsNullOrEmpty(query) && !NameMatches(nm, query)) continue;
                            snap.InvitableStory.Add(nm);
                            if (snap.InvitableStory.Count >= 40) break;
                        }
                    }
                    var og = RuntimeAccess.Read(inviteUi, "otherGirls") as IEnumerable;
                    if (og != null)
                    {
                        foreach (var item in og)
                        {
                            var cf = item as ChaFileControl;
                            string nm = GetChaFileName(cf);
                            if (nm.Length == 0 || !seen.Add(nm)) continue;
                            if (!string.IsNullOrEmpty(query) && !NameMatches(nm, query)) continue;
                            snap.InvitableCards.Add(nm);
                            if (snap.InvitableCards.Count >= 55) break;
                        }
                    }
                }
            }

            if (snap.InvitableCards.Count == 0)
            {
                var files = CollectFemaleCardPngs(backend == PlugBackend.KsPlug);
                for (int i = 0; i < files.Length && snap.InvitableCards.Count < 45; i++)
                {
                    string fn = Path.GetFileNameWithoutExtension(files[i]);
                    if (!string.IsNullOrEmpty(query) && !NameMatches(fn, query)) continue;
                    if (seen.Add(fn)) snap.InvitableCards.Add(fn);
                }
            }
            return snap;
        }

        private string FindFemaleCardFilePath(string query, out ChaFileControl cachedCard)
        {
            cachedCard = null;
            var backend = EnsurePlugBackend();

            if (backend == PlugBackend.KsPlug)
            {
                var inviteMenu = GetKsPlugHMenu("invite");
                if (inviteMenu != null)
                {
                    var gf = ReadMemberValue(inviteMenu, "girlFiles") as IEnumerable;
                    if (gf != null)
                    {
                        foreach (var fi in gf)
                        {
                            if (fi == null) continue;
                            var cf = ReadMemberValue(fi, "File") as ChaFileControl;
                            string cp = Convert.ToString(ReadMemberValue(fi, "cardPath")) ?? "";
                            string nm = GetChaFileName(cf);
                            string fn = cf != null ? (cf.charaFileName ?? "") : "";
                            if (NameMatches(nm, query) ||
                                NameMatches(Path.GetFileNameWithoutExtension(cp), query) ||
                                NameMatches(Path.GetFileNameWithoutExtension(fn), query))
                            {
                                cachedCard = cf;
                                if (!string.IsNullOrEmpty(cp) && File.Exists(cp)) return cp;
                                if (!string.IsNullOrEmpty(fn))
                                {
                                    string full = Path.Combine(Path.Combine(Paths.GameRootPath, "UserData/chara/female"), fn);
                                    if (File.Exists(full)) return full;
                                }
                                return cp.Length > 0 ? cp : fn;
                            }
                        }
                    }
                }
            }
            else if (backend == PlugBackend.KPlug)
            {
                var inviteUi = GetKPlugStatic("inviteUi");
                if (inviteUi != null)
                {
                    var og = RuntimeAccess.Read(inviteUi, "otherGirls") as IEnumerable;
                    if (og != null)
                    {
                        foreach (var item in og)
                        {
                            var cf = item as ChaFileControl;
                            if (cf == null) continue;
                            string nm = GetChaFileName(cf);
                            string fn = cf.charaFileName ?? "";
                            if (NameMatches(nm, query) || NameMatches(Path.GetFileNameWithoutExtension(fn), query))
                            {
                                cachedCard = cf;
                                string full = Path.Combine(Path.Combine(Paths.GameRootPath, "UserData/chara/female"), fn);
                                if (File.Exists(full)) return full;
                                return fn;
                            }
                        }
                    }
                }
            }

            var files = CollectFemaleCardPngs(backend == PlugBackend.KsPlug);
            for (int i = 0; i < files.Length; i++)
            {
                if (NameMatches(Path.GetFileNameWithoutExtension(files[i]), query)) return files[i];
            }
            var probe = new ChaFileControl();
            for (int i = 0; i < files.Length && i < 120; i++)
            {
                try
                {
                    if (probe.LoadCharaFile(files[i], 1, true, true) && NameMatches(GetChaFileName(probe), query))
                        return files[i];
                }
                catch { }
            }
            return "";
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Character Commands (Invite, Swap, Focus, Replace Body, Guest/Main Behaviors)
        // ─────────────────────────────────────────────────────────────────────────────

        internal CharacterOpResult DirectorCharacterCommand(string action, string name, string style, string behavior)
        {
            if (Scene == null || Flags == null) throw new InvalidOperationException("当前不在本体 H 场景中");
            string act = (action ?? "").Trim().ToLowerInvariant();
            var backend = EnsurePlugBackend();

            if (act == "replace_body")
            {
                if (string.IsNullOrEmpty(name)) throw new InvalidOperationException("请提供要切换的目标角色名");
                var fems = GetMainFemales();
                if (fems.Count == 0 || fems[0] == null) throw new InvalidOperationException("当前没有主女角色");
                ChaFileControl cached;
                string cardPath = FindFemaleCardFilePath(name, out cached);
                if (string.IsNullOrEmpty(cardPath) || !File.Exists(cardPath))
                    throw new InvalidOperationException("未在 UserData/chara/female 找到人物卡：" + name);
                var fem = fems[0];
                int keepSlot = fem.fileStatus != null ? fem.fileStatus.coordinateType : 0;
                if (!fem.chaFile.LoadCharaFile(cardPath, 1, false, true))
                    throw new InvalidOperationException("读取人物卡失败：" + cardPath);
                fem.Reload(false, false, false, false);
                if (keepSlot >= 0 && keepSlot <= 6)
                {
                    try { fem.ChangeCoordinateTypeAndReload((ChaFileDefine.CoordinateType)keepSlot, true); } catch { }
                }
                string newName = GetChaName(fem);
                string finalName = newName.Length > 0 ? newName : Path.GetFileNameWithoutExtension(cardPath);
                uiNotice = string.Format(L("已原生切换主角身体为：{0}", "Swapped main character body natively to: {0}"), finalName);
                return new CharacterOpResult { Action = act, Character = finalName, Detail = "replaced" };
            }

            if (act == "focus")
            {
                int mainIdx;
                object guestAi;
                var cha = ResolveTargetCharacter(0, name, out mainIdx, out guestAi);
                if (cha == null) throw new InvalidOperationException("未找到要定位的角色：" + name);
                Transform bone = null;
                if (cha.objBodyBone != null)
                {
                    var all = cha.objBodyBone.GetComponentsInChildren<Transform>(true);
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i].name == "cf_j_spine03" || all[i].name == "cf_j_head")
                        {
                            bone = all[i];
                            if (all[i].name == "cf_j_spine03") break;
                        }
                    }
                }
                Vector3 targetPos = bone != null ? bone.position : (cha.transform.position + Vector3.up * 1.15f);
                if (Flags.ctrlCamera != null) Flags.ctrlCamera.TargetPos = targetPos;
                string nm = GetChaName(cha);
                uiNotice = string.Format(L("已将镜头定位到角色：{0}", "Focused camera on character: {0}"), nm);
                return new CharacterOpResult { Action = act, Character = nm, Detail = "focused" };
            }

            if (backend == PlugBackend.KsPlug)
                return DirectorCharacterCommandKsPlug(act, name, style, behavior);

            return DirectorCharacterCommandKPlug(act, name, style, behavior);
        }

        private CharacterOpResult DirectorCharacterCommandKPlug(string act, string name, string style, string behavior)
        {
            var dealer = GetKPlugStatic("dealer");
            var inviteUi = GetKPlugStatic("inviteUi");

            if (act == "open_invite_panel" || act == "close_invite_panel")
            {
                if (inviteUi == null) throw new InvalidOperationException("未检测到 kPlug 邀请面板 (kPlug.Core.inviteUi)");
                bool open = act == "open_invite_panel";
                SetMemberValue(inviteUi, "showMenu", open);
                uiNotice = open
                    ? string.Format(L("已打开 {0} 角色邀请面板", "Opened {0} character invite panel"), "kPlug")
                    : string.Format(L("已关闭 {0} 角色邀请面板", "Closed {0} character invite panel"), "kPlug");
                return new CharacterOpResult { Action = act, Detail = uiNotice, PanelOpen = open };
            }

            if (act == "invite")
            {
                if (dealer == null) throw new InvalidOperationException("未检测到 kPlug Dealer，无法邀请角色");
                if (string.IsNullOrEmpty(name))
                {
                    if (inviteUi != null) SetMemberValue(inviteUi, "showMenu", true);
                    uiNotice = string.Format(L("已打开 {0} 邀请面板供主人选择角色", "Opened {0} invite panel to choose a character"), "kPlug");
                    return new CharacterOpResult { Action = "open_invite_panel", Detail = uiNotice, PanelOpen = true };
                }
                if (inviteUi != null)
                {
                    var sg = RuntimeAccess.Read(inviteUi, "storyGirls") as IEnumerable;
                    if (sg != null)
                    {
                        foreach (var h in sg)
                        {
                            var heroine = h as SaveData.Heroine;
                            if (heroine != null && NameMatches(heroine.Name ?? "", name))
                            {
                                InvokeMember(dealer, "InviteSomeone", heroine.Name);
                                uiNotice = string.Format(L("{0} 正在邀请角色：{1}", "{0} inviting character: {1}"), "kPlug", heroine.Name);
                                return new CharacterOpResult { Action = act, Character = heroine.Name, Detail = "kPlug.story" };
                            }
                        }
                    }
                }
                ChaFileControl card;
                string cardPath = FindFemaleCardFilePath(name, out card);
                if (card == null && !string.IsNullOrEmpty(cardPath) && File.Exists(cardPath))
                {
                    card = new ChaFileControl();
                    if (!card.LoadCharaFile(cardPath, 1, false, true)) card = null;
                }
                if (card == null) throw new InvalidOperationException("在人物卡与剧情列表里未找到匹配角色：" + name);
                InvokeMember(dealer, "InviteCard", card);
                string cardName = GetChaFileName(card);
                string finalName = cardName.Length > 0 ? cardName : name;
                uiNotice = string.Format(L("{0} 正在加载邀请人物卡：{1}", "{0} loading invite card: {1}"), "kPlug", finalName);
                return new CharacterOpResult { Action = act, Character = finalName, Detail = "kPlug.card" };
            }

            if (act == "swap_main_guest")
            {
                if (dealer == null) throw new InvalidOperationException("未检测到 kPlug Dealer");
                int mainIdx;
                object guestAi;
                ResolveTargetCharacter(-1, name, out mainIdx, out guestAi);
                if (guestAi == null)
                {
                    var guests = GetKPlugHaremGuests();
                    if (guests.Count > 0) guestAi = guests[0];
                }
                if (guestAi == null) throw new InvalidOperationException("当前场景没有已邀请的 kPlug 角色可切换为主角");
                int dealerId = Convert.ToInt32(RuntimeAccess.Read(guestAi, "dealerId") ?? 0);
                string gName = Convert.ToString(RuntimeAccess.Read(guestAi, "CharaName")) ?? name;
                object okObj = InvokeMember(dealer, "AskForSwitch", dealerId, true);
                if (okObj == null || (okObj is bool && !(bool)okObj))
                    InvokeMember(guestAi, "SwapToMain", false);
                uiNotice = string.Format(L("{0} 正在切换主角为：{1}", "{0} swapping main character to: {1}"), "kPlug", gName);
                return new CharacterOpResult { Action = act, Character = gName, Detail = "swapped" };
            }

            if (act == "behavior")
            {
                string beh = (behavior ?? style ?? "masturbate").Trim().ToLowerInvariant();
                int mainIdx;
                object guestAi;
                var cha = ResolveTargetCharacter(-1, name, out mainIdx, out guestAi);

                if (beh == "striptease" || beh == "strip")
                {
                    object targetAi = guestAi ?? GetKPlugStatic("mainAi");
                    if (targetAi == null) throw new InvalidOperationException("未检测到 kPlug AI 控制器");
                    InvokeMember(targetAi, "DoStripTease", 0f, true, true, true);
                    string targetLabel = string.IsNullOrEmpty(name) ? GetPrimaryFemaleName() : name;
                    uiNotice = string.Format(L("{0} 角色开始脱衣舞：{1}", "{0} character started striptease: {1}"), "kPlug", targetLabel);
                    return new CharacterOpResult { Action = act, Character = targetLabel, Detail = "striptease" };
                }

                if (beh == "dance" || beh == "stop_dance")
                {
                    if (dealer == null) throw new InvalidOperationException("未检测到 kPlug Dealer");
                    if (cha == null) cha = ResolveTargetCharacter(0, "", out mainIdx, out guestAi);
                    if (cha == null) throw new InvalidOperationException("未找到目标角色");
                    InvokeMember(dealer, beh == "dance" ? "AskForDance" : "AskForStopDance", cha);
                    string danceName = GetChaName(cha);
                    uiNotice = beh == "dance"
                        ? string.Format(L("{0} 角色开始跳舞：{1}", "{0} character started dancing: {1}"), "kPlug", danceName)
                        : string.Format(L("已停止跳舞：{0}", "Stopped dancing: {0}"), danceName);
                    return new CharacterOpResult { Action = act, Character = danceName, Detail = beh };
                }

                if (beh == "masturbate" || beh == "stop_masturbate")
                {
                    if (guestAi == null)
                    {
                        var guests = GetKPlugHaremGuests();
                        if (guests.Count > 0) guestAi = guests[0];
                    }
                    if (guestAi != null)
                    {
                        string gName = Convert.ToString(RuntimeAccess.Read(guestAi, "CharaName")) ?? name;
                        if (beh == "stop_masturbate")
                        {
                            InvokeMember(guestAi, "ForceStopOnanism", true);
                            uiNotice = string.Format(L("已让角色停止自慰：{0}", "Stopped guest solo action: {0}"), gName);
                            return new CharacterOpResult { Action = act, Character = gName, Detail = "stop_masturbate" };
                        }
                        var onaEnum = RuntimeAccess.Find("kPlug.DefAndParse.Def+Onanism");
                        if (onaEnum == null) throw new InvalidOperationException("未找到 kPlug Onanism 枚举");
                        string stName = string.IsNullOrEmpty(style) ? "Lay" : style;
                        object onaVal;
                        try { onaVal = Enum.Parse(onaEnum, stName, true); }
                        catch { onaVal = Enum.Parse(onaEnum, "Lay", true); }
                        InvokeMember(guestAi, "LaunchOnanism", onaVal, true);
                        uiNotice = string.Format(L("{0} 已控制 {1} 开始自慰 ({2})", "{0} started solo action for {1} ({2})"), "kPlug", gName, onaVal);
                        return new CharacterOpResult { Action = act, Character = gName, Detail = "masturbate:" + onaVal };
                    }
                    var sprite = RuntimeAccess.Read(Scene, "sprite") as HSprite;
                    if (beh == "masturbate" && sprite != null && Flags.mode == HFlag.EMode.masturbation)
                    {
                        sprite.SetMasturbationStart();
                        uiNotice = L("已启动原生自慰模式", "Started native solo mode");
                        return new CharacterOpResult { Action = act, Character = GetPrimaryFemaleName(), Detail = "native_masturbate" };
                    }
                    throw new InvalidOperationException("当前场景没有 kPlug 在场副角色（可先用 kPlug 邀请角色入场后再控制其自慰）");
                }

                if (beh == "dismiss" || beh == "leave")
                {
                    if (dealer == null || guestAi == null) throw new InvalidOperationException("未找到要请离的 kPlug 副角色");
                    string gName = Convert.ToString(RuntimeAccess.Read(guestAi, "CharaName")) ?? name;
                    InvokeMember(dealer, "DeleteCharInH", guestAi);
                    uiNotice = string.Format(L("已请离 {0} 角色：{1}", "Dismissed {0} guest: {1}"), "kPlug", gName);
                    return new CharacterOpResult { Action = act, Character = gName, Detail = "dismissed" };
                }

                throw new ArgumentException("未知角色行为：" + beh);
            }

            throw new ArgumentException("未知角色控制动作：" + act);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // KKS ksPlug Character Commands & Coroutines
        // ─────────────────────────────────────────────────────────────────────────────

        private object PickFirstFemaleKsPlugGuest()
        {
            var guests = GetKPlugHaremGuests();
            for (int i = 0; i < guests.Count; i++)
            {
                if (!IsGuestMale(guests[i])) return guests[i];
            }
            return null;
        }

        private CharacterOpResult DirectorCharacterCommandKsPlug(string act, string name, string style, string behavior)
        {
            object hCtrl;
            bool hReady = IsKsPlugHCtrlReady(out hCtrl);
            var inviteMenu = hReady ? GetKsPlugHMenu("invite") : null;

            if (act == "open_invite_panel" || act == "close_invite_panel")
            {
                if (inviteMenu == null) throw new InvalidOperationException("ksPlug 邀请面板尚未就绪 (HCtrl.HMenus.invite)");
                bool open = act == "open_invite_panel";
                SetMemberValue(inviteMenu, "ShowMenu", open);
                uiNotice = open
                    ? string.Format(L("已打开 {0} 角色邀请面板", "Opened {0} character invite panel"), "ksPlug")
                    : string.Format(L("已关闭 {0} 角色邀请面板", "Closed {0} character invite panel"), "ksPlug");
                return new CharacterOpResult { Action = act, Detail = uiNotice, PanelOpen = open };
            }

            if (act == "invite")
            {
                if (inviteMenu == null) throw new InvalidOperationException("ksPlug 邀请控制器尚未就绪 (HCtrl.HMenus.invite)");
                if (string.IsNullOrEmpty(name))
                {
                    SetMemberValue(inviteMenu, "ShowMenu", true);
                    uiNotice = string.Format(L("已打开 {0} 邀请面板供主人选择角色", "Opened {0} invite panel to choose a character"), "ksPlug");
                    return new CharacterOpResult { Action = "open_invite_panel", Detail = uiNotice, PanelOpen = true };
                }

                var girlFiles = ReadMemberValue(inviteMenu, "girlFiles");
                var girlsTabInf = ReadMemberValue(inviteMenu, "girlsTabInf");
                var gfEnum = girlFiles as IEnumerable;

                if (gfEnum != null)
                {
                    foreach (var fi in gfEnum)
                    {
                        if (fi == null) continue;
                        var heroine = ReadMemberValue(fi, "Save") as SaveData.Heroine;
                        var cf = ReadMemberValue(fi, "File") as ChaFileControl;
                        string cp = Convert.ToString(ReadMemberValue(fi, "cardPath")) ?? "";
                        string hName = heroine != null ? (heroine.Name ?? "") : "";
                        string cName = GetChaFileName(cf);
                        string fName = cp.Length > 0 ? Path.GetFileNameWithoutExtension(cp) : "";

                        if (NameMatches(hName, name) || NameMatches(cName, name) || NameMatches(fName, name))
                        {
                            string matchedName = hName.Length > 0 ? hName : (cName.Length > 0 ? cName : fName);
                            if (Convert.ToBoolean(ReadMemberValue(fi, "isCreated") ?? false))
                            {
                                uiNotice = string.Format(L("{0} 角色已在当前场景中：{1}", "{0} character already in scene: {1}"), "ksPlug", matchedName);
                                return new CharacterOpResult { Action = act, Character = matchedName, Detail = "already_present" };
                            }
                            if (heroine != null)
                            {
                                InvokeStatic(ksPlugMenuInviteType, "InvFromOtherComonent", heroine);
                                uiNotice = string.Format(L("{0} 正在邀请剧情角色：{1}", "{0} inviting story character: {1}"), "ksPlug", matchedName);
                                return new CharacterOpResult { Action = act, Character = matchedName, Detail = "ksPlug.story" };
                            }
                            InvokeMember(inviteMenu, "InviteSomeone", fi, girlsTabInf, girlFiles);
                            uiNotice = string.Format(L("{0} 正在加载邀请人物卡：{1}", "{0} loading invite card: {1}"), "ksPlug", matchedName);
                            return new CharacterOpResult { Action = act, Character = matchedName, Detail = "ksPlug.card" };
                        }
                    }
                }

                // Fallback card scan (critical when ksPlug settings.txt has LimitLoadChaFile=True & ChaFileLimit=0!)
                ChaFileControl card;
                string cardPath = FindFemaleCardFilePath(name, out card);
                if (!string.IsNullOrEmpty(cardPath) && File.Exists(cardPath))
                {
                    // Always load with noLoadPNG=false so ChaFile.facePngData is populated for ChaCore.CreateFaceText!
                    var loaded = new ChaFileControl();
                    if (loaded.LoadCharaFile(cardPath, 1, false, true)) card = loaded;
                }
                if (card == null) throw new InvalidOperationException("在人物卡与剧情列表里未找到匹配角色：" + name);
                if (ksPlugChaListFileInfoType == null) throw new InvalidOperationException("未找到 ksPlug.Character.ChaList+FileInfo 类型");

                object syntheticFi = Activator.CreateInstance(ksPlugChaListFileInfoType);
                SetMemberValue(syntheticFi, "File", card);
                // CRITICAL: NEVER set filter="All" — MenuInvite.CheckFiltersAfterInvite removes any filter with 0 remaining items!
                SetMemberValue(syntheticFi, "filter", "");
                SetMemberValue(syntheticFi, "cardPath", cardPath ?? "");
                SetMemberValue(syntheticFi, "canBeInvited", true);
                SetMemberValue(syntheticFi, "canBeInteract", true);
                SetMemberValue(syntheticFi, "isCreated", false);

                InvokeMember(inviteMenu, "InviteSomeone", syntheticFi, girlsTabInf, girlFiles);
                string cardName = GetChaFileName(card);
                string finalName = cardName.Length > 0 ? cardName : name;
                uiNotice = string.Format(L("{0} 正在加载邀请人物卡：{1}", "{0} loading invite card: {1}"), "ksPlug", finalName);
                return new CharacterOpResult { Action = act, Character = finalName, Detail = "ksPlug.card" };
            }

            if (act == "swap_main_guest")
            {
                if (!hReady) throw new InvalidOperationException("ksPlug HCtrl 尚未就绪");
                int mainIdx;
                object guestAi;
                ResolveTargetCharacter(-1, name, out mainIdx, out guestAi);
                if (guestAi == null || IsGuestMale(guestAi))
                    guestAi = PickFirstFemaleKsPlugGuest();
                if (guestAi == null) throw new InvalidOperationException("当前场景没有已邀请的 ksPlug 女性副角色可切换为主角");

                string gName = GetGuestDisplayName(guestAi, name);
                object okObj = InvokeMember(guestAi, "AskForSwap");
                if (okObj is bool && (bool)okObj)
                {
                    uiNotice = string.Format(L("{0} 正在切换主角为：{1}", "{0} swapping main character to: {1}"), "ksPlug", gName);
                    return new CharacterOpResult { Action = act, Character = gName, Detail = "swapped" };
                }

                // Some guest actions (e.g. Strip/Service) block CanSwap() until broken.
                if (Convert.ToBoolean(ReadMemberValue(guestAi, "InAction") ?? false))
                {
                    InvokeMember(guestAi, "BreakAction");
                    StartCoroutine(IE_DeferredKsPlugSwap(guestAi));
                    uiNotice = string.Format(L("{0} 已中断副角色动作并准备切换主角为：{1}", "{0} interrupted guest action to swap main character to: {1}"), "ksPlug", gName);
                    return new CharacterOpResult { Action = act, Character = gName, Detail = "swapped_after_break" };
                }

                throw new InvalidOperationException("ksPlug 当前状态暂不支持切换主角（如高潮/插入过渡中或角色正忙）：" + gName);
            }

            if (act == "behavior")
            {
                string beh = (behavior ?? style ?? "masturbate").Trim().ToLowerInvariant();
                int mainIdx;
                object guestAi;
                var cha = ResolveTargetCharacter(-1, name, out mainIdx, out guestAi);

                if (beh == "striptease" || beh == "strip")
                {
                    if (guestAi != null)
                    {
                        string gName = GetGuestDisplayName(guestAi, name);
                        var gCha = ReadMemberValue(guestAi, "Cha") as ChaControl;
                        bool hasTop = gCha == null || ksPlugClothesType == null || Convert.ToBoolean(InvokeStatic(ksPlugClothesType, "HasTopCloth", gCha) ?? true);
                        bool hasBot = gCha == null || ksPlugClothesType == null || Convert.ToBoolean(InvokeStatic(ksPlugClothesType, "HasBotCloth", gCha) ?? true);
                        if (!hasTop && !hasBot)
                        {
                            uiNotice = string.Format(L("{0} 副角色已无衣物可脱：{1}", "{0} guest already undressed: {1}"), "ksPlug", gName);
                            return new CharacterOpResult { Action = act, Character = gName, Detail = "already_naked" };
                        }

                        object okObj = InvokeMember(guestAi, "AskForStrip", (object)null, hasTop, hasBot);
                        if (!(okObj is bool && (bool)okObj))
                        {
                            if (Convert.ToBoolean(ReadMemberValue(guestAi, "InAction") ?? false))
                            {
                                InvokeMember(guestAi, "BreakAction");
                                StartCoroutine(IE_DeferredKsPlugStrip(guestAi, hasTop, hasBot));
                            }
                            else
                            {
                                ForceKsPlugGuestStrip(guestAi, hasTop, hasBot);
                            }
                        }
                        uiNotice = string.Format(L("{0} 角色开始脱衣舞：{1}", "{0} character started striptease: {1}"), "ksPlug", gName);
                        return new CharacterOpResult { Action = act, Character = gName, Detail = "striptease" };
                    }

                    // ksPlug has no animated striptease on AIMain; fall back cleanly to instant undress.
                    var res = DirectorSetClothes(mainIdx >= 0 ? mainIdx : 0, name, "undress", -1);
                    uiNotice = string.Format(L("{0} 主角无脱衣舞动画，已直接脱衣：{1}", "{0} main character has no striptease; undressed directly: {1}"), "ksPlug", res.Character);
                    return new CharacterOpResult { Action = act, Character = res.Character, Detail = "instant_undress" };
                }

                if (beh == "dance" || beh == "stop_dance")
                {
                    if (guestAi != null)
                    {
                        string gName = GetGuestDisplayName(guestAi, name);
                        if (beh == "stop_dance")
                        {
                            InvokeMember(guestAi, "BreakAction");
                            uiNotice = string.Format(L("已停止跳舞：{0}", "Stopped dancing: {0}"), gName);
                            return new CharacterOpResult { Action = act, Character = gName, Detail = "stop_dance" };
                        }

                        object clipVal = ResolveKsPlugDanceClip(style);
                        if (clipVal == null) throw new InvalidOperationException("未找到 ksPlug.Character.Animations+Clip 枚举");
                        StartKsPlugGuestDance(guestAi, clipVal);
                        uiNotice = string.Format(L("{0} 副角色开始跳舞：{1} ({2})", "{0} guest started dancing: {1} ({2})"), "ksPlug", gName, clipVal);
                        return new CharacterOpResult { Action = act, Character = gName, Detail = "dance:" + clipVal };
                    }

                    if (!hReady) throw new InvalidOperationException("ksPlug HCtrl 尚未就绪");
                    var mainGirlCore = ReadMemberValue(hCtrl, "MainGirl");
                    if (!IsUnityAlive(mainGirlCore)) throw new InvalidOperationException("未找到 ksPlug 主女角色 (HCtrl.MainGirl)");
                    string mName = GetPrimaryFemaleName();

                    if (beh == "stop_dance")
                    {
                        InvokeStatic(ksPlugMainActionOverType, "DestroyOverride", mainGirlCore, true);
                        uiNotice = string.Format(L("已停止主角跳舞：{0}", "Stopped main character dancing: {0}"), mName);
                        return new CharacterOpResult { Action = act, Character = mName, Detail = "stop_dance" };
                    }

                    var curAnim = ReadMemberValue(hCtrl, "CurAnim");
                    int curCtx = curAnim != null ? Convert.ToInt32(ReadMemberValue(curAnim, "ctx") ?? 0) : 0;
                    int curSpot = Convert.ToInt32(ReadMemberValue(hCtrl, "HSpot") ?? 0);
                    if (curCtx != 1 || curSpot != 0)
                        throw new InvalidOperationException("ksPlug 主角跳舞仅支持在爱抚(Tease)默认点位下使用（副角色可在任意场景跳舞）");

                    object mainClip = ResolveKsPlugDanceClip(style);
                    if (mainClip == null) throw new InvalidOperationException("未找到 ksPlug.Character.Animations+Clip 枚举");
                    InvokeStatic(ksPlugMainActionOverType, "CreateOrRefresh", mainGirlCore, mainClip);
                    uiNotice = string.Format(L("{0} 主角开始跳舞：{1} ({2})", "{0} main character started dancing: {1} ({2})"), "ksPlug", mName, mainClip);
                    return new CharacterOpResult { Action = act, Character = mName, Detail = "dance:" + mainClip };
                }

                if (beh == "masturbate" || beh == "stop_masturbate")
                {
                    if (guestAi == null || IsGuestMale(guestAi))
                        guestAi = PickFirstFemaleKsPlugGuest();

                    if (guestAi != null)
                    {
                        string gName = GetGuestDisplayName(guestAi, name);
                        if (beh == "stop_masturbate")
                        {
                            InvokeMember(guestAi, "BreakAction");
                            uiNotice = string.Format(L("已让角色停止自慰：{0}", "Stopped guest solo action: {0}"), gName);
                            return new CharacterOpResult { Action = act, Character = gName, Detail = "stop_masturbate" };
                        }

                        if (ksPlugGuestActionTypeEnum == null || ksPlugAIGuestType == null)
                            throw new InvalidOperationException("ksPlug GuestAction 类型不可用");

                        object mastTyp = Enum.ToObject(ksPlugGuestActionTypeEnum, 2); // GuestAction.Type.Mast == 2
                        var coro = InvokeStatic(ksPlugAIGuestType, "IE_StartSoloAction", guestAi, mastTyp, true, 0f) as IEnumerator;
                        var guestMb = guestAi as MonoBehaviour;
                        if (guestMb == null || coro == null)
                            throw new InvalidOperationException("启动 ksPlug 副角色自慰协程失败");
                        guestMb.StartCoroutine(coro);

                        object clipOverride = ResolveKsPlugMastClip(style);
                        if (clipOverride != null)
                            StartCoroutine(IE_ApplyKsPlugMastClip(guestAi, clipOverride));

                        string tag = clipOverride != null ? clipOverride.ToString() : "Auto";
                        uiNotice = string.Format(L("{0} 已控制 {1} 开始自慰 ({2})", "{0} started solo action for {1} ({2})"), "ksPlug", gName, tag);
                        return new CharacterOpResult { Action = act, Character = gName, Detail = "masturbate:" + tag };
                    }

                    var sprite = RuntimeAccess.Read(Scene, "sprite") as HSprite;
                    if (beh == "masturbate" && sprite != null && Flags.mode == HFlag.EMode.masturbation)
                    {
                        sprite.SetMasturbationStart();
                        uiNotice = L("已启动原生自慰模式", "Started native solo mode");
                        return new CharacterOpResult { Action = act, Character = GetPrimaryFemaleName(), Detail = "native_masturbate" };
                    }
                    throw new InvalidOperationException("当前场景没有 ksPlug 在场女性副角色（可先用 ksPlug 邀请角色入场后再控制其自慰）");
                }

                if (beh == "dismiss" || beh == "leave")
                {
                    if (guestAi == null) throw new InvalidOperationException("未找到要请离的 ksPlug 副角色");
                    string gName = GetGuestDisplayName(guestAi, name);
                    // Always call AskToLeave() (NOT DeleteGuest) so group partners and binders detach cleanly!
                    InvokeMember(guestAi, "AskToLeave");
                    uiNotice = string.Format(L("已请离 {0} 角色：{1}", "Dismissed {0} guest: {1}"), "ksPlug", gName);
                    return new CharacterOpResult { Action = act, Character = gName, Detail = "dismissed" };
                }

                if (beh == "stop_action" || beh == "stop")
                {
                    if (guestAi != null)
                    {
                        string gName = GetGuestDisplayName(guestAi, name);
                        var gAct = ReadMemberValue(guestAi, "gAction");
                        int typInt = IsUnityAlive(gAct) ? Convert.ToInt32(ReadMemberValue(gAct, "Typ") ?? 0) : 0;
                        InvokeMember(guestAi, "BreakAction");
                        // GuestAction.Type.Tease(3) isn't destroyed by BreakAction; clean it up explicitly.
                        if (typInt == 3 && IsUnityAlive(gAct))
                        {
                            var uo = gAct as UnityEngine.Object;
                            if (uo != null) Destroy(uo);
                        }
                        uiNotice = string.Format(L("已停止 {0} 副角色动作：{1}", "Stopped {0} guest action: {1}"), "ksPlug", gName);
                        return new CharacterOpResult { Action = act, Character = gName, Detail = "stopped" };
                    }
                    if (hReady)
                    {
                        var mainGirlCore = ReadMemberValue(hCtrl, "MainGirl");
                        if (IsUnityAlive(mainGirlCore))
                            InvokeStatic(ksPlugMainActionOverType, "DestroyOverride", mainGirlCore, true);
                    }
                    string mName = GetPrimaryFemaleName();
                    uiNotice = string.Format(L("已还原主角动作：{0}", "Restored main character action: {0}"), mName);
                    return new CharacterOpResult { Action = act, Character = mName, Detail = "stopped" };
                }

                throw new ArgumentException("未知角色行为：" + beh);
            }

            throw new ArgumentException("未知角色控制动作：" + act);
        }

        private object ResolveKsPlugDanceClip(string style)
        {
            if (ksPlugAnimClipEnum == null) return null;
            string s = (style ?? "").Trim();
            if (s.Length > 0)
            {
                try { return Enum.Parse(ksPlugAnimClipEnum, s, true); }
                catch { }
                int num;
                if (int.TryParse(s, out num))
                {
                    num = Mathf.Clamp(num, 1, 11);
                    string dn = "Dance_" + num.ToString("00");
                    try { return Enum.Parse(ksPlugAnimClipEnum, dn, true); }
                    catch { }
                }
                string low = s.ToLowerInvariant();
                if (low.Contains("stretch"))
                {
                    try { return Enum.Parse(ksPlugAnimClipEnum, "Stretch_01", true); }
                    catch { }
                }
            }
            return Enum.ToObject(ksPlugAnimClipEnum, 66); // Animations.Clip.Dance_01 == 66
        }

        private object ResolveKsPlugMastClip(string style)
        {
            if (ksPlugAnimClipEnum == null || string.IsNullOrEmpty(style)) return null;
            string s = style.Trim();
            if (s.Length == 0) return null;
            try
            {
                object exact = Enum.Parse(ksPlugAnimClipEnum, s, true);
                if (exact != null && exact.ToString().StartsWith("Mast_", StringComparison.OrdinalIgnoreCase))
                    return exact;
            }
            catch { }

            string low = s.ToLowerInvariant();
            if (low == "lay" || low == "crouch" || low == "floor" || low == "kneel")
                return Enum.ToObject(ksPlugAnimClipEnum, 33); // Mast_01 (crouchMasts)
            if (low == "crouch2" || low == "lay2")
                return Enum.ToObject(ksPlugAnimClipEnum, 34); // Mast_02
            if (low == "stand" || low == "standing")
                return Enum.ToObject(ksPlugAnimClipEnum, 35); // Mast_03 (standMasts)
            if (low == "stand2")
                return Enum.ToObject(ksPlugAnimClipEnum, 36); // Mast_04
            if (low == "sit" || low == "chair")
                return Enum.ToObject(ksPlugAnimClipEnum, 37); // Mast_10 (sitMasts)
            if (low == "sit2")
                return Enum.ToObject(ksPlugAnimClipEnum, 38); // Mast_11
            if (low == "desk" || low == "table")
                return Enum.ToObject(ksPlugAnimClipEnum, 39); // Mast_20 (deskMasts)
            if (low == "wall")
                return Enum.ToObject(ksPlugAnimClipEnum, 40); // Mast_30 (wallMasts)
            return null;
        }

        private void StartKsPlugGuestDance(object guestAi, object clipVal)
        {
            if (!IsUnityAlive(guestAi) || ksPlugGuestActionType == null || ksPlugGuestActionTypeEnum == null) return;
            var curAct = ReadMemberValue(guestAi, "gAction");
            if (IsUnityAlive(curAct))
            {
                int typInt = Convert.ToInt32(ReadMemberValue(curAct, "Typ") ?? 0);
                if (typInt == 9) // GuestAction.Type.SpMove
                {
                    if (ksPlugMenuCharaPoseInfoType != null)
                    {
                        var poseInfo = Activator.CreateInstance(ksPlugMenuCharaPoseInfoType);
                        SetMemberValue(poseInfo, "clip", clipVal);
                        InvokeMember(curAct, "SetGuestActionMenuPose", poseInfo);
                    }
                    else
                    {
                        var sp = InvokeStatic(ksPlugSpecialMoveType, "ActionAs", curAct);
                        if (IsUnityAlive(sp)) InvokeMember(sp, "SetClip", clipVal);
                    }
                    return;
                }
                InvokeMember(guestAi, "BreakAction");
                StartCoroutine(IE_DeferredKsPlugDance(guestAi, clipVal));
                return;
            }
            CreateKsPlugGuestDanceAction(guestAi, clipVal);
        }

        private void CreateKsPlugGuestDanceAction(object guestAi, object clipVal)
        {
            if (!IsUnityAlive(guestAi)) return;
            object spTyp = Enum.ToObject(ksPlugGuestActionTypeEnum, 9); // GuestAction.Type.SpMove == 9
            object actObj = InvokeStatic(ksPlugGuestActionType, "Create", guestAi, spTyp, null, null);
            if (!IsUnityAlive(actObj)) return;
            SetMemberValue(actObj, "startByUser", true);
            SetMemberValue(guestAi, "gAction", actObj);
            var spObj = InvokeStatic(ksPlugSpecialMoveType, "ActionAs", actObj);
            if (IsUnityAlive(spObj)) InvokeMember(spObj, "SetClip", clipVal);
        }

        private void ForceKsPlugGuestStrip(object guestAi, bool stripTop, bool stripBot)
        {
            if (!IsUnityAlive(guestAi) || ksPlugGuestActionType == null || ksPlugGuestActionTypeEnum == null) return;
            object stripTyp = Enum.ToObject(ksPlugGuestActionTypeEnum, 1); // GuestAction.Type.Strip == 1
            object actObj = InvokeStatic(ksPlugGuestActionType, "Create", guestAi, stripTyp, null, null);
            if (!IsUnityAlive(actObj)) return;
            SetMemberValue(actObj, "startByUser", true);
            SetMemberValue(guestAi, "gAction", actObj);
            InvokeMember(actObj, "SetStripFlag", stripTop, stripBot);
        }

        private IEnumerator IE_DeferredKsPlugSwap(object guestAi)
        {
            yield return null;
            yield return null;
            if (!IsUnityAlive(guestAi) || Scene == null || Scene.isEnd) yield break;
            try { InvokeMember(guestAi, "AskForSwap"); }
            catch { }
        }

        private IEnumerator IE_DeferredKsPlugStrip(object guestAi, bool stripTop, bool stripBot)
        {
            yield return null;
            yield return null;
            if (!IsUnityAlive(guestAi) || Scene == null || Scene.isEnd) yield break;
            try
            {
                object okObj = InvokeMember(guestAi, "AskForStrip", (object)null, stripTop, stripBot);
                if (!(okObj is bool && (bool)okObj))
                    ForceKsPlugGuestStrip(guestAi, stripTop, stripBot);
            }
            catch { }
        }

        private IEnumerator IE_DeferredKsPlugDance(object guestAi, object clipVal)
        {
            yield return null;
            yield return null;
            if (!IsUnityAlive(guestAi) || Scene == null || Scene.isEnd) yield break;
            try { CreateKsPlugGuestDanceAction(guestAi, clipVal); }
            catch { }
        }

        private IEnumerator IE_ApplyKsPlugMastClip(object guestAi, object clipVal)
        {
            if (ksPlugMenuCharaPoseInfoType == null) yield break;
            for (int f = 0; f < 25; f++)
            {
                yield return null;
                if (!IsUnityAlive(guestAi) || Scene == null || Scene.isEnd) yield break;
                var gAct = ReadMemberValue(guestAi, "gAction");
                if (!IsUnityAlive(gAct)) continue;
                int typInt = Convert.ToInt32(ReadMemberValue(gAct, "Typ") ?? 0);
                if (typInt != 2) continue;
                if (!Convert.ToBoolean(ReadMemberValue(gAct, "isInit") ?? false)) continue;

                try
                {
                    var poseInfo = Activator.CreateInstance(ksPlugMenuCharaPoseInfoType);
                    SetMemberValue(poseInfo, "clip", clipVal);
                    InvokeMember(gAct, "SetGuestActionMenuPose", poseInfo);
                }
                catch { }
                yield break;
            }
        }
    }
}
