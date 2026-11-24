using System;

namespace KoiSync.Core
{
    public enum FeelPreset
    {
        Raw = 0,
        Balanced = 1,
        Intense = 2
    }

    public enum NonShaftFeelMode
    {
        Foreplay = 0, // Plan A (Default): outer surface window [22..80], WLoop GREEN / SLoop brisk GREEN-YELLOW, preserving deep [0..22] for true insertion contrast
        FullSync = 1, // Plan B: full [0..80] empirical stimulation for all active non-shaft animations
        Silent = 2    // Park gently at outer edge (L0 = 80) during non-shaft / foreplay / peeping
    }

    // Empirical Layer-2 feel translator derived from multi-axis SR6 library analysis:
    // - Keeps Layer-1 geometry truth untouched (direction, phase, contact & engagement)
    // - Maps shallow/compressed game strokes onto the empirical 0..80 golden L0 window
    // - Applies frequency-aware amplitude curve A(f) and >4.5Hz/climax deep-lock (0..26)
    // - Enhances under-expressed secondary axes (L1/L2/R0/R1/R2) with soft shoulders
    public sealed class FeelTranslator
    {
        public const double DefaultOuterCeiling = 0.99;
        private double outerCeiling = DefaultOuterCeiling; // Configurable device L0 ceiling (0.55..0.99; default 0.99 = 990)
        private string lastStage = "";
        private double winMin, winMax, rawEma;
        private double lastMinTime, lastMaxTime;
        private bool winReady;
        private double clock, lastRevClock, measuredPeriod;
        private double lastRaw;
        private int dir;
        private double climaxSweepTime;

        public double OuterCeiling
        {
            get { return outerCeiling; }
            set { outerCeiling = Numbers.Clamp(Numbers.Finite(value) ? value : DefaultOuterCeiling, 0.55, 0.99); }
        }
        public double EstimatedFreqHz { get; private set; }
        public double TargetAmplitude { get; private set; }
        public double EstimatedSpeedUnitsPerSec { get; private set; }
        public string HeatmapBand { get; private set; } = "蓝";
        public string Status { get; private set; } = "原味几何";

        public void Reset()
        {
            lastStage = "";
            winReady = false;
            winMin = winMax = rawEma = 0;
            lastMinTime = lastMaxTime = 0;
            clock = 0;
            lastRevClock = 0;
            measuredPeriod = 0;
            lastRaw = 0;
            dir = 0;
            climaxSweepTime = 0;
            EstimatedFreqHz = 0;
            TargetAmplitude = 0;
            EstimatedSpeedUnitsPerSec = 0;
            HeatmapBand = "蓝";
            Status = "原味几何";
        }

        public static double RecommendedRateCap(FeelPreset preset)
        {
            if (preset == FeelPreset.Intense) return 5.50;
            if (preset == FeelPreset.Balanced) return 4.60;
            return 2.00;
        }

        public static string BandLabel(double speedUnitsPerSec)
        {
            if (speedUnitsPerSec < 20) return "静止";
            if (speedUnitsPerSec < 100) return "蓝区";
            if (speedUnitsPerSec < 200) return "绿区";
            if (speedUnitsPerSec < 280) return "黄区";
            if (speedUnitsPerSec < 360) return "橙区";
            if (speedUnitsPerSec < 450) return "红区";
            return "深红";
        }

        // axes[] is in physical convention before SixAxisDrive inversion:
        // axes[0] = physical inward depth (0 = fully out, 1 = fully deep -> device L0 = 1 - axes[0])
        // axes[1..5] = L1, L2, R0, R1, R2 centered at 0.5
        public double[] Translate(
            double[] axes,
            NativeMotionResult measured,
            NativeMotionSample sample,
            FeelPreset preset,
            bool isForeplay,
            double dt)
        {
            return Translate(axes, measured, sample, preset, isForeplay, NonShaftFeelMode.Foreplay, dt);
        }

