using System;
using BepInEx.Configuration;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        private ConfigEntry<int> modelPreviewSourceSetting;
        private readonly StudioDrive modelPreviewDrive=new StudioDrive();
        private bool modelPreviewPicker;
        private Rect modelPreviewPickerArea;
        private string modelPreviewStatus="";
        // Preserve the previous source until the player makes a choice. An
        // explicit choice remains independent of device/recording input.
        private bool PreviewUsesScript {get{return modelPreviewSourceSetting.Value<0?UseScripts:modelPreviewSourceSetting.Value==1;}}

        private void InitializeModelPreview()
        {
            modelPreviewSourceSetting=Config.Bind("View","ModelPreviewSource",-1,
                "Model preview only: 0 live binding/calibration, 1 recorded script. -1 preserves the current output source until explicitly selected. Does not change device or recording input.");
        }
        private void SetModelPreviewSource(int source)
        {
            modelPreviewSourceSetting.Value=source==1?1:0;Config.Save();
            UpdateModelPreview();
        }
        private void UpdateModelPreview()
        {
            if(preview==null)return;
            if(!showPreview||scenePending||Binding==null||Sample==null||Measured==null)
            {
                preview.Hide();modelPreviewStatus=showPreview?UiText.T("预览等待绑定"):UiText.T("预览已隐藏");return;
            }
            double length=MappingMode==AxisMappingMode.Manual?Measured.Length:strokeMappingSetting.Value?PlayerLength:Measured.Length;
            if(!PreviewUsesScript)
            {
                preview.DisplayLive(Sample,Measured,MappingMode==AxisMappingMode.Manual?ManualPlaneDegrees:new MotionVector(),showPreview,xray,length);
            }
            else
            {
                double[] values;
                if(!TrySampleScriptFrame(out values))
                {
                    preview.Hide();modelPreviewStatus=L("当前时间无脚本数据 · 请在保存 / 录制中选择脚本","No script at this time · choose one in Save / Record");return;
                }
                // Use a separate drive so previewing a script cannot replace
                // live output, reset its smoothing, or contaminate recording.
                Array.Copy(Drive.Minimum,modelPreviewDrive.Minimum,6);Array.Copy(Drive.Maximum,modelPreviewDrive.Maximum,6);
                modelPreviewDrive.PhysicalL0Minimum=Drive.PhysicalL0Minimum;
                modelPreviewDrive.UpdateScript(values);
                preview.Display(Sample,Measured,modelPreviewDrive.Values,showPreview,xray,length,
                    MappingMode==AxisMappingMode.Manual?Measured.Length:strokeMappingSetting.Value?DeviceStroke:double.NaN);
            }
            modelPreviewStatus=preview.Status;
        }
        private void DrawModelPreviewSource(Rect buttonRect,Rect statusRect)
        {
            string caption=PreviewUsesScript?L("脚本预览","Script preview"):L("实时预览","Live preview");
            if(BTip(buttonRect,caption+"  ▾",L("模型预览：","Model preview: ")+caption))
            {
                bool open=!modelPreviewPicker;ClosePopups();ClearUiTextFocus();modelPreviewPicker=open;
                // The control sits near the bottom of the page; open upwards.
                modelPreviewPickerArea=new Rect(buttonRect.x,ContentTop+buttonRect.y-70,buttonRect.width,68);
            }
            GUI.Label(statusRect,modelPreviewStatus,small);
        }
        private void DrawModelPreviewPicker()
        {
            var area=modelPreviewPickerArea;
            if(Event.current.type==EventType.MouseDown&&!area.Contains(Event.current.mousePosition)||Event.current.type==EventType.KeyDown&&Event.current.keyCode==KeyCode.Escape)
            {modelPreviewPicker=false;ClearUiTextFocus();Event.current.Use();return;}
            Popup(area);
            var names=new[]{L("实时预览","Live preview"),L("脚本预览","Script preview")};
            for(int i=0;i<names.Length;i++)if(B(new Rect(area.x+4,area.y+4+i*30,area.width-8,28),((PreviewUsesScript?1:0)==i?"●  ":"")+names[i]))
            {
                modelPreviewPicker=false;ClearUiTextFocus();SetModelPreviewSource(i);
                if(Event.current.type!=EventType.Used)Event.current.Use();return;
            }
        }
    }
}
