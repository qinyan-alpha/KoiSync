using System;

namespace KoiSync.Core
{
    // Body-part references follow Osr2_sr6_link.Dis_angle_blowjob/breastsex/handjob.
    // No shared last-angle fields: independent actors must not contaminate each other.
    public sealed class StudioContactReference
    {
        public MotionVector SourceLeft,SourceRight,TwistStart,TwistEnd,TiltStart,TiltEnd;
        public MotionVector SourceTorso,TargetTorso;
        public string Name="部位参考";
    }
    public static class StudioContactMotion
    {
        private static MotionVector SourcePoint(NativeMotionSample s,MotionVector p)
        {return s.Origin+s.ReferenceFrame.World(s.BodyFrame.Local(p-s.AnchorRoot));}
        private static MotionVector TargetPoint(NativeMotionSample s,MotionVector p)
        {return s.Target+s.TargetReferenceFrame.World(s.TargetBodyFrame.Local(p-s.AnchorTarget));}
        public static MotionVector Tilt(NativeMotionSample s)
        {return s.TargetReferenceFrame.World(s.TargetBodyFrame.Local(s.Contact.TiltEnd-s.Contact.TiltStart));}
        public static MotionVector Twist(NativeMotionSample s)
        {return s.TargetReferenceFrame.World(s.TargetBodyFrame.Local(s.Contact.TwistEnd-s.Contact.TwistStart));}
        private static MotionVector Normal(MotionVector p,MotionVector left,MotionVector right,MotionVector fallback)
        {var n=MotionVector.Cross(p-left,p-right);return n.Length>1e-7?n.Unit():fallback.Unit();}
        private static double Acute(MotionVector a,MotionVector b)
        {if(a.Length<1e-7||b.Length<1e-7)return 0;double angle=MotionVector.Angle(a,b);return angle>90?180-angle:angle;}
        public static NativeMotionResult Solve(NativeMotionSample s)
        {
            var c=s.Contact;double length=(s.End-s.Origin).Length;
            if(length<1e-7)throw new ArgumentException("来源长度无效");
            var left=SourcePoint(s,c.SourceLeft);var right=SourcePoint(s,c.SourceRight);var mid=(left+right)*.5;
            var plane=Normal(s.Target,left,right,s.ReferenceFrame.Y);
            // Correct orthogonal projection; the original subtraction mirrored the point across the plane.
            var projected=s.Origin-plane*MotionVector.Dot(s.Origin-s.Target,plane);
            var along=mid-s.Target;if(along.Length<1e-7)along=s.ReferenceFrame.Z;along=along.Unit();
            var delta=projected-s.Target;
            double surge=Math.Abs(MotionVector.Dot(delta,along)),sway=MotionVector.Cross(delta,along).Length;

            var sourceNormal=Normal(s.Origin,left,right,s.ReferenceFrame.Y);
            if(MotionVector.Dot(sourceNormal,s.ReferenceFrame.Y)<0)sourceNormal=sourceNormal*-1;
            var sourceLine=right-left;sourceLine-=sourceNormal*MotionVector.Dot(sourceLine,sourceNormal);
            if(sourceLine.Length<1e-7)sourceLine=s.ReferenceFrame.X;sourceLine=sourceLine.Unit();
            var targetLine=Twist(s);targetLine-=sourceNormal*MotionVector.Dot(targetLine,sourceNormal);
            double wrapped=targetLine.Length<1e-7?double.NaN:Math.IEEERemainder(Math.Atan2(MotionVector.Dot(MotionVector.Cross(sourceLine,targetLine),sourceNormal),MotionVector.Dot(sourceLine,targetLine))*180/Math.PI,180);
            double r0=s.R0Continuity!=null?s.R0Continuity.Resolve(wrapped):Numbers.Finite(wrapped)?wrapped:0;

            var tiltA=TargetPoint(s,c.TiltStart);var tilt=Tilt(s);
            var rollNormal=Normal(tiltA,left,right,s.ReferenceFrame.Y);
            var toward=mid-tiltA;if(toward.Length<1e-7)toward=s.ReferenceFrame.Z;toward=toward.Unit();
            var projectedTilt=tilt-toward*MotionVector.Dot(tilt,toward);
            double r1=Acute(projectedTilt,rollNormal);
            if(MotionVector.Dot(projectedTilt,MotionVector.Cross(rollNormal,toward))<0)r1=-r1;
            var sourceTorso=s.ReferenceFrame.World(s.BodyFrame.Local(c.SourceTorso));
            var targetTorso=s.TargetReferenceFrame.World(s.TargetBodyFrame.Local(c.TargetTorso));
            double r2=tilt.Length<1e-7?0:(90-Acute(tilt,rollNormal))*(MotionVector.Dot(sourceTorso,targetTorso)<0?1:-1);
            double distance=(s.Target-s.Origin).Length;
            double[] raw={distance,surge,sway,r0,r1,r2};
            double[] normalized={distance/length,.5+surge/length,.5+sway/length,PelvisTwist.Position(r0),.5+r1/180,.5+r2/180};
            foreach(double value in normalized)if(!Numbers.Finite(value))throw new ArgumentException("部位参考坐标无效");
            return new NativeMotionResult{Length=length,X=(s.End-s.Origin).Unit(),Y=along,Z=MotionVector.Cross(plane,along).Unit(),Raw=raw,Normalized=normalized};
        }
    }
}
