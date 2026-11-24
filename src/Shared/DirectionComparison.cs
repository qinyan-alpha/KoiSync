using System;

namespace KoiSync.Core
{
    public sealed class DirectionComparison
    {
        public double Deviation,Yaw,Pitch;
        public static DirectionComparison Measure(NativeMotionSample sample,MotionVector entry,MotionVector end)
        {
            var basis=NativeMotion.Solve(sample);MotionVector direction=(end-entry).Unit();
            double x=MotionVector.Dot(direction,basis.X),y=MotionVector.Dot(direction,basis.Y),z=MotionVector.Dot(direction,basis.Z);
            return new DirectionComparison{Deviation=MotionVector.Angle(basis.X,direction),
                Yaw=Math.Atan2(z,x)*180/Math.PI,Pitch=Math.Atan2(y,Math.Sqrt(x*x+z*z))*180/Math.PI};
        }
    }
}
