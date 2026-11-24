using System;

namespace KoiSync.Core
{
    public struct AlignmentFrame
    {
        public MotionVector Origin,X,Y,Z;
        public static AlignmentFrame Build(MotionVector origin,MotionVector right,MotionVector up)
        {
            MotionVector y=up.Unit(),x=(right-y*MotionVector.Dot(right,y)).Unit();
            return new AlignmentFrame{Origin=origin,X=x,Y=y,Z=MotionVector.Cross(x,y).Unit()};
        }
        public MotionVector World(MotionVector local){return X*local.X+Y*local.Y+Z*local.Z;}
        public MotionVector Local(MotionVector world){return new MotionVector(MotionVector.Dot(world,X),MotionVector.Dot(world,Y),MotionVector.Dot(world,Z));}
        public AlignmentFrame Adjust(MotionVector offset,MotionVector degrees)
        {
            return new AlignmentFrame{Origin=Origin+World(offset),X=World(Rotate(new MotionVector(1,0,0),degrees)),Y=World(Rotate(new MotionVector(0,1,0),degrees)),Z=World(Rotate(new MotionVector(0,0,1),degrees))};
        }
        // Unity Quaternion.Euler order: Z, then X, then Y, in local coordinates.
        private static MotionVector Rotate(MotionVector p,MotionVector degrees)
        {
            double x=degrees.X*Math.PI/180,y=degrees.Y*Math.PI/180,z=degrees.Z*Math.PI/180;
            var a=new MotionVector(p.X*Math.Cos(z)-p.Y*Math.Sin(z),p.X*Math.Sin(z)+p.Y*Math.Cos(z),p.Z);
            var b=new MotionVector(a.X,a.Y*Math.Cos(x)-a.Z*Math.Sin(x),a.Y*Math.Sin(x)+a.Z*Math.Cos(x));
            return new MotionVector(b.X*Math.Cos(y)+b.Z*Math.Sin(y),b.Y,-b.X*Math.Sin(y)+b.Z*Math.Cos(y));
        }
    }
    public sealed class TimelineViewport
    {
        public double Start,Span=30,Duration=30;
        public void Fit(double duration){Duration=Math.Max(.01,duration);Start=0;Span=Duration;}
        public double At(double fraction){return Numbers.Clamp(Start+fraction*Span,0,Duration);}
        public void Zoom(double fraction,double wheel)
        {
            double anchor=Start+fraction*Span;
            Span=Numbers.Clamp(Span*Math.Pow(1.25,-wheel),Math.Min(.05,Duration),Duration);
            Start=anchor-fraction*Span;Clamp();
        }
        public void Pan(double fraction){Start+=fraction*Span;Clamp();}
        public void Clamp(){Span=Numbers.Clamp(Span,Math.Min(.05,Duration),Duration);Start=Numbers.Clamp(Start,0,Math.Max(0,Duration-Span));}
        public double Tick(double width)
        {
            double desired=Span/Math.Max(1,width/95),power=Math.Pow(10,Math.Floor(Math.Log10(desired))),n=desired/power;
            return (n<=1?1:n<=2?2:n<=5?5:10)*power;
        }
    }
    public static class BodyTwist
    {
        // Extract twist around the measured shaft from the relative pelvis rotation.
        // Both complete bases are used, so a waist line parallel to the shaft is not singular.
        public static double Measure(AlignmentFrame a,AlignmentFrame b,MotionVector axis)
        {
            MotionVector c0=b.X*a.X.X+b.Y*a.Y.X+b.Z*a.Z.X,c1=b.X*a.X.Y+b.Y*a.Y.Y+b.Z*a.Z.Y,c2=b.X*a.X.Z+b.Y*a.Y.Z+b.Z*a.Z.Z;
            double w,x,y,z,t=c0.X+c1.Y+c2.Z;
            if(t>0){double s=Math.Sqrt(t+1)*2;w=s/4;x=(c1.Z-c2.Y)/s;y=(c2.X-c0.Z)/s;z=(c0.Y-c1.X)/s;}
            else if(c0.X>c1.Y&&c0.X>c2.Z){double s=Math.Sqrt(1+c0.X-c1.Y-c2.Z)*2;w=(c1.Z-c2.Y)/s;x=s/4;y=(c1.X+c0.Y)/s;z=(c2.X+c0.Z)/s;}
            else if(c1.Y>c2.Z){double s=Math.Sqrt(1+c1.Y-c0.X-c2.Z)*2;w=(c2.X-c0.Z)/s;x=(c1.X+c0.Y)/s;y=s/4;z=(c2.Y+c1.Z)/s;}
            else{double s=Math.Sqrt(1+c2.Z-c0.X-c1.Y)*2;w=(c0.Y-c1.X)/s;x=(c2.X+c0.Z)/s;y=(c2.Y+c1.Z)/s;z=s/4;}
            double dot=MotionVector.Dot(new MotionVector(x,y,z),axis.Unit());
            if(w*w+dot*dot<1e-12)return double.NaN;
            double angle=2*Math.Atan2(dot,w)*180/Math.PI;while(angle>180)angle-=360;while(angle< -180)angle+=360;return angle;
        }
    }
}
