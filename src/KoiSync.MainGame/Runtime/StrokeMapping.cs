using KoiSync.Core;

namespace KoiSync.MainGame
{
    public static class StrokeMapping
    {
        public const double ReferenceLength=ShaftStrokeMapping.ReferenceLength;
        public const double ReferenceStroke=ShaftStrokeMapping.ReferenceStroke;
        public static bool ValidLength(double value){return ShaftStrokeMapping.ValidLength(value);}
        public static bool ValidModelLength(double value){return ShaftStrokeMapping.ValidModelLength(value);}
        public static double Depth(NativeMotionResult pose,double player,double game,double stroke,AxisMappingMode mode,bool nativeTipBound=false)
        {return ShaftStrokeMapping.Depth(pose,player,game,stroke,(DriveMappingMode)mode,nativeTipBound);}
        public static double SoftDepth(double depth,double limit){return ShaftStrokeMapping.SoftDepth(depth,limit);}
    }
}
