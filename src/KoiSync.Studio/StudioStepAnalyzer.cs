using System;
using System.Collections.Generic;
using System.Globalization;
using KoiSync.Core;

namespace KoiSync.Studio
{
    public sealed class StepOpeningObservation
    {
        public int SourceKey;
        public int TargetKey;
        public int Opening;
        public string SourceName = "";
        public string TargetName = "";
        public double Distance;
        public double Length;
        public double Insertion;
        public double AlignmentCos;
        public double RawDepthRatio;
        public bool ExplicitAttached;
    }

    public sealed class StepBakeFrame
    {
        public double Time;
        public readonly List<StepOpeningObservation> Observations = new List<StepOpeningObservation>();
        public int ActiveSparseProps;
        public int ActiveFluidProps;
        public double BlendShapeDelta;
        public double RawL0 = double.NaN;
    }

    public sealed class StepOpeningCandidateSummary
    {
        public int SourceKey;
        public int TargetKey;
        public int Opening;
        public string SourceName = "";
        public string TargetName = "";
        public double Score;
        public double MeanDistanceMm;
        public double MeanInsertion;
        public int ExplicitFrames;
        public int ContactFrames;
    }

    public sealed class StepChapterResult
    {
        public double Start;
        public double End;
        public int SourceKey = -1;
        public int TargetKey = -1;
        public int Opening;
        public string SourceName = "";
        public string TargetName = "";
        public double Confidence;
        public bool NeedsReview;
        public string ReviewNote = "";
        public double DepthP05 = double.NaN;
        public double DepthP95 = double.NaN;
        public double PeakHz;
        public double AmplitudePercent;
        public double LoopStart;
        public double LoopEnd;
        public double SeamError;
        public bool Loopable;
        public bool ClimaxDetected;
        public double ClimaxScore;
        public string ClimaxEvidence = "";
        public string Role = "medium";
        public string Label = "";
        public readonly List<StepOpeningCandidateSummary> TopCandidates = new List<StepOpeningCandidateSummary>();
    }

    public sealed class StepBakeAnalysisResult
    {
        public double Duration;
        public int FrameCount;
        public readonly List<StepChapterResult> Chapters = new List<StepChapterResult>();
        public readonly List<StudioDirectorSegment> Segments = new List<StudioDirectorSegment>();
        public int ReviewCount;
        public int ClimaxCount;
    }

    public static class StudioStepAnalyzer
    {
        private sealed class PairAccum
        {
            public int SourceKey, TargetKey, Opening;
            public string SourceName = "", TargetName = "";
            public double Score, DistSum, InsSum;
            public int ExplicitFrames, ContactFrames;
        }

        private sealed class ClimaxWindow
        {
            public double Start, End, Score;
            public string Evidence = "";
        }

