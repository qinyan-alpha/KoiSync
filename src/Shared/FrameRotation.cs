using System;

namespace KoiSync.Core
{
    // Shortest quaternion interpolation in the device frame. No Unity dependency
    // and no state changes from diagnostic/render calls.
    public static class FrameRotation
    {
        public static AlignmentFrame Blend(AlignmentFrame reference,AlignmentFrame target,double weight)
        {
            if(weight<=0)return reference;
            if(weight>=1)return target;
            MotionVector a=reference.Local(target.X),b=reference.Local(target.Y),c=reference.Local(target.Z);
            double w,x,y,z,trace=a.X+b.Y+c.Z;
            if(trace>0){double s=Math.Sqrt(trace+1)*2;w=s/4;x=(b.Z-c.Y)/s;y=(c.X-a.Z)/s;z=(a.Y-b.X)/s;}
            else if(a.X>b.Y&&a.X>c.Z){double s=Math.Sqrt(1+a.X-b.Y-c.Z)*2;w=(b.Z-c.Y)/s;x=s/4;y=(b.X+a.Y)/s;z=(c.X+a.Z)/s;}
            else if(b.Y>c.Z){double s=Math.Sqrt(1+b.Y-a.X-c.Z)*2;w=(c.X-a.Z)/s;x=(b.X+a.Y)/s;y=s/4;z=(c.Y+b.Z)/s;}
            else{double s=Math.Sqrt(1+c.Z-a.X-b.Y)*2;w=(a.Y-b.X)/s;x=(c.X+a.Z)/s;y=(c.Y+b.Z)/s;z=s/4;}
            double norm=Math.Sqrt(w*w+x*x+y*y+z*z),sign=w<0?-1:1;
            w=Numbers.Clamp(w*sign/norm,-1,1);x*=sign/norm;y*=sign/norm;z*=sign/norm;
            double angle=Math.Acos(w),sin=Math.Sin(angle);
            double factor=sin<1e-9?weight:Math.Sin(angle*weight)/sin;
            var q=new MotionVector(x*factor,y*factor,z*factor);w=Math.Cos(angle*weight);
            return new AlignmentFrame{Origin=target.Origin,
                X=reference.World(Rotate(new MotionVector(1,0,0),q,w)),
                Y=reference.World(Rotate(new MotionVector(0,1,0),q,w)),
                Z=reference.World(Rotate(new MotionVector(0,0,1),q,w))};
        }
        private static MotionVector Rotate(MotionVector v,MotionVector q,double w)
        {MotionVector t=MotionVector.Cross(q,v)*2;return v+t*w+MotionVector.Cross(q,t);}
    }
}
