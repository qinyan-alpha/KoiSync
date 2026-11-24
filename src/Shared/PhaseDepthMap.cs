using System;

namespace KoiSync.Core
{
    // Learns the exact 1-cycle mapping between an animation's normalizedTime phase [0, 1)
    // and Layer-1 physical insertion depth (TargetDepth), enabling arbitrary Timeline-curve
    // scrubbing (shallow tip-rub, deep bottom-grind, 9-shallow-1-deep, asymmetric tease, etc.).
    public sealed class PhaseDepthMap
    {
        public const int BinCount = 64;
        private readonly double[] rawBins = new double[BinCount];
        private readonly bool[] filled = new bool[BinCount];
        private readonly double[] smoothDepth = new double[BinCount];
        private readonly double[] normDepth = new double[BinCount];
        private string identity = "";
        private int filledCount;
        private bool compiled;

        private int minBin;
        private int maxBin;
        private double minDepth;
        private double maxDepth;
        private double femalePelvisSpan;
        private double malePelvisSpan;
        private double femaleMoverShare = 0.5;
        private bool moverMeasured;

        public string Identity { get { return identity; } }
        public int FilledBins { get { return filledCount; } }
        public double Coverage { get { return (double)filledCount / BinCount; } }
        public double MinDepthMeters { get { return minDepth; } }
        public double MaxDepthMeters { get { return maxDepth; } }
        public double DepthSpanMeters { get { return Math.Max(0.0, maxDepth - minDepth); } }
        public double ShallowPhase { get { return (double)minBin / BinCount; } }
        public double DeepPhase { get { return (double)maxBin / BinCount; } }
        public double FemalePelvisSpanMeters { get { return femalePelvisSpan; } }
        public double MalePelvisSpanMeters { get { return malePelvisSpan; } }
        public double FemaleMoverShare { get { return femaleMoverShare; } }
        public bool MoverMeasured { get { return moverMeasured; } }
        public bool Ready { get { return compiled && filledCount >= 16 && (maxDepth - minDepth) >= 0.004; } }

        public void Reset(string newIdentity = "")
        {
            identity = newIdentity ?? "";
            Array.Clear(rawBins, 0, BinCount);
            Array.Clear(filled, 0, BinCount);
            Array.Clear(smoothDepth, 0, BinCount);
            Array.Clear(normDepth, 0, BinCount);
            filledCount = 0;
            compiled = false;
            minBin = 0;
            maxBin = BinCount / 2;
            minDepth = 0;
            maxDepth = 0;
            femalePelvisSpan = 0;
            malePelvisSpan = 0;
            femaleMoverShare = 0.5;
            moverMeasured = false;
        }

        // Records measured 1-cycle world-space pelvis excursion for Female vs Male
        // so ProceduralBodyGrind automatically drives the real active mover (e.g. Female on cowgirl, Male on missionary).
        public void SetMoverExcursion(double femaleSpanMeters, double maleSpanMeters, bool isFemaleInitiative)
        {
            femalePelvisSpan = Numbers.Finite(femaleSpanMeters) && femaleSpanMeters > 0 ? femaleSpanMeters : 0.0;
            malePelvisSpan = Numbers.Finite(maleSpanMeters) && maleSpanMeters > 0 ? maleSpanMeters : 0.0;
            double total = femalePelvisSpan + malePelvisSpan;
            if (total >= 0.018)
            {
                double rawShare = Numbers.Clamp(femalePelvisSpan / total, 0.0, 1.0);
                // Only use native isFemaleInitiative as gentle tiebreaker when both hips move roughly equally
                if (rawShare > 0.36 && rawShare < 0.64)
                {
                    double bias = isFemaleInitiative ? 0.72 : 0.28;
                    femaleMoverShare = Numbers.Clamp(rawShare * 0.70 + bias * 0.30, 0.0, 1.0);
                }
                else
                {
                    femaleMoverShare = rawShare;
                }
                moverMeasured = true;
            }
            else
            {
                femaleMoverShare = isFemaleInitiative ? 0.85 : 0.15;
                moverMeasured = false;
            }
        }

        public void Observe(string mapIdentity, double phase, double depthMeters)
        {
            if (!Numbers.Finite(phase) || !Numbers.Finite(depthMeters)) return;
            if (!string.Equals(identity, mapIdentity ?? "", StringComparison.Ordinal))
                Reset(mapIdentity);
            double wrapped = phase - Math.Floor(phase);
            int idx = (int)Math.Floor(wrapped * BinCount);
            if (idx < 0) idx = 0;
            if (idx >= BinCount) idx = BinCount - 1;
            if (!filled[idx])
            {
                rawBins[idx] = depthMeters;
                filled[idx] = true;
                filledCount++;
            }
            else
            {
                rawBins[idx] = rawBins[idx] * 0.7 + depthMeters * 0.3;
            }
            compiled = false;
            if (filledCount >= 16) Compile();
        }

