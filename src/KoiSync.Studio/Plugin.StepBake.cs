using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using KoiSync.Core;
using UnityEngine;
using OCI = global::Studio.ObjectCtrlInfo;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        private enum StepBakePhase { Idle, Prescan, BakeScripts }

        private sealed class SparsePropMonitor
        {
            internal object TreeNode;
            internal bool IsFluidProp;
        }

        private StepBakePhase stepBakePhase;
        private bool stepBakeWaitingSample;
        private bool stepBakeChainScripts;
        private double stepBakeTime;
        private double stepBakeStepSeconds = 0.05;
        private double stepBakeScriptStepSeconds = 0.02;
        private double stepBakeDuration;
        private bool stepBakeSavedSync;
        private bool stepBakeSavedAuto;
        private MotionSource stepBakeSavedSource;
        private float stepBakeSavedSpeed = 1f;
        private readonly List<StepBakeFrame> stepBakeFrames = new List<StepBakeFrame>();
        private readonly List<double> stepBakeHints = new List<double>();
        private readonly List<SparsePropMonitor> stepBakeSparseProps = new List<SparsePropMonitor>();
        private readonly Dictionary<ChaControl, float[]> stepBakePrevBlends = new Dictionary<ChaControl, float[]>();
        private StepBakeAnalysisResult lastStepBakeResult;
        private string stepBakeStatusText = "";

        internal bool StepBakeRunning { get { return stepBakePhase != StepBakePhase.Idle; } }
        internal string StepBakePhaseName { get { return stepBakePhase.ToString().ToLowerInvariant(); } }
        internal double StepBakeProgress
        {
            get
            {
                if (stepBakePhase == StepBakePhase.Idle) return lastStepBakeResult != null ? 1.0 : 0.0;
                double frac = stepBakeDuration > 0 ? Numbers.Clamp(stepBakeTime / stepBakeDuration, 0, 1) : 0;
                if (stepBakeChainScripts && stepBakePhase == StepBakePhase.Prescan) return frac * 0.5;
                if (stepBakeChainScripts && stepBakePhase == StepBakePhase.BakeScripts) return 0.5 + frac * 0.5;
                return frac;
            }
        }
        internal string StepBakeStatus { get { return stepBakeStatusText ?? ""; } }
        internal StepBakeAnalysisResult LastStepBakeResult { get { return lastStepBakeResult; } }
        internal string StepBakeReportPath { get { return string.IsNullOrEmpty(scriptStem) ? "" : scriptStem + ".stepbake.json"; } }

        internal void StartStepBake(double stepSeconds = 0.05, bool alsoBakeScripts = false, double scriptStepSeconds = 0.02)
        {
            if (!Clock.Available) throw new InvalidOperationException(L("Timeline 尚未就绪", "Timeline is not ready"));
            if (Clock.Duration < 1.0) throw new InvalidOperationException(L("当前场景时长过短", "Scene duration is too short"));
            if (actors.Count < 2) ReadActors();
            if (actors.Count < 2) throw new InvalidOperationException(L("当前场景需要至少两个角色", "At least two actors are required"));

            if (recording) FinishRecording();
            if (DirectorLoop.Mode != StudioPlaybackMode.Linear) DirectorReleaseLinear(false, 1.0);

            stepBakeSavedSync = sendOutput;
            stepBakeSavedAuto = autoCameraPairs;
            stepBakeSavedSource = motionSource;
            sendOutput = false;
            if (Devices != null) Devices.Stop();
            stepBakeSavedSpeed = (float)Math.Max(0.25, Math.Min(2.5, Clock.Speed > 0 ? Clock.Speed : 1.0));

            if (!autoCameraPairs) SetAutomaticGrouping(true);
            CachePairCandidates();
            CaptureTimelineObjectDefaults();
            CollectStepBakeHintsAndMonitors();

            stepBakeFrames.Clear();
            stepBakePrevBlends.Clear();
            lastStepBakeResult = null;
            stepBakeStepSeconds = Numbers.Clamp(Numbers.Finite(stepSeconds) ? stepSeconds : 0.05, 0.02, 0.25);
            stepBakeScriptStepSeconds = Numbers.Clamp(Numbers.Finite(scriptStepSeconds) ? scriptStepSeconds : 0.02, 0.015, 0.10);
            stepBakeChainScripts = alsoBakeScripts;
            stepBakeDuration = Clock.Duration;
            stepBakeTime = 0.0;
            stepBakePhase = StepBakePhase.Prescan;
            stepBakeWaitingSample = false;

            Clock.Pause();
            Clock.SetSpeed(1.0f);
            stepBakeStatusText = L("正在高速预扫描…", "Fast prescan running…");
            status = stepBakeStatusText;
        }

        internal void StartScriptBake(double stepSeconds = 0.02, bool prescanIfEmpty = true)
        {
            if (!Clock.Available) throw new InvalidOperationException(L("Timeline 尚未就绪", "Timeline is not ready"));
            if (string.IsNullOrEmpty(scriptStem)) throw new InvalidOperationException(L("先加载或保存场景卡，以确定脚本路径", "Load or save a scene card to establish the script path"));
            if (Clock.Duration < 1.0) throw new InvalidOperationException(L("当前场景时长过短", "Scene duration is too short"));
            if (actors.Count < 2) ReadActors();
            if (actors.Count < 2) throw new InvalidOperationException(L("当前场景需要至少两个角色", "At least two actors are required"));

            if (prescanIfEmpty && Book.Chapters.Count == 0)
            {
                StartStepBake(0.05, true, stepSeconds);
                return;
            }

            if (recording) FinishRecording();
            if (DirectorLoop.Mode != StudioPlaybackMode.Linear) DirectorReleaseLinear(false, 1.0);

            stepBakeSavedSync = sendOutput;
            stepBakeSavedAuto = autoCameraPairs;
            stepBakeSavedSource = motionSource;
            stepBakeSavedSpeed = (float)Math.Max(0.25, Math.Min(2.5, Clock.Speed > 0 ? Clock.Speed : 1.0));
            stepBakeChainScripts = false;
            stepBakeScriptStepSeconds = Numbers.Clamp(Numbers.Finite(stepSeconds) ? stepSeconds : 0.02, 0.015, 0.10);
            BeginScriptBakePhase();
        }

        private void BeginScriptBakePhase()
        {
            sendOutput = false;
            if (Devices != null) Devices.Stop();
            CachePairCandidates();
            CaptureTimelineObjectDefaults();
            motionSource = MotionSource.Live;
            autoCameraPairs = false;
            followChapters = true;
            manualOpening = false;
            appliedChapter = "";
            previousChapterTime = -1;
            Drive.Reset();
            Feel.Reset();
            ResetChapterTransition();
            recorder.Begin(0.0);

            stepBakeDuration = Clock.Duration;
            stepBakeTime = 0.0;
            stepBakePhase = StepBakePhase.BakeScripts;
            stepBakeWaitingSample = false;

            Clock.Pause();
            Clock.SetSpeed(1.0f);
            stepBakeStatusText = L("正在高速烘焙 6 轴脚本…", "Baking 6-axis scripts…");
            status = stepBakeStatusText;
        }

        internal void CancelStepBake()
        {
            if (stepBakePhase == StepBakePhase.Idle) return;
            stepBakePhase = StepBakePhase.Idle;
            stepBakeWaitingSample = false;
            stepBakeChainScripts = false;
            stepBakeFrames.Clear();
            stepBakePrevBlends.Clear();
            motionSource = stepBakeSavedSource;
            autoCameraPairs = stepBakeSavedAuto;
            sendOutput = stepBakeSavedSync;
            if (Clock.Available) Clock.SetSpeed(stepBakeSavedSpeed);
            stepBakeStatusText = L("已取消预扫描", "Prescan cancelled");
            status = stepBakeStatusText;
        }

        // Called in Plugin.Update() BEFORE rendering/LateUpdate.
        private void AdvanceStepBakeSeek()
        {
            if (stepBakePhase == StepBakePhase.Idle) return;
            if (!Clock.Available || scenePending) { CancelStepBake(); return; }
            if (stepBakeWaitingSample) return;

            if (stepBakeTime > stepBakeDuration + 0.001)
            {
                if (stepBakePhase == StepBakePhase.Prescan) FinalizeStepBake();
                else FinalizeScriptBake();
                return;
            }
            double t = Math.Min(stepBakeTime, stepBakeDuration);
            RestoreTimelineObjectsBefore(t);
            Clock.Seek(t);
            Clock.RefreshPose();
            stepBakeWaitingSample = true;
        }

        // Called in AtFrameEnd() AFTER Unity LateUpdate (FinalIK, NodesConstraints, BetterPenetration, OCIItems) has settled!
        private void CaptureStepBakeFrame()
        {
            if (stepBakePhase == StepBakePhase.Idle || !stepBakeWaitingSample) return;
            stepBakeWaitingSample = false;
            if (stepBakePhase == StepBakePhase.BakeScripts)
            {
                CaptureScriptBakeFrame();
                return;
            }

            try
            {
                double t = Math.Min(stepBakeTime, stepBakeDuration);
                var frame = new StepBakeFrame { Time = Math.Round(t, 3) };

                // 1. Refresh live pair paths (including scene-level OCIItem shafts like penis7!) & constraints
                foreach (var actor in pairActors)
                {
                    if (actor.Actor == null) continue;
                    if (actor.Bp == null) actor.Bp = StudioBinding.FindBp(actor.Actor);
                    RefreshShaft(actor);
                    UpdatePairPath(actor);
                }
                RefreshPairConstraints();

                // 2. Collect explicit BP / NC attachment pairs for this frame
                var explicitKeys = new HashSet<string>();
                foreach (var source in pairActors)
                {
                    if (source.Root == null || source.Model != null || !ActorVisible(source.Actor)) continue;
                    var actual = RuntimeAccess.Read(RuntimeAccess.Read(source.Bp, "collisionAgent"), "m_collisionCharacter") as ChaControl;
                    if (actual == null || actual == source.Actor || !RuntimeAccess.Flag(source.Bp, "danTargetsValid")) continue;
                    string node = Convert.ToString(RuntimeAccess.Read(source.Bp, "danEntryParentName"));
                    if (!string.IsNullOrEmpty(node))
                    {
                        var kind = StudioBinding.Infer(node, OpeningKind.Vaginal);
                        explicitKeys.Add(Key(source.Actor) + ":" + Key(actual) + ":" + (int)kind);
                    }
                }
                foreach (var c in pairConstraints)
                {
                    if (c.Entry == null || c.Source.Root == null || !ActiveConstraint(c.Source, c.Child)) continue;
                    if (!ActorVisible(c.Source.Actor) || !ActorVisible(c.Target.Actor)) continue;
                    if (!RuntimeAccess.Flag(c.Handle, "enabled") || !RuntimeAccess.Flag(c.Handle, "position")) continue;
                    var kind = StudioBinding.Infer(StudioBinding.AnatomicalName(c.Entry, c.Target.Actor.transform), OpeningKind.Vaginal);
                    explicitKeys.Add(Key(c.Source.Actor) + ":" + Key(c.Target.Actor) + ":" + (int)kind);
                }

                // 3. Measure EVERY visible source -> target opening pair in live 3D space!
                double bestDepthRatio = double.NaN;
                double bestWeight = -1;
                foreach (var source in pairActors)
                {
                    if (source.Root == null || source.Path == null || !ActorVisible(source.Actor)) continue;
                    int srcKey = Key(source.Actor);
                    string srcName = StudioBinding.Name(source.Actor);
                    double pathLen = StudioShaftMotion.PathLength(source.Path);
                    if (pathLen < 0.02) continue;
                    MotionVector shaftRoot = source.Path[0];
                    MotionVector shaftTip = source.Path[source.Path.Length - 1];
                    MotionVector shaftDir = shaftTip - shaftRoot;
                    double shaftNorm = shaftDir.Length;
                    if (shaftNorm > 1e-5) shaftDir = shaftDir * (1.0 / shaftNorm);

                    foreach (var target in pairActors)
                    {
                        if (target == source || !ActorVisible(target.Actor)) continue;
                        int tgtKey = Key(target.Actor);
                        string tgtName = StudioBinding.Name(target.Actor);

                        foreach (var entry in target.Openings)
                        {
                            if (!entry.Contact.Alive) continue;
                            MotionVector point, right, inward;
                            entry.Contact.Pose(source.Path, out point, out right, out inward);
                            double dist = StudioShaftMotion.ClosestDistance(source.Path, point);
                            double ins = StudioShaftMotion.InsertionFraction(source.Path, point, inward);
                            double cos = inward.Length > 1e-5 ? MotionVector.Dot(shaftDir, inward * (1.0 / inward.Length)) : 0;
                            bool isExp = explicitKeys.Contains(srcKey + ":" + tgtKey + ":" + (int)entry.Kind);
                            double axial = shaftNorm > 1e-5 ? MotionVector.Dot(point - shaftRoot, shaftDir) : (point - shaftRoot).Length;
                            if (ins >= 0.85 && shaftNorm > 1e-5 && axial <= 0.015 && axial >= -pathLen * 0.65)
                            {
                                double radial = ((point - shaftRoot) - shaftDir * axial).Length;
                                if (radial < dist) dist = radial;
                            }
                            double limit = CameraPairRanking.ContactLimit(pathLen);
                            if (!isExp && dist > limit * 1.4) continue;

                            double rawDepth = Numbers.Clamp(1.0 - axial / Math.Max(0.05, pathLen), -0.35, 1.85);

                            frame.Observations.Add(new StepOpeningObservation
                            {
                                SourceKey = srcKey,
                                TargetKey = tgtKey,
                                Opening = (int)entry.Kind,
                                SourceName = srcName,
                                TargetName = tgtName,
                                Distance = dist,
                                Length = pathLen,
                                Insertion = ins,
                                AlignmentCos = cos,
                                RawDepthRatio = rawDepth,
                                ExplicitAttached = isExp
                            });

                            double w = (isExp ? 4.0 : 0.0) + Math.Exp(-dist / 0.018) * 2.6 + Math.Max(0, ins);
                            if (w > bestWeight) { bestWeight = w; bestDepthRatio = rawDepth; }
                        }
                    }
                }

                if (Numbers.Finite(bestDepthRatio))
                    frame.RawL0 = Numbers.Clamp(1.0 - bestDepthRatio, 0, 1);

                // 4. Count active visual sparse / fluid prop monitors (audio-only SFX already excluded)
                int activeSparse = 0;
                int activeFluid = 0;
                for (int i = 0; i < stepBakeSparseProps.Count; i++)
                {
                    var mon = stepBakeSparseProps[i];
                    if (mon.TreeNode != null && RuntimeAccess.Flag(mon.TreeNode, "visible"))
                    {
                        activeSparse++;
                        if (mon.IsFluidProp) activeFluid++;
                    }
                }
                frame.ActiveSparseProps = activeSparse;
                frame.ActiveFluidProps = activeFluid;

                // 5. Facial blendshape delta energy
                frame.BlendShapeDelta = SampleBlendShapeDelta();

                stepBakeFrames.Add(frame);
                stepBakeTime += stepBakeStepSeconds;

                if (stepBakeFrames.Count % 25 == 0)
                {
                    int pct = (int)Math.Round(StepBakeProgress * 100.0);
                    stepBakeStatusText = L("正在高速预扫描 ", "Fast prescan ") + pct + "% (" + t.ToString("F1", CultureInfo.InvariantCulture) + "s / " + stepBakeDuration.ToString("F1", CultureInfo.InvariantCulture) + "s)";
                    status = stepBakeStatusText;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("StepBake prescan frame error: " + ex.GetBaseException().Message);
                CancelStepBake();
            }
        }

        private void CaptureScriptBakeFrame()
        {
            try
            {
                double t = Math.Min(stepBakeTime, stepBakeDuration);
                double dt = stepBakeScriptStepSeconds;
                climaxPulse = null;
                climaxLiveValue = double.NaN;

                UpdateChapterBinding();
                if (Binding != null && Binding.Alive)
                {
                    if (MappingMode != AxisMappingMode.Manual && Feel.Preset != FeelPreset.Raw)
                    {
                        double cap = Math.Max(0.06, Binding.RestLength * 0.70);
                        if (Binding.L1HalfRange > cap * 1.6) Binding.L1HalfRange = cap;
                        if (Binding.L2HalfRange > cap * 1.6) Binding.L2HalfRange = cap;
                    }
                    Sample = Binding.Sample(dt);
                    Measured = MappingMode == AxisMappingMode.Manual ? SolveManualCalibration(Sample) : NativeMotion.Solve(Sample);
                    Binding.Commit(Sample, Measured);
                    SampleCount++;
                    ObserveLiveL0Frequency();
                    Drive.PhysicalL0Minimum = 0;
                    UpdateClimax();
                    UpdateLiveDrive(dt);
                    ApplyClimaxPulse();
                    UpdateOutputTransition(dt);
                    recorder.Add(t, outputValues, climaxPulse);
                }
                else
                {
                    Drive.UpdateScript(new[] { 1d, .5, .5, .5, .5, .5 });
                    Array.Copy(Drive.Values, outputValues, 6);
                    recorder.Add(t, outputValues, null);
                }

                stepBakeTime += stepBakeScriptStepSeconds;
                if (recorder.Count % 40 == 0)
                {
                    int pct = (int)Math.Round(StepBakeProgress * 100.0);
                    stepBakeStatusText = L("正在高速烘焙脚本 ", "Baking scripts ") + pct + "% (" + recorder.Count + L(" 帧)", " frames)");
                    status = stepBakeStatusText;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("StepBake script frame error: " + ex.GetBaseException().Message);
                CancelStepBake();
            }
        }

        private void FinalizeStepBake()
        {
            try
            {
                var analysis = StudioStepAnalyzer.Analyze(stepBakeFrames, stepBakeHints, stepBakeDuration);
                lastStepBakeResult = analysis;

                RememberChapters();
                Book.Chapters.Clear();
                for (int i = 0; i < analysis.Chapters.Count; i++)
                {
                    var ch = analysis.Chapters[i];
                    var item = new Chapter
                    {
                        Start = ch.Start,
                        Source = ch.SourceKey,
                        Target = ch.TargetKey,
                        Opening = ch.Opening,
                        SourceName = ch.SourceName,
                        TargetName = ch.TargetName,
                        Name = ch.SourceKey >= 0 && ch.TargetKey >= 0
                            ? ch.SourceName + " → " + ch.TargetName + " · " + OpeningNames[Math.Max(0, Math.Min(OpeningNames.Length - 1, ch.Opening))]
                            : ch.Label,
                        Manual = true,
                        MappingMode = Feel.Preset == FeelPreset.Raw ? -1 : (int)AxisMappingMode.Soft,
                        Phase = ch.ClimaxDetected ? ChapterPhase.Climax : ChapterPhase.Automatic,
                        ClimaxDetected = ch.ClimaxDetected,
                        ClimaxEvidence = ch.ClimaxEvidence ?? "",
                        Climax = ch.ClimaxDetected ? BuildAutoClimaxOptions() : null,
                        DepthP05 = ch.DepthP05,
                        DepthP95 = ch.DepthP95,
                        PeakHz = ch.PeakHz,
                        NeedsReview = ch.NeedsReview,
                        ReviewNote = ch.ReviewNote ?? ""
                    };
                    Book.Chapters.Add(item);
                }

                string sceneName = string.IsNullOrEmpty(scenePath) ? "StudioScene" : Path.GetFileNameWithoutExtension(scenePath);
                DirectorBook.LoadSegments(analysis.Segments, analysis.Duration, sceneName, "director_json");
                if (directorApi != null && !string.IsNullOrEmpty(scriptStem))
                    directorApi.PersistDirectorAndStepBakeReports(analysis);

                MarkSceneDataDirty();
                SaveSceneData();

                if (stepBakeChainScripts && !string.IsNullOrEmpty(scriptStem))
                {
                    stepBakeFrames.Clear();
                    stepBakePrevBlends.Clear();
                    BeginScriptBakePhase();
                    return;
                }

                stepBakePhase = StepBakePhase.Idle;
                stepBakeWaitingSample = false;
                RestoreTimelineObjectsBefore(0);
                Clock.Seek(0);
                Clock.RefreshPose();
                Clock.SetSpeed(stepBakeSavedSpeed);
                motionSource = stepBakeSavedSource;
                autoCameraPairs = stepBakeSavedAuto;
                sendOutput = stepBakeSavedSync;
                appliedChapter = "";
                followChapters = true;
                manualOpening = false;
                SelectChapter(Book.At(0));

                stepBakeStatusText = L("预扫描完成 · ", "Prescan done · ") + analysis.Chapters.Count + L(" 章", " ch")
                    + (analysis.ReviewCount > 0 ? " · " + analysis.ReviewCount + L(" 待复核", " review") : "")
                    + (analysis.ClimaxCount > 0 ? " · " + analysis.ClimaxCount + L(" 高潮", " climax") : "");
                status = stepBakeStatusText;
                DirectorLastNote = stepBakeStatusText;
            }
            catch (Exception ex)
            {
                stepBakePhase = StepBakePhase.Idle;
                stepBakeWaitingSample = false;
                stepBakeChainScripts = false;
                motionSource = stepBakeSavedSource;
                autoCameraPairs = stepBakeSavedAuto;
                sendOutput = stepBakeSavedSync;
                if (Clock.Available) Clock.SetSpeed(stepBakeSavedSpeed);
                stepBakeStatusText = L("预扫描失败：", "Prescan failed: ") + ex.GetBaseException().Message;
                status = stepBakeStatusText;
                Logger.LogWarning(stepBakeStatusText);
            }
            finally
            {
                stepBakeFrames.Clear();
                stepBakePrevBlends.Clear();
            }
        }

        private void FinalizeScriptBake()
        {
            stepBakePhase = StepBakePhase.Idle;
            stepBakeWaitingSample = false;
            stepBakeChainScripts = false;

            bool bakedOk = false;
            try
            {
                if (recorder.Count >= 2 && !string.IsNullOrEmpty(scriptStem))
                {
                    if (stepBakeDuration >= recorder.End && stepBakeDuration - recorder.End < 0.15)
                        recorder.ExtendEnd(stepBakeDuration);
                    recorder.Save(scriptStem);
                    recordedPath = scriptStem + ".funscript";
                    recordedStart = recorder.Start;
                    recordedEnd = recorder.End;
                    scripts.Load(recordedPath);
                    motionSource = stepBakeSavedSource == MotionSource.Script ? MotionSource.Script : MotionSource.Auto;
                    bakedOk = true;
                    CaptureChapterCalibration();
                    MarkSceneDataDirty();
                    SaveSceneData();
                    scriptsStatus = L("六轴脚本已烘焙保存 · ", "Six-axis scripts baked · ") + recorder.Count + L(" 帧", " frames");
                    stepBakeStatusText = scriptsStatus;
                    status = scriptsStatus;
                    DirectorLastNote = scriptsStatus;
                }
                else
                {
                    stepBakeStatusText = L("烘焙帧数不足，未保存脚本", "Too few baked frames; scripts not saved");
                    status = stepBakeStatusText;
                }
            }
            catch (Exception ex)
            {
                stepBakeStatusText = L("脚本烘焙失败：", "Script bake failed: ") + ex.GetBaseException().Message;
                status = stepBakeStatusText;
                Logger.LogWarning(stepBakeStatusText);
            }
            finally
            {
                RestoreTimelineObjectsBefore(0);
                Clock.Seek(0);
                Clock.RefreshPose();
                Clock.SetSpeed(stepBakeSavedSpeed);
                if (!bakedOk) motionSource = stepBakeSavedSource;
                autoCameraPairs = stepBakeSavedAuto;
                sendOutput = stepBakeSavedSync;
                appliedChapter = "";
                followChapters = true;
                SelectChapter(Book.At(0));
            }
        }

        private ClimaxOptions BuildAutoClimaxOptions()
        {
            if (Feel.Preset == FeelPreset.Raw) return ClimaxDefaults();
            bool intense = Feel.Preset == FeelPreset.Intense;
            return new ClimaxOptions
            {
                LocalSettings = true,
                Detect = true,
                Enabled = true,
                FrequencyMode = ClimaxFrequencyMode.AutoMotion,
                Minimum = intense ? 0 : 10,
                Maximum = intense ? 260 : 240,
                Hertz = intense ? 8.5 : 7.0,
                MaximumSeconds = intense ? 7.0 : 6.5
            };
        }

        private static bool IsExcludedNonClimaxProp(string lowerName, bool hasAudio, bool hasVisual)
        {
            if (hasAudio && !hasVisual) return true;
            string[] noise = { "piston", "moan", "pant", "breath", "kiss", "spank", "ambient", "sound", "audio", "sfx", "voice", "bgm", "light", "shadow", "lamp", "smoke", "camera", "fan", "softbj" };
            for (int i = 0; i < noise.Length; i++)
                if (lowerName.IndexOf(noise[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static bool IsFluidOrClimaxProp(string rawName, string lowerName)
        {
            if (ClimaxCues.Shooting(rawName) || ClimaxCues.Marker(rawName)) return true;
            string[] cues = { "cum", "jizz", "sperm", "juice", "extract", "puddle", "smear", "splash", "squirt", "waterstream", "liquid", "ekitai", "seieki", "精液", "射精", "潮吹", "白浊" };
            for (int i = 0; i < cues.Length; i++)
                if (lowerName.IndexOf(cues[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private void CollectStepBakeHintsAndMonitors()
        {
            stepBakeHints.Clear();
            stepBakeSparseProps.Clear();
            double dur = Clock.Available ? Clock.Duration : 0;
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
                    string id = Convert.ToString(RuntimeAccess.Read(track, "id")) ?? "";
                    var keys = RuntimeAccess.Read(track, "keyframes") as IDictionary;
                    if (keys == null || keys.Count == 0) continue;

                    if (id == "constraintEnabled")
                    {
                        foreach (DictionaryEntry kv in keys)
                        {
                            double kt = Convert.ToDouble(kv.Key);
                            if (kt > 0.4) stepBakeHints.Add(kt);
                        }
                    }
                    else if (id == "objectEnabled" && keys.Count <= 12)
                    {
                        double firstOnT = double.MaxValue;
                        foreach (DictionaryEntry kv in keys)
                        {
                            double kt = Convert.ToDouble(kv.Key);
                            object val = RuntimeAccess.Read(kv.Value, "value");
                            if (val is bool && (bool)val && kt < firstOnT) firstOnT = kt;
                        }
                        if (firstOnT <= 2.0 || firstOnT >= double.MaxValue * 0.5) continue;

                        var oci = RuntimeAccess.Read(track, "oci") as OCI;
                        if (oci == null || oci.treeNodeObject == null) continue;

                        string rawName = oci.treeNodeObject.textName ?? "";
                        string parentName = oci.treeNodeObject.parent != null ? (oci.treeNodeObject.parent.textName ?? "") : "";
                        string lower = (rawName + " " + parentName).ToLowerInvariant();
                        bool hasAudio = false;
                        bool hasVisual = false;
                        var ociItem = oci as global::Studio.OCIItem;
                        if (ociItem != null && ociItem.objectItem != null)
                        {
                            hasAudio = ociItem.objectItem.GetComponentInChildren<AudioSource>(true) != null;
                            hasVisual = ociItem.objectItem.GetComponentInChildren<Renderer>(true) != null
                                || ociItem.objectItem.GetComponentInChildren<ParticleSystem>(true) != null;
                            lower += " " + ociItem.objectItem.name.ToLowerInvariant();
                        }
                        if (IsExcludedNonClimaxProp(lower, hasAudio, hasVisual)) continue;

                        bool isFluid = IsFluidOrClimaxProp(rawName, lower);
                        stepBakeSparseProps.Add(new SparsePropMonitor
                        {
                            TreeNode = oci.treeNodeObject,
                            IsFluidProp = isFluid
                        });
                        if (isFluid && dur > 10.0 && firstOnT >= dur * 0.45 && firstOnT < dur - 1.0)
                            stepBakeHints.Add(firstOnT);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("CollectStepBakeHintsAndMonitors: " + ex.GetBaseException().Message);
            }
        }

        private double SampleBlendShapeDelta()
        {
            double deltaSum = 0;
            for (int i = 0; i < actors.Count; i++)
            {
                var actor = actors[i];
                if (actor == null || actor.sex != 1 || !ActorVisible(actor)) continue;
                var smrs = actor.GetComponentsInChildren<SkinnedMeshRenderer>(false);
                if (smrs == null) continue;
                SkinnedMeshRenderer face = null;
                int maxBlends = 0;
                for (int s = 0; s < smrs.Length; s++)
                {
                    var m = smrs[s].sharedMesh;
                    if (m != null && m.blendShapeCount > maxBlends)
                    {
                        maxBlends = m.blendShapeCount;
                        face = smrs[s];
                    }
                }
                if (face == null || maxBlends == 0) continue;
                int count = Math.Min(maxBlends, 64);
                var cur = new float[count];
                for (int b = 0; b < count; b++) cur[b] = face.GetBlendShapeWeight(b);
                float[] prev;
                if (stepBakePrevBlends.TryGetValue(actor, out prev) && prev.Length == count)
                {
                    for (int b = 0; b < count; b++) deltaSum += Math.Abs(cur[b] - prev[b]);
                }
                stepBakePrevBlends[actor] = cur;
            }
            return deltaSum;
        }
    }
}
