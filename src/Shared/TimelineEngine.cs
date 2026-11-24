using System;

namespace KoiSync.Core
{
    public enum TimelinePreset
    {
        Custom = 0,
        TipRub = 1,              // High-frequency shallow glans/entrance friction (breaks native speed ceiling)
        DeepGrind = 2,           // Deep bottom-out hold + full-body 3D circular/figure-8 waist grind + paired 6-axis SR6
        NineShallowOneDeep = 3,  // N brisk yellow-band shallow strokes + 1 full-stroke plunge from outer edge to bottom hold
        TeasePlunge = 4,         // Fast slam inward -> bottom twist hold -> agonizing slow pullout -> tip pause
        EdgeHover = 5,           // Hover at entrance tip with slow breathing pulse + gentle R0 twist massage
        TurboSprint = 6,         // Continuous full-stroke acceleration breaking native speed cap
        ClimaxEjaculation = 7,   // In-place climax sprint -> deep ejaculation pulse + decaying 3D body spasms -> afterglow tremble
        Organic = 8,             // Markov stochastic rhythm engine smoothly weaving contrasting rhythms at phrase boundaries
        WaistOnly = 9            // Hold fixed depth & perform pure waist/pelvis grind (circular, figure-8, or straight R1/R2 lines)
    }

    public enum GrindPattern
    {
        Auto = 0,
        Circle = 1,              // Full 360-deg sensual circular hip grind
        Figure8 = 2,             // Figure-8 (infinity) belly-dance hip sway
        RockFB = 3,              // Anterior-posterior pelvic rock & clitoral/deep press
        Corkscrew = 4,           // Deep axial corkscrew twist + tight hip orbit
        LineFB = 5,              // Pure straight front-back waist sway (L1 + R2 only; zero circle, zero R0/L2/R1)
        LineLR = 6,              // Pure straight left-right waist sway (L2 + R1 only; zero circle, zero R0/L1/R2)
        LineR1R2 = 7,            // Pure straight in-phase R1 + R2 tilt line (zero circle, zero R0 twist)
        PitchOnly = 8,           // Pure R2 pitch rocking only
        RollOnly = 9             // Pure R1 roll rocking only
    }

    public enum ActiveMoverMode
    {
        Auto = 0,                // Auto-detect from real 1-cycle pelvis excursion + native isFemaleInitiative
        Female = 1,              // Force female pelvis + spine + limb IK as primary mover (e.g. cowgirl)
        Male = 2,                // Force male pelvis + spine + limb IK as primary mover (e.g. missionary / doggy)
        Both = 3                 // Shared counter-phase grind between both actors
    }

    public sealed class TimelineStep
    {
        public string Name = "";
        public TimelinePreset Preset = TimelinePreset.Custom;
        public double DurationSeconds = 12.0;
        public double RampSeconds = 0.45;
        public bool Loop = false;              // When true, loops continuously until AI sends next command or user stops

        // Animation depth sub-arc in [0..1] (0=shallowest frame of pose, 1=deepest frame of pose)
        public double DepthMin = 0.02;
        public double DepthMax = 0.96;

        // Paired SR6 L0 window in device coordinates (0=deepest, OuterCeiling=configurable outer edge, 1.0=out)
        public double L0Min = 0.00;
        public double L0Max = 0.80;

        // Temporal curve parameters (completely free from Koikatsu's native clip speed!)
        public double FreqHz = 2.0;
        public double FreqEndHz = -1.0;        // If > 0, linearly ramps frequency from FreqHz -> FreqEndHz over step duration
        public double InwardRatio = 0.50;      // Fraction of active stroke spent moving inward (0.15=fast slam / slow pull)
        public double PauseBottomSeconds = 0.0;
        public double PauseTopSeconds = 0.0;

        // Generalized N-shallow M-deep tuning (supports 1浅1深, 2浅1深, 3浅2深, 9浅1深, etc.!):
        // ShallowBaseDepth (<0 => DepthMin) sets where the N shallow strokes start; ShallowDepthMax / ShallowL0Min set their yellow-band travel.
        // The M deep strokes always plunge from DepthMin (L0Max) all the way to DepthMax (L0Min) and return cleanly.
        public bool ShallowDeepMode = false;
        public int ShallowCount = 9;
        public int DeepCount = 1;
        public double ShallowBaseDepth = -1.0; // -1=use DepthMin; 0..0.70 allows doing shallow strokes around any depth base
        public double ShallowDepthMax = 0.44;  // Yellow-band shallow stroke travel (~44% depth) instead of tiny micro-ticks
        public double ShallowL0Min = 0.34;     // Paired SR6 L0 low bound during shallow strokes (~46u stroke @ 2.4Hz = yellow band!)
        public double DeepHoldSeconds = 0.95;

        // Optional fixed depth hold [0..1] for pure waist-grind without depth stroking ("只扭腰不抽插"); <0 = normal stroking
        public double HoldDepth = -1.0;

        // Paired SR6 multi-axis Twist (R0) & Waist-Grind (R1/R2/L1/L2) + Real 3D Full-Body Procedural Waist Grind!
        public double TwistAmp = 0.0;          // 0..0.45 (adds R0 corkscrew twist & 3D pelvis yaw)
        public double GrindAmp = 0.0;          // 0..0.42 (adds circular/figure-8/rock 3D pelvis + spine + limb IK & SR6 R1/R2/L1/L2)
        public double GrindHz = 1.2;           // Waist-grind cycle frequency in Hz
        public GrindPattern GrindPattern = GrindPattern.Circle;
        public ActiveMoverMode MoverMode = ActiveMoverMode.Auto;
        public double BodyGrindScale = -1.0;   // <0 = auto proportional to GrindAmp/TwistAmp; 0 = disable 3D body grind; 0.1..1.6 = explicit
        public bool PingPong = true;           // Stay on single smooth monotonic branch for partial arcs

        // v0.2.2 Markov Organic Stochastic Rhythm & Climax Spasm fields
        public bool OrganicRandom = false;     // Enable phrase-boundary Markov stochastic rhythm variation
        public double OrganicIntensity = 0.55; // 0.0=gentle slow contrast .. 1.0=high-energy intense contrast
        public int OrganicSeed = 0;            // 0=auto entropy seed; non-zero=reproducible deterministic sequence
        public double SpasmAmp = 0.0;          // 0..1.5 additive 3D body-spasm / pelvic-shiver intensity

