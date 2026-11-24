using System;
using KoiSync.Core;

namespace KoiSync.MainGame
{
    internal static class ActionMotion
    {
        internal static NativeMotionResult Solve(NativeMotionSample sample,ActionCalibration calibration,AxisMappingMode mode,bool useEndpoints)
        {return Solve(sample,calibration,mode,useEndpoints,false);}

        internal static NativeMotionResult Solve(NativeMotionSample sample,ActionCalibration calibration,AxisMappingMode mode,bool useEndpoints,bool adaptivePrincipalAxis)
        {
            if(!useEndpoints)return NativeMotion.Solve(sample);
            var end=adaptivePrincipalAxis?sample.End:ManualCalibrationMotion.WorldEnd(sample,calibration.Origin,calibration.ManualEnd.Value);
            var plane=mode==AxisMappingMode.Manual?calibration.PlaneDegrees:new MotionVector();
            return ManualCalibrationMotion.Solve(sample,end,plane);
        }

        internal static void Drive(GameDrive drive,NativeMotionSample sample,NativeMotionResult measured,ActionProfile profile,
            bool adapt,double player,double deviceStroke,double seconds)
        {Drive(drive,sample,measured,profile,adapt,player,deviceStroke,seconds,null,FeelPreset.Raw,NonShaftFeelMode.Foreplay);}

        internal static void Drive(GameDrive drive,NativeMotionSample sample,NativeMotionResult measured,ActionProfile profile,
            bool adapt,double player,double deviceStroke,double seconds,FeelTranslator feel,FeelPreset feelPreset)
        {Drive(drive,sample,measured,profile,adapt,player,deviceStroke,seconds,feel,feelPreset,NonShaftFeelMode.Foreplay);}

        internal static void Drive(GameDrive drive,NativeMotionSample sample,NativeMotionResult measured,ActionProfile profile,
            bool adapt,double player,double deviceStroke,double seconds,FeelTranslator feel,FeelPreset feelPreset,NonShaftFeelMode nonShaftMode)
        {DirectorActionEvent unused;Drive(drive,sample,measured,profile,adapt,player,deviceStroke,seconds,feel,feelPreset,nonShaftMode,null,0,0,out unused);}

        internal static void Drive(GameDrive drive,NativeMotionSample sample,NativeMotionResult measured,ActionProfile profile,
            bool adapt,double player,double deviceStroke,double seconds,FeelTranslator feel,FeelPreset feelPreset,NonShaftFeelMode nonShaftMode,
            DirectorModulator director,double femaleGauge,double maleGauge,out DirectorActionEvent directorEvent)
        {
            directorEvent=null;
            var mode=profile.Mode;
            // In Balanced/Intense feel mode, upgrade legacy Absolute profiles to Soft rotation
            // so high-angle poses (e.g. cowgirl R2 pitch) get C1 soft-shoulder protection instead of pegging at 999.
            if(feelPreset!=FeelPreset.Raw&&mode==AxisMappingMode.Absolute)mode=AxisMappingMode.Soft;
            var reference=mode==AxisMappingMode.Relative?profile.Rotation:null;
            var input=adapt?ShaftStrokeMapping.Adapt(measured,player,profile.GameLength,deviceStroke,(DriveMappingMode)mode,
                sample.HasNativeTipContact,reference==null):measured.Normalized;
            if(reference!=null)input=reference.Apply(measured,input);
            if(mode==AxisMappingMode.Relative&&reference==null)mode=AxisMappingMode.Soft;
            bool isNonShaft=profile.Effector!=ActionSource.Shaft&&profile.Effector!=ActionSource.AnimatedProp;
            if(feel!=null&&feelPreset!=FeelPreset.Raw)
                input=feel.Translate(input,measured,sample,feelPreset,isNonShaft,nonShaftMode,seconds);
            if(director!=null)
                input=director.Apply(input,seconds,femaleGauge,maleGauge,out directorEvent,feel!=null?feel.OuterCeiling:FeelTranslator.DefaultOuterCeiling);
            drive.PhysicalL0Minimum=0;
            drive.Update(input,seconds,mode);
        }
    }
}
