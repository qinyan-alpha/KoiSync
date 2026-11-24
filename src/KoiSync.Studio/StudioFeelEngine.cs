using System;
using KoiSync.Core;

namespace KoiSync.Studio
{
    public sealed class StudioFeelEngine
    {
        private readonly FeelTranslator translator = new FeelTranslator();
        private string lastChapterKey = "";

        public FeelPreset Preset = FeelPreset.Balanced;
        public NonShaftFeelMode NonShaftMode = NonShaftFeelMode.Foreplay;

        public double OuterCeiling
        {
            get { return translator.OuterCeiling; }
            set { translator.OuterCeiling = value; }
        }

        public double EstimatedFreqHz { get { return translator.EstimatedFreqHz; } }
        public double TargetAmplitude { get { return translator.TargetAmplitude; } }
        public double EstimatedSpeedUnitsPerSec { get { return translator.EstimatedSpeedUnitsPerSec; } }
        public string HeatmapBand { get { return translator.HeatmapBand; } }
        public string Status { get { return translator.Status; } }

        public void Reset()
        {
            translator.Reset();
            lastChapterKey = "";
        }

        public static FeelPreset ParsePreset(string text)
        {
            string s = (text ?? "").Trim().ToLowerInvariant();
            if (s == "raw" || s == "0" || s == "absolute" || s == "绝对真实" || s == "原味") return FeelPreset.Raw;
            if (s == "intense" || s == "2" || s == "high" || s == "高刺激") return FeelPreset.Intense;
            return FeelPreset.Balanced;
        }

        public static double RecommendedRateCap(FeelPreset preset)
        {
            return FeelTranslator.RecommendedRateCap(preset);
        }

        // Apply optional chapter-prescanned [p05..p95] depth normalization plus
        // shared Layer-2 FeelTranslator (Raw / Balanced / Intense).
        // axes[0] is physical inward depth (0 = outer, 1 = deep), axes[1..5] centered at 0.5.
        public double[] Process(
            double[] axes,
            NativeMotionResult measured,
            NativeMotionSample sample,
            string chapterKey,
            int openingKind,
            bool isClimax,
            double chapterDepthP05,
            double chapterDepthP95,
            double chapterPeakHz,
            double dt)
        {
            if (axes == null || axes.Length != 6) return axes;
            if (Preset == FeelPreset.Raw || measured == null || sample == null)
            {
                if (chapterKey != lastChapterKey)
                {
                    lastChapterKey = chapterKey ?? "";
                    translator.Reset();
                }
                return axes;
            }

            string key = (chapterKey ?? "") + ":" + (isClimax ? "climax" : "main");
            if (!string.Equals(key, lastChapterKey, StringComparison.Ordinal))
            {
                lastChapterKey = key;
                translator.Reset();
            }

            bool isNonShaft = openingKind >= 3; // LeftHand, RightHand, Breast, HandVaginal
            bool isHandOnShaft = openingKind == 3 || openingKind == 4; // LeftHand, RightHand directly stroking shaft
            var workingAxes = (double[])axes.Clone();
            var workingSample = CopySampleForFeel(sample, key, isClimax, chapterPeakHz);
            var workingMeasured = measured;

            // Chapter-level prescanned range normalization: solves Studio's classic
            // shallow 3..7cm pelvis excursion, light fingertip teasing, and deep over-insertion bottom-outs
            // without forcing the user into Manual mode.
            double span = chapterDepthP95 - chapterDepthP05;
            double minSpan = isHandOnShaft ? 0.025 : 0.04;
            if (Numbers.Finite(chapterDepthP05) && Numbers.Finite(chapterDepthP95) && span >= minSpan && measured.Length > 1e-5)
            {
                double rawDepthRatio = measured.TargetDepth / measured.Length;
                double norm = Numbers.Clamp((rawDepthRatio - chapterDepthP05) / span, -0.10, 1.10);
                bool overInserted = chapterDepthP95 > 0.96 || isHandOnShaft;
                double blend = Preset == FeelPreset.Intense
                    ? (overInserted ? 0.95 : 0.85)
                    : (overInserted ? 0.88 : 0.68);
                double targetLow = Preset == FeelPreset.Intense ? 0.01 : 0.05;
                double targetHigh = Preset == FeelPreset.Intense ? 0.99 : 0.95;
                double stretched = Numbers.Clamp(targetLow + norm * (targetHigh - targetLow), 0.0, 1.0);
                double rawClamped = Numbers.Clamp(rawDepthRatio, 0.0, 1.0);
                double blendedRatio = Numbers.Clamp(rawClamped * (1.0 - blend) + stretched * blend, 0.0, 1.0);

                workingAxes[0] = blendedRatio;
                workingMeasured = new NativeMotionResult
                {
                    Raw = measured.Raw,
                    Normalized = workingAxes,
                    Engagement = isHandOnShaft ? Math.Max(0.85, measured.Engagement) : measured.Engagement,
                    GeometricEngagement = isHandOnShaft ? Math.Max(0.85, measured.GeometricEngagement) : measured.GeometricEngagement,
                    ContactDistance = measured.ContactDistance,
                    TargetDepth = blendedRatio * measured.Length,
                    Length = measured.Length
                };
            }

            NonShaftFeelMode effNonShaft = isHandOnShaft ? NonShaftFeelMode.FullSync : NonShaftMode;
            return translator.Translate(workingAxes, workingMeasured, workingSample, Preset, isNonShaft, effNonShaft, dt);
        }

        private static NativeMotionSample CopySampleForFeel(NativeMotionSample src, string chapterKey, bool isClimax, double chapterPeakHz)
        {
            double cycleSec = Numbers.Finite(chapterPeakHz) && chapterPeakHz >= 0.2 && chapterPeakHz <= 8.0
                ? 1.0 / chapterPeakHz
                : src.AnimationDuration;
            return new NativeMotionSample
            {
                Studio = true,
                HasShaftPose = src.HasShaftPose,
                RootDistance = src.RootDistance,
                HasBodyFrame = src.HasBodyFrame,
                HasTargetFrame = src.HasTargetFrame,
                Origin = src.Origin,
                End = src.End,
                Target = src.Target,
                AnchorRoot = src.AnchorRoot,
                AnchorEnd = src.AnchorEnd,
                AnchorTarget = src.AnchorTarget,
                ShaftPath = src.ShaftPath,
                ShaftRestLength = src.ShaftRestLength,
                ReferenceFrame = src.ReferenceFrame,
                BodyFrame = src.BodyFrame,
                TargetReferenceFrame = src.TargetReferenceFrame,
                TargetBodyFrame = src.TargetBodyFrame,
                SourceRootDirection = src.SourceRootDirection,
                SourceDeformedDirection = src.SourceDeformedDirection,
                ShaftYaw = src.ShaftYaw,
                ShaftTwistHalfTurn = src.ShaftTwistHalfTurn,
                ShaftContact = src.ShaftContact,
                HasExplicitContact = src.HasExplicitContact,
                ContactSequence = src.ContactSequence,
                ContactDeltaTime = src.ContactDeltaTime,
                LateralSpan = src.LateralSpan,
                PlaneWidth = src.PlaneWidth,
                PlaneDepth = src.PlaneDepth,
                AnimationState = isClimax ? "OLoop_" + chapterKey : (chapterPeakHz >= 1.6 ? "SLoop_" + chapterKey : "WLoop_" + chapterKey),
                AnimationDuration = cycleSec
            };
        }
    }
}