        public static TimelineStep FromPreset(TimelinePreset preset, double durationSeconds = 12.0, double outerCeiling = FeelTranslator.DefaultOuterCeiling)
        {
            double dur = Numbers.Clamp(Numbers.Finite(durationSeconds) ? durationSeconds : 12.0, 0.5, 600.0);
            double ceil = Numbers.Clamp(Numbers.Finite(outerCeiling) ? outerCeiling : FeelTranslator.DefaultOuterCeiling, 0.55, 0.99);
            switch (preset)
            {
                case TimelinePreset.TipRub:
                    return new TimelineStep
                    {
                        Name = "高频磨龟头",
                        Preset = preset,
                        DurationSeconds = dur,
                        DepthMin = 0.02, DepthMax = 0.24,
                        L0Min = Math.Max(0.20, ceil - 0.30), L0Max = ceil,
                        FreqHz = 3.2, InwardRatio = 0.50,
                        TwistAmp = 0.22, GrindAmp = 0.14, GrindHz = 1.5,
                        GrindPattern = GrindPattern.Circle,
                        BodyGrindScale = 0.45,
                        PingPong = true
                    };
                case TimelinePreset.DeepGrind:
                    return new TimelineStep
                    {
                        Name = "深埋扭腰碾磨",
                        Preset = preset,
                        DurationSeconds = dur,
                        DepthMin = 0.84, DepthMax = 1.00,
                        L0Min = 0.00, L0Max = 0.18,
                        FreqHz = 1.8, InwardRatio = 0.50,
                        TwistAmp = 0.36, GrindAmp = 0.34, GrindHz = 1.25,
                        GrindPattern = GrindPattern.Circle,
                        BodyGrindScale = 1.00,
                        PingPong = true
                    };
                case TimelinePreset.NineShallowOneDeep:
                    return new TimelineStep
                    {
                        Name = "九浅一深",
                        Preset = preset,
                        DurationSeconds = dur,
                        DepthMin = 0.02, DepthMax = 1.00,
                        L0Min = 0.00, L0Max = ceil,
                        FreqHz = 2.4, InwardRatio = 0.46,
                        ShallowCount = 9, ShallowBaseDepth = -1.0,
                        ShallowDepthMax = 0.44, ShallowL0Min = Math.Max(0.18, ceil - 0.46),
                        DeepHoldSeconds = 0.95,
                        TwistAmp = 0.26, GrindAmp = 0.22, GrindHz = 1.35,
                        GrindPattern = GrindPattern.Circle,
                        BodyGrindScale = 0.65,
                        PingPong = true
                    };
                case TimelinePreset.TeasePlunge:
                    return new TimelineStep
                    {
                        Name = "急顶慢拔折磨",
                        Preset = preset,
                        DurationSeconds = dur,
                        DepthMin = 0.03, DepthMax = 0.99,
                        L0Min = 0.00, L0Max = ceil,
                        FreqHz = 0.85, InwardRatio = 0.18,
                        PauseBottomSeconds = 0.60, PauseTopSeconds = 0.45,
                        TwistAmp = 0.28, GrindAmp = 0.22, GrindHz = 1.1,
                        GrindPattern = GrindPattern.Corkscrew,
                        BodyGrindScale = 0.70,
                        PingPong = false
                    };
                case TimelinePreset.EdgeHover:
                    return new TimelineStep
                    {
                        Name = "入口寸止悬停",
                        Preset = preset,
                        DurationSeconds = dur,
                        DepthMin = 0.03, DepthMax = 0.12,
                        L0Min = Math.Max(0.45, ceil - 0.12), L0Max = ceil,
                        FreqHz = 0.75, InwardRatio = 0.50,
                        TwistAmp = 0.24, GrindAmp = 0.18, GrindHz = 0.85,
                        GrindPattern = GrindPattern.RockFB,
                        BodyGrindScale = 0.50,
                        PingPong = true
                    };
                case TimelinePreset.TurboSprint:
                    return new TimelineStep
                    {
                        Name = "破限狂暴加速",
                        Preset = preset,
                        DurationSeconds = dur,
                        DepthMin = 0.04, DepthMax = 0.98,
                        L0Min = 0.00, L0Max = ceil,
                        FreqHz = 1.4, FreqEndHz = 3.4, InwardRatio = 0.48,
                        TwistAmp = 0.16, GrindAmp = 0.12, GrindHz = 1.8,
                        GrindPattern = GrindPattern.RockFB,
                        BodyGrindScale = 0.35,
                        PingPong = false
                    };
                case TimelinePreset.ClimaxEjaculation:
                    return new TimelineStep
                    {
                        Name = "高潮射精抽搐",
                        Preset = preset,
                        DurationSeconds = Math.Max(6.0, dur),
                        RampSeconds = 0.28,
                        DepthMin = 0.18, DepthMax = 1.00,
                        L0Min = 0.00, L0Max = Math.Min(ceil, 0.72),
                        FreqHz = 2.8, InwardRatio = 0.44,
                        TwistAmp = 0.22, GrindAmp = 0.24, GrindHz = 1.4,
                        GrindPattern = GrindPattern.RockFB,
                        BodyGrindScale = 0.75,
                        SpasmAmp = 1.0,
                        PingPong = true
                    };
                case TimelinePreset.Organic:
                    return new TimelineStep
                    {
                        Name = "有机随机节奏",
                        Preset = preset,
                        DurationSeconds = dur,
                        RampSeconds = 0.55,
                        Loop = true,
                        DepthMin = 0.04, DepthMax = 0.98,
                        L0Min = 0.00, L0Max = ceil,
                        FreqHz = 1.85, InwardRatio = 0.48,
                        TwistAmp = 0.24, GrindAmp = 0.24, GrindHz = 1.25,
                        GrindPattern = GrindPattern.Circle,
                        BodyGrindScale = 0.75,
                        OrganicRandom = true,
                        OrganicIntensity = 0.58,
                        PingPong = true
                    };
                case TimelinePreset.WaistOnly:
                    return new TimelineStep
                    {
                        Name = "定深纯摆腰",
                        Preset = preset,
                        DurationSeconds = dur,
                        RampSeconds = 0.40,
                        Loop = true,
                        HoldDepth = 0.85,
                        DepthMin = 0.85, DepthMax = 0.85,
                        L0Min = 0.12, L0Max = 0.12,
                        FreqHz = 1.0, InwardRatio = 0.50,
                        TwistAmp = 0.0, GrindAmp = 0.38, GrindHz = 1.35,
                        GrindPattern = GrindPattern.LineFB,
                        BodyGrindScale = 1.05,
                        PingPong = true
                    };
                default:
                    return new TimelineStep { Name = "自定义曲线", Preset = TimelinePreset.Custom, DurationSeconds = dur, L0Max = ceil };
            }
        }

