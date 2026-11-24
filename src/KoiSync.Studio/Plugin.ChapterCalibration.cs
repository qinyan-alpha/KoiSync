using System;
using System.Globalization;
using KoiSync.Core;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        private bool calibrationApplying;
        private void ProtectChapterCalibration()
        {if(!calibrationApplying){var c=RotationChapter();if(c!=null){c.CalibrationEdited=true;MarkSceneDataDirty();}}}
        private bool CalibrationMatchesBinding(ChapterCalibration c)
        {
            return c==null||Binding!=null&&Math.Abs(c.Length-Binding.RestLength)<1e-8&&Math.Abs(c.L1-l1HalfRange)<1e-8&&Math.Abs(c.L2-l2HalfRange)<1e-8&&
                Math.Abs(c.R0Plane-manualR0PlaneDegrees)<1e-8&&Math.Abs(c.R1Plane-manualR1PlaneDegrees)<1e-8&&Math.Abs(c.R2Plane-manualR2PlaneDegrees)<1e-8&&(c.Origin-Binding.OriginOffset).Length<1e-8&&
                c.ManualEnd.HasValue==Binding.ManualEndOffset.HasValue&&(!c.ManualEnd.HasValue||(c.ManualEnd.Value-Binding.ManualEndOffset.Value).Length<1e-8);
        }
        private ChapterCalibration CurrentCalibration()
        {
            if(Binding==null)return null;
            return new ChapterCalibration{Length=Binding.RestLength,L1=l1HalfRange,L2=l2HalfRange,R0Plane=manualR0PlaneDegrees,R1Plane=manualR1PlaneDegrees,R2Plane=manualR2PlaneDegrees,Origin=Binding.OriginOffset,ManualEnd=Binding.ManualEndOffset,Linked=linkLengthSetting.Value};
        }
        private void CaptureChapterCalibration()
        {
            if(calibrationApplying||Binding==null)return;
            var chapter=Book.At(Clock.Time);
            if(chapter==null){if(autoCameraPairs)return;chapter=MakeChapter(Binding.Source,Binding.Target,Binding.Kind);Book.Observe(0,chapter);}
            if(chapter.Source!=Key(Binding.Source)||chapter.Target!=Key(Binding.Target)||chapter.Opening!=(int)Binding.Kind)return;
            chapter.Calibration=CurrentCalibration();MarkSceneDataDirty();
        }
        private void ApplyChapterCalibration(ChapterCalibration calibration)
        {
            if(calibration==null||Binding==null)return;calibration.Validate();calibrationApplying=true;
            try
            {
                Binding.SetLength(calibration.Length);Binding.OriginOffset=calibration.Origin;Binding.ManualEndOffset=calibration.ManualEnd;
                manualR0PlaneDegrees=calibration.R0Plane;manualR1PlaneDegrees=calibration.R1Plane;manualR2PlaneDegrees=calibration.R2Plane;
                l1HalfRange=calibration.L1;l2HalfRange=calibration.L2;mappingLength=calibration.Length;
                Binding.L1HalfRange=l1HalfRange;Binding.L2HalfRange=l2HalfRange;
                linkLengthSetting.Value=calibration.Linked;rodMillimetres=(calibration.Length*1000).ToString("G6",CultureInfo.InvariantCulture);
                if(ManualCalibrationActive)EnsureManualEndpoints(calibration.Length);
                RefreshRangeText();RefreshOriginText();Drive.Reset();
            }
            finally{calibrationApplying=false;}
        }
        private ChapterCalibration CalibrationForPair(string identity)
        {
            var at=Book.At(Clock.Time);if(at!=null&&at.Identity==identity&&at.Calibration!=null)return at.Calibration.Copy();
            for(int i=Book.Chapters.Count-1;i>=0;i--)if(Book.Chapters[i].Identity==identity&&Book.Chapters[i].Calibration!=null)return Book.Chapters[i].Calibration.Copy();
            return null;
        }
        private void SaveCalibrationToSelectedChapter()
        {
            if(selectedChapter==null||Binding==null)return;
            RememberChapters();selectedChapter.Calibration=CurrentCalibration();selectedChapter.CalibrationEdited=true;MarkSceneDataDirty();appliedChapter="";
            status=L("当前校准已写入所选章节","Current calibration saved to the selected chapter");
        }
    }
}