        public double[] Translate(
            double[] axes,
            NativeMotionResult measured,
            NativeMotionSample sample,
            FeelPreset preset,
            bool isNonShaft,
            NonShaftFeelMode nonShaftMode,
            double dt)
        {
            if (axes == null || axes.Length != 6) return axes;
            if (preset == FeelPreset.Raw || measured == null || sample == null)
            {
                Status = "原味几何 · 1:1 物理映射";
                HeatmapBand = "原味";
                return axes;
            }

            if (isNonShaft && nonShaftMode == NonShaftFeelMode.Silent)
            {
                Status = "非插入静默 · 待机等待插入";
                HeatmapBand = "静止";
                EstimatedFreqHz = 0;
                TargetAmplitude = 0;
                EstimatedSpeedUnitsPerSec = 0;
                return new[] { 1.0 - outerCeiling, 0.5, 0.5, 0.5, 0.5, 0.5 };
            }

            bool foreplayLayer = isNonShaft && nonShaftMode == NonShaftFeelMode.Foreplay;

            var output = (double[])axes.Clone();
            double safeDt = Numbers.Finite(dt) ? Numbers.Clamp(dt, 0, 0.1) : 0.016;
            clock += safeDt;

            string stage = sample.AnimationState ?? "";
            if (!string.Equals(stage, lastStage, StringComparison.Ordinal))
            {
                lastStage = stage;
                winReady = false;
                dir = 0;
                measuredPeriod = 0;
                lastRevClock = 0;
                climaxSweepTime = 0;
            }

            if (measured.Engagement <= 1e-4)
            {
                Status = "等待接触 · 保持退出位";
                HeatmapBand = "静止";
                EstimatedFreqHz = 0;
                TargetAmplitude = 0;
                EstimatedSpeedUnitsPerSec = 0;
                return output;
            }

            // Use un-squished linear depth ratio so deep poses (e.g. 135..180 mm) and shallow tip-grazing poses (-35..+10 mm) stay alive
            double rawInward = measured.Length > 1e-5
                ? Numbers.Clamp(measured.TargetDepth / measured.Length, -0.25, 1.5)
                : Numbers.Clamp(axes[0], 0, 1);

            // Estimate cycle frequency from Animator duration (fallback: observed reversal cadence)
            double cycleSec = Numbers.Finite(sample.AnimationDuration) && sample.AnimationDuration > 0.12 && sample.AnimationDuration < 8.0
                ? sample.AnimationDuration
                : 0;

            ObserveEnvelope(rawInward, safeDt, cycleSec > 0 ? cycleSec : 1.0);
            if (cycleSec <= 0)
                cycleSec = measuredPeriod > 0.12 && measuredPeriod < 8.0 ? measuredPeriod : 1.0;

            double freqHz = Numbers.Clamp(1.0 / cycleSec, 0.25, 10.0);
            EstimatedFreqHz = freqHz;

            var phase = ClassifyPhase(stage);
            if (phase == NativeStrokePhase.Out)
            {
                // Pull-out safety: keep device L0 <= outerCeiling
                output[0] = Math.Max(output[0], 1.0 - outerCeiling);
                Status = string.Format("拔出保护 · L0 ≤ {0:0}", outerCeiling * 100.0);
                HeatmapBand = "蓝区";
                return output;
            }

            double span = winMax - winMin;
            // Deadband: never fabricate motion on genuinely static poses (< 1.5% model length)
            double activity = SmoothStep(0.012, 0.040, span) * measured.Engagement;
            if (activity <= 1e-4)
            {
                Status = "静止阶段 · 保持几何原值";
                HeatmapBand = "静止";
                TargetAmplitude = 0;
                EstimatedSpeedUnitsPerSec = 0;
                return output;
            }

            double u = Numbers.Clamp((rawInward - winMin) / Math.Max(span, 1e-5), 0, 1); // 0=shallowest, 1=deepest
            bool intense = preset == FeelPreset.Intense;
            double devLow, devHigh; // Target window in DEVICE L0 coordinates (0=deep, 1=out)

            if (phase == NativeStrokePhase.Climax || freqHz > 6.0)
            {
                if (foreplayLayer)
                {
                    // Foreplay climax flutter stays shallow in [34..58], distinct from true insertion deep-lock [0..26]
                    devLow = intense ? 0.32 : 0.36;
                    devHigh = intense ? 0.58 : 0.60;
                }
                else if (freqHz >= 2.0 || stage.IndexOf("Orgasm", StringComparison.Ordinal) >= 0)
                {
                    // Empirical >2 Hz / rapid climax law: deep-locked rapid flutter inside device L0 0.02..0.26
                    devLow = intense ? 0.02 : 0.04;
                    devHigh = intense ? 0.24 : 0.26;
                }
                else
                {
                    // Slow inside climax/pulsing loop (e.g. 0.75 Hz SS_IN_Loop): deep rhythmic throb instead of tiny 33 u/s creep
                    devLow = intense ? 0.00 : 0.02;
                    devHigh = intense ? 0.50 : 0.44;
                }
                climaxSweepTime += safeDt;
                // Gentle R0/R1 micro-stimulation when raw rotation is quiet
                double sweep = 0.5 + 0.06 * Math.Sin(climaxSweepTime * 2.0 * Math.PI / 0.65);
                if (Math.Abs(output[3] - 0.5) < 0.03) output[3] = sweep;
                if (Math.Abs(output[4] - 0.5) < 0.03) output[4] = 1.0 - sweep;
            }
            else if (foreplayLayer)
            {
                // Plan A Foreplay layering (aibu / oral cunnilingus / fingering / solo / lesbian / peeping):
                // Strictly stays in outer surface window [22..80], preserving deep [0..22] exclusively
                // so transitioning into real insertion creates an unmistakable deep plunge contrast!
                bool fastStage = phase == NativeStrokePhase.Deep;
                double vTarget = fastStage
                    ? (intense ? 225.0 : 190.0)
                    : (intense ? 175.0 : 140.0);
                double minAmp = fastStage ? (intense ? 0.44 : 0.40) : (intense ? 0.36 : 0.34);
                double maxAmp = fastStage ? (intense ? 0.58 : 0.54) : (intense ? 0.54 : 0.53);
                double amp = Numbers.Clamp(vTarget / (200.0 * freqHz), minAmp, maxAmp);
                double floor = intense ? 0.22 : 0.26;
                devHigh = outerCeiling;
                devLow = Math.Max(floor, devHigh - amp);
            }
            else
            {
                // Empirical A(f) curve from 23.1h human multi-axis library:
                // Scales smoothly with configurable outerCeiling (default 0.99, adjustable 0.55..0.99)
                double speedCap = RecommendedRateCap(preset) * 100.0; // units/s
                double ceilingScale = outerCeiling / 0.80;
                double baseAmp;
                if (intense)
                {
                    if (freqHz <= 1.0) baseAmp = 0.68 * ceilingScale;
                    else if (freqHz <= 4.8) baseAmp = 0.78 * ceilingScale;
                    else baseAmp = Math.Max(0.72 * ceilingScale, Math.Min(0.78 * ceilingScale, speedCap / (200.0 * freqHz)));
                }
                else
                {
                    if (freqHz <= 1.0) baseAmp = 0.56 * ceilingScale;
                    else if (freqHz <= 1.6) baseAmp = 0.64 * ceilingScale;
                    else if (freqHz <= 4.8) baseAmp = 0.68 * ceilingScale;
                    else baseAmp = Math.Max(0.64 * ceilingScale, Math.Min(0.68 * ceilingScale, speedCap / (200.0 * freqHz)));
                }

                // Stage modulation: WLoop slightly gentler than SLoop/OLoop so speed transitions feel layered
                if (phase == NativeStrokePhase.Normal) baseAmp *= intense ? 0.90 : 0.85;
                else if (phase == NativeStrokePhase.Deep) baseAmp *= intense ? 1.03 : 1.00;
                baseAmp = Numbers.Clamp(baseAmp, 0.30, outerCeiling);

                // Depth-tier anchor preservation inside [0.00, outerCeiling]
                double rawMean = 0.5 * (winMin + winMax);
                if (rawMean >= 0.68)
                {
                    // Deep pose (cowgirl / 3P / OLoop): anchor bottom firmly at 0.00
                    devLow = 0.00;
                    devHigh = Math.Min(outerCeiling, baseAmp);
                }
                else if (rawMean <= 0.32)
                {
                    // Shallow pose (tip tease / shallow sitting): preserve outer top up to outerCeiling
                    devHigh = outerCeiling;
                    devLow = Math.Max(0.00, outerCeiling - baseAmp);
                }
                else
                {
                    // Standard insertion / oral: symmetric inside [0, outerCeiling] slightly biased deep
                    double mid = outerCeiling * 0.50;
                    double center = Numbers.Clamp(mid + (0.50 - rawMean) * 0.25, baseAmp * 0.5, outerCeiling - baseAmp * 0.5);
                    devLow = Numbers.Clamp(center - baseAmp * 0.5, 0.0, outerCeiling - baseAmp);
                    devHigh = Numbers.Clamp(devLow + baseAmp, devLow, outerCeiling);
                }
            }

            // Convert target device L0 [devLow..devHigh] back to physical inward depth (inward = 1 - deviceL0)
            // Deepest point (u=1) -> devLow (inward = 1 - devLow); shallowest (u=0) -> devHigh (inward = 1 - devHigh)
            double targetDeviceL0 = devHigh - u * (devHigh - devLow);
            double targetInward = 1.0 - Numbers.Clamp(targetDeviceL0, 0, outerCeiling);
            output[0] = axes[0] + (targetInward - axes[0]) * activity;

            // Secondary-axis empirical coupling enhancement (with C1 soft shoulder inside [0.06, 0.94])
            double[] secGains = intense
                ? new[] { 1.0, 1.55, 1.20, 2.30, 1.70, 2.00 }
                : new[] { 1.0, 1.30, 1.10, 1.80, 1.40, 1.55 };
            for (int i = 1; i < 6; i++)
            {
                double boosted = SoftBoostAroundCenter(axes[i], secGains[i], 0.44);
                output[i] = axes[i] + (boosted - axes[i]) * activity;
            }

            double ampUnits = (devHigh - devLow) * 100.0;
            double estSpeed = Math.Min(RecommendedRateCap(preset) * 100.0, 2.0 * freqHz * ampUnits);
            TargetAmplitude = ampUnits;
            EstimatedSpeedUnitsPerSec = estSpeed;
            HeatmapBand = BandLabel(estSpeed);
            string modeTag = foreplayLayer ? (intense ? "前戏偏强" : "前戏柔抚")
                : (isNonShaft ? (intense ? "非插入强爽" : "非插入全幅") : (intense ? "强爽激进" : "均衡体感"));
            Status = string.Format(
                "{0} · {1:0.0}Hz · L0 {2:0}–{3:0} · {4}({5:0}u/s)",
                modeTag,
                freqHz,
                devLow * 100.0,
                devHigh * 100.0,
                HeatmapBand,
                estSpeed);

            return output;
        }

