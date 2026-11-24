using KoiSync.Core;
using KoiSync.Plugin;
using KoiSync.Wire;

internal static class FunscriptChecks
{
    internal static void Run(Action<bool,string> check)
    {
        void Near(double actual,double expected,string label)=>check(Math.Abs(actual-expected)<1e-8,label);
        const string json="{\"version\":\"1.0\",\"inverted\":false,\"range\":90,\"metadata\":{\"title\":\"test\"},\"actions\":[{\"at\":1000,\"pos\":100},{\"at\":0,\"pos\":0},{\"at\":500,\"pos\":10},{\"at\":500,\"pos\":50}]}";
        var script=FunscriptPlayback.Parse(json);double position;
        check(script.Count==3&&script.Duration==1,"Funscript sorts actions and keeps the last duplicate timestamp");
        check(script.TryPosition(.25,out position),"Funscript provides an interpolated position");Near(position,.25,"Milliseconds and 0–100 convert to seconds and 0–1");
        foreach(var time in new[]{1d,.1,.9,0,.6}){script.TryPosition(time,out position);Near(position,time,"Arbitrary seek, reverse and loop times use the same interpolation: "+time);}
        check(!script.TryPosition(-.001,out position)&&!script.TryPosition(1.001,out position),"Before start and after end do not invent script motion");
        var inverted=FunscriptPlayback.Parse(json.Replace("false","true"));inverted.TryPosition(.25,out position);Near(position,.75,"Script inverted flag reverses positions");
        var delayed=new Funscript(new[]{new FunscriptAction{At=500,Position=30},new FunscriptAction{At=1000,Position=70}},false);
        delayed.TryPosition(0,out position);Near(position,.3,"A delayed first action holds its first position before its timestamp");
        var single=new Funscript(new[]{new FunscriptAction{At=1000,Position=40}},false);single.TryPosition(.5,out position);Near(position,.4,"Single-action scripts remain a defined static position");
        foreach(string bad in new[]{"{}","{\"actions\":[]}","{\"actions\":[{\"at\":0,\"pos\":101}]}","{\"actions\":[{\"at\":-1,\"pos\":0}]}","{\"actions\":[{\"at\":0.5,\"pos\":0}]}","{\"actions\":[{\"at\":\"0\",\"pos\":0}]}","{\"inverted\":1,\"actions\":[{\"at\":0,\"pos\":0}]}","{\"actions\":[{\"at\":0}]}"})
        {bool rejected=false;try{FunscriptPlayback.Parse(bad);}catch{rejected=true;}check(rejected,"Malformed funscript is rejected: "+bad);}
        string directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"KoiSync-funscript-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            string[] suffixes={"",".surge",".L2",".TWIST",".roll",".R2"};
            for(int i=0;i<6;i++)File.WriteAllText(Path.Combine(directory,"场景"+suffixes[i]+".funscript"),json);
            File.WriteAllText(Path.Combine(directory,"场景2.pitch.funscript"),json);
            File.WriteAllText(Path.Combine(directory,"场景.json"),"{\"metadata\":{}}");
            File.WriteAllText(Path.Combine(directory,"场景.stroke.funscript"),json.Replace("false","true"));
            var playback=new FunscriptPlayback();playback.Load(Path.Combine(directory,"场景.roll.funscript"));
            check(playback.Enabled&&playback.Scripts.Count==6,"Selecting an auxiliary axis loads all six matching sibling scripts");
            check(playback.AxisPaths[5].EndsWith("场景.R2.funscript")&&!playback.Scripts[0].Inverted,"Unrelated stems are excluded and the plain L0 filename wins its alias");
            playback.Load(Path.Combine(directory,"场景.stroke.funscript"));check(playback.Scripts[0].Inverted,"Explicitly selected alias takes precedence over discovered files for its axis");
            playback.Load(Path.Combine(directory,"场景.roll.funscript"));playback.Offset=.5;
            var candidate=playback.Sample(.75,true,true,"timeline");for(int i=0;i<6;i++)Near(candidate.ScriptPositions[i],.25,"All six loaded axes use the same offset clock "+i);
            check(!playback.Sample(.25,true,true,"timeline").Active,"Positive offset delays every loaded axis");
            check(!playback.Sample(.75,false,true,"timeline").Active,"Missing playback clock prevents script output");
            var previous=playback.Scripts;int revision=playback.Revision;
            File.WriteAllText(Path.Combine(directory,"场景.L2.funscript"),"{}");bool failed=false;
            try{playback.Load(Path.Combine(directory,"场景.roll.funscript"));}catch{failed=true;}
            check(failed&&ReferenceEquals(previous,playback.Scripts)&&revision==playback.Revision,"Bad companion cannot partially replace an active script group");
            File.Delete(Path.Combine(directory,"场景.L2.funscript"));playback.Load(Path.Combine(directory,"场景.roll.funscript"));
            check(playback.Scripts.Count==5&&playback.AxisPaths[2]==null,"Missing companion remains absent when changing groups");
            candidate=playback.Sample(.75,true,true,"timeline");check(double.IsNaN(candidate.ScriptPositions[2]),"Missing axis is absent rather than synthesized from L0");
            playback.Clear();check(!playback.Enabled&&playback.Scripts==null&&playback.AxisPaths.All(p=>p==null),"Removing a group clears every loaded axis");
        }
        finally{Directory.Delete(directory,true);}

