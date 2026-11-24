using System;
using System.Linq;
using KoiSync.Core;
using KoiSync.Wire;

internal static class ShaftInvariantChecks
{
    public static void Run(Action<bool,string> check)
    {
        void Near(double a,double b,string label,double epsilon=1e-7)=>check(Math.Abs(a-b)<epsilon,label+" actual="+a+" expected="+b);
        var origin=new MotionVector();var up=new MotionVector(0,1,0);
        var identity=AlignmentFrame.Build(origin,new MotionVector(1,0,0),up);
        MotionVector[] Chain(double length)=>Enumerable.Range(0,9).Select(i=>up*(length*i/8)).ToArray();
        NativeMotionSample Sample(MotionVector[] path,MotionVector entry)=>new NativeMotionSample{
            HasShaftPose=true,HasTargetFrame=true,HasBodyFrame=true,Studio=true,RootDistance=true,
            Origin=path[0],End=path[path.Length-1],Target=entry,ShaftPath=path,ShaftRestLength=.18,
            SourceRootDirection=path[1]-path[0],ReferenceFrame=identity,BodyFrame=identity,
            TargetReferenceFrame=AlignmentFrame.Build(entry,identity.X,up)};
        var far=Sample(Chain(.18),new MotionVector(0,-1,0));
        var result=NativeMotion.Solve(far);
        Near(result.Engagement,0,"An entrance one metre behind the root is detached");
        Near(result.Normalized[0],0,"A distant same-axis entrance does not command full depth");
        check(result.ContactDistance>=1,"Contact distance measures finite geometry");

        var grazing=Sample(new[]{origin,new MotionVector(0,.09,.004),new MotionVector(0,.18,0)},new MotionVector(0,.09,0));
        grazing.TargetReferenceFrame=AlignmentFrame.Build(grazing.Target,identity.X,new MotionVector(0,0,1));
        result=NativeMotion.Solve(grazing);
        Near(result.Engagement,1,"A curved segment near the opening engages without a plane crossing");
        check(result.ContactDistance<.0041,"Grazing contact uses finite segment distance");
        Near(result.Normalized[0],.5,"Grazing contact keeps the physical entry projection");

        var sideways=Sample(new[]{origin,new MotionVector(0,0,.18)},new MotionVector(0,0,.4));
        sideways.ReferenceFrame=AlignmentFrame.Build(origin,identity.X,new MotionVector(0,0,1));
        sideways.TargetReferenceFrame=sideways.ReferenceFrame;sideways.TargetReferenceFrame.Origin=sideways.Target;
        var neutral=ContactPreviewMotion.Solve(sideways,null);
        Near(MotionVector.Angle(neutral.RodDirection,neutral.Sleeve.Y),0,"Neutral channel follows a rotated mount axis within reference-model servo precision",.005);
        Near((neutral.Sleeve.Origin-(neutral.RodRoot+neutral.RodDirection*.18)).Length,0,"Detached sleeve parks at rotated shaft tip");

        var aim=Sample(new[]{origin,new MotionVector(.03,.09,0).Unit()*.18},new MotionVector(.03,.09,0));
        result=NativeMotion.Solve(aim);
        Near(result.Raw[2],-.03,"Reaiming the BP chain cannot absorb a 30 mm lateral move (UserLeft = -X)");
        Near(MotionVector.Angle(result.X,up),0,"Device mounting direction is independent of BP feedback");
        Near((ContactPreviewMotion.Solve(aim,null).Sleeve.Origin-aim.Target).Length,0,"Lateral target reconstructs from the output axes");

        var curve=Sample(new[]{origin,new MotionVector(0,.06,0),new MotionVector(.04,.09,0),new MotionVector(.04,.16,0)},new MotionVector(.04,.09,0));
        Near((ContactPreviewMotion.Solve(curve,null).Sleeve.Origin-curve.Target).Length,0,"Bent chain does not move the rigid preview off the entry");
        Near(NativeMotion.Solve(curve).MaterialInsertion,.07/.18,"Material fraction remains separately observable");
        var full=Sample(Chain(.18),new MotionVector(0,.09,0));var squished=Sample(Chain(.135),full.Target);
        Near(NativeMotion.Solve(full).Normalized[0],.5,"Fixed entry starts at half physical depth");
        Near(NativeMotion.Solve(squished).Normalized[0],.5,"BP shortening alone does not change physical depth");
        Near((ContactPreviewMotion.Solve(full,null).Sleeve.Origin-ContactPreviewMotion.Solve(squished,null).Sleeve.Origin).Length,0,"BP shortening alone does not move the cup");

        StudioShaftMotion.DeviceBasis(identity,new MotionVector(0,1e-6,1),out var x1,out var y1,out var z1);
        StudioShaftMotion.DeviceBasis(identity,new MotionVector(0,-1e-6,1),out var x2,out var y2,out var z2);
        check(MotionVector.Angle(y1,y2)<.001&&MotionVector.Angle(z1,z2)<.001,"Crossing the reference equator does not flip lateral axes");
        var rotation=ContactPreviewMotion.BodyFrame(identity,origin,80,12,-20);
        var halfway=FrameRotation.Blend(identity,rotation,.5);
        var twice=FrameRotation.Blend(identity,rotation,1);
        Near(MotionVector.Dot(halfway.X,halfway.Y),0,"Quaternion blend keeps an orthogonal frame");
        Near(halfway.Y.Length,1,"Quaternion blend keeps unit vectors");
        Near((twice.Y-rotation.Y).Length,0,"Quaternion blend reaches the target");
        var continuity=new ContinuousAngle();continuity.Resolve(170);
        full.ShaftYaw=continuity;full.TargetReferenceFrame=ContactPreviewMotion.BodyFrame(identity,full.Target,-170,0,0);
        NativeMotion.Solve(full);ContactPreviewMotion.Solve(full,null);
        Near(continuity.Peek(0),0,"Diagnostic solves do not advance the yaw continuity state");

        // Regression: the attached scene oscillates laterally by 2-44 mm while
        // remaining inside the entry plane. It must not withdraw on a timer.
        var bound=Sample(Chain(.18),new MotionVector(.044,.09,0));bound.HasExplicitContact=true;
        Near(NativeMotion.Solve(bound).Engagement,1,"Explicit bound contact tolerates the observed 44 mm chain offset");
        bound.HasExplicitContact=false;
        Near(NativeMotion.Solve(bound).Engagement,0,"An unbound nearby pair cannot inherit attachment tolerance");
        bound.HasExplicitContact=true;bound.TargetReferenceFrame.Origin=new MotionVector(.10,.09,0);
        Near(NativeMotion.Solve(bound).Normalized[0],0,"Explicit attachment does not bridge a large finite gap");
        bound.TargetReferenceFrame.Origin=new MotionVector(0,.20,0);
        Near(NativeMotion.Solve(bound).Normalized[0],0,"Explicit attachment releases beyond the visible tip");
        bound.TargetReferenceFrame.Origin=new MotionVector(0,-1,0);
        Near(NativeMotion.Solve(bound).Engagement,0,"Explicit attachment cannot engage behind a distant root");
        double previousBound=1;
        for(int i=0;i<=80;i++)
        {
            double weight=StudioShaftMotion.BoundContactEngagement(.04+i*.0005,.18);
            check(weight<=previousBound+1e-9&&previousBound-weight<.03,"Bound release is continuous and monotonic at step "+i);
            previousBound=weight;
        }

        foreach(int fps in new[]{30,60})
        {
            var pipeline=new MotionPipeline{Minimum=0,Maximum=1};long seq=0;double time=0;
            NativeMotionResult Push(NativeMotionSample value,string id="same")
            {
                pipeline.Update(new Snapshot{Session="hold",Scene="test",Active=true,Sequence=++seq,SampleTime=time},new Candidate{Id=id,Native=value,Active=true},0,StrokeAxis.Z);
                time+=1.0/fps;return NativeMotion.Solve(value);
            }
            NativeMotionSample Offset(double x)=>Sample(Chain(.18).Select(v=>v+new MotionVector(x,0,0)).ToArray(),new MotionVector(0,.09,0));
            Push(Offset(0));
            var dropout=Offset(.04);
            Near(Push(dropout).Engagement,1,"Confirmed contact bridges a bounded dropout at "+fps+" fps");
            for(int i=0;i<10;i++)NativeMotion.Solve(dropout);
            Near(NativeMotion.Solve(dropout).Engagement,1,"Diagnostic repetitions cannot consume contact grace at "+fps+" fps");
            for(int i=1;i<fps*2;i++)Near(Push(Offset(.04)).Engagement,1,"Grace covers the full two seconds at "+fps+" fps");
            Near(Push(Offset(.04)).Engagement,0,"A persistent missing contact expires at "+fps+" fps");
            Push(Offset(0));Near(Push(Offset(.1)).Engagement,0,"A large finite gap releases immediately at "+fps+" fps");
            Push(Offset(0));var outside=Sample(Chain(.08),new MotionVector(0,.09,0));
            Near(Push(outside).Normalized[0],0,"Leaving the entry plane withdraws without grace at "+fps+" fps");
            Near(Push(Offset(.04)).Engagement,0,"Withdrawal clears the previous contact latch at "+fps+" fps");
            Push(Offset(0));Near(Push(Offset(.04),"other").Engagement,0,"Switching sources cannot inherit a contact latch at "+fps+" fps");
        }
    }
}
