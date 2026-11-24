using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using KoiSync.Wire;
using KoiSync.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    public sealed partial class KoiSyncPanel:MonoBehaviour
    {
        private Plugin host;
        private GameObject canvasObject,eventObject,window,hudObject;
        private Font font;
        private Sprite rounded;
        private Texture2D roundedTexture;
        private Dropdown sources,device,transport,axis;
        private InputField endpoint,argument;
        private Text context,status,raw,rawDetail,bones,command,connection,endpointCaption,values,sourceState;
        private Button connect,arm,calibrate,nativeSourceButton;
        private Text nativeAxisNote;
        private Toggle debug,invert;
        private readonly Toggle[] channels=new Toggle[6];
        private SignalGraphic chart;
        private RangeSlider outputRange;
        private Text rangeLabel;
        private bool building,refreshing;
        private string sourceSignature="";
        private readonly List<string> overviewSourceIds=new List<string>();
        private CursorLockMode oldLock;
        private bool oldCursor;
        private EventSystem previousEventSystem;
        private string connectionError="";
        private readonly string[] addresses={"COM3","127.0.0.1","ws://127.0.0.1:12345"};
        private readonly string[] arguments={"115200","8000","0"};
        private int previousTransport;
        private static readonly Color Background=new Color32(14,23,34,255),Card=new Color32(25,38,53,255),Muted=new Color32(148,167,188,255),Accent=new Color32(107,229,202,255);
        public bool Visible {get;private set;}
        internal bool WorkspaceVisible{get{return Visible&&window.activeSelf;}}
        internal DeviceKind DeviceKind{get{return DeviceKind.SR6;}}

        internal void Initialize(Plugin plugin)
        {
            host=plugin;building=true;
            font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Noto Sans CJK SC","Arial"},16);
            roundedTexture=new Texture2D(32,32,TextureFormat.RGBA32,false);
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)
            {
                float dx=Mathf.Max(8-x,x-23),dy=Mathf.Max(8-y,y-23);
                float distance=Mathf.Sqrt(Mathf.Max(0,dx)*Mathf.Max(0,dx)+Mathf.Max(0,dy)*Mathf.Max(0,dy));
                roundedTexture.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01(8.5f-distance)));
            }
            roundedTexture.Apply();rounded=Sprite.Create(roundedTexture,new Rect(0,0,32,32),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(10,10,10,10));
            canvasObject=new GameObject("KoiSync Interface");DontDestroyOnLoad(canvasObject);
            Canvas canvas=canvasObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=32000;
            CanvasScaler scaler=canvasObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1500,950);scaler.matchWidthOrHeight=.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
            RectTransform root=canvasObject.GetComponent<RectTransform>();
            RectTransform win=Rect(root,"Workspace",0,0,1200,800);win.anchorMin=win.anchorMax=new Vector2(.5f,.5f);win.pivot=new Vector2(.5f,.5f);win.anchoredPosition=Vector2.zero;
            window=win.gameObject;Image(win,Background,true).raycastTarget=true;
            overviewPage=Rect(win,"Overview page",0,0,1200,800).gameObject;pages.Add(WorkspacePage.Overview,overviewPage);
            pageHost=Rect(win,"Workspace pages",184,105,998,665);
            RectTransform sidebar=Panel(win,0,0,170,800,new Color32(19,32,46,255));
            Label(sidebar,"KoiSync",24,32,145,42,29,Accent,FontStyle.Bold);
            Label(sidebar,"MOTION WORKSPACE",25,79,145,20,10,Muted);
            NavigationButton(sidebar,WorkspacePage.Overview,"同步工作台",143,ShowOverview);
            if(host.IsMainGame)
            {
                NavigationButton(sidebar,WorkspacePage.Stroke,"动画行程",195,ShowStrokeSettings);
                NavigationButton(sidebar,WorkspacePage.Transitions,"拔出动 / 无交互",247,ShowTransitions);
                NavigationButton(sidebar,WorkspacePage.Shortcuts,"快捷键",299,ShowShortcuts);
                // The former "演示数据" entry was removed: it silently replaced every real sample with a
                // synthetic frame, which left the six-axis window at zero and dropped the native stroke
                // boost. The synthetic frame now only exists for the automated --koisync-smoke capture.
                var more=Dropdown(sidebar,10,357,150,new[]{"更多设置…","VR / 控制","来源 / 全局限幅","3D 显示设置","姿态检查","关于 / 许可"},i=>{switch(i){case 1:TogglePage(WorkspacePage.VR,ShowVrSettings);break;case 2:TogglePage(WorkspacePage.Native,ShowNativeSettings);break;case 3:TogglePage(WorkspacePage.Geometry,Show3DSettings);break;case 4:TogglePage(WorkspacePage.Skeleton,ShowSkeletonInspection);break;case 5:TogglePage(WorkspacePage.Licenses,ShowLicenses);break;}});
                more.onValueChanged.AddListener(i=>{if(i!=0)more.value=0;});
            }
            else
            {
            if(host.IsMainGame)NavigationButton(sidebar,WorkspacePage.Native,"本体同步",187,ShowNativeSettings);
            else NavigationButton(sidebar,WorkspacePage.Studio,"工作室绑定",187,ShowStudioBindings);
            if(!host.IsMainGame)NavigationButton(sidebar,WorkspacePage.StudioReplace,"批量换女角",231,ShowStudioCharacterReplacement);
            NavigationButton(sidebar,WorkspacePage.Monitor,"六轴监控",275,ShowAxisMonitor);
            NavigationButton(sidebar,WorkspacePage.Geometry,"3D 显示",319,Show3DSettings);
            NavigationButton(sidebar,WorkspacePage.Skeleton,"姿态检查",363,ShowSkeletonInspection);
            NavigationButton(sidebar,WorkspacePage.Licenses,"关于 / 许可",407,ShowLicenses);
            if(!host.IsMainGame)NavigationButton(sidebar,WorkspacePage.Experiment,"工作室实验",451,ShowStudioExperiment);
            NavigationButton(sidebar,WorkspacePage.Transitions,"拔出动 / 无交互",495,ShowTransitions);
            NavigationButton(sidebar,WorkspacePage.Shortcuts,"快捷键",539,ShowShortcuts);
            NavigationButton(sidebar,WorkspacePage.VR,"VR / 控制",host.IsMainGame?451:583,ShowVrSettings);
            Button(sidebar,"悬浮数值监控",10,627,150,35,ShowHud);
            }
            Label(sidebar,host.PanelShortcut+" 打开 / 收起\n"+host.DebugShortcut+" 场景标记\nVR："+host.VrOptions.ToggleKey.Value,24,670,132,48,12,Muted);
            Label(sidebar,"KOIKATU + STUDIO",25,720,145,20,10,Accent);
            Label(sidebar,"单 DLL  /  0.1.28",25,750,145,20,11,Muted);
            RectTransform title=Rect(win,"Title drag",170,0,1030,100);Image(title,Color.clear,false).raycastTarget=true;
            title.gameObject.AddComponent<PanelDrag>().Target=win;
            AddResizeHandle(win);
            Label(title,"设备同步工作台",28,25,480,40,26,Color.white,FontStyle.Bold);
            Label(title,"在游戏里看见骨骼、通道与设备的映射。",30,67,490,23,13,Muted);
            Button(title,"停止输出",810,30,116,39,()=>host.Stop("已手动停止"),new Color32(93,43,55,255));
            Button(title,"收起",939,30,65,39,CloseWorkspace);
            if(host.IsMainGame)BuildCompactOverview();
            else
            {
            RectTransform summary=Panel(overviewPage.transform,196,112,625,94,Card);
            context=Label(summary,"等待场景",18,16,370,31,19,Accent,FontStyle.Bold);
            sourceState=Label(summary,"本体和工作室共用采样核心",18,53,555,23,12,Muted);
            RectTransform spatial=Panel(overviewPage.transform,196,220,625,205,Card);
            Label(spatial,"测量输入 · 0–999",18,17,570,29,18,Color.white,FontStyle.Bold);
            Label(spatial,"000 = 0% · 500 ≈ 中点 · 999 = 100% · — 表示无有效范围",18,53,585,25,12,Muted);
            raw=Label(spatial,"L0 —      L1 —      L2 —\nR0 —      R1 —      R2 —",18,83,585,51,19,Accent);
            rawDetail=Label(spatial,"原始值保留在六轴监控中",18,132,585,19,11,Muted);
            Button(spatial,"查看场景 3D  ↗",395,153,210,35,()=>{host.DebugVisible=true;CloseWorkspace();});
            Button(spatial,"3D 显示设置",18,153,164,35,Show3DSettings);
            Button(spatial,"全身骨架检查",196,153,185,35,ShowSkeletonInspection);
            RectTransform curve=Panel(overviewPage.transform,196,439,625,216,Card);
            Label(curve,"通道曲线",18,15,220,28,18,Color.white,FontStyle.Bold);
            Label(curve,"映射目标 · 0–999",370,19,240,23,12,Muted);
            RectTransform graph=Rect(curve,"Graph",18,52,589,115);chart=graph.gameObject.AddComponent<SignalGraphic>();chart.raycastTarget=false;
            values=Label(curve,"L0 —    L1 —    L2 —    R0 —    R1 —    R2 —",18,182,590,22,12,Muted);
            RectTransform bottom=Panel(overviewPage.transform,196,669,625,98,Card);
            status=Label(bottom,"选择运动源后采集动作范围。",18,15,586,30,15,Accent);
            command=Label(bottom,"设备输出：未启用",18,54,586,27,12,Muted);
            RectTransform settings=Panel(overviewPage.transform,837,112,339,655,Card);
            Label(settings,"运动源与映射",18,15,300,28,18,Color.white,FontStyle.Bold);
            sources=Dropdown(settings,18,52,303,new string[]{"选择运动源…"},i=>{if(!refreshing&&i>0&&i<=overviewSourceIds.Count)host.Select(overviewSourceIds[i-1]);});
            nativeSourceButton=Button(settings,"本体自动来源 · 配置 ↗",18,52,303,34,ShowNativeSettings);nativeSourceButton.gameObject.SetActive(host.IsMainGame);
            bones=Label(settings,"等待骨骼数据",18,95,305,30,11,Muted);
            if(!host.IsMainGame)
            {
                sources.GetComponent<RectTransform>().anchoredPosition=new Vector2(18,-96);bones.gameObject.SetActive(false);
                overviewPairBox=Dropdown(settings,18,52,303,new[]{"先选择交互角色对…"},i=>{if(refreshing||i<0||i>=overviewPairs.Count)return;overviewPair=overviewPairs[i];sourceSignature="";Refresh();});
            }
            debug=Toggle(settings,"游戏内 3D 通道",18,132,220,true,v=>host.DebugVisible=v);
            axis=Dropdown(settings,18,170,140,new[]{"L0 · 局部 Z","L0 · 局部 Y","L0 · 局部 X"},i=>{host.PauseOutput("已更改投影轴");host.Axis=(StrokeAxis)i;});
            nativeAxisNote=Label(settings,"本体参考轴 · 自动",18,176,153,28,12,Muted);nativeAxisNote.gameObject.SetActive(host.IsMainGame);
            invert=Toggle(settings,"反转 L0",181,176,145,false,v=>{host.PauseOutput("已更改方向");host.Pipeline.InvertStroke=v;});
            calibrate=Button(settings,"采集范围 · 6 秒",18,216,303,36,()=>host.Calibrate());
            rangeLabel=Label(settings,"输出范围  10% — 90%",18,263,300,24,12,Muted);
            float savedLo=host.Config.Bind("Output","Minimum",.1f,"Global output lower bound").Value;
            float savedHi=host.Config.Bind("Output","Maximum",.9f,"Global output upper bound").Value;
            outputRange=Range(settings,18,289,303,savedLo,savedHi,(lo,hi)=>UpdateRange());
            outputRange.Finished=()=>{host.Config.Bind("Output","Minimum",.1f,"").Value=outputRange.Lower;host.Config.Bind("Output","Maximum",.9f,"").Value=outputRange.Upper;};
            for(int i=0;i<6;i++){int index=i;channels[i]=Toggle(settings,MotionPipeline.Axes[i],18+(i%3)*102,323+(i/3)*28,99,host.Pipeline.EnabledAxes[i],v=>{host.PauseOutput("已更改发送通道");host.OutputChannels.Set(index,v);});}
            Panel(settings,18,386,303,1,new Color32(49,67,87,255));
            Label(settings,"设备连接",18,400,110,25,17,Color.white,FontStyle.Bold);
            autoConnectToggle=Toggle(settings,"自动扫描连接",144,400,181,true,v=>{if(building||autoConnect==null)return;autoConnect.Value=v;ResumeAutoConnection();});
            device=Dropdown(settings,18,435,105,new[]{"TCode","Intiface"},i=>{if(building)return;host.PauseOutput("已更换协议");host.Devices.Disconnect();if(savedDevice!=null)savedDevice.Value=i;transport.value=i==1?2:transport.value==2?0:transport.value;ResumeAutoConnection();Refresh();});
            transport=Dropdown(settings,135,435,186,new[]{"串口 / 蓝牙 COM","Wi-Fi / UDP","Intiface Central"},TransportChanged);
            endpointCaption=Label(settings,"串口名称 / 波特率",18,476,214,22,11,Muted);
            Button(settings,"扫描",245,473,76,25,ResumeAutoConnection);
            endpoint=Input(settings,18,502,213,"COM3");argument=Input(settings,240,502,81,"115200");
            endpoint.onValueChanged.AddListener(v=>{connectionError="";if(endpoint.isFocused)autoPaused=true;});argument.onValueChanged.AddListener(v=>{connectionError="";if(argument.isFocused)autoPaused=true;});
            connect=Button(settings,"连接设备",18,546,145,35,Connect);
            arm=Button(settings,"启用同步",177,546,144,35,host.EnableSync,new Color32(37,108,97,255));
            connection=Label(settings,"未连接",18,590,304,54,11,Muted);
            }
            Label(win,host.PanelShortcut+" 界面  ·  "+host.DebugShortcut+" 3D  ·  单 DLL / 游戏内运行  ·  蓝牙设备可经 Intiface Central 接入",196,774,990,20,11,Muted);
            for(int i=0;i<3;i++)
            {
                addresses[i]=host.Config.Bind("Device","Endpoint"+i,addresses[i],"Endpoint for transport "+i).Value;
                arguments[i]=host.Config.Bind("Device","Argument"+i,arguments[i],"Baud / UDP port / Intiface device index").Value;
            }
            InitializeConnections();building=false;UpdateRange();canvasObject.SetActive(false);
            BuildChannelHud();
            ActivatePage(WorkspacePage.Overview);
        }
        private void EnsureEventSystem()
        {
            if(eventObject==null)
            {
                eventObject=new GameObject("KoiSync UI Events");DontDestroyOnLoad(eventObject);eventObject.AddComponent<EventSystem>();
                eventObject.AddComponent<StandaloneInputModule>().forceModuleActive=true;
            }
            else eventObject.SetActive(true);
            EventSystem.current=eventObject.GetComponent<EventSystem>();
        }
        private void ShowLicenses()
        {
            if(licensePage!=null){ActivatePage(WorkspacePage.Licenses);return;}
            RectTransform modal=CreatePage(WorkspacePage.Licenses);licensePage=modal.gameObject;
            Label(modal,"KoiSync 0.1.9 · 第三方组件许可",24,20,880,38,21,Accent,FontStyle.Bold);
            string text="KoiSync\nWebSocket4Net 0.15.2 / SuperSocket.ClientEngine 0.10.0 — Apache License 2.0\nLitJSON 0.19.0 — Public Domain\n这些通信组件内嵌于本 DLL。\n\n";
            text+="KKS_SexRobotController — MIT; hs2robotics / illusive-groax\n\n";
            foreach(string file in new[]{"Apache-2.0.txt","LitJSON-PublicDomain.txt","KKS-SexRobotController-MIT.txt"})
                using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("KoiSync.Licenses."+file))
                    if(stream!=null)using(var reader=new System.IO.StreamReader(stream))text+=reader.ReadToEnd()+"\n\n";
            RectTransform viewport=Rect(modal,"License viewport",24,78,948,556);Image(viewport,Color.white,false).raycastTarget=true;viewport.gameObject.AddComponent<Mask>().showMaskGraphic=false;
            ScrollRect scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;
            Text body=Label(viewport,text,0,0,930,6000,12,Color.white);body.supportRichText=false;
            body.rectTransform.sizeDelta=new Vector2(930,Mathf.Max(556,body.preferredHeight+24));scroll.content=body.rectTransform;scroll.viewport=viewport;
        }
        public void SetVisible(bool visible)
        {
            if(Visible==visible)return;
            if(visible&&!AnyWindowOpen)window.SetActive(true);
            Visible=visible;
            if(alignmentGizmo!=null){alignmentGizmo.Editing=AlignmentEditing;alignmentGizmo.CancelDrag();alignmentGizmo.Redraw();}
            GameHooks.BlockCamera=visible;
            GameHooks.BlockShortcuts=visible;GameHooks.CameraCapture=CaptureSceneInput;
            if(visible){previousEventSystem=EventSystem.current;EnsureEventSystem();oldLock=Cursor.lockState;oldCursor=Cursor.visible;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
            else
            {
                ReleaseVrInput();
                DismissDropdowns();
                if(eventObject!=null)eventObject.SetActive(false);
                if(previousEventSystem!=null&&previousEventSystem.isActiveAndEnabled)EventSystem.current=previousEventSystem;
                previousEventSystem=null;Cursor.lockState=oldLock;Cursor.visible=oldCursor;
            }
            canvasObject.SetActive(visible);
            if(visible&&vrFrame!=null)UpdateVr(vrFrame,true);
        }
        private bool AnyWindowOpen{get{return window.activeSelf||hudObject!=null&&hudObject.activeSelf||alignmentWindow!=null&&alignmentWindow.gameObject.activeSelf||timelineWindow!=null&&timelineWindow.gameObject.activeSelf||studioDiagnosisWindow!=null&&studioDiagnosisWindow.gameObject.activeSelf;}}
        private Candidate MonitoredSource{get{return LaboratoryOpen?alignmentGizmo.Preview:host.OutputSource;}}
        private void ShowWorkspace(){window.SetActive(true);SetVisible(true);window.transform.SetAsLastSibling();DismissDropdowns();}
        internal void ToggleWorkspace(){if(WorkspaceVisible)CloseWorkspace();else ShowWorkspace();}
        private void CloseWorkspace(){DismissDropdowns();window.SetActive(false);HideIfEmpty();}
        private void ShowHud(){hudObject.SetActive(true);SetVisible(true);hudObject.transform.SetAsLastSibling();}
        internal void ToggleHud(){if(hudObject!=null&&hudObject.activeSelf)CloseHud();else ShowHud();}
        private void CloseHud(){hudObject.SetActive(false);HideIfEmpty();}
        private void HideIfEmpty(){if(!AnyWindowOpen)SetVisible(false);}
        private void LateUpdate()
        {
            if(Visible){EnsureEventSystem();if(GameHooks.BlockCamera){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}RefreshFloatingTools();}
        }
        private void UpdateRange()
        {
            if(building||outputRange==null)return;
            float lo=outputRange.Lower,hi=outputRange.Upper;
            host.Pipeline.Minimum=lo;host.Pipeline.Maximum=hi;
            rangeLabel.text="输出范围  "+Mathf.RoundToInt(lo*999).ToString("D3")+" — "+Mathf.RoundToInt(hi*999).ToString("D3")+" · 双手柄";
        }
        private void TransportChanged(int index)
        {
            if(building||endpoint==null)return;
            connectionError="";
            host.PauseOutput("已更换连接方式");host.Devices.Disconnect();
            bool wasBuilding=building;building=true;device.value=index==2?1:0;building=wasBuilding;if(savedDevice!=null)savedDevice.Value=device.value;
            addresses[previousTransport]=endpoint.text;arguments[previousTransport]=argument.text;
            previousTransport=index;endpoint.text=addresses[index];argument.text=arguments[index];
            endpointCaption.text=index==0?"串口名称 / 波特率":index==1?"设备 IP / UDP 端口":"本机 WebSocket / 设备索引";
            if(savedTransport!=null)savedTransport.Value=index;ResumeAutoConnection();
        }
        private void Connect(){Connect(false);}
        private void Connect(bool automatic)
        {
            if(!automatic)autoPaused=true;
            if(host.Devices.Connecting){connectionError="";host.Stop("已取消连接");host.Devices.Disconnect();Refresh();return;}
            if(host.Devices.Connected){host.Stop("已断开设备");host.Devices.Disconnect();return;}
            if(host.Demo){host.Pipeline.Status="请退出演示数据后连接设备";return;}
            connectionError="";
            int number;if(!int.TryParse(argument.text,out number)||number<0){connectionError="连接参数必须为非负整数";return;}
            if(transport.value==0&&(number<1200||number>2000000)||transport.value==1&&(number<1||number>65535))
            {connectionError="波特率或 UDP 端口超出范围";return;}
            try
            {
                host.PauseOutput("连接中 · 保留同步启用状态");
                host.Devices.Connect(new DeviceSettings{Transport=transport.value,Endpoint=endpoint.text.Trim(),Argument=number,Kind=DeviceKind});
                host.Config.Bind("Device","Endpoint"+transport.value,"","").Value=endpoint.text.Trim();
                host.Config.Bind("Device","Argument"+transport.value,"","").Value=argument.text;
                savedTransport.Value=transport.value;savedDevice.Value=device.value;
                Refresh();
            }
            catch(Exception ex){connectionError=ex.Message;}
        }
        internal void Refresh()
        {
            if(building)return;refreshing=true;
            if(contactModelStatus!=null)contactModelStatus.text=host.ContactPreviewStatus;
            if(host.IsMainGame)RefreshCompactOverview();
            else
            {
            context.text=(host.Demo?"演示数据":host.Frame.Context)+"  /  "+host.Frame.Scene;
            sourceState.text=host.Frame.State+(TimelineState.HasActorTracks?" · "+TimelineState.Description:"");
            StringBuilder signature=new StringBuilder();foreach(var c in host.Frame.Candidates)signature.Append(c.Id).Append('|');
            bool sourcePopup=((WorkspaceDropdown)sources).PopupOpen;
            if(host.StudioAdapter!=null&&!host.Demo)RefreshOverviewPairs();
            else if(!sourcePopup&&sourceSignature!=signature.ToString())
            {
                sourceSignature=signature.ToString();overviewSourceIds.Clear();sources.options.Clear();sources.options.Add(new Dropdown.OptionData("选择运动源…"));
                foreach(var c in host.Frame.Candidates){overviewSourceIds.Add(c.Id);sources.options.Add(new Dropdown.OptionData(c.Label));}
            }
            if(!sourcePopup){sources.value=overviewSourceIds.IndexOf(host.SelectedId)+1;sources.RefreshShownValue();}
            var monitored=MonitoredSource;
            bones.text=monitored==null?"未选中有效骨骼":monitored.ReferenceBone+" → "+monitored.TargetBone;
            debug.isOn=host.DebugVisible;
            if(axis.value!=(int)host.Axis)axis.value=(int)host.Axis;
            double[] r=host.Pipeline.Raw;
            RefreshSkeleton();
            RefreshAxisMonitor();
            RefreshNative();
            bool native=host.IsMainGame&&!host.Demo;
            sources.gameObject.SetActive(!native);nativeSourceButton.gameObject.SetActive(native);
            bool referenceFrame=native||!host.Demo&&host.Pipeline.NativeMode;
            axis.gameObject.SetActive(!referenceFrame);nativeAxisNote.gameObject.SetActive(referenceFrame);
            nativeAxisNote.text=native?"本体参考轴 · 自动":"绑定参考轴 · 自动";
            string[] input=new string[6],mappedText=new string[6];
            for(int i=0;i<6;i++){input[i]=monitored==null?"—":ChannelMonitor.InputText(host.Pipeline,i);mappedText[i]=monitored==null?"—":ChannelMonitor.MappedText(host.Pipeline,i);}
            raw.text=string.Format("L0 {0}      L1 {1}      L2 {2}\nR0 {3}      R1 {4}      R2 {5}",input[0],input[1],input[2],input[3],input[4],input[5]);
            rawDetail.text=monitored==null?"原始值：无来源":string.Format(CultureInfo.InvariantCulture,"原始 L0 {0:F4} u · R0 {1:F1}° · 完整原始值见六轴监控",r[0],r[3]);
            RefreshStudio();
            RefreshStudioCharacterReplacement();
            RefreshExperiment();
            values.text=string.Format("L0 {0}    L1 {1}    L2 {2}    R0 {3}    R1 {4}    R2 {5}",mappedText[0],mappedText[1],mappedText[2],mappedText[3],mappedText[4],mappedText[5]);
            status.text=host.Pipeline.Status;command.text=host.Devices.LastCommand;
            }
            RefreshSkeleton();RefreshAxisMonitor();RefreshNative();FlushGlobalSettings();RefreshStrokeSettings();RefreshTransitions();
            connection.text=connectionError.Length>0?connectionError:host.Devices.Status+(transport.value==0?"\n"+scanStatus:transport.value==2?"\n先在 Central 配对；此路径使用 L0。":"");
            connect.GetComponentInChildren<Text>().text=host.Devices.Connecting?"取消连接":host.Devices.Connected?"断开设备":"连接设备";
            arm.interactable=!host.Demo&&!host.Sync.Enabled;
            arm.GetComponentInChildren<Text>().text=host.Demo?"演示模式":host.Sync.Enabled?"同步已启用":"启用同步";
            RefreshPlaybackProfiles();RefreshChannelBanks();
            calibrate.interactable=host.Pipeline.HasLiveSample&&!host.Pipeline.Calibrating;
            calibrate.GetComponentInChildren<Text>().text=!host.Pipeline.HasLiveSample?"等待活动场景":host.Pipeline.NativeMode?"重置自动范围":host.Pipeline.Calibrating?"正在采集动作范围…":"采集范围 · 6 秒";
            int[] supported=TCode.SupportedAxes(DeviceKind);
            for(int i=0;i<6;i++)if(channels[i]!=null)channels[i].interactable=i==0||transport.value!=2&&Array.IndexOf(supported,i)>=0;
            refreshing=false;
        }
        internal void PushCurve(){if(chart==null)return;if(host.Pipeline.Calibrated)chart.Push(host.Pipeline.Values);else chart.Clear();}
        internal bool CheckControls()
        {
            bool wasVisible=Visible;SetVisible(false);bool hidden=!canvasObject.activeSelf;SetVisible(true);
            if(host.IsMainGame){SetVisible(wasVisible);return hidden&&outputRange!=null&&device!=null&&transport!=null;}
            sources.Show();GameObject list=GameObject.Find("Dropdown List");bool dropdown=list!=null;sources.Hide();if(list!=null)list.SetActive(false);
            int before=(int)host.Axis;axis.value=(before+1)%3;bool changed=(int)host.Axis==(before+1)%3;axis.value=before;
            bool wasDebug=host.DebugVisible;debug.isOn=!wasDebug;bool toggled=host.DebugVisible==!wasDebug;debug.isOn=wasDebug;
            if(!wasVisible)SetVisible(false);
            return hidden&&dropdown&&changed&&toggled&&!host.Devices.Connected;
        }
        internal void Cleanup()
        {
            if(alignmentGizmo!=null)Destroy(alignmentGizmo);
            ReleaseVrInput();
            if(Visible)SetVisible(false);
            if(canvasObject!=null)Destroy(canvasObject);if(hudObject!=null)Destroy(hudObject);if(eventObject!=null)Destroy(eventObject);
            if(rounded!=null)Destroy(rounded);if(roundedTexture!=null)Destroy(roundedTexture);if(font!=null)Destroy(font);
        }
        private RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
        {
            GameObject go=new GameObject(name,typeof(RectTransform));RectTransform r=go.GetComponent<RectTransform>();r.SetParent(parent,false);
            r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;
        }
        private Image Image(RectTransform r,Color color,bool round)
        {
            Image image=r.gameObject.AddComponent<Image>();image.raycastTarget=false;image.color=color;if(round){image.sprite=rounded;image.type=UnityEngine.UI.Image.Type.Sliced;}return image;
        }
        private RectTransform Panel(Transform parent,float x,float y,float w,float h,Color color)
        {RectTransform r=Rect(parent,"Panel",x,y,w,h);Image(r,color,true);return r;}
        private Text Label(Transform parent,string value,float x,float y,float w,float h,int size,Color color,FontStyle style=FontStyle.Normal)
        {
            Text t=Rect(parent,value,x,y,w,h).gameObject.AddComponent<Text>();t.font=font;t.fontSize=size;t.color=color;t.text=value;t.fontStyle=style;t.raycastTarget=false;t.verticalOverflow=VerticalWrapMode.Truncate;return t;
        }
        private Button Button(Transform parent,string text,float x,float y,float w,float h,UnityEngine.Events.UnityAction action,Color? background=null)
        {
            RectTransform r=Panel(parent,x,y,w,h,background??new Color32(41,60,79,255));Button b=r.gameObject.AddComponent<Button>();b.targetGraphic=r.GetComponent<Image>();b.onClick.AddListener(action);
            b.targetGraphic.raycastTarget=true;StyleControl(b);
            Text t=Label(r,text,5,0,w-10,h,13,Color.white);t.alignment=TextAnchor.MiddleCenter;return b;
        }
        private Toggle Toggle(Transform parent,string text,float x,float y,float w,bool value,UnityEngine.Events.UnityAction<bool> action)
        {
            RectTransform r=Rect(parent,text,x,y,w,24);Toggle toggle=r.gameObject.AddComponent<Toggle>();
            Image(r,Color.clear,false).raycastTarget=true;
            RectTransform box=Panel(r,0,2,19,19,new Color32(52,72,92,255));RectTransform check=Panel(box,4,4,11,11,Accent);
            toggle.targetGraphic=box.GetComponent<Image>();toggle.graphic=check.GetComponent<Image>();toggle.isOn=value;
            StyleControl(toggle);
            Label(r,text,27,1,w-27,23,12,Color.white);toggle.onValueChanged.AddListener(action);return toggle;
        }
        private InputField Input(Transform parent,float x,float y,float w,string initial)
        {
            RectTransform r=Panel(parent,x,y,w,33,new Color32(13,25,38,255));InputField field=r.gameObject.AddComponent<InputField>();
            field.targetGraphic=r.GetComponent<Image>();field.targetGraphic.raycastTarget=true;StyleControl(field);
            Text text=Label(r,"",8,0,w-16,33,12,Color.white);text.alignment=TextAnchor.MiddleLeft;text.supportRichText=false;
            field.textComponent=text;field.text=initial;field.characterLimit=180;return field;
        }
        private Slider Slider(Transform parent,float x,float y,float w,float value,UnityEngine.Events.UnityAction<float> action)
        {
            RectTransform r=Rect(parent,"Range",x,y,w,20);Image(r,Color.clear,false).raycastTarget=true;Panel(r,0,8,w,5,new Color32(53,73,92,255));
            Slider slider=r.gameObject.AddComponent<Slider>();RectTransform track=Rect(r,"Track",5,0,w-10,18);
            RectTransform handle=Panel(track,0,1,13,16,Accent);handle.pivot=new Vector2(.5f,.5f);
            slider.handleRect=handle;slider.targetGraphic=handle.GetComponent<Image>();StyleControl(slider);slider.minValue=0;slider.maxValue=1;slider.value=value;slider.onValueChanged.AddListener(action);return slider;
        }
        private Dropdown Dropdown(Transform parent,float x,float y,float w,string[] choices,UnityEngine.Events.UnityAction<int> changed)
        {
            RectTransform r=Panel(parent,x,y,w,34,new Color32(40,58,77,255));WorkspaceDropdown dropdown=r.gameObject.AddComponent<WorkspaceDropdown>();dropdown.BlockerArea=parent.IsChildOf(window.transform)?pageHost:parent as RectTransform;
            dropdown.targetGraphic=r.GetComponent<Image>();dropdown.targetGraphic.raycastTarget=true;StyleControl(dropdown);
            Text caption=Label(r,"",9,0,w-31,34,12,Color.white);caption.alignment=TextAnchor.MiddleLeft;
            Label(r,"▾",w-22,5,20,24,15,Muted);dropdown.captionText=caption;
            RectTransform template=Panel(r,0,36,w,234,new Color32(34,52,72,255));template.name="Template";
            ScrollRect scroll=template.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=46;scroll.inertia=false;
            RectTransform viewport=Rect(template,"Viewport",2,2,w-4,230);Image(viewport,Color.white,false).raycastTarget=true;viewport.gameObject.AddComponent<Mask>().showMaskGraphic=false;
            RectTransform content=Rect(viewport,"Content",0,0,w-4,46);
            RectTransform item=Rect(content,"Item",0,0,w-4,46);Toggle toggle=item.gameObject.AddComponent<Toggle>();
            Image bg=Image(item,new Color32(41,66,84,255),false);bg.raycastTarget=true;toggle.targetGraphic=bg;StyleControl(toggle);
            RectTransform check=Panel(item,5,19,6,8,Accent);toggle.graphic=check.GetComponent<Image>();
            Text itemText=Label(item,"",17,3,w-24,40,12,Color.white);itemText.alignment=TextAnchor.MiddleLeft;itemText.supportRichText=false;
            scroll.viewport=viewport;scroll.content=content;
            dropdown.template=template;dropdown.itemText=itemText;
            dropdown.AddOptions(new List<string>(choices));dropdown.onValueChanged.AddListener(changed);template.gameObject.SetActive(false);return dropdown;
        }
        private static void StyleControl(Selectable control)
        {
            ColorBlock colors=control.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(1.22f,1.22f,1.22f,1);colors.pressedColor=new Color(.65f,.85f,.82f,1);colors.disabledColor=new Color(.48f,.48f,.48f,.7f);colors.fadeDuration=.08f;control.colors=colors;
        }
    }
    public sealed class PanelDrag:MonoBehaviour,IBeginDragHandler,IDragHandler
    {
        public RectTransform Target;
        public void OnBeginDrag(PointerEventData data){if(data.button==PointerEventData.InputButton.Left)Target.SetAsLastSibling();}
        public void OnDrag(PointerEventData data)
        {
            if(data.button!=PointerEventData.InputButton.Left)return;
            Canvas canvas=GetComponentInParent<Canvas>();
            if(canvas.rootCanvas.renderMode==RenderMode.WorldSpace)
            {
                Vector2 previous,current;var parent=Target.parent as RectTransform;
                if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,data.position-data.delta,data.pressEventCamera,out previous)||
                   !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,data.position,data.pressEventCamera,out current))return;
                Target.anchoredPosition+=current-previous;
            }
            else Target.anchoredPosition+=data.delta/canvas.scaleFactor;
            Vector2 bounds=((RectTransform)canvas.transform).rect.size*.5f-Target.rect.size*Target.localScale.x*.5f;
            Target.anchoredPosition=new Vector2(Mathf.Clamp(Target.anchoredPosition.x,-Mathf.Max(0,bounds.x),Mathf.Max(0,bounds.x)),Mathf.Clamp(Target.anchoredPosition.y,-Mathf.Max(0,bounds.y),Mathf.Max(0,bounds.y)));
        }
    }
    public sealed class SignalGraphic:MaskableGraphic
    {
        private readonly Queue<double[]> history=new Queue<double[]>();
        private static readonly Color[] colors={new Color32(107,229,202,255),new Color32(240,183,104,255),new Color32(180,148,242,255),new Color32(72,171,153,255),new Color32(174,127,63,255),new Color32(129,99,183,255)};
        public void Push(double[] frame){history.Enqueue((double[])frame.Clone());while(history.Count>240)history.Dequeue();SetVerticesDirty();}
        public void Clear(){if(history.Count==0)return;history.Clear();SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();Rect r=rectTransform.rect;
            for(int i=0;i<5;i++)Line(vh,new Vector2(r.xMin,r.yMin+r.height*i/4),new Vector2(r.xMax,r.yMin+r.height*i/4),new Color32(48,67,85,255),1);
            double[][] frames=history.ToArray();
            for(int channel=5;channel>=0;channel--)for(int i=1;i<frames.Length;i++)
                Line(vh,new Vector2(r.xMin+(i-1)*r.width/239,r.yMin+(float)frames[i-1][channel]*r.height),new Vector2(r.xMin+i*r.width/239,r.yMin+(float)frames[i][channel]*r.height),colors[channel],channel==0?2:1.1f);
        }
        private static void Line(VertexHelper vh,Vector2 a,Vector2 b,Color color,float width)
        {
            Vector2 delta=b-a;if(delta.sqrMagnitude<0.00001f)return;Vector2 normal=new Vector2(-delta.y,delta.x).normalized*width*.5f;int start=vh.currentVertCount;
            vh.AddVert(a-normal,color,Vector2.zero);vh.AddVert(a+normal,color,Vector2.zero);vh.AddVert(b+normal,color,Vector2.zero);vh.AddVert(b-normal,color,Vector2.zero);
            vh.AddTriangle(start,start+1,start+2);vh.AddTriangle(start,start+2,start+3);
        }
    }
}
