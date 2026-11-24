using System;
using System.Globalization;
using KoiSync.Core;
using UnityEngine;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    // The travel boost is one global configuration: it decides how far one stroke travels for every
    // animation. A single animation can still narrow its own six axes in 3D 对齐 · 更多轴范围配置.
    public sealed partial class KoiSyncPanel
    {
        private GameObject strokePage;
        private Text strokeContext,strokeNotice,strokeNormalValue,strokeDeepValue,strokeClimaxTravelValue,strokeFrequencyValue,strokeClimaxRangeValue;
        private Toggle strokeAuto,strokeClimax;
        private Dropdown strokeClimaxMode;
        private Slider strokeNormalSlider,strokeDeepSlider,strokeClimaxTravelSlider,strokeFrequencySlider;
        private RangeSlider strokeClimaxRange;
        private bool strokeLoading,globalDirty;
        private NativeStrokeProfile StrokeSettings{get{return host.Global.Stroke;}}
        private void ShowStrokeSettings()
        {
            if(strokePage!=null){ActivatePage(WorkspacePage.Stroke);LoadStrokeSettings();return;}
            var page=CreatePage(WorkspacePage.Stroke);strokePage=page.gameObject;
            Label(page,"动画行程 · 全局配置",24,18,700,38,24,Accent,FontStyle.Bold);
            strokeContext=Label(page,"",24,58,948,34,13,Muted);
            var boost=Panel(page,20,100,948,232,Card);
            strokeAuto=Toggle(boost,"循环均值不足时自动补强",16,14,620,true,v=>{if(strokeLoading)return;StrokeSettings.AutoRange=v;MarkGlobalChanged();});
            Label(boost,"普通循环平均行程",16,48,320,28,16,Accent);
            strokeNormalValue=ValueLabel(boost,760,48,120);
            strokeNormalSlider=Slider(boost,330,52,410,StrokeSettings.NormalTravel/999f,v=>{if(strokeLoading)return;StrokeSettings.NormalTravel=Mathf.RoundToInt(v*999);strokeNormalValue.text=StrokeSettings.NormalTravel.ToString();MarkGlobalChanged();});
            Label(boost,"高强度平均行程",16,88,320,28,16,Accent);
            strokeDeepValue=ValueLabel(boost,760,88,120);
            strokeDeepSlider=Slider(boost,330,92,410,StrokeSettings.DeepTravel/999f,v=>{if(strokeLoading)return;StrokeSettings.DeepTravel=Mathf.RoundToInt(v*999);strokeDeepValue.text=StrokeSettings.DeepTravel.ToString();MarkGlobalChanged();});
            Label(boost,"行程 = 一次运动的局部极大值 − 局部极小值；补强把多次行程的平均值拉到目标以上。\n已达标的循环保持原值，补强沿原动作比例变化；这里的改动影响所有动画。\nL1 / L2 映射跨度属于单个动画，在 3D 对齐 的面宽 / 面深 / L1-2 一行里调整。",16,136,916,86,12,Muted);
            var climax=Panel(page,20,342,948,250,Card);
            strokeClimax=Toggle(climax,"启用高潮模拟",16,12,620,true,v=>{if(strokeLoading)return;StrokeSettings.ClimaxEnabled=v;MarkGlobalChanged();});
            Label(climax,"每秒换向次数",16,46,320,28,16,Accent);
            strokeFrequencyValue=ValueLabel(climax,760,46,120);
            strokeFrequencySlider=Slider(climax,330,52,410,(float)((StrokeSettings.ClimaxChangesPerSecond-1)/19),v=>{if(strokeLoading)return;StrokeSettings.ClimaxChangesPerSecond=Math.Round(1+v*19,1);strokeFrequencyValue.text=StrokeSettings.ClimaxChangesPerSecond.ToString("0.#",CultureInfo.InvariantCulture);MarkGlobalChanged();});
            Label(climax,"高潮模式",16,84,320,28,16,Accent);
            strokeClimaxMode=Dropdown(climax,330,78,410,new[]{"跟随游戏 · 行程自定义","完全自定义"},i=>{if(strokeLoading)return;StrokeSettings.ClimaxFollowsGame=i==0;MarkGlobalChanged();});
            Label(climax,"高潮行程",16,122,320,28,16,Accent);
            strokeClimaxTravelValue=ValueLabel(climax,760,122,120);
            strokeClimaxTravelSlider=Slider(climax,330,128,410,StrokeSettings.ClimaxTravel/999f,v=>{if(strokeLoading)return;StrokeSettings.ClimaxTravel=Mathf.RoundToInt(v*999);strokeClimaxTravelValue.text=StrokeSettings.ClimaxTravel.ToString();MarkGlobalChanged();});
            Label(climax,"高潮往返",16,160,320,28,16,Accent);
            strokeClimaxRangeValue=ValueLabel(climax,760,160,120);
            strokeClimaxRange=Range(climax,330,166,410,StrokeSettings.ClimaxMinimum/999f,StrokeSettings.ClimaxMaximum/999f,(lo,hi)=>{if(strokeLoading)return;StrokeSettings.ClimaxMinimum=Mathf.RoundToInt(lo*999);StrokeSettings.ClimaxMaximum=Mathf.RoundToInt(hi*999);strokeClimaxRangeValue.text=StrokeSettings.ClimaxMinimum+" — "+StrokeSettings.ClimaxMaximum;MarkGlobalChanged();});
            strokeClimaxRange.Finished=()=>MarkGlobalChanged();
            Label(climax,"跟随游戏：测量高潮动作本身的行程并补强，端点与频率不参与；完全自定义：按上面的端点与每秒换向次数生成往返。\n未达目标的循环才补强，静止不生成运动，最终仍受设备上下限约束。",16,198,916,46,12,Muted);
            Button(page,"六轴数值",24,600,218,37,ToggleHud);
            Button(page,"动画时间轴",259,600,218,37,ToggleMotionTimeline);
            Button(page,"恢复默认行程",494,600,218,37,ResetGlobalStroke);
            strokeNotice=Label(page,"",729,606,239,32,11,Muted);
            LoadStrokeSettings();
        }
        private Text ValueLabel(Transform parent,float x,float y,float w)
        {
            Text text=Label(parent,"",x,y,w,26,15,Accent);text.alignment=TextAnchor.MiddleRight;return text;
        }
        private void ResetGlobalStroke()
        {
            host.Global.ResetStroke();host.Profiles.PushGlobalStroke(host.Global);LoadStrokeSettings();
            if(strokeNotice!=null)strokeNotice.text="已恢复默认行程配置。";
        }
        private void MarkGlobalChanged()
        {
            globalDirty=true;
            if(strokeNotice!=null)strokeNotice.text=host.IsMainGame?"已修改 · 松开后保存（影响所有动画）":"已修改 · 松开后保存";
        }
        // Sliders stream while the pointer is held: preview immediately, write the configuration once the
        // pointer is released so a drag does not rewrite the file on every frame.
        internal void FlushGlobalSettings()
        {
            if(!globalDirty)return;
            try
            {
                host.Profiles.PushGlobalStroke(host.Global);host.Global.ApplyTo(host.Pipeline);
                if(!UnityEngine.Input.GetMouseButton(0))
                {
                    globalDirty=false;host.Global.Save();
                    host.Global.Status="已保存全局配置";
                    RefreshTransitions();RefreshChannelBanks();
                }
            }
            catch(Exception ex){globalDirty=false;host.Global.Status="全局配置保存失败："+ex.GetBaseException().Message;}
        }
        private void LoadStrokeSettings()
        {
            var p=StrokeSettings;strokeLoading=true;
            strokeAuto.isOn=p.AutoRange;strokeClimax.isOn=p.ClimaxEnabled;
            strokeNormalSlider.value=p.NormalTravel/999f;strokeNormalValue.text=p.NormalTravel.ToString();
            strokeDeepSlider.value=p.DeepTravel/999f;strokeDeepValue.text=p.DeepTravel.ToString();
            strokeClimaxMode.value=p.ClimaxFollowsGame?0:1;strokeClimaxMode.RefreshShownValue();
            strokeClimaxTravelSlider.value=p.ClimaxTravel/999f;strokeClimaxTravelValue.text=p.ClimaxTravel.ToString();
            strokeFrequencySlider.value=(float)((p.ClimaxChangesPerSecond-1)/19);strokeFrequencyValue.text=p.ClimaxChangesPerSecond.ToString("0.#",CultureInfo.InvariantCulture);
            strokeClimaxRange.SetRange(p.ClimaxMinimum/999f,p.ClimaxMaximum/999f);strokeClimaxRangeValue.text=p.ClimaxMinimum+" — "+p.ClimaxMaximum;
            strokeLoading=false;
            if(strokeNotice!=null)strokeNotice.text="全局配置 · 影响所有动画";
            RefreshStrokeSettings();
        }
        internal void RefreshStrokeSettings()
        {
            if(strokePage==null||!strokePage.activeInHierarchy)return;
            bool current=host.Profiles.NativeKey.Length>0;
            strokeContext.text=(current?host.Profiles.NativeKey:"等待本体动画")+"\n影响所有动画；单个动画可在 3D 对齐 → 更多轴范围 里单独收窄 · "+host.Global.Status;
        }
    }
}
