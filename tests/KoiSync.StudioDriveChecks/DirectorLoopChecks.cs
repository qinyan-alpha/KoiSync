using System;
using KoiSync.Studio;

internal static class DirectorLoopChecks
{
    public static void Run(Action<bool, string> check)
    {
        // 1. Segment sanitization & lookup
        var book = new StudioDirectorBook();
        book.LoadSegments(new[]
        {
            new StudioDirectorSegment { Id = "seg_01", Label = "开场前戏", Role = "intro", Start = 0, End = 10.5, LoopStart = 0.6, LoopEnd = 5.1, Loopable = true, FrequencyHz = 0.59, SeamErrorCm = 0.07 },
            new StudioDirectorSegment { Id = "seg_02", Label = "稳健主循环", Role = "medium", Start = 10.5, End = 36.2, LoopStart = 21.7, LoopEnd = 30.4, Loopable = true, FrequencyHz = 1.4, SeamErrorCm = 0.79 },
            new StudioDirectorSegment { Id = "seg_03", Label = "加速冲刺", Role = "fast", Start = 80.6, End = 114.5, LoopStart = 92.0, LoopEnd = 108.0, Loopable = true, FrequencyHz = 2.1, SeamErrorCm = 1.4 },
            new StudioDirectorSegment { Id = "seg_04", Label = "高潮爆发", Role = "climax", Start = 114.5, End = 120.0, Loopable = false },
            new StudioDirectorSegment { Id = "seg_05", Label = "高潮余韵", Role = "afterglow", Start = 120.0, End = 128.9, Loopable = false }
        }, 128.9, "2023_0726_0212_58_942", "director_json");

        check(book.Segments.Count == 5, "DirectorBook loads all valid segments");
        check(book.Find("seg_02")?.Id == "seg_02", "DirectorBook finds by exact id");
        check(book.Find("2")?.Id == "seg_02", "DirectorBook finds by 1-based index");
        check(book.Find("sloop")?.Id == "seg_03", "DirectorBook maps sloop alias to fast role");
        check(book.Find("主循环")?.Id == "seg_02", "DirectorBook finds by label substring");
        check(book.FindClimax()?.Id == "seg_04", "DirectorBook locates climax segment");
        check(book.At(25.0)?.Id == "seg_02", "DirectorBook.At locates active segment at playhead");

        // 2. Fallback derivation from ChapterBook
        var chapters = new ChapterBook();
        chapters.Observe(0, new Chapter { Source = 1, Target = 2, Opening = 2, Name = "Oral" });
        chapters.Observe(30, new Chapter { Source = 1, Target = 2, Opening = 0, Name = "Main" });
        var clxChapter = chapters.Split(90);
        clxChapter.CustomName = "Climax";
        clxChapter.Phase = ChapterPhase.Climax;
        var fallback = StudioDirectorBook.BuildFallbackFromChapters(chapters, 110, "FallbackScene");
        check(fallback.SourceKind == "chapters_fallback", "Fallback book reports chapters_fallback source");
        check(fallback.Segments.Count == 3, "Fallback book maps all chapters");
        check(fallback.Segments[0].Role == "oral", "Fallback book infers oral opening role");
        check(fallback.FindClimax()?.Id == "ch_03", "Fallback book preserves climax chapter role");

        // 3. SegmentLoop clamping, wrap counting, transition fade trigger, and speed clamp
        var loop = new StudioSegmentLoopController();
        var startTick = loop.StartSegment(book.Find("seg_02"), true, 1.35, true, 0, null, false);
        check(startTick.NeedSeek && Math.Abs(startTick.SeekTime - 21.7) < 1e-6, "Loop start seeks directly to effective loopStart");
        check(startTick.NeedPlay && startTick.NeedSpeed && Math.Abs(startTick.TargetSpeed - 1.35f) < 1e-4, "Loop start activates playback and target speed");
        check(startTick.TriggerTransitionFade, "Loop start requests smooth chapter transition fade");
        check(loop.Mode == StudioPlaybackMode.SegmentLoop && loop.FreezeCamera, "Loop controller tracks SegmentLoop and FreezeCamera");

        // Mid-segment tick should not seek
        var midTick = loop.Tick(25.0, true, 0.1);
        check(!midTick.NeedSeek && !midTick.NeedPause, "Mid-segment playhead does not seek");

        // Reaching loopEnd triggers seamless wrap back to loopStart with transition fade
        var wrapTick = loop.Tick(30.39, true, 0.1);
        check(wrapTick.NeedSeek && Math.Abs(wrapTick.SeekTime - 21.7) < 1e-6, "Reaching loopEnd wraps back to loopStart");
        check(wrapTick.TriggerTransitionFade, "Loop wrap triggers 6-axis transition fade");
        check(loop.LoopCount == 1, "Loop wrap increments LoopCount");

        // Rapid duplicate frame within 60ms must not double-seek while Timeline processes Seek
        var guardTick = loop.Tick(30.40, true, 0.02);
        check(!guardTick.NeedSeek, "60ms seek guard suppresses duplicate wrap seeks");

        // Speed clamping
        var speedTick = loop.SetSpeed(9.9);
        check(speedTick.NeedSpeed && Math.Abs(speedTick.TargetSpeed - 2.5f) < 1e-4, "Speed clamps cleanly to 2.5x upper bound");

        // 4. SegmentOnce climax -> queued afterglow auto-transition -> pause at end
        var clxTick = loop.StartSegment(book.Find("seg_04"), false, 1.0, false, 0, book.Find("seg_05"), true);
        check(loop.Mode == StudioPlaybackMode.SegmentOnce && Math.Abs(clxTick.SeekTime - 114.5) < 1e-6, "Climax plays once from segment start");
        var handoff = loop.Tick(119.99, true, 0.1);
        check(handoff.NeedSeek && Math.Abs(handoff.SeekTime - 120.0) < 1e-6, "Climax end automatically hands off to afterglow");
        check(loop.ActiveSegment.Id == "seg_05", "ActiveSegment advances to afterglow");
        var finish = loop.Tick(128.89, true, 0.1);
        check(finish.NeedPause && loop.Mode == StudioPlaybackMode.Linear, "Afterglow end pauses cleanly and exits lock");

        // 5. TTL loop expiration & release
        loop.StartSegment(book.Find("seg_01"), true, 1.0, true, 1.0, book.Find("seg_02"), false);
        var ttlAdvance = loop.Tick(2.5, true, 0.25);
        ttlAdvance = loop.Tick(2.8, true, 0.25);
        ttlAdvance = loop.Tick(3.1, true, 0.25);
        ttlAdvance = loop.Tick(3.4, true, 0.25);
        check(ttlAdvance.NeedSeek && loop.ActiveSegment.Id == "seg_02", "Loop TTL expiration transitions into queued next segment");

        var rel = loop.ReleaseLinear(true, 1.0);
        check(loop.Mode == StudioPlaybackMode.Linear && !loop.FreezeCamera && rel.NeedPlay, "ReleaseLinear clears lock, unfreezes camera, and resumes linear playback");
    }
}
