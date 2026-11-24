using System;
using System.Globalization;
using System.Threading;
using BepInEx.Configuration;
using KoiSync.Core;
using KoiSync.Plugin;
using UnityEngine;

namespace KoiSync.MainGame
{
    public sealed partial class Plugin
    {
        private static string[] TransportNames {get{return new[]{L("串口","Serial"),"UDP","Intiface (L0)"};}}
        private static readonly string[] TransportAddresses={"COM4","127.0.0.1","ws://127.0.0.1:12345"};
        private static readonly string[] TransportArguments={"115200","8000","0"};
        private static string[] ResponseLabels {get{return new[]{L("设备发送延迟（ms）","Device send delay (ms)"),L("实时输出平滑（ms）","Live smoothing (ms)"),L("输出速度上限（% / s）","Output rate limit (% / s)")};}}
        private static readonly float[] ResponseMaximum={250,1000,2000};
        private readonly float[] responses=new float[3];
        private readonly bool[] changedResponse=new bool[3];
        private readonly string[] gainText={"1","1","1","1","1","1"};
        private readonly ConfigEntry<bool>[] softRotation=new ConfigEntry<bool>[3];
        private string endpointText="",argumentText="",deviceNotice="";
        private int selectedTransport;
        private readonly ConfigEntry<string>[] savedDeviceAddress=new ConfigEntry<string>[3],savedDeviceArgument=new ConfigEntry<string>[3];
        private volatile string scanResult="";
        private volatile bool scanningPorts;
        private int dragAxis=-1;
        private bool dragUpper;

        private void InitializeDeviceUi()
        {
            selectedTransport=Mathf.Clamp(transport.Value,0,2);
            for(int i=0;i<3;i++)
            {
                savedDeviceAddress[i]=Config.Bind("Device","Endpoint"+i,i==selectedTransport?endpoint.Value:TransportAddresses[i]);
                savedDeviceArgument[i]=Config.Bind("Device","Argument"+i,i==selectedTransport?argument.Value.ToString(CultureInfo.InvariantCulture):TransportArguments[i]);
            }
            LoadDeviceAddress();
            for(int i=0;i<3;i++)softRotation[i]=Config.Bind("Axes",Axes[i+3]+"SoftLimit",true);
            Drive.SoftTwistLimit=softRotation[0].Value;
            Drive.SoftRollLimit=softRotation[1].Value;
            Drive.SoftPitchLimit=softRotation[2].Value;
        }

        private static double ValidLengthSetting(ConfigEntry<float> entry,double fallback,bool model)
        {
            double metres=entry.Value/1000.0;
            return (model?ShaftStrokeMapping.ValidModelLength(metres):ShaftStrokeMapping.ValidLength(metres))?metres:fallback;
        }

        private void ApplyFeelRateFloor(FeelPreset preset)
        {
            if(outputRate==null||preset==FeelPreset.Raw)return;
            float rec=(float)(FeelTranslator.RecommendedRateCap(preset)*100.0);
            if(outputRate.Value<rec&&Math.Abs(outputRate.Value-200f)<1f)
            {
                outputRate.Value=rec;
                ApplyResponseSettings();
            }
        }

        private void ApplyResponseSettings()
        {
            responses[0]=ValidResponse(sendDelay.Value,0,0);
            responses[1]=ValidResponse(smoothing.Value,1,25);
            responses[2]=ValidResponse(outputRate.Value,2,450);
            double prevDelay=Devices.DelaySeconds;
            double nextDelay=responses[0]/1000.0;
            if((prevDelay==0)!=(nextDelay==0))Devices.Stop();
            Devices.DelaySeconds=nextDelay;
            Drive.SmoothingSeconds=responses[1]/1000.0;
            Drive.MaximumChangePerSecond=responses[2]/100.0;
        }
        private static float ValidResponse(float value,int i,float fallback)
        {return Numbers.Finite(value)?Mathf.Clamp(value,0,ResponseMaximum[i]):fallback;}
        private void SaveResponse()
        {
            if(changedResponse[0])sendDelay.Value=responses[0];
            if(changedResponse[1])smoothing.Value=responses[1];
            if(changedResponse[2])outputRate.Value=responses[2];
            Array.Clear(changedResponse,0,changedResponse.Length);
            Config.Save();
        }