        public static StepBakeAnalysisResult Analyze(
            IList<StepBakeFrame> frames,
            IList<double> hintBoundaries,
            double duration)
        {
            var result = new StepBakeAnalysisResult
            {
                Duration = Numbers.Finite(duration) && duration > 0 ? duration : 0,
                FrameCount = frames == null ? 0 : frames.Count
            };
            if (frames == null || frames.Count < 2 || result.Duration < 0.5)
                return result;

            var sortedFrames = new List<StepBakeFrame>(frames);
            sortedFrames.Sort((a, b) => a.Time.CompareTo(b.Time));

            // 1. Detect 3-signal climax windows first so their boundaries become chapter cuts
            var climaxWindows = DetectClimaxWindows(sortedFrames, result.Duration);

            // 1b. Detect real pose/opening/actor-pair transition boundaries directly from bone observations!
            // Automatically splits long multi-act scenes (e.g. Intro -> Oral -> Vaginal -> Afterglow)
            // AND multi-person (3P/4P/9P) actor-pair switches even without explicit NodesConstraints cuts.
            var interactionBounds = DetectInteractionBoundaries(sortedFrames, result.Duration);

            // 2. Build merged chapter boundaries
            var rawBounds = new List<double> { 0.0, result.Duration };
            if (hintBoundaries != null)
            {
                for (int i = 0; i < hintBoundaries.Count; i++)
                {
                    double b = hintBoundaries[i];
                    if (Numbers.Finite(b) && b > 0.5 && b < result.Duration - 0.5) rawBounds.Add(b);
                }
            }
            for (int i = 0; i < interactionBounds.Count; i++)
            {
                double b = interactionBounds[i];
                if (Numbers.Finite(b) && b > 0.5 && b < result.Duration - 0.5) rawBounds.Add(b);
            }
            for (int i = 0; i < climaxWindows.Count; i++)
            {
                rawBounds.Add(climaxWindows[i].Start);
                rawBounds.Add(climaxWindows[i].End);
            }
            rawBounds.Sort();

            var bounds = new List<double> { 0.0 };
            for (int i = 0; i < rawBounds.Count; i++)
            {
                double b = rawBounds[i];
                if (b - bounds[bounds.Count - 1] >= 2.5 && result.Duration - b >= 1.5)
                    bounds.Add(Math.Round(b, 2));
            }
            if (result.Duration - bounds[bounds.Count - 1] >= 1.0)
                bounds.Add(Math.Round(result.Duration, 2));
            else
                bounds[bounds.Count - 1] = Math.Round(result.Duration, 2);

            // 3. Vote binding, compute percentile depth calibration, loop seam, and climax tagging per chapter
            for (int i = 0; i < bounds.Count - 1; i++)
            {
                double s0 = bounds[i], s1 = bounds[i + 1];
                var slice = new List<StepBakeFrame>();
                for (int f = 0; f < sortedFrames.Count; f++)
                    if (sortedFrames[f].Time >= s0 - 0.001 && sortedFrames[f].Time <= s1 + 0.001)
                        slice.Add(sortedFrames[f]);

                var ch = AnalyzeChapter(i, s0, s1, result.Duration, slice, climaxWindows);
                result.Chapters.Add(ch);
                if (ch.NeedsReview) result.ReviewCount++;
                if (ch.ClimaxDetected) result.ClimaxCount++;

                result.Segments.Add(new StudioDirectorSegment
                {
                    Id = "seg_" + (i + 1).ToString("D2", CultureInfo.InvariantCulture),
                    Label = ch.Label,
                    Role = ch.Role,
                    Notes = BuildSegmentNotes(ch),
                    Start = ch.Start,
                    End = ch.End,
                    LoopStart = ch.LoopStart,
                    LoopEnd = ch.LoopEnd,
                    Loopable = ch.Loopable,
                    FrequencyHz = ch.PeakHz,
                    AmplitudePercent = ch.AmplitudePercent,
                    SeamErrorCm = Math.Round(ch.SeamError * 10.0, 2),
                    DefaultSpeed = 1.0
                });
            }

            return result;
        }

        private static string BuildSegmentNotes(StepChapterResult ch)
        {
            var parts = new List<string>();
            if (ch.SourceKey >= 0 && ch.TargetKey >= 0)
                parts.Add(ch.SourceName + " -> " + ch.TargetName + " (" + OpeningLabelEn(ch.Opening) + ")");
            if (!double.IsNaN(ch.DepthP05) && !double.IsNaN(ch.DepthP95))
                parts.Add("depth=" + Math.Round(ch.DepthP05 * 100) + "%.." + Math.Round(ch.DepthP95 * 100) + "%");
            if (ch.NeedsReview && !string.IsNullOrEmpty(ch.ReviewNote))
                parts.Add("[REVIEW] " + ch.ReviewNote);
            if (!string.IsNullOrEmpty(ch.ClimaxEvidence))
                parts.Add("[CLIMAX] " + ch.ClimaxEvidence);
            return string.Join(" | ", parts.ToArray());
        }