        private void ObserveEnvelope(double raw, double dt, double cycleSec)
        {
            if (!winReady)
            {
                winMin = winMax = rawEma = lastRaw = raw;
                lastMinTime = lastMaxTime = clock;
                winReady = true;
                return;
            }

            // Track fast operating center so stale stage-transition spikes shrink toward real motion
            double emaTau = Math.Max(0.35, cycleSec * 0.55);
            rawEma += (raw - rawEma) * (1.0 - Math.Exp(-dt / emaTau));

            // Instant outward expansion + touch timestamps
            double curSpan = Math.Max(0.01, winMax - winMin);
            if (raw <= winMin) { winMin = raw; lastMinTime = clock; }
            else if (raw <= winMin + curSpan * 0.08) lastMinTime = clock;

            if (raw >= winMax) { winMax = raw; lastMaxTime = clock; }
            else if (raw >= winMax - curSpan * 0.08) lastMaxTime = clock;

            // Gentle periodic decay while bounds are freshly visited (< 1 cycle);
            // rapid stale-bound recovery when an initial transition spike goes unvisited > 1 cycle.
            double staleAge = Math.Max(0.65, cycleSec * 1.05);
            double gentleTau = Numbers.Clamp(cycleSec * 1.4, 0.9, 2.2);
            double recoverTau = Math.Max(0.22, cycleSec * 0.30);

            double minRate = (clock - lastMinTime) > staleAge
                ? (1.0 - Math.Exp(-dt / recoverTau))
                : (1.0 - Math.Exp(-dt / gentleTau)) * 0.18;
            double maxRate = (clock - lastMaxTime) > staleAge
                ? (1.0 - Math.Exp(-dt / recoverTau))
                : (1.0 - Math.Exp(-dt / gentleTau)) * 0.18;

            winMin += (rawEma - winMin) * minRate;
            winMax += (rawEma - winMax) * maxRate;
            if (raw < winMin) { winMin = raw; lastMinTime = clock; }
            if (raw > winMax) { winMax = raw; lastMaxTime = clock; }

            // Track reversal cadence as fallback when AnimationDuration is unavailable
            double delta = raw - lastRaw;
            if (Math.Abs(delta) > 0.004)
            {
                int nextDir = delta > 0 ? 1 : -1;
                if (dir != 0 && nextDir != dir)
                {
                    if (lastRevClock > 0)
                    {
                        double fullPeriod = (clock - lastRevClock) * 2.0;
                        if (fullPeriod > 0.12 && fullPeriod < 6.0)
                            measuredPeriod = measuredPeriod <= 0 ? fullPeriod : measuredPeriod * 0.65 + fullPeriod * 0.35;
                    }
                    lastRevClock = clock;
                }
                dir = nextDir;
                lastRaw = raw;
            }
        }

