using System;
using System.Globalization;
using KoiSync.Core;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        private double manualR0PlaneDegrees,manualR1PlaneDegrees,manualR2PlaneDegrees;
        private MotionVector ManualPlaneDegrees {get{return new MotionVector(manualR0PlaneDegrees,manualR1PlaneDegrees,manualR2PlaneDegrees);}}
        private bool ManualCalibrationActive {get{return !UseScripts&&MappingMode==AxisMappingMode.Manual;}}
        private void EnsureManualEndpoints(double initialLength)
        {
            if(Binding==null)return;
            if(!Binding.ManualEndOffset.HasValue)
            {
                var modelEnd=Binding.ReadModelEndOffset();
                try{ManualCalibrationMotion.Length(Binding.OriginOffset,modelEnd);Binding.ManualEndOffset=modelEnd;}
                catch(ArgumentException){Binding.ManualEndOffset=Binding.OriginOffset+new MotionVector(initialLength,0,0);}
            }
            Binding.SetLength(ManualCalibrationMotion.Length(Binding.OriginOffset,Binding.ManualEndOffset.Value));
            SyncManualPlayerLength();
        }
        private void SyncManualPlayerLength()
        {
            if(Binding==null)return;
            // The endpoint distance is the single length authority in manual
            // mode. Range linking is applied once by SyncMappingLength.
            playerLengthSetting.Value=(float)(Binding.RestLength*1000);
            playerLengthText=rodMillimetres=(Binding.RestLength*1000).ToString("G6",CultureInfo.InvariantCulture);
            lengthSetting.Value=(float)(Binding.RestLength*1000);
        }
        private void SetManualLength(double length,bool save)
        {
            EnsureManualEndpoints(Binding.RestLength);
            var direction=(Binding.ManualEndOffset.Value-Binding.OriginOffset).Unit();
            var end=Binding.OriginOffset+direction*length;
            length=ManualCalibrationMotion.Length(Binding.OriginOffset,end);
            Binding.SetLength(length);Binding.ManualEndOffset=end;SyncManualPlayerLength();
            SyncMappingLength(length,save);Drive.Reset();Devices.Stop();
            CaptureChapterCalibration();ProtectChapterCalibration();if(save)Config.Save();
        }
        private void ResetManualEndToModel(bool save)
        {
            if(Binding==null)return;
            var end=Binding.ReadModelEndOffset();
            double length=ManualCalibrationMotion.Length(Binding.OriginOffset,end);
            Binding.ManualEndOffset=end;Binding.SetLength(length);SyncManualPlayerLength();
            SyncMappingLength(length,save);Drive.Reset();Devices.Stop();
            CaptureChapterCalibration();ProtectChapterCalibration();if(save)Config.Save();
            status=L("阴茎末端已回到当前模型骨骼末端","Penis tip restored to the current model bone tip");
        }
        private void SetManualPlane(int axis,double degrees)
        {
            if(!ManualCalibrationActive||RotationChapter()==null)return;
            double limit=axis==0?180:90,value=Numbers.Clamp(Math.Round(degrees,1),-limit,limit);
            if(axis==0)manualR0PlaneDegrees=value;else if(axis==1)manualR1PlaneDegrees=value;else manualR2PlaneDegrees=value;
            Drive.Reset();Devices.Stop();CaptureChapterCalibration();ProtectChapterCalibration();
        }
        private NativeMotionResult SolveManualCalibration(NativeMotionSample sample)
        {
            if(!Binding.ManualEndOffset.HasValue)
            {EnsureManualEndpoints(Binding.RestLength);CaptureChapterCalibration();}
            return ManualCalibrationMotion.Solve(sample,ManualCalibrationMotion.WorldEnd(sample,Binding.OriginOffset,Binding.ManualEndOffset.Value),ManualPlaneDegrees);
        }
    }
}