        private static StepChapterResult AnalyzeChapter(
            int index,
            double s0,
            double s1,
            double duration,
            List<StepBakeFrame> slice,
            List<ClimaxWindow> climaxWindows)
        {
            var ch = new StepChapterResult
            {
                Start = s0,
                End = s1,
                LoopStart = s0,
                LoopEnd = s1
            };

            // Check climax overlap
            ClimaxWindow matchedClimax = null;
            for (int i = 0; i < climaxWindows.Count; i++)
            {
                var cw = climaxWindows[i];
                double overlap = Math.Min(s1, cw.End) - Math.Max(s0, cw.Start);
                if (overlap >= Math.Min(2.0, (s1 - s0) * 0.45))
                {
                    if (matchedClimax == null || cw.Score > matchedClimax.Score)
                        matchedClimax = cw;
                }
            }
            bool afterClimax = false;
            for (int i = 0; i < climaxWindows.Count; i++)
                if (s0 >= climaxWindows[i].End - 0.5 && climaxWindows[i].Score >= 0.60)
                    afterClimax = true;

            if (matchedClimax != null)
            {
                ch.ClimaxScore = Math.Round(matchedClimax.Score, 2);
                ch.ClimaxEvidence = matchedClimax.Evidence;
                if (matchedClimax.Score >= 0.60)
                {
                    ch.ClimaxDetected = true;
                }
                else
                {
                    ch.NeedsReview = true;
                    ch.ReviewNote = "高潮候选 (" + matchedClimax.Evidence + ")";
                }
            }

            // Pass 1: Chapter-level majority voting with anti-Anal-flip guard
            var votes = new Dictionary<string, PairAccum>();
            for (int f = 0; f < slice.Count; f++)
            {
                var obs = slice[f].Observations;
                for (int k = 0; k < obs.Count; k++)
                {
                    var o = obs[k];
                    double limit = CameraPairRanking.ContactLimit(Math.Max(0.05, o.Length));
                    if (o.Opening == 2 && limit < 0.085) limit = 0.085;
                    // Guard against scenes (e.g. Sugardust) that leave BP attached to Vagina during
                    // foreplay/handjob/oral when the shaft is 20-45cm away: explicit attachment only
                    // counts when genuinely near contact (<= 12cm).
                    bool nearExplicit = o.ExplicitAttached && o.Distance <= 0.12;
                    if (!nearExplicit && o.Distance > limit) continue;

                    double distScore = Math.Exp(-o.Distance / 0.018) * 2.6;
                    double insScore = Numbers.Clamp(o.Insertion, 0, 1) * 1.4;
                    double alignScore = Math.Max(0, o.AlignmentCos) * 0.9;
                    double frameScore = (nearExplicit ? 4.0 : 0.0) + distScore + insScore + alignScore;
                    if (frameScore <= 0.05) continue;

                    string key = o.SourceKey + ":" + o.TargetKey + ":" + o.Opening;
                    PairAccum acc;
                    if (!votes.TryGetValue(key, out acc))
                    {
                        acc = new PairAccum
                        {
                            SourceKey = o.SourceKey,
                            TargetKey = o.TargetKey,
                            Opening = o.Opening,
                            SourceName = o.SourceName ?? "",
                            TargetName = o.TargetName ?? ""
                        };
                        votes[key] = acc;
                    }
                    acc.Score += frameScore;
                    acc.DistSum += o.Distance;
                    acc.InsSum += o.Insertion;
                    acc.ContactFrames++;
                    if (nearExplicit) acc.ExplicitFrames++;
                }
            }

            var ranked = new List<PairAccum>(votes.Values);
            ranked.Sort((a, b) => b.Score.CompareTo(a.Score));

            for (int i = 0; i < Math.Min(4, ranked.Count); i++)
            {
                var r = ranked[i];
                ch.TopCandidates.Add(new StepOpeningCandidateSummary
                {
                    SourceKey = r.SourceKey,
                    TargetKey = r.TargetKey,
                    Opening = r.Opening,
                    SourceName = r.SourceName,
                    TargetName = r.TargetName,
                    Score = Math.Round(r.Score, 2),
                    MeanDistanceMm = r.ContactFrames > 0 ? Math.Round(r.DistSum / r.ContactFrames * 1000.0, 1) : 0,
                    MeanInsertion = r.ContactFrames > 0 ? Math.Round(r.InsSum / r.ContactFrames, 2) : 0,
                    ExplicitFrames = r.ExplicitFrames,
                    ContactFrames = r.ContactFrames
                });
            }

            PairAccum winner = ranked.Count > 0 ? ranked[0] : null;
            if (winner == null)
            {
                // No actor pair in contact (e.g. story interlude / fade-out): never emit a ghost climax!
                ch.ClimaxDetected = false;
                ch.ClimaxScore = 0;
                ch.ClimaxEvidence = "";
            }
            if (winner != null)
            {
                // Oral-Beats-Hand-During-BJ Guard:
                // When a female holds the shaft base with her hand while giving a blowjob, hand distance
                // can hit 0 mm while Oral is 10-20 mm. Prefer Oral whenever Oral is close (<= 24 mm)
                // and has solid support (>= 45% of hand score) without explicit hand attachment.
                if ((winner.Opening == 3 || winner.Opening == 4) && winner.ExplicitFrames == 0)
                {
                    var oral = ranked.Find(r => r.SourceKey == winner.SourceKey && r.TargetKey == winner.TargetKey && r.Opening == 2);
                    if (oral != null && oral.ContactFrames > 0)
                    {
                        double oralDistMm = (oral.DistSum / oral.ContactFrames) * 1000.0;
                        if (oralDistMm <= 24.0 && oral.Score >= winner.Score * 0.45)
                        {
                            winner = oral;
                            ch.ReviewNote = AppendNote(ch.ReviewNote, "口交伴手扶：已优先按口部锁定");
                        }
                    }
                }

                // Anti-Anal-Flip Guard:
                // Vaginal (0) and Anal (1) are only ~3cm apart in Koikatsu. Without explicit attachment,
                // require Anal to beat Vaginal on the same actor pair by > 65% before selecting Anal.
                if (winner.Opening == 1 && winner.ExplicitFrames == 0)
                {
                    var vag = ranked.Find(r => r.SourceKey == winner.SourceKey && r.TargetKey == winner.TargetKey && r.Opening == 0);
                    if (vag != null && winner.Score <= vag.Score * 1.65)
                    {
                        winner = vag;
                        ch.NeedsReview = true;
                        ch.ReviewNote = AppendNote(ch.ReviewNote, "阴道/肛门距离接近，已优先按阴道锁定");
                    }
                }
                else if (winner.Opening == 0 && winner.ExplicitFrames == 0)
                {
                    var anal = ranked.Find(r => r.SourceKey == winner.SourceKey && r.TargetKey == winner.TargetKey && r.Opening == 1);
                    if (anal != null && anal.Score >= winner.Score * 0.78)
                    {
                        ch.NeedsReview = true;
                        ch.ReviewNote = AppendNote(ch.ReviewNote, "阴道/肛门接近，建议复核");
                    }
                }

                // General ambiguity check between #1 and #2 if #1 isn't explicitly attached
                if (ranked.Count >= 2 && winner.ExplicitFrames == 0)
                {
                    var runnerUp = ranked[0] == winner ? ranked[1] : ranked[0];
                    if (runnerUp.Score >= winner.Score * 0.80 && runnerUp.Opening != winner.Opening)
                    {
                        ch.NeedsReview = true;
                        ch.ReviewNote = AppendNote(ch.ReviewNote, OpeningLabelEn(winner.Opening) + "/" + OpeningLabelEn(runnerUp.Opening) + " 接近");
                    }
                }

                ch.SourceKey = winner.SourceKey;
                ch.TargetKey = winner.TargetKey;
                ch.Opening = winner.Opening;
                ch.SourceName = winner.SourceName;
                ch.TargetName = winner.TargetName;
                double totalTop = ranked.Count >= 2 ? winner.Score + (ranked[0] == winner ? ranked[1].Score : ranked[0].Score) : winner.Score;
                ch.Confidence = totalTop > 1e-6 ? Math.Round(winner.Score / totalTop, 2) : 1.0;
            }

            // Pass 2: Extract winning depth series for [P05..P95] calibration, Hz, and loop seam
            var times = new List<double>();
            var depths = new List<double>();
            for (int f = 0; f < slice.Count; f++)
            {
                double d = double.NaN;
                if (winner != null)
                {
                    var obs = slice[f].Observations;
                    for (int k = 0; k < obs.Count; k++)
                    {
                        if (obs[k].SourceKey == winner.SourceKey && obs[k].TargetKey == winner.TargetKey && obs[k].Opening == winner.Opening)
                        {
                            d = obs[k].RawDepthRatio;
                            break;
                        }
                    }
                }
                if (double.IsNaN(d) && Numbers.Finite(slice[f].RawL0))
                    d = 1.0 - slice[f].RawL0;
                if (Numbers.Finite(d))
                {
                    times.Add(slice[f].Time);
                    depths.Add(Numbers.Clamp(d, -0.35, 1.85));
                }
            }

            if (depths.Count >= 6)
            {
                var sortedDepths = new List<double>(depths);
                sortedDepths.Sort();
                ch.DepthP05 = Math.Round(Percentile(sortedDepths, 0.05), 4);
                ch.DepthP95 = Math.Round(Percentile(sortedDepths, 0.95), 4);
                ch.AmplitudePercent = Math.Round(Numbers.Clamp((ch.DepthP95 - ch.DepthP05) * 100.0, 0, 100), 1);

                double span = Math.Max(0.02, ch.DepthP95 - ch.DepthP05);
                int reversals = 0, prevDir = 0;
                double deadband = Math.Max(0.015, span * 0.10);
                double anchor = depths[0];
                for (int i = 1; i < depths.Count; i++)
                {
                    double diff = depths[i] - anchor;
                    if (Math.Abs(diff) < deadband) continue;
                    int curDir = diff > 0 ? 1 : -1;
                    if (prevDir != 0 && curDir != prevDir) reversals++;
                    prevDir = curDir;
                    anchor = depths[i];
                }
                double durSpan = Math.Max(0.5, times[times.Count - 1] - times[0]);
                ch.PeakHz = Math.Round((reversals / 2.0) / durSpan, 2);

                // Find best phase-matched loop seam [loopStart, loopEnd] >= max(2.5s, 30% of chapter)
                SolveScalarLoopSeam(times, depths, out ch.LoopStart, out ch.LoopEnd, out ch.SeamError);
            }

            // Double-defense Anti-Sprint Guard: an active high-speed pounding chapter
            // (e.g. 2.2..4.8 Hz, >= 18% amplitude) MUST stay high-speed real-motion sync,
            // never overwritten by synthetic climax quiver!
            if (ch.ClimaxDetected && ch.PeakHz >= 2.2 && ch.AmplitudePercent >= 18.0)
            {
                ch.ClimaxDetected = false;
                ch.ClimaxScore = 0;
                ch.ClimaxEvidence = "";
            }

            // Role & label
            bool beforeClimax = false;
            for (int i = 0; i < climaxWindows.Count; i++)
                if (climaxWindows[i].Score >= 0.60 && climaxWindows[i].Start >= s1 - 2.0 && climaxWindows[i].Start - s1 <= 4.0)
                    beforeClimax = true;

            double slen = s1 - s0;
            if (ch.ClimaxDetected)
            {
                ch.Role = "climax";
                ch.Label = "射精高潮";
            }
            else if (afterClimax)
            {
                ch.Role = "afterglow";
                ch.Label = "高潮余韵";
            }
            else if (beforeClimax && ch.PeakHz >= 1.6 && ch.AmplitudePercent >= 18.0)
            {
                ch.Role = "fast";
                ch.Label = "高潮前冲刺 (" + ch.PeakHz.ToString("F1", CultureInfo.InvariantCulture) + "Hz)";
            }
            else if (ch.PeakHz >= 2.2 && ch.AmplitudePercent >= 18.0)
            {
                ch.Role = "fast";
                ch.Label = "高速冲刺 (" + ch.PeakHz.ToString("F1", CultureInfo.InvariantCulture) + "Hz)";
            }
            else if (slen < 4.5)
            {
                ch.Role = "transition";
                ch.Label = "姿态过渡";
            }
            else if (winner != null && winner.Opening == 2)
            {
                ch.Role = "oral";
                ch.Label = ch.PeakHz > 0 ? "口部互动 (" + ch.PeakHz.ToString("F1", CultureInfo.InvariantCulture) + "Hz)" : "口部互动";
            }
            else if (winner != null && (winner.Opening == 3 || winner.Opening == 4 || winner.Opening == 6 || winner.Opening == 7))
            {
                ch.Role = "hand";
                ch.Label = "手部互动";
            }
            else if (winner != null && winner.Opening == 5)
            {
                ch.Role = "foreplay";
                ch.Label = "胸部互动";
            }
            else if (index == 0 && (ch.AmplitudePercent < 22.0 || ch.PeakHz < 0.65))
            {
                ch.Role = "intro";
                ch.Label = "开场前戏";
            }
            else if (ch.PeakHz >= 1.65)
            {
                ch.Role = "fast";
                ch.Label = "高速冲刺 (" + ch.PeakHz.ToString("F1", CultureInfo.InvariantCulture) + "Hz)";
            }
            else if (ch.PeakHz >= 0.85)
            {
                ch.Role = "medium";
                ch.Label = "中速主循环 (" + ch.PeakHz.ToString("F1", CultureInfo.InvariantCulture) + "Hz)";
            }
            else
            {
                ch.Role = "slow";
                ch.Label = ch.PeakHz > 0 ? "慢速节奏 (" + ch.PeakHz.ToString("F1", CultureInfo.InvariantCulture) + "Hz)" : "慢速互动";
            }

            ch.Loopable = ch.Role != "climax" && ch.Role != "afterglow" && ch.Role != "transition"
                && (ch.LoopEnd - ch.LoopStart) >= 2.2
                && ch.AmplitudePercent >= 6.0
                && ch.SeamError <= 0.14;

            return ch;
        }