        var tracks=new Funscript[6];tracks[0]=script;tracks[3]=inverted;
        tracks[5]=new Funscript(new[]{new FunscriptAction{At=0,Position=50},new FunscriptAction{At=500,Position=50}},false);
        var set=new FunscriptSet(tracks);check(set.Duration==1&&set.Count==3,"Six-axis group duration is the longest loaded track");
        set.TrySample(.75,out var positions);check(double.IsNaN(positions[5])&&positions[3]==.25,"A shorter axis stops while the remaining axes continue");
        var pipeline=new MotionPipeline{Minimum=0,Maximum=1};var sync=new SyncSession{Enabled=true};
        var source=new Candidate{Id="script",ScriptMotion=true,ScriptPositions=positions,Active=true};
        var frame=new Snapshot{Session="funscript",Scene="empty studio",Active=true,Sequence=1,SampleTime=.02};
        check(sync.Update(pipeline,frame,source,true,StrokeAxis.Z),"Scripts output with a playback clock and no geometry or calibration cycle");
        Near(pipeline.Desired[0],.75,"Script L0 is not changed by native animation enhancement");Near(pipeline.Desired[3],.25,"Script R0 maps its independent position");
        check(pipeline.AxisCalibrated[0]&&pipeline.AxisCalibrated[3]&&!pipeline.AxisCalibrated[1]&&!pipeline.AxisCalibrated[5],"Only present, unexpired script axes can be sent");
        var enabled=new bool[6];for(int i=0;i<6;i++)enabled[i]=pipeline.AxisCalibrated[i];
        string tcode=TCode.Encode(pipeline.Values,enabled,DeviceKind.SR6,20);check(tcode.Contains("L0")&&tcode.Contains("R0")&&!tcode.Contains("R2")&&!tcode.Contains("L1"),"TCode omits missing and ended script axes");
        source.Active=false;frame.Sequence++;frame.SampleTime+=.02;
        check(!sync.Update(pipeline,frame,source,true,StrokeAxis.Z)&&!pipeline.Armed&&sync.Enabled&&pipeline.HasDesired,"Pause stops sending while retaining sync intent and preview");
        source.Active=true;set.TrySample(.1,out positions);source.ScriptPositions=positions;frame.Sequence++;frame.SampleTime+=.02;
        check(sync.Update(pipeline,frame,source,true,StrokeAxis.Z),"Resume after a backward seek uses the current script position");Near(pipeline.Desired[0],.1,"Backward seek immediately updates target position");
        frame.Sequence++;frame.SampleTime+=.02;sync.Update(pipeline,frame,source,true,StrokeAxis.Z);
        Near(pipeline.Values[0],.1,"Script reaches its authored position without the geometry slew limit");
        pipeline.AxisMinimum[3]=.3;pipeline.AxisMaximum[3]=.6;pipeline.AxisGain[3]=2;pipeline.InvertStroke=true;
        frame.Sequence++;frame.SampleTime+=.02;sync.Update(pipeline,frame,source,true,StrokeAxis.Z);
        Near(pipeline.Desired[0],.9,"Existing L0 reverse applies to scripts");Near(pipeline.Desired[3],.6,"Per-axis script gain still obeys its global limit");
        Near(pipeline.Values[0],.9,"Fast script reversal preserves the full authored stroke");
        sync.Stop();frame.Sequence++;frame.SampleTime+=.02;check(!sync.Update(pipeline,frame,source,true,StrokeAxis.Z)&&!pipeline.Armed,"Manual Stop is not overridden by script playback");
        sync.Enabled=true;set.TrySample(1.1,out positions);source.ScriptPositions=positions;source.Active=false;frame.Sequence++;frame.SampleTime+=.02;
        check(!sync.Update(pipeline,frame,source,true,StrokeAxis.Z)&&!pipeline.HasDesired&&!pipeline.Calibrated,"Past all script ends, no axes remain available");
        var curve=new MotionCurve();curve.Add(.2,1,new[]{.4,double.NaN,.5,double.NaN,.6,double.NaN},"script");
        check(curve.Count==1&&double.IsNaN(curve.At(.2).Values[1]),"Timeline records available axes without inventing missing curve values");
    }
}
