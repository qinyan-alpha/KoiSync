using System;

namespace KoiSync.Core
{
    internal static class RotationReferenceMotion
    {
        private static readonly AlignmentFrame Identity=new AlignmentFrame{X=new MotionVector(1,0,0),Y=new MotionVector(0,1,0),Z=new MotionVector(0,0,1)};

        internal static bool CanCapture(NativeMotionResult measured)
        {return measured!=null&&measured.Raw!=null&&measured.Raw.Length==6&&measured.Engagement>=.999&&Numbers.Finite(measured.Raw[3])&&Numbers.Finite(measured.Raw[4])&&Numbers.Finite(measured.Raw[5]);}

        internal static double[] Apply(NativeMotionResult measured,double[] normalized,double yaw,double roll,double pitch)
        {
            if(measured.Engagement<=0)return normalized;
            // Compare complete frames and fade the reference with contact.
            var baseline=FrameRotation.Blend(Identity,ContactPreviewMotion.BodyFrame(Identity,new MotionVector(),yaw,roll,pitch),measured.Engagement);
            var desired=ContactPreviewMotion.BodyFrame(Identity,new MotionVector(),measured.Raw[3],measured.Raw[4],measured.Raw[5]);
            var angles=StudioShaftMotion.BodyAngles(baseline,desired);
            var result=(double[])normalized.Clone();
            var rotation=SR6DeviceRotation.Positions(angles[0],angles[1],angles[2],1-result[0],result[1],result[2]);
            Array.Copy(rotation,0,result,3,3);
            return result;
        }
    }
}