        private static List<ClimaxWindow> DetectClimaxWindows(List<StepBakeFrame> frames, double duration)
        {
            var windows = new List<ClimaxWindow>();
            if (frames.Count < 20 || duration < 10.0) return windows;

            // Compute median BlendShapeDelta across whole scene for adaptive threshold
            var deltas = new List<double>();
            for (int i = 0; i < frames.Count; i++)
                if (frames[i].BlendShapeDelta > 0) deltas.Add(frames[i].BlendShapeDelta);
            deltas.Sort();
            double medianBlend = deltas.Count > 0 ? Percentile(deltas, 0.50) : 0;

            double step = 0.5;
            double winLen = 3.5;
            var scored = new List<ClimaxWindow>();
            var recentWinTimes = new List<double>();
            var recentWinHz = new List<double>();
            var recentWinAmp = new List<double>();

            for (double t = duration * 0.45; t < duration - 0.8; t += step)
            {
                double tEnd = Math.Min(duration, t + winLen);
                int minSparse = int.MaxValue, maxSparse = 0;
                int minFluid = int.MaxValue, maxFluid = 0;
                double firstFluidRiseTime = double.NaN;
                double blendSum = 0;
                int count = 0;
                var depths = new List<double>();
                for (int i = 0; i < frames.Count; i++)
                {
                    var f = frames[i];
                    if (f.Time < t || f.Time > tEnd) continue;
                    count++;
                    if (f.ActiveSparseProps < minSparse) minSparse = f.ActiveSparseProps;
                    if (f.ActiveSparseProps > maxSparse) maxSparse = f.ActiveSparseProps;
                    if (f.ActiveFluidProps < minFluid) minFluid = f.ActiveFluidProps;
                    if (f.ActiveFluidProps > maxFluid) maxFluid = f.ActiveFluidProps;
                    if (double.IsNaN(firstFluidRiseTime) && minFluid < int.MaxValue && f.ActiveFluidProps > minFluid)
                        firstFluidRiseTime = f.Time;
                    blendSum += f.BlendShapeDelta;
                    if (Numbers.Finite(f.RawL0)) depths.Add(1.0 - f.RawL0);
                }
                if (count < 4) continue;

                // Measure kinematics of this window
                double hz = 0, amp = 0, meanDepth = 0;
                if (depths.Count >= 6)
                {
                    double lo = depths[0], hi = depths[0], sum = 0;
                    for (int i = 0; i < depths.Count; i++) { if (depths[i] < lo) lo = depths[i]; if (depths[i] > hi) hi = depths[i]; sum += depths[i]; }
                    amp = hi - lo;
                    meanDepth = sum / depths.Count;
                    int rev = 0, pd = 0;
                    double anch = depths[0], db = Math.Max(0.02, amp * 0.15);
                    for (int i = 1; i < depths.Count; i++)
                    {
                        double diff = depths[i] - anch;
                        if (Math.Abs(diff) < db) continue;
                        int cd = diff > 0 ? 1 : -1;
                        if (pd != 0 && cd != pd) rev++;
                        pd = cd; anch = depths[i];
                    }
                    hz = (rev / 2.0) / Math.Max(0.5, tEnd - t);
                }

                // Look back up to 9s for preceding high-speed sprint kinematics
                double recentPeakHz = 0, recentPeakAmp = 0;
                for (int r = 0; r < recentWinTimes.Count; r++)
                {
                    if (t - recentWinTimes[r] <= 9.0 && t - recentWinTimes[r] >= 1.5)
                    {
                        if (recentWinHz[r] > recentPeakHz) recentPeakHz = recentWinHz[r];
                        if (recentWinAmp[r] > recentPeakAmp) recentPeakAmp = recentWinAmp[r];
                    }
                }
                recentWinTimes.Add(t);
                recentWinHz.Add(hz);
                recentWinAmp.Add(amp);

                int fluidDelta = maxFluid - minFluid;
                int sparseDelta = maxSparse - minSparse;
                bool activeFastSprint = hz >= 2.1 && amp >= 0.18;

                // Unconditional Anti-Sprint Guard: while actively pounding at high frequency
                // (e.g. 2.1..4.8 Hz, >= 18% amplitude), NEVER mark the sprint itself as synthetic
                // climax (even if a pre-cum/wet/splash prop turns on during the sprint). Real
                // ejaculation starts when the sprint breaks into hold/contraction + cum burst.
                if (activeFastSprint)
                    continue;

                double score = 0;
                var reasons = new List<string>();

                // Signal 1: Rising-edge fluid/cum prop activation (explicit ejaculation burst)
                if (fluidDelta >= 1)
                {
                    score += fluidDelta >= 2 ? 0.70 : 0.60;
                    reasons.Add("cum+" + fluidDelta);
                }
                else if (!activeFastSprint && sparseDelta >= 2)
                {
                    score += 0.55;
                    reasons.Add("props+" + sparseDelta);
                }

                // Signal 2: Sprint -> Ejaculation drop / deep-hold contraction
                if (!activeFastSprint && recentPeakHz >= 1.5 && (hz <= 1.25 || amp <= recentPeakAmp * 0.55) && meanDepth >= 0.60)
                {
                    score += 0.40;
                    reasons.Add("sprint->ejac");
                }

                // Signal 3: KKPE BlendShape facial spasm during non-sprint ejaculation onset
                double meanBlend = blendSum / count;
                if (!activeFastSprint && medianBlend > 1e-4 && meanBlend >= medianBlend * 2.0 && t >= duration * 0.55)
                {
                    score += 0.25;
                    reasons.Add("expr-spike");
                }

                if (score >= 0.55)
                {
                    double winStart = !double.IsNaN(firstFluidRiseTime) ? firstFluidRiseTime : t;
                    scored.Add(new ClimaxWindow
                    {
                        Start = Math.Round(winStart, 2),
                        End = Math.Round(Math.Max(winStart + 2.5, tEnd), 2),
                        Score = Math.Min(1.0, score),
                        Evidence = string.Join("+", reasons.ToArray())
                    });
                }
            }

            // Merge overlapping/adjacent windows within 2.5s first, then cap each
            // episode to <= 7.0s from ejaculation onset so persistent post-climax
            // fluid props transition cleanly into afterglow without spawning ghost episodes.
            for (int i = 0; i < scored.Count; i++)
            {
                var w = scored[i];
                if (windows.Count > 0 && w.Start <= windows[windows.Count - 1].End + 2.5)
                {
                    var last = windows[windows.Count - 1];
                    last.End = Math.Max(last.End, w.End);
                    if (w.Score > last.Score)
                    {
                        last.Score = w.Score;
                        last.Evidence = w.Evidence;
                    }
                }
                else
                {
                    windows.Add(w);
                }
            }
            for (int i = 0; i < windows.Count; i++)
            {
                var w = windows[i];
                if (w.End - w.Start > 6.5)
                    w.End = Math.Round(w.Start + 6.5, 2);
            }
            // Anti-persistent-puddle cooldown: after a climax burst, suppress weak non-sprint
            // fluid/prop ripples within 16s so lingering cum puddles don't flood multi-round scenes.
            var filtered = new List<ClimaxWindow>();
            for (int i = 0; i < windows.Count; i++)
            {
                var w = windows[i];
                if (filtered.Count > 0)
                {
                    var prev = filtered[filtered.Count - 1];
                    bool hasSprintDrop = w.Evidence != null && w.Evidence.IndexOf("sprint->ejac", StringComparison.Ordinal) >= 0;
                    if (w.Start - prev.End < 16.0 && !hasSprintDrop)
                        continue;
                }
                filtered.Add(w);
            }
            return filtered;
        }

