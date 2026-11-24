using System;

namespace KoiSync.Core
{
    // Manual endpoints use the male reference frame, in axial/L1/L2 order.
    // The selected game entrance is an independent world-space observation.
    public static class ManualCalibrationMotion
    {
        public const double MinimumLength=.001;
        public static double Length(MotionVector origin,MotionVector end)
        {
            double length=(end-origin).Length;
            if(!Numbers.Finite(length)||length<MinimumLength-1e-9||length>.5+1e-9)
                throw new ArgumentException("Manual endpoint distance must be 1-500 mm");
            return Numbers.Clamp(length,MinimumLength,.5);
        }
        public static MotionVector MoveOriginAlongAxis(MotionVector origin,MotionVector end,double metresTowardEnd)
        {
            if(!Numbers.Finite(metresTowardEnd))throw new ArgumentException("Manual origin movement must be finite");
            var axis=end-origin;double length=Length(origin,end);
            double nextLength=Numbers.Clamp(length-metresTowardEnd,MinimumLength,.5);
            // The configured end remains authoritative. Reconstructing the
            // origin from it prevents cumulative lateral drift while dragging.
            return end-axis.Unit()*nextLength;
        }
        public static MotionVector WorldEnd(NativeMotionSample sample,MotionVector originOffset,MotionVector endOffset)
        {
            var d=endOffset-originOffset;
            return sample.Origin+sample.ReferenceFrame.Y*d.X+sample.ReferenceFrame.Z*d.Y+sample.ReferenceFrame.X*d.Z;
        }
        public static MotionVector LocalEnd(AlignmentFrame reference,MotionVector worldEnd)
        {
            var d=worldEnd-reference.Origin;
            return new MotionVector(MotionVector.Dot(d,reference.Y),MotionVector.Dot(d,reference.Z),MotionVector.Dot(d,reference.X));
        }
        public static NativeMotionResult Solve(NativeMotionSample sample,MotionVector end)
        {return Solve(sample,end,new MotionVector());}
        public static NativeMotionResult Solve(NativeMotionSample sample,MotionVector end,double r2PlaneDegrees)
        {return Solve(sample,end,new MotionVector(0,0,r2PlaneDegrees));}
        public static NativeMotionResult Solve(NativeMotionSample sample,MotionVector end,MotionVector planeDegrees)
        {
            if(sample==null||!sample.HasShaftPose||!sample.HasTargetFrame)throw new ArgumentException("Missing manual binding");
            ValidatePlane(planeDegrees);
            double length=Length(sample.Origin,end);
            MotionVector x,y,z;StudioShaftMotion.DeviceBasis(sample.ReferenceFrame,end-sample.Origin,out x,out y,out z);
            var device=new AlignmentFrame{Origin=sample.Origin,X=z,Y=x,Z=y};
            var delta=sample.TargetReferenceFrame.Origin-sample.Origin;
            double depth=length-MotionVector.Dot(delta,x),primary=Numbers.Clamp(depth/length,0,1);
            // y = UserFwd (TCode L1 > 0.5 = Forward), z = UserRight (TCode L2 > 0.5 = UserLeft = -z).
            double l1=MotionVector.Dot(delta,y),l2=-MotionVector.Dot(delta,z);
            double width=sample.PlaneWidth>0?sample.PlaneWidth:length,span=sample.PlaneDepth>0?sample.PlaneDepth:length;
            double n1=.5+l1/(width*sample.LateralSpan),n2=.5+l2/(span*sample.LateralSpan);
            var target=Plane(sample.TargetReferenceFrame,planeDegrees);
            var twistFrame=StudioShaftMotion.TwistFrame(target,sample.ShaftTwistHalfTurn);
            // Keep the three angles from the same rigid frame. A 180-degree
            // yaw fold alone changes that frame, particularly in reclined poses.
            var body=StudioShaftMotion.BodyAngles(device,twistFrame);
            double yaw=body[0];
            var rotations=SR6DeviceRotation.Positions(yaw,body[1],body[2],1-primary,n1,n2);
            var normalized=new[]{primary,n1,n2,rotations[0],rotations[1],rotations[2]};
            foreach(double value in normalized)if(!Numbers.Finite(value))throw new ArgumentException("Invalid manual pose");
            // A manual binding never falls back to BP chain length, a learned
            // stroke, or a transient contact gate while the user edits endpoints.
            return new NativeMotionResult{X=x,Y=y,Z=z,DeviceFrame=device,Length=length,
                VisibleLength=sample.ShaftPath==null?length:StudioShaftMotion.PathLength(sample.ShaftPath),
                Engagement=1,GeometricEngagement=1,ContactDistance=(delta-x*Numbers.Clamp(length-depth,0,length)).Length,
                MaterialInsertion=primary,TargetDepth=depth,Raw=new[]{primary*length,l1,l2,yaw,body[1],body[2]},Normalized=normalized};
        }
        public static ContactPreviewPose Preview(NativeMotionSample sample,NativeMotionResult measured)
        {return Preview(sample,measured,new MotionVector());}
        public static ContactPreviewPose Preview(NativeMotionSample sample,NativeMotionResult measured,double r2PlaneDegrees)
        {return Preview(sample,measured,new MotionVector(0,0,r2PlaneDegrees));}
        public static ContactPreviewPose Preview(NativeMotionSample sample,NativeMotionResult measured,MotionVector planeDegrees)
        {
            ValidatePlane(planeDegrees);
            var pose=new ContactPreviewPose{Sleeve=Plane(sample.TargetReferenceFrame,planeDegrees),RodRoot=sample.Origin,
                RodDirection=measured.X,RodLength=measured.Length,VisibleRodLength=measured.VisibleLength,
                Engagement=measured.Engagement,ContactDistance=measured.ContactDistance};
            Array.Copy(measured.Normalized,pose.Axes,6);
            return pose;
        }
        private static AlignmentFrame Plane(AlignmentFrame frame,MotionVector degrees)
        {
            return Math.Abs(degrees.X)+Math.Abs(degrees.Y)+Math.Abs(degrees.Z)<1e-12?frame:
                ContactPreviewMotion.BodyFrame(frame,frame.Origin,degrees.X,degrees.Y,degrees.Z);
        }
        private static void ValidatePlane(MotionVector degrees)
        {
            if(!Numbers.Finite(degrees.X)||!Numbers.Finite(degrees.Y)||!Numbers.Finite(degrees.Z)||
               degrees.X< -180||degrees.X>180||degrees.Y< -90||degrees.Y>90||degrees.Z< -90||degrees.Z>90)
                throw new ArgumentException("Manual plane angles must be R0 -180..180 and R1/R2 -90..90 degrees");
        }
    }
}
