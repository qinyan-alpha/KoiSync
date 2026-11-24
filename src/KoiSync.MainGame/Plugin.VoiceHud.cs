using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.MainGame
{
    public sealed partial class Plugin
    {
        private static string[] AiHubTabs { get { return new[] { L("实时对话", "Live Chat"), L("手控导演", "Manual Director"), L("主动模式 & SR6实验", "Autonomy & SR6 Lab") }; } }

        private ConfigEntry<bool> voiceEnabled;
        private ConfigEntry<bool> showQuickDock;
        private ConfigEntry<string> voiceSidecarUrl;
        private ConfigEntry<string> voiceSidecarScript;
        private ConfigEntry<KeyboardShortcut> voicePttKey;

        private VoiceClient voiceClient;
        private int aiHubTab;
        private float nextVoicePoll;
        private float nextAllowedSidecarLaunchAt;
        private string voiceInputText = "";
        private string dockInputText = "";
        private string manualCharQuery = "";
        private Vector2 voiceChatScroll;
        private Vector2 voiceMemScroll;
        private Vector2 manualGuestScroll;
        private Vector2 mathLabEventScroll;
        private bool voiceMicHeldByKey;
        private bool voiceMicRecordingLocal;
        private bool rebindingPttKey;
        private bool dockCollapsed;
        private bool dockChatOpen;
        private bool dockPosInit;
        private Rect dockRect = new Rect(640, 560, 344, 102);
        private string hudToastReply = "";
        private string hudToastMeta = "";
        private float hudToastUntil;
        private string lastSeenStatusReply = "";
        private readonly SR6MathParams mathLabUiParams = new SR6MathParams();

        // Thread-safe bridge from VoiceClient background callbacks to Unity main thread (fixes P0-1)
        private readonly object voiceHudGate = new object();
        private bool pendingToastReady;
        private string pendingToastReply = "";
        private string pendingToastMeta = "";
        private string pendingVoiceNotice;
        private bool pendingMicClear;

        // Local debounced slider cache so dragging doesn't flood POST /config (fixes P1-9)
        private float localFreeMicThr = -34f;
        private bool localFreeMicThrDirty;
        private float localFreeMicThrSendAt;
        private float localVoiceGain = 0.88f;
        private bool localVoiceGainDirty;
        private float localVoiceGainSendAt;

        private void InitializeVoiceHud()
        {
            voiceEnabled = Config.Bind("Voice", "Enabled", false, "Enable AI real-time voice & smart director (connects to local VoiceDirector)");
            showQuickDock = Config.Bind("Voice", "ShowQuickDock", false, "Show compact quick director overlay in H-scene");
            voiceSidecarUrl = Config.Bind("Voice", "SidecarUrl", "http://127.0.0.1:41295", "Local KoiSync.VoiceDirector URL");
            voiceSidecarScript = Config.Bind("Voice", "SidecarScriptPath", "", "Optional path to start_voice_director.ps1 (leave blank to auto-detect)");
            voicePttKey = Config.Bind("Voice", "PushToTalkKey", new KeyboardShortcut(KeyCode.V), "In-game push-to-talk key (supports keyboard or side mouse buttons Mouse3/Mouse4)");
            voiceClient = new VoiceClient(voiceSidecarUrl.Value);
        }

        private string ResolveVoiceSidecarScript()
        {
            string configured = voiceSidecarScript != null ? (voiceSidecarScript.Value ?? "").Trim() : "";
            if (configured.Length > 0 && File.Exists(configured)) return configured;

            string pluginDir = "";
            try { if (Info != null && !string.IsNullOrEmpty(Info.Location)) pluginDir = Path.GetDirectoryName(Info.Location); } catch { }
            string[] candidates = new[]
            {
                string.IsNullOrEmpty(pluginDir) ? "" : Path.Combine(Path.Combine(pluginDir, "voice_director"), "start_voice_director.ps1"),
                Path.Combine(Path.Combine(Paths.GameRootPath, "voice_director"), "start_voice_director.ps1"),
                string.IsNullOrEmpty(DataRoot) ? "" : Path.Combine(Path.Combine(DataRoot, "voice_director"), "start_voice_director.ps1"),
                @"E:\project\c#\KoiSync\voice_director\start_voice_director.ps1",
            };
            for (int i = 0; i < candidates.Length; i++)
                if (!string.IsNullOrEmpty(candidates[i]) && File.Exists(candidates[i])) return candidates[i];
            return candidates[0].Length > 0 ? candidates[0] : candidates[1];
        }

        private void QueueVoiceToast(string reply, string meta)
        {
            if (string.IsNullOrEmpty(reply)) return;
            lock (voiceHudGate)
            {
                pendingToastReply = reply;
                pendingToastMeta = meta ?? "";
                pendingToastReady = true;
            }
        }

        private void QueueVoiceNotice(string notice, bool clearMic)
        {
            lock (voiceHudGate)
            {
                if (!string.IsNullOrEmpty(notice)) pendingVoiceNotice = notice;
                if (clearMic) pendingMicClear = true;
            }
        }

        private void FlushVoiceHudMainThreadQueue()
        {
            bool hasToast = false;
            string toastR = "", toastM = "", notice = null;
            bool clearMic = false;
            lock (voiceHudGate)
            {
                if (pendingToastReady)
                {
                    hasToast = true;
                    toastR = pendingToastReply;
                    toastM = pendingToastMeta;
                    pendingToastReady = false;
                }
                if (pendingVoiceNotice != null)
                {
                    notice = pendingVoiceNotice;
                    pendingVoiceNotice = null;
                }
                if (pendingMicClear)
                {
                    clearMic = true;
                    pendingMicClear = false;
                }
            }
            if (clearMic) { voiceMicRecordingLocal = false; voiceMicHeldByKey = false; }
            if (notice != null) uiNotice = notice;
            if (hasToast)
            {
                hudToastReply = toastR;
                hudToastMeta = toastM;
                hudToastUntil = Time.realtimeSinceStartup + 11f;
            }
        }

        private void UpdateVoiceHud()
        {
            FlushVoiceHudMainThreadQueue();

            // Capture new PTT key when user clicks rebind
            if (rebindingPttKey)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    rebindingPttKey = false;
                    uiNotice = L("已取消修改语音快捷键", "Cancelled voice hotkey rebind");
                    return;
                }
                foreach (KeyCode kc in Enum.GetValues(typeof(KeyCode)))
                {
                    if (kc == KeyCode.None || kc == KeyCode.Mouse0 || kc == KeyCode.Mouse1 || kc == KeyCode.Mouse2) continue;
                    if (Input.GetKeyDown(kc))
                    {
                        voicePttKey.Value = new KeyboardShortcut(kc);
                        Config.Save();
                        rebindingPttKey = false;
                        uiNotice = L("按住说话快捷键已改为：", "Push-to-talk key set to: ") + kc;
                        return;
                    }
                }
                return;
            }

            if (voiceClient == null || voiceEnabled == null || !voiceEnabled.Value)
            {
                if (voiceMicHeldByKey) { voiceMicHeldByKey = false; voiceMicRecordingLocal = false; }
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now >= nextVoicePoll)
            {
                nextVoicePoll = now + 0.85f;
                voiceClient.PollStatusAsync();
            }

            // Flush debounced slider config updates
            if (localFreeMicThrDirty && !Input.GetMouseButton(0) && now >= localFreeMicThrSendAt)
            {
                localFreeMicThrDirty = false;
                voiceClient.UpdateVoiceConfigAsync("freeMicThresholdDb", (float)Math.Round(localFreeMicThr));
            }
            if (localVoiceGainDirty && !Input.GetMouseButton(0) && now >= localVoiceGainSendAt)
            {
                localVoiceGainDirty = false;
                voiceClient.UpdateVoiceConfigAsync("voiceGain", (float)Math.Round(localVoiceGain, 2));
            }

            var st = voiceClient.GetSnapshot();
            if (!localFreeMicThrDirty) localFreeMicThr = st.FreeMicThresholdDb;
            if (!localVoiceGainDirty) localVoiceGain = st.VoiceGain;

            if (st.Connected && !string.IsNullOrEmpty(st.LastReply) && st.LastReply != lastSeenStatusReply)
            {
                lastSeenStatusReply = st.LastReply;
                hudToastReply = st.LastReply;
                hudToastMeta = st.LastActionSummary ?? "";
                hudToastUntil = now + 11f;
            }

            // Release stuck PTT state even if focus/connection/mode changed mid-hold (fixes P1-9)
            KeyCode pttKey = voicePttKey != null ? voicePttKey.Value.MainKey : KeyCode.None;
            if (voiceMicHeldByKey)
            {
                if (!st.Connected || !st.VoiceInputEnabled || st.FreeMicEnabled || pttKey == KeyCode.None || !Input.GetKey(pttKey))
                {
                    voiceMicHeldByKey = false;
                    if (st.Connected && voiceMicRecordingLocal) StopVoiceMicAndSend();
                    else voiceMicRecordingLocal = false;
                }
                return;
            }

            // Push-to-Talk hotkey (only when VoiceInput is ON, Free-Mic is OFF, and not typing inside a text field)
            if (st.Connected && st.VoiceInputEnabled && !st.FreeMicEnabled && pttKey != KeyCode.None)
            {
                bool typing = GUIUtility.keyboardControl != 0 && (dockChatOpen || (showWindow && mainPage == 2));
                if (!typing && Input.GetKeyDown(pttKey))
                {
                    voiceMicHeldByKey = true;
                    StartVoiceMic();
                }
            }
        }

        private string PttKeyLabel()
        {
            if (rebindingPttKey) return L("请按键...", "Press key...");
            return voicePttKey != null ? voicePttKey.Value.MainKey.ToString() : "V";
        }

        private void SetVoiceEnabledWithHardGate(bool enabled)
        {
            if (voiceEnabled == null) return;
            voiceEnabled.Value = enabled;
            Config.Save();
            if (enabled)
            {
                ResumeAiTakeover();
                if (voiceClient != null) voiceClient.PollStatusAsync();
                uiNotice = L("已开启 AI 语音互动连接", "AI voice interaction enabled");
            }
            else
            {
                DisconnectAiTakeover(L("已关闭 AI 语音互动并彻底切断后台 AI 接管", "AI voice disabled; background AI takeover cut"));
            }
        }

        private void StartVoiceMic()
        {
            if (voiceClient == null || voiceMicRecordingLocal || voiceClient.Busy) return;
            voiceMicRecordingLocal = true;
            voiceClient.StartMicAsync((ok, info) =>
            {
                if (!ok)
                    QueueVoiceNotice(L("麦克风启动失败：", "Microphone failed to start: ") + info, true);
            });
        }

        private void StopVoiceMicAndSend()
        {
            if (voiceClient == null || !voiceMicRecordingLocal) return;
            voiceMicRecordingLocal = false;
            var st = voiceClient.GetSnapshot();
            voiceClient.StopMicAndChatAsync(st.Language, (u, reply, summary) =>
            {
                QueueVoiceToast(reply, summary);
            });
        }

        private void SendVoiceText(string text)
        {
            string t = (text ?? "").Trim();
            if (t.Length == 0 || voiceClient == null) return;
            if (AiTakeoverBlocked) ResumeAiTakeover();
            var st = voiceClient.GetSnapshot();
            voiceClient.SendChatAsync(t, st.Language, (reply, summary) =>
            {
                QueueVoiceToast(reply, summary);
            });
        }

        private void LaunchVoiceSidecarScript()
        {
            try
            {
                var st = voiceClient != null ? voiceClient.GetSnapshot() : null;
                if (st != null && st.Connected)
                {
                    uiNotice = L("后台服务已在线，无需重复启动（防多开锁已生效）", "Voice sidecar is already online (single-instance lock active)");
                    return;
                }
                float now = Time.realtimeSinceStartup;
                if (now < nextAllowedSidecarLaunchAt)
                {
                    int waitSec = Mathf.CeilToInt(nextAllowedSidecarLaunchAt - now);
                    uiNotice = L("后台服务正在初始化 CUDA 模型，请等待约 ", "Sidecar is initializing CUDA models; wait ~") + waitSec + L(" 秒（已防重复启动）", "s (launch throttled)");
                    return;
                }
                string ps1 = ResolveVoiceSidecarScript();
                if (!File.Exists(ps1))
                {
                    uiNotice = L("未找到启动脚本：", "Launch script not found: ") + ps1;
                    return;
                }
                nextAllowedSidecarLaunchAt = now + 18f;
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + ps1 + "\"",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Minimized,
                };
                Process.Start(psi);
                uiNotice = L("正在单实例拉起后台服务（约 5~12 秒自动连上，已加防多开保护）", "Launching sidecar (connects in ~5–12s; single-instance protected)");
            }
            catch (Exception ex)
            {
                uiNotice = L("拉起语音服务失败：", "Failed to launch voice sidecar: ") + ex.Message;
            }
        }

        private bool voiceShutdownSent;

        internal void ShutdownVoiceSidecarOnGameExit()
        {
            if (voiceShutdownSent) return;
            voiceShutdownSent = true;
            try
            {
                if (voiceClient != null)
                    voiceClient.ShutdownSidecarSync(450);
            }
            catch { }
        }

        private void StopVoiceSidecar()
        {
            try
            {
                DisconnectAiTakeover(L("正在停止后台 AI 服务并释放接管...", "Stopping background AI service and releasing takeover..."));
                if (voiceClient != null)
                    voiceClient.PostJsonFireAndForget("/shutdown", "{}");
                nextAllowedSidecarLaunchAt = 0f;
                string ps1 = ResolveVoiceSidecarScript();
                if (File.Exists(ps1))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + ps1 + "\" -Stop",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                    };
                    Process.Start(psi);
                }
                uiNotice = L("已发送停止后台 AI 服务指令并断开 AI 接管", "Sent stop command to AI sidecar and cut takeover");
            }
            catch (Exception ex)
            {
                uiNotice = L("停止后台服务失败：", "Failed to stop sidecar: ") + ex.Message;
            }
        }

        private void TriggerQuickPreset(string presetName)
        {
            if (Scene == null || Flags == null)
            {
                uiNotice = L("请先进入本体 H 场景", "Enter a Main Game H-scene first");
                return;
            }
            try
            {
                if (presetName == "release")
                {
                    DirectorReleaseSpeedLatch();
                    ReleaseDirectorGaugeControl();
                    if (Expressions != null) Expressions.ReleaseAll();
                    if (MathEngine.Active) DirectorStopMathLab(L("手动释放 SR6 数学实验台", "Released SR6 Math Lab"));
                    if (Timeline.Active) DirectorStopTimeline(L("手动释放时间轴变式", "Released Timeline variant"));
                    else RestoreTimelineActors(true);
                    Director.Release(0.6);
                    uiNotice = L("已释放导演接管，还给游戏原生", "Director released; restored native control");
                    return;
                }
                if (presetName == "start_insert")
                {
                    if (Flags.mode == HFlag.EMode.aibu)
                        SwitchPoseByChineseHint("正常位");
                    else
                        DirectorGameAction("start_insert", null, null);
                    return;
                }
                if (!IsCurrentlyInserted())
                {
                    uiNotice = L("尚未插入！请先点「插入开始」或切入体位后再使用插入变式", "Not inserted yet! Click Insert or switch to an insertion pose first");
                    return;
                }
                double ceil = ActiveOuterCeilingL0;
                if (presetName == "climax")
                {
                    DirectorTriggerSeamlessClimax("sameS");
                    return;
                }
                if (presetName == "one_shallow_one_deep")
                {
                    var sd = TimelineStep.FromPreset(TimelinePreset.NineShallowOneDeep, 45.0, ceil);
                    sd.Name = "1浅1深";
                    sd.ShallowCount = 1; sd.DeepCount = 1; sd.FreqHz = 1.65; sd.Loop = true; sd.DeepHoldSeconds = 0.55;
                    DirectorStartTimeline("1浅1深", new[] { sd }, true);
                    uiNotice = L("已启动组合变式：1浅1深", "Started variant: 1 Shallow 1 Deep");
                    return;
                }
                if (presetName == "waist_line_fb")
                {
                    var wl = TimelineStep.FromPreset(TimelinePreset.WaistOnly, 45.0, ceil);
                    wl.Name = "定深前后直线摆腰";
                    wl.GrindPattern = GrindPattern.LineFB; wl.TwistAmp = 0.0; wl.GrindAmp = 0.38; wl.Loop = true;
                    DirectorStartTimeline("定深前后直线摆腰", new[] { wl }, true);
                    uiNotice = L("已启动：定深前后直线摆腰 (仅L1+R2，不画圈不旋转)", "Started: Fixed-depth F/B Sway (L1+R2 only, no orbit/twist)");
                    return;
                }
                TimelinePreset p = TimelinePreset.Organic;
                double dur = 45.0;
                bool loop = true;
                if (presetName == "deep_grind") { p = TimelinePreset.DeepGrind; dur = 25.0; }
                else if (presetName == "nine_shallow") { p = TimelinePreset.NineShallowOneDeep; dur = 28.0; }
                else if (presetName == "tip_rub") { p = TimelinePreset.TipRub; dur = 20.0; }
                else if (presetName == "tease_plunge") { p = TimelinePreset.TeasePlunge; dur = 24.0; }
                else if (presetName == "turbo_sprint") { p = TimelinePreset.TurboSprint; dur = 18.0; }
                var step = TimelineStep.FromPreset(p, dur, ceil);
                step.Loop = loop;
                DirectorStartTimeline(presetName, new[] { step }, loop);
                uiNotice = L("已启动变式：", "Started variant: ") + UiText.TranslateComposite(step.Name);
            }
            catch (Exception ex)
            {
                uiNotice = L("快捷动作提示：", "Quick action notice: ") + ex.GetBaseException().Message;
            }
        }

        private void SwitchPoseByChineseHint(string hint)
        {
            if (Scene == null || Flags == null) { uiNotice = L("当前不在 H 场景中", "Not currently in an H-scene"); return; }
            try
            {
                DirectorSwitchPose(hint, false, true, true);
            }
            catch (Exception ex)
            {
                uiNotice = L("切换体位失败：", "Pose switch failed: ") + ex.GetBaseException().Message;
            }
        }

        private void OpenVoiceConfigInBrowser(string hash = "")
        {
            try
            {
                string url = (voiceSidecarUrl != null ? voiceSidecarUrl.Value : "http://127.0.0.1:41295").TrimEnd('/') + "/ui" + (hash ?? "");
                Process.Start(url);
            }
            catch (Exception ex) { uiNotice = L("打开网页控制台失败：", "Failed to open web console: ") + ex.Message; }
        }

        /// <summary>
        /// Decluttered, minimal Settings card: keeps only essential toggles + 1-click Start/Stop/Web Console.
        /// Full API keys, Base URL, model discovery, custom prompts, and TTS/ASR live cleanly in Web Console!
        /// </summary>
        private void DrawVoiceSettingsCard()
        {
            float y = Card(18, 256, 874, 212, L("AI 语音导演（精简控制 · 详细 API / 模型 / 提示词请打开网页控制台）", "AI Voice Director (Quick controls · open Web Console for API / models / prompts)"));
            var st = voiceClient != null ? voiceClient.GetSnapshot() : new VoiceStatusSnapshot();

            // Row 1: 3 essential toggles
            bool vOn = voiceEnabled != null && voiceEnabled.Value;
            bool nextVOn = GUI.Toggle(new Rect(32, y, 178, 24), vOn, L("开启 AI 语音互动", "Enable AI voice"), toggle);
            if (nextVOn != vOn) SetVoiceEnabledWithHardGate(nextVOn);

            bool dOn = showQuickDock != null && showQuickDock.Value;
            bool nextDOn = GUI.Toggle(new Rect(224, y, 168, 24), dOn, L("右下角快捷悬浮条", "Show quick dock"), toggle);
            if (nextDOn != dOn && showQuickDock != null) { showQuickDock.Value = nextDOn; Config.Save(); }

            GUI.enabled = vOn && st.Connected;
            bool nextSilent = GUI.Toggle(new Rect(408, y, 206, 24), st.SilentMode, L("静音执行 (只听做动作)", "Silent mode (actions only)"), toggle);
            if (nextSilent != st.SilentMode && voiceClient != null)
            {
                voiceClient.UpdateVoiceConfigAsync("silentMode", nextSilent);
                uiNotice = nextSilent ? L("已进入静音执行：只听语音做动作，AI不出声", "Silent mode on: executes voice commands without TTS") : L("已恢复 AI 语音发声", "AI voice speech restored");
            }
            GUI.enabled = true;

            // Row 2: 5 clean service & console actions
            float r2 = y + 38;
            if (Button(32, r2, 134, 30, st.Connected ? L("服务运行中", "Sidecar online") : L("启动后台服务", "Start sidecar"), st.Connected ? activeButton : primaryButton))
                LaunchVoiceSidecarScript();
            if (Button(174, r2, 126, 30, L("停止后台服务", "Stop sidecar"), dangerButton))
                StopVoiceSidecar();
            if (Button(308, r2, 266, 30, L("打开网页控制台 (API / 模型 / 提示词)", "Open Web Console (API / Models / Prompts)"), activeButton))
                OpenVoiceConfigInBrowser("#models");
            if (Button(582, r2, 146, 30, L("说话键: ", "PTT: ") + PttKeyLabel(), rebindingPttKey ? dangerButton : button))
            {
                rebindingPttKey = !rebindingPttKey;
                uiNotice = rebindingPttKey ? L("请按下任意键盘按键或鼠标侧键（ESC取消）", "Press any key or mouse side button (Esc to cancel)") : "";
            }
            if (Button(736, r2, 142, 30, AiTakeoverBlocked ? L("恢复 AI 接管", "Resume AI") : L("断开 AI 接管", "Cut AI takeover"), AiTakeoverBlocked ? button : dangerButton))
            {
                if (AiTakeoverBlocked) { ResumeAiTakeover(); uiNotice = L("已恢复允许 AI 接管", "AI takeover re-enabled"); }
                else DisconnectAiTakeover(L("已彻底断开 AI 接管并交还游戏控制", "AI takeover cut; returned control to game"));
            }

            // Row 3: Concise live status + kPlug arbitration badge
            string statusLine;
            if (AiTakeoverBlocked)
                statusLine = L("状态：AI 接管已硬切断（后台所有动作指令均被拦截，点击「恢复 AI 接管」或重新勾选上方开关即可恢复）", "Status: AI takeover hard-blocked (all remote commands intercepted; click Resume AI to re-enable)");
            else if (!vOn)
                statusLine = L("状态：未开启（勾选上方「开启 AI 语音互动」即可连接本地后台服务）", "Status: Disabled (check Enable AI voice above to connect to the local sidecar)");
            else if (st.Connected)
                statusLine = L("在线 v", "Online v") + st.Version + " · " + st.Provider + "/" + st.Model +
                             L(" · 自主=", " · Autonomy=") + AutonomyMode +
                             (IsKPlugLoaded && KPlugGirlThinkPaused ? string.Format(L(" · {0} 主动思考已自动关闭", " · {0} auto-think paused"), PlugBrandName) : "");
            else
                statusLine = L("状态：后台未连接（点击「启动后台服务」单实例拉起，已防重复启动卡死）", "Status: Sidecar offline (click Start sidecar to launch a single instance)");
            GUI.Label(new Rect(32, y + 84, 846, 26), statusLine, label);

            GUI.Label(new Rect(32, y + 118, 846, 40),
                L("提示：为保持游戏内界面清爽，API Key、中转地址、模型选择、自定义提示词与语音模型目录已统一收敛到网页控制台；游戏内点击侧栏「AI 互动」即可对话、手控服装/节奏或做 SR6 数学实验。",
                  "Tip: API keys, base URLs, models, custom prompts, and voice models live in the Web Console. Use the AI Hub sidebar tab in-game for chat, manual director controls, and the SR6 Math Lab."),
                wrapSmall);
        }

        /// <summary>
        /// Unified 3rd Sidebar Tab ("AI Hub") with 3 clean top sub-tabs:
        ///   0: Live Chat  |  1: Manual Director  |  2: Autonomy & SR6 Lab
        /// </summary>
        private void DrawAiHubPage()
        {
            var tabs = AiHubTabs;
            for (int i = 0; i < tabs.Length; i++)
            {
                if (Button(18 + i * 296, 55, 282, 30, tabs[i], aiHubTab == i ? activeButton : button))
                {
                    aiHubTab = i;
                    uiNotice = "";
                    GUI.FocusControl(null);
                }
            }
            if (aiHubTab == 0) DrawVoiceChatPage();
            else if (aiHubTab == 1) DrawManualDirectorPage();
            else DrawMathLabAndAutonomyPage();
        }

        private void DrawVoiceChatPage()
        {
            var st = voiceClient != null ? voiceClient.GetSnapshot() : new VoiceStatusSnapshot();
            bool vOn = voiceEnabled != null && voiceEnabled.Value;

            // Left: Live Chat Transcript + Text Input + Mic Controls (y=95, h=498 -> bottom=593)
            float y = Card(18, 95, 560, 498, L("实时语音 / 文字调情对话", "Live Voice / Text Chat") + (st.Connected ? ("  ·  " + st.Model) : L("  ·  未连接", "  ·  Offline")));

            if (!vOn)
            {
                GUI.Label(new Rect(34, y + 10, 520, 48), L("当前语音互动处于关闭状态。点击下方按钮开启即可连接本地后台服务（防多开单实例保护）。", "AI voice interaction is off. Click below to enable and connect to the local sidecar."), wrapSmall);
                if (Button(34, y + 66, 180, 34, L("开启 AI 语音互动", "Enable AI voice"), primaryButton)) SetVoiceEnabledWithHardGate(true);
                if (Button(224, y + 66, 148, 34, L("启动后台服务", "Start sidecar"))) LaunchVoiceSidecarScript();
                if (Button(382, y + 66, 148, 34, L("打开网页控制台", "Open Web Console"))) OpenVoiceConfigInBrowser("");
            }
            else
            {
                var entries = voiceClient != null ? voiceClient.GetTranscriptCopy() : new VoiceChatEntry[0];
                float totalH = 12f;
                var heights = new float[entries.Length];
                for (int i = 0; i < entries.Length; i++)
                {
                    int lines = Math.Max(1, (entries[i].Text.Length + 25) / 26);
                    heights[i] = 28f + lines * 20f + (string.IsNullOrEmpty(entries[i].Meta) ? 4f : 20f);
                    totalH += heights[i] + 6f;
                }
                voiceChatScroll = GUI.BeginScrollView(new Rect(30, y, 536, 366), voiceChatScroll, new Rect(0, 0, 514, Math.Max(354f, totalH)));
                float cy = 6f;
                if (entries.Length == 0)
                {
                    GUI.Label(new Rect(8, 12, 496, 86),
                        L("已就绪！可直接语音或打字互动：\n·「换正常位 / 骑乘位」→ 先切面对面经典并询问变体与插入\n·「半脱上衣」/「把内裤脱掉」/「换泳装」→ 精准单件控制\n·「1浅1深」/「只扭腰不旋转」/「开启虚拟支点行波」",
                          "Ready! Speak or type commands:\n· \"Switch to missionary / cowgirl\" → pose & insertion control\n· \"Half-undress top\" / \"Take off panties\" / \"Swimsuit\" → outfit control\n· \"Slow down\" / \"Front-back sway only\" / \"Virtual pivot wave\""),
                        wrapSmall);
                }
                else
                {
                    for (int i = 0; i < entries.Length; i++)
                    {
                        var e = entries[i];
                        bool isUser = e.Role == "user";
                        bool isAi = e.Role == "ai";
                        string prefix = isUser ? L("主人：", "You: ") : (isAi ? L("她：", "AI: ") : L("提示：", "Info: "));
                        Color bg = isUser ? new Color(.14f, .18f, .27f, .92f) : (isAi ? new Color(.11f, .20f, .22f, .92f) : new Color(.10f, .12f, .15f, .80f));
                        float h = heights[i];
                        Box(new Rect(4, cy, 504, h), bg);
                        GUI.Label(new Rect(12, cy + 5, 488, h - 24f), prefix + e.Text, wrapSmall);
                        if (!string.IsNullOrEmpty(e.Meta))
                            GUI.Label(new Rect(12, cy + h - 21f, 488, 19f), e.Meta, small);
                        cy += h + 6f;
                    }
                }
                GUI.EndScrollView();

                // Input row (y + 374 = 505, h = 30)
                GUI.SetNextControlName("KoiSyncVoiceChatInput");
                voiceInputText = Edit(30, y + 374, 368, 30, voiceInputText);
                bool enterPressed = Event.current.type == EventType.KeyDown &&
                                    (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) &&
                                    GUI.GetNameOfFocusedControl() == "KoiSyncVoiceChatInput";
                bool canSend = st.Connected && !voiceClient.Busy;
                GUI.enabled = canSend;
                if (Button(404, y + 374, 74, 30, voiceClient.Busy ? L("思考中", "Busy") : L("发送", "Send"), primaryButton) || (canSend && enterPressed && voiceInputText.Trim().Length > 0))
                {
                    string send = voiceInputText;
                    voiceInputText = "";
                    SendVoiceText(send);
                    if (enterPressed) Event.current.Use();
                }
                GUI.enabled = st.Connected && st.VoiceInputEnabled && !st.FreeMicEnabled;
                if (Button(484, y + 374, 82, 30, !st.VoiceInputEnabled ? L("麦已关闭", "Mic off") : (voiceMicRecordingLocal ? L("松开发送", "Release") : L("点击说话", "Talk")), voiceMicRecordingLocal ? dangerButton : activeButton))
                {
                    if (voiceMicRecordingLocal) StopVoiceMicAndSend();
                    else StartVoiceMic();
                }

                GUI.enabled = st.Connected;
                if (Button(30, y + 412, 118, 28, L("打断说话", "Interrupt"), dangerButton))
                    voiceClient.PostSimpleAsync("/stop", "{}");
                if (Button(156, y + 412, 134, 28, L("让她主动问一句", "Prompt check-in")))
                    voiceClient.PostSimpleAsync("/proactive", "{\"force\":true}");
                if (Button(298, y + 412, 124, 28, L("清空对话上下文", "Clear history")))
                    voiceClient.PostSimpleAsync("/chat/clear", "{}");
                if (Button(430, y + 412, 136, 28, L("打开网页控制台", "Open Web Console")))
                    OpenVoiceConfigInBrowser("");
                GUI.enabled = true;
            }

            // Right Top: Compact Model / Mic / Voice controls (y=95, h=342 -> bottom=437)
            float ry = Card(590, 95, 302, 342, L("模型、麦克风与声线", "Model, Mic & Voice"));
            GUI.enabled = vOn && st.Connected;
            GUI.Label(new Rect(604, ry, 274, 20), L("大模型（缓存 ", "LLM (Cache ") + st.CacheHitRate.ToString("0", CultureInfo.InvariantCulture) + "% · ¥" + st.EstCostRmb.ToString("0.0000", CultureInfo.InvariantCulture) + "）", small);
            if (Button(604, ry + 21, 134, 24, L("DS-Chat (推荐)", "DS-Chat (Rec.)"), st.Model == "deepseek-chat" ? activeButton : button))
                voiceClient.UpdateBrainPresetAsync("deepseek", "deepseek-chat");
            if (Button(744, ry + 21, 134, 24, "DS-V4.1-Flash", st.Model == "deepseek-flash" ? activeButton : button))
                voiceClient.UpdateBrainPresetAsync("deepseek", "deepseek-flash");
            if (Button(604, ry + 49, 134, 24, "DS-V4-Pro", st.Model == "deepseek-v4-pro" ? activeButton : button))
                voiceClient.UpdateBrainPresetAsync("deepseek", "deepseek-v4-pro");
            if (Button(744, ry + 49, 134, 24, "Gemini 3.1", st.Model == "gemini-3.1-flash-lite" ? activeButton : button))
                voiceClient.UpdateBrainPresetAsync("gemini", "gemini-3.1-flash-lite");

            GUI.Label(new Rect(604, ry + 78, 274, 20), L("麦克风模式", "Microphone mode"), small);
            if (Button(604, ry + 98, 88, 24, L("关麦", "Mic off"), !st.VoiceInputEnabled ? activeButton : button))
                voiceClient.UpdateVoiceConfigAsync("voiceInputEnabled", false);
            if (Button(696, ry + 98, 90, 24, L("按键(", "PTT(") + PttKeyLabel() + ")", st.VoiceInputEnabled && !st.FreeMicEnabled ? activeButton : button))
            {
                voiceClient.UpdateVoiceConfigAsync("voiceInputEnabled", true);
                voiceClient.UpdateVoiceConfigAsync("freeMicEnabled", false);
            }
            if (Button(790, ry + 98, 88, 24, L("自由麦", "Open mic"), st.VoiceInputEnabled && st.FreeMicEnabled ? activeButton : button))
            {
                voiceClient.UpdateVoiceConfigAsync("voiceInputEnabled", true);
                voiceClient.UpdateVoiceConfigAsync("freeMicEnabled", true);
            }

            GUI.enabled = vOn && st.Connected;
            if (st.VoiceInputEnabled && st.FreeMicEnabled)
            {
                GUI.Label(new Rect(604, ry + 126, 168, 20), L("自由麦灵敏度", "Open-mic threshold"), small);
                GUI.Label(new Rect(790, ry + 126, 88, 20), localFreeMicThr.ToString("0", CultureInfo.InvariantCulture) + " dB", small);
                float nextThr = GUI.HorizontalSlider(new Rect(604, ry + 145, 274, 16), localFreeMicThr, -46f, -18f);
                if (Math.Abs(nextThr - localFreeMicThr) >= 0.5f)
                {
                    localFreeMicThr = (float)Math.Round(nextThr);
                    localFreeMicThrDirty = true;
                    localFreeMicThrSendAt = Time.realtimeSinceStartup + 0.15f;
                }
            }
            else
            {
                GUI.enabled = vOn;
                if (Button(604, ry + 128, 274, 24, rebindingPttKey ? L("请按任意键 (ESC取消)", "Press any key (Esc cancels)") : L("修改按住说话键（当前：", "Change PTT key (Current: ") + PttKeyLabel() + ")", rebindingPttKey ? dangerButton : button))
                    rebindingPttKey = !rebindingPttKey;
                GUI.enabled = vOn && st.Connected;
            }

            GUI.Label(new Rect(604, ry + 162, 274, 20), L("识别 / 静音 / 声线", "ASR / Silent / Voice"), small);
            if (Button(604, ry + 182, 88, 24, L("GPU高精", "GPU ASR"), st.AsrEngine == "gpu_turbo" ? activeButton : button))
                voiceClient.UpdateVoiceConfigAsync("asrEngine", "gpu_turbo");
            if (Button(696, ry + 182, 90, 24, L("Gemini直听", "Gemini ASR"), st.AsrEngine == "gemini_direct" ? activeButton : button))
                voiceClient.UpdateVoiceConfigAsync("asrEngine", "gemini_direct");
            if (Button(790, ry + 182, 88, 24, L("静音执行", "Silent"), st.SilentMode ? dangerButton : button))
                voiceClient.UpdateVoiceConfigAsync("silentMode", !st.SilentMode);

            if (Button(604, ry + 210, 88, 24, L("中文 Serena", "ZH Serena"), st.Language == "zh" ? activeButton : button))
                voiceClient.UpdateVoiceConfigAsync("language", "zh");
            if (Button(696, ry + 210, 90, 24, L("日语 04_JA", "JA 04_JA"), st.Language == "ja" ? activeButton : button))
                voiceClient.UpdateVoiceConfigAsync("language", "ja");
            if (Button(790, ry + 210, 88, 24, "English", st.Language == "en" ? activeButton : button))
                voiceClient.UpdateVoiceConfigAsync("language", "en");

            bool nextPro = GUI.Toggle(new Rect(604, ry + 239, 274, 22), st.ProactiveEnabled, L("高潮/动作结束或10分钟后主动问", "Proactive check-in after climax / idle"), toggle);
            if (nextPro != st.ProactiveEnabled)
                voiceClient.UpdateVoiceConfigAsync("proactiveEnabled", nextPro);

            GUI.Label(new Rect(604, ry + 264, 174, 20), L("AI 贴耳混音音量", "AI voice volume"), small);
            GUI.Label(new Rect(816, ry + 264, 62, 20), localVoiceGain.ToString("0.00", CultureInfo.InvariantCulture) + "x", small);
            float nextGain = GUI.HorizontalSlider(new Rect(604, ry + 284, 274, 16), localVoiceGain, 0.30f, 1.50f);
            if (Math.Abs(nextGain - localVoiceGain) >= 0.02f)
            {
                localVoiceGain = (float)Math.Round(nextGain, 2);
                localVoiceGainDirty = true;
                localVoiceGainSendAt = Time.realtimeSinceStartup + 0.15f;
            }
            GUI.enabled = true;

            // Right Bottom: Long-Term Preferences Memory Card (y=446, h=147 -> bottom=593)
            float my = Card(590, 446, 302, 147, L("长期偏好记忆 (", "Long-term memories (") + st.Memories.Count + ")");
            float memH = Math.Max(66f, st.Memories.Count * 32f + 6f);
            voiceMemScroll = GUI.BeginScrollView(new Rect(600, my, 282, 74), voiceMemScroll, new Rect(0, 0, 260, memH));
            if (st.Memories.Count == 0)
                GUI.Label(new Rect(4, 6, 250, 46), L("暂无长期偏好。聊天时说「记住以后都这样」会自动存入。", "No saved preferences yet. Say \"remember this\" in chat to store one."), wrapSmall);
            else
                for (int i = 0; i < st.Memories.Count; i++)
                    GUI.Label(new Rect(4, i * 32f, 252, 30f), "• " + st.Memories[i], wrapSmall);
            GUI.EndScrollView();
            GUI.enabled = vOn && st.Connected && st.Memories.Count > 0;
            if (Button(604, my + 79, 274, 24, L("清空长期记忆", "Clear memories"), dangerButton))
                voiceClient.PostSimpleAsync("/memory", "{\"action\":\"clear\"}");
            GUI.enabled = true;
        }

        private void DrawManualDirectorPage()
        {
            bool inH = Scene != null && !Scene.isEnd && Flags != null;
            bool inserted = IsCurrentlyInserted();

            // Left Card: Rhythm Presets + Live Sliders + 6-Axis Quick Switches (y=95, h=498 -> bottom=593)
            float y = Card(18, 95, 438, 498, L("动作变式、实时参数与六轴手控", "Motion Variants, Live Params & 6-Axis Control") + (inserted ? L("（已插入）", " (Inserted)") : L("（未插入）", " (Not inserted)")));
            GUI.enabled = inH;
            if (!inserted)
            {
                GUI.Label(new Rect(32, y, 272, 22), L("当前尚未插入，插入专属变式已锁定：", "Not inserted; insertion variants locked:"), small);
                if (Button(312, y - 2, 130, 26, L("一键插入开始", "Insert now"), primaryButton)) TriggerQuickPreset("start_insert");
            }
            else
            {
                GUI.Label(new Rect(32, y, 410, 22), Timeline.Active ? UiText.TranslateComposite(Timeline.SummaryStatus()) : L("当前由游戏原生节奏驱动，可随时切换变式：", "Following native game rhythm; pick any variant:"), small);
            }

            GUI.enabled = inH && inserted;
            bool tlOn = Timeline != null && Timeline.Active;
            TimelinePreset ap = tlOn ? Timeline.ActivePreset : TimelinePreset.Custom;
            if (Button(32, y + 26, 98, 26, L("有机随机", "Organic"), tlOn && ap == TimelinePreset.Organic ? activeButton : button)) TriggerQuickPreset("organic");
            if (Button(136, y + 26, 98, 26, L("深埋扭腰", "Deep grind"), tlOn && ap == TimelinePreset.DeepGrind ? activeButton : button)) TriggerQuickPreset("deep_grind");
            if (Button(240, y + 26, 98, 26, L("九浅一深", "9S 1D"), tlOn && ap == TimelinePreset.NineShallowOneDeep ? activeButton : button)) TriggerQuickPreset("nine_shallow");
            if (Button(344, y + 26, 98, 26, L("浅逗入口", "Tip rub"), tlOn && ap == TimelinePreset.TipRub ? activeButton : button)) TriggerQuickPreset("tip_rub");

            if (Button(32, y + 57, 98, 26, L("1浅1深", "1S 1D"), tlOn && ap == TimelinePreset.NineShallowOneDeep ? activeButton : button)) TriggerQuickPreset("one_shallow_one_deep");
            if (Button(136, y + 57, 98, 26, L("定深直摆", "F/B sway"), tlOn && ap == TimelinePreset.WaistOnly ? activeButton : button)) TriggerQuickPreset("waist_line_fb");
            if (Button(240, y + 57, 98, 26, L("原生高潮", "Climax"), button)) TriggerQuickPreset("climax");
            GUI.enabled = inH;
            if (Button(344, y + 57, 98, 26, L("释放接管", "Release"), AnyDirectorActive ? dangerButton : button)) TriggerQuickPreset("release");

            double freq, dMin, dMax, gAmp, tAmp;
            Timeline.SnapshotActiveParams(out freq, out dMin, out dMax, out gAmp, out tAmp);

            GUI.enabled = inH && inserted;
            float sy = y + 90;
            DrawManualSlider(32, sy, L("动作频率 freqHz", "Frequency (freqHz)"), (float)freq, 0.4f, 3.2f, "0.00 Hz", v => ApplyManualTimelineTune(s => s.FreqHz = v));
            DrawManualSlider(32, sy + 36, L("最浅深度 depthMin", "Min depth (depthMin)"), (float)dMin, 0.0f, 0.90f, "0.00", v => ApplyManualTimelineTune(s => { s.DepthMin = v; if (s.DepthMax < v + 0.08) s.DepthMax = Math.Min(1.0, v + 0.08); }));
            DrawManualSlider(32, sy + 72, L("最深深度 depthMax", "Max depth (depthMax)"), (float)dMax, 0.15f, 1.0f, "0.00", v => ApplyManualTimelineTune(s => { s.DepthMax = v; if (s.DepthMin > v - 0.08) s.DepthMin = Math.Max(0.0, v - 0.08); }));
            DrawManualSlider(32, sy + 108, L("扭腰摆幅 grindAmp", "Grind amp (grindAmp)"), (float)gAmp, 0.0f, 1.0f, "0.00", v => ApplyManualTimelineTune(s => s.GrindAmp = v));
            DrawManualSlider(32, sy + 144, L("旋转幅度 twistAmp", "Twist amp (twistAmp)"), (float)tAmp, 0.0f, 1.0f, "0.00", v => ApplyManualTimelineTune(s => s.TwistAmp = v));

            GUI.Label(new Rect(32, sy + 184, 54, 22), L("轨迹：", "Path:"), small);
            if (Button(86, sy + 181, 54, 24, L("画圈", "Circle"))) ApplyManualTimelineTune(s => s.GrindPattern = GrindPattern.Circle);
            if (Button(143, sy + 181, 54, 24, L("8字", "Fig-8"))) ApplyManualTimelineTune(s => s.GrindPattern = GrindPattern.Figure8);
            if (Button(200, sy + 181, 76, 24, L("前后直线", "F/B line"))) ApplyManualTimelineTune(s => { s.GrindPattern = GrindPattern.LineFB; s.TwistAmp = 0.0; });
            if (Button(279, sy + 181, 76, 24, L("左右直线", "L/R line"))) ApplyManualTimelineTune(s => { s.GrindPattern = GrindPattern.LineLR; s.TwistAmp = 0.0; });
            if (Button(358, sy + 181, 84, 24, L("R1+R2直", "R1+R2"))) ApplyManualTimelineTune(s => { s.GrindPattern = GrindPattern.LineR1R2; s.TwistAmp = 0.0; });

            // Six-Axis Quick Controls
            GUI.enabled = true;
            float axY = sy + 216;
            Box(new Rect(32, axY - 4, 410, 1), Edge);
            for (int i = 0; i < 6; i++)
            {
                bool on = Drive.Enabled[i];
                if (Button(32 + i * 69, axY + 4, 64, 24, Axes[i] + (on ? L("开", " ON") : L("关", " OFF")), on ? activeButton : button))
                {
                    BeginDriveEdit("manual axis toggle");
                    Drive.Enabled[i] = !on;
                    axesDirty = true;
                    FinishDriveEdit();
                }
            }
            float curSm = smoothing != null ? smoothing.Value : 25f;
            DrawManualSlider(32, axY + 36, L("输出平滑度 smoothingMs", "Smoothing (ms)"), curSm, 8f, 80f, "0 ms", v =>
            {
                if (smoothing != null) { smoothing.Value = Mathf.Round(v); ApplyResponseSettings(); Config.Save(); }
            });
            GUI.Label(new Rect(32, axY + 78, 96, 22), L("原生游戏速：", "Native speed:"), small);
            GUI.enabled = inH;
            if (Button(132, axY + 75, 72, 24, L("慢 25%", "Slow 25%"))) DirectorSetSpeedLatch(0.25, 25.0);
            if (Button(210, axY + 75, 72, 24, L("中 55%", "Mid 55%"))) DirectorSetSpeedLatch(0.55, 25.0);
            if (Button(288, axY + 75, 72, 24, L("快 85%", "Fast 85%"))) DirectorSetSpeedLatch(0.85, 25.0);
            if (Button(366, axY + 75, 76, 24, L("原生自由", "Native"))) DirectorReleaseSpeedLatch();
            GUI.enabled = true;

            // Right Top: Pose + Clothes + Outfit + Expression (y=95, h=298 -> bottom=393)
            float ry = Card(468, 95, 424, 298, L("体位、服装半脱/单件与表情手控", "Poses, Clothing (Half / Piece) & Expressions"));
            GUI.enabled = inH;
            GUI.Label(new Rect(482, ry - 2, 396, 18), L("常用体位一键切换", "Quick pose switch"), small);
            if (Button(482, ry + 18, 76, 24, L("正常位", "Missionary"))) SwitchPoseByChineseHint("正常位");
            if (Button(562, ry + 18, 76, 24, L("后背位", "Doggy"))) SwitchPoseByChineseHint("后背位");
            if (Button(642, ry + 18, 76, 24, L("骑乘位", "Cowgirl"))) SwitchPoseByChineseHint("骑乘位");
            if (Button(722, ry + 18, 76, 24, L("面对面", "Face-to-face"))) SwitchPoseByChineseHint("面对面");
            if (Button(802, ry + 18, 76, 24, L("侧卧位", "Spooning"))) SwitchPoseByChineseHint("侧卧位");

            if (Button(482, ry + 46, 95, 24, L("爱抚上手", "Foreplay"))) UiEdit(() => DirectorGameAction("aibu_attach", null, null));
            if (Button(582, ry + 46, 95, 24, L("一键插入", "Insert"))) UiEdit(() => DirectorGameAction("start_insert", null, null));
            if (Button(682, ry + 46, 95, 24, L("原生内射", "Finish in"))) UiEdit(() => DirectorGameAction("finish_inside", null, null));
            if (Button(782, ry + 46, 96, 24, L("原生外射", "Finish out"))) UiEdit(() => DirectorGameAction("finish_outside", null, null));

            GUI.Label(new Rect(482, ry + 74, 396, 18), L("脱穿衣（全身半脱 / 精准单件半脱与全脱）", "Clothing (full / half / individual pieces)"), small);
            if (Button(482, ry + 94, 76, 24, L("全穿", "Dress all"))) UiEdit(() => DirectorSetClothes(0, "", "dress", -1));
            if (Button(562, ry + 94, 76, 24, L("全半脱", "Half all"), activeButton)) UiEdit(() => DirectorSetClothes(0, "", "half", -1));
            if (Button(642, ry + 94, 76, 24, L("全脱", "Undress"), dangerButton)) UiEdit(() => DirectorSetClothes(0, "", "undress", -1));
            if (Button(722, ry + 94, 76, 24, L("半脱上衣", "Half top"))) UiEdit(() => DirectorSetClothes(0, "", "half", 0));
            if (Button(802, ry + 94, 76, 24, L("脱上衣", "Off top"))) UiEdit(() => DirectorSetClothes(0, "", "undress", 0));

            if (Button(482, ry + 122, 76, 24, L("半脱内裤", "Half pty"))) UiEdit(() => DirectorSetClothes(0, "", "half", 3));
            if (Button(562, ry + 122, 76, 24, L("脱内裤", "Off pty"))) UiEdit(() => DirectorSetClothes(0, "", "undress", 3));
            if (Button(642, ry + 122, 76, 24, L("半脱胸罩", "Half bra"))) UiEdit(() => DirectorSetClothes(0, "", "half", 2));
            if (Button(722, ry + 122, 76, 24, L("脱胸罩", "Off bra"))) UiEdit(() => DirectorSetClothes(0, "", "undress", 2));
            if (Button(802, ry + 122, 76, 24, L("脱丝袜", "Off leg"))) UiEdit(() => DirectorSetClothes(0, "", "undress", 5));

            GUI.Label(new Rect(482, ry + 150, 396, 18), L("切换服装槽位", "Switch outfit slot"), small);
            string[] oLbl = { L("1校服", "1 Sch"), L("2二套", "2 Home"), L("3体操", "3 Gym"), L("4泳装", "4 Swim"), L("5社团", "5 Club"), L("6私服", "6 Cas"), L("7睡衣", "7 Sleep") };
            int curSlot = inH ? GetPrimaryFemaleOutfitSlot() : -1;
            for (int i = 0; i < 7; i++)
            {
                if (Button(482 + i * 57, ry + 170, 53, 24, oLbl[i], curSlot == i ? activeButton : button))
                    UiEdit(() => DirectorSwitchOutfit(0, "", i));
            }

            GUI.Label(new Rect(482, ry + 198, 396, 18), L("表情手控", "Expression control"), small);
            string[] eKeys = { "sweet_smile", "happy_trance", "shy_tease", "ecstasy_melt", "heart_trance", "climax" };
            string[] eLbls = { L("甜笑", "Smile"), L("微醺", "Trance"), L("害羞", "Shy"), L("融化", "Melt"), L("爱心", "Hearts"), L("高潮", "Climax") };
            for (int i = 0; i < eKeys.Length; i++)
            {
                if (Button(482 + i * 56, ry + 218, 52, 24, eLbls[i]))
                    if (Expressions != null) Expressions.SetTarget("both", eKeys[i]);
            }
            if (Button(818, ry + 218, 60, 24, L("还原", "Reset")))
                if (Expressions != null) Expressions.ReleaseAll();
            GUI.enabled = true;

            // Right Bottom: kPlug / ksPlug Character Invite / Swap / Masturbate (y=401, h=192 -> bottom=593)
            string plugName = PlugBrandName;
            float ky = Card(468, 401, 424, 192, string.Format(L("{0} 角色邀请、换人与副角色行为", "{0} Character Invite, Swap & Guest Actions"), plugName));
            GUI.enabled = inH;
            if (Button(482, ky, 146, 24, string.Format(L("打开 {0} 邀请面板", "Open {0} invite"), plugName), primaryButton))
                UiEdit(() => DirectorCharacterCommand("open_invite_panel", "", "", ""));
            if (Button(634, ky, 114, 24, L("关闭邀请面板", "Close invite")))
                UiEdit(() => DirectorCharacterCommand("close_invite_panel", "", "", ""));
            if (Button(754, ky, 124, 24, L("定位主角镜头", "Focus main")))
                UiEdit(() => DirectorCharacterCommand("focus", "", "", ""));

            manualCharQuery = Edit(482, ky + 30, 186, 26, manualCharQuery);
            if (Button(674, ky + 30, 100, 26, string.Format(L("{0} 邀请", "{0} invite"), plugName)))
                UiEdit(() => DirectorCharacterCommand("invite", manualCharQuery.Trim(), "", ""));
            if (Button(780, ky + 30, 98, 26, L("换主角身体", "Swap body")))
                UiEdit(() => DirectorCharacterCommand("replace_body", manualCharQuery.Trim(), "", ""));

            var guests = inH ? GetKPlugGuestSnapshots() : new System.Collections.Generic.List<HaremGuestSummary>();
            manualGuestScroll = GUI.BeginScrollView(new Rect(482, ky + 62, 396, 84), manualGuestScroll, new Rect(0, 0, 376, Math.Max(78f, guests.Count * 30f + 4f)));
            if (guests.Count == 0)
            {
                GUI.Label(new Rect(4, 6, 368, 44), string.Format(L("当前无 {0} 副角色在场。点上方「打开 {0} 邀请面板」或输入角色名邀请后，可在此控制自慰/换主角。", "No {0} guest characters present. Open the invite panel or type a name above to invite and control guests."), plugName), wrapSmall);
            }
            else
            {
                for (int i = 0; i < guests.Count; i++)
                {
                    var g = guests[i];
                    string gn = !string.IsNullOrEmpty(g.Name) ? g.Name : (L("角色", "Guest ") + (i + 1));
                    bool mast = g.Masturbating;
                    float gy = i * 30f + 2f;
                    GUI.Label(new Rect(2, gy + 3, 92, 22), gn, small);
                    if (Button(96, gy, 50, 24, L("定位", "Cam"))) UiEdit(() => DirectorCharacterCommand("focus", gn, "", ""));
                    if (Button(150, gy, 56, 24, L("切主角", "Swap"))) UiEdit(() => DirectorCharacterCommand("swap_main_guest", gn, "", ""));
                    if (Button(210, gy, 56, 24, mast ? L("停自慰", "Stop") : L("自慰", "Solo"), mast ? dangerButton : activeButton))
                        UiEdit(() => DirectorCharacterCommand("behavior", gn, "Lay", mast ? "stop_masturbate" : "masturbate"));
                    if (Button(270, gy, 50, 24, L("脱衣", "Strip"))) UiEdit(() => DirectorCharacterCommand("behavior", gn, "", "striptease"));
                    if (Button(324, gy, 48, 24, L("请离", "Leave"), dangerButton)) UiEdit(() => DirectorCharacterCommand("behavior", gn, "", "dismiss"));
                }
            }
            GUI.EndScrollView();
            GUI.enabled = true;
        }

        private void DrawManualSlider(float x, float y, string caption, float current, float minV, float maxV, string fmt, Action<float> onChange)
        {
            GUI.Label(new Rect(x, y, 290, 18), caption, small);
            GUI.Label(new Rect(x + 318, y, 92, 18), current.ToString(fmt, CultureInfo.InvariantCulture), small);
            float next = GUI.HorizontalSlider(new Rect(x, y + 18, 410, 15), current, minV, maxV);
            if (Math.Abs(next - current) >= (maxV - minV) * 0.015f)
                onChange(next);
        }

        private void ApplyManualTimelineTune(Action<TimelineStep> mutator)
        {
            if (!IsCurrentlyInserted()) return;
            if (Timeline.Active)
            {
                DirectorTuneTimeline(mutator, null);
            }
            else
            {
                // Use Custom (non-Organic) preset so explicit slider/pattern edits take effect immediately (fixes P1-4)
                var step = TimelineStep.FromPreset(TimelinePreset.Custom, 45.0, ActiveOuterCeilingL0);
                step.Loop = true;
                mutator(step);
                DirectorStartTimeline("手控微调", new[] { step }, true);
            }
        }

        private void SyncAutonomyToVoiceSidecar()
        {
            if (voiceClient == null) return;
            string json = string.Format(
                "{{\"autonomyMode\":\"{0}\",\"allowAutoPose\":{1},\"allowAutoClimaxEdge\":{2},\"allowAutoClothes\":{3},\"allowAutoKPlugSwap\":{4}}}",
                AutonomyMode ?? "player",
                AllowAutoPose ? "true" : "false",
                AllowAutoClimaxEdge ? "true" : "false",
                AllowAutoClothes ? "true" : "false",
                AllowAutoKPlugSwap ? "true" : "false");
            voiceClient.PostJsonFireAndForget("/autonomy", json);
        }

        private void ApplyMathLabParam(Action<SR6MathParams> mutator, bool autoStart = false)
        {
            mutator(mathLabUiParams);
            if (MathEngine.Active)
            {
                DirectorTuneMathLab(mutator);
            }
            else if (autoStart && IsDirectPenetrativeInserted())
            {
                UiEdit(() => DirectorStartMathLab(mathLabUiParams.Sanitized()));
            }
        }

        private void DrawMathLabAndAutonomyPage()
        {
            bool inH = Scene != null && !Scene.isEnd && Flags != null;
            bool directIns = inH && IsDirectPenetrativeInserted();
            bool mOn = MathEngine.Active;
            var curM = mOn ? MathEngine.CurrentParams : mathLabUiParams;

            // Left Panel: SR6 6-DOF Mathematical Limit Trajectory Lab (y=95, h=498 -> bottom=593)
            float y = Card(18, 95, 440, 498, L("SR6 六自由度数学极限实验台 · 双人骨骼与权重同步", "SR6 6-DOF Math Lab · Dual-Actor Skeleton & Physics"));
            GUI.Label(
                new Rect(32, y - 4, 412, 20),
                directIns
                    ? L("● 已处于直接性器官插入态（sonyu*）：5 大 6-DOF 数学流形已解锁", "● Direct insertion active (sonyu*): all 5 6-DOF manifolds unlocked")
                    : L("○ 仅限直接性器官插入态解锁（非插入态自动封锁以护骨护权重）", "○ Unlocks during direct insertion (auto-locked outside insertion)"),
                small);

            GUI.enabled = directIns;
            if (Button(32, y + 18, 132, 26, L("1.虚拟支点行波", "1.Pivot Wave"), mOn && curM.Family == SR6MathFamily.VirtualPivotWave ? activeButton : primaryButton))
            { mathLabUiParams.Family = SR6MathFamily.VirtualPivotWave; UiEdit(() => DirectorStartMathLab(mathLabUiParams.Sanitized())); }
            if (Button(170, y + 18, 132, 26, L("2.变螺距双螺旋", "2.Dual Helix"), mOn && curM.Family == SR6MathFamily.VariablePitchHelix ? activeButton : button))
            { mathLabUiParams.Family = SR6MathFamily.VariablePitchHelix; UiEdit(() => DirectorStartMathLab(mathLabUiParams.Sanitized())); }
            if (Button(308, y + 18, 134, 26, L("3.6D利萨如纽结", "3.6D Lissajous"), mOn && curM.Family == SR6MathFamily.LissajousKnot6D ? activeButton : button))
            { mathLabUiParams.Family = SR6MathFamily.LissajousKnot6D; UiEdit(() => DirectorStartMathLab(mathLabUiParams.Sanitized())); }

            if (Button(32, y + 48, 132, 26, L("4.拍频微颤共振", "4.Beat Flutter"), mOn && curM.Family == SR6MathFamily.BeatFlutterResonance ? activeButton : button))
            { mathLabUiParams.Family = SR6MathFamily.BeatFlutterResonance; UiEdit(() => DirectorStartMathLab(mathLabUiParams.Sanitized())); }
            if (Button(170, y + 48, 132, 26, L("5.平滑混沌流形", "5.Smooth Chaos"), mOn && curM.Family == SR6MathFamily.BoundedSmoothChaos ? activeButton : button))
            { mathLabUiParams.Family = SR6MathFamily.BoundedSmoothChaos; UiEdit(() => DirectorStartMathLab(mathLabUiParams.Sanitized())); }
            GUI.enabled = true;
            if (Button(308, y + 48, 134, 26, L("停止实验 / 还原", "Stop / Reset"), mOn ? dangerButton : button))
                DirectorStopMathLab(L("手动停止 SR6 数学实验台", "Stopped SR6 Math Lab"));

            GUI.enabled = directIns;
            float sy = y + 80;
            DrawManualSlider(32, sy, L("主频率 FreqHz", "Base freq (FreqHz)"), (float)curM.FreqHz, 0.25f, 3.0f, "0.00 Hz", v => ApplyMathLabParam(p => p.FreqHz = v));
            DrawManualSlider(32, sy + 34, L("虚拟支点高度 PivotZ (0根部..1顶端)", "Virtual pivot PivotZ (0 base..1 tip)"), (float)curM.PivotZ, 0.0f, 1.0f, "0.00", v => ApplyMathLabParam(p => { p.PivotZ = v; p.PivotSweepHz = 0.0; }));

            bool sweepOn = curM.PivotSweepHz > 0.01;
            if (Button(32, sy + 70, 148, 22, sweepOn ? L("● 行波扫描:开", "● Sweep: ON") : L("○ 行波扫描:关", "○ Sweep: OFF"), sweepOn ? activeButton : button))
                ApplyMathLabParam(p => p.PivotSweepHz = sweepOn ? 0.0 : 0.24);
            if (Button(186, sy + 70, 80, 22, L("锁根部", "Base"))) ApplyMathLabParam(p => { p.PivotZ = 0.05; p.PivotSweepHz = 0.0; });
            if (Button(270, sy + 70, 82, 22, L("锁中段", "Mid"))) ApplyMathLabParam(p => { p.PivotZ = 0.50; p.PivotSweepHz = 0.0; });
            if (Button(356, sy + 70, 86, 22, L("锁顶端", "Tip"))) ApplyMathLabParam(p => { p.PivotZ = 0.95; p.PivotSweepHz = 0.0; });

            DrawManualSlider(32, sy + 98, L("R0 螺旋扭转度 HelixTwist (±105°)", "R0 HelixTwist (±105°)"), (float)curM.HelixTwist, 0.0f, 1.0f, "0.00", v => ApplyMathLabParam(p => p.HelixTwist = v));
            DrawManualSlider(32, sy + 132, L("空间倾压幅度 OrbitAmp (L1/L2/R1/R2)", "OrbitAmp (L1/L2/R1/R2)"), (float)curM.OrbitAmp, 0.0f, 1.0f, "0.00", v => ApplyMathLabParam(p => p.OrbitAmp = v));

            float curShare = curM.FemaleWeightShare < 0 ? 0.50f : (float)curM.FemaleWeightShare;
            string shareCap = curM.FemaleWeightShare < 0
                ? L("双人骨骼权重 α_F (当前: 自动分摊)", "Actor split α_F (Auto)")
                : string.Format(CultureInfo.InvariantCulture, L("双人骨骼权重 α_F (女{0:0}% / 男{1:0}%)", "Actor split α_F (F {0:0}% / M {1:0}%)"), curShare * 100f, (1f - curShare) * 100f);
            DrawManualSlider(32, sy + 166, shareCap, curShare, 0.0f, 1.0f, "0.00", v => ApplyMathLabParam(p => p.FemaleWeightShare = v));
            if (Button(32, sy + 202, 96, 22, L("自动权重", "Auto"), curM.FemaleWeightShare < 0 ? activeButton : button)) ApplyMathLabParam(p => p.FemaleWeightShare = -1.0);
            if (Button(134, sy + 202, 100, 22, L("50/50共舞", "50/50"), curM.FemaleWeightShare >= 0.45 && curM.FemaleWeightShare <= 0.55 ? activeButton : button)) ApplyMathLabParam(p => p.FemaleWeightShare = 0.50);
            if (Button(240, sy + 202, 98, 22, L("85%女主导", "85% F"), curM.FemaleWeightShare >= 0.80 ? activeButton : button)) ApplyMathLabParam(p => p.FemaleWeightShare = 0.85);
            if (Button(344, sy + 202, 98, 22, L("85%男主导", "85% M"), curM.FemaleWeightShare >= 0.0 && curM.FemaleWeightShare <= 0.20 ? activeButton : button)) ApplyMathLabParam(p => p.FemaleWeightShare = 0.15);

            DrawManualSlider(32, sy + 230, L("软组织物理权重 (DynamicBone_Ver02 胸臀)", "Soft-tissue physics (DynamicBone_Ver02)"), (float)curM.SoftTissueWeight, 0.0f, 1.5f, "0.00", v => ApplyMathLabParam(p => p.SoftTissueWeight = v));
            GUI.enabled = true;

            // Live Quantitative Experiment Telemetry Box (ty = 481, h = 96 -> 577 <= 593)
            float ty = sy + 270;
            Box(new Rect(32, ty, 410, 96), new Color(.04f, .055f, .075f, 0.95f));
            string mStatus = mOn
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    L("运行中：{0}  ·  SR6可达域={1:0.00}  ·  支点 z_p={2:0.00}\n双人骨骼：{3} (女{4:0}% / 男{5:0}%)  ·  软体同步骨={6}\n安全指标：插口漂移={7:0.1}mm  ·  脚IK漂移={8:0.1}mm  ·  腰扭={9:0.0}°\n6轴：L0={10:0} L1={11:0} L2={12:0} R0={13:0} R1={14:0} R2={15:0}",
                      "Active: {0}  ·  Reach={1:0.00}  ·  Pivot z_p={2:0.00}\nRig: {3} (F {4:0}% / M {5:0}%)  ·  Soft bones={6}\nSafety: Entry drift={7:0.1}mm  ·  Foot IK={8:0.1}mm  ·  Yaw={9:0.0}°\n6-DOF: L0={10:0} L1={11:0} L2={12:0} R0={13:0} R1={14:0} R2={15:0}"),
                    UiText.T(SR6MathManifoldEngine.FamilyDisplayName(curM.Family)),
                    MathEngine.LastWorkspaceContract,
                    MathEngine.LastActivePivotZ,
                    BodyGrind != null ? BodyGrind.ResolvedMover : "None",
                    BodyGrind != null ? BodyGrind.FemaleShareApplied * 100f : 0f,
                    BodyGrind != null ? BodyGrind.MaleShareApplied * 100f : 0f,
                    BodyGrind != null ? BodyGrind.SoftTissueBonesSynced : 0,
                    BodyGrind != null ? BodyGrind.PrimaryEntryDriftMm : 0f,
                    BodyGrind != null ? BodyGrind.PrimaryFootDriftMm : 0f,
                    BodyGrind != null ? BodyGrind.PrimaryHipYawDeg : 0f,
                    Drive.Values[0] * 100.0, Drive.Values[1] * 100.0, Drive.Values[2] * 100.0,
                    Drive.Values[3] * 100.0, Drive.Values[4] * 100.0, Drive.Values[5] * 100.0)
                : L("实验台待机中。进入任意直接插入体位并完成插入后，点击上方 5 大数学流形即可无缝切换体验 SR6 极限轨迹与双人骨骼/软体物理同步。",
                    "Math Lab standby. Enter any direct insertion pose and insert, then pick one of the 5 manifolds above for live SR6 trajectories + dual-actor skeleton & soft-bone sync.");
            GUI.Label(new Rect(40, ty + 6, 394, 86), mStatus, wrapSmall);

            // Right Top Panel: Pure Female-Initiative Mode + 4 Independent Non-Mutually-Exclusive Permissions (y=95, h=240 -> bottom=335)
            string arbPlug = PlugBrandName;
            float ry = Card(468, 95, 424, 240, string.Format(L("纯女方主动模式 & {0} 防冲突仲裁", "Autonomy Mode & {0} Conflict Arbitration"), arbPlug));
            GUI.Label(new Rect(482, ry - 2, 396, 18), string.Format(L("互动主权模式（开启 AI MCP 时默认关闭 {0} 原生主动）：", "Control mode (pauses {0} auto-think while AI MCP is active):"), arbPlug), small);
            if (Button(482, ry + 18, 126, 26, L("听从主人主导", "Player lead"), AutonomyMode == "player" ? activeButton : button))
            { DirectorConfigureAutonomy("player", null, null, null, null); SyncAutonomyToVoiceSidecar(); }
            if (Button(614, ry + 18, 126, 26, L("协同提议模式", "Co-pilot"), AutonomyMode == "copilot" ? activeButton : button))
            { DirectorConfigureAutonomy("copilot", null, null, null, null); SyncAutonomyToVoiceSidecar(); }
            if (Button(746, ry + 18, 132, 26, L("纯女方主动模式", "AI initiative"), AutonomyMode == "female_active" ? primaryButton : button))
            { DirectorConfigureAutonomy("female_active", null, null, null, null); SyncAutonomyToVoiceSidecar(); }

            GUI.Label(new Rect(482, ry + 50, 396, 18), L("4 项独立权限开关（互不互斥，也可在聊天中口头授权）：", "4 independent permissions (can also be granted verbally):"), small);
            bool nPose = GUI.Toggle(new Rect(482, ry + 72, 192, 22), AllowAutoPose, L(" 允许自主换体位 / 插入", " Auto pose / insert"), toggle);
            bool nClim = GUI.Toggle(new Rect(682, ry + 72, 196, 22), AllowAutoClimaxEdge, L(" 允许自主控射与寸止", " Auto climax / edging"), toggle);
            bool nClth = GUI.Toggle(new Rect(482, ry + 98, 192, 22), AllowAutoClothes, L(" 允许自主脱衣与换装", " Auto clothes / outfits"), toggle);
            bool nSwap = GUI.Toggle(new Rect(682, ry + 98, 196, 22), AllowAutoKPlugSwap, string.Format(L(" 允许自主拉人/轮换角色", " Auto {0} invite / swap"), arbPlug), toggle);
            if (nPose != AllowAutoPose || nClim != AllowAutoClimaxEdge || nClth != AllowAutoClothes || nSwap != AllowAutoKPlugSwap)
            {
                DirectorConfigureAutonomy(null, nPose, nClim, nClth, nSwap);
                SyncAutonomyToVoiceSidecar();
            }

            string arbLine = IsKPlugLoaded
                ? (KPlugGirlThinkPaused
                    ? string.Format(L("● AI MCP 已接管：已默认关闭 {0} 原生主动思考，杜绝双大脑打架", "● AI MCP active: {0} native auto-think paused to prevent conflicts"), arbPlug)
                    : (DirectorListening ? string.Format(L("● AI MCP 已就绪（进入 H 场景即自动关闭 {0} 主动）", "● AI MCP ready (auto-pauses {0} auto-think in H-scene)"), arbPlug) : string.Format(L("○ AI MCP 已关闭，{0} 保持原设置", "○ AI MCP disabled; {0} unchanged"), arbPlug)))
                : string.Format(L("○ 当前未检测到 {0}（原生场景完整支持 3 档主权与 4 项权限）", "○ {0} not detected (all 3 modes & 4 permissions work natively)"), arbPlug);
            GUI.Label(new Rect(482, ry + 126, 396, 34), arbLine, wrapSmall);

            GUI.enabled = AutonomyMode != "player";
            if (Button(482, ry + 166, 192, 26, L("立刻触发一次女方主动决策", "Trigger AI beat now"), AutonomyMode != "player" ? primaryButton : button))
            {
                if (voiceClient != null) voiceClient.PostJsonFireAndForget("/autonomy/beat", "{}");
            }
            GUI.enabled = true;
            if (Button(682, ry + 166, 196, 26, L("打开网页完整控制台", "Open Web Console")))
                OpenVoiceConfigInBrowser("");

            // Right Bottom Panel: Native H-Event Bus Live Monitor (y=343, h=250 -> bottom=593)
            float ey = Card(468, 343, 424, 250, L("恋活原生 H 事件总线实时流", "Native H-Event Bus Live Stream"));
            var evs = EventBus.Recent(18);
            mathLabEventScroll = GUI.BeginScrollView(new Rect(482, ey, 396, 204), mathLabEventScroll, new Rect(0, 0, 376, Math.Max(194f, evs.Count * 26f + 6f)));
            if (evs.Count == 0)
            {
                GUI.Label(new Rect(4, 8, 368, 40), L("暂无事件。进入恋活 H 场景后，切体位、插入、高潮清零、衣物调节与玩家手动操作都会在此实时记录。", "No events yet. Pose switches, insertions, climaxes, clothing edits, and manual clicks appear here live."), wrapSmall);
            }
            else
            {
                for (int i = evs.Count - 1, row = 0; i >= 0; i--, row++)
                {
                    var ev = evs[i];
                    string line = string.Format("#{0} [{1}] {2} · {3}", ev.Id, ev.Type, ev.Stage, ev.Detail);
                    GUI.Label(new Rect(4, row * 26f + 2f, 370, 24f), line, small);
                }
            }
            GUI.EndScrollView();
        }

        private bool ShouldDrawQuickDock()
        {
            return Scene != null && !Scene.isEnd && Flags != null && showQuickDock != null && showQuickDock.Value;
        }

        private bool InsideQuickDock(Vector2 scaledPoint)
        {
            return ShouldDrawQuickDock() && dockRect.Contains(scaledPoint);
        }

        private void DrawHQuickDockOverlay()
        {
            if (!ShouldDrawQuickDock()) return;
            bool vOn = voiceEnabled != null && voiceEnabled.Value;
            float sw = Screen.width / uiScale;
            float sh = Screen.height / uiScale;

            float w = dockCollapsed ? 132f : 344f;
            float h = dockCollapsed ? 30f : (vOn ? (dockChatOpen ? 124f : 90f) : 56f);
            dockRect.width = w;
            dockRect.height = h;

            if (!dockPosInit)
            {
                dockPosInit = true;
                dockRect.x = Mathf.Max(16f, sw - 285f - w);
                dockRect.y = Mathf.Max(16f, sh - h - 22f);
            }
            dockRect.x = Mathf.Clamp(dockRect.x, 8f, Mathf.Max(8f, sw - w - 8f));
            dockRect.y = Mathf.Clamp(dockRect.y, 8f, Mathf.Max(8f, sh - h - 8f));

            if (vOn && Time.realtimeSinceStartup < hudToastUntil && !string.IsNullOrEmpty(hudToastReply))
            {
                float tw = 370f;
                float th = string.IsNullOrEmpty(hudToastMeta) ? 58f : 78f;
                float tx = Mathf.Clamp(dockRect.xMax - tw, 8f, Mathf.Max(8f, sw - tw - 8f));
                float ty = Mathf.Max(8f, dockRect.y - th - 6f);
                Box(new Rect(tx, ty, tw, th), Edge);
                Box(new Rect(tx + 1, ty + 1, tw - 2, th - 2), new Color(.05f, .07f, .10f, .92f));
                Box(new Rect(tx + 1, ty + 1, 3, th - 2), Accent);
                GUI.Label(new Rect(tx + 10, ty + 5, tw - 18, 46), "「" + hudToastReply + "」", wrapSmall);
                if (!string.IsNullOrEmpty(hudToastMeta))
                {
                    var oldCol = GUI.contentColor;
                    GUI.contentColor = Accent;
                    GUI.Label(new Rect(tx + 10, ty + 53, tw - 18, 20), L("已调：", "Applied: ") + hudToastMeta, small);
                    GUI.contentColor = oldCol;
                }
            }

            dockRect = GUI.Window(731932, dockRect, DrawQuickDockWindow, "", windowStyle);
        }

        private void DrawQuickDockWindow(int id)
        {
            bool vOn = voiceEnabled != null && voiceEnabled.Value;
            bool inserted = IsCurrentlyInserted();

            if (dockCollapsed)
            {
                if (Button(4, 3, 98, 24, L("展开导演", "Director"), activeButton)) dockCollapsed = false;
                GUI.Label(new Rect(106, 5, 24, 20), "⋮⋮", small);
                GUI.DragWindow(new Rect(0, 0, 132, 30));
                return;
            }

            Box(new Rect(1, 1, dockRect.width - 2, 20), HeaderFill);
            GUI.Label(new Rect(8, 2, 260, 18), L("⋮⋮ 快捷导演", "⋮⋮ Quick Director") + (inserted ? L(" · 已插入", " · Inserted") : L(" · 未插入(拖动可移)", " · Idle (drag)")), small);
            if (Button(274, 2, 42, 17, L("手控", "Ctrl"))) { showWindow = true; mainPage = 2; aiHubTab = 1; }
            if (Button(318, 2, 22, 17, "–")) dockCollapsed = true;

            TimelinePreset activeP = Timeline != null && Timeline.Active ? Timeline.ActivePreset : TimelinePreset.Custom;
            bool tlOn = Timeline != null && Timeline.Active;
            if (!inserted)
            {
                if (Button(6, 25, 118, 26, L("一键插入开始", "Insert now"), primaryButton)) TriggerQuickPreset("start_insert");
                GUI.enabled = false;
                Button(128, 25, 48, 26, L("有机", "Org"));
                Button(180, 25, 48, 26, L("深埋", "Deep"));
                Button(232, 25, 48, 26, L("高潮", "Peak"));
                GUI.enabled = true;
                if (Button(284, 25, 54, 26, L("释放", "Free"), AnyDirectorActive ? dangerButton : button)) TriggerQuickPreset("release");
            }
            else
            {
                if (Button(6, 25, 62, 26, L("有机", "Org"), tlOn && activeP == TimelinePreset.Organic ? activeButton : button)) TriggerQuickPreset("organic");
                if (Button(72, 25, 62, 26, L("深埋", "Deep"), tlOn && activeP == TimelinePreset.DeepGrind ? activeButton : button)) TriggerQuickPreset("deep_grind");
                if (Button(138, 25, 62, 26, L("九浅", "9S1D"), tlOn && activeP == TimelinePreset.NineShallowOneDeep ? activeButton : button)) TriggerQuickPreset("nine_shallow");
                if (Button(204, 25, 62, 26, L("高潮", "Peak"), tlOn && activeP == TimelinePreset.ClimaxEjaculation ? activeButton : button)) TriggerQuickPreset("climax");
                if (Button(270, 25, 68, 26, L("释放", "Free"), AnyDirectorActive ? dangerButton : button)) TriggerQuickPreset("release");
            }

            if (vOn && voiceClient != null)
            {
                var st = voiceClient.GetSnapshot();
                GUI.enabled = st.Connected;
                if (!st.VoiceInputEnabled)
                {
                    if (Button(6, 56, 122, 26, L("语音输入:关", "Voice: OFF"), button))
                        voiceClient.UpdateVoiceConfigAsync("voiceInputEnabled", true);
                }
                else if (st.FreeMicEnabled)
                {
                    if (Button(6, 56, 122, 26, st.Recording ? L("自由麦:听声中", "OpenMic: Live") : L("自由麦:开启", "OpenMic: ON"), activeButton))
                        voiceClient.UpdateVoiceConfigAsync("freeMicEnabled", false);
                }
                else
                {
                    string micLbl = !st.Connected ? L("语音离线", "Offline") : (voiceMicRecordingLocal ? L("松开发送", "Release") : (voiceClient.Busy ? L("思考中...", "Busy...") : L("说话(", "Talk(") + PttKeyLabel() + ")"));
                    if (Button(6, 56, 122, 26, micLbl, voiceMicRecordingLocal ? dangerButton : primaryButton))
                    {
                        if (voiceMicRecordingLocal) StopVoiceMicAndSend();
                        else StartVoiceMic();
                    }
                }
                if (Button(132, 56, 68, 26, dockChatOpen ? L("收起", "Hide") : L("打字", "Type"), dockChatOpen ? activeButton : button))
                    dockChatOpen = !dockChatOpen;
                if (Button(204, 56, 62, 26, st.SilentMode ? L("已静音", "Muted") : L("静音", "Mute"), st.SilentMode ? dangerButton : button))
                    voiceClient.UpdateVoiceConfigAsync("silentMode", !st.SilentMode);
                GUI.enabled = true;
                if (Button(270, 56, 68, 26, L("对话页", "Chat")))
                {
                    showWindow = true;
                    mainPage = 2;
                    aiHubTab = 0;
                }

                if (dockChatOpen)
                {
                    GUI.SetNextControlName("KoiSyncDockChatInput");
                    dockInputText = Edit(6, 88, 270, 28, dockInputText);
                    bool enter = Event.current.type == EventType.KeyDown &&
                                 (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) &&
                                 GUI.GetNameOfFocusedControl() == "KoiSyncDockChatInput";
                    bool canSend = st.Connected && !voiceClient.Busy;
                    GUI.enabled = canSend;
                    if (Button(280, 88, 58, 28, L("发送", "Send"), primaryButton) || (canSend && enter && dockInputText.Trim().Length > 0))
                    {
                        string msg = dockInputText;
                        dockInputText = "";
                        SendVoiceText(msg);
                        if (enter) Event.current.Use();
                    }
                    GUI.enabled = true;
                }
            }

            GUI.DragWindow(new Rect(0, 0, 272, 22));
        }
    }
}
