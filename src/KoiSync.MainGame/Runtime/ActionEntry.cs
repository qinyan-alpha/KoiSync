using System;
using KoiSync.Core;

namespace KoiSync.MainGame
{
    internal struct ActionEntry
    {
        internal readonly AlignmentFrame Landmark,Observed,Calibrated;
        internal readonly string Source;

        internal ActionEntry(AlignmentFrame landmark,AlignmentFrame observed,MotionVector offset,string source)
        {
            Landmark=landmark;
            Observed=observed;
            observed.Origin+=observed.Y*offset.X+observed.Z*offset.Y+observed.X*offset.Z;
            Calibrated=observed;
            Source=source;
        }

        internal static bool MatchesNative(NativeTarget contact,bool pinned,bool sameActor,string node,string parent)
        {
            if(!pinned||!sameActor)return false;
            switch(contact)
            {
                case NativeTarget.VAGINAL:return Contains(node,"kokan")||Contains(node,"vagina")||Contains(parent,"kokan")||Contains(parent,"vagina");
                case NativeTarget.ANAL:return Contains(node,"ana")||Contains(parent,"ana");
                case NativeTarget.ORAL:return Contains(node,"head")||Contains(node,"mouth")||Contains(parent,"head")||Contains(parent,"mouth");
                default:return false;
            }
        }

        internal static AlignmentFrame Frame(MotionVector origin,MotionVector right,MotionVector inward)
        {
            var axis=inward.Unit();
            if(MotionVector.Cross(right,axis).Length<1e-7)
                right=Math.Abs(axis.X)<.9?new MotionVector(1,0,0):new MotionVector(0,0,1);
            return AlignmentFrame.Build(origin,right,axis);
        }

        internal static double AngleBetween(AlignmentFrame a,AlignmentFrame b)
        {
            double trace=MotionVector.Dot(a.X,b.X)+MotionVector.Dot(a.Y,b.Y)+MotionVector.Dot(a.Z,b.Z);
            return Math.Acos(Numbers.Clamp((trace-1)*.5,-1,1))*180/Math.PI;
        }

        private static bool Contains(string text,string part)
        {return text!=null&&text.IndexOf(part,StringComparison.OrdinalIgnoreCase)>=0;}
    }
}
