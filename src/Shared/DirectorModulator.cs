using System;

namespace KoiSync.Core
{
    public enum DirectorMode
    {
        Passthrough = 0,
        Envelope = 1,
        Hold = 2,
        Sequence = 3
    }

    public sealed class DirectorStep
    {
        public string Name = "";
        public double DurationSeconds = 8.0;
        public double RampSeconds = 0.6;
        public DirectorMode Mode = DirectorMode.Envelope;
        public double L0Min = 0.0;
        public double L0Max = 0.80;
        public double HoldL0 = 0.72;
        public double IntensityScale = 1.0;
        public double FlutterHz = 0.0;
        public double FlutterAmp = 0.0;
        public string GameStage = "";
        public double? GameSpeed;
        public FeelPreset? FeelPreset;
        public NonShaftFeelMode? NonShaftMode;

        public DirectorStep Copy()
        {
            return new DirectorStep
            {
                Name = Name ?? "",
                DurationSeconds = Numbers.Clamp(Numbers.Finite(DurationSeconds) ? DurationSeconds : 8.0, 0.2, 180.0),
                RampSeconds = Numbers.Clamp(Numbers.Finite(RampSeconds) ? RampSeconds : 0.6, 0.05, 10.0),
                Mode = Mode == DirectorMode.Sequence ? DirectorMode.Envelope : Mode,
                L0Min = Numbers.Clamp(Numbers.Finite(L0Min) ? L0Min : 0.0, 0.0, 0.92),
                L0Max = Numbers.Clamp(Numbers.Finite(L0Max) ? L0Max : 0.80, 0.05, 0.95),
                HoldL0 = Numbers.Clamp(Numbers.Finite(HoldL0) ? HoldL0 : 0.72, 0.0, 0.95),
                IntensityScale = Numbers.Clamp(Numbers.Finite(IntensityScale) ? IntensityScale : 1.0, 0.15, 1.40),
                FlutterHz = Numbers.Clamp(Numbers.Finite(FlutterHz) ? FlutterHz : 0.0, 0.0, 8.0),
                FlutterAmp = Numbers.Clamp(Numbers.Finite(FlutterAmp) ? FlutterAmp : 0.0, 0.0, 0.18),
                GameStage = GameStage ?? "",
                GameSpeed = GameSpeed.HasValue && Numbers.Finite(GameSpeed.Value) ? (double?)Numbers.Clamp(GameSpeed.Value, 0.0, 1.0) : null,
                FeelPreset = FeelPreset,
                NonShaftMode = NonShaftMode
            };
        }
    }

    public sealed class DirectorEdgeGuard
    {
        public bool Enabled;
        public double FemaleThreshold = 85.0;
        public double MaleThreshold = 85.0;
        public double BrakeDurationSeconds = 7.0;
        public double CooldownSeconds = 3.0;
        public double BrakeL0Min = 0.52;
        public double BrakeL0Max = 0.80;
        public string BrakeStage = "WLoop";
        public double BrakeSpeed = 0.18;

        public DirectorEdgeGuard Copy()
        {
            double lo = Numbers.Clamp(Numbers.Finite(BrakeL0Min) ? BrakeL0Min : 0.52, 0.0, 0.90);
            double hi = Numbers.Clamp(Numbers.Finite(BrakeL0Max) ? BrakeL0Max : 0.80, Math.Max(lo + 0.05, 0.05), 0.95);
            return new DirectorEdgeGuard
            {
                Enabled = Enabled,
                FemaleThreshold = Numbers.Clamp(Numbers.Finite(FemaleThreshold) ? FemaleThreshold : 85.0, 40.0, 99.0),
                MaleThreshold = Numbers.Clamp(Numbers.Finite(MaleThreshold) ? MaleThreshold : 85.0, 40.0, 99.0),
                BrakeDurationSeconds = Numbers.Clamp(Numbers.Finite(BrakeDurationSeconds) ? BrakeDurationSeconds : 7.0, 1.5, 30.0),
                CooldownSeconds = Numbers.Clamp(Numbers.Finite(CooldownSeconds) ? CooldownSeconds : 3.0, 0.5, 30.0),
                BrakeL0Min = lo,
                BrakeL0Max = hi,
                BrakeStage = string.IsNullOrEmpty(BrakeStage) ? "WLoop" : BrakeStage,
                BrakeSpeed = Numbers.Clamp(Numbers.Finite(BrakeSpeed) ? BrakeSpeed : 0.18, 0.0, 1.0)
            };
        }
    }

