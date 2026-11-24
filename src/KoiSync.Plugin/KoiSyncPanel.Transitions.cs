using System;
using System.Globalization;
using KoiSync.Core;
using UnityEngine;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    // Pull-out and no-interaction configuration. Both are global: the pull-out ceiling and the idle /
    // climax sweeps behave the same in every animation, so they live on one page with no per-animation
    // save step. Every value is a slider, and a value that has two ends uses one dual-handle slider.
    public sealed partial class KoiSyncPanel
    {
        private GameObject transitionsPage;
        private Text transitionsStatus,transitionsNotice,outCeilingValue,idlePeriodValue,idleRangeValue,idleRotationValue,climaxCeilingValue,climaxPeriodValue,climaxRotationValue;
        private Toggle outLimitToggle,idleMotionToggle,idleFollowToggle,idleRotationToggle,climaxPullToggle,climaxRotationToggle;
        private Slider outCeilingSlider,idlePeriodSlider,climaxCeilingSlider,climaxPeriodSlider;
        private RangeSlider idleRange,idleRotationRange,climaxRotationRange;
        private bool transitionsLoading;
        private NativeStrokeProfile TransitionSettings{get{return host.Global.Stroke;}}
        private void ShowTransitions()
        {
            if(transitionsPage!=null){ActivatePage(WorkspacePage.Transitions);LoadTransitionSettings();return;}
            var page=CreatePage(WorkspacePage.Transitions);transitionsPage=page.gameObject;
            Label(page,"拔出动 / 无交互 · 全局配置",24,18,700,38,24,Accent,FontStyle.Bold);
            transitionsStatus=Label(page,"",24,56,948,28,13,Muted);
            Label(page,"拔出动 · 阴茎离开阴道 / 口 / 胸 / 手",16,88,916,24,15,Accent);
            outLimitToggle=Toggle(page,"限制 L0 上限（默认开启，避免同步过程中从玩家身上脱出）",16,114,916,true,v=>{if(transitionsLoading)return;TransitionSettings.OutLimitEnabled=v;MarkGlobalChanged();});
            Label(page,"L0 上限",16,140,320,28,16,Accent);
            outCeilingValue=ValueLabel(page,760,140,120);
            outCeilingSlider=Slider(page,330,146,410,TransitionSettings.OutCeiling/999f,v=>{if(transitionsLoading)return;TransitionSettings.OutCeiling=Mathf.RoundToInt(v*999);outCeilingValue.text=TransitionSettings.OutCeiling.ToString();MarkGlobalChanged();});
            Label(page,"无交互待机 · Idle / 结束状态",16,180,916,24,15,Accent);
            idleMotionToggle=Toggle(page,"保持小幅往返运动（调整体位时不会因分神而疲软）",16,206,916,true,v=>{if(transitionsLoading)return;TransitionSettings.IdleMotionEnabled=v;MarkGlobalChanged();});
            idleFollowToggle=Toggle(page,"频率跟随动画（关闭则使用自定义周期）",16,232,916,true,v=>{if(transitionsLoading)return;TransitionSettings.IdleFollowsGame=v;MarkGlobalChanged();});
            Label(page,"往返周期 s",16,258,320,28,16,Accent);
            idlePeriodValue=ValueLabel(page,760,258,120);
            idlePeriodSlider=Slider(page,330,264,410,(float)((TransitionSettings.IdlePeriod-.2)/9.8),v=>{if(transitionsLoading)return;TransitionSettings.IdlePeriod=.2+v*9.8;idlePeriodValue.text=TransitionSettings.IdlePeriod.ToString("0.##",CultureInfo.InvariantCulture);MarkGlobalChanged();});
            Label(page,"L0 范围",16,296,320,28,16,Accent);
            idleRangeValue=ValueLabel(page,760,296,120);
            idleRange=Range(page,330,302,410,TransitionSettings.IdleMinimum/999f,TransitionSettings.IdleMaximum/999f,(lo,hi)=>{if(transitionsLoading)return;TransitionSettings.IdleMinimum=Mathf.RoundToInt(lo*999);TransitionSettings.IdleMaximum=Mathf.RoundToInt(hi*999);idleRangeValue.text=TransitionSettings.IdleMinimum+" — "+TransitionSettings.IdleMaximum;MarkGlobalChanged();});
            idleRotationToggle=Toggle(page,"R0 / R1 同步摆动",16,334,916,true,v=>{if(transitionsLoading)return;TransitionSettings.IdleRotationEnabled=v;MarkGlobalChanged();});
            Label(page,"R0 / R1 范围",16,360,320,28,16,Accent);
            idleRotationValue=ValueLabel(page,760,360,120);
            idleRotationRange=Range(page,330,366,410,TransitionSettings.IdleRotationMinimum/999f,TransitionSettings.IdleRotationMaximum/999f,(lo,hi)=>{if(transitionsLoading)return;TransitionSettings.IdleRotationMinimum=Mathf.RoundToInt(lo*999);TransitionSettings.IdleRotationMaximum=Mathf.RoundToInt(hi*999);idleRotationValue.text=TransitionSettings.IdleRotationMinimum+" — "+TransitionSettings.IdleRotationMaximum;MarkGlobalChanged();});
            Label(page,"射精 / 高潮拉低 · 游戏本身会让部分动画推向 999",16,400,916,24,15,Accent);
            climaxPullToggle=Toggle(page,"把 L0 压在低位，保持紧包裹",16,426,916,true,v=>{if(transitionsLoading)return;TransitionSettings.ClimaxPullInEnabled=v;MarkGlobalChanged();});
            Label(page,"L0 上限",16,452,320,28,16,Accent);
            climaxCeilingValue=ValueLabel(page,760,452,120);
            climaxCeilingSlider=Slider(page,330,458,410,TransitionSettings.ClimaxCeiling/999f,v=>{if(transitionsLoading)return;TransitionSettings.ClimaxCeiling=Mathf.RoundToInt(v*999);climaxCeilingValue.text=TransitionSettings.ClimaxCeiling.ToString();MarkGlobalChanged();});
            climaxRotationToggle=Toggle(page,"R0 / R1 快速摆动（补充刺激）",16,490,916,true,v=>{if(transitionsLoading)return;TransitionSettings.ClimaxRotationEnabled=v;MarkGlobalChanged();});
            Label(page,"周期 s",16,516,320,28,16,Accent);
            climaxPeriodValue=ValueLabel(page,760,516,120);
            climaxPeriodSlider=Slider(page,330,522,410,(float)((TransitionSettings.ClimaxRotationPeriod-.1)/9.9),v=>{if(transitionsLoading)return;TransitionSettings.ClimaxRotationPeriod=.1+v*9.9;climaxPeriodValue.text=TransitionSettings.ClimaxRotationPeriod.ToString("0.##",CultureInfo.InvariantCulture);MarkGlobalChanged();});
            Label(page,"R0 / R1 范围",16,554,320,28,16,Accent);
            climaxRotationValue=ValueLabel(page,760,554,120);
            climaxRotationRange=Range(page,330,560,410,TransitionSettings.ClimaxRotationMinimum/999f,TransitionSettings.ClimaxRotationMaximum/999f,(lo,hi)=>{if(transitionsLoading)return;TransitionSettings.ClimaxRotationMinimum=Mathf.RoundToInt(lo*999);TransitionSettings.ClimaxRotationMaximum=Mathf.RoundToInt(hi*999);climaxRotationValue.text=TransitionSettings.ClimaxRotationMinimum+" — "+TransitionSettings.ClimaxRotationMaximum;MarkGlobalChanged();});
            Button(page,"六轴数值",24,596,218,37,ToggleHud);
            Button(page,"恢复默认",259,596,218,37,ResetTransitionSettings);
            Button(page,"返回动画行程",494,596,218,37,ShowStrokeSettings);
            transitionsNotice=Label(page,"",729,602,239,30,11,Muted);
            LoadTransitionSettings();
        }
        private void ResetTransitionSettings()
        {
            host.Global.ResetStroke();host.Profiles.PushGlobalStroke(host.Global);LoadTransitionSettings();
            transitionsNotice.text="已恢复默认拔出 / 待机 / 高潮配置。";
        }
        private void LoadTransitionSettings()
        {
            var p=TransitionSettings;transitionsLoading=true;
            outLimitToggle.isOn=p.OutLimitEnabled;outCeilingSlider.value=p.OutCeiling/999f;outCeilingValue.text=p.OutCeiling.ToString();
            idleMotionToggle.isOn=p.IdleMotionEnabled;idleFollowToggle.isOn=p.IdleFollowsGame;
            idlePeriodSlider.value=(float)((p.IdlePeriod-.2)/9.8);idlePeriodValue.text=p.IdlePeriod.ToString("0.##",CultureInfo.InvariantCulture);
            idleRange.SetRange(p.IdleMinimum/999f,p.IdleMaximum/999f);idleRangeValue.text=p.IdleMinimum+" — "+p.IdleMaximum;
            idleRotationToggle.isOn=p.IdleRotationEnabled;
            idleRotationRange.SetRange(p.IdleRotationMinimum/999f,p.IdleRotationMaximum/999f);idleRotationValue.text=p.IdleRotationMinimum+" — "+p.IdleRotationMaximum;
            climaxPullToggle.isOn=p.ClimaxPullInEnabled;climaxCeilingSlider.value=p.ClimaxCeiling/999f;climaxCeilingValue.text=p.ClimaxCeiling.ToString();
            climaxRotationToggle.isOn=p.ClimaxRotationEnabled;
            climaxPeriodSlider.value=(float)((p.ClimaxRotationPeriod-.1)/9.9);climaxPeriodValue.text=p.ClimaxRotationPeriod.ToString("0.##",CultureInfo.InvariantCulture);
            climaxRotationRange.SetRange(p.ClimaxRotationMinimum/999f,p.ClimaxRotationMaximum/999f);climaxRotationValue.text=p.ClimaxRotationMinimum+" — "+p.ClimaxRotationMaximum;
            transitionsLoading=false;
            if(transitionsNotice!=null)transitionsNotice.text="全局配置 · 影响所有动画";
            RefreshTransitions();
        }
        internal void RefreshTransitions()
        {
            if(transitionsStatus==null)return;
            transitionsStatus.text="影响所有动画 · 拔出上限、待机与高潮摆动都是全局配置";
        }
    }
}