        private bool CanSendAxes()
        {
            if(Devices.LinearOnly)return Drive.Enabled[0];
            foreach(bool enabled in Drive.Enabled)if(enabled)return true;
            return false;
        }
        private string OutputStatus()
        {
            if(!SyncRequested)return L("设备同步关闭","Device sync off");
            if(!Devices.Connected)return L("同步已启用 · 等待连接","Sync armed · waiting for device");
            if(EditingDrive)return L("同步已启用 · 正在校准","Sync armed · calibrating");
            if(!outputValid)return L("同步已启用 · 等待有效姿态","Sync armed · waiting for valid pose");
            if(!CanSendAxes())return L("同步已启用 · 未选可发送通道","Sync armed · no channels enabled");
            var telemetry=Devices.GetTelemetry();
            return telemetry.OutputRequested&&telemetry.SecondsSinceMovement<.5?L("同步发送中","Sending output"):L("同步已启用 · 等待发送","Sync armed · waiting to send");
        }

        private void DrawDevicePage()
        {
            float y=Card(18,57,430,260,L("设备连接","Device connection"));
            int selected=GUI.Toolbar(new Rect(32,y,402,32),selectedTransport,TransportNames,button);
            if(selected!=selectedTransport)
            {
                Devices.Disconnect();selectedTransport=selected;LoadDeviceAddress();deviceNotice="";
            }
            GUI.Label(new Rect(32,y+43,260,23),selected==0?L("串口","Serial port"):selected==1?L("设备 IP / 主机","Device IP / host"):L("WebSocket 地址","WebSocket URL"),small);
            GUI.Label(new Rect(301,y+43,134,23),selected==0?L("波特率","Baud rate"):selected==1?L("UDP 端口","UDP port"):L("设备索引","Device index"),small);
            endpointText=Edit(32,y+66,260,29,endpointText);
            argumentText=Edit(301,y+66,133,29,argumentText);
            bool linked=Devices.Connecting||Devices.Connected;
            if(Button(32,y+110,194,34,Devices.Connecting?L("取消连接","Cancel"):Devices.Connected?L("断开","Disconnect"):L("连接设备","Connect"),linked?button:primaryButton))
                ToggleConnection(selected);
            GUI.enabled=selected==0&&!scanningPorts;
            if(Button(238,y+110,196,34,scanningPorts?L("扫描中…","Scanning…"):L("扫描串口","Scan ports")))ScanPorts();
            GUI.enabled=true;
            GUI.Label(new Rect(32,y+152,402,27),DeviceStatus(),label);
            GUI.Label(new Rect(32,y+186,402,32),UiText.TranslateComposite(deviceNotice.Length>0?deviceNotice:selected==0?scanResult:""),wrapSmall);

            y=Card(462,57,430,260,L("设备同步","Device sync"));
            if(Button(476,y,402,46,SyncRequested?L("停止设备同步","Stop synchronization"):L("开启设备同步","Enable synchronization"),SyncRequested?dangerButton:primaryButton))SetSync(!SyncRequested);
            GUI.Label(new Rect(476,y+59,402,28),OutputStatus(),label);
            GUI.Label(new Rect(476,y+91,402,28),syncKey.Value+" "+L("切换同步","Toggle sync"),small);
            GUI.Label(new Rect(476,y+120,402,54),Devices.LinearOnly?L("Intiface 发送 L0。六轴监控继续显示当前计算。","Intiface sends L0 only. Six-axis monitor still shows full calculation."):
                L("等待连接、有效绑定或可发送通道时，保持同步开关。","Sync stays armed while waiting for connection, valid binding, or enabled channels."),wrapSmall);
            if(Button(476,y+180,402,34,showChannels?L("隐藏六轴监控","Hide six-axis monitor"):L("打开六轴监控","Open six-axis monitor")))ToggleChannels();

            y=Card(18,330,874,259,L("同步与输出响应","Sync & output response"));
            for(int i=0;i<responses.Length;i++)DrawResponse(i,y+i*45);
            GUI.Label(new Rect(32,y+147,846,57),L("延迟只作用于发送。平滑和速度上限只作用于实时计算；录制使用延迟前的最终值。数值为 0 时关闭对应处理。","Delay applies only to device output. Smoothing and rate limits apply only to live calculation; recording uses pre-delay final values. Set 0 to disable a stage."),wrapSmall);
        }

