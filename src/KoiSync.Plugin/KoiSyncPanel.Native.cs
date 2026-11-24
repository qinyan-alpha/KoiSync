using System;
using System.Collections.Generic;
using System.Globalization;
using KoiSync.Core;
using UnityEngine;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    public sealed partial class KoiSyncPanel
    {
        private GameObject nativePage;
        private Text nativeStatus,nativeDetails,nativeNotice;
        private Dropdown nativeProfile;
        private NativeTarget[] nativeChoices;
        private readonly InputField[] nativeLow=new InputField[6],nativeHigh=new InputField[6];
        private readonly RangeSlider[] nativeRanges=new RangeSlider[6];
        private void ShowNativeSettings()
        {
            if(nativePage!=null){ActivatePage(WorkspacePage.Native);return;}
            RectTransform modal=CreatePage(WorkspacePage.Native);nativePage=modal.gameObject;
            Label(modal,"本体同步 · 动画与部位参考",22,17,870,36,23,Accent,FontStyle.Bold);
            nativeStatus=Label(modal,"",24,63,948,58,13,Muted);
            RectTransform mapping=Panel(modal,20,130,958,119,Card);
            Label(mapping,"动画 → 测量配置",16,12,400,26,16,Accent);
            var choices=new List<NativeTarget>();var labels=new List<string>();
            foreach(NativeTarget target in Enum.GetValues(typeof(NativeTarget)))if(target!=NativeTarget.BOTH_FEET){choices.Add(target);labels.Add(target==NativeTarget.AUTO?"AUTO · 按动画自动选择":target.ToString());}
            nativeChoices=choices.ToArray();
            bool initializing=true;
            nativeProfile=Dropdown(mapping,16,49,400,labels.ToArray(),i=>{if(initializing)return;host.PauseOutput("已切换本体测量配置");host.Native.Override.Value=nativeChoices[i];host.Pipeline.Reset("等待新的本体参考数据");});
            nativeProfile.value=Math.Max(0,Array.IndexOf(nativeChoices,host.Native.Override.Value));
            initializing=false;
            Button(mapping,"保存到当前动画",434,49,174,36,()=>{
                try{host.PauseOutput("已保存动画映射");NativeTarget chosen=host.Native.Override.Value;host.Native.SaveCurrent();var preset=host.Profiles.NativeMeasurement;if(preset!=null){preset.Target=chosen;host.Profiles.Changed(false);}nativeProfile.value=0;nativeNotice.text="已保存；AUTO 会使用当前动画的配置。";}
                catch(Exception ex){nativeNotice.text=ex.Message;}
            });
            Button(mapping,"重载映射",625,49,130,36,()=>{host.PauseOutput("已重载动画映射");host.Native.Reload();host.Pipeline.Reset("等待本体参考数据");});
            Label(mapping,"未知动画可手动选择后保存；SWAP 使用第二名目标角色。",16,91,910,22,11,Muted);
            RectTransform limits=Panel(modal,20,261,468,318,Card);
            Label(limits,"各轴限幅 · 000—999 · 双手柄",16,12,445,27,16,Accent);
            for(int i=0;i<6;i++)
            {
                int index=i;float y=49+i*35;
                Label(limits,MotionPipeline.Axes[i],16,y+7,30,25,13,Color.white);
                nativeLow[i]=Input(limits,48,y,56,Mathf.RoundToInt(host.Native.Minimum[i].Value*999).ToString());
                nativeHigh[i]=Input(limits,400,y,59,Mathf.RoundToInt(host.Native.Maximum[i].Value*999).ToString());
                nativeRanges[i]=Range(limits,111,y+2,280,host.Native.Minimum[i].Value,host.Native.Maximum[i].Value,(lo,hi)=>{
                    host.Native.Minimum[index].Value=lo;host.Native.Maximum[index].Value=hi;
                    nativeLow[index].text=Mathf.RoundToInt(lo*999).ToString();nativeHigh[index].text=Mathf.RoundToInt(hi*999).ToString();
                });
                nativeRanges[i].Finished=()=>host.Config.Save();
            }
            Button(limits,"应用六轴限幅",16,275,444,31,()=>{
                float[] lo=new float[6],hi=new float[6];
                for(int i=0;i<6;i++)if(!float.TryParse(nativeLow[i].text,NumberStyles.Float,CultureInfo.InvariantCulture,out lo[i])||!float.TryParse(nativeHigh[i].text,NumberStyles.Float,CultureInfo.InvariantCulture,out hi[i])||!Numbers.Finite(lo[i])||!Numbers.Finite(hi[i])||lo[i]<0||hi[i]>999||lo[i]>hi[i]){nativeNotice.text=MotionPipeline.Axes[i]+" 范围无效：要求 000 ≤ 最小值 ≤ 最大值 ≤ 999";return;}
                for(int i=0;i<6;i++){host.Native.Minimum[i].Value=lo[i]/999f;host.Native.Maximum[i].Value=hi[i]/999f;nativeRanges[i].SetRange(lo[i]/999f,hi[i]/999f);}host.Config.Save();nativeNotice.text="限幅已保存；超出范围时保持边界位置，同步继续。";
            });
            nativeDetails=Label(modal,"",24,493,462,86,12,Accent);
            nativeNotice=Label(modal,"动画 / 对齐页可保存每个动作的来源、长度、插入范围及 XYZ。同步启用状态跨动作与场景保留。",24,586,944,30,12,Muted);
            Button(modal,"六轴实时监控",24,614,224,34,()=>TogglePage(WorkspacePage.Monitor,ShowAxisMonitor));
            Label(modal,"SR6 六轴 · OSR2 三轴 · SSR1 / Handy 复用相同 L0",271,620,701,24,12,Muted);
            RefreshNative();
        }
        private void RefreshNative()
        {
            if(nativePage==null||!nativePage.activeInHierarchy)return;
            nativeStatus.text=(host.IsMainGame?"Koikatu 本体":"此页配置用于 Koikatu 本体")+" · "+host.Native.MappingStatus+"\n"+host.Native.CurrentAnimation+" · "+host.Native.CurrentProfile+" · "+host.Native.SamplingStatus;
            nativeDetails.text=host.Pipeline.NativeMode?string.Format(CultureInfo.InvariantCulture,"参考长度 {0:F6} u · 阶段幅度 ×{1:F2}\nL0 = 根点到目标距离 / 有效长度；重合 000。\n独立数值窗口的倍率按 000 起算，之后限幅、平滑。",host.Pipeline.NativeLength,host.Pipeline.NativeGain):"等待完整的本体参考坐标；缺少必要节点时会显示具体名称。";
        }
    }
}