        public TimelineStep Sanitized()
        {
            bool waistHold = Preset == TimelinePreset.WaistOnly || (Numbers.Finite(HoldDepth) && HoldDepth >= 0.0);
            double holdD = waistHold ? Numbers.Clamp(Numbers.Finite(HoldDepth) && HoldDepth >= 0.0 ? HoldDepth : DepthMax, 0.05, 1.0) : -1.0;
            double dMin, dMax, lMin, lMax;
            if (waistHold)
            {
                dMin = dMax = holdD;
                double holdL0 = Numbers.Clamp(1.0 - holdD * 0.85, 0.02, 0.90);
                lMin = lMax = holdL0;
            }
            else
            {
                dMin = Numbers.Clamp(Numbers.Finite(DepthMin) ? DepthMin : 0.02, 0.0, 0.95);
                dMax = Numbers.Clamp(Numbers.Finite(DepthMax) ? DepthMax : 0.96, Math.Min(1.0, dMin + 0.03), 1.0);
                lMin = Numbers.Clamp(Numbers.Finite(L0Min) ? L0Min : 0.0, 0.0, 0.94);
                lMax = Numbers.Clamp(Numbers.Finite(L0Max) ? L0Max : FeelTranslator.DefaultOuterCeiling, Math.Min(0.99, lMin + 0.04), 0.99);
            }
            double sBase = Numbers.Finite(ShallowBaseDepth) && ShallowBaseDepth >= 0.0
                ? Numbers.Clamp(ShallowBaseDepth, 0.0, Math.Max(0.0, dMax - 0.08))
                : -1.0;
            double effBase = sBase >= 0.0 ? sBase : dMin;
            double sMax = Numbers.Clamp(Numbers.Finite(ShallowDepthMax) ? ShallowDepthMax : 0.44, Math.Min(0.92, effBase + 0.06), Math.Max(effBase + 0.06, Math.Min(0.95, dMax)));
            double sL0Min = Numbers.Clamp(Numbers.Finite(ShallowL0Min) ? ShallowL0Min : Math.Max(lMin, lMax - 0.46), lMin, Math.Max(lMin, lMax - 0.04));
            double bScale = Numbers.Finite(BodyGrindScale)
                ? (BodyGrindScale < 0 ? -1.0 : Numbers.Clamp(BodyGrindScale, 0.0, 1.6))
                : -1.0;
            bool sdMode = ShallowDeepMode || Preset == TimelinePreset.NineShallowOneDeep;
            return new TimelineStep
            {
                Name = string.IsNullOrEmpty(Name) ? Preset.ToString() : Name,
                Preset = Preset,
                DurationSeconds = Numbers.Clamp(Numbers.Finite(DurationSeconds) ? DurationSeconds : 12.0, 0.5, 600.0),
                RampSeconds = Numbers.Clamp(Numbers.Finite(RampSeconds) ? RampSeconds : 0.45, 0.05, 5.0),
                Loop = Loop,
                DepthMin = dMin,
                DepthMax = dMax,
                L0Min = lMin,
                L0Max = lMax,
                HoldDepth = holdD,
                FreqHz = Numbers.Clamp(Numbers.Finite(FreqHz) ? FreqHz : 2.0, 0.15, 5.0),
                FreqEndHz = Numbers.Finite(FreqEndHz) && FreqEndHz > 0 ? Numbers.Clamp(FreqEndHz, 0.15, 5.0) : -1.0,
                InwardRatio = Numbers.Clamp(Numbers.Finite(InwardRatio) ? InwardRatio : 0.5, 0.12, 0.88),
                PauseBottomSeconds = Numbers.Clamp(Numbers.Finite(PauseBottomSeconds) ? PauseBottomSeconds : 0.0, 0.0, 5.0),
                PauseTopSeconds = Numbers.Clamp(Numbers.Finite(PauseTopSeconds) ? PauseTopSeconds : 0.0, 0.0, 5.0),
                ShallowDeepMode = sdMode,
                ShallowCount = Math.Max(1, Math.Min(20, ShallowCount)),
                DeepCount = Math.Max(1, Math.Min(10, DeepCount)),
                ShallowBaseDepth = sBase,
                ShallowDepthMax = sMax,
                ShallowL0Min = sL0Min,
                DeepHoldSeconds = Numbers.Clamp(Numbers.Finite(DeepHoldSeconds) ? DeepHoldSeconds : 0.95, 0.0, 5.0),
                TwistAmp = Numbers.Clamp(Numbers.Finite(TwistAmp) ? TwistAmp : 0.0, 0.0, 0.45),
                GrindAmp = Numbers.Clamp(Numbers.Finite(GrindAmp) ? GrindAmp : 0.0, 0.0, 0.42),
                GrindHz = Numbers.Clamp(Numbers.Finite(GrindHz) ? GrindHz : 1.2, 0.2, 4.0),
                GrindPattern = GrindPattern == GrindPattern.Auto ? GrindPattern.Circle : GrindPattern,
                MoverMode = MoverMode,
                BodyGrindScale = bScale,
                PingPong = PingPong || (dMax - dMin) < 0.70,
                OrganicRandom = (OrganicRandom || Preset == TimelinePreset.Organic) && !waistHold && !sdMode,
                OrganicIntensity = Numbers.Clamp(Numbers.Finite(OrganicIntensity) ? OrganicIntensity : 0.55, 0.0, 1.0),
                OrganicSeed = OrganicSeed,
                SpasmAmp = Numbers.Clamp(Numbers.Finite(SpasmAmp) ? SpasmAmp : 0.0, 0.0, 1.5)
            };
        }
    }

    public struct TimelineFrame
    {
        public bool Active;
        public double ScrubPhase;        // Exact Animator Layer-0 normalizedTime [0..1)
        public double DepthNorm;         // Logical curve depth [0..1] (0=shallowest, 1=deepest)
        public double DeviceL0;          // Paired SR6 L0 [0..1] (0=deepest, 1=out)
        public bool GoingInward;
        public double[] PhysicalAxes;    // 6-axis physical convention ready for SixAxisDrive.Update()

        // Real 3D full-body procedural waist-grind kinematics (instant pelvis + 0.11s phase-lagged spine counter-wave)
        public double BodyGrindWeight;   // 0..1.6 effective amplitude multiplier for 3D pelvis/spine/limb IK radiation
        public ActiveMoverMode MoverMode;
        public GrindPattern GrindPattern;
        public double GrindLat;          // [-1..+1] right/left pelvis sway
        public double GrindFwd;          // [-1..+1] forward/back pelvic rock
        public double GrindAxial;        // [-1..+1] subtle press/lift along insertion axis
        public double GrindYaw;          // [-1..+1] pelvis yaw twist (R0)
        public double GrindRoll;         // [-1..+1] hip side tilt (R1)
        public double GrindPitch;        // [-1..+1] pelvic tilt / arch (R2)
        public double GrindYawLag;       // Phase-lagged (~0.11s) for organic spine counter-wave radiation
        public double GrindRollLag;
        public double GrindPitchLag;

        // v0.2.2 Climax Ejaculation & Body-Spasm + Organic Markov telemetry
        public double SpasmAmp;          // 0..1.5 live procedural decaying body-spasm amplitude
        public double SpasmPhase;        // Continuous high-freq spasm phase (radians)
        public int ClimaxPhase;          // 0=none, 1=sprint, 2=ejaculation_pulse, 3=afterglow
        public int EjaculationPulseCount;// Monotonic counter incremented on each ejaculation throb in Phase 2
    }

    // 60Hz Timeline Curve Engine:
    // Breaks Koikatsu's rigid linear animation clock by synthesizing expressive depth/rhythm
    // curves, scrubbing ALL actors' Animator Layer-0 in exact frame-lock, driving full-body
    // 3D pelvis/spine/limb-IK waist-grind motion, and outputting paired 6-axis SR6 waveforms.
    public sealed class TimelineEngine
    {
        private readonly object gate = new object();
        private TimelineStep[] steps;
        private int stepIndex = -1;
        private double stepElapsed;
        private double cycleAccum;
        private double grindPhase;
        private double prevDepthNorm;
        private double smoothDepthNorm;
        private double smoothDeviceL0 = 0.80;
        private double smoothBodyWeight;
        private bool smoothInit;
        private int nsodCycleIndex;
        private double nsodDeepHoldLeft;
        private bool sequenceLoop;
        private string sequenceName = "";
        private string lastEvent = "未激活";

        // v0.2.2 Markov Organic Stochastic Rhythm + Climax Ejaculation runtime state
        private System.Random organicRng;
        private TimelineStep organicEffective;
        private int organicState;             // 0=DeepGrind, 1=FullThrust, 2=ShallowDeep, 3=TeasePlunge, 4=TipRub, 5=EdgeHover
        private double organicPhraseLeft;
        private string organicSubModeName = "";
        private double spasmPhase;
        private double smoothSpasmAmp;
        private int currentClimaxPhase;
        private int ejaculationPulseCount;
        private int lastClimaxPulseIdx;

        public bool Active { get { lock (gate) return steps != null && stepIndex >= 0 && stepIndex < steps.Length; } }
        public string LastEvent { get { lock (gate) return lastEvent; } }
        public TimelinePreset ActivePreset { get { lock (gate) return (steps != null && stepIndex >= 0 && stepIndex < steps.Length) ? steps[stepIndex].Preset : TimelinePreset.Custom; } }
        public int CurrentClimaxPhase { get { lock (gate) return currentClimaxPhase; } }
        public int EjaculationPulseCount { get { lock (gate) return ejaculationPulseCount; } }
        public bool OrganicActive { get { lock (gate) return steps != null && stepIndex >= 0 && stepIndex < steps.Length && steps[stepIndex].OrganicRandom; } }
        public string OrganicSubMode { get { lock (gate) return organicSubModeName; } }

