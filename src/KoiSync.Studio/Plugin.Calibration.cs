using System;
using System.Globalization;
using BepInEx.Configuration;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        private readonly ModelOrigins modelOrigins=new ModelOrigins();
        private ConfigEntry<float> l1RangeSetting,l2RangeSetting,mappingLengthSetting;
        private ConfigEntry<bool> linkLengthSetting;
        private double l1HalfRange,l2HalfRange,mappingLength;
        private string l1RangeText,l2RangeText;
        private readonly string[] originText={"0","0","0"};
        private bool showCalibrationHandles=true,mappingDirty;

        private void InitializeCalibration()
        {
            float initial=lengthSetting.Value>=20?lengthSetting.Value*.5f:70;
            l1RangeSetting=Config.Bind("Mapping","L1HalfRangeMm",initial,"Distance from centre to one L1 endpoint, mm; independent of penis length");
            l2RangeSetting=Config.Bind("Mapping","L2HalfRangeMm",initial,"Distance from centre to one L2 endpoint, mm; independent of penis length");
            linkLengthSetting=Config.Bind("Mapping","ScaleWithLength",true,"Scale L1/L2 mapping ranges proportionally when penis length changes");
            mappingLengthSetting=Config.Bind("Mapping","LengthReferenceMm",initial*2,"Reference length used for proportional L1/L2 calibration");
            mappingLength=mappingLengthSetting.Value>=20&&mappingLengthSetting.Value<=500?mappingLengthSetting.Value/1000.0:initial/500.0;
            l1HalfRange=ValidHalfRange(l1RangeSetting.Value/1000.0)?l1RangeSetting.Value/1000.0:initial/1000.0;
            l2HalfRange=ValidHalfRange(l2RangeSetting.Value/1000.0)?l2RangeSetting.Value/1000.0:initial/1000.0;
            RefreshRangeText();
        }
        private static bool ValidHalfRange(double metres){return Numbers.Finite(metres)&&metres>=.001&&metres<=.5;}
        private void RefreshRangeText(){l1RangeText=(l1HalfRange*1000).ToString("G6",CultureInfo.InvariantCulture);l2RangeText=(l2HalfRange*1000).ToString("G6",CultureInfo.InvariantCulture);}
        private void ApplyBindingCalibration()
        {
            if(Binding==null)return;
            manualR0PlaneDegrees=manualR1PlaneDegrees=manualR2PlaneDegrees=0;
            var chapter=Book.At(Clock.Time);
            if(chapter!=null&&chapter.Source==Key(Binding.Source)&&chapter.Target==Key(Binding.Target)&&chapter.Opening==(int)Binding.Kind&&chapter.Calibration!=null)
            {ApplyChapterCalibration(chapter.Calibration);return;}
            calibrationApplying=true;
            try{SyncMappingLength(Binding.RestLength,true,false);}finally{calibrationApplying=false;}
            Binding.L1HalfRange=l1HalfRange;Binding.L2HalfRange=l2HalfRange;
            rodMillimetres=(Binding.RestLength*1000).ToString("G6",CultureInfo.InvariantCulture);
            Binding.OriginOffset=Binding.IsHandSource?new MotionVector():modelOrigins.Get(Key(Binding.Source));RefreshOriginText();
        }
        private void RefreshOriginText()
        {
            var value=Binding==null?new MotionVector():Binding.OriginOffset;
            originText[0]=(value.X*1000).ToString("G6",CultureInfo.InvariantCulture);
            originText[1]=(value.Y*1000).ToString("G6",CultureInfo.InvariantCulture);
            originText[2]=(value.Z*1000).ToString("G6",CultureInfo.InvariantCulture);
        }
        private void SetMappingRanges(double l1,double l2,bool save,bool stopOutput=true)
        {
            if(!ValidHalfRange(l1)||!ValidHalfRange(l2))throw new ArgumentException(L("L1/L2 单侧范围须为 1–500 mm","L1/L2 half ranges must be 1–500 mm"));
            l1HalfRange=l1;l2HalfRange=l2;RefreshRangeText();
            if(Binding!=null){Binding.L1HalfRange=l1;Binding.L2HalfRange=l2;}
            if(stopOutput){Devices.Stop();ProtectChapterCalibration();}Drive.Reset();mappingDirty=true;
            if(save)SaveMappingRanges();
        }
        private void SaveMappingRanges()
        {l1RangeSetting.Value=(float)(l1HalfRange*1000);l2RangeSetting.Value=(float)(l2HalfRange*1000);mappingLengthSetting.Value=(float)(mappingLength*1000);Config.Save();mappingDirty=false;CaptureChapterCalibration();}
        private void SyncMappingLength(double length,bool save,bool stopOutput=true)
        {
            if(!Numbers.Finite(length)||length<=0||Math.Abs(length-mappingLength)<1e-9)return;
            double ratio=length/mappingLength;mappingLength=length;
            if(linkLengthSetting.Value)SetMappingRanges(Numbers.Clamp(l1HalfRange*ratio,.001,.5),Numbers.Clamp(l2HalfRange*ratio,.001,.5),false,stopOutput);
            mappingDirty=true;if(save)SaveMappingRanges();
        }
        private void ApplyMappingText()
        {
            try{SetMappingRanges(ParseMillimetres(l1RangeText)/1000,ParseMillimetres(l2RangeText)/1000,true);status=L("L1/L2 映射范围已应用","L1/L2 mapping ranges applied");}
            catch(Exception error){status=error.Message;}
        }
        private static double ParseMillimetres(string text)
        {
            text=text.Trim();if(text.EndsWith("mm",StringComparison.OrdinalIgnoreCase))text=text.Substring(0,text.Length-2).Trim();
            double value;if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||!Numbers.Finite(value))throw new ArgumentException(L("请输入有效的毫米数值","Enter a finite value in millimetres"));return value;
        }
        private void SetOriginOffset(MotionVector offset)
        {
            if(Binding==null)return;
            double manualLength=0;
            if(ManualCalibrationActive)
            {EnsureManualEndpoints(Binding.RestLength);manualLength=ManualCalibrationMotion.Length(offset,Binding.ManualEndOffset.Value);}
            if(!Binding.IsHandSource)modelOrigins.Set(Key(Binding.Source),offset);Binding.OriginOffset=offset;Binding.ResetTransientMotion();Drive.Reset();
            if(ManualCalibrationActive)
            {Binding.SetLength(manualLength);SyncManualPlayerLength();SyncMappingLength(manualLength,true);}
            else Binding.ManualEndOffset=null;
            Devices.Stop();RefreshOriginText();CaptureChapterCalibration();ProtectChapterCalibration();
        }
        private void ApplyOriginText()
        {
            try{SetOriginOffset(new MotionVector(ParseMillimetres(originText[0])/1000,ParseMillimetres(originText[1])/1000,ParseMillimetres(originText[2])/1000));status=L("起点偏移已应用；保存场景可保留","Origin offset applied; save the scene to keep it");}
            catch(Exception error){status=error.Message=="Invalid model origin offset"?L("起点各方向偏移须在 ±500 mm 内","Origin offsets must be within ±500 mm"):error.Message;}
        }
        private void ResetModelLength()
        {
            if(Binding==null)return;
            try
            {
                double length=Binding.ReadModelLength();
                if(ManualCalibrationActive){ResetManualEndToModel(true);length=Binding.RestLength;}
                else
                {
                    Binding.ManualEndOffset=null;Binding.SetLength(length);SyncMappingLength(length,true);
                    if(strokeMappingSetting.Value&&!Binding.IsHandSource)SetPlayerLength(length,true);
                }
                rodMillimetres=(length*1000).ToString("G6",CultureInfo.InvariantCulture);lengthSetting.Value=(float)(length*1000);
                Devices.Stop();Drive.Reset();Config.Save();CaptureChapterCalibration();
                status=Binding.IsHandSource?L("手指链长已重置；玩家长度保持不变","Finger length reset; player length unchanged"):L("阴茎长度已按当前模型骨骼链重置并固定","Penis length captured from the current model bone chain");
            }
            catch(Exception error){status=error.Message;}
        }
        private void ResetModelOrigin()
        {if(Binding==null)return;SetOriginOffset(new MotionVector());status=L("阴茎起点已回到实际骨骼根部","Penis origin restored to the actual bone root");}

        private void DrawCalibrationPage()
        {
            DrawModelCard(new Rect(20,0,478,206));
            DrawMappingCard(new Rect(20,216,478,232));
            DrawStrokeSettings(new Rect(516,0,478,206));
            DrawManualPlaneAngles(new Rect(516,216,478,128));
            DrawPreviewCard(new Rect(516,354,478,94));
            if(mappingDirty&&(Event.current.rawType==EventType.MouseUp||Event.current.type==EventType.KeyUp))SaveMappingRanges();
        }
        private void DrawModelCard(Rect card)
        {
            float top=Card(card,L("模型与起点","Model and origin"));float x=card.x+12,w=card.width-24;
            GUI.Label(new Rect(x,top+2,200,26),ManualCalibrationActive?L("阴茎长度（起点 → 末端）mm","Penis length (origin → tip) mm"):Binding!=null&&Binding.IsHandSource?L("手指链长 mm","Finger length mm"):L("模型长度 mm","Model length mm"),label);
            rodMillimetres=Text(new Rect(x+200,top,120,28),rodMillimetres,12);
            if(B(new Rect(x+328,top,w-328,28),UiText.T("应用长度"),primaryButton))ApplyLength();
            GUI.enabled=Binding!=null;
            float half=(w-8)/2;
            if(B(new Rect(x,top+36,half,28),ManualCalibrationActive?L("重置末端到实际位置","Reset tip to model"):L("恢复模型长度","Reset model length")))ResetModelLength();
            if(B(new Rect(x+half+8,top+36,half,28),L("重置起始位置到实际位置","Reset origin to model")))ResetModelOrigin();
            GUI.enabled=true;
            GUI.Label(new Rect(x,top+72,w,22),L("起点相对实际骨骼根部的偏移（mm）","Origin offset from the actual bone root (mm)"),small);
            GUI.enabled=Binding!=null;
            var names=new[]{L("轴向","Axial"),"L1","L2"};
            for(int i=0;i<3;i++){float fx=x+i*118;GUI.Label(new Rect(fx,top+99,44,24),names[i],small);originText[i]=Text(new Rect(fx+44,top+96,68,28),originText[i],12);}
            if(B(new Rect(x+358,top+96,w-358,28),L("应用起点","Apply origin")))ApplyOriginText();
            GUI.enabled=true;
            GUI.Label(new Rect(x,top+130,w,40),ManualCalibrationActive?L("起点与末端沿同一轴线拖动；入口跟随游戏绑定点。","Drag origin and tip on the same axis; entry follows the game binding."):L("末端沿轴向调长度；起点偏移随角色运动，并随场景保存。","Tip adjusts length. Origin offsets follow the actor and save with the scene."),wrapSmall);
        }
        private void DrawMappingCard(Rect card)
        {
            float top=Card(card,L("L1 / L2 映射范围","L1 / L2 mapping range"));float x=card.x+12,w=card.width-24;
            showCalibrationHandles=GUI.Toggle(new Rect(x,top,w,24),showCalibrationHandles,L("显示拖动手柄：阴茎末端、起点、L1/L2 范围","Show handles: penis tip, origin and L1/L2 ranges"),toggle);
            DrawMappingRow(1,x,top+30);DrawMappingRow(2,x,top+64);
            if(B(new Rect(x+w-96,top+30,96,62),L("应用范围","Apply ranges")))ApplyMappingText();
            bool linked=GUI.Toggle(new Rect(x,top+102,w,24),linkLengthSetting.Value,L("阴茎长度联动 L1/L2 范围（保留各轴比例）","Link L1/L2 ranges to penis length (keep each axis ratio)"),toggle);
            if(linked!=linkLengthSetting.Value){linkLengthSetting.Value=linked;mappingLength=Binding!=null?Binding.RestLength:lengthSetting.Value>0?lengthSetting.Value/1000.0:mappingLength;SaveMappingRanges();}
            GUI.Label(new Rect(x,top+134,w,40),L("± 范围：中心到单侧满行程的距离；范围越小，响应越大。","± range: centre to one endpoint. Smaller range gives more response."),wrapSmall);
        }
        private void DrawManualPlaneAngles(Rect card)
        {
            float top=Card(card,L("旋转平面 · 手动模式","Rotation plane · manual mode"));float x=card.x+12;
            bool active=ManualCalibrationActive&&RotationChapter()!=null;GUI.enabled=active;
            var names=new[]{L("R0 扭转","R0 twist"),L("R1 侧倾","R1 roll"),L("R2 俯仰","R2 pitch")};
            for(int i=0;i<3;i++)
            {
                float row=top+i*30;double current=i==0?manualR0PlaneDegrees:i==1?manualR1PlaneDegrees:manualR2PlaneDegrees;
                float limit=i==0?180:90;GUI.Label(new Rect(x,row,84,26),names[i],small);
                double next=Math.Round(GUI.HorizontalSlider(new Rect(x+86,row+6,224,20),(float)current,-limit,limit),1);
                if(Math.Abs(next-current)>.049)SetManualPlane(i,next);
                GUI.Label(new Rect(x+318,row,60,26),current.ToString("F1",CultureInfo.InvariantCulture)+"°",small);
                if(B(new Rect(x+378,row-1,76,26),L("归零","Reset")))SetManualPlane(i,0);
            }
            GUI.enabled=true;
        }
        private void DrawPreviewCard(Rect card)
        {
            float top=Card(card,L("显示 / 预览","Display / preview"));float x=card.x+12;
            bool models=GUI.Toggle(new Rect(x,top,140,24),showPreview,UiText.T("预览模型"),toggle);
            if(models!=showPreview){showPreview=models;previewSetting.Value=models;if(!models)preview.Hide();}
            bool through=GUI.Toggle(new Rect(x+150,top,140,24),xray,UiText.T("透视显示"),toggle);if(through!=xray){xray=through;xraySetting.Value=xray;}
            DrawModelPreviewSource(new Rect(x+296,top-2,158,28),new Rect(x,top+28,card.width-24,22));
        }
        private void DrawMappingRow(int axis,float x,float y)
        {
            GUI.Label(new Rect(x,y+3,96,25),axis==1?L("L1 前后 ±","L1 F/B ±"):L("L2 左右 ±","L2 L/R ±"),label);
            string value=axis==1?l1RangeText:l2RangeText;
            value=Text(new Rect(x+96,y,70,28),value,12);if(axis==1)l1RangeText=value;else l2RangeText=value;
            GUI.Label(new Rect(x+170,y+4,30,24),"mm",small);
            double current=axis==1?l1HalfRange:l2HalfRange;
            double updated=Math.Round(GUI.HorizontalSlider(new Rect(x+204,y+7,140,20),(float)(current*1000),1,500),1)/1000;
            if(Math.Abs(updated-current)>.000051)SetMappingRanges(axis==1?updated:l1HalfRange,axis==2?updated:l2HalfRange,false);
        }
    }
}
