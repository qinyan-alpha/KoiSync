using UnityEngine;
using UnityEngine.UI;
using KoiSync.Core;

namespace KoiSync.Plugin
{
    public sealed partial class KoiSyncPanel
    {
        private void BuildCompactOverview()
        {
            var summary=Panel(overviewPage.transform,196,112,980,123,Card);
            context=Label(summary,"当前动画",20,15,935,31,20,Accent,FontStyle.Bold);
            sourceState=Label(summary,"",20,52,935,27,13,Muted);
            status=Label(summary,"等待数据",20,86,935,26,14,Accent);
            var actions=Panel(overviewPage.transform,196,252,542,330,Card);
            Label(actions,"同步控制",20,18,490,30,19,Color.white,FontStyle.Bold);
            Button(actions,"动画行程",20,67,240,44,ShowStrokeSettings);
            Button(actions,"六轴数值",278,67,244,44,ShowHud);
            Button(actions,"3D 对齐",20,126,115,44,ShowStudioTools);
            Button(actions,"动画时间轴",147,126,113,44,ShowMotionTimeline);
            debug=Toggle(actions,"显示场景标记",286,135,234,true,v=>host.DebugVisible=v);
            calibrate=Button(actions,"重新观察循环",20,188,240,37,()=>host.Calibrate());
            invert=Toggle(actions,"反转 L0",286,196,230,false,v=>{if(refreshing)return;host.Pipeline.InvertStroke=v;});
            Label(actions,"发送通道、映射范围与全局轴值限制都移到「六轴数值」页，那里是全局配置。",20,250,490,60,13,Muted);
            var settings=Panel(overviewPage.transform,754,252,422,499,Card);
            Label(settings,"设备连接",20,18,375,30,19,Color.white,FontStyle.Bold);
            autoConnectToggle=Toggle(settings,"自动扫描连接",20,69,379,true,v=>{if(building||autoConnect==null)return;autoConnect.Value=v;ResumeAutoConnection();});
            device=Dropdown(settings,20,118,127,new[]{"TCode","Intiface"},i=>{if(building)return;host.PauseOutput("已更换协议");host.Devices.Disconnect();if(savedDevice!=null)savedDevice.Value=i;transport.value=i==1?2:transport.value==2?0:transport.value;ResumeAutoConnection();Refresh();});
            transport=Dropdown(settings,160,118,242,new[]{"串口 / 蓝牙 COM","Wi-Fi / UDP","Intiface Central"},TransportChanged);
            endpointCaption=Label(settings,"串口名称 / 波特率",20,178,294,25,13,Muted);
            Button(settings,"扫描",323,176,79,28,ResumeAutoConnection);
            endpoint=Input(settings,20,217,263,"COM3");argument=Input(settings,295,217,107,"115200");
            endpoint.onValueChanged.AddListener(v=>{connectionError="";if(endpoint.isFocused)autoPaused=true;});argument.onValueChanged.AddListener(v=>{connectionError="";if(argument.isFocused)autoPaused=true;});
            connect=Button(settings,"连接设备",20,283,185,43,Connect);
            arm=Button(settings,"启用同步",218,283,184,43,host.EnableSync,new Color32(37,108,97,255));
            connection=Label(settings,"未连接",20,354,382,105,14,Muted);
        }
        private void RefreshCompactOverview()
        {
            context.text=host.Demo?"演示数据":string.IsNullOrEmpty(host.Native.CurrentAnimation)?host.Frame.Context:host.Native.CurrentAnimation;
            sourceState.text=host.Native.CurrentProfile+" · "+host.Native.SamplingStatus;
            status.text=host.Pipeline.Status+(host.Pipeline.NativeStrokeStatus.Length==0?"":" · "+host.Pipeline.NativeStrokeStatus);
            debug.isOn=host.DebugVisible;invert.isOn=host.Pipeline.InvertStroke;
        }
    }
}