        public string SummaryStatus()
        {
            lock (gate)
            {
                if (steps == null || stepIndex < 0 || stepIndex >= steps.Length) return "时间轴变式未激活";
                var st = steps[stepIndex];
                string label = st.OrganicRandom && !string.IsNullOrEmpty(organicSubModeName)
                    ? st.Name + "·" + organicSubModeName
                    : st.Name;
                if (st.Loop || sequenceLoop)
                {
                    return string.Format("变式[{0}] {1}/{2} · {3} (循环中)",
                        string.IsNullOrEmpty(sequenceName) ? "AI" : sequenceName,
                        stepIndex + 1, steps.Length, label);
                }
                double rem = Math.Max(0.0, st.DurationSeconds - stepElapsed);
                return string.Format("变式[{0}] {1}/{2} · {3} ({4:0.0}s)",
                    string.IsNullOrEmpty(sequenceName) ? "AI" : sequenceName,
                    stepIndex + 1, steps.Length, label, rem);
            }
        }

        public void Snapshot(out bool active, out string name, out string stepName, out int idx, out int total, out double remSec, out double depthNorm, out double devL0)
        {
            bool unusedLoop;
            GrindPattern unusedPat;
            ActiveMoverMode unusedMover;
            double unusedWeight;
            Snapshot(out active, out name, out stepName, out idx, out total, out remSec, out depthNorm, out devL0, out unusedLoop, out unusedPat, out unusedMover, out unusedWeight);
        }

        public void Snapshot(out bool active, out string name, out string stepName, out int idx, out int total, out double remSec, out double depthNorm, out double devL0,
            out bool looping, out GrindPattern pattern, out ActiveMoverMode mover, out double bodyWeight)
        {
            lock (gate)
            {
                active = steps != null && stepIndex >= 0 && stepIndex < steps.Length;
                name = sequenceName;
                stepName = active ? steps[stepIndex].Name : "";
                idx = active ? stepIndex : -1;
                total = steps == null ? 0 : steps.Length;
                looping = active && (steps[stepIndex].Loop || sequenceLoop);
                remSec = active ? Math.Max(0.0, steps[stepIndex].DurationSeconds - stepElapsed) : 0.0;
                depthNorm = smoothDepthNorm;
                devL0 = smoothDeviceL0;
                pattern = active ? steps[stepIndex].GrindPattern : GrindPattern.Circle;
                mover = active ? steps[stepIndex].MoverMode : ActiveMoverMode.Auto;
                bodyWeight = active ? smoothBodyWeight : 0.0;
            }
        }

        public void SnapshotActiveParams(out double freqHz, out double depthMin, out double depthMax, out double grindAmp, out double twistAmp)
        {
            lock (gate)
            {
                if (steps != null && stepIndex >= 0 && stepIndex < steps.Length)
                {
                    var st = steps[stepIndex];
                    freqHz = st.FreqHz;
                    depthMin = st.DepthMin;
                    depthMax = st.DepthMax;
                    grindAmp = st.GrindAmp;
                    twistAmp = st.TwistAmp;
                }
                else
                {
                    freqHz = 1.4;
                    depthMin = 0.0;
                    depthMax = 1.0;
                    grindAmp = 0.5;
                    twistAmp = 0.35;
                }
            }
        }

        public void Start(string name, TimelineStep[] rawSteps, bool loopSequence = false)
        {
            if (rawSteps == null || rawSteps.Length == 0) throw new ArgumentException("Timeline requires at least one step");
            var clean = new TimelineStep[rawSteps.Length];
            for (int i = 0; i < rawSteps.Length; i++)
                clean[i] = (rawSteps[i] ?? new TimelineStep()).Sanitized();
            lock (gate)
            {
                steps = clean;
                stepIndex = 0;
                stepElapsed = 0.0;
                sequenceLoop = loopSequence;
                sequenceName = string.IsNullOrEmpty(name) ? clean[0].Name : name;
                ResetStepState(clean[0]);
                lastEvent = "启动时间轴变式: " + sequenceName + " (" + clean.Length + "段" + ((loopSequence || clean[0].Loop) ? " · 循环" : "") + ")";
            }
        }

        // Live non-interrupting parameter tuning while a Timeline variant is actively running.
        // Smoothly adjusts frequency, depth window, L0 bounds, shallow travel, twist/grind, pattern, mover, or loop without resetting phase!
        public bool TuneActive(Action<TimelineStep> mutator, bool? loopSequence = null)
        {
            if (mutator == null) return false;
            lock (gate)
            {
                if (steps == null || stepIndex < 0 || stepIndex >= steps.Length) return false;
                var cur = steps[stepIndex];
                double prevFreq = cur.FreqHz, prevDMin = cur.DepthMin, prevDMax = cur.DepthMax;
                double prevGAmp = cur.GrindAmp, prevTAmp = cur.TwistAmp, prevHold = cur.HoldDepth;
                GrindPattern prevPat = cur.GrindPattern;
                bool wasOrganic = cur.OrganicRandom;

                mutator(cur);

                bool waveformEdited = Math.Abs(cur.FreqHz - prevFreq) > 1e-6 ||
                                      Math.Abs(cur.DepthMin - prevDMin) > 1e-6 ||
                                      Math.Abs(cur.DepthMax - prevDMax) > 1e-6 ||
                                      Math.Abs(cur.GrindAmp - prevGAmp) > 1e-6 ||
                                      Math.Abs(cur.TwistAmp - prevTAmp) > 1e-6 ||
                                      Math.Abs(cur.HoldDepth - prevHold) > 1e-6 ||
                                      cur.GrindPattern != prevPat;
                if (wasOrganic && waveformEdited && cur.OrganicRandom)
                {
                    // Explicit waveform edits override autonomous Organic phrasing immediately (fixes P1-4)
                    cur.OrganicRandom = false;
                    cur.Preset = TimelinePreset.Custom;
                    organicEffective = null;
                }

                steps[stepIndex] = cur.Sanitized();
                if (organicEffective != null && steps[stepIndex].OrganicRandom)
                {
                    // Propagate live mover/L0Max/intensity tweaks into current organic phrase immediately
                    organicEffective.MoverMode = steps[stepIndex].MoverMode;
                    organicEffective.OrganicIntensity = steps[stepIndex].OrganicIntensity;
                    if (organicEffective.L0Max > 0.35) organicEffective.L0Max = steps[stepIndex].L0Max;
                }
                if (loopSequence.HasValue) sequenceLoop = loopSequence.Value;
                lastEvent = "实时微调变式: " + steps[stepIndex].Name;
                return true;
            }
        }

        // Guardrail #3: Live-sync UI outerCeilingL0 slider into active & queued Timeline steps immediately!
        public void UpdateOuterCeiling(double newCeiling)
        {
            double ceil = Numbers.Clamp(Numbers.Finite(newCeiling) ? newCeiling : FeelTranslator.DefaultOuterCeiling, 0.55, 0.99);
            lock (gate)
            {
                if (steps == null) return;
                for (int i = 0; i < steps.Length; i++)
                {
                    var s = steps[i];
                    if (s == null) continue;
                    // Only adjust outer-reaching steps (preserve deep_grind's buried L0Max ~0.18)
                    if (s.Preset != TimelinePreset.DeepGrind && s.Preset != TimelinePreset.ClimaxEjaculation && s.L0Max >= 0.45)
                    {
                        s.L0Max = ceil;
                        if (s.Preset == TimelinePreset.NineShallowOneDeep)
                            s.ShallowL0Min = Math.Max(s.L0Min + 0.04, Math.Max(0.18, ceil - 0.46));
                        else if (s.Preset == TimelinePreset.TipRub)
                            s.L0Min = Math.Max(0.20, ceil - 0.30);
                        else if (s.Preset == TimelinePreset.EdgeHover)
                            s.L0Min = Math.Max(0.45, ceil - 0.12);
                        steps[i] = s.Sanitized();
                    }
                }
                if (organicEffective != null && organicEffective.Preset != TimelinePreset.DeepGrind && organicEffective.L0Max >= 0.45)
                {
                    organicEffective.L0Max = ceil;
                    organicEffective = organicEffective.Sanitized();
                }
            }
        }