        private static List<double> DetectInteractionBoundaries(List<StepBakeFrame> frames, double duration)
        {
            var cuts = new List<double>();
            if (frames == null || frames.Count < 8) return cuts;

            string stableKey = null;
            string pendingKey = null;
            double pendingStart = 0;

            for (int i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                string curKey = ClassifyFrameInteraction(f);

                if (stableKey == null)
                {
                    stableKey = curKey;
                    pendingKey = null;
                    continue;
                }
                if (curKey == stableKey)
                {
                    pendingKey = null;
                    continue;
                }
                if (curKey != pendingKey)
                {
                    pendingKey = curKey;
                    pendingStart = f.Time;
                    continue;
                }
                double holdReq = curKey == "none" ? 3.0 : 1.6;
                if (f.Time - pendingStart >= holdReq)
                {
                    if (pendingStart > 1.5 && pendingStart < duration - 1.5)
                        cuts.Add(Math.Round(pendingStart, 2));
                    stableKey = curKey;
                    pendingKey = null;
                }
            }
            return cuts;
        }

        private static string ClassifyFrameInteraction(StepBakeFrame frame)
        {
            if (frame == null || frame.Observations.Count == 0) return "none";
            double bestScore = 0;
            StepOpeningObservation best = null;
            StepOpeningObservation bestOral = null;
            double bestOralScore = 0;

            for (int k = 0; k < frame.Observations.Count; k++)
            {
                var o = frame.Observations[k];
                double limit = CameraPairRanking.ContactLimit(Math.Max(0.05, o.Length));
                if (o.Opening == 2 && limit < 0.085) limit = 0.085;
                bool nearExplicit = o.ExplicitAttached && o.Distance <= 0.12;
                if (!nearExplicit && o.Distance > limit) continue;
                double sc = (nearExplicit ? 4.0 : 0.0)
                    + Math.Exp(-o.Distance / 0.018) * 2.6
                    + Numbers.Clamp(o.Insertion, 0, 1) * 1.4
                    + Math.Max(0, o.AlignmentCos) * 0.9;
                if (o.Opening == 2 && o.Distance <= 0.024 && sc > bestOralScore)
                {
                    bestOralScore = sc;
                    bestOral = o;
                }
                if (sc > bestScore)
                {
                    bestScore = sc;
                    best = o;
                }
            }
            if (best == null || bestScore < 0.85) return "none";
            // Oral beats hand-holding-base on same pair during BJ
            if ((best.Opening == 3 || best.Opening == 4) && !best.ExplicitAttached
                && bestOral != null && bestOral.SourceKey == best.SourceKey && bestOral.TargetKey == best.TargetKey
                && bestOralScore >= bestScore * 0.45)
            {
                best = bestOral;
            }
            // Group Vaginal(0) & Anal(1) together for boundary detection so pelvic tilts don't over-fragment
            int group = (best.Opening == 0 || best.Opening == 1) ? 0
                : (best.Opening == 3 || best.Opening == 4) ? 3
                : best.Opening;
            return best.SourceKey + ":" + best.TargetKey + ":" + group;
        }