        private static NativeStrokePhase ClassifyPhase(string state)
        {
            if (string.IsNullOrEmpty(state)) return NativeStrokePhase.Measured;
            if (state.StartsWith("A_", StringComparison.Ordinal)) state = state.Substring(2);
            switch (state)
            {
                case "WLoop":
                    return NativeStrokePhase.Normal;
                case "SLoop":
                case "MLoop":
                case "OLoop":
                    return NativeStrokePhase.Deep;
                case "Pull":
                case "OUT_Start":
                case "OUT_Loop":
                case "OUT_A":
                case "M_OUT_Start":
                case "M_OUT_Loop":
                case "M_OUT_A":
                case "WS_OUT_Start":
                case "WS_OUT_Loop":
                case "WS_OUT_A":
                case "SS_OUT_Start":
                case "SS_OUT_Loop":
                case "SS_OUT_A":
                    return NativeStrokePhase.Out;
                case "IN_Start":
                case "IN_Loop":
                case "M_IN_Start":
                case "M_IN_Loop":
                case "WF_IN_Start":
                case "WF_IN_Loop":
                case "WS_IN_Start":
                case "WS_IN_Loop":
                case "SF_IN_Start":
                case "SF_IN_Loop":
                case "SS_IN_Start":
                case "SS_IN_Loop":
                case "WOrgasm":
                case "SOrgasm":
                case "Orgasm":
                    return NativeStrokePhase.Climax;
                default:
                    return NativeStrokePhase.Measured;
            }
        }

        private static double SmoothStep(double edge0, double edge1, double x)
        {
            double t = Numbers.Clamp((x - edge0) / Math.Max(edge1 - edge0, 1e-9), 0, 1);
            return t * t * (3.0 - 2.0 * t);
        }

        private static double SoftBoostAroundCenter(double value, double gain, double maxHalfSpan)
        {
            double offset = (value - 0.5) * gain;
            double abs = Math.Abs(offset);
            double knee = maxHalfSpan * 0.65;
            if (abs <= knee) return Numbers.Clamp(0.5 + offset, 0, 1);
            double excess = abs - knee;
            double shoulder = maxHalfSpan - knee;
            double compressed = knee + shoulder * (excess / (shoulder + excess));
            return Numbers.Clamp(0.5 + Math.Sign(offset) * compressed, 0, 1);
        }
    }
}