        public void Stop(string reason = "时间轴变式已停止")
        {
            lock (gate)
            {
                steps = null;
                stepIndex = -1;
                stepElapsed = 0;
                sequenceLoop = false;
                smoothInit = false;
                smoothBodyWeight = 0.0;
                smoothSpasmAmp = 0.0;
                currentClimaxPhase = 0;
                lastClimaxPulseIdx = -1;
                organicEffective = null;
                organicSubModeName = "";
                lastEvent = reason ?? "时间轴变式已停止";
            }
        }

        public TimelineFrame Step(double dt, PhaseDepthMap map, double[] basePhysicalAxes)
        {
            double safeDt = Numbers.Finite(dt) ? Numbers.Clamp(dt, 0.0, 0.1) : 0.016;
            lock (gate)
            {
                if (steps == null || stepIndex < 0 || stepIndex >= steps.Length)
                    return new TimelineFrame { Active = false };

                var st = steps[stepIndex];
                stepElapsed += safeDt;
                while (stepIndex < steps.Length && stepElapsed >= steps[stepIndex].DurationSeconds)
                {
                    if (steps[stepIndex].Loop)
                    {
                        // Single-step seamless continuous loop
                        stepElapsed %= Math.Max(0.5, steps[stepIndex].DurationSeconds);
                        break;
                    }
                    stepElapsed -= steps[stepIndex].DurationSeconds;
                    stepIndex++;
                    if (stepIndex >= steps.Length && sequenceLoop)
                    {
                        stepIndex = 0;
                        st = steps[0];
                        ResetStepState(st);
                        lastEvent = "循环重播变式: " + st.Name;
                        break;
                    }
                    if (stepIndex < steps.Length)
                    {
                        st = steps[stepIndex];
                        ResetStepState(st);
                        lastEvent = "切入变式 " + (stepIndex + 1) + "/" + steps.Length + ": " + st.Name;
                    }
                }
                if (stepIndex >= steps.Length)
                {
                    steps = null;
                    stepIndex = -1;
                    sequenceLoop = false;
                    smoothInit = false;
                    smoothBodyWeight = 0.0;
                    smoothSpasmAmp = 0.0;
                    currentClimaxPhase = 0;
                    organicEffective = null;
                    organicSubModeName = "";
                    lastEvent = "时间轴变式全部完成，恢复正常动画";
                    return new TimelineFrame { Active = false };
                }

                // Markov Organic Stochastic Rhythm phrase scheduler (when OrganicRandom is active)
                TimelineStep evalStep = st;
                bool inOrganicCrossfade = false;
                if (st.OrganicRandom)
                {
                    if (organicEffective == null || organicPhraseLeft <= 0.0)
                    {
                        AdvanceOrganicPhrase(st);
                    }
                    organicPhraseLeft -= safeDt;
                    evalStep = organicEffective;
                    inOrganicCrossfade = organicPhraseLeft > (evalStep.DurationSeconds - 0.65);
                }

                // Advance continuous grind & spasm oscillators
                grindPhase += safeDt * evalStep.GrindHz * 2.0 * Math.PI;
                if (grindPhase > 1e5) grindPhase %= 2.0 * Math.PI;
                spasmPhase += safeDt * 6.4 * 2.0 * Math.PI;
                if (spasmPhase > 1e5) spasmPhase %= 2.0 * Math.PI;

                // Evaluate logical depth curve [0..1] AND paired SR6 L0 [0..1]
                double rawDepth, rawDevL0;
                double liveSpasmTarget = evalStep.SpasmAmp;
                bool usePingPong = evalStep.PingPong;
                if (evalStep.Preset == TimelinePreset.ClimaxEjaculation)
                {
                    EvaluateClimaxEjaculation(evalStep, safeDt, out rawDepth, out rawDevL0, out liveSpasmTarget, ref usePingPong);
                }
                else
                {
                    currentClimaxPhase = 0;
                    EvaluateStepCurve(evalStep, safeDt, out rawDepth, out rawDevL0, ref usePingPong);
                }

                // Smooth slew across step transitions, organic phrase switches, & live TuneActive() updates
                if (!smoothInit)
                {
                    smoothDepthNorm = prevDepthNorm = rawDepth;
                    smoothDeviceL0 = rawDevL0;
                    smoothInit = true;
                }
                else
                {
                    double rate;
                    if (stepElapsed < evalStep.RampSeconds)
                        rate = Math.Max(10.0, 4.5 / Math.Max(0.05, evalStep.RampSeconds));
                    else if (inOrganicCrossfade)
                        rate = 14.0;
                    else
                        rate = 48.0;
                    double alpha = 1.0 - Math.Exp(-safeDt * rate);
                    smoothDepthNorm += (rawDepth - smoothDepthNorm) * alpha;
                    smoothDeviceL0 += (rawDevL0 - smoothDeviceL0) * alpha;
                }

                double spasmAlpha = 1.0 - Math.Exp(-safeDt * 8.0);
                smoothSpasmAmp += (liveSpasmTarget - smoothSpasmAmp) * spasmAlpha;

                bool goingInward = smoothDepthNorm >= prevDepthNorm - 1e-5;
                prevDepthNorm = smoothDepthNorm;

                double scrubPhase = map != null && map.Ready
                    ? map.MapDepthToPhase(smoothDepthNorm, goingInward, usePingPong)
                    : smoothDepthNorm * 0.5;

                // Evaluate 3D waist-grind kinematics (instant pelvis + 0.11s lagged spine counter-wave)
                double gLat, gFwd, gAxial, gYaw, gRoll, gPitch;
                EvaluateGrindPattern(evalStep.GrindPattern, grindPhase, smoothDepthNorm, out gLat, out gFwd, out gAxial, out gYaw, out gRoll, out gPitch);

                double lagRadians = 0.11 * evalStep.GrindHz * 2.0 * Math.PI;
                double uLat, uFwd, uAxial, gYawLag, gRollLag, gPitchLag;
                EvaluateGrindPattern(evalStep.GrindPattern, grindPhase - lagRadians, smoothDepthNorm, out uLat, out uFwd, out uAxial, out gYawLag, out gRollLag, out gPitchLag);

                // Target 3D procedural body-grind weight (smoothly slewed so body motion never snaps!)
                double autoBodyWeight = Numbers.Clamp(Math.Max(evalStep.GrindAmp / 0.30, evalStep.TwistAmp / 0.34), 0.0, 1.25);
                double targetBodyWeight = evalStep.BodyGrindScale >= 0.0 ? evalStep.BodyGrindScale : autoBodyWeight;
                if (evalStep.Preset == TimelinePreset.NineShallowOneDeep && nsodDeepHoldLeft > 0)
                    targetBodyWeight = Math.Max(targetBodyWeight, 0.95);
                if (evalStep.Preset == TimelinePreset.ClimaxEjaculation && currentClimaxPhase == 2)
                    targetBodyWeight = Math.Max(targetBodyWeight, 0.90);
                double wAlpha = 1.0 - Math.Exp(-safeDt * 4.5);
                smoothBodyWeight += (targetBodyWeight - smoothBodyWeight) * wAlpha;

                // Synthesize paired 6-axis physical vector 100% phase-locked to the exact 3D pelvis grind wave
                var phys = basePhysicalAxes != null && basePhysicalAxes.Length == 6
                    ? (double[])basePhysicalAxes.Clone()
                    : new[] { 0.5, 0.5, 0.5, 0.5, 0.5, 0.5 };

                phys[0] = Numbers.Clamp(1.0 - smoothDeviceL0, 0.0, 1.0);

                if (evalStep.GrindAmp > 1e-4)
                {
                    bool pureLinear = evalStep.GrindPattern == GrindPattern.LineFB ||
                                      evalStep.GrindPattern == GrindPattern.LineLR ||
                                      evalStep.GrindPattern == GrindPattern.LineR1R2 ||
                                      evalStep.GrindPattern == GrindPattern.PitchOnly ||
                                      evalStep.GrindPattern == GrindPattern.RollOnly;
                    double bleed = pureLinear ? 0.0 : 0.28;
                    // phys[1] = L1 (surge / front-back), phys[2] = L2 (sway / left-right) (fixes P1-2)
                    phys[1] = Numbers.Clamp(0.5 + (phys[1] - 0.5) * bleed + evalStep.GrindAmp * 0.55 * gFwd, 0.08, 0.92);
                    phys[2] = Numbers.Clamp(0.5 + (phys[2] - 0.5) * bleed + evalStep.GrindAmp * 0.55 * gLat, 0.08, 0.92);
                    phys[4] = Numbers.Clamp(0.5 + (phys[4] - 0.5) * (pureLinear ? 0.0 : 0.25) + evalStep.GrindAmp * 0.85 * gRoll, 0.10, 0.90);
                    phys[5] = Numbers.Clamp(0.5 + (phys[5] - 0.5) * (pureLinear ? 0.0 : 0.22) + evalStep.GrindAmp * 0.85 * gPitch, 0.10, 0.90);
                    if (pureLinear && evalStep.TwistAmp <= 1e-4) phys[3] = 0.5;
                }

                if (evalStep.TwistAmp > 1e-4)
                {
                    double strokeTerm = (smoothDepthNorm - 0.5) * 0.8;
                    double twistWave = evalStep.GrindAmp > 1e-4 ? (0.65 * gYaw + 0.35 * strokeTerm) : Math.Sin(cycleAccum * 2.0 * Math.PI);
                    phys[3] = Numbers.Clamp(0.5 + evalStep.TwistAmp * Numbers.Clamp(twistWave, -1.0, 1.0), 0.06, 0.94);
                }

                return new TimelineFrame
                {
                    Active = true,
                    ScrubPhase = scrubPhase,
                    DepthNorm = smoothDepthNorm,
                    DeviceL0 = smoothDeviceL0,
                    GoingInward = goingInward,
                    PhysicalAxes = phys,
                    BodyGrindWeight = smoothBodyWeight,
                    MoverMode = evalStep.MoverMode,
                    GrindPattern = evalStep.GrindPattern,
                    GrindLat = gLat,
                    GrindFwd = gFwd,
                    GrindAxial = gAxial,
                    GrindYaw = gYaw,
                    GrindRoll = gRoll,
                    GrindPitch = gPitch,
                    GrindYawLag = gYawLag,
                    GrindRollLag = gRollLag,
                    GrindPitchLag = gPitchLag,
                    SpasmAmp = smoothSpasmAmp,
                    SpasmPhase = spasmPhase,
                    ClimaxPhase = currentClimaxPhase,
                    EjaculationPulseCount = ejaculationPulseCount
                };
            }
        }

