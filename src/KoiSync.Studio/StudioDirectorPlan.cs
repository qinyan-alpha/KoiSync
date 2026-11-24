using System;
using System.Collections.Generic;
using System.Globalization;
using KoiSync.Core;

namespace KoiSync.Studio
{
    public enum StudioPlaybackMode
    {
        Linear = 0,
        SegmentLoop = 1,
        SegmentOnce = 2
    }

    public sealed class StudioDirectorSegment
    {
        public string Id = "";
        public string Label = "";
        public string Role = "medium";
        public string Notes = "";
        public double Start;
        public double End;
        public double LoopStart;
        public double LoopEnd;
        public bool Loopable = true;
        public double FrequencyHz;
        public double AmplitudePercent;
        public double SeamErrorCm;
        public double DefaultSpeed = 1.0;

        public double EffectiveLoopStart
        {
            get { return HasExplicitLoopWindow ? LoopStart : Start; }
        }

        public double EffectiveLoopEnd
        {
            get { return HasExplicitLoopWindow ? LoopEnd : End; }
        }

        public bool HasExplicitLoopWindow
        {
            get { return LoopEnd > LoopStart + 0.25 && LoopStart >= Start - 0.05 && LoopEnd <= End + 0.05; }
        }

        public StudioDirectorSegment Copy()
        {
            return new StudioDirectorSegment
            {
                Id = Id ?? "",
                Label = Label ?? "",
                Role = Role ?? "medium",
                Notes = Notes ?? "",
                Start = Start,
                End = End,
                LoopStart = LoopStart,
                LoopEnd = LoopEnd,
                Loopable = Loopable,
                FrequencyHz = FrequencyHz,
                AmplitudePercent = AmplitudePercent,
                SeamErrorCm = SeamErrorCm,
                DefaultSpeed = DefaultSpeed
            };
        }

        public void Sanitize(double duration, int fallbackIndex)
        {
            if (string.IsNullOrEmpty(Id))
                Id = "seg_" + (fallbackIndex + 1).ToString("D2", CultureInfo.InvariantCulture);
            Id = Id.Trim();
            Label = (Label ?? "").Trim();
            if (Label.Length == 0) Label = Id;
            Role = NormalizeRole(Role);
            Notes = (Notes ?? "").Trim();

            double maxDur = Numbers.Finite(duration) && duration > 0 ? duration : Math.Max(End, LoopEnd);
            if (!Numbers.Finite(maxDur) || maxDur < 0) maxDur = 0;

            Start = Numbers.Finite(Start) ? Math.Max(0, Start) : 0;
            End = Numbers.Finite(End) ? Math.Max(Start, End) : maxDur;
            if (maxDur > 0)
            {
                Start = Math.Min(Start, maxDur);
                End = Math.Min(End, maxDur);
            }
            if (End < Start) End = Start;

            if (!Numbers.Finite(LoopStart) || !Numbers.Finite(LoopEnd) || LoopEnd <= LoopStart + 0.25)
            {
                LoopStart = Start;
                LoopEnd = End;
            }
            else
            {
                LoopStart = Math.Max(Start, Math.Min(End, LoopStart));
                LoopEnd = Math.Max(LoopStart, Math.Min(End, LoopEnd));
                if (LoopEnd <= LoopStart + 0.25)
                {
                    LoopStart = Start;
                    LoopEnd = End;
                }
            }

            FrequencyHz = Numbers.Finite(FrequencyHz) && FrequencyHz > 0 ? FrequencyHz : 0;
            AmplitudePercent = Numbers.Finite(AmplitudePercent) && AmplitudePercent >= 0 ? Numbers.Clamp(AmplitudePercent, 0, 100) : 0;
            SeamErrorCm = Numbers.Finite(SeamErrorCm) && SeamErrorCm >= 0 ? SeamErrorCm : 0;
            DefaultSpeed = Numbers.Finite(DefaultSpeed) && DefaultSpeed > 0 ? Numbers.Clamp(DefaultSpeed, 0.25, 2.5) : 1.0;
        }

        public static string NormalizeRole(string raw)
        {
            string s = (raw ?? "").Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
            switch (s)
            {
                case "intro":
                case "opening":
                case "idle":
                    return "intro";
                case "foreplay":
                case "tease":
                case "aibu":
                    return "foreplay";
                case "oral":
                case "blowjob":
                case "mouth":
                    return "oral";
                case "hand":
                case "handjob":
                case "service":
                    return "hand";
                case "slow":
                case "gentle":
                case "wloop":
                    return "slow";
                case "medium":
                case "mid":
                case "normal":
                case "main":
                    return "medium";
                case "fast":
                case "intense":
                case "sprint":
                case "sloop":
                    return "fast";
                case "climax":
                case "orgasm":
                case "ejac":
                case "cum":
                case "oloop":
                    return "climax";
                case "afterglow":
                case "outro":
                case "cooldown":
                    return "afterglow";
                case "transition":
                    return "transition";
                default:
                    return s.Length > 0 ? s : "medium";
            }
        }
    }