        private void ToggleConnection(int selected)
        {
            if(Devices.Connected||Devices.Connecting){Devices.Disconnect();return;}
            try
            {
                int number;
                string address=endpointText.Trim();
                bool parsed=int.TryParse(argumentText,NumberStyles.Integer,CultureInfo.InvariantCulture,out number);
                if(address.Length==0||!parsed||selected==0&&(number<1200||number>2000000)||
                    selected==1&&(number<1||number>65535)||selected==2&&number<0)
                    throw new ArgumentException(L("请检查设备地址与连接参数","Check device address and connection parameters"));
                savedDeviceAddress[selected].Value=endpoint.Value=address;
                savedDeviceArgument[selected].Value=argumentText;
                argument.Value=number;transport.Value=selected;Config.Save();deviceNotice="";
                Devices.Connect(new DeviceSettings{Transport=selected,Endpoint=address,Argument=number,Kind=DeviceKind.SR6});
            }
            catch(Exception error){deviceNotice=error.GetBaseException().Message;}
        }
        private void LoadDeviceAddress()
        {endpointText=savedDeviceAddress[selectedTransport].Value;argumentText=savedDeviceArgument[selectedTransport].Value;}
        private void ScanPorts()
        {
            scanningPorts=true;deviceNotice="";
            ThreadPool.QueueUserWorkItem(_=>
            {
                try
                {
                    var ports=SerialTransport.GetPortNames();
                    scanResult=ports.Length==0?L("未发现串口","No serial ports found"):L("可用串口   ","Available ports   ")+string.Join("  ",ports);
                }
                catch(Exception error){scanResult=error.GetBaseException().Message;}
                finally{scanningPorts=false;}
            });
        }
        private void DrawResponse(int index,float y)
        {
            GUI.Label(new Rect(32,y+1,217,27),ResponseLabels[index],label);
            float prior=responses[index];
            float next=GUI.HorizontalSlider(new Rect(253,y+10,479,20),prior,0,ResponseMaximum[index]);
            GUI.Label(new Rect(756,y+2,122,27),prior==0?L("0 · 关闭","0 · Off"):prior.ToString("0.#",CultureInfo.InvariantCulture),small);
            if(Math.Abs(next-prior)<.5)return;
            next=Mathf.Round(next);
            responses[index]=next;changedResponse[index]=true;responseDirty=true;
            if(index==0)
            {
                if((prior==0)!=(next==0))Devices.Stop();
                Devices.DelaySeconds=next/1000.0;
            }
            else
            {
                BeginDriveEdit("output response changed");
                if(index==1)Drive.SmoothingSeconds=next/1000.0;
                else Drive.MaximumChangePerSecond=next/100.0;
            }
        }

        private void DrawSettingsPage()
        {
            float y=Card(18,57,430,184,L("显示与快捷键","Display & shortcuts"));
            previewEnabled.Value=GUI.Toggle(new Rect(32,y,188,24),previewEnabled.Value,L("场景模型预览","Scene model preview"),toggle);
            xray.Value=GUI.Toggle(new Rect(238,y,196,24),xray.Value,L("模型透视显示","X-ray preview"),toggle);
            showEntryMarkers.Value=GUI.Toggle(new Rect(32,y+30,400,24),showEntryMarkers.Value,L("工作台显示入口标记","Show entry markers in scene"),toggle);
            GUI.Label(new Rect(32,y+62,402,24),panelKey.Value+" "+L("显隐面板","Panel")+"  ·  "+syncKey.Value+" "+L("同步","Sync")+"  ·  "+axesKey.Value+" "+L("六轴","Axes"),small);
            if(Button(32,y+96,170,28,showChannels?L("隐藏六轴监控","Hide six-axis monitor"):L("打开六轴监控","Open six-axis monitor")))ToggleChannels();
            GUI.Label(new Rect(210,y+100,224,24),L("六轴行程与倍率监控","Six-axis travel & gain monitor"),small);

            y=Card(462,57,430,184,L("柔性旋转响应","Soft rotation response"));
            for(int i=0;i<3;i++)
            {
                bool current=softRotation[i].Value;
                bool enabled=GUI.Toggle(new Rect(476,y+i*32,402,26),current,Axes[i+3]+"  "+L("大角度保留连续变化","Soft compression at large angles"),toggle);
                if(enabled==current)continue;
                BeginDriveEdit("rotation limits changed");softRotation[i].Value=enabled;
                Drive.SoftTwistLimit=softRotation[0].Value;Drive.SoftRollLimit=softRotation[1].Value;Drive.SoftPitchLimit=softRotation[2].Value;
                ResetMotionHistory();FinishDriveEdit();
            }
            GUI.Label(new Rect(476,y+100,402,38),L("柔性映射使用这些旋转选项，尽量保真模式使用动作基准。","Soft mapping uses these rotation limits; relative mode uses an action reference."),wrapSmall);

            DrawVoiceSettingsCard();
        }

