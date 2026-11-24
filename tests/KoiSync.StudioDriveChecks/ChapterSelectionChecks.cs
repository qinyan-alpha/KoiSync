using KoiSync.Studio;

internal static class ChapterSelectionChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var s=new ChapterSelection();
        s.Choose("oral",0,0,true,1,false);
        for(int i=1;i<=40;i++)s.Choose(i%3==0?"hand":"oral",i*.1,i*.1,true,1,false);
        check(s.Identity=="oral","Brief alternating hand proximity cannot replace sustained oral contact");
        for(int i=41;i<=49;i++)s.Choose("hand",i*.1,i*.1,true,1,false);
        check(s.Identity=="hand"&&Math.Abs(s.Start-4.1)<1e-8,"Sustained contact change records the first confirmed observation time");
        s.Choose("vaginal",60,5,true,1,false);
        check(!s.Continuous&&s.Identity=="vaginal","A seek updates the preview without reporting continuous analysis");
        s.Choose("anal",61,6,false,1,false);s.Choose("oral",61,7,false,1,false);
        check(!s.Continuous&&s.Identity=="oral","Paused edits never accumulate chapter evidence");
        s.Choose("oral",61,8,true,1,false);check(!s.Continuous,"First frame after resume establishes a new playback origin");
        s.Choose("oral",61.1,8.1,true,1,false);s.Choose("new-pair",61.2,8.2,true,1,true);
        check(s.Continuous&&s.Identity=="new-pair","Hidden group or explicit binding change can switch immediately");
        s.Reset();s.Choose("a",0,0,true,2,false);s.Choose("a",.2,.1,true,2,false);
        check(s.Continuous,"Continuity respects Timeline playback speed");
        s.Choose("b",.2,.2,false,2,false);check(!s.Continuous,"Pausing at a candidate transition does not confirm a chapter");
    }
}
