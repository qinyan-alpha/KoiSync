using System;

namespace KoiSync.Core
{
    public static class PelvisTwist
    {
        // A pelvis line has no arrow: parallel and antiparallel both mean zero twist.
        // Compare in the source pelvis plane so target pitch does not become R0.
        public static double Measure(AlignmentFrame source,AlignmentFrame target)
        {
            double right=MotionVector.Dot(target.X,source.X),forward=MotionVector.Dot(target.X,source.Z);
            if(right*right+forward*forward<1e-8)return double.NaN;
            double angle=Math.Atan2(-forward,right)*180/Math.PI;
            return Math.IEEERemainder(angle,180);
        }
        public static double Resolve(NativeMotionSample sample)
        {
            double angle=Measure(sample.ReferenceFrame,sample.TargetReferenceFrame);
            return sample.R0Continuity!=null?sample.R0Continuity.Resolve(angle):Numbers.Finite(angle)?angle:0;
        }
        public static double Position(double angle){return .5+angle/180;}
    }
}
