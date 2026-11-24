using System.Text.Json;
using KoiSync.Core;
using KoiSync.Studio;

internal static class ClimaxChecks
{
    public static void Run(Action<bool,string> check)
    {
        foreach(string clip in new[]{"Orgasm","WOrgasm","Orgasm_Start","Orgasm_Loop","M_IN_Start","M_OUT_Loop","WF_IN_Loop","SS_IN_Start","L_M_IN_Start","M_A_M_OUT_Start","S_A_SF_IN_Loop","IN_Loop","Oral_Idle_IN"})
            check(ClimaxCues.Animation(clip),"Explicit active animation: "+clip);
        foreach(string clip in new[]{"L_OLoop","OLoop","Orgasm_A","Orgasm_B","WS_IN_A","L_SS_IN_A","M_OUT_A","OUT_A","IN_A","Idle","Before_Orgasm","SLoop","AfterClimax"})
            check(!ClimaxCues.Animation(clip),"Preparatory / afterglow state excluded: "+clip);
        check(ClimaxCues.Shooting("Cum Juice 15 (Shooting)"),"Verified event prop recognized");
        foreach(string name in new[]{"Cum Juice 54 (Drip)","Cum Juice 46 (Cum-Covered P*)","liquid","Cum Juice 32 (Abdomen)","Cum Juice 55 (Shoot Dirty)","Cum"})
            check(!ClimaxCues.Shooting(name),"Residue does not trigger: "+name);
        var windows=ClimaxCues.Windows(new[]{0d,2,2.2,2.4,2.5,3,20},new[]{false,true,true,false,true,false,true},40,6,.65);
        check(windows.Count==2&&windows[0].Start==2&&windows[0].End==3,"Pulse cluster merges without repeated true keys restarting it");
        check(windows[1].Start==20&&windows[1].End==26,"Persistent prop activation has a finite maximum");
        var bursts=ClimaxCues.Windows(new[]{0d,2,3.8,5,6.8,8,9.8,12,13},new[]{false,true,false,true,false,true,false,true,false},20,6,ClimaxCues.EpisodeGap);
        check(bursts.Count==2&&bursts[0].Start==2&&bursts[0].End==9.8&&bursts[1].Start==12,"Short gaps within a burst episode do not create repeated tiny chapters; a longer pause separates events");
        var defaults=new ClimaxOptions();var pulse=defaults.Create(2,10,0,1);
        for(int cycle=0;cycle<10;cycle++)
        {
            check(Math.Abs(pulse.Value(2+cycle*.1)-350d/999)<1e-10,"10 Hz lower endpoint "+cycle);
            check(Math.Abs(pulse.Value(2+cycle*.1+.05)-750d/999)<1e-10,"10 Hz upper endpoint "+cycle);
        }
        check(pulse.Value(2.027)==pulse.Value(2.027),"Same paused / sought time has deterministic phase");
        var drive=new StudioDrive();drive.Update(new[]{.8,.5,.5,.5,.5,.5},.02,AxisMappingMode.Absolute);
        double sceneInput=drive.Input[0];drive.ApplyL0Effect(pulse,2.05);
        check(Math.Abs(drive.Input[0]-sceneInput)<1e-12,"Climax effect preserves the teal geometry input");
        check(Math.Abs(drive.Target[0]-750d/999)<1e-10&&Math.Abs(drive.Values[0]-750d/999)<1e-10,"Climax effect immediately replaces the orange L0 target and output");
        check(Math.Abs(drive.Values[1]-.5)<1e-12&&Math.Abs(drive.Values[5]-.5)<1e-12,"Climax effect leaves the other five outputs live");
        var limited=defaults.Create(0,2,.5,.65);check(limited.Minimum==.5&&limited.Maximum==.65,"Effect respects explicit and physical final limits");
        defaults.Enabled=false;check(defaults.Create(0,2,0,1)==null,"Effect can be disabled independently of chapter label");defaults.Enabled=true;
        var book=new ChapterBook();book.Observe(0,new Chapter{Source=1,Target=2,Manual=true,CustomName="Edited",Calibration=new ChapterCalibration{Length=.16,L1=.08,L2=.08}});
        check(book.MarkClimax(2,5,10,"1:2:0",defaults,"Timeline: fixture"),"Explicit event splits a manually bound chapter without changing its binding");
        check(book.Chapters.Count==3&&!book.At(1).IsClimax&&book.At(2).IsClimax&&!book.At(5).IsClimax,"Only the exact event interval is tagged");
        check(book.At(3).Manual&&book.At(3).CustomName=="Edited"&&book.At(3).Calibration.Length==.16,"Manual title and calibration survive automatic event overlay");
        check(!book.MarkClimax(2,5,10,"1:2:0",defaults,"Timeline: fixture")&&book.Chapters.Count==3,"Replaying an event is idempotent");
        string xml=book.ToXml();var copy=new ChapterBook();copy.FromXml(xml);
        check(copy.At(3).IsClimax&&copy.At(3).Climax.Hertz==10&&copy.At(3).ClimaxEvidence=="Timeline: fixture","Effect and recognition evidence round-trip with chapters");
        check(copy.Split(3).IsClimax&&copy.At(3).Climax!=copy.At(2).Climax,"Manual split copies effect independently");
        copy.At(3).Phase=ChapterPhase.Normal;
        check(!copy.MarkClimax(3,4,10,"1:2:0",defaults,"fixture")&&!copy.At(3).IsClimax,"Manual normal phase blocks recognition");
        check(!copy.MarkClimax(1,2,10,"99:2:0",defaults,"fixture"),"An unrelated pair cannot acquire the event");
        var deleted=new ChapterBook();deleted.FromXml(xml);deleted.Remove(deleted.At(3));
        check(!deleted.MarkClimax(2,5,10,"1:2:0",defaults,"fixture")&&!deleted.At(3).IsClimax,"Deleting a detected chapter suppresses its immediate recreation");
        var recorder=new StudioRecorder();var wave=defaults.Create(0,1,0,1);recorder.Begin(0);
        // Deliberately irregular, much slower than the desired oscillation.
        foreach(double t in new[]{0d,.137,.312,.551,.789,1d})recorder.Add(t,new[]{wave.Value(t),.5,.4,.5,.5,.5},wave);
        string directory=Path.Combine(Path.GetTempPath(),"KoiSync-climax-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        try
        {
            recorder.Save(Path.Combine(directory,"pulse"));using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"pulse.funscript")));
            var actions=doc.RootElement.GetProperty("actions").EnumerateArray().Select(a=>(Time:a.GetProperty("at").GetInt32(),Pos:a.GetProperty("pos").GetInt32())).ToArray();
            check(actions.Zip(actions.Skip(1),(a,b)=>a.Time<b.Time).All(x=>x),"Recorded keys remain strictly ordered");
            for(int half=0;half<=20;half++)check(actions.Any(a=>a.Time==half*50&&a.Pos==(half%2==0?35:75)),"Exact 50 ms turn retained at low frame rate: "+half);
            using var side=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"pulse.surge.funscript")));
            check(side.RootElement.GetProperty("actions").EnumerateArray().All(a=>a.GetProperty("pos").GetInt32()==50),"Added L0 turns do not create motion on other axes");
        }
        finally{Directory.Delete(directory,true);}
        // Simulate a costly frame: the solved value lags the timestamp, and a
        // frame and a turn round to the same millisecond.
        var collision=new StudioRecorder();collision.Begin(0);
        foreach(double t in new[]{0d,.0504,.1002,.1501,.2004})collision.Add(t,new[]{.63,.5,.5,.5,.5,.5},wave);
        string collisionDir=Path.Combine(Path.GetTempPath(),"KoiSync-pulse-collision-"+Guid.NewGuid());Directory.CreateDirectory(collisionDir);
        try
        {
            collision.Save(Path.Combine(collisionDir,"pulse"));using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(collisionDir,"pulse.funscript")));
            var actions=doc.RootElement.GetProperty("actions").EnumerateArray().ToArray();
            for(int half=0;half<=4;half++)check(actions.Any(a=>a.GetProperty("at").GetInt32()==half*50&&a.GetProperty("pos").GetInt32()==(half%2==0?35:75)),"Turn overrides a same-millisecond rendered frame: "+half);
        }
        finally{Directory.Delete(collisionDir,true);}
    }
}
