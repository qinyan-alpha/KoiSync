using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using KoiSync.Core;
using UnityEngine;
using OCI = global::Studio.ObjectCtrlInfo;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        private sealed class TimelineObjectDefault
        {
            internal object TreeNode;
            internal double FirstKeyTime;
            internal bool InitialVisible;
        }

        private sealed class FrozenCameraTrack
        {
            internal object Track;
            internal bool OriginalEnabled;
        }

        private sealed class EndOfFrameWork
        {
            internal int DelayFrames;
            internal Action Action;
        }

        private ConfigEntry<bool> directorEnabledSetting;
        private ConfigEntry<int> directorPortSetting;
        private StudioDirectorBridge directorBridge;
        private StudioDirectorApi directorApi;
        internal readonly StudioDirectorBook DirectorBook = new StudioDirectorBook();
        internal readonly StudioSegmentLoopController DirectorLoop = new StudioSegmentLoopController();

        private readonly List<TimelineObjectDefault> timelineObjectDefaults = new List<TimelineObjectDefault>();
        private readonly List<FrozenCameraTrack> frozenCameraTracks = new List<FrozenCameraTrack>();
        private readonly Queue<EndOfFrameWork> directorEndOfFrameQueue = new Queue<EndOfFrameWork>();
        private readonly object directorEndOfFrameLock = new object();

        private bool directorTransitionPending;
        private bool directorRecordingFullPass;
        private float directorRecordingReturnSpeed = 1f;
        private double directorLastUpdateTime;
        private string pendingDirectorLoadScenePath = "";
        internal string DirectorLastNote { get; private set; }

        internal bool DirectorListening { get { return directorBridge != null && directorBridge.Listening; } }
        internal int DirectorPort { get { return directorBridge != null && directorBridge.Listening ? directorBridge.Port : (directorPortSetting != null ? directorPortSetting.Value : 41290); } }
        internal string DirectorScenePath { get { return scenePath ?? ""; } }
        internal string DirectorScriptStem { get { return scriptStem ?? ""; } }
        internal string DirectorRecordedPath { get { return recordedPath ?? ""; } }
        internal bool DirectorIsRecording { get { return recording; } }
        internal int DirectorRecordedFrames { get { return recorder != null ? recorder.Count : 0; } }
        internal bool DirectorAutoAnalysis { get { return autoCameraPairs; } }
        internal bool DirectorSendOutput { get { return sendOutput; } set { sendOutput = value; if (!sendOutput && Devices != null) Devices.Stop(); } }
        internal bool DirectorClimaxActive { get { return ClimaxEffectActive; } }
        internal bool DirectorUsingScripts { get { return UseScripts; } }
        internal double[] DirectorOutputSnapshot { get { return (double[])outputValues.Clone(); } }
        internal string[] DirectorActorNamesSnapshot { get { return (string[])actorNames.Clone(); } }
        internal string DirectorPlanFilePath { get { return string.IsNullOrEmpty(scriptStem) ? "" : scriptStem + ".director.json"; } }

        internal void DirectorSetUiState(bool? show, int? tab)
        {
            if (show.HasValue)
            {
                showWindow = show.Value;
                if (showWindow) { guiVisible = true; mainPage = 1; workbenchCollapsed = false; }
            }
            if (tab.HasValue && tab.Value >= 0 && tab.Value <= 3)
            {
                mainPage = 1;
                workbenchCollapsed = false;
                SetWorkbenchPage(tab.Value);
            }
        }

        internal void DirectorConfigureChapter(int index, int? sourceKey, int? targetKey, int? opening, string phase, string climaxMode, double? climaxHz, double? climaxMin, double? climaxMax, string customName, int? grindPattern = null, double? grindIntensity = null, double? grindHz = null, int? grindMover = null, bool? grindLinkSkeleton = null, bool? grindSoftTissue = null, bool? grindDepthWeighted = null)
        {
            if (index < 0 || index >= Book.Chapters.Count)
                throw new ArgumentOutOfRangeException("index");
            RememberChapters();
            var ch = Book.Chapters[index];
            if (sourceKey.HasValue) ch.Source = sourceKey.Value;
            if (targetKey.HasValue) ch.Target = targetKey.Value;
            if (opening.HasValue && opening.Value >= 0 && opening.Value < OpeningNames.Length) ch.Opening = opening.Value;
            if (!string.IsNullOrEmpty(customName)) ch.CustomName = customName;
            if (!string.IsNullOrEmpty(phase))
            {
                string p = phase.ToLowerInvariant();
                if (p == "climax") { ch.Phase = ChapterPhase.Climax; ch.ClimaxDetected = true; }
                else if (p == "normal") { ch.Phase = ChapterPhase.Normal; ch.ClimaxDetected = false; }
                else if (p == "auto" || p == "automatic") ch.Phase = ChapterPhase.Automatic;
                ch.EffectsEdited = true;
            }
            if (!string.IsNullOrEmpty(climaxMode) || climaxHz.HasValue || climaxMin.HasValue || climaxMax.HasValue)
            {
                var opt = ChapterEffectOptions(ch);
                opt.LocalSettings = true;
                if (!string.IsNullOrEmpty(climaxMode))
                {
                    string m = climaxMode.ToLowerInvariant();
                    if (m == "fixed") opt.FrequencyMode = ClimaxFrequencyMode.Fixed;
                    else if (m == "simulated" || m == "sim") opt.FrequencyMode = ClimaxFrequencyMode.Simulated;
                    else if (m == "live" || m == "livel0") opt.FrequencyMode = ClimaxFrequencyMode.LiveL0;
                    else opt.FrequencyMode = ClimaxFrequencyMode.AutoMotion;
                }
                if (climaxHz.HasValue) opt.Hertz = Numbers.Clamp(climaxHz.Value, 0.1, 20.0);
                if (climaxMin.HasValue) opt.Minimum = Numbers.Clamp(climaxMin.Value, 0.0, 999.0);
                if (climaxMax.HasValue) opt.Maximum = Numbers.Clamp(climaxMax.Value, opt.Minimum, 999.0);
                opt.Validate();
                ch.Climax = opt;
                ch.EffectsEdited = true;
            }
            if (grindPattern.HasValue || grindIntensity.HasValue || grindHz.HasValue || grindMover.HasValue || grindLinkSkeleton.HasValue || grindSoftTissue.HasValue || grindDepthWeighted.HasValue)
            {
                var g = ch.Grind != null ? ch.Grind.Copy() : new ChapterGrindOptions();
                if (grindPattern.HasValue) g.Pattern = (StudioGrindPattern)grindPattern.Value;
                if (grindIntensity.HasValue) g.Intensity = grindIntensity.Value;
                if (grindHz.HasValue) g.Hertz = grindHz.Value;
                if (grindMover.HasValue) g.MoverMode = (ActiveMoverMode)grindMover.Value;
                if (grindLinkSkeleton.HasValue) g.LinkSkeleton = grindLinkSkeleton.Value;
                if (grindSoftTissue.HasValue) g.SoftTissue = grindSoftTissue.Value;
                if (grindDepthWeighted.HasValue) g.DepthWeighted = grindDepthWeighted.Value;
                g.Validate();
                ch.Grind = g.Enabled ? g : null;
                ch.EffectsEdited = true;
            }
            ch.Manual = true;
            ch.NeedsReview = false;
            MarkSceneDataDirty();
            SaveSceneData();
            RefreshActiveClimaxPulse();
            DirectorLastNote = "Configured chapter #" + (index + 1);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void InitializeDirector()
        {
            DirectorLastNote = "";
            directorEnabledSetting = Config.Bind("Director", "Enabled", true, "Enable local loopback AI Director HTTP/MCP bridge (127.0.0.1).");
            directorPortSetting = Config.Bind("Director", "Port", 41290, new ConfigDescription("Loopback port for Studio AI Director bridge (default 41290; MainGame uses 41289).", new AcceptableValueRange<int>(1024, 65535)));

            directorApi = new StudioDirectorApi(this);
            directorBridge = new StudioDirectorBridge(req => directorApi.Handle(req), msg => Logger.LogInfo(msg));
            if (directorEnabledSetting.Value)
                directorBridge.Start(directorPortSetting.Value);
        }

        private void ShutdownDirector()
        {
            RestoreFrozenCameraTracks();
            DirectorLoop.Reset();
            DirectorBook.Clear();
            timelineObjectDefaults.Clear();
            lock (directorEndOfFrameLock) directorEndOfFrameQueue.Clear();
            if (directorBridge != null)
            {
                directorBridge.Dispose();
                directorBridge = null;
            }
        }

        private void ResetDirectorForSceneChange()
        {
            RestoreFrozenCameraTracks();
            DirectorLoop.Reset();
            DirectorBook.Clear();
            timelineObjectDefaults.Clear();
            directorRecordingFullPass = false;
            directorTransitionPending = false;
            DirectorLastNote = "Scene changing";
        }

        private void OnDirectorSceneReady()
        {
            CaptureTimelineObjectDefaults();
            ReloadDirectorPlanFromDisk();
        }

        private void UpdateDirector()
        {
            if (directorBridge != null)
                directorBridge.Pump(6);

            if (!string.IsNullOrEmpty(pendingDirectorLoadScenePath))
            {
                string nextPath = pendingDirectorLoadScenePath;
                pendingDirectorLoadScenePath = "";
                try
                {
                    var studio = global::Studio.Studio.Instance;
                    var coroMethod = typeof(global::Studio.Studio).GetMethod("LoadSceneCoroutine", RuntimeAccess.Flags, null, new[] { typeof(string) }, null);
                    if (studio != null && coroMethod != null)
                    {
                        var enumerator = coroMethod.Invoke(studio, new object[] { nextPath }) as IEnumerator;
                        if (enumerator != null)
                        {
                            StartCoroutine(enumerator);
                            DirectorLastNote = "Loading scene: " + Path.GetFileName(nextPath);
                            return;
                        }
                    }
                    var method = typeof(global::Studio.Studio).GetMethod("LoadScene", RuntimeAccess.Flags, null, new[] { typeof(string) }, null);
                    if (studio != null && method != null)
                    {
                        method.Invoke(studio, new object[] { nextPath });
                        DirectorLastNote = "Loaded scene: " + Path.GetFileName(nextPath);
                    }
                }
                catch (Exception ex)
                {
                    DirectorLastNote = "LoadScene failed: " + ex.GetBaseException().Message;
                    Logger.LogWarning(DirectorLastNote);
                }
                return;
            }

            double now = Time.realtimeSinceStartup;
            double dt = directorLastUpdateTime > 0 ? Math.Max(0, Math.Min(0.25, now - directorLastUpdateTime)) : 1.0 / 60.0;
            directorLastUpdateTime = now;

            if (scenePending || !Clock.Available) return;

            // Auto-complete full-pass recording when Timeline reaches the end
            if (directorRecordingFullPass)
            {
                if (!recording)
                {
                    directorRecordingFullPass = false;
                    Clock.SetSpeed(directorRecordingReturnSpeed);
                }
                else if (Clock.Duration > 0 && Clock.Time >= Clock.Duration - 0.05)
                {
                    FinishRecording();
                    Clock.Pause();
                    Clock.SetSpeed(directorRecordingReturnSpeed);
                    directorRecordingFullPass = false;
                    DirectorLastNote = "Full-pass recording completed";
                }
                return;
            }

            if (DirectorLoop.Mode != StudioPlaybackMode.Linear)
            {
                var tick = DirectorLoop.Tick(Clock.Time, Clock.Playing, dt);
                ApplyDirectorLoopTick(tick);
            }
        }

        private void RunDirectorEndOfFrame()
        {
            int count;
            lock (directorEndOfFrameLock) count = directorEndOfFrameQueue.Count;
            for (int i = 0; i < count; i++)
            {
                EndOfFrameWork work = null;
                lock (directorEndOfFrameLock)
                {
                    if (directorEndOfFrameQueue.Count > 0) work = directorEndOfFrameQueue.Dequeue();
                }
                if (work == null) break;
                if (work.DelayFrames > 0)
                {
                    work.DelayFrames--;
                    lock (directorEndOfFrameLock) directorEndOfFrameQueue.Enqueue(work);
                    continue;
                }
                try { work.Action(); }
                catch (Exception ex) { Logger.LogWarning("Studio director EndOfFrame work failed: " + ex.GetBaseException().Message); }
            }
        }

        internal void EnqueueDirectorEndOfFrame(Action action, int delayFrames = 0)
        {
            if (action == null) return;
            lock (directorEndOfFrameLock)
                directorEndOfFrameQueue.Enqueue(new EndOfFrameWork { Action = action, DelayFrames = Math.Max(0, delayFrames) });
        }

        private bool ConsumeDirectorTransitionBoundary()
        {
            if (!directorTransitionPending) return false;
            directorTransitionPending = false;
            return true;
        }

        internal StudioDirectorBook GetEffectiveDirectorBook()
        {
            if (DirectorBook.Segments.Count > 0) return DirectorBook;
            double dur = Clock.Available ? Clock.Duration : 0;
            string name = string.IsNullOrEmpty(scenePath) ? "" : Path.GetFileNameWithoutExtension(scenePath);
            return StudioDirectorBook.BuildFallbackFromChapters(Book, dur, name);
        }

        internal bool ReloadDirectorPlanFromDisk()
        {
            string path = DirectorPlanFilePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || directorApi == null)
            {
                DirectorBook.Clear();
                return false;
            }
            try
            {
                string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
                double dur = Clock.Available ? Clock.Duration : 0;
                int loaded = directorApi.LoadPlanJson(json, dur);
                DirectorLastNote = "Loaded director plan (" + loaded + " segments)";
                Logger.LogInfo("KoiSync Studio: " + DirectorLastNote + " from " + path);
                return loaded > 0;
            }
            catch (Exception ex)
            {
                DirectorLastNote = "Director plan load failed: " + ex.GetBaseException().Message;
                Logger.LogWarning(DirectorLastNote);
                return false;
            }
        }

        internal void SaveDirectorPlanJson(string jsonText)
        {
            string path = DirectorPlanFilePath;
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("请先加载或保存 Studio 场景卡，以确定 .director.json 保存路径");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path + ".tmp", jsonText, new System.Text.UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(path + ".tmp", path, null);
            else File.Move(path + ".tmp", path);
            DirectorLastNote = "Saved director plan (" + DirectorBook.Segments.Count + " segments)";
        }

        internal void DirectorSafeSeek(double targetTime, bool playAfterSeek, bool triggerFade = true)
        {
            if (!Clock.Available) throw new InvalidOperationException("Timeline 尚未就绪");
            double clamped = Math.Max(0, Math.Min(Clock.Duration, targetTime));
            if (triggerFade) directorTransitionPending = true;
            RestoreTimelineObjectsBefore(clamped);
            Clock.Seek(clamped);
            Clock.RefreshPose();
            if (playAfterSeek) Clock.Play();
        }

        internal void DirectorPlaySegment(string query, bool loop, double speed, bool freezeCamera, double maxDurationSeconds, string nextQuery, bool pauseAtEnd)
        {
            if (!Clock.Available) throw new InvalidOperationException("Timeline 尚未就绪");
            if (recording) FinishRecording();
            directorRecordingFullPass = false;

            var effective = GetEffectiveDirectorBook();
            var seg = effective.Find(query);
            if (seg == null) throw new ArgumentException("未找到匹配的分段：" + query);

            StudioDirectorSegment nextSeg = null;
            if (!string.IsNullOrEmpty(nextQuery))
                nextSeg = effective.Find(nextQuery);

            var tick = DirectorLoop.StartSegment(seg, loop, speed, freezeCamera, maxDurationSeconds, nextSeg, pauseAtEnd);
            SetCameraTracksFrozen(freezeCamera);
            ApplyDirectorLoopTick(tick);
        }

        internal void DirectorGotoClimax(string explicitQuery, double speed, string nextQuery, bool pauseAtEnd)
        {
            if (!Clock.Available) throw new InvalidOperationException("Timeline 尚未就绪");
            if (recording) FinishRecording();
            directorRecordingFullPass = false;

            var effective = GetEffectiveDirectorBook();
            StudioDirectorSegment climax = !string.IsNullOrEmpty(explicitQuery)
                ? effective.Find(explicitQuery)
                : effective.FindClimax();
            if (climax == null)
                throw new InvalidOperationException("当前场景未标记高潮分段（可先运行 studio_auto_segment.py 或在章节编辑器中标记高潮）");

            StudioDirectorSegment nextSeg = null;
            if (!string.IsNullOrEmpty(nextQuery))
                nextSeg = effective.Find(nextQuery);
            else
                nextSeg = effective.Find("afterglow");

            var tick = DirectorLoop.StartSegment(climax, false, speed, false, 0, nextSeg, pauseAtEnd);
            SetCameraTracksFrozen(false);
            ApplyDirectorLoopTick(tick);
        }

        internal void DirectorSetSpeed(double speed)
        {
            if (!Clock.Available) throw new InvalidOperationException("Timeline 尚未就绪");
            var tick = DirectorLoop.SetSpeed(speed);
            ApplyDirectorLoopTick(tick);
        }

        internal void DirectorReleaseLinear(bool resumePlay, double speed)
        {
            RestoreFrozenCameraTracks();
            var tick = DirectorLoop.ReleaseLinear(resumePlay, speed);
            if (Clock.Available) ApplyDirectorLoopTick(tick);
            else DirectorLastNote = tick.EventNote;
        }

        internal void DirectorStartRecording(bool fullPass, double speed, bool enableAnalysis)
        {
            if (!Clock.Available) throw new InvalidOperationException("Timeline 尚未就绪");
            if (string.IsNullOrEmpty(scriptStem)) throw new InvalidOperationException("请先加载场景卡，再开始录制");
            if (DirectorLoop.Mode != StudioPlaybackMode.Linear)
                DirectorReleaseLinear(false, 1.0);

            if (enableAnalysis && !autoCameraPairs)
                SetAutomaticGrouping(true);
            if (actors.Count < 2) ReadActors();
            if (Binding == null && !autoCameraPairs && actors.Count >= 2) BindPair();

            if (fullPass)
            {
                directorRecordingReturnSpeed = (float)Math.Max(0.25, Math.Min(2.5, Clock.Speed > 0 ? Clock.Speed : 1.0));
                Clock.Pause();
                DirectorSafeSeek(0, false, false);
                BeginRecording();
                if (!recording) throw new InvalidOperationException(scriptsStatus);
                directorRecordingFullPass = true;
                Clock.SetSpeed((float)Numbers.Clamp(Numbers.Finite(speed) && speed > 0 ? speed : 1.0, 0.25, 2.5));
                Clock.Play();
                DirectorLastNote = "Full-pass 6-axis recording started from 0.0s";
            }
            else
            {
                BeginRecording();
                if (!recording) throw new InvalidOperationException(scriptsStatus);
                if (Numbers.Finite(speed) && speed > 0) Clock.SetSpeed((float)Numbers.Clamp(speed, 0.25, 2.5));
                Clock.Play();
                DirectorLastNote = "Recording started at " + Clock.Time.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "s";
            }
        }

        internal void DirectorStopRecording()
        {
            directorRecordingFullPass = false;
            if (recording) FinishRecording();
            if (Clock.Available) Clock.Pause();
            DirectorLastNote = "Recording stopped";
        }

        internal void DirectorTriggerAutoChapters()
        {
            if (!Clock.Available) throw new InvalidOperationException("Timeline 尚未就绪");
            if (!autoCameraPairs) SetAutomaticGrouping(true);
            RebuildAutomaticChapters();
            CaptureTimelineObjectDefaults();
            DirectorLastNote = "Rebuilt automatic attachment chapters (" + Book.Chapters.Count + ")";
        }

        internal void DirectorLoadScene(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path is required");
            string full = Path.GetFullPath(path.Trim());
            if (!File.Exists(full)) throw new ArgumentException("场景卡文件不存在：" + full);
            if (!string.Equals(Path.GetExtension(full), ".png", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Studio 场景卡必须是 .png 文件");
            var studio = global::Studio.Studio.Instance;
            if (studio == null) throw new InvalidOperationException("CharaStudio 实例尚未就绪");

            if (recording) FinishRecording();
            if (DirectorLoop.Mode != StudioPlaybackMode.Linear) DirectorReleaseLinear(false, 1.0);
            CancelStepBake();
            if (Clock.Available && Clock.Playing) Clock.Pause();
            if (Clock.Available) Clock.SetSpeed(1.0f);

            var method = typeof(global::Studio.Studio).GetMethod("LoadScene", RuntimeAccess.Flags, null, new[] { typeof(string) }, null);
            if (method == null) throw new InvalidOperationException("Studio.LoadScene 接口不可用");
            pendingDirectorLoadScenePath = full;
            DirectorLastNote = "Queued scene load: " + Path.GetFileName(full);
        }

        private void ApplyDirectorLoopTick(StudioLoopTickResult tick)
        {
            if (tick == null) return;
            if (!string.IsNullOrEmpty(tick.EventNote)) DirectorLastNote = tick.EventNote;
            if (tick.TriggerTransitionFade) directorTransitionPending = true;
            if (tick.NeedSpeed && Clock.CanSetSpeed) Clock.SetSpeed(tick.TargetSpeed);
            if (tick.NeedSeek)
            {
                RestoreTimelineObjectsBefore(tick.SeekTime);
                Clock.Seek(tick.SeekTime);
                Clock.RefreshPose();
            }
            if (tick.NeedPause)
            {
                Clock.Pause();
                if (DirectorLoop.Mode == StudioPlaybackMode.Linear) RestoreFrozenCameraTracks();
            }
            else if (tick.NeedPlay)
            {
                Clock.Play();
            }
            if (DirectorLoop.Mode == StudioPlaybackMode.Linear && !DirectorLoop.FreezeCamera && frozenCameraTracks.Count > 0)
                RestoreFrozenCameraTracks();
        }

        // Snapshot t=0 visibility of all Timeline objectEnabled tracks so jumping backwards
        // from climax segments cleanly hides props whose first keyframe is later than targetTime.
        private void CaptureTimelineObjectDefaults()
        {
            timelineObjectDefaults.Clear();
            try
            {
                var tlType = RuntimeAccess.Find("Timeline.Timeline");
                var self = RuntimeAccess.Read(tlType, "_self");
                var tracks = RuntimeAccess.Read(self, "_interpolables") as IDictionary;
                if (tracks == null) return;

                foreach (DictionaryEntry row in tracks)
                {
                    object track = row.Value;
                    if (track == null || !RuntimeAccess.Flag(track, "enabled")) continue;
                    if (!string.Equals(Convert.ToString(RuntimeAccess.Read(track, "id")), "objectEnabled", StringComparison.Ordinal)) continue;
                    var oci = RuntimeAccess.Read(track, "oci") as OCI;
                    if (oci == null || oci.treeNodeObject == null) continue;
                    var keys = RuntimeAccess.Read(track, "keyframes") as IDictionary;
                    if (keys == null || keys.Count == 0) continue;

                    double minTime = double.MaxValue;
                    foreach (DictionaryEntry kv in keys)
                    {
                        double kt = Convert.ToDouble(kv.Key);
                        if (kt < minTime) minTime = kt;
                    }
                    if (minTime <= 0.05) continue; // Track already defines its state at t=0.

                    bool initialVisible = RuntimeAccess.Flag(oci.treeNodeObject, "visible");
                    timelineObjectDefaults.Add(new TimelineObjectDefault
                    {
                        TreeNode = oci.treeNodeObject,
                        FirstKeyTime = minTime,
                        InitialVisible = initialVisible
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("CaptureTimelineObjectDefaults: " + ex.GetBaseException().Message);
            }
        }

        private void RestoreTimelineObjectsBefore(double targetTime)
        {
            if (timelineObjectDefaults.Count == 0) return;
            for (int i = 0; i < timelineObjectDefaults.Count; i++)
            {
                var item = timelineObjectDefaults[i];
                if (item.TreeNode == null || item.FirstKeyTime <= targetTime + 0.001) continue;
                try
                {
                    var nodeType = item.TreeNode.GetType();
                    var setVis = nodeType.GetMethod("SetVisible", RuntimeAccess.Flags, null, new[] { typeof(bool) }, null);
                    if (setVis != null)
                        setVis.Invoke(item.TreeNode, new object[] { item.InitialVisible });
                    else
                    {
                        var prop = nodeType.GetProperty("visible", RuntimeAccess.Flags);
                        if (prop != null && prop.CanWrite) prop.SetValue(item.TreeNode, item.InitialVisible, null);
                    }
                }
                catch { }
            }
        }

        private void SetCameraTracksFrozen(bool freeze)
        {
            if (!freeze)
            {
                RestoreFrozenCameraTracks();
                return;
            }
            if (frozenCameraTracks.Count > 0) return;
            try
            {
                var tlType = RuntimeAccess.Find("Timeline.Timeline");
                var self = RuntimeAccess.Read(tlType, "_self");
                var tracks = RuntimeAccess.Read(self, "_interpolables") as IDictionary;
                if (tracks == null) return;

                foreach (DictionaryEntry row in tracks)
                {
                    object track = row.Value;
                    if (track == null) continue;
                    string id = (Convert.ToString(RuntimeAccess.Read(track, "id")) ?? "").ToLowerInvariant();
                    string owner = (Convert.ToString(RuntimeAccess.Read(track, "owner")) ?? "").ToLowerInvariant();
                    if (!id.Contains("camera") && owner != "camera") continue;
                    bool orig = RuntimeAccess.Flag(track, "enabled");
                    if (!orig) continue;
                    if (TrySetTrackEnabled(track, false))
                        frozenCameraTracks.Add(new FrozenCameraTrack { Track = track, OriginalEnabled = orig });
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("SetCameraTracksFrozen: " + ex.GetBaseException().Message);
            }
        }

        private void RestoreFrozenCameraTracks()
        {
            if (frozenCameraTracks.Count == 0) return;
            for (int i = 0; i < frozenCameraTracks.Count; i++)
            {
                try { TrySetTrackEnabled(frozenCameraTracks[i].Track, frozenCameraTracks[i].OriginalEnabled); }
                catch { }
            }
            frozenCameraTracks.Clear();
        }

        private static bool TrySetTrackEnabled(object track, bool enabled)
        {
            if (track == null) return false;
            var t = track.GetType();
            var prop = t.GetProperty("enabled", RuntimeAccess.Flags);
            if (prop != null && prop.CanWrite && prop.PropertyType == typeof(bool))
            {
                prop.SetValue(track, enabled, null);
                return true;
            }
            var field = t.GetField("enabled", RuntimeAccess.Flags);
            if (field != null && field.FieldType == typeof(bool))
            {
                field.SetValue(track, enabled);
                return true;
            }
            return false;
        }

        internal byte[] DirectorCaptureScreenshot(int width, int height, string explicitSavePath, out string filePath, string cameraMode = "main", bool captureUi = false)
        {
            if (captureUi)
            {
                int sw = Screen.width, sh = Screen.height;
                var screenTex = new Texture2D(sw, sh, TextureFormat.RGB24, false);
                try
                {
                    screenTex.ReadPixels(new Rect(0, 0, sw, sh), 0, 0);
                    screenTex.Apply(false, false);
                    byte[] png = screenTex.EncodeToPNG();
                    filePath = ResolveScreenshotPath(explicitSavePath);
                    File.WriteAllBytes(filePath, png);
                    return png;
                }
                finally { Destroy(screenTex); }
            }

            var cam = Camera.main;
            if (cam == null) throw new InvalidOperationException("当前 Studio 场景无主摄像机");
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            Vector3 origPos = cam.transform.position;
            Quaternion origRot = cam.transform.rotation;
            float origFov = cam.fieldOfView;
            try
            {
                string mode = (cameraMode ?? "main").ToLowerInvariant();
                if ((mode == "crotch_side" || mode == "wide_front") && TryResolveContactFocus(out Vector3 focus, out Vector3 rightAxis, out Vector3 fwdAxis))
                {
                    if (mode == "crotch_side")
                    {
                        cam.transform.position = focus + rightAxis * 0.42f + fwdAxis * 0.24f + Vector3.up * 0.14f;
                        cam.transform.LookAt(focus, Vector3.up);
                        cam.fieldOfView = 42f;
                    }
                    else
                    {
                        cam.transform.position = focus - fwdAxis * 0.85f + Vector3.up * 0.22f + rightAxis * 0.18f;
                        cam.transform.LookAt(focus + Vector3.up * 0.08f, Vector3.up);
                        cam.fieldOfView = 48f;
                    }
                }
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply(false, false);
                byte[] png = tex.EncodeToPNG();

                filePath = ResolveScreenshotPath(explicitSavePath);
                File.WriteAllBytes(filePath, png);
                return png;
            }
            finally
            {
                cam.transform.position = origPos;
                cam.transform.rotation = origRot;
                cam.fieldOfView = origFov;
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                Destroy(tex);
                rt.Release();
                Destroy(rt);
            }
        }

        private string ResolveScreenshotPath(string explicitSavePath)
        {
            if (!string.IsNullOrEmpty(explicitSavePath))
            {
                string full = Path.GetFullPath(explicitSavePath);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                return full;
            }
            string baseDir = !string.IsNullOrEmpty(scriptStem)
                ? Path.GetDirectoryName(scriptStem)
                : Path.Combine(BepInEx.Paths.GameRootPath, "UserData/KoiSyncStudio/Director");
            string dir = Path.Combine(baseDir, "DirectorFrames");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "latest_studio_view.png");
        }

        private bool TryResolveContactFocus(out Vector3 focus, out Vector3 rightAxis, out Vector3 fwdAxis)
        {
            focus = Vector3.zero; rightAxis = Vector3.right; fwdAxis = Vector3.forward;
            if (Binding != null && Binding.Root != null && Binding.Opening != null)
            {
                focus = Vector3.Lerp(Binding.Root.position, Binding.Opening.position, 0.55f);
                var cam = Camera.main;
                if (cam != null)
                {
                    rightAxis = cam.transform.right; rightAxis.y = 0f;
                    if (rightAxis.sqrMagnitude < 1e-4f) rightAxis = Vector3.right; else rightAxis.Normalize();
                    fwdAxis = cam.transform.forward; fwdAxis.y = 0f;
                    if (fwdAxis.sqrMagnitude < 1e-4f) fwdAxis = Vector3.forward; else fwdAxis.Normalize();
                }
                return true;
            }
            return false;
        }
    }
}