    public sealed class StudioDirectorBook
    {
        public readonly List<StudioDirectorSegment> Segments = new List<StudioDirectorSegment>();
        public string SceneName = "";
        public string SourceKind = "empty"; // "director_json" | "chapters_fallback" | "empty"
        public double Duration;

        public void Clear()
        {
            Segments.Clear();
            SceneName = "";
            SourceKind = "empty";
            Duration = 0;
        }

        public void LoadSegments(IEnumerable<StudioDirectorSegment> items, double duration, string sceneName, string sourceKind)
        {
            Segments.Clear();
            Duration = Numbers.Finite(duration) && duration > 0 ? duration : 0;
            SceneName = sceneName ?? "";
            SourceKind = string.IsNullOrEmpty(sourceKind) ? "director_json" : sourceKind;
            if (items == null) return;
            int idx = 0;
            foreach (var item in items)
            {
                if (item == null) continue;
                var copy = item.Copy();
                copy.Sanitize(Duration, idx);
                if (copy.End - copy.Start >= 0.25)
                {
                    Segments.Add(copy);
                    idx++;
                }
            }
            Segments.Sort((a, b) => a.Start.CompareTo(b.Start));
        }

        public static StudioDirectorBook BuildFallbackFromChapters(ChapterBook book, double duration, string sceneName)
        {
            var result = new StudioDirectorBook();
            double dur = Numbers.Finite(duration) && duration > 0 ? duration : 0;
            var list = new List<StudioDirectorSegment>();
            if (book != null && book.Chapters.Count > 0)
            {
                for (int i = 0; i < book.Chapters.Count; i++)
                {
                    var ch = book.Chapters[i];
                    double start = Math.Max(0, ch.Start);
                    double end = i + 1 < book.Chapters.Count ? book.Chapters[i + 1].Start : dur;
                    if (end <= start + 0.25) continue;
                    string label = !string.IsNullOrEmpty(ch.CustomName) ? ch.CustomName : ch.Name;
                    string role = ch.IsClimax ? "climax" : InferRoleFromOpening(ch.Opening, i, book.Chapters.Count);
                    list.Add(new StudioDirectorSegment
                    {
                        Id = "ch_" + (i + 1).ToString("D2", CultureInfo.InvariantCulture),
                        Label = string.IsNullOrEmpty(label) ? ("Chapter " + (i + 1)) : label,
                        Role = role,
                        Notes = ch.ClimaxEvidence ?? "",
                        Start = start,
                        End = end,
                        LoopStart = start,
                        LoopEnd = end,
                        Loopable = !ch.IsClimax && (end - start) >= 2.0,
                        DefaultSpeed = 1.0
                    });
                }
            }
            else if (dur >= 1.0)
            {
                list.Add(new StudioDirectorSegment
                {
                    Id = "seg_01",
                    Label = string.IsNullOrEmpty(sceneName) ? "Full Scene" : sceneName,
                    Role = "medium",
                    Start = 0,
                    End = dur,
                    LoopStart = 0,
                    LoopEnd = dur,
                    Loopable = true,
                    DefaultSpeed = 1.0
                });
            }
            result.LoadSegments(list, dur, sceneName, list.Count > 0 ? "chapters_fallback" : "empty");
            return result;
        }

        private static string InferRoleFromOpening(int opening, int index, int total)
        {
            if (opening == 2) return "oral";
            if (opening == 3 || opening == 4 || opening == 6 || opening == 7) return "hand";
            if (opening == 5) return "foreplay";
            if (index == 0 && total >= 3) return "intro";
            return "medium";
        }

        public StudioDirectorSegment At(double time)
        {
            if (!Numbers.Finite(time) || Segments.Count == 0) return null;
            StudioDirectorSegment best = null;
            for (int i = 0; i < Segments.Count; i++)
            {
                var s = Segments[i];
                if (time >= s.Start - 0.001 && time <= s.End + 0.001) return s;
                if (s.Start <= time) best = s;
            }
            return best;
        }