    public sealed class DirectorActionEvent
    {
        public string Reason = "";
        public string GameStage = "";
        public double? GameSpeed;
        public double SpeedHoldSeconds;
        public double? RollbackFemaleMax;
        public double? RollbackMaleMax;
        public FeelPreset? FeelPreset;
        public NonShaftFeelMode? NonShaftMode;
    }

    // 60Hz real-time cerebellum for the AI Director MCP:
    // - Smoothly ramps L0 window overrides, teasing holds, micro-flutter, and multi-step sequences
    // - Runs real-time EdgeGuard gauge braking with zero LLM round-trip latency
    // - Enforces TTL dead-man watchdog and smooth fade-out back to native game sync
    // - Outputs physical inward-depth axes[] BEFORE SixAxisDrive rate caps and user travel limits
    public sealed class DirectorModulator
    {
        private readonly object gate = new object();
        private DirectorMode mode = DirectorMode.Passthrough;
        private DirectorStep activeStep;
        private DirectorStep[] sequence;
        private int sequenceIndex = -1;
        private double stepElapsed;
        private double ttlRemaining;
        private double blendWeight;
        private double rampRate = 1.6;
        private double flutterPhase;
        private bool stepEventPending;
        private string sequenceName = "";
        private string lastEvent = "待机";

        // Smooth current target parameters (for C0/C1 transitions between consecutive steps)
        private double curL0Min = 0.0, curL0Max = 0.80, curHoldL0 = 0.72, curIntensity = 1.0;
        private double curFlutterHz = 0.0, curFlutterAmp = 0.0;
        private bool paramsInitialized;

        // Real-time EdgeGuard state
        private DirectorEdgeGuard edgeGuard = new DirectorEdgeGuard();
        private double edgeBrakeRemaining;
        private double edgeCooldownRemaining;
        private int edgeTriggerCount;

        public bool Active
        {
            get { lock (gate) return mode != DirectorMode.Passthrough || blendWeight > 1e-4 || edgeBrakeRemaining > 0; }
        }

        public DirectorMode Mode { get { lock (gate) return mode; } }
        public bool EdgeGuardEnabled { get { lock (gate) return edgeGuard.Enabled; } }
        public bool EdgeBrakeActive { get { lock (gate) return edgeBrakeRemaining > 0; } }
        public int EdgeTriggerCount { get { lock (gate) return edgeTriggerCount; } }
        public double EdgeBrakeRemaining { get { lock (gate) return edgeBrakeRemaining; } }
        public double BlendWeight { get { lock (gate) return blendWeight; } }
        public string LastEvent { get { lock (gate) return lastEvent; } }

        public string SummaryStatus()
        {
            lock (gate)
            {
                if (edgeBrakeRemaining > 0)
                    return string.Format("寸止制动中 · 剩 {0:0.0}s (已触发 {1} 次)", edgeBrakeRemaining, edgeTriggerCount);
                if (mode == DirectorMode.Passthrough)
                {
                    if (blendWeight > 0.01) return "正在平滑交还游戏同步…";
                    return edgeGuard.Enabled
                        ? string.Format("待机 · 防射守护 ≥{0:0}%/{1:0}%", edgeGuard.FemaleThreshold, edgeGuard.MaleThreshold)
                        : "待机 · 正常跟随游戏同步";
                }
                if (mode == DirectorMode.Sequence && sequence != null && sequenceIndex >= 0 && sequenceIndex < sequence.Length)
                {
                    var st = sequence[sequenceIndex];
                    double rem = Math.Max(0, st.DurationSeconds - stepElapsed);
                    string label = string.IsNullOrEmpty(st.Name) ? ("步骤 " + (sequenceIndex + 1)) : st.Name;
                    return string.Format("剧本[{0}] {1}/{2} · {3} ({4:0.0}s)",
                        string.IsNullOrEmpty(sequenceName) ? "AI" : sequenceName,
                        sequenceIndex + 1, sequence.Length, label, rem);
                }
                if (mode == DirectorMode.Hold)
                    return string.Format("AI 悬停挑逗 · L0={0:0} (剩 {1:0}s)", curHoldL0 * 100.0, Math.Max(0, ttlRemaining));
                return string.Format("AI 行程接管 · L0 {0:0}–{1:0} (剩 {2:0}s)", curL0Min * 100.0, curL0Max * 100.0, Math.Max(0, ttlRemaining));
            }
        }

