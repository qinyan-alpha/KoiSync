using KoiSync.Core;
using KoiSync.Studio;

internal static class StrokeMappingChecks
{
    public static void Run(Action<bool,string> check)
    {
        void Near(double a,double b,string label)=>check(Math.Abs(a-b)<1e-8,label);
        NativeMotionResult Pose(double model,double entrance,double engagement=1)=>new(){Length=model,TargetDepth=model-entrance,Engagement=engagement,MaterialInsertion=engagement>0?.5:0};
        var pose=Pose(.20,.08);
        Near(StrokeMapping.Depth(pose,.13,.13,.12,AxisMappingMode.Absolute),1,"Absolute mode retains geometric depth until device travel clips");
        Near(StrokeMapping.Depth(pose,.13,.13,.12,AxisMappingMode.Soft)*.12,.078,"A 20 cm asset does not turn a 13 cm interaction into excessive depth");
        foreach(double model in new[]{.15,.20,.30})
            Near(StrokeMapping.Depth(Pose(model,model*.4),.13,.13,.12,AxisMappingMode.Relative)*.12,.078,"The same insertion fraction remains independent of asset length");
        Near(StrokeMapping.Depth(pose,.20,.13,.12,AxisMappingMode.Relative)*.12,.078,"Faithful mode never amplifies a short motion for a longer player length");
        Near(StrokeMapping.Depth(Pose(.20,.16),.20,.13,.12,AxisMappingMode.Soft)*.12,.04,"Soft mode applies the explicit player-to-interaction ratio");
        Near(StrokeMapping.Depth(pose,.08,.13,.12,AxisMappingMode.Relative)*.12,.048,"Faithful mode scales down to a shorter usable length");
        Near(StrokeMapping.Depth(Pose(.30,.12),.13,.195,.12,AxisMappingMode.Soft),StrokeMapping.Depth(pose,.13,.13,.12,AxisMappingMode.Soft),"Uniform scene scaling keeps proportional stroke unchanged");
        Near(StrokeMapping.Depth(Pose(.20,.20),.13,.13,.12,AxisMappingMode.Soft),0,"Visible model tip remains neutral at contact");
        check(StrokeMapping.Depth(Pose(.20,.199),.13,.13,.12,AxisMappingMode.Relative)>0,"Shallow contact is retained without a model-length dead zone");
        Near(StrokeMapping.Depth(Pose(.20,.01,0),.13,.13,.12,AxisMappingMode.Soft),0,"Detached geometry never inserts the device");
        var noCross=Pose(.20,.01);noCross.MaterialInsertion=0;
        Near(StrokeMapping.Depth(noCross,.13,.13,.12,AxisMappingMode.Relative),0,"A visible tip outside the opening cannot insert a virtual shorter rod");
        foreach(double limit in new[]{.05,.10,.12,.20})
        {
            var depths=Enumerable.Range(0,200).Select(i=>StrokeMapping.SoftDepth(i*.003,limit)).ToArray();
            check(depths.All(d=>d>=0&&d<limit)&&depths.Zip(depths.Skip(1),(a,b)=>b>a).All(v=>v),"Depth shoulder stays monotonic and inside the usable stroke");
            Near(StrokeMapping.SoftDepth(limit*.8,limit),limit*.8,"Central 80 percent of usable depth is exact");
            Near(StrokeMapping.SoftDepth(limit*.4,limit),limit*.4,"Small stroke amplitude stays exact");
        }
        var drive=new StudioDrive{PhysicalL0Minimum=1-.08/.12};drive.Gain[0]=10;
        drive.Update(new[]{1.0,.5,.5,.5,.5,.5},.02,AxisMappingMode.Soft);
        Near(drive.Values[0],1-.08/.12,"Gain cannot exceed the player usable depth cap");
        drive.UpdateScript(new[]{0.0,.5,.5,.5,.5,.5});
        Near(drive.Values[0],1-.08/.12,"Loaded scripts retain the configured usable depth cap without remapping");
        drive.Maximum[0]=.1;bool rejected=false;
        try{drive.Update(new[]{1.0,.5,.5,.5,.5,.5},.02);}catch(ArgumentException){rejected=true;}
        check(rejected,"Incompatible hardware and user travel limits stop instead of violating either range");
        var raw=new StudioDrive();raw.Update(new[]{.2,.5,.5,-.4,1.4,-.2},.02,AxisMappingMode.Absolute);
        check(raw.Values[3]==0&&raw.Values[4]==1&&raw.Values[5]==0&&!raw.RelativeRotation,"Absolute profile bypasses every rotation compressor");
        var book=new ChapterBook();book.Observe(0,new Chapter{Source=1,Target=2,StrokeCalibration=ChapterStrokeCalibration.FromBody(1.7)});
        Near(book.At(0).StrokeCalibration.GameLength,.13,"Body scale preset uses the stated 130 mm / 1700 mm convention");
        var split=book.Split(5);split.StrokeCalibration.GameLength=.15;split.StrokeCalibration.FromHeight=false;
        Near(book.At(0).StrokeCalibration.GameLength,.13,"Per-chapter interaction length edits are isolated");
        var restored=new ChapterBook();restored.FromXml(book.ToXml());
        Near(restored.At(6).StrokeCalibration.GameLength,.15,"Manual chapter interaction length survives reload");
        check(!restored.At(6).StrokeCalibration.FromHeight,"Manual override remains manual after reload");
        var identity=AlignmentFrame.Build(new(),new(1,0,0),new(0,1,0));
        var measured=new NativeMotionResult{Length=.20,X=new(0,1,0),Y=new(0,0,1),Z=new(1,0,0),DeviceFrame=identity,Normalized=new[]{.5,.5,.5,.5,.5,.5}};
        var sample=new NativeMotionSample{HasShaftPose=true,HasTargetFrame=true,Origin=new(),LateralSpan=1,PlaneWidth=.20,PlaneDepth=.20};
        var preview=ContactPreviewMotion.Solve(sample,new[]{.5,.5,.5,.5,.5,.5},.13,measured,.12);
        Near(preview.RodLength,.13,"Preview uses player length independently of asset length");
        Near(preview.Sleeve.Origin.Y,.07,"Preview half L0 travel moves 60 mm, not half of player length");
        var expected=SR6DeviceRotation.Frame(.5,.5,.5,.5,.5,.5);
        check((preview.Sleeve.Y-expected.Y).Length<1e-10,"Preview rotation still reconstructs the exact six device commands");
    }
}