        public StudioDirectorSegment Find(string query)
        {
            if (Segments.Count == 0) return null;
            string q = (query ?? "").Trim();
            if (q.Length == 0) return Segments[0];

            // 1. Exact ID match (case-insensitive)
            for (int i = 0; i < Segments.Count; i++)
                if (string.Equals(Segments[i].Id, q, StringComparison.OrdinalIgnoreCase))
                    return Segments[i];

            // 2. 1-based numeric index
            int oneBased;
            if (int.TryParse(q, NumberStyles.Integer, CultureInfo.InvariantCulture, out oneBased) &&
                oneBased >= 1 && oneBased <= Segments.Count)
                return Segments[oneBased - 1];

            // 3. Exact normalized Role match (prefer loopable segment for non-climax roles)
            string normRole = StudioDirectorSegment.NormalizeRole(q);
            StudioDirectorSegment roleFallback = null;
            for (int i = 0; i < Segments.Count; i++)
            {
                if (string.Equals(Segments[i].Role, normRole, StringComparison.OrdinalIgnoreCase))
                {
                    if (normRole == "climax" || Segments[i].Loopable) return Segments[i];
                    if (roleFallback == null) roleFallback = Segments[i];
                }
            }
            if (roleFallback != null) return roleFallback;

            // 4. Case-insensitive substring on Label or Notes
            string low = q.ToLowerInvariant();
            for (int i = 0; i < Segments.Count; i++)
            {
                var s = Segments[i];
                if ((s.Label != null && s.Label.ToLowerInvariant().Contains(low)) ||
                    (s.Notes != null && s.Notes.ToLowerInvariant().Contains(low)))
                    return s;
            }
            return null;
        }

        public StudioDirectorSegment FindClimax()
        {
            for (int i = 0; i < Segments.Count; i++)
                if (string.Equals(Segments[i].Role, "climax", StringComparison.OrdinalIgnoreCase))
                    return Segments[i];
            for (int i = 0; i < Segments.Count; i++)
            {
                string low = ((Segments[i].Label ?? "") + " " + (Segments[i].Notes ?? "")).ToLowerInvariant();
                if (low.Contains("climax") || low.Contains("高潮") || low.Contains("射精") || low.Contains("爆发"))
                    return Segments[i];
            }
            return null;
        }
    }

    public sealed class StudioLoopTickResult
    {
        public bool NeedSeek;
        public double SeekTime;
        public bool NeedPause;
        public bool NeedPlay;
        public bool NeedSpeed;
        public float TargetSpeed = 1f;
        public bool TriggerTransitionFade;
        public string EventNote = "";
    }

    public sealed class StudioSegmentLoopController
    {
        private const double EndMarginSeconds = 0.02;
        private const double UnderflowToleranceSeconds = 0.15;

        public StudioPlaybackMode Mode { get; private set; }
        public StudioDirectorSegment ActiveSegment { get; private set; }
        public StudioDirectorSegment QueuedNextSegment { get; private set; }
        public bool PauseAtEnd { get; private set; }
        public bool FreezeCamera { get; private set; }
        public double TargetSpeed { get; private set; }
        public int LoopCount { get; private set; }
        public double ActiveElapsedSeconds { get; private set; }
        public double MaxDurationSeconds { get; private set; }

        private double lastSeekWallSeconds = -10.0;
        private double wallClock;

        public StudioSegmentLoopController()
        {
            Reset();
        }

        public void Reset()
        {
            Mode = StudioPlaybackMode.Linear;
            ActiveSegment = null;
            QueuedNextSegment = null;
            PauseAtEnd = false;
            FreezeCamera = false;
            TargetSpeed = 1.0;
            LoopCount = 0;
            ActiveElapsedSeconds = 0;
            MaxDurationSeconds = 0;
            lastSeekWallSeconds = -10.0;
        }

        public StudioLoopTickResult StartSegment(
            StudioDirectorSegment segment,
            bool loop,
            double speed,
            bool freezeCamera,
            double maxDurationSeconds,
            StudioDirectorSegment nextSegment,
            bool pauseAtEnd)
        {
            if (segment == null) throw new ArgumentNullException("segment");
            if (segment.End <= segment.Start + 0.2)
                throw new ArgumentException("Segment duration is too short");

            ActiveSegment = segment.Copy();
            Mode = loop ? StudioPlaybackMode.SegmentLoop : StudioPlaybackMode.SegmentOnce;
            QueuedNextSegment = nextSegment != null ? nextSegment.Copy() : null;
            PauseAtEnd = pauseAtEnd;
            FreezeCamera = freezeCamera;
            TargetSpeed = Numbers.Clamp(Numbers.Finite(speed) && speed > 0 ? speed : ActiveSegment.DefaultSpeed, 0.25, 2.5);
            LoopCount = 0;
            ActiveElapsedSeconds = 0;
            MaxDurationSeconds = Numbers.Finite(maxDurationSeconds) && maxDurationSeconds > 0 ? maxDurationSeconds : 0;
            lastSeekWallSeconds = wallClock;

            double startAt = loop ? ActiveSegment.EffectiveLoopStart : ActiveSegment.Start;
            return new StudioLoopTickResult
            {
                NeedSeek = true,
                SeekTime = startAt,
                NeedPlay = true,
                NeedSpeed = true,
                TargetSpeed = (float)TargetSpeed,
                TriggerTransitionFade = true,
                EventNote = (loop ? "Loop segment: " : "Play segment: ") + ActiveSegment.Id
            };
        }

