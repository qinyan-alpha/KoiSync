using System;
using System.Collections.Generic;
using KoiSync.Core;
using KoiSync.Studio;

internal static class StepBakeAndFeelChecks
{
    public static void Run(Action<bool, string> check)
    {
        // 1. StudioStepAnalyzer: Anti-Anal-Flip guard & review flag
        var frames = new List<StepBakeFrame>();
        for (int i = 0; i <= 600; i++)
        {
            double t = i * 0.05; // 0..30s
            var f = new StepBakeFrame { Time = t };
            double wave = 0.5 + 0.35 * Math.Sin(2.0 * Math.PI * (t < 15.0 ? 0.8 : 2.2) * t);
            if (t < 15.0)
            {
                // Ambiguous 3cm vaginal vs anal without explicit attachment:
                // Anal slightly closer by 10%, must still lock to Vaginal AND set NeedsReview=true!
                f.Observations.Add(new StepOpeningObservation
                {
                    SourceKey = 1, TargetKey = 2, Opening = 0, SourceName = "M", TargetName = "F",
                    Distance = 0.022, Length = 0.16, Insertion = 0.55, AlignmentCos = 0.80, RawDepthRatio = 0.20 + 0.35 * wave
                });
                f.Observations.Add(new StepOpeningObservation
                {
                    SourceKey = 1, TargetKey = 2, Opening = 1, SourceName = "M", TargetName = "F",
                    Distance = 0.018, Length = 0.16, Insertion = 0.58, AlignmentCos = 0.84, RawDepthRatio = 0.20 + 0.35 * wave
                });
                f.RawL0 = 1.0 - (0.20 + 0.35 * wave);
            }
            else if (t < 24.0)
            {
                // Clear explicit Vaginal fast sprint (2.2 Hz)
                f.Observations.Add(new StepOpeningObservation
                {
                    SourceKey = 1, TargetKey = 2, Opening = 0, SourceName = "M", TargetName = "F",
                    Distance = 0.010, Length = 0.16, Insertion = 0.80, AlignmentCos = 0.92, RawDepthRatio = 0.15 + 0.70 * wave,
                    ExplicitAttached = true
                });
                f.RawL0 = 1.0 - (0.15 + 0.70 * wave);
            }
            else
            {
                // Climax burst (24..30s): 3 sparse props ON + deep-hold + BlendShape spike!
                f.Observations.Add(new StepOpeningObservation
                {
                    SourceKey = 1, TargetKey = 2, Opening = 0, SourceName = "M", TargetName = "F",
                    Distance = 0.008, Length = 0.16, Insertion = 0.90, AlignmentCos = 0.95, RawDepthRatio = 0.82 + 0.04 * Math.Sin(t * 20),
                    ExplicitAttached = true
                });
                f.RawL0 = 0.18;
                f.ActiveSparseProps = t >= 24.5 ? 4 : 0;
                f.ActiveFluidProps = t >= 24.5 ? 4 : 0;
                f.BlendShapeDelta = 18.0;
            }
            if (t < 24.0) f.BlendShapeDelta = 2.0;
            frames.Add(f);
        }

        var analysis = StudioStepAnalyzer.Analyze(frames, new[] { 15.0 }, 30.0);
        check(analysis.Chapters.Count >= 3, "StepAnalyzer splits hint boundaries + climax burst into >=3 chapters");
        var ch1 = analysis.Chapters[0];
        check(ch1.Opening == 0, "Anti-Anal-Flip guard keeps Vaginal when Anal lacks >=28% margin without explicit attachment");
        check(ch1.NeedsReview, "Close Vaginal/Anal chapter is automatically flagged for user review");
        check(ch1.DepthP95 > ch1.DepthP05 + 0.15, "Chapter percentile depth calibration measures real excursion");

        var ch2 = analysis.Chapters[1];
        check(ch2.Opening == 0 && !ch2.NeedsReview, "Explicitly attached Vaginal chapter has high confidence and no review flag");
        check(ch2.PeakHz >= 1.8 && ch2.Loopable, "Fast periodic chapter detects high frequency and valid loop seam");

        check(analysis.ClimaxCount >= 1, "3-signal climax detector catches sparse props + deep-hold + expression spike");
        check(analysis.Segments.Count == analysis.Chapters.Count, "StepAnalyzer produces 1:1 StudioDirectorSegment list");

        // 2. StudioFeelEngine: Raw vs Balanced vs Intense on shallow Studio stroke (0.20..0.50)
        var feel = new StudioFeelEngine();
        var origin = new MotionVector();
        var up = new MotionVector(0, 1, 0);
        var frame = AlignmentFrame.Build(origin, new MotionVector(1, 0, 0), up);
        var sample = new NativeMotionSample
        {
            Studio = true, HasShaftPose = true, HasTargetFrame = true,
            ReferenceFrame = frame, TargetReferenceFrame = frame,
            Origin = origin, End = up * 0.16, Target = up * 0.08,
            ShaftRestLength = 0.16, ShaftPath = new[] { origin, up * 0.16 }
        };

        double rawMin = 1, rawMax = 0, balMin = 1, balMax = 0, intMin = 1, intMax = 0;
        foreach (FeelPreset preset in new[] { FeelPreset.Raw, FeelPreset.Balanced, FeelPreset.Intense })
        {
            feel.Reset();
            feel.Preset = preset;
            double lo = 1, hi = 0;
            for (int i = 0; i < 120; i++)
            {
                double depthRatio = 0.35 + 0.15 * Math.Sin(i * 0.15); // shallow 0.20..0.50 excursion
                var measured = new NativeMotionResult
                {
                    Raw = new[] { depthRatio, 0, 0, 0, 0, 0 },
                    Normalized = new[] { depthRatio, 0.52, 0.48, 0.51, 0.52, 0.49 },
                    Engagement = 1.0, GeometricEngagement = 1.0, Length = 0.16, TargetDepth = depthRatio * 0.16
                };
                var outAxes = feel.Process(measured.Normalized, measured, sample, "ch1", 0, false, 0.20, 0.50, 1.2, 0.02);
                if (i >= 40)
                {
                    if (outAxes[0] < lo) lo = outAxes[0];
                    if (outAxes[0] > hi) hi = outAxes[0];
                }
            }
            if (preset == FeelPreset.Raw) { rawMin = lo; rawMax = hi; }
            else if (preset == FeelPreset.Balanced) { balMin = lo; balMax = hi; }
            else { intMin = lo; intMax = hi; }
        }
        check(Math.Abs((rawMax - rawMin) - 0.30) < 0.02, "Feel Raw preserves 1:1 shallow 0.30 excursion");
        check((balMax - balMin) > (rawMax - rawMin) + 0.12, "Feel Balanced expands shallow chapter stroke via P05..P95 + FeelTranslator");
        check((intMax - intMin) >= (balMax - balMin) - 0.02 && intMax >= balMax - 0.02, "Feel Intense reaches deeper/wider dynamic range than Balanced");

        // 3. Chapter XML round-trip with new prescan metadata
        var book = new ChapterBook();
        book.Observe(0, new Chapter { Source = 1, Target = 2, Opening = 0, DepthP05 = 0.18, DepthP95 = 0.62, PeakHz = 1.45, NeedsReview = true, ReviewNote = "Verify opening" });
        string xml = book.ToXml();
        var loaded = new ChapterBook();
        loaded.FromXml(xml);
        check(loaded.Chapters.Count == 1 && loaded.Chapters[0].NeedsReview && loaded.Chapters[0].ReviewNote == "Verify opening", "ChapterBook round-trips NeedsReview & ReviewNote");
        check(Math.Abs(loaded.Chapters[0].DepthP05 - 0.18) < 1e-6 && Math.Abs(loaded.Chapters[0].DepthP95 - 0.62) < 1e-6 && Math.Abs(loaded.Chapters[0].PeakHz - 1.45) < 1e-6, "ChapterBook round-trips DepthP05/DepthP95/PeakHz");

        // 4. Over-insertion Studio stroke (depthRatio 0.82..1.38) stays unclipped in Feel Balanced
        feel.Reset();
        feel.Preset = FeelPreset.Balanced;
        double ovLo = 1, ovHi = 0;
        for (int i = 0; i < 120; i++)
        {
            double depthRatio = 1.10 + 0.28 * Math.Sin(i * 0.16); // 0.82..1.38
            var measured = new NativeMotionResult
            {
                Raw = new[] { Math.Min(1.0, depthRatio) * 0.10, 0, 0, 0, 0, 0 },
                Normalized = new[] { Math.Min(1.0, depthRatio), 0.5, 0.5, 0.5, 0.5, 0.5 },
                Engagement = 1.0, GeometricEngagement = 1.0, Length = 0.10, TargetDepth = depthRatio * 0.10
            };
            var outAxes = feel.Process(measured.Normalized, measured, sample, "overins", 0, false, 0.82, 1.38, 1.8, 0.02);
            if (i >= 40)
            {
                if (outAxes[0] < ovLo) ovLo = outAxes[0];
                if (outAxes[0] > ovHi) ovHi = outAxes[0];
            }
        }
        check((ovHi - ovLo) >= 0.45, "Over-inserted chapter (0.82..1.38) retains >=0.45 smooth dynamic range in Balanced feel");

        // 5. Deep Studio bottom-out radial contact keeps Engagement=1.0 when root pushes 39mm past entry
        var deepSample = new NativeMotionSample
        {
            Studio = true, HasShaftPose = true, HasTargetFrame = true,
            ReferenceFrame = AlignmentFrame.Build(new MotionVector(0, 0.039, 0), new MotionVector(1, 0, 0), up),
            TargetReferenceFrame = AlignmentFrame.Build(new MotionVector(0.015, 0, 0), new MotionVector(1, 0, 0), up),
            Origin = new MotionVector(0, 0.039, 0), End = new MotionVector(0, 0.140, 0),
            Target = new MotionVector(0.015, 0, 0),
            SourceRootDirection = up, SourceDeformedDirection = up,
            ShaftRestLength = 0.101,
            ShaftPath = new[] { new MotionVector(0, 0.039, 0), new MotionVector(0, 0.140, 0) },
            ShaftContact = new ShaftContactTracker(),
            ContactSequence = 1, ContactDeltaTime = 0.02
        };
        var deepRes = NativeMotion.Solve(deepSample);
        check(deepRes.Engagement >= 0.99 && deepRes.TargetDepth > 0.13, "Deep Studio bottom-out retains full engagement and unclamped TargetDepth");

        // 6. Pre-climax 3.5Hz sprint stays fast (NOT climax!), followed by fluid burst + drop -> capped climax <= 7.0s -> afterglow
        var longFrames = new List<StepBakeFrame>();
        for (int i = 0; i <= 880; i++)
        {
            double t = i * 0.05; // 0..44s
            var f = new StepBakeFrame { Time = t };
            bool preSprint = t >= 16.0 && t < 26.0; // fast 3.5Hz pre-climax sprint!
            bool inEjac = t >= 26.0;
            double freq = preSprint ? 3.5 : 1.5;
            double wave = 0.5 + 0.35 * Math.Sin(2.0 * Math.PI * freq * t);
            double depth = inEjac ? (t < 32.0 ? 0.96 + 0.04 * Math.Sin(t * 4) : 0.92) : (0.35 + 0.55 * wave);
            f.Observations.Add(new StepOpeningObservation
            {
                SourceKey = 1, TargetKey = 2, Opening = 0, SourceName = "M", TargetName = "F",
                Distance = 0.008, Length = 0.14, Insertion = 0.92, AlignmentCos = 0.95,
                RawDepthRatio = depth,
                ExplicitAttached = true
            });
            f.RawL0 = Math.Max(0, Math.Min(1, 1.0 - depth));
            f.ActiveSparseProps = inEjac ? 4 : 0;
            f.ActiveFluidProps = inEjac ? 4 : 0; // stays ON for full 18s after ejaculation!
            f.BlendShapeDelta = (t >= 26.0 && t <= 31.0) ? 18.0 : 2.0;
            longFrames.Add(f);
        }
        var longAnalysis = StudioStepAnalyzer.Analyze(longFrames, new[] { 16.0, 26.0 }, 44.0);
        var sprintCh = longAnalysis.Chapters.Find(c => c.Start >= 15.5 && c.End <= 26.5);
        var climaxCh = longAnalysis.Chapters.Find(c => c.ClimaxDetected);
        var afterCh = longAnalysis.Chapters.Find(c => c.Role == "afterglow");
        check(sprintCh != null && !sprintCh.ClimaxDetected && sprintCh.Role == "fast", "Pre-climax 3.5Hz high-speed sprint stays real-sync fast chapter (never misclassified as climax)");
        check(climaxCh != null && climaxCh.Start >= 25.0 && (climaxCh.End - climaxCh.Start) <= 7.2, "Real ejaculation starts at fluid onset and caps climax burst <= 7.0s");
        check(afterCh != null && afterCh.Start >= climaxCh.End - 0.1, "Tail after capped climax automatically becomes afterglow chapter");

        // 7. Smart Motion-Aware Climax mode (AutoMotion vs Fixed vs Simulated)
        var autoOpt = new ClimaxOptions { Enabled = true, FrequencyMode = ClimaxFrequencyMode.AutoMotion, Minimum = 0, Maximum = 240, Hertz = 7.0 };
        check(autoOpt.ShouldUseFixedOscillation(0.20, 55.0, false, 0), "AutoMotion engages fixed vibration when climax scene frequency is low (0.20 Hz hold)");
        check(autoOpt.ShouldUseFixedOscillation(2.0, 10.0, false, 0), "AutoMotion engages fixed vibration when climax scene amplitude is tiny (10%)");
        check(!autoOpt.ShouldUseFixedOscillation(2.2, 32.0, false, 0), "AutoMotion yields to computed simulated motion when scene is actively thrusting (2.2 Hz, 32%)");
        check(!autoOpt.ShouldUseFixedOscillation(0, double.NaN, true, 1.8), "AutoMotion yields to simulated motion during live playback when live L0 cycle >= 1.35 Hz");
        var fixedOpt = new ClimaxOptions { Enabled = true, FrequencyMode = ClimaxFrequencyMode.Fixed, Minimum = 0, Maximum = 240, Hertz = 7.0 };
        check(fixedOpt.ShouldUseFixedOscillation(3.0, 50.0, true, 3.0), "Manual Fixed mode always engages fixed-frequency vibration");
        var simOpt = new ClimaxOptions { Enabled = true, FrequencyMode = ClimaxFrequencyMode.Simulated, Minimum = 0, Maximum = 240, Hertz = 7.0 };
        check(!simOpt.ShouldUseFixedOscillation(0.10, 5.0, false, 0), "Manual Simulated mode never overrides with fixed-frequency vibration");

        // 8. Studio 6D Research Grinding Engine + XML Round-Trip
        var gBook = new ChapterBook();
        var gCh = new Chapter
        {
            Start = 0, Source = 1, Target = 2, Opening = 0, Manual = true,
            Grind = new ChapterGrindOptions { Pattern = StudioGrindPattern.Lissajous6D, Intensity = 0.80, Hertz = 1.4, DepthWeighted = true, LinkSkeleton = true, SoftTissue = true, MoverMode = ActiveMoverMode.Female }
        };
        gBook.Chapters.Add(gCh);
        string gXml = gBook.ToXml();
        var gBook2 = new ChapterBook();
        gBook2.FromXml(gXml);
        check(gBook2.Chapters.Count == 1 && gBook2.Chapters[0].Grind != null && gBook2.Chapters[0].Grind.Pattern == StudioGrindPattern.Lissajous6D
            && Math.Abs(gBook2.Chapters[0].Grind.Intensity - 0.80) < 1e-6 && gBook2.Chapters[0].Grind.LinkSkeleton && gBook2.Chapters[0].Grind.SoftTissue,
            "ChapterGrindOptions persists cleanly through ChapterBook XML round-trip");

        var baseAxes = new[] { 0.85, 0.50, 0.50, 0.50, 0.50, 0.50 };
        var gfShallow = StudioGrindEngine.Evaluate(0.35, 0.15, gCh.Grind);
        var gfDeep = StudioGrindEngine.Evaluate(0.35, 0.90, gCh.Grind);
        check(gfDeep.Active && gfDeep.EffectiveWeight > gfShallow.EffectiveWeight * 3.0, "Depth-weighted grinding swells strongly on deep insertion and softens on withdrawal");
        foreach (StudioGrindPattern pat in new[] { StudioGrindPattern.Circle, StudioGrindPattern.Figure8, StudioGrindPattern.Corkscrew, StudioGrindPattern.RockFB, StudioGrindPattern.LineFB, StudioGrindPattern.LineLR, StudioGrindPattern.Lissajous6D })
        {
            var opt = new ChapterGrindOptions { Pattern = pat, Intensity = 0.75, Hertz = 1.25, DepthWeighted = false };
            double maxDelta = 0;
            for (int k = 0; k < 24; k++)
            {
                var gf = StudioGrindEngine.Evaluate(k * 0.05, 0.85, opt);
                var modded = StudioGrindEngine.ApplyToAxes(baseAxes, gf);
                for (int a = 1; a < 6; a++) maxDelta = Math.Max(maxDelta, Math.Abs(modded[a] - 0.50));
                check(modded[0] >= 0 && modded[0] <= 1 && modded[1] >= 0.02 && modded[1] <= 0.98 && modded[5] >= 0.02 && modded[5] <= 0.98, "Grind output stays strictly within safe axis bounds for " + pat);
            }
            check(maxDelta >= 0.18, "Grind pattern " + pat + " injects rich secondary-axis stimulation");
        }
        check(StudioGrindEngine.IsDirectInsertionOpening(0) && StudioGrindEngine.IsDirectInsertionOpening(1) && StudioGrindEngine.IsDirectInsertionOpening(2) && !StudioGrindEngine.IsDirectInsertionOpening(4),
            "6D grinding & skeleton coupling strictly gates to direct insertion openings (Vaginal/Anal/Oral)");

        // 7. Auto pose/actor-pair boundary splitting + BJ-over-hand + no-contact climax guard (zero hint boundaries!)
        var multiFrames = new List<StepBakeFrame>();
        for (int i = 0; i <= 300; i++)
        {
            double t = i * 0.1;
            var f = new StepBakeFrame { Time = t, RawL0 = 0.5 + 0.3 * Math.Sin(2 * Math.PI * 1.1 * t) };
            if (t < 10.0)
            {
                // Girl A blowjob while holding base with LeftHand (0mm hand + 13mm oral -> must pick Oral!)
                f.Observations.Add(new StepOpeningObservation { SourceKey = 10, TargetKey = 20, Opening = 3, SourceName = "M1", TargetName = "GirlA", Distance = 0.001, Length = 0.15, Insertion = 0.55, AlignmentCos = 0.85, RawDepthRatio = 0.55 });
                f.Observations.Add(new StepOpeningObservation { SourceKey = 10, TargetKey = 20, Opening = 2, SourceName = "M1", TargetName = "GirlA", Distance = 0.013, Length = 0.15, Insertion = 0.50, AlignmentCos = 0.80, RawDepthRatio = 0.50 });
            }
            else if (t < 20.0)
            {
                // Girl A Vaginal
                f.Observations.Add(new StepOpeningObservation { SourceKey = 10, TargetKey = 20, Opening = 0, SourceName = "M1", TargetName = "GirlA", Distance = 0.005, Length = 0.15, Insertion = 0.70, AlignmentCos = 0.90, RawDepthRatio = 0.70 });
            }
            else
            {
                // 3P switch to Girl B Vaginal!
                f.Observations.Add(new StepOpeningObservation { SourceKey = 10, TargetKey = 30, Opening = 0, SourceName = "M1", TargetName = "GirlB", Distance = 0.005, Length = 0.15, Insertion = 0.75, AlignmentCos = 0.92, RawDepthRatio = 0.75 });
            }
            multiFrames.Add(f);
        }
        var multiRes = StudioStepAnalyzer.Analyze(multiFrames, new double[0], 30.0);
        check(multiRes.Chapters.Count == 3, "Auto interaction boundary detector splits Oral -> GirlA Vaginal -> GirlB Vaginal with zero hint cuts");
        check(multiRes.Chapters[0].Opening == 2 && multiRes.Chapters[0].TargetKey == 20, "BJ-over-hand guard locks blowjob with hand support to Oral (2)");
        check(multiRes.Chapters[1].Opening == 0 && multiRes.Chapters[1].TargetKey == 20, "Second chapter locks cleanly to GirlA Vaginal (0)");
        check(multiRes.Chapters[2].Opening == 0 && multiRes.Chapters[2].TargetKey == 30, "Third chapter auto-splits on 3P actor switch to GirlB Vaginal (0)");
    }
}