        public void Compile()
        {
            if (filledCount < 4) { compiled = false; return; }

            // 1. Circularly interpolate any unfilled bins
            var dense = new double[BinCount];
            for (int i = 0; i < BinCount; i++)
            {
                if (filled[i]) { dense[i] = rawBins[i]; continue; }
                int prev = -1, next = -1;
                for (int d = 1; d < BinCount; d++)
                {
                    int li = (i - d + BinCount) % BinCount;
                    if (prev < 0 && filled[li]) prev = d;
                    int ri = (i + d) % BinCount;
                    if (next < 0 && filled[ri]) next = d;
                    if (prev >= 0 && next >= 0) break;
                }
                if (prev < 0 || next < 0) dense[i] = 0;
                else
                {
                    double lv = rawBins[(i - prev + BinCount) % BinCount];
                    double rv = rawBins[(i + next) % BinCount];
                    double t = (double)prev / (prev + next);
                    dense[i] = lv + (rv - lv) * t;
                }
            }

            // 2. Light 3-tap circular smoothing to suppress single-frame jitter
            minDepth = double.MaxValue;
            maxDepth = double.MinValue;
            minBin = 0;
            maxBin = BinCount / 2;
            for (int i = 0; i < BinCount; i++)
            {
                int l = (i - 1 + BinCount) % BinCount;
                int r = (i + 1) % BinCount;
                double v = dense[l] * 0.25 + dense[i] * 0.50 + dense[r] * 0.25;
                smoothDepth[i] = v;
                if (v < minDepth) { minDepth = v; minBin = i; }
                if (v > maxDepth) { maxDepth = v; maxBin = i; }
            }

            double span = Math.Max(1e-6, maxDepth - minDepth);
            for (int i = 0; i < BinCount; i++)
                normDepth[i] = Numbers.Clamp((smoothDepth[i] - minDepth) / span, 0.0, 1.0);

            compiled = true;
        }

        // Map a desired normalized insertion depth d in [0..1] (0=shallowest, 1=deepest)
        // onto the exact animation normalizedTime phase in [0..1).
        // - pingPong=true stays on ONE continuous monotonic branch (inward arc) so rapid
        //   sub-arc micro-strokes (tip_rub, deep_grind) never pop across opposite half-poses!
        // - pingPong=false follows the natural circular loop (inward arc when goingInward,
        //   outward arc when withdrawing) for full-range strokes.
        public double MapDepthToPhase(double targetDepthNorm, bool goingInward, bool pingPong)
        {
            if (!compiled) Compile();
            if (!Ready) return Numbers.Clamp(targetDepthNorm, 0.0, 0.999);

            double d = Numbers.Clamp(targetDepthNorm, 0.0, 1.0);
            bool useInwardArc = pingPong || goingInward;
            int start = useInwardArc ? minBin : maxBin;
            int end = useInwardArc ? maxBin : minBin;
            int arcSteps = (end - start + BinCount) % BinCount;
            if (arcSteps < 2) return (double)start / BinCount;

            if (useInwardArc)
            {
                // Monotonically non-decreasing cumulative envelope from minBin -> maxBin
                double prevVal = 0.0;
                for (int s = 1; s <= arcSteps; s++)
                {
                    int bi = (start + s) % BinCount;
                    double curVal = s == arcSteps ? 1.0 : Math.Max(prevVal, normDepth[bi]);
                    if (d <= curVal + 1e-6)
                    {
                        double frac = curVal > prevVal + 1e-6 ? Numbers.Clamp((d - prevVal) / (curVal - prevVal), 0.0, 1.0) : 0.0;
                        double p = (start + (s - 1) + frac) / BinCount;
                        return p - Math.Floor(p);
                    }
                    prevVal = curVal;
                }
                return (double)end / BinCount;
            }
            else
            {
                // Monotonically non-increasing cumulative envelope from maxBin -> minBin
                double prevVal = 1.0;
                for (int s = 1; s <= arcSteps; s++)
                {
                    int bi = (start + s) % BinCount;
                    double curVal = s == arcSteps ? 0.0 : Math.Min(prevVal, normDepth[bi]);
                    if (d >= curVal - 1e-6)
                    {
                        double frac = prevVal > curVal + 1e-6 ? Numbers.Clamp((prevVal - d) / (prevVal - curVal), 0.0, 1.0) : 0.0;
                        double p = (start + (s - 1) + frac) / BinCount;
                        return p - Math.Floor(p);
                    }
                    prevVal = curVal;
                }
                return (double)end / BinCount;
            }
        }
    }
}