        public StudioLoopTickResult SetSpeed(double speed)
        {
            TargetSpeed = Numbers.Clamp(Numbers.Finite(speed) && speed > 0 ? speed : 1.0, 0.25, 2.5);
            return new StudioLoopTickResult
            {
                NeedSpeed = true,
                TargetSpeed = (float)TargetSpeed,
                EventNote = "Speed " + TargetSpeed.ToString("F2", CultureInfo.InvariantCulture) + "x"
            };
        }

        public StudioLoopTickResult ReleaseLinear(bool resumePlay, double speed)
        {
            double s = Numbers.Clamp(Numbers.Finite(speed) && speed > 0 ? speed : 1.0, 0.25, 2.5);
            Reset();
            TargetSpeed = s;
            return new StudioLoopTickResult
            {
                NeedPlay = resumePlay,
                NeedSpeed = true,
                TargetSpeed = (float)s,
                TriggerTransitionFade = true,
                EventNote = "Released to linear playback"
            };
        }

        public StudioLoopTickResult Tick(double clockTime, bool clockPlaying, double dt)
        {
            var result = new StudioLoopTickResult();
            if (!Numbers.Finite(dt) || dt < 0) dt = 0;
            if (dt > 0.25) dt = 0.25;
            wallClock += dt;

            if (Mode == StudioPlaybackMode.Linear || ActiveSegment == null || !Numbers.Finite(clockTime))
                return result;

            if (clockPlaying) ActiveElapsedSeconds += dt;

            // Optional TTL expiration on loop mode
            if (Mode == StudioPlaybackMode.SegmentLoop && MaxDurationSeconds > 0 && ActiveElapsedSeconds >= MaxDurationSeconds)
            {
                if (QueuedNextSegment != null)
                {
                    var next = QueuedNextSegment;
                    return StartSegment(next, next.Loopable && next.Role != "climax" && next.Role != "afterglow", next.DefaultSpeed, FreezeCamera, 0, null, next.Role == "afterglow");
                }
                return ReleaseLinear(true, 1.0);
            }

            if (Mode == StudioPlaybackMode.SegmentLoop)
            {
                double lo = ActiveSegment.EffectiveLoopStart;
                double hi = ActiveSegment.EffectiveLoopEnd;
                if (hi <= lo + 0.25)
                {
                    lo = ActiveSegment.Start;
                    hi = ActiveSegment.End;
                }
                // Guard against repeated seeks within 60ms while Timeline processes Seek()
                bool seekReady = (wallClock - lastSeekWallSeconds) >= 0.06;
                if (seekReady && (clockTime >= hi - EndMarginSeconds || clockTime < lo - UnderflowToleranceSeconds))
                {
                    lastSeekWallSeconds = wallClock;
                    if (clockTime >= hi - EndMarginSeconds) LoopCount++;
                    result.NeedSeek = true;
                    result.SeekTime = lo;
                    result.TriggerTransitionFade = true;
                    result.EventNote = "Loop wrap #" + LoopCount + " -> " + lo.ToString("F2", CultureInfo.InvariantCulture) + "s";
                }
            }
            else if (Mode == StudioPlaybackMode.SegmentOnce)
            {
                if (clockTime >= ActiveSegment.End - EndMarginSeconds)
                {
                    if (QueuedNextSegment != null)
                    {
                        var next = QueuedNextSegment;
                        bool nextLoop = next.Loopable && next.Role != "climax" && next.Role != "afterglow";
                        bool nextPause = next.Role == "afterglow" || PauseAtEnd;
                        return StartSegment(next, nextLoop, next.DefaultSpeed, FreezeCamera, 0, null, nextPause);
                    }
                    if (PauseAtEnd)
                    {
                        Mode = StudioPlaybackMode.Linear;
                        FreezeCamera = false;
                        result.NeedPause = true;
                        result.EventNote = "Segment finished & paused: " + ActiveSegment.Id;
                    }
                    else
                    {
                        Mode = StudioPlaybackMode.Linear;
                        FreezeCamera = false;
                        result.EventNote = "Segment finished -> linear: " + ActiveSegment.Id;
                    }
                }
            }

            return result;
        }
    }
}