        private void DrawChannelWindow(int id)
        {
            Box(new Rect(1,1,648,43),HeaderFill);
            GUI.Label(new Rect(18,9,480,29),L("六轴行程","Six axes"),title);
            if(Button(600,9,32,27,"×")){showChannels=false;dragAxis=-1;FinishDriveEdit();}
            GUI.DragWindow(new Rect(12,4,530,36));
            GUI.Label(new Rect(18,49,610,24),L("青色 · 输入     橙色 · 最终输出     白线 · 滤波前目标","Teal · Input     Orange · Output     White · Pre-filter target"),small);
            for(int i=0;i<6;i++)DrawChannel(i);
            GUI.Label(new Rect(18,270,612,26),OutputStatus()+"   ·   "+(outputFromScript?L("脚本回放","Script playback"):L("实时计算","Live calculation")),small);
        }
        private void DrawChannel(int axis)
        {
            float x=18+axis*103;
            GUI.Label(new Rect(x,76,98,23),Axes[axis]+"    "+(outputValid?Position(outputValues[axis]):"—"),label);
            var track=new Rect(x+14,105,43,103);
            float low=track.yMax-(float)Drive.Minimum[axis]*track.height;
            float high=track.yMax-(float)Drive.Maximum[axis]*track.height;
            Box(track,Edge);Box(new Rect(track.x-3,high,49,low-high),new Color(.52f,.20f,.38f,.42f));
            if(outputValid)
            {
                FillRail(new Rect(track.x,track.y,18,track.height),Drive.Input[axis],Accent);
                FillRail(new Rect(track.x+25,track.y,18,track.height),outputValues[axis],Warm);
                Box(new Rect(track.x+24,track.yMax-(float)Drive.Target[axis]*track.height,20,1),Color.white);
            }
            Box(new Rect(track.x-4,high-2,51,4),Warm);Box(new Rect(track.x-4,low-2,51,4),Accent);
            GUI.Label(new Rect(x+66,track.y-5,36,22),Position(Drive.Maximum[axis]),small);
            GUI.Label(new Rect(x+66,track.yMax-14,36,22),Position(Drive.Minimum[axis]),small);
            var e=Event.current;
            if(e.type==EventType.MouseDown&&e.button==0&&new Rect(track.x-6,track.y-5,56,track.height+10).Contains(e.mousePosition))
            {
                dragAxis=axis;dragUpper=Mathf.Abs(e.mousePosition.y-high)<Mathf.Abs(e.mousePosition.y-low);
                BeginDriveEdit("axis travel changed");e.Use();
            }
            if(dragAxis==axis&&(e.type==EventType.MouseDrag||e.rawType==EventType.MouseUp))
            {
                double value=Mathf.Clamp01((track.yMax-e.mousePosition.y)/track.height);
                if(dragUpper)Drive.Maximum[axis]=Math.Max(Drive.Minimum[axis],value);
                else Drive.Minimum[axis]=Math.Min(Drive.Maximum[axis],value);
                axesDirty=true;
                if(e.rawType==EventType.MouseUp){dragAxis=-1;FinishDriveEdit();}
                if(e.type!=EventType.Used)e.Use();
            }
            bool enabled=GUI.Toggle(new Rect(x,214,25,27),Drive.Enabled[axis],"",toggle);
            if(enabled!=Drive.Enabled[axis])
            {BeginDriveEdit("axis enable changed");Drive.Enabled[axis]=enabled;axesDirty=true;FinishDriveEdit();}
            GUI.Label(new Rect(x+29,218,16,24),"×",small);
            GUI.enabled=!outputFromScript;
            string updated=Edit(x+43,215,52,25,gainText[axis]);
            GUI.enabled=true;
            if(updated!=gainText[axis])
            {
                gainText[axis]=updated;
                double value;
                if(double.TryParse(updated,NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&Numbers.Finite(value)&&value>=0&&value<=20)
                {BeginDriveEdit("axis gain changed");Drive.Gain[axis]=value;axesDirty=true;}
            }
            if(outputValid&&!outputFromScript&&axis>=3)
            {
                string request=axis==3?Drive.TwistRequestedDegrees.ToString("F1",CultureInfo.InvariantCulture)+"°":
                    axis==4?(Drive.RotationRequests[1]*100).ToString("F1",CultureInfo.InvariantCulture)+"%":
                    Drive.PitchRequestedDegrees.ToString("F1",CultureInfo.InvariantCulture)+"°";
                GUI.Label(new Rect(x,243,103,23),L("请求 ","Req ")+request,small);
            }
        }
        private static void FillRail(Rect rail,double value,Color color)
        {
            float height=(float)Numbers.Clamp(value,0,1)*rail.height;
            Box(new Rect(rail.x,rail.yMax-height,rail.width,height),color);
        }
        private void SaveAxes()
        {
            for(int i=0;i<6;i++)
            {
                min[i].Value=(float)Drive.Minimum[i];max[i].Value=(float)Drive.Maximum[i];gain[i].Value=(float)Drive.Gain[i];
                enabledAxes[i].Value=Drive.Enabled[i];
            }
            Config.Save();
        }
    }
}