        private static readonly double[,] OrganicMarkov = new double[6, 6]
        {
            // From 0=DeepGrind   -> high contrast: 38% FullThrust, 28% ShallowDeep, 20% TeasePlunge, 9% TipRub, 5% EdgeHover
            { 0.00, 0.38, 0.28, 0.20, 0.09, 0.05 },
            // From 1=FullThrust  -> 32% DeepGrind, 26% ShallowDeep, 20% TeasePlunge, 14% TipRub, 8% EdgeHover
            { 0.32, 0.00, 0.26, 0.20, 0.14, 0.08 },
            // From 2=ShallowDeep -> 34% DeepGrind, 30% FullThrust, 18% TeasePlunge, 12% TipRub, 6% EdgeHover
            { 0.34, 0.30, 0.00, 0.18, 0.12, 0.06 },
            // From 3=TeasePlunge -> 30% DeepGrind, 34% FullThrust, 20% ShallowDeep, 10% TipRub, 6% EdgeHover
            { 0.30, 0.34, 0.20, 0.00, 0.10, 0.06 },
            // From 4=TipRub      -> 28% DeepGrind, 32% FullThrust, 24% TeasePlunge, 16% ShallowDeep
            { 0.28, 0.32, 0.16, 0.24, 0.00, 0.00 },
            // From 5=EdgeHover   -> 35% TeasePlunge, 35% FullThrust, 20% DeepGrind, 10% TipRub
            { 0.20, 0.35, 0.00, 0.35, 0.10, 0.00 }
        };

        private static readonly GrindPattern[] OrganicPatterns = new[]
        {
            GrindPattern.Circle, GrindPattern.Figure8, GrindPattern.Corkscrew, GrindPattern.RockFB
        };

