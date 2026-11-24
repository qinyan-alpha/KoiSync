using KoiSync.Core;
using UnityEngine;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    // The six-value page owns every global value setting: the per-axis limits and gains of the six bars,
    // the output mapping range and the six send switches. One place, one title, no duplication with the
    // overview page.
    public sealed partial class KoiSyncPanel
    {
        private GameObject monitorPage;
        private void ShowAxisMonitor()
        {
            if(monitorPage!=null){ActivatePage(WorkspacePage.Monitor);return;}
            var page=CreatePage(WorkspacePage.Monitor);monitorPage=page.gameObject;
            Label(page,"六轴数值 · 全局配置",22,16,700,36,23,Accent,FontStyle.Bold);
            Label(page,"上下限与倍率影响所有动画，是整体的安全边界；单个动画可在 3D 对齐 → 更多轴范围 里单独收窄，单个设置优先。",22,54,760,26,12,Muted);
            Button(page,"独立窗口",794,18,183,34,ShowHud);
            BuildChannelBank(page,23,96,950,330);RefreshChannelBanks();
            Label(page,"全局轴值限制 · 映射范围 · 通道选择",16,520,950,28,17,Accent,FontStyle.Bold);
            rangeLabel=Label(page,"全局输出范围  100 — 899 · 双手柄",16,556,320,26,14,Muted);
            float lo=host.Config.Bind("Output","Minimum",.1f,"Global output lower bound").Value,hi=host.Config.Bind("Output","Maximum",.9f,"Global output upper bound").Value;
            outputRange=Range(page,330,556,410,lo,hi,(a,b)=>UpdateRange());
            outputRange.Finished=()=>{host.Config.Bind("Output","Minimum",.1f,"").Value=outputRange.Lower;host.Config.Bind("Output","Maximum",.9f,"").Value=outputRange.Upper;};
            Label(page,"发送通道 · 勾选后保存",16,596,300,26,15,Muted);
            for(int i=0;i<6;i++){int index=i;channels[i]=Toggle(page,MotionPipeline.Axes[i],330+i*100,596,94,host.Pipeline.EnabledAxes[i],v=>{host.PauseOutput("已更改发送通道");host.OutputChannels.Set(index,v);});}
            UpdateRange();
        }
        private void RefreshAxisMonitor(){}
    }
}
