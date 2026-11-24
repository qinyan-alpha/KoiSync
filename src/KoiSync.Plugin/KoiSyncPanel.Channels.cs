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
        private sealed class ChannelBank
        {
            internal RectTransform Root;
            internal readonly ChannelFader[] Faders=new ChannelFader[6];
            internal readonly InputField[] Gain=new InputField[6];
            internal string Context="",DragContext="";
        }
        private readonly List<ChannelBank> channelBanks=new List<ChannelBank>();
        private string ChannelContext{get{return host.IsMainGame?"native:"+host.Profiles.NativeKey:host.Profiles.ScenePath+":"+(LaboratoryOpen?alignmentKey:host.Profiles.CurrentSegment==null?"":host.Profiles.CurrentSegment.Id);}}
        private MeasurementProfile ChannelProfile
        {get{if(LaboratoryOpen){if(host.IsMainGame&&alignmentNativeKey!=host.Profiles.NativeKey||alignmentScene!=host.Profiles.Scene)LoadAlignment("");return alignmentDraft;}return host.IsMainGame?host.Profiles.NativeMeasurement:host.Profiles.StudioMeasurement;}}
        private ChannelBank BuildChannelBank(RectTransform parent,float x,float y,float width,float height=190)
        {
            var bank=new ChannelBank{Root=Rect(parent,"Vertical channel controls",x,y,width,height+82)};channelBanks.Add(bank);float spacing=width/6;
            for(int i=0;i<6;i++)
            {
                int axis=i;float left=i*spacing+7,w=spacing-14;
                var r=Rect(bank.Root,"Vertical limits "+MotionPipeline.Axes[i],left+(w-52)*.5f,0,52,height);var f=r.gameObject.AddComponent<ChannelFader>();bank.Faders[i]=f;
                f.ValueHint=Label(r,"",0,0,52,21,12,Color.white);f.ValueHint.gameObject.SetActive(false);
                bank.Gain[i]=Input(bank.Root,left+10,height+8,w-10,"1");Label(bank.Root,"×",left,height+14,13,21,12,Muted);
                Label(bank.Root,MotionPipeline.Axes[i],left,height+48,w,25,17,Accent,FontStyle.Bold);
                f.Started=()=>bank.DragContext=ChannelContext;
                f.Finished=()=>EditChannel(bank,axis,true,bank.DragContext);
                bank.Gain[i].onEndEdit.AddListener(v=>EditChannel(bank,axis,false,bank.Context));
            }
            return bank;
        }
        // The six-bar window and the monitor page configure the global axis limits and gains, so one
        // adjustment covers every animation. A single animation narrows its own axes in the
        // 3D 对齐 window, and those per-animation values win over the global ones.
        private void EditChannel(ChannelBank bank,int axis,bool range,string context)
        {
            try
            {
                if(range)
                {
                    host.Global.AxisMinimum[axis].Value=bank.Faders[axis].Lower;
                    host.Global.AxisMaximum[axis].Value=bank.Faders[axis].Upper;
                }
                else
                {
                    double gain=ProfileNumber(bank.Gain[axis]);
                    if(gain<0||gain>20)throw new ArgumentException("通道倍率为 0–20");
                    host.Global.AxisGain[axis].Value=(float)gain;
                }
                host.Global.Save();host.Global.ApplyTo(host.Pipeline);
                host.Global.Status=MotionPipeline.Axes[axis]+" 全局配置已保存";
                bank.Gain[axis].textComponent.color=Color.white;RefreshChannelBanks();
            }
            catch(Exception ex){host.Global.Status=ex.GetBaseException().Message;bank.Gain[axis].textComponent.color=new Color(1,.4f,.4f);}
        }
        private void RefreshChannelBanks()
        {
            string context=ChannelContext;
            foreach(var b in channelBanks)
            {
                if(!b.Root.gameObject.activeInHierarchy)continue;bool changed=b.Context!=context;b.Context=context;
                for(int i=0;i<6;i++)
                {
                    var f=b.Faders[i];bool busy=b.Gain[i].isFocused||f.Editing;
                    bool mapped=MonitoredSource!=null&&host.Pipeline.HasDesired;double value=mapped?host.Pipeline.Desired[i]:double.NaN;
                    float lo=host.Global.AxisMinimum[i].Value,hi=Math.Max(lo,host.Global.AxisMaximum[i].Value),gain=host.Global.AxisGain[i].Value;
                    f.Set(busy&&!changed?f.Lower:lo,busy&&!changed?f.Upper:hi,(float)value,mapped);
                    if(!busy||changed)b.Gain[i].text=gain.ToString("0.####",CultureInfo.InvariantCulture);
                }
            }
        }
        private void BuildChannelHud()
        {
            var hud=FloatingWindow("KoiSync 3D inspection HUD","",460,326,new Vector2(340,230),CloseHud);hudObject=hud.gameObject;
            BuildChannelBank(hud,13,42,434);hudObject.SetActive(false);
        }
    }
}