        private void AdvanceOrganicPhrase(TimelineStep parent)
        {
            if (organicRng == null)
                organicRng = parent.OrganicSeed != 0 ? new System.Random(parent.OrganicSeed) : new System.Random(Environment.TickCount ^ 0x5A3C91);

            int nextState;
            if (organicEffective == null)
            {
                nextState = organicRng.Next(0, 4); // Start strong with DeepGrind/FullThrust/ShallowDeep/TeasePlunge
            }
            else
            {
                double r = organicRng.NextDouble();
                double acc = 0.0;
                nextState = (organicState + 1) % 6;
                for (int j = 0; j < 6; j++)
                {
                    acc += OrganicMarkov[organicState, j];
                    if (r <= acc) { nextState = j; break; }
                }
            }
            organicState = nextState;

            double intensity = Numbers.Clamp(parent.OrganicIntensity, 0.0, 1.0);
            double ceil = parent.L0Max;
            double phraseDur = 4.5 + organicRng.NextDouble() * 4.0; // 4.5 .. 8.5s natural phrase length
            double speedScale = 0.72 + intensity * 0.56 + (organicRng.NextDouble() - 0.5) * 0.18;
            GrindPattern pat = OrganicPatterns[organicRng.Next(OrganicPatterns.Length)];

            TimelineStep sub;
            switch (nextState)
            {
                case 0: // DeepGrind
                    sub = TimelineStep.FromPreset(TimelinePreset.DeepGrind, phraseDur, ceil);
                    sub.GrindPattern = pat;
                    sub.GrindHz = Numbers.Clamp(1.0 + intensity * 0.55 + (organicRng.NextDouble() - 0.5) * 0.25, 0.7, 2.0);
                    sub.FreqHz = Numbers.Clamp(1.4 * speedScale, 0.9, 2.6);
                    organicSubModeName = "深埋碾磨(" + pat + ")";
                    break;
                case 1: // FullThrust (expressive full-stroke wave)
                    sub = TimelineStep.FromPreset(TimelinePreset.Custom, phraseDur, ceil);
                    sub.DepthMin = 0.04 + organicRng.NextDouble() * 0.12;
                    sub.DepthMax = 0.92 + organicRng.NextDouble() * 0.08;
                    sub.L0Min = 0.00;
                    sub.L0Max = ceil;
                    sub.FreqHz = Numbers.Clamp(1.65 * speedScale, 0.85, 3.1);
                    sub.InwardRatio = Numbers.Clamp(0.32 + organicRng.NextDouble() * 0.30, 0.24, 0.66);
                    sub.TwistAmp = 0.18 + organicRng.NextDouble() * 0.14;
                    sub.GrindAmp = 0.16 + organicRng.NextDouble() * 0.14;
                    sub.GrindHz = 1.0 + organicRng.NextDouble() * 0.6;
                    sub.GrindPattern = pat;
                    sub.BodyGrindScale = 0.55 + organicRng.NextDouble() * 0.30;
                    sub.PingPong = false;
                    organicSubModeName = "全幅律动";
                    break;
                case 2: // Shallow-Deep (3..5 shallow + 1 deep)
                    sub = TimelineStep.FromPreset(TimelinePreset.NineShallowOneDeep, phraseDur + 1.5, ceil);
                    sub.ShallowCount = organicRng.Next(3, 6);
                    sub.FreqHz = Numbers.Clamp(2.1 * speedScale, 1.3, 3.0);
                    sub.GrindPattern = pat;
                    sub.DeepHoldSeconds = 0.65 + organicRng.NextDouble() * 0.55;
                    organicSubModeName = sub.ShallowCount + "浅1深";
                    break;
                case 3: // TeasePlunge
                    sub = TimelineStep.FromPreset(TimelinePreset.TeasePlunge, phraseDur, ceil);
                    sub.FreqHz = Numbers.Clamp(0.85 * speedScale, 0.55, 1.45);
                    sub.InwardRatio = 0.16 + organicRng.NextDouble() * 0.12;
                    sub.PauseBottomSeconds = 0.35 + organicRng.NextDouble() * 0.45;
                    sub.PauseTopSeconds = 0.20 + organicRng.NextDouble() * 0.35;
                    sub.GrindPattern = pat;
                    organicSubModeName = "急顶慢拔";
                    break;
                case 4: // TipRub
                    sub = TimelineStep.FromPreset(TimelinePreset.TipRub, Math.Min(phraseDur, 5.5), ceil);
                    sub.FreqHz = Numbers.Clamp(2.7 * speedScale, 1.8, 3.8);
                    sub.GrindPattern = pat;
                    organicSubModeName = "高频磨顶";
                    break;
                default: // EdgeHover
                    sub = TimelineStep.FromPreset(TimelinePreset.EdgeHover, Math.Min(phraseDur, 4.5), ceil);
                    sub.GrindPattern = pat;
                    organicSubModeName = "入口寸止";
                    break;
            }
            sub.MoverMode = parent.MoverMode;
            sub.SpasmAmp = parent.SpasmAmp;
            sub.OrganicRandom = false; // Child step evaluates its concrete waveform directly
            organicEffective = sub.Sanitized();
            organicPhraseLeft = organicEffective.DurationSeconds;
            nsodCycleIndex = 0;
            nsodDeepHoldLeft = 0.0;
            lastEvent = "有机节奏切换: " + organicSubModeName;
        }

        private void EvaluateClimaxEjaculation(TimelineStep st, double dt, out double depthNorm, out double devL0, out double spasmTarget, ref bool usePingPong)
        {
            double dur = Math.Max(6.0, st.DurationSeconds);
            double t = Numbers.Clamp(stepElapsed, 0.0, dur);
            double p1End = Math.Min(2.6, dur * 0.24);
            double p2End = Math.Min(p1End + 4.2, dur * 0.65);

            if (t < p1End)
            {
                // Phase 1: Intense climax sprint building to peak release
                currentClimaxPhase = 1;
                double frac = t / Math.Max(0.2, p1End);
                double f = 2.4 + 0.9 * frac;
                cycleAccum = (cycleAccum + dt * f) % 1.0;
                double wave = AsymmetricCosine(cycleAccum, 0.44);
                depthNorm = 0.22 + wave * 0.78;
                devL0 = st.L0Max * (1.0 - 0.30 * frac) * (1.0 - wave) + st.L0Min * wave;
                spasmTarget = st.SpasmAmp * (0.25 + 0.45 * frac);
                usePingPong = true;
            }
            else if (t < p2End)
            {
                // Phase 2: Buried deep + 5 decaying rhythmic ejaculation pulses + strong decaying body spasms
                currentClimaxPhase = 2;
                double p2Dur = Math.Max(0.5, p2End - p1End);
                double p2t = t - p1End;
                double p2Frac = p2t / p2Dur;
                double decay = Math.Exp(-1.35 * p2Frac);

                // 5 decaying ejaculation pulses slowing gently from ~1.5Hz -> 0.9Hz
                double pulsePhase = p2Frac * 5.0;
                int pulseIdx = (int)Math.Floor(pulsePhase);
                if (pulseIdx > lastClimaxPulseIdx)
                {
                    lastClimaxPulseIdx = pulseIdx;
                    ejaculationPulseCount++;
                }
                double sub = pulsePhase - Math.Floor(pulsePhase);
                // Sharp inward squeeze followed by soft elastic release
                double pulse = sub < 0.28
                    ? Math.Sin((sub / 0.28) * Math.PI * 0.5)
                    : Math.Cos(((sub - 0.28) / 0.72) * Math.PI * 0.5);
                pulse = Math.Max(0.0, pulse);

                depthNorm = Numbers.Clamp(0.86 + 0.14 * pulse * (0.45 + 0.55 * decay), 0.0, 1.0);
                devL0 = Numbers.Clamp(st.L0Min + 0.14 * (1.0 - pulse * (0.45 + 0.55 * decay)), 0.0, 0.99);
                spasmTarget = st.SpasmAmp * (0.35 + 0.95 * (0.55 + 0.45 * pulse) * decay);
                usePingPong = true;
            }
            else
            {
                // Phase 3: Warm afterglow deep breathing rock (always keeps living visible motion!)
                currentClimaxPhase = 3;
                double p3Dur = Math.Max(0.5, dur - p2End);
                double p3Frac = Numbers.Clamp((t - p2End) / p3Dur, 0.0, 1.0);
                double fade = Math.Pow(1.0 - p3Frac, 1.4);
                double breath = 0.5 - 0.5 * Math.Cos((t - p2End) * 2.0 * Math.PI * 0.65);
                depthNorm = Numbers.Clamp(0.68 + 0.22 * breath * (0.35 + 0.65 * fade), 0.0, 1.0);
                devL0 = Numbers.Clamp(st.L0Min + 0.08 + 0.18 * (1.0 - breath * (0.35 + 0.65 * fade)), 0.0, 0.99);
                spasmTarget = st.SpasmAmp * 0.32 * fade;
                usePingPong = true;
            }
        }