        public void SetOverride(DirectorStep step, double ttlSeconds)
        {
            if (step == null) throw new ArgumentNullException("step");
            var clean = step.Copy();
            if (clean.L0Max < clean.L0Min + 0.04) clean.L0Max = Math.Min(0.95, clean.L0Min + 0.04);
            double ttl = Numbers.Clamp(Numbers.Finite(ttlSeconds) ? ttlSeconds : 60.0, 0.5, 300.0);
            lock (gate)
            {
                sequence = null;
                sequenceIndex = -1;
                sequenceName = "";
                activeStep = clean;
                mode = clean.Mode == DirectorMode.Hold ? DirectorMode.Hold : DirectorMode.Envelope;
                stepElapsed = 0;
                ttlRemaining = ttl;
                rampRate = 1.0 / Math.Max(0.05, clean.RampSeconds);
                stepEventPending = HasGameAction(clean) || clean.Mode == DirectorMode.Hold;
                if (!paramsInitialized)
                {
                    curL0Min = clean.L0Min; curL0Max = clean.L0Max; curHoldL0 = clean.HoldL0;
                    curIntensity = clean.IntensityScale; curFlutterHz = clean.FlutterHz; curFlutterAmp = clean.FlutterAmp;
                    paramsInitialized = true;
                }
                lastEvent = string.IsNullOrEmpty(clean.Name) ? ("覆盖模式:" + mode) : clean.Name;
            }
        }

        public void StartSequence(string name, DirectorStep[] steps)
        {
            if (steps == null || steps.Length == 0) throw new ArgumentException("Sequence must contain at least one step");
            var clean = new DirectorStep[steps.Length];
            double totalDur = 0;
            for (int i = 0; i < steps.Length; i++)
            {
                clean[i] = (steps[i] ?? new DirectorStep()).Copy();
                if (clean[i].L0Max < clean[i].L0Min + 0.04) clean[i].L0Max = Math.Min(0.95, clean[i].L0Min + 0.04);
                totalDur += clean[i].DurationSeconds;
            }
            lock (gate)
            {
                sequence = clean;
                sequenceIndex = 0;
                sequenceName = name ?? "AI剧本";
                activeStep = clean[0];
                mode = DirectorMode.Sequence;
                stepElapsed = 0;
                ttlRemaining = Math.Min(300.0, totalDur + 1.0);
                rampRate = 1.0 / Math.Max(0.05, clean[0].RampSeconds);
                stepEventPending = HasGameAction(clean[0]) || clean[0].Mode == DirectorMode.Hold;
                if (!paramsInitialized)
                {
                    curL0Min = clean[0].L0Min; curL0Max = clean[0].L0Max; curHoldL0 = clean[0].HoldL0;
                    curIntensity = clean[0].IntensityScale; curFlutterHz = clean[0].FlutterHz; curFlutterAmp = clean[0].FlutterAmp;
                    paramsInitialized = true;
                }
                lastEvent = "启动剧本: " + sequenceName + " (" + clean.Length + "步)";
            }
        }

        public void ConfigureEdgeGuard(DirectorEdgeGuard guard)
        {
            var clean = (guard ?? new DirectorEdgeGuard()).Copy();
            lock (gate)
            {
                edgeGuard = clean;
                if (!clean.Enabled)
                {
                    edgeBrakeRemaining = 0;
                    edgeCooldownRemaining = 0;
                    lastEvent = "防射守护已关闭";
                }
                else
                {
                    lastEvent = string.Format("防射守护已武装 (女≥{0:0}% / 男≥{1:0}%)", clean.FemaleThreshold, clean.MaleThreshold);
                }
            }
        }

