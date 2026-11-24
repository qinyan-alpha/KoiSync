using System.Text.Json;
using KoiSync.Core;
using KoiSync.Studio;

internal static class WorkflowChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var book=new ChapterBook();book.Observe(0,new Chapter{Source=3,Target=9,SourceName="来源",TargetName="目标",Calibration=new ChapterCalibration{Length=.12,L1=.04,L2=.025,Origin=new(.01,-.002,.003),Linked=false}});
        var later=book.Split(10);later.Calibration.Length=.18;later.Calibration.L2=.06;
        check(book.At(0).Calibration.Length==.12,"Splitting clones calibration rather than aliasing it");
        var loaded=new ChapterBook();loaded.FromXml(book.ToXml());
        check(loaded.At(5).Calibration.Length==.12&&loaded.At(11).Calibration.Length==.18,"Same actor pair retains distinct calibration in adjacent chapters");
        check(loaded.At(11).Calibration.L2==.06&&!loaded.At(11).Calibration.Linked&&loaded.At(0).SourceName=="来源","Ranges, linkage and actor names survive metadata roundtrip");
        check(loaded.At(0).Calibration.Origin.Y==-.002,"Signed origin offsets survive metadata roundtrip");
        string valid=loaded.ToXml();bool rejected=false;try{loaded.FromXml(valid.Replace("length=\"0.12\"","length=\"NaN\""));}catch(ArgumentException){rejected=true;}
        check(rejected&&loaded.ToXml()==valid,"Invalid calibration does not partially replace chapters");
        loaded.Observe(12,new Chapter{Source=6,Target=8});check(loaded.At(12).Source==3,"Visible manual chapter keeps its selected pair");
        loaded.Observe(12,new Chapter{Source=6,Target=8},true);check(loaded.At(12).Source==6&&loaded.At(11).Source==3,"Hidden manual pair can end at an exact visibility boundary");
        var legacy=new ChapterBook();legacy.FromXml("<chapters version=\"1\"><chapter time=\"0\" source=\"1\" target=\"2\" opening=\"0\" manual=\"false\" name=\"legacy\" /></chapters>");
        check(legacy.At(0).Calibration==null,"Existing cards without chapter calibration remain readable");
        var drive=new StudioDrive();drive.Gain[0]=4;drive.Gain[1]=3;
        drive.UpdateScript(new[]{.21,.63,.4,.6,.45,.55});
        check(drive.Values[0]==.21&&drive.Values[1]==.63,"Playback does not invert L0 or reapply recording gains");
        drive.Maximum[1]=.55;drive.UpdateScript(new[]{.21,.63,.4,.6,.45,.55});check(drive.Values[1]==.55,"User travel limits also apply during script playback");
        check(CameraPairRanking.Score(.5,.5,10,1.8)>CameraPairRanking.Score(.65,.5,1,1.8),"Screen centre takes priority over physical camera distance");
        check(CameraPairRanking.Score(.5,.5,-1,1.8)<0&&CameraPairRanking.Score(1.2,.5,1,1.8)<0,"Behind-camera and offscreen pairs cannot win centre ranking");
        check(CameraPairRanking.KeepContact(.012,.008,.035),"Nearby openings do not create millimetre-scale chapter flicker");
        check(!CameraPairRanking.KeepContact(.03,.008,.035),"Clearly closer opening can replace the estimated contact");
        check(!CameraPairRanking.KeepContact(.04,.038,.035),"Lost contact cannot be retained by opening hysteresis");
        check(!CameraPairRanking.AcquireCavity(.030,.174,.5),"A distant adjacent entrance cannot start a new internal chapter");
        check(!CameraPairRanking.AcquireCavity(.017,.174,0),"Approach without insertion does not claim a nearby internal opening");
        check(CameraPairRanking.AcquireCavity(.012,.174,.5)&&CameraPairRanking.AcquireCavity(.004,.174,0),"Confirmed insertion and direct tip contact can acquire an entrance");
        check(CameraPairRanking.KeepCavity(.022,.009,.174,.5),"Ongoing cavity contact wins over a nearby rival opening");
        check(CameraPairRanking.PreferCavityToGrip(.016,.174,.45)&&!CameraPairRanking.PreferCavityToGrip(.016,.174,0),"Confirmed internal contact outranks an incidental supporting grip");
        check(CameraPairRanking.KeepCavityOverGrip(.026,.174,.32)&&CameraPairRanking.KeepCavityOverGrip(.011,.174,0),"A short withdrawal toward the tip does not split the oral chapter");
        check(!CameraPairRanking.KeepCavityOverGrip(.045,.174,.5)&&!CameraPairRanking.KeepCavityOverGrip(.025,.174,0),"Clear separation allows a genuine change to a hand interaction");
        check(!CameraPairRanking.PreferContact(1,true,.008,0,false,.001)&&CameraPairRanking.PreferContact(0,false,.001,1,true,.008),"Grip priority must not favour a farther adjacent internal entrance");
        check(CameraPairRanking.PreferContact(2,true,.016,3,false,.002)&&!CameraPairRanking.PreferContact(3,false,.002,2,true,.016),"Supporting grip priority is independent of bone iteration order");
        check(!CameraPairRanking.KeepCavity(.040,.004,.174,.5)&&!CameraPairRanking.KeepCavity(.030,.002,.174,.5)&&!CameraPairRanking.KeepCavity(.012,.003,.174,0),"Lost insertion or clearly centred new contact permits a real change");
        check(!CameraPairRanking.AttachmentMatchesContact(.485,.140),"A stale BP mouth target cannot suppress the current entrance contact");
        check(CameraPairRanking.AttachmentMatchesContact(.016,.140),"The visible nearby entrance remains eligible after a constraint is disabled");
        check(CameraPairRanking.AttachmentMatchesContact(.037,.222),"Existing oral contact tolerance is preserved for a longer model");
        check(!CameraPairRanking.AttachmentMatchesContact(double.NaN,.140)&&!CameraPairRanking.AttachmentMatchesContact(double.PositiveInfinity,.140),"Invalid BP contact distances are not authoritative");
        string temp=Path.Combine(Path.GetTempPath(),"koisync-workflow-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            string path=SceneScriptPath.BasePath(temp,Path.Combine(temp,"UserData/Studio/scene/sixpluswan/020 - FACE.png"));
            check(path==Path.GetFullPath(Path.Combine(temp,"UserData/KoiSyncStudio/sixpluswan/020 - FACE")),"Scripts mirror the author folder and card name under UserData");
            var recorder=new StudioRecorder();var axes=new[]{.21,.62,.43,.54,.35,.76};
            recorder.Begin(0);recorder.Add(.018,axes);recorder.ExtendEnd(.02);check(recorder.Start==0&&recorder.End==.02,"Recording includes start and final frame boundaries");recorder.Clear();
            recorder.Add(12.345,axes);recorder.Add(12.35,axes);check(recorder.Count==1,"Recording caps capture frequency without creating duplicate times");
            recorder.Add(12.385,axes);check(!recorder.Add(1,axes)&&recorder.Count==2,"Loop or backwards seek cannot overwrite the earlier take");
            recorder.Save(path);var companions=FunscriptFiles.Companions(path+".funscript");check(companions.All(File.Exists),"Selecting L0 discovers all five generated companion files");
            for(int i=0;i<6;i++)
            {
                using var doc=JsonDocument.Parse(File.ReadAllText(companions[i]));var actions=doc.RootElement.GetProperty("actions");
                check(actions[0].GetProperty("at").GetInt64()==12345&&actions[1].GetProperty("at").GetInt64()==12385,"Absolute scene time is preserved for axis "+i);
                check(actions[0].GetProperty("pos").GetInt32()==(int)Math.Round(axes[i]*100),"Final device position is recorded for axis "+i);
            }
        }
        finally{Directory.Delete(temp,true);}
    }
}
