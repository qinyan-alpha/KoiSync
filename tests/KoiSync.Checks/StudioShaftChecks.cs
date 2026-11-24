using System;
using System.Numerics;
using KoiSync.Core;
using KoiSync.Wire;

internal static class StudioShaftChecks
{
    public static void Run(Action<bool,string> check)
    {
        void Near(double a,double b,string label,double tolerance=1e-5){check(Math.Abs(a-b)<tolerance,label+" actual="+a+" expected="+b);}
        var identity=AlignmentFrame.Build(new MotionVector(),new MotionVector(1,0,0),new MotionVector(0,1,0));
        AlignmentFrame Pose(double yaw,double roll,double pitch)
        {
            var q=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,(float)(roll*Math.PI/180))*Quaternion.CreateFromAxisAngle(Vector3.UnitX,(float)(pitch*Math.PI/180))*Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)(yaw*Math.PI/180));
            MotionVector V(Vector3 v)=>new MotionVector(v.X,v.Y,v.Z);
            return new AlignmentFrame{X=V(Vector3.Transform(Vector3.UnitX,q)),Y=V(Vector3.Transform(Vector3.UnitY,q)),Z=V(Vector3.Transform(Vector3.UnitZ,q))};
        }
        AlignmentFrame At(AlignmentFrame f,MotionVector origin){f.Origin=origin;return f;}
        var target=new MotionVector(0,.5,0);
        var s=new NativeMotionSample{Studio=true,HasShaftPose=true,RootDistance=true,HasBodyFrame=true,HasTargetFrame=true,Origin=new MotionVector(),End=new MotionVector(0,1,0),Target=target,ReferenceFrame=identity,BodyFrame=identity,TargetReferenceFrame=At(Pose(45,10,-5),target),PlaneWidth=1,PlaneDepth=1,LateralSpan=6,SourceRootDirection=new MotionVector(0,1,0),SourceDeformedDirection=new MotionVector(0,1,0),ShaftPath=new[]{new MotionVector(),new MotionVector(0,1,0)},ShaftRestLength=1};
        var a=NativeMotion.Solve(s);
        Near(a.Raw[3],45,"Static side-facing first pose retains 45-degree twist");
        Near(a.Raw[4],10,"Static waist lean retains 10 degrees");
        Near(a.Raw[5],-5,"Static pitch retained without first-frame subtraction");
        foreach(double delta in new[]{-15.0,0,15,95})
        {
            s.TargetReferenceFrame=At(Pose(180+delta,0,67),target);s.ShaftTwistHalfTurn=true;
            var corrected=NativeMotion.Solve(s);
            Near(corrected.Raw[3],delta,"Fixed half-turn basis preserves real twist "+delta,1e-4);
            Near(corrected.Normalized[3],.5-delta/240,"Untwisted opposing axes centre R0 "+delta,1e-4);
            Near(corrected.Raw[5],67,"Twist basis leaves pitch unchanged",1e-4);
        }
        s.ShaftTwistHalfTurn=false;s.TargetReferenceFrame=At(Pose(45,10,-5),target);
        check(a.Normalized[3]<.5&&a.Normalized[5]<.5,"SR6 reference twist and pitch signs");
        var anchored=ContactPreviewMotion.Solve(s,a.Normalized);
        Near((anchored.Sleeve.Origin-s.TargetReferenceFrame.Origin).Length,0,"Matching SR6 values keep the sleeve on its rough intercourse anchor");
        var anchoredAngles=StudioShaftMotion.BodyAngles(s.ReferenceFrame,anchored.Sleeve);
        Near(anchoredAngles[0],45,"Preview reconstructs R0 around the male POV frame");
        Near(anchoredAngles[1],10,"Preview reconstructs travel-dependent R1");
        Near(anchoredAngles[2],-5,"Preview reconstructs R2");
        Near(anchored.RodLength,1,"Preview rod keeps the calibrated physical length");
        var driven=(double[])a.Normalized.Clone();driven[0]+=.1;
        var movedL0=ContactPreviewMotion.Solve(s,driven);
        Near(MotionVector.Dot(movedL0.Sleeve.Origin-anchored.Sleeve.Origin,a.X),-.1,"Higher scripted L0 moves the sleeve toward the fixed rod root");
        driven=(double[])a.Normalized.Clone();driven[1]+=.1;
        var movedL1=ContactPreviewMotion.Solve(s,driven);
        Near(MotionVector.Dot(movedL1.Sleeve.Origin-anchored.Sleeve.Origin,a.Y),.6,"Scripted L1 uses the configured lateral span");
        driven=(double[])a.Normalized.Clone();driven[2]+=.1;
        var movedL2=ContactPreviewMotion.Solve(s,driven);
        Near(MotionVector.Dot(movedL2.Sleeve.Origin-anchored.Sleeve.Origin,a.Z),-.6,"Scripted L2 moves toward UserLeft (-Z) using the configured depth span");
        driven=(double[])a.Normalized.Clone();driven[0]=.72;driven[3]=.2;driven[4]=.8;driven[5]=.7;
        var rotated=ContactPreviewMotion.Solve(s,driven);var rotatedAngles=StudioShaftMotion.BodyAngles(s.ReferenceFrame,rotated.Sleeve);
        // Independent values captured from the original browser SR6 model.
        Near(rotatedAngles[0],72.49496540157428,"Scripted R0 includes the carrier's coupled rotation");
        Near(rotatedAngles[1],-9.226074553682578,"Scripted R1 matches the SR6 carrier at current travel");
        Near(rotatedAngles[2],9.960666786333427,"Scripted R2 matches the reference including coupling");
        s.TargetReferenceFrame=At(identity,s.Target);
        s.SourceDeformedDirection=new MotionVector(Math.Sqrt(.5),Math.Sqrt(.5),0);
        Near(NativeMotion.Solve(s).Raw[4],0,"Visible shaft bend does not change sleeve roll");
        s.SourceDeformedDirection=new MotionVector(-Math.Sqrt(.5),Math.Sqrt(.5),0);
        Near(NativeMotion.Solve(s).Raw[4],0,"Opposite shaft bend still leaves sleeve roll unchanged");
        s.SourceDeformedDirection=new MotionVector(0,-1,.2);
        Near(NativeMotion.Solve(s).Raw[4],0,"Backward internal fold does not invent 180-degree lateral swing");

        // The physical shaft is the absolute male reference.  With no script a
        // clearly detached target returns every secondary channel to neutral and
        // parks the sleeve entrance at the shaft tip.  First contact is L0=0;
        // later insertion moves the entrance back by its physical axial depth.
        s.SourceDeformedDirection=s.SourceRootDirection=new MotionVector(0,1,0);s.ShaftRestLength=1;
        s.Origin=new MotionVector();s.End=new MotionVector(0,1,0);s.ShaftPath=new[]{s.Origin,s.End};
        s.Target=new MotionVector(0,1.3,0);s.TargetReferenceFrame=At(Pose(55,20,-12),s.Target);
        var detached=NativeMotion.Solve(s);Near(detached.Engagement,0,"A target clearly beyond the visible tip is detached");
        for(int i=0;i<6;i++)Near(detached.Normalized[i],i==0?0:.5,"Detached native pose returns to the physical neutral channel "+i);
        var parked=ContactPreviewMotion.Solve(s,null);Near((parked.Sleeve.Origin-new MotionVector(0,1,0)).Length,0,"Detached sleeve entrance parks at the physical shaft tip");
        Near(parked.ContactDistance,.3,"Detached diagnostic reports visible tip-to-entry distance");

        s.Target=new MotionVector(0,1,0);s.TargetReferenceFrame=At(identity,s.Target);
        var touching=NativeMotion.Solve(s);Near(touching.Engagement,1,"Exact visible-tip contact engages the target frame");Near(touching.Normalized[0],0,"First contact starts at zero insertion");
        Near((ContactPreviewMotion.Solve(s,null).Sleeve.Origin-s.Target).Length,0,"At first contact the shaft tip and sleeve entrance coincide");

        s.Target=new MotionVector(.02,.6,.03);s.TargetReferenceFrame=At(identity,s.Target);
        var inserted=NativeMotion.Solve(s);Near(inserted.Engagement,1,"A nearby entrance intersecting the shaft remains fully engaged");Near(inserted.Normalized[0],.4,"L0 is physical axial depth divided by the fixed length");
        Near(inserted.Raw[1],.03,"L1 is entrance offset from the physical tip in POV-forward");Near(inserted.Raw[2],-.02,"L2 is entrance offset from the physical tip in POV-left (-right)");
        // Explicit physical SR6 TCode v0.3 sign regression checks (identity = X:UserRight, Y:ShaftUp, Z:UserFwd):
        // R1 > 0.5 tilts cup top UserRight (+X), R2 > 0.5 tilts cup top UserFwd (+Z), R0 < 0.5 twists CW from top.
        s.Target=new MotionVector(0,.5,0);
        s.TargetReferenceFrame=At(Pose(0,-10,0),s.Target); // cup top tilted +10 deg toward +X (UserRight)
        check(NativeMotion.Solve(s).Normalized[4]>.5,"Tilting canal toward UserRight (+X) raises R1 above 0.5");
        s.TargetReferenceFrame=At(Pose(0,0,15),s.Target); // cup top tilted +15 deg toward +Z (UserFwd)
        check(NativeMotion.Solve(s).Normalized[5]>.5,"Tilting canal toward UserFwd (+Z) raises R2 above 0.5");
        s.TargetReferenceFrame=At(Pose(0,0,-15),s.Target); // cup top tilted -15 deg toward -Z (UserBack)
        check(NativeMotion.Solve(s).Normalized[5]<.5,"Tilting canal toward UserBack (-Z) lowers R2 below 0.5");
        s.Target=new MotionVector(.02,.6,.03);s.TargetReferenceFrame=At(identity,s.Target);
        var insertedPose=ContactPreviewMotion.Solve(s,null);Near((insertedPose.Sleeve.Origin-s.Target).Length,0,"Native L0/L1/L2 reconstruct the target entrance from the shaft tip");
        var shortPose=ContactPreviewMotion.Solve(s,null,.5);Near(shortPose.RodLength,.5,"Preview may deliberately follow an alternate visible shaft length");
        Near(MotionVector.Dot(shortPose.Sleeve.Origin-shortPose.RodRoot,shortPose.RodDirection),.3,"Alternate rod length still uses L0 fraction times the selected length");

        s.Target=new MotionVector(.5,.6,0);s.TargetReferenceFrame=At(Pose(60,20,10),s.Target);
        var falsePlane=NativeMotion.Solve(s);Near(falsePlane.Engagement,0,"A distant lateral target cannot create insertion through the infinite entrance plane");
        for(int i=0;i<6;i++)Near(falsePlane.Normalized[i],i==0?0:.5,"False plane crossing returns to neutral channel "+i);

        s.Target=new MotionVector();s.TargetReferenceFrame=At(identity,s.Target);s.PlaneWidth=s.PlaneDepth=0;
        s.ShaftPath=new[]{new MotionVector(0,-.2,0),new MotionVector(),new MotionVector(.18,.24,0),new MotionVector(.18,.54,0)};s.ShaftRestLength=.8;
        s.Origin=s.ShaftPath[0];s.End=s.ShaftPath[s.ShaftPath.Length-1];s.SourceRootDirection=s.ShaftPath[1]-s.ShaftPath[0];
        a=NativeMotion.Solve(s);Near(a.Normalized[0],.75,"Bent centerline retains the physical entry projection");Near(a.Raw[0],.6,"L0 raw value uses fixed physical rest length");Near(a.Length,.8,"Reported shaft length remains physical rest length");Near(s.Width,.8,"Automatic lateral scale uses fixed physical length");
        s.ShaftPath=new[]{new MotionVector(0,-.1,0),new MotionVector(),new MotionVector(.09,.12,0),new MotionVector(.09,.27,0)};
        s.Origin=s.ShaftPath[0];s.End=s.ShaftPath[s.ShaftPath.Length-1];s.SourceRootDirection=s.ShaftPath[1]-s.ShaftPath[0];
        a=NativeMotion.Solve(s);Near(a.MaterialInsertion,.75,"Uniform shape scaling preserves the material diagnostic");Near(a.Normalized[0],.875,"Moving the root toward a fixed entrance increases physical depth");Near(a.Raw[0],.7,"Depth uses entrance projection in fixed physical millimetres");Near(s.Width,.8,"DanLengthSquish cannot change L1/L2 normalization scale");
        s.ShaftPath=new[]{new MotionVector(0,.4,0),new MotionVector(0,.7,0)};s.Origin=s.ShaftPath[0];s.End=s.ShaftPath[1];
        Near(NativeMotion.Solve(s).Normalized[0],0,"A distant chain in the positive halfspace is not contact");
        s.ShaftPath=new[]{new MotionVector(0,-.4,0),new MotionVector(0,-.1,0)};s.Origin=s.ShaftPath[0];s.End=s.ShaftPath[1];s.SourceRootDirection=s.End-s.Origin;
        Near(NativeMotion.Solve(s).Normalized[0],0,"Chain wholly outside entry plane is withdrawn");

        s.SourceDeformedDirection=new MotionVector(.2,.8,.3);s.Target=new MotionVector(.02,.4,-.03);s.TargetReferenceFrame=At(Pose(45,10,-5),s.Target);
        s.ShaftPath=new[]{new MotionVector(0,-.2,0),new MotionVector(),new MotionVector(.18,.24,0),new MotionVector(.18,.54,0)};s.Origin=s.ShaftPath[0];s.End=s.ShaftPath[s.ShaftPath.Length-1];s.SourceRootDirection=s.ShaftPath[1]-s.ShaftPath[0];
        a=NativeMotion.Solve(s);
        var qMove=Quaternion.CreateFromYawPitchRoll(.7f,-.4f,.2f);var shift=new Vector3(4,-2,3);
        MotionVector Move(MotionVector v,bool point){var r=Vector3.Transform(new Vector3((float)v.X,(float)v.Y,(float)v.Z),qMove)+(point?shift:Vector3.Zero);return new MotionVector(r.X,r.Y,r.Z);}
        AlignmentFrame MoveFrame(AlignmentFrame f)=>new AlignmentFrame{Origin=Move(f.Origin,true),X=Move(f.X,false),Y=Move(f.Y,false),Z=Move(f.Z,false)};
        var movedPath=Array.ConvertAll(s.ShaftPath,v=>Move(v,true));
        var m=new NativeMotionSample{Studio=true,HasShaftPose=true,Origin=Move(s.Origin,true),End=Move(s.End,true),Target=Move(s.Target,true),ReferenceFrame=MoveFrame(s.ReferenceFrame),TargetReferenceFrame=MoveFrame(s.TargetReferenceFrame),SourceRootDirection=Move(s.SourceRootDirection,false),SourceDeformedDirection=Move(s.SourceDeformedDirection,false),ShaftPath=movedPath,ShaftRestLength=s.ShaftRestLength,PlaneWidth=1,PlaneDepth=1,LateralSpan=6};
        var moved=NativeMotion.Solve(m);for(int i=0;i<6;i++)Near(moved.Raw[i],a.Raw[i],"Shaft pose invariant under common scene rotation/translation "+i,3e-5);
        s.Target=new MotionVector(.7,0,-.03);s.TargetReferenceFrame=At(identity,s.Target);double before=NativeMotion.Solve(s).Raw[0];
        s.Target=new MotionVector(-.4,0,.8);s.TargetReferenceFrame=At(identity,s.Target);Near(NativeMotion.Solve(s).Raw[0],before,"Translation within the entry plane cannot inflate L0");
        s.TargetReferenceFrame=At(Pose(20,0,90),s.Target);check(Array.TrueForAll(NativeMotion.Solve(s).Raw,double.IsFinite),"Near-vertical body pose remains finite");
        for(int l=0;l<=20;l++)foreach(double r in new[]{.15,.35,.5,.7,.9})Near(SR6RollMapping.Position(SR6RollMapping.Angle(r,l/20.0),l/20.0),r,"SR6 roll inverse preserves travel-dependent model angle",1e-6);
        var p=new MotionPipeline{Minimum=0,Maximum=1};p.AxisGain[1]=p.AxisGain[2]=p.AxisGain[3]=2.5;
        s.Target=new MotionVector(0,.5,0);s.TargetReferenceFrame=At(identity,s.Target);s.SourceDeformedDirection=new MotionVector(0,1,0);s.ShaftPath=new[]{new MotionVector(),new MotionVector(0,1,0)};s.ShaftRestLength=1;s.Origin=s.ShaftPath[0];s.End=s.ShaftPath[1];s.SourceRootDirection=s.End-s.Origin;
        p.Update(new Snapshot{Active=false,Sequence=1,SampleTime=.02,State="paused",Scene="shaft-test"},new Candidate{Id="shaft",Native=s,Active=false},0,StrokeAxis.Z);
        foreach(int i in new[]{1,2,3})Near(p.Desired[i],.5,"Native shaft gain amplifies around neutral, axis "+i);
        foreach(double t in new[]{0,1,2,4,5})Near(ScriptVibration.At(.05,t,2,4,.04,8,.1),.05,"Original primary preserved outside optional vibration "+t);
        double low=1,high=0;int changes=0;double previous=.05;
        for(int i=0;i<=240;i++){double value=ScriptVibration.At(.05,i/60.0,2,4,.04,8,.1);check(value>=0&&value<=1,"Optional vibration stays in script range");if(i<120)Near(value,.05,"Optional vibration leaves first half unchanged");if(i>126&&i<234){low=Math.Min(low,value);high=Math.Max(high,value);if(Math.Abs(value-previous)>.01)changes++;}previous=value;}
        check(high-low>.07&&changes>40,"Explicit climax alternative carries short 8Hz motion");
        ShaftInvariantChecks.Run(check);
    }
}