        public void Release(double fadeSeconds = 0.6)
        {
            lock (gate)
            {
                mode = DirectorMode.Passthrough;
                sequence = null;
                sequenceIndex = -1;
                activeStep = null;
                ttlRemaining = 0;
                edgeBrakeRemaining = 0;
                rampRate = 1.0 / Numbers.Clamp(Numbers.Finite(fadeSeconds) ? fadeSeconds : 0.6, 0.05, 5.0);
                stepEventPending = false;
                lastEvent = "平滑交还游戏同步";
            }
        }

        public void AbortImmediate(string reason = "急停切断")
        {
            lock (gate)
            {
                mode = DirectorMode.Passthrough;
                sequence = null;
                sequenceIndex = -1;
                activeStep = null;
                stepElapsed = 0;
                ttlRemaining = 0;
                blendWeight = 0;
                stepEventPending = false;
                paramsInitialized = false;
                edgeBrakeRemaining = 0;
                edgeCooldownRemaining = 0;
                lastEvent = reason ?? "急停切断";
            }
        }

        public DirectorEdgeGuard SnapshotEdgeGuard()
        {
            lock (gate) return edgeGuard.Copy();
        }

        public void SnapshotState(out string outMode, out string outStatus, out string outLastEvent,
            out int outStepIdx, out int outStepTotal, out double outTtl, out double outBlend,
            out bool outEdgeEnabled, out bool outEdgeBraking, out int outEdgeCount)
        {
            lock (gate)
            {
                outMode = edgeBrakeRemaining > 0 ? "EdgeBrake" : mode.ToString();
                outStatus = SummaryStatus();
                outLastEvent = lastEvent;
                outStepIdx = sequenceIndex;
                outStepTotal = sequence == null ? 0 : sequence.Length;
                outTtl = edgeBrakeRemaining > 0 ? edgeBrakeRemaining : Math.Max(0, ttlRemaining);
                outBlend = blendWeight;
                outEdgeEnabled = edgeGuard.Enabled;
                outEdgeBraking = edgeBrakeRemaining > 0;
                outEdgeCount = edgeTriggerCount;
            }
        }

