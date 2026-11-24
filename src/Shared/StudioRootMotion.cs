using System;

namespace KoiSync.Core
{
    // Studio positions: root separation is 0 at contact and 1 at reference length.
    public static class StudioRootMotion
    {
        public static MotionVector TiltVector(NativeMotionSample s)
        {
            if(s.Contact!=null)return StudioContactMotion.Tilt(s);
            MotionVector v=s.TargetHip-s.Target;
            return s.TargetReferenceFrame.World(s.TargetBodyFrame.Local(v));
        }
        public static NativeMotionResult Solve(NativeMotionSample s)
        {
            MotionVector shaft=s.End-s.Origin;double length=shaft.Length;
            MotionVector axis=shaft.Unit(),delta=s.Target-s.Origin;
            MotionVector right=s.ReferenceFrame.X.Unit(),forward=s.ReferenceFrame.Z.Unit(),up=s.ReferenceFrame.Y.Unit();
            double distance=delta.Length,l1=MotionVector.Dot(delta,right),l2=MotionVector.Dot(delta,forward);
            MotionVector tilt=TiltVector(s);double tiltLength=tilt.Length;
            double r1=0,r2=0;
            if(tiltLength>1e-7)
            {
                double horizontal=MotionVector.Dot(tilt,right);
                r1=Math.Asin(Numbers.Clamp(horizontal/tiltLength,-1,1))*180/Math.PI;
                // R2 uses the same TCode/OSR2 form as the contact and prop paths: -(90 - angle(line, reference)).
                // The reference is the in-plane pitch axis, not the plane normal. A normal penetration pose
                // puts the target pelvis line along the plane normal, which reads 90 deg here and therefore
                // lands on the centre (500) instead of pinning the axis at the top of its range.
                r2=-(90-MotionVector.Angle(tilt,forward));
            }
            double r0=PelvisTwist.Resolve(s);
            double[] raw={distance,l1,l2,r0,r1,r2};
            double[] normalized={distance/length,.5+l1/length,.5+l2/length,PelvisTwist.Position(r0),.5+r1/180,.5+r2/180};
            foreach(double n in normalized)if(!Numbers.Finite(n))throw new ArgumentException("工作室根点坐标无效");
            return new NativeMotionResult{X=axis,Y=right,Z=forward,Length=length,Raw=raw,Normalized=normalized};
        }
    }
}
