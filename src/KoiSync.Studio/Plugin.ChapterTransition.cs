using System;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> transitionSetting;
        private ConfigEntry<float> transitionMsSetting,transitionThresholdSetting;
        private readonly ChapterTransition transition=new ChapterTransition();
        // Final six-axis output after the chapter transition. Drive.Values
        // stays the live filter state, so the fade never feeds back into it.
        private readonly double[] outputValues={0,.5,.5,.5,.5,.5};
        private Chapter transitionChapter;
        private bool transitionHasChapter;

        private void InitializeChapterTransition()
        {
            transitionSetting=Config.Bind("Output","ChapterTransition",true,"Fade sudden SR6 output jumps where playback crosses into another chapter.");
            transitionMsSetting=Config.Bind("Output","ChapterTransitionMs",500f,new ConfigDescription("Fade duration in ms.",new AcceptableValueRange<float>(100,3000)));
            transitionThresholdSetting=Config.Bind("Output","ChapterTransitionThresholdPercent",8f,new ConfigDescription("Fade only when an axis jumps by more than this percentage of its full range.",new AcceptableValueRange<float>(1,50)));
        }
        private void ResetChapterTransition(){transition.Reset();transitionChapter=null;transitionHasChapter=false;}
        // Called once per valid output frame, after live/script/climax values.
        private void UpdateOutputTransition(double dt)
        {
            Array.Copy(Drive.Values,outputValues,6);
            var chapter=Clock.Available?Book.At(Clock.Time):null;
            // Compare with the chapter of the previous valid output: a disabled
            // chapter stops output while the device holds its last position.
            bool boundary=(transitionHasChapter&&chapter!=transitionChapter)||ConsumeDirectorTransitionBoundary();
            transitionChapter=chapter;transitionHasChapter=true;
            if(!transitionSetting.Value){transition.Reset();return;}
            transition.Seconds=transitionMsSetting.Value/1000.0;transition.Threshold=transitionThresholdSetting.Value/100.0;
            transition.Apply(outputValues,dt,boundary,!UseScripts&&climaxPulse!=null);
            for(int i=0;i<6;i++)
            {
                double low=i==0?Math.Max(Drive.Minimum[i],Drive.PhysicalL0Minimum):Drive.Minimum[i];
                low=Math.Min(low,Drive.Maximum[i]);
                outputValues[i]=Math.Max(low,Math.Min(Drive.Maximum[i],outputValues[i]));
            }
        }
        private string TransitionStatus()
        {
            if(!transitionSetting.Value)return L("已关闭 · 章节切换时直接输出新姿态","Off · chapter changes output the new pose directly");
            if(transition.Active)return L("正在平滑过渡 · ","Fading · ")+Math.Round(transition.Progress*100)+"%";
            return L("待命 · 上次章节切换突变 ","Ready · last chapter jump ")+Math.Round(transition.LastJump*100)+"%";
        }
        private void DrawChapterTransitionSettings(Rect card)
        {
            float top=Card(card,L("章节过渡平滑","Chapter transition smoothing"));float x=card.x+12,w=card.width-24;
            bool enabled=GUI.Toggle(new Rect(x,top,w,24),transitionSetting.Value,L("章节切换时平滑 SR6 输出突变","Smooth sudden SR6 output jumps at chapter changes"),toggle);
            if(enabled!=transitionSetting.Value){transitionSetting.Value=enabled;transition.Reset();Config.Save();}
            GUI.enabled=enabled;
            DrawSliderSetting(top+32,L("过渡时长 ms","Fade duration ms"),transitionMsSetting,100,3000,10,"ms");
            DrawSliderSetting(top+66,L("触发阈值 %","Trigger threshold %"),transitionThresholdSetting,1,50,1,"%");
            GUI.enabled=true;
            GUI.Label(new Rect(x,top+100,w,40),L("任一轴在章节切换处的突变超过阈值时，从上一章的输出平滑过渡到新章节动作；新章节的动作照常叠加。作用于设备发送、六轴窗和录制；固定频率高潮振动的 L0 保持原样。","When any axis jumps by more than the threshold at a chapter change, output fades from the previous chapter into the new chapter's motion, which continues underneath. Applies to device output, the six-axis display and recording; fixed-frequency climax L0 stays unchanged."),wrapSmall);
            GUI.Label(new Rect(x,top+142,w,22),TransitionStatus(),small);
        }
        private void DrawSliderSetting(float y,string name,ConfigEntry<float> setting,float minimum,float maximum,float step,string unit)
        {
            GUI.Label(new Rect(32,y+2,300,28),name,label);
            float previous=setting.Value;
            float value=Mathf.Round(GUI.HorizontalSlider(new Rect(340,y+9,500,20),previous,minimum,maximum)/step)*step;
            if(Math.Abs(value-previous)>.001f)setting.Value=Mathf.Clamp(value,minimum,maximum);
            GUI.Label(new Rect(856,y+4,126,28),setting.Value.ToString("0.#",CultureInfo.InvariantCulture)+" "+unit,small);
        }
    }
}