        // Physical axes[] convention: axes[0] = physical inward depth (0=out, 1=deep -> device L0 = 1 - axes[0])
        public double[] Apply(double[] axes, double dt, double femaleGauge, double maleGauge, out DirectorActionEvent pendingAction, double outerCeiling = FeelTranslator.DefaultOuterCeiling)
        {
            pendingAction = null;
            if (axes == null || axes.Length != 6) return axes;
            double safeDt = Numbers.Finite(dt) ? Numbers.Clamp(dt, 0.0, 0.1) : 0.016;
            double ceil = Numbers.Clamp(Numbers.Finite(outerCeiling) ? outerCeiling : FeelTranslator.DefaultOuterCeiling, 0.55, 0.99);

            lock (gate)
            {
                // 1. Real-time 60Hz EdgeGuard check (zero LLM latency!)
                if (edgeCooldownRemaining > 0) edgeCooldownRemaining = Math.Max(0, edgeCooldownRemaining - safeDt);
                if (edgeBrakeRemaining > 0)
                {
                    edgeBrakeRemaining = Math.Max(0, edgeBrakeRemaining - safeDt);
                    if (edgeBrakeRemaining <= 0)
                    {
                        edgeCooldownRemaining = edgeGuard.CooldownSeconds;
                        lastEvent = "寸止制动结束，进入冷却";
                    }
                }
                else if (edgeGuard.Enabled && edgeCooldownRemaining <= 0 &&
                         ((Numbers.Finite(femaleGauge) && femaleGauge >= edgeGuard.FemaleThreshold) ||
                          (Numbers.Finite(maleGauge) && maleGauge >= edgeGuard.MaleThreshold)))
                {
                    edgeBrakeRemaining = edgeGuard.BrakeDurationSeconds;
                    edgeTriggerCount++;
                    lastEvent = string.Format("防射守护触发 #{0} (女 {1:0}% / 男 {2:0}%)", edgeTriggerCount, femaleGauge, maleGauge);
                    pendingAction = new DirectorActionEvent
                    {
                        Reason = "edge_guard_brake",
                        GameStage = edgeGuard.BrakeStage,
                        GameSpeed = edgeGuard.BrakeSpeed,
                        SpeedHoldSeconds = edgeGuard.BrakeDurationSeconds,
                        RollbackFemaleMax = Math.Max(20.0, edgeGuard.FemaleThreshold - 22.0),
                        RollbackMaleMax = Math.Max(20.0, edgeGuard.MaleThreshold - 22.0)
                    };
                }

                // 2. Advance Sequence / TTL watchdog
                if (mode == DirectorMode.Sequence && sequence != null && sequenceIndex >= 0 && sequenceIndex < sequence.Length)
                {
                    stepElapsed += safeDt;
                    while (sequenceIndex < sequence.Length && stepElapsed >= sequence[sequenceIndex].DurationSeconds)
                    {
                        stepElapsed -= sequence[sequenceIndex].DurationSeconds;
                        sequenceIndex++;
                        if (sequenceIndex < sequence.Length)
                        {
                            activeStep = sequence[sequenceIndex];
                            rampRate = 1.0 / Math.Max(0.05, activeStep.RampSeconds);
                            stepEventPending = HasGameAction(activeStep) || activeStep.Mode == DirectorMode.Hold;
                            lastEvent = "进入步骤 " + (sequenceIndex + 1) + "/" + sequence.Length +
                                (string.IsNullOrEmpty(activeStep.Name) ? "" : (": " + activeStep.Name));
                        }
                    }
                    if (sequenceIndex >= sequence.Length)
                    {
                        mode = DirectorMode.Passthrough;
                        sequence = null;
                        sequenceIndex = -1;
                        activeStep = null;
                        rampRate = 1.0 / 0.6;
                        lastEvent = "剧本自然完成，平滑交还同步";
                    }
                }
                else if (mode == DirectorMode.Envelope || mode == DirectorMode.Hold)
                {
                    ttlRemaining -= safeDt;
                    if (ttlRemaining <= 0)
                    {
                        mode = DirectorMode.Passthrough;
                        activeStep = null;
                        rampRate = 1.0 / 0.6;
                        lastEvent = "看门狗超时，自动平滑交还同步";
                    }
                }

                // 3. Emit pending step game-control event (if EdgeGuard didn't override this tick)
                if (pendingAction == null && stepEventPending && activeStep != null && edgeBrakeRemaining <= 0)
                {
                    stepEventPending = false;
                    string effectiveStage = activeStep.GameStage;
                    // Visual-tactile lockstep: if AI enters a Hold/pause step without specifying a stage,
                    // automatically shift the in-game actors to Idle (breathing in place) so visuals and SR6 never desync.
                    if (string.IsNullOrEmpty(effectiveStage) && activeStep.Mode == DirectorMode.Hold)
                        effectiveStage = "Idle";
                    pendingAction = new DirectorActionEvent
                    {
                        Reason = string.IsNullOrEmpty(activeStep.Name) ? "director_step" : activeStep.Name,
                        GameStage = effectiveStage,
                        GameSpeed = activeStep.GameSpeed,
                        SpeedHoldSeconds = mode == DirectorMode.Sequence ? activeStep.DurationSeconds : Math.Min(ttlRemaining, 60.0),
                        FeelPreset = activeStep.FeelPreset,
                        NonShaftMode = activeStep.NonShaftMode
                    };
                }

                // 4. Determine active effective target mode & blend weight
                bool braking = edgeBrakeRemaining > 0;
                DirectorMode effectiveMode;
                double tgtMin, tgtMax, tgtHold, tgtIntensity, tgtFHz, tgtFAmp;
                if (braking)
                {
                    effectiveMode = DirectorMode.Envelope;
                    tgtMin = edgeGuard.BrakeL0Min;
                    tgtMax = edgeGuard.BrakeL0Max;
                    tgtHold = edgeGuard.BrakeL0Max;
                    tgtIntensity = 0.70;
                    tgtFHz = 0.0;
                    tgtFAmp = 0.0;
                    blendWeight = Math.Min(1.0, blendWeight + safeDt * 3.5);
                }
                else if (mode != DirectorMode.Passthrough && activeStep != null && activeStep.Mode != DirectorMode.Passthrough)
                {
                    effectiveMode = activeStep.Mode;
                    tgtMin = activeStep.L0Min;
                    tgtMax = activeStep.L0Max;
                    tgtHold = activeStep.HoldL0;
                    tgtIntensity = activeStep.IntensityScale;
                    tgtFHz = activeStep.FlutterHz;
                    tgtFAmp = activeStep.FlutterAmp;
                    blendWeight = Math.Min(1.0, blendWeight + safeDt * rampRate);
                }
                else
                {
                    effectiveMode = DirectorMode.Passthrough;
                    tgtMin = curL0Min; tgtMax = curL0Max; tgtHold = curHoldL0;
                    tgtIntensity = curIntensity; tgtFHz = 0.0; tgtFAmp = 0.0;
                    blendWeight = Math.Max(0.0, blendWeight - safeDt * rampRate);
                    if (blendWeight <= 1e-4)
                    {
                        paramsInitialized = false;
                        return axes;
                    }
                }

                // Initialize target parameters cleanly on fresh activation (blendWeight handles smooth 0->1 ramp),
                // and slew smoothly between consecutive steps without snapping.
                if (!paramsInitialized)
                {
                    curL0Min = tgtMin; curL0Max = tgtMax; curHoldL0 = tgtHold;
                    curIntensity = tgtIntensity; curFlutterHz = tgtFHz; curFlutterAmp = tgtFAmp;
                    paramsInitialized = true;
                }
                else
                {
                    double slew = braking ? 8.0 : Math.Max(2.5, rampRate * 2.5);
                    double pAlpha = 1.0 - Math.Exp(-safeDt * slew);
                    curL0Min += (tgtMin - curL0Min) * pAlpha;
                    curL0Max += (tgtMax - curL0Max) * pAlpha;
                    curHoldL0 += (tgtHold - curHoldL0) * pAlpha;
                    curIntensity += (tgtIntensity - curIntensity) * pAlpha;
                    curFlutterHz += (tgtFHz - curFlutterHz) * pAlpha;
                    curFlutterAmp += (tgtFAmp - curFlutterAmp) * pAlpha;
                }

                var output = (double[])axes.Clone();
                double inDeviceL0 = Numbers.Clamp(1.0 - axes[0], 0.0, 1.0);
                double flutter = 0.0;
                if (curFlutterHz > 0.05 && curFlutterAmp > 1e-4)
                {
                    flutterPhase += safeDt * curFlutterHz * 2.0 * Math.PI;
                    if (flutterPhase > 1e5) flutterPhase %= 2.0 * Math.PI;
                    flutter = Math.Sin(flutterPhase) * curFlutterAmp;
                }

                double directedDevL0;
                if (effectiveMode == DirectorMode.Hold)
                {
                    directedDevL0 = Numbers.Clamp(curHoldL0 + flutter, 0.0, 0.95);
                    for (int i = 1; i < 6; i++)
                        output[i] = axes[i] + (0.5 - axes[i]) * (0.75 * blendWeight);
                }
                else
                {
                    // Map incoming [0..ceil] stroke smoothly onto director [curL0Min..curL0Max] (fixes P2-6)
                    double normInward = Numbers.Clamp((ceil - inDeviceL0) / ceil, 0.0, 1.0);
                    double center = 0.5 * (curL0Min + curL0Max);
                    double halfSpan = 0.5 * (curL0Max - curL0Min) * curIntensity;
                    double lo = Numbers.Clamp(center - halfSpan, 0.0, 0.95);
                    double hi = Numbers.Clamp(center + halfSpan, lo, 0.95);
                    directedDevL0 = Numbers.Clamp(hi - normInward * (hi - lo) + flutter, 0.0, 0.95);
                    for (int i = 1; i < 6; i++)
                    {
                        double scaled = Numbers.Clamp(0.5 + (axes[i] - 0.5) * curIntensity, 0.05, 0.95);
                        output[i] = axes[i] + (scaled - axes[i]) * blendWeight;
                    }
                }

                double finalDevL0 = inDeviceL0 + (directedDevL0 - inDeviceL0) * blendWeight;
                output[0] = Numbers.Clamp(1.0 - finalDevL0, 0.0, 1.0);
                return output;
            }
        }

        private static bool HasGameAction(DirectorStep step)
        {
            return !string.IsNullOrEmpty(step.GameStage) || step.GameSpeed.HasValue ||
                   step.FeelPreset.HasValue || step.NonShaftMode.HasValue;
        }
    }
}
