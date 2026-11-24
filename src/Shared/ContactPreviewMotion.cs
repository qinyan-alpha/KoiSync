using System;

namespace KoiSync.Core
{
    // Reconstructs the diagnostic sleeve from the same six normalized values
    // that are offered to an SR6. The game skeleton supplies only a rough
    // registration point; script/output deltas determine the visible pose.
    public sealed class ContactPreviewPose
    {
        public AlignmentFrame Sleeve;
        public MotionVector RodRoot,RodDirection;
        public double RodLength;
        public double VisibleRodLength,Engagement,ContactDistance;
        public readonly double[] Axes=new double[6];
    }

    public static class ContactPreviewMotion
    {
        public static ContactPreviewPose Solve(NativeMotionSample sample,double[] drivenAxes)
        {return Solve(sample,drivenAxes,double.NaN);}

        public static ContactPreviewPose Solve(NativeMotionSample sample,double[] drivenAxes,double modelLength)
        {
            if(sample==null)throw new ArgumentException("Missing intercourse binding");
            return Solve(sample,drivenAxes,modelLength,NativeMotion.Solve(sample));
        }

        public static ContactPreviewPose Solve(NativeMotionSample sample,double[] drivenAxes,double modelLength,NativeMotionResult measured)
        {return Solve(sample,drivenAxes,modelLength,measured,double.NaN);}

        public static ContactPreviewPose Solve(NativeMotionSample sample,double[] drivenAxes,double modelLength,NativeMotionResult measured,double strokeLength)
        {
            if(sample==null||!sample.HasShaftPose||!sample.HasTargetFrame)throw new ArgumentException("Missing intercourse binding");
            if(measured==null)throw new ArgumentException("Missing measured pose");
            if(!Numbers.Finite(measured.Length)||measured.Length<1e-7)throw new ArgumentException("Invalid shaft length");
            double length=Numbers.Finite(modelLength)&&modelLength>1e-7?modelLength:measured.Length;
            double width=sample.PlaneWidth>0?sample.PlaneWidth:length,depth=sample.PlaneDepth>0?sample.PlaneDepth:length;

            var pose=new ContactPreviewPose();
            for(int i=0;i<6;i++)
            {
                double value=drivenAxes!=null&&drivenAxes.Length>i&&Numbers.Finite(drivenAxes[i])?drivenAxes[i]:
                    i==1?.5+measured.Raw[1]/(width*sample.LateralSpan):
                    i==2?.5+measured.Raw[2]/(depth*sample.LateralSpan):measured.Normalized[i];
                pose.Axes[i]=Numbers.Clamp(value,0,1);
            }

            // The cyan shaft is the physical male reference.  All six driven
            // values reconstruct one absolute device pose from its root/tip:
            // L0 moves the entrance back from the tip, while L1/L2 place it in
            // the two lateral directions.  A neutral unscripted pose therefore
            // parks the sleeve entrance exactly at the physical tip.
            MotionVector root=sample.Origin,tip=root+measured.X*length;
            double l0=pose.Axes[0]*(Numbers.Finite(strokeLength)&&strokeLength>0?strokeLength:length);
            double l1=(pose.Axes[1]-.5)*width*sample.LateralSpan;
            double l2=(pose.Axes[2]-.5)*depth*sample.LateralSpan;
            // measured.Y = UserFwd (+L1), measured.Z = UserRight (-L2, since TCode L2 > 0.5 moves UserLeft).
            MotionVector origin=tip-measured.X*l0+measured.Y*l1-measured.Z*l2;

            var rotation=SR6DeviceRotation.Frame(1-pose.Axes[0],pose.Axes[1],pose.Axes[2],pose.Axes[3],pose.Axes[4],pose.Axes[5]);
            pose.Sleeve=new AlignmentFrame{Origin=origin,X=measured.DeviceFrame.World(rotation.X),Y=measured.DeviceFrame.World(rotation.Y),Z=measured.DeviceFrame.World(rotation.Z)};
            pose.RodRoot=root;
            pose.RodDirection=measured.X;
            pose.RodLength=length;
            pose.VisibleRodLength=measured.VisibleLength;
            pose.Engagement=measured.Engagement;
            pose.ContactDistance=measured.ContactDistance;
            return pose;
        }

        // Exact inverse of StudioShaftMotion.BodyAngles. Its decomposition is
        // Rz(roll) * Rx(pitch) * Ry(yaw), expressed in the male POV frame.
        public static AlignmentFrame BodyFrame(AlignmentFrame reference,MotionVector origin,double yaw,double roll,double pitch)
        {
            return new AlignmentFrame
            {
                Origin=origin,
                X=reference.World(Rotate(new MotionVector(1,0,0),yaw,roll,pitch)),
                Y=reference.World(Rotate(new MotionVector(0,1,0),yaw,roll,pitch)),
                Z=reference.World(Rotate(new MotionVector(0,0,1),yaw,roll,pitch))
            };
        }

        private static MotionVector Rotate(MotionVector point,double yawDegrees,double rollDegrees,double pitchDegrees)
        {
            double yaw=yawDegrees*Math.PI/180,roll=rollDegrees*Math.PI/180,pitch=pitchDegrees*Math.PI/180;
            double cy=Math.Cos(yaw),sy=Math.Sin(yaw),cr=Math.Cos(roll),sr=Math.Sin(roll),cp=Math.Cos(pitch),sp=Math.Sin(pitch);
            // Apply yaw, then pitch, then roll.
            var a=new MotionVector(point.X*cy+point.Z*sy,point.Y,-point.X*sy+point.Z*cy);
            var b=new MotionVector(a.X,a.Y*cp-a.Z*sp,a.Y*sp+a.Z*cp);
            return new MotionVector(b.X*cr-b.Y*sr,b.X*sr+b.Y*cr,b.Z);
        }
    }
}