        private static void EvaluateGrindPattern(GrindPattern pattern, double phi, double depthNorm,
            out double lat, out double fwd, out double axial, out double yaw, out double roll, out double pitch)
        {
            double s = Math.Sin(phi);
            double c = Math.Cos(phi);
            double s2 = Math.Sin(2.0 * phi);
            double c2 = Math.Cos(2.0 * phi);
            switch (pattern)
            {
                case GrindPattern.Figure8:
                    lat = s;
                    fwd = 0.85 * s2;
                    axial = 0.30 * c2;
                    yaw = c;
                    roll = s;
                    pitch = 0.85 * s2;
                    break;
                case GrindPattern.RockFB:
                    lat = 0.25 * s;
                    fwd = c;
                    axial = 0.45 * c;
                    yaw = 0.30 * s;
                    roll = 0.25 * s;
                    pitch = 1.00 * c;
                    break;
                case GrindPattern.Corkscrew:
                    lat = 0.70 * s;
                    fwd = 0.70 * c;
                    axial = 0.45 * Math.Sin(phi * 0.5);
                    yaw = 1.00 * s;
                    roll = 0.65 * c;
                    pitch = 0.65 * s;
                    break;
                case GrindPattern.LineFB:
                    // Pure straight front-back waist sway (L1 + R2 only; zero lateral, zero roll, zero circle, zero R0 yaw!)
                    lat = 0.0;
                    fwd = s;
                    axial = 0.25 * s;
                    yaw = 0.0;
                    roll = 0.0;
                    pitch = 1.00 * s;
                    break;
                case GrindPattern.LineLR:
                    // Pure straight left-right waist sway (L2 + R1 only; zero forward, zero pitch, zero circle, zero R0 yaw!)
                    lat = s;
                    fwd = 0.0;
                    axial = 0.0;
                    yaw = 0.0;
                    roll = 1.00 * s;
                    pitch = 0.0;
                    break;
                case GrindPattern.LineR1R2:
                    // Pure in-phase straight R1 + R2 diagonal tilt line (zero circular orbit, zero R0 twist!)
                    lat = 0.35 * s;
                    fwd = 0.35 * s;
                    axial = 0.0;
                    yaw = 0.0;
                    roll = 1.00 * s;
                    pitch = 1.00 * s;
                    break;
                case GrindPattern.PitchOnly:
                    // Pure R2 pitch rock
                    lat = 0.0;
                    fwd = 0.0;
                    axial = 0.0;
                    yaw = 0.0;
                    roll = 0.0;
                    pitch = 1.00 * s;
                    break;
                case GrindPattern.RollOnly:
                    // Pure R1 roll rock
                    lat = 0.0;
                    fwd = 0.0;
                    axial = 0.0;
                    yaw = 0.0;
                    roll = 1.00 * s;
                    pitch = 0.0;
                    break;
                default: // Circle
                    lat = s;
                    fwd = c;
                    axial = 0.35 * c2;
                    yaw = 0.85 * Math.Sin(phi + 0.35);
                    roll = s;
                    pitch = c;
                    break;
            }
        }

        private void ResetStepState(TimelineStep st)
        {
            cycleAccum = 0.0;
            nsodCycleIndex = 0;
            nsodDeepHoldLeft = 0.0;
            currentClimaxPhase = 0;
            lastClimaxPulseIdx = -1;
            organicEffective = null;
            organicPhraseLeft = 0.0;
            organicSubModeName = "";
            if (st != null && st.OrganicRandom)
            {
                organicRng = st.OrganicSeed != 0 ? new System.Random(st.OrganicSeed) : new System.Random(Environment.TickCount ^ 0x5A3C91);
            }
        }

        private void EvaluateStepCurve(TimelineStep st, double dt, out double depthNorm, out double devL0, ref bool usePingPong)
        {
            double frac = Numbers.Clamp(stepElapsed / Math.Max(0.1, st.DurationSeconds), 0.0, 1.0);
            double curFreq = st.FreqEndHz > 0 ? st.FreqHz + (st.FreqEndHz - st.FreqHz) * frac : st.FreqHz;

            if (st.HoldDepth >= 0.0 || st.Preset == TimelinePreset.WaistOnly)
            {
                // Pure fixed-depth waist-grind mode (zero L0 stroking!)
                depthNorm = st.DepthMax;
                devL0 = st.L0Max;
                usePingPong = true;
                return;
            }

            if (st.ShallowDeepMode || st.Preset == TimelinePreset.NineShallowOneDeep)
            {
                // Generalized N-shallow M-deep: N crisp yellow-band shallow thrusts followed by M full-stroke deep plunges!
                int totalInGroup = Math.Max(1, st.ShallowCount) + Math.Max(1, st.DeepCount);
                bool deepCycle = nsodCycleIndex >= st.ShallowCount;
                if (deepCycle && nsodDeepHoldLeft > 0)
                {
                    nsodDeepHoldLeft -= dt;
                    double pulse = 0.04 * (0.5 - 0.5 * Math.Cos(grindPhase * 2.0));
                    depthNorm = Numbers.Clamp(st.DepthMax - pulse, 0.0, 1.0);
                    devL0 = Numbers.Clamp(st.L0Min + pulse * 0.8, 0.0, 0.99);
                    return;
                }

                double effFreq = deepCycle ? Math.Max(0.75, curFreq * 0.55) : curFreq;
                double prevC = cycleAccum;
                cycleAccum += dt * effFreq;
                if (deepCycle && prevC < st.InwardRatio && cycleAccum >= st.InwardRatio && st.DeepHoldSeconds > 0 && nsodDeepHoldLeft <= 0)
                {
                    cycleAccum = st.InwardRatio;
                    nsodDeepHoldLeft = st.DeepHoldSeconds;
                    depthNorm = st.DepthMax;
                    devL0 = st.L0Min;
                    return;
                }
                if (cycleAccum >= 1.0)
                {
                    cycleAccum -= Math.Floor(cycleAccum);
                    nsodCycleIndex = (nsodCycleIndex + 1) % totalInGroup;
                    deepCycle = nsodCycleIndex >= st.ShallowCount;
                }

                double wave = AsymmetricCosine(cycleAccum, st.InwardRatio);
                if (deepCycle)
                {
                    // Full dramatic plunge from outer edge all the way to full depth & back!
                    depthNorm = st.DepthMin + wave * (st.DepthMax - st.DepthMin);
                    devL0 = st.L0Max - wave * (st.L0Max - st.L0Min);
                    usePingPong = false;
                }
                else
                {
                    double sBase = st.ShallowBaseDepth >= 0.0 ? st.ShallowBaseDepth : st.DepthMin;
                    double baseFrac = Numbers.Clamp((sBase - st.DepthMin) / Math.Max(1e-4, st.DepthMax - st.DepthMin), 0.0, 0.85);
                    double sL0Max = st.L0Max - baseFrac * (st.L0Max - st.L0Min);
                    double sL0Min = Math.Min(st.ShallowL0Min, Math.Max(st.L0Min, sL0Max - 0.12));
                    depthNorm = sBase + wave * (st.ShallowDepthMax - sBase);
                    devL0 = sL0Max - wave * (sL0Max - sL0Min);
                    usePingPong = true;
                }
                return;
            }

            // General parametric cycle with optional top/bottom dwell pauses & asymmetric speed
            double activePeriod = 1.0 / Math.Max(0.15, curFreq);
            double totalPeriod = activePeriod + st.PauseTopSeconds + st.PauseBottomSeconds;
            cycleAccum = (cycleAccum + dt / totalPeriod) % 1.0;

            double tIn = (activePeriod * st.InwardRatio) / totalPeriod;
            double tBot = tIn + st.PauseBottomSeconds / totalPeriod;
            double tOut = tBot + (activePeriod * (1.0 - st.InwardRatio)) / totalPeriod;

            double u;
            if (cycleAccum < tIn)
                u = 0.5 - 0.5 * Math.Cos(Math.PI * (cycleAccum / Math.Max(1e-5, tIn)));
            else if (cycleAccum < tBot)
                u = 1.0;
            else if (cycleAccum < tOut)
                u = 0.5 + 0.5 * Math.Cos(Math.PI * ((cycleAccum - tBot) / Math.Max(1e-5, tOut - tBot)));
            else
                u = 0.0;

            depthNorm = st.DepthMin + u * (st.DepthMax - st.DepthMin);
            devL0 = st.L0Max - u * (st.L0Max - st.L0Min);
        }

        private static double AsymmetricCosine(double phase, double inwardRatio)
        {
            double p = phase - Math.Floor(phase);
            double r = Numbers.Clamp(inwardRatio, 0.1, 0.9);
            if (p < r) return 0.5 - 0.5 * Math.Cos(Math.PI * (p / r));
            return 0.5 + 0.5 * Math.Cos(Math.PI * ((p - r) / (1.0 - r)));
        }
    }
}
