using KoiSync.Core;
using KoiSync.Studio;

internal static class RotationReferenceChecks
{
    public static void Run(Action<bool,string> check)
    {
        var identity=AlignmentFrame.Build(new(),new(1,0,0),new(0,1,0));
        AlignmentFrame Frame(double yaw,double roll,double pitch)=>ContactPreviewMotion.BodyFrame(identity,new(),yaw,roll,pitch);
        NativeMotionResult Pose(AlignmentFrame frame,double engagement=1,double l0=.5,double l1=.5,double l2=.5)
        {
            var a=StudioShaftMotion.BodyAngles(identity,frame);
            var r=engagement<=0?new[]{.5,.5,.5}:SR6DeviceRotation.Positions(a[0],a[1],a[2],l0,l1,l2);
            return new NativeMotionResult{Raw=new[]{0.0,0,0,a[0],a[1],a[2]},Normalized=new[]{1-l0,l1,l2,r[0],r[1],r[2]},Engagement=engagement};
        }
        AlignmentFrame Output(ChapterRotationReference reference,NativeMotionResult pose)
        {
            var drive=new StudioDrive();drive.Update(reference.Apply(pose),.02,true);
            return SR6DeviceRotation.Frame(drive.Values[0],drive.Values[1],drive.Values[2],drive.Values[3],drive.Values[4],drive.Values[5]);
        }
        double Error(AlignmentFrame a,AlignmentFrame b)=>Math.Max((a.X-b.X).Length,Math.Max((a.Y-b.Y).Length,(a.Z-b.Z).Length));
        foreach(var angles in new[]{new[]{0.0,0,-37},new[]{150.0,40,-58},new[]{-155.0,-35,54}})
        {
            var baseline=Frame(angles[0],angles[1],angles[2]);var reference=ChapterRotationReference.Capture(Pose(baseline),17.074);
            foreach(var linear in new[]{new[]{.5,.5,.5},new[]{.2,.4,.6},new[]{.8,.7,.3}})
            {
                check(Error(Output(reference,Pose(baseline,1,linear[0],linear[1],linear[2])),identity)<2e-6,"Fixed reference maps compound orientation to neutral at changed linear position");
                foreach(var delta in new[]{new[]{45.0,0,0},new[]{0.0,4,0},new[]{0.0,0,18}})
                {
                    var relative=Frame(delta[0],delta[1],delta[2]);
                    var desired=new AlignmentFrame{X=baseline.World(relative.X),Y=baseline.World(relative.Y),Z=baseline.World(relative.Z)};
                    var pose=Pose(desired,1,linear[0],linear[1],linear[2]);
                    check(Error(Output(reference,pose),relative)<2e-6,"Quaternion reference preserves local rotation amplitude and direction");
                    var transformed=reference.Apply(pose);
                    check(transformed.Take(3).SequenceEqual(pose.Normalized.Take(3)),"Rotation reference never alters L0/L1/L2");
                }
            }
            foreach(double weight in new[]{0.0,.001,.1,.5,.99,1})
            {
                var faded=Pose(FrameRotation.Blend(identity,baseline,weight),weight);
                check(Error(Output(reference,faded),identity)<.0002,"Contact fade does not create opposite baseline tilt");
            }
            check(!ChapterRotationReference.CanCapture(Pose(baseline,.5)),"Partial contact cannot establish a chapter reference");
        }
        var wrap=ChapterRotationReference.Capture(Pose(Frame(179,0,0)),2);
        check(Error(Output(wrap,Pose(Frame(-179,0,0))),Frame(2,0,0))<2e-6,"Relative twist crosses signed 180 degrees without jumping");

        var drive2=new StudioDrive{SoftTwistLimit=false,SoftRollLimit=false,SoftPitchLimit=false};
        foreach(int axis in new[]{3,4,5})
        {
            double Map(double v)
            {
                var axes=new[]{.5,.5,.5,.5,.5,.5};axes[axis]=v;drive2.Reset();drive2.Update(axes,.02,true);return drive2.Values[axis];
            }
            check(new[]{.1,.2,.5,.8,.9}.All(v=>Math.Abs(Map(v)-v)<1e-10),"Relative mode preserves central 80 percent of rotation travel");
            var outputs=new[]{-2.0,-.5,0,.1,.5,.9,1,1.5,3}.Select(Map).ToArray();
            check(outputs.All(v=>v>0&&v<1)&&outputs.Zip(outputs.Skip(1),(a,b)=>b>a).All(v=>v),"Relative mode always retains a monotonic soft fallback");
            drive2.Minimum[axis]=.2;drive2.Maximum[axis]=.8;
            check(Map(-2)==.2&&Map(3)==.8,"Relative mode respects user travel limits");
            drive2.Minimum[axis]=0;drive2.Maximum[axis]=1;
        }
        var script=new[]{.2,.3,.4,0.0,1,0};drive2.UpdateScript(script);
        check(!drive2.RelativeRotation&&drive2.Values.SequenceEqual(script),"Recorded final values bypass reference mapping and soft fallback");

        var book=new ChapterBook();var first=new Chapter{Source=1,Target=2,Opening=0,RotationReference=wrap.Copy()};book.Observe(0,first);
        var split=book.Split(3);split.RotationReference.Yaw=175;
        check(first.RotationReference.Yaw==179,"Splitting copies the reference without sharing mutable state");
        var xml=book.ToXml();var loaded=new ChapterBook();loaded.FromXml(xml);
        var restored=loaded.At(1).RotationReference;
        check(restored.Yaw==wrap.Yaw&&restored.Time==wrap.Time,"Chapter reference round-trips through scene metadata");
        var target=Pose(Frame(-177,0,0));var before=restored.Apply(target);
        restored.Apply(Pose(Frame(0,50,-65)));
        check(before.SequenceEqual(restored.Apply(target))&&before.SequenceEqual(wrap.Apply(target)),"Seek history and reload cannot shift an established reference");
        bool rejected=false;
        try{loaded.FromXml(xml.Replace("yaw=\"179\"","yaw=\"NaN\""));}catch(ArgumentException){rejected=true;}
        check(rejected&&loaded.ToXml()==xml,"Invalid reference metadata rejects atomically");
        loaded.FromXml("<chapters><chapter time=\"0\" source=\"1\" target=\"2\" opening=\"0\" /></chapters>");
        check(loaded.At(0).RotationReference==null,"Older cards load without creating a reference");
        first.Manual=false;split.Manual=false;book.CompactAutomatic();
        check(book.Chapters.Count==2,"Distinct saved baselines keep an automatic chapter boundary");
        split.RotationReference=first.RotationReference.Copy();book.CompactAutomatic();
        check(book.Chapters.Count==1&&book.At(0).RotationReference!=null,"Equivalent automatic chapters preserve their reference when merged");
        book.CorrectAutomaticOpening(1,new Chapter{Source=1,Target=2,Opening=1});
        check(book.At(0).RotationReference==null,"Changing the contact opening clears the old reference");
        book.At(0).RotationReference=wrap.Copy();book.ReadAttachmentEvents(new());
        check(book.At(0).RotationReference!=null,"Re-reading BP keys preserves saved chapter references");
    }
}