        private static void SolveScalarLoopSeam(List<double> times, List<double> vals, out double loopStart, out double loopEnd, out double seamErr)
        {
            loopStart = times[0];
            loopEnd = times[times.Count - 1];
            seamErr = 1.0;
            int n = times.Count;
            if (n < 16) return;

            // Subsample to <= 120 points so O(M^2) is always instant (<1ms)
            int stride = Math.Max(1, n / 120);
            var st = new List<double>();
            var sv = new List<double>();
            for (int i = 0; i < n; i += stride) { st.Add(times[i]); sv.Add(vals[i]); }
            int m = st.Count;
            if (m < 10) return;

            double totalDur = st[m - 1] - st[0];
            double minLoopDur = Math.Max(2.2, totalDur * 0.30);
            double bestScore = 999.0;
            double bestErr = 1.0;

            for (int i = 1; i < m - 3; i++)
            {
                double vi = sv[i + 1] - sv[i - 1];
                for (int j = i + 3; j < m - 1; j++)
                {
                    if (st[j] - st[i] < minLoopDur) continue;
                    double vj = sv[j + 1] - sv[j - 1];
                    double posErr = Math.Abs(sv[i] - sv[j]);
                    double velErr = Math.Abs(vi - vj);
                    double score = posErr + 0.5 * velErr;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestErr = posErr;
                        loopStart = Math.Round(st[i], 2);
                        loopEnd = Math.Round(st[j], 2);
                    }
                }
            }
            seamErr = Math.Round(bestErr, 4);
        }

        private static double Percentile(List<double> sorted, double p)
        {
            if (sorted.Count == 0) return 0;
            double idx = Numbers.Clamp(p, 0, 1) * (sorted.Count - 1);
            int lo = (int)Math.Floor(idx);
            int hi = Math.Min(sorted.Count - 1, lo + 1);
            double frac = idx - lo;
            return sorted[lo] * (1.0 - frac) + sorted[hi] * frac;
        }

        private static string AppendNote(string existing, string add)
        {
            if (string.IsNullOrEmpty(existing)) return add;
            return existing + " · " + add;
        }

        public static string OpeningLabelEn(int opening)
        {
            switch (opening)
            {
                case 0: return "Vaginal";
                case 1: return "Anal";
                case 2: return "Oral";
                case 3: return "LeftHand";
                case 4: return "RightHand";
                case 5: return "Breast";
                case 6: return "LeftHandVaginal";
                case 7: return "RightHandVaginal";
                default: return "Unknown";
            }
        }
    }
}
