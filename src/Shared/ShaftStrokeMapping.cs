using System;

namespace KoiSync.Core
{
    public static class ShaftStrokeMapping
    {
        public const double ReferenceLength=.1384;
        public const double ReferenceStroke=.120;
        public const double ReferenceInteraction=.130;

        public static bool ValidLength(double value){return Numbers.Finite(value)&&value>=.02&&value<=.5;}
        public static bool ValidModelLength(double value){return Numbers.Finite(value)&&value>=ManualCalibrationMotion.MinimumLength&&value<=.5;}

        public static double FromBodyHeight(double height)
        {
            double scale=Numbers.Finite(height)&&height>=.5&&height<=5?height/1.70:1;
            return Numbers.Clamp(ReferenceInteraction*scale,.02,.5);
        }

        public static double Depth(NativeMotionResult pose,double player,double game,double stroke,DriveMappingMode mode,bool nativeTipBound=false)
        {
            if(!ValidModelLength(player)||!ValidLength(game)||!ValidLength(stroke))throw new ArgumentException("Player length must be 1-500 mm; game/device travel must be 20-500 mm");
            bool pinned=nativeTipBound&&StudioShaftMotion.NativeTipReachesEntry(pose.ContactDistance,pose.Length);
            if(pose.Engagement<=0||pose.MaterialInsertion<=1e-7&&!pinned)return 0;
            if(!ValidModelLength(pose.Length))throw new ArgumentException("Invalid model mapping length");

            double depth=Math.Max(0,pose.TargetDepth)*pose.Engagement;
            if(mode==DriveMappingMode.Soft)depth*=player/pose.Length;
            else if(mode!=DriveMappingMode.Absolute)depth*=Math.Min(player,game)/pose.Length;
            double limit=Math.Min(player,stroke);
            return (mode==DriveMappingMode.Absolute?Numbers.Clamp(depth,0,limit):SoftDepth(depth,limit))/stroke;
        }

        public static double SoftDepth(double depth,double limit)
        {
            double knee=limit*.8,shoulder=limit-knee;
            if(depth<=knee)return Math.Max(0,depth);
            double excess=depth-knee;
            return knee+shoulder*(excess/(shoulder+excess));
        }

        public static double[] Adapt(NativeMotionResult pose,double player,double game,double stroke,DriveMappingMode mode,bool nativeTipBound=false,bool solveRotation=true)
        {
            var input=(double[])pose.Normalized.Clone();
            input[0]=Depth(pose,player,game,stroke,mode,nativeTipBound);
            if(solveRotation&&pose.Engagement>0)
            {
                var rotation=SR6DeviceRotation.Positions(pose.Raw[3],pose.Raw[4],pose.Raw[5],1-input[0],input[1],input[2]);
                Array.Copy(rotation,0,input,3,3);
            }
            return input;
        }
    }
}
