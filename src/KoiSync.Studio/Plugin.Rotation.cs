using BepInEx.Configuration;
using KoiSync.Core;
using System;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> rotationReferenceSetting;
        private ConfigEntry<int> mappingModeSetting;
        private ConfigEntry<int> feelPresetSetting;
        private ConfigEntry<float> feelOuterCeilingSetting;
        internal readonly StudioFeelEngine Feel=new StudioFeelEngine();
        private void InitializeFeel()
        {
            feelPresetSetting=Config.Bind("Feel","Preset",(int)FeelPreset.Balanced,"0 Raw (1:1 geometry), 1 Balanced (chapter-calibrated golden window), 2 Intense (full dynamic expansion).");
            feelOuterCeilingSetting=Config.Bind("Feel","OuterCeilingL0",(float)(FeelTranslator.DefaultOuterCeiling*100.0),"L0 outer limit (55..99%, default 99%).");
            Feel.Preset=(FeelPreset)Math.Max(0,Math.Min(2,feelPresetSetting.Value));
            Feel.OuterCeiling=Numbers.Clamp(feelOuterCeilingSetting.Value/100.0,0.55,0.99);
        }
        internal void SetFeelOuterCeiling(double ceiling)
        {
            double clamped=Numbers.Clamp(Numbers.Finite(ceiling)?ceiling:FeelTranslator.DefaultOuterCeiling,0.55,0.99);
            Feel.OuterCeiling=clamped;
            if(feelOuterCeilingSetting!=null)feelOuterCeilingSetting.Value=(float)Math.Round(clamped*100.0,1);
            Config.Save();
        }
        internal void SetFeelPreset(FeelPreset preset)
        {
            Feel.Preset=preset;
            if(feelPresetSetting!=null)feelPresetSetting.Value=(int)preset;
            if(mappingModeSetting!=null&&mappingModeSetting.Value==(int)AxisMappingMode.Manual)
                mappingModeSetting.Value=(int)AxisMappingMode.Soft;
            var ch=RotationChapter();
            if(ch!=null&&ch.MappingMode==(int)AxisMappingMode.Manual)
            {
                ch.MappingMode=(int)AxisMappingMode.Soft;
                MarkSceneDataDirty();
            }
            Feel.Reset();
            Drive.Reset();
            Config.Save();
        }
        private int ChapterMappingMode(Chapter chapter)
        {return chapter!=null&&chapter.MappingMode>=0?chapter.MappingMode:Math.Max(0,Math.Min(3,mappingModeSetting.Value));}
        private AxisMappingMode MappingMode {get{return (AxisMappingMode)ChapterMappingMode(RotationChapter());}}
        private Chapter RotationChapter()
        {
            var chapter=Book.At(Clock.Time);
            return Binding!=null&&chapter!=null&&chapter.Source==Key(Binding.Source)&&chapter.Target==Key(Binding.Target)&&chapter.Opening==(int)Binding.Kind?chapter:null;
        }
        private void UpdateLiveDrive(double dt)
        {
            var chapter=RotationChapter();var mode=MappingMode;
            if(mode==AxisMappingMode.Relative&&chapter!=null&&chapter.RotationReference==null&&ChapterRotationReference.CanCapture(Measured))
            {
                chapter.RotationReference=ChapterRotationReference.Capture(Measured,Clock.Time);
                MarkSceneDataDirty();
            }
            var reference=mode!=AxisMappingMode.Relative||chapter==null?null:chapter.RotationReference;
            double[] input=Measured.Normalized;
            if(strokeMappingSetting.Value&&Feel.Preset==FeelPreset.Raw&&mode!=AxisMappingMode.Manual)
            {
                var calibration=EnsureStrokeCalibration();
                input=ShaftStrokeMapping.Adapt(Measured,PlayerLength,calibration.GameLength,DeviceStroke,(DriveMappingMode)mode,solveRotation:reference==null);
            }
            if(reference!=null)input=reference.Apply(Measured,input);
            if(mode!=AxisMappingMode.Manual&&Feel.Preset!=FeelPreset.Raw&&Sample!=null&&Measured!=null)
            {
                string chKey=chapter==null?"live":chapter.Start.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+":"+chapter.Identity;
                int opening=Binding!=null?(int)Binding.Kind:0;
                bool isClimax=chapter!=null&&chapter.IsClimax&&ShouldUseFixedClimaxOscillation(chapter);
                double p05=chapter!=null?chapter.DepthP05:double.NaN;
                double p95=chapter!=null?chapter.DepthP95:double.NaN;
                double peakHz=chapter!=null?chapter.PeakHz:0;
                input=Feel.Process(input,Measured,Sample,chKey,opening,isClimax,p05,p95,peakHz,dt);
            }
            activeGrindFrame=default(StudioGrindFrame);
            if(chapter!=null&&!chapter.IsClimax&&StudioGrindEngine.IsDirectInsertionOpening(chapter.Opening)&&chapter.Grind!=null&&chapter.Grind.Enabled)
            {
                activeGrindFrame=StudioGrindEngine.Evaluate(Clock.Available?Clock.Time:0,Numbers.Clamp(input[0],0,1),chapter.Grind);
                input=StudioGrindEngine.ApplyToAxes(input,activeGrindFrame);
            }
            Drive.Update(input,dt,mode==AxisMappingMode.Relative&&reference==null?AxisMappingMode.Soft:mode);
        }
        private void SetRotationMode(bool automaticReference)
        {
            SetMappingMode(automaticReference?2:1);
        }
        private void SetMappingMode(int mode)
        {
            if(mode<0||mode>3||UseScripts&&mode!=3)return;
            double initialLength=Binding==null?.13:PreviewLength;
            // Length, origin and preview all edit the currently bound pose.
            // A list selection may point at a different, unplayed chapter.
            var chapter=RotationChapter();
            if(chapter!=null)
            {
                if(chapter.MappingMode==mode&&mappingModeSetting.Value==mode&&mode!=3)return;
                RememberChapters();chapter.MappingMode=mode;MarkSceneDataDirty();
            }
            // Chapters with an explicit mode keep it. Unset chapters inherit
            // the user's latest choice instead of silently reverting to Soft.
            mappingModeSetting.Value=mode;rotationReferenceSetting.Value=mode==2;
            if(mode==3)
            {
                // Selecting manual calibration deliberately switches to live
                // input. It never disarms the user's device sync toggle.
                motionSource=MotionSource.Live;SetAutomaticGrouping(false);
                if(chapter!=null)chapter.Manual=true;
                if(Binding!=null&&(chapter==null||chapter==RotationChapter()))
                {EnsureManualEndpoints(initialLength);SyncMappingLength(Binding.RestLength,true);CaptureChapterCalibration();ProtectChapterCalibration();}
                status=L("直观调节：拖动起点或末端，玩家长度自动更新；入口跟随游戏绑定点。","Manual: edit either endpoint; player length updates automatically. Entry follows the game binding.");
            }
            Drive.Reset();Devices.Stop();MarkSceneDataDirty();Config.Save();SaveSceneData();
        }
        private void RecaptureRotationReference()
        {
            var chapter=RotationChapter();
            if(UseScripts||MappingMode!=AxisMappingMode.Relative||chapter==null||!ChapterRotationReference.CanCapture(Measured))return;
            RememberChapters();Devices.Stop();
            chapter.RotationReference=ChapterRotationReference.Capture(Measured,Clock.Time);
            MarkSceneDataDirty();SaveSceneData();
            status=L("已保存当前章节的旋转基准","Rotation reference saved for the current chapter");
        }
    }
}
