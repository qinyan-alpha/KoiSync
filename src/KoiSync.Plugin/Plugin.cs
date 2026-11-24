using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx;
using BepInEx.Configuration;
using KoiSync.Core;
using KoiSync.Wire;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KoiSync.Plugin
{
    [BepInPlugin("local.koisync", "KoiSync", "0.1.38")]
    [BepInProcess("Koikatu")]
    [BepInProcess("KoikatuVR")]
    [BepInProcess("CharaStudio")]
    [BepInDependency("com.joan6694.illusionplugins.timeline",BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.bepis.bepinex.sideloader",BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly string session = Guid.NewGuid().ToString("N");
        private ISceneAdapter adapter;
        private double nextScan, nextSend, nextWarning;
        private long sequence;
        private DebugGeometry geometry;
        private ContactDevicePreview contactPreview;
        internal string ContactPreviewStatus{get{return contactPreview==null?"等待设备预览":contactPreview.Status;}}
        internal readonly MotionPipeline Pipeline = new MotionPipeline();
        internal readonly DebugViewSettings ViewSettings = new DebugViewSettings();
        internal DeviceHub Devices;
        internal NativeSettings Native;
        internal StudioSettings StudioOptions;
        internal StudioExperiment Experiment;
        internal VrSettings VrOptions;
        internal readonly VrRuntime Vr=new VrRuntime();
        internal readonly StudioFocus Focus=new StudioFocus();
        internal readonly SourceHandoff Handoff=new SourceHandoff();
        internal readonly SyncSession Sync=new SyncSession();
        internal readonly FunscriptPlayback ScriptPlayback=new FunscriptPlayback();
        internal Candidate ScriptSource;
        internal Candidate OutputSource{get{return ScriptPlayback.Enabled&&!Demo?ScriptSource:Selected;}}
        internal PlaybackProfiles Profiles;
        internal GlobalSettings Global;
        private bool vrRecenterHeld;
        private string pushedNativeKey="";
        private GameHooks hooks;
        internal string PanelShortcut{get{return panelKey.Value.ToString();}}
        internal string DebugShortcut{get{return debugKey.Value.ToString();}}
        internal StudioAdapter StudioAdapter{get{return adapter as StudioAdapter;}}
        internal bool IsMainGame{get{return adapter is MainGameAdapter;}}
        internal NativePlayback NativePlayback{get{var main=adapter as MainGameAdapter;return main==null?null:main.Playback;}}
        internal Candidate PreviewNative(MeasurementProfile measurement)
        {
            var native=adapter as MainGameAdapter;if(native==null)return null;
            var frame=new Snapshot{Active=adapter.Active};native.Sample(frame,Native,Profiles,measurement);
            return frame.Candidates.Count==0?null:frame.Candidates[0];
        }
        internal Snapshot Frame = new Snapshot();
        internal Candidate Selected;
        internal string SelectedId = "";
        internal StrokeAxis Axis;
        internal bool DebugVisible = true, Demo;
        private KoiSyncPanel panel;
        private double calibrationEnd, nextUi;
        private ConfigEntry<KeyboardShortcut> panelKey, debugKey;
        internal OutputChannels OutputChannels;
        private bool smoke;
        private double captureAt;
        private int strokeMemoryRevision;
        private double nextStrokeSave;
        internal bool StudioMutationActive { get; private set; }

        private void Awake()
        {
            EmbeddedDependencies.Initialize();
            bool studio = Process.GetCurrentProcess().ProcessName.Equals("CharaStudio", StringComparison.OrdinalIgnoreCase);
            adapter = studio ? (ISceneAdapter)new StudioAdapter() : new MainGameAdapter(Process.GetCurrentProcess().ProcessName.Equals("KoikatuVR",StringComparison.OrdinalIgnoreCase));
            Native=new NativeSettings(Config);
            OutputChannels=new OutputChannels(Config,Pipeline.EnabledAxes);
            StudioOptions=new StudioSettings(Config);
            Experiment=new StudioExperiment(Config);
            Profiles=new PlaybackProfiles(Paths.GameRootPath);
            Global=new GlobalSettings(Config);
            // The travel / pull-out / idle / climax configuration is global: every animation follows it.
            Profiles.Global=Global;Profiles.PushGlobalStroke(Global);Global.ApplyTo(Pipeline);
            Sync.Enabled=Config.Bind("Output","AutoEnableSync",true,"Keep synchronization enabled through animation changes, pauses and scene transitions; unavailable data waits without movement. Manual Stop disables the session.").Value;
            VrOptions=new VrSettings(Config);
            Devices = new DeviceHub(message=>Logger.LogInfo("[Device] "+message));
            panelKey=Config.Bind("UI","PanelKey",new KeyboardShortcut(KeyCode.F9),"Toggle KoiSync panel");
            debugKey=Config.Bind("UI","DebugKey",new KeyboardShortcut(KeyCode.F8),"Toggle 3D channel geometry");
            ViewSettings.Load(Config);
            geometry = gameObject.AddComponent<DebugGeometry>();geometry.Settings=ViewSettings;geometry.MonitorPipeline=Pipeline;
            contactPreview=gameObject.AddComponent<ContactDevicePreview>();contactPreview.Settings=ViewSettings;
            panel=gameObject.AddComponent<KoiSyncPanel>();panel.Initialize(this);
            hooks=new GameHooks(this);
            smoke=Array.IndexOf(Environment.GetCommandLineArgs(),"--koisync-smoke")>=0;
            if(smoke){Demo=true;captureAt=clock.Elapsed.TotalSeconds+12;}
            panel.SetVisible(smoke||Config.Bind("UI","ShowOnStart",false,"Open the panel when the game starts").Value);
            Logger.LogInfo("KoiSync 0.1.38 | " + adapter.Context + " | panel "+PanelShortcut+" / VR "+VrOptions.ToggleKey.Value+"; output disconnected");
            StartCoroutine(SampleAfterAnimation());
        }

        private void Update()
        {
            if(hooks!=null)hooks.Refresh();
            // Safety net for the removed user-facing demo entry: a synthetic frame must never shadow a live scene.
            if(Demo&&!smoke&&adapter!=null&&adapter.Active){Logger.LogWarning("演示数据已自动退出：检测到真实场景");ToggleDemo();}
            bool controllerToggle=Vr.Poll(VrOptions);
            geometry.RenderCamera=Vr.Frame.Active?Vr.Frame.Camera:null;
            panel.UpdateVr(Vr.Frame,false);
            if(VrOptions.RecenterKey.Value.IsDown()){panel.SetVisible(true);panel.RecenterVr();}
            else if(panelKey.Value.IsDown()||VrOptions.ToggleKey.Value.IsDown()||controllerToggle)panel.ToggleWorkspace();
            bool recenterButton=Vr.ButtonHeld(VrOptions.RecenterButton.Value);
            if(recenterButton&&!vrRecenterHeld){panel.SetVisible(true);panel.RecenterVr();}
            vrRecenterHeld=recenterButton;
            if(debugKey.Value.IsDown())DebugVisible=!DebugVisible;
            panel.PollTimelineInput();if(NativePlayback!=null)NativePlayback.Hold();
            PollClimaxChord();panel.PollDeviceShortcut();panel.PollShortcutCapture();
            double now=clock.Elapsed.TotalSeconds;
            panel.UpdateAutoConnection(now);
            if(Pipeline.Calibrating&&now>=calibrationEnd)Pipeline.FinishCalibration();
            if(now>=nextUi){panel.Refresh();nextUi=now+0.1;}
            if(smoke&&now>=captureAt)
            {
                smoke=false;
                bool controls=panel.CheckControls();
                string output=System.IO.Path.Combine(Paths.GameRootPath,"KoiSync-ui-smoke.png");
                Application.CaptureScreenshot(output);
                DebugGeometry.CaptureFixture(System.IO.Path.Combine(Paths.GameRootPath,"KoiSync-3d-smoke.png"));
                Logger.LogInfo("KoiSync UI smoke: panel="+panel.Visible+", controls="+controls+", calibrated="+Pipeline.Calibrated+", connected="+Devices.Connected+", screenshot="+output);
            }
        }

        private IEnumerator SampleAfterAnimation()
        {
            WaitForEndOfFrame end = new WaitForEndOfFrame();
            while (true)
            {
                yield return end;
                double now = clock.Elapsed.TotalSeconds;
                if (now < nextSend) continue;
                nextSend = now + 1.0 / 60.0;
                if(StudioMutationActive)continue;
                try
                {
                    if (now >= nextScan)
                    {
                        adapter.Refresh(); nextScan = now + 1;
                        if(hooks!=null)hooks.Refresh();
                    }
                    Snapshot frame = new Snapshot();
                    frame.Session = session; frame.Sequence = sequence++; frame.SampleTime = now;
                    frame.Context = adapter.Context; frame.Scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                    frame.State = adapter.State; frame.Active = adapter.Active && Time.timeScale > 0;
                    MainGameAdapter native=adapter as MainGameAdapter;
                    if(native!=null)native.Sample(frame,Native,Profiles);
                    else ((StudioAdapter)adapter).Sample(frame,StudioOptions,Profiles.StudioMeasurement);
                    // A new animation starts from the global configuration; its own stroke memory is kept.
                    if(native!=null&&Profiles.NativeKey!=pushedNativeKey){pushedNativeKey=Profiles.NativeKey;Profiles.PushGlobalStroke(Global);Global.ApplyTo(Pipeline);}
                    if(Demo)frame=DemoFrame(now);
                    if(native!=null&&!Demo)SelectedId=frame.Candidates.Count>0?frame.Candidates[0].Id:"";
                    if(StudioAdapter!=null&&!Demo)UpdateStudioSource(frame,Vr.Frame.Active?Vr.Frame.Camera:Camera.main,now);
                    Frame=frame;
                    Selected=frame.Candidates.Find(c=>c.Id==SelectedId);
                    if(Selected==null&&SelectedId.Length==0&&frame.Candidates.Count==1&&(Demo||StudioAdapter==null||!StudioOptions.AutoSource.Value&&StudioAdapter.Sources.Count==1)){Selected=frame.Candidates[0];SelectedId=Selected.Id;}
                    if(StudioAdapter!=null)Experiment.Update(Demo?null:StudioAdapter.Find(SelectedId),Demo?null:Selected,Time.unscaledTime);
                    if(Selected!=null&&!Demo)
                    {
                        var segment=StudioAdapter==null?null:Profiles.CurrentSegment;
                        MeasurementProfile preset=StudioAdapter==null?Profiles.NativeMeasurement:Profiles.StudioMeasurement;
                        Selected.MotionKey+="|preset:"+(StudioAdapter==null?Profiles.NativeKey:Profiles.ScenePath+":"+(segment==null?"default":segment.Id))+":"+(preset==null?"":preset.MotionKey);
                    }
                    Candidate preview=panel.AlignmentPreview(Vr.Frame.Active?Vr.Frame.Camera:Camera.main);
                    if(StudioAdapter!=null){if(Selected!=null&&Selected.Native!=null)Selected.Native.RotationContext=Profiles.Scene;if(preview!=null&&preview.Native!=null)preview.Native.RotationContext=Profiles.Scene;}
                    bool output;
                    ScriptSource=null;
                    if(ScriptPlayback.Enabled&&!Demo)
                    {
                        bool available=IsMainGame?NativePlayback!=null&&NativePlayback.Available:TimelineState.Available;
                        bool playing=IsMainGame?available&&!NativePlayback.Paused:TimelineState.Playing;
                        double scriptTime=IsMainGame?(available?NativePlayback.Time:0):TimelineState.Time;
                        string scriptClock=IsMainGame?(available?NativePlayback.Key:""):Profiles.ScenePath;
                        ScriptSource=ScriptPlayback.Sample(scriptTime,available,playing,scriptClock);
                        frame.Active=available&&Time.timeScale>0;
                    }
                    if(panel.LaboratoryOpen||panel.LaboratoryBusy){Sync.Update(Pipeline,frame,preview,false,Axis);PauseOutput("3D 对齐编辑 · 预览，保留同步启用");output=false;}
                    else output=Demo?Pipeline.Update(frame,Selected,0,Axis):Sync.Update(Pipeline,frame,OutputSource,Devices.Connected,Axis);
                    // Persist the captured stroke travel now and then so the next session starts enhanced.
                    if(!Demo&&Selected!=null&&Selected.Native!=null&&Selected.Native.Stroke!=null)
                    {
                        int revision=Selected.Native.Stroke.MemoryRevision;
                        if(revision!=strokeMemoryRevision&&now>=nextStrokeSave){strokeMemoryRevision=revision;nextStrokeSave=now+10;Profiles.Changed(false);}
                    }
                    if(StudioAdapter!=null&&!Demo)
                    {
                        if(Selected==null&&!ScriptPlayback.Enabled)Pipeline.Status=Profiles.StudioSource==TimelineSegment.NoSource?"本段数据源：无 · 同步保持启用，等待下一段":StudioOptions.AutoSource.Value?Focus.Status:SelectedId.Length>0?"所选绑定不可用 · 查看工作室绑定页":StudioAdapter.Discovery;
                    }
                    if(output&&!Demo)
                    {
                        bool[] enabled=new bool[6];for(int i=0;i<6;i++)enabled[i]=Pipeline.EnabledAxes[i]&&Pipeline.AxisCalibrated[i];
                        Devices.Offer(Pipeline.Values,enabled,panel.DeviceKind);
                    }
                    else Devices.Stop();
                    bool geometryOn=DebugVisible&&ViewSettings.ShowWireframe;
                    geometry.LabelsVisible=geometryOn&&(!panel.WorkspaceVisible||panel.AlignmentEditing);
                    geometry.BpObservation=StudioAdapter!=null&&Experiment.ShowDirection?Experiment.Bp:null;
                    geometry.Display(Demo?null:panel.LaboratoryOpen?preview:Selected,geometryOn,(byte)Axis);
                    Candidate contactAnchor=Demo?null:panel.LaboratoryOpen?preview:Selected;
                    Candidate contactDrive=panel.LaboratoryOpen?preview:OutputSource;
                    contactPreview.Display(contactAnchor,Pipeline,contactDrive,geometryOn&&ViewSettings.ShowContactModel);
                    panel.SampleSkeleton();
                    panel.PushCurve();
                    panel.RecordMotionCurve();
                    if(smoke&&!Pipeline.Calibrated&&!Pipeline.Calibrating)Calibrate();
                }
                catch (Exception ex)
                {
                    PauseOutput("同步等待 · 采样异常");
                    if (now >= nextWarning) { Logger.LogWarning("Telemetry: " + ex.Message); nextWarning = now + 5; }
                }
            }
        }

        internal ChaControl[] InspectionActors()
        {
            if(Selected==null||Demo)return new ChaControl[0];
            if(StudioAdapter!=null)
            {
                StudioSource source=StudioAdapter.Find(SelectedId);var actors=new List<ChaControl>();
                if(source!=null){if(Alive(source.Source.Actor))actors.Add(source.Source.Actor);if(Alive(source.Target.Actor)&&!actors.Contains(source.Target.Actor))actors.Add(source.Target.Actor);}
                return actors.ToArray();
            }
            foreach(ActorPair pair in adapter.Pairs)
                if(Alive(pair.Reference)&&Alive(pair.Target)&&Selected.Id.StartsWith(pair.Reference.GetInstanceID()+":"+pair.Target.GetInstanceID()+":"))return new[]{pair.Reference,pair.Target};
            return new ChaControl[0];
        }
        private static bool Alive(ChaControl actor) { return actor != null && actor.gameObject.activeInHierarchy; }
        internal void UpdateStudioSource(Snapshot frame,Camera camera,double now)
        {
            TimelineSegment segment=Profiles==null?null:Profiles.CurrentSegment;
            string pinned=Profiles==null?"":Profiles.StudioSource;
            if(pinned==TimelineSegment.NoSource){SelectedId="none:"+(segment==null?"default":segment.Id);Focus.Reset();Focus.Status="本段数据源：无";return;}
            if(pinned.Length>0)
            {
                StudioSource bound=StudioAdapter.Sources.Find(s=>s.SavedKey==pinned);SelectedId=bound==null?"missing:"+(segment==null?"default":segment.Id):bound.Id;Focus.Reset();
                Focus.Status=bound==null?"本段指定的绑定未找到 · 请在分段页重新选择":"Timeline 分段 · "+bound.Label;return;
            }
            if(segment==null&&Profiles.Scene.Defaults==null&&!StudioOptions.AutoSource.Value){Focus.Reset();return;}
            string id=Focus.Choose(StudioAdapter,frame,camera,SelectedId,now);
            if(id==SelectedId)return;
            Devices.Stop();SelectedId=id;
        }
        internal void SetAutoSource(bool enabled)
        {
            PauseOutput(enabled?"已开启镜头自动选择":"已锁定当前绑定");StudioOptions.AutoSource.Value=enabled;Focus.Reset();
            var segment=Profiles==null?null:Profiles.CurrentSegment;var source=StudioAdapter==null?null:StudioAdapter.Find(SelectedId);
            if(segment!=null&&(enabled||source!=null)){segment.Source=enabled?"":source.SavedKey;segment.Label=enabled?"":source.Label;Profiles.Changed(true);}
            else if(segment==null&&Profiles!=null&&(enabled||source!=null)){Profiles.Scene.DefaultSource=enabled?"":source.SavedKey;Profiles.Scene.DefaultLabel=enabled?"":source.Label;Profiles.Changed(true);}
        }
        internal void Select(string id)
        {
            PauseOutput("已选择运动源");if(StudioAdapter!=null)
            {
                var segment=Profiles==null?null:Profiles.CurrentSegment;StudioSource source=StudioAdapter.Find(id);
                if(segment!=null&&source!=null){segment.Source=source.SavedKey;segment.Label=source.Label;Profiles.Changed(true);}
                else {StudioOptions.AutoSource.Value=false;if(source!=null){Profiles.Scene.DefaultSource=source.SavedKey;Profiles.Scene.DefaultLabel=source.Label;Profiles.Changed(true);}}
            }
            Focus.Reset();SelectedId=id;
        }
        internal void RescanStudio(){PauseOutput("重新扫描工作室绑定");if(StudioAdapter!=null)StudioAdapter.Refresh();nextScan=clock.Elapsed.TotalSeconds+1;}
        internal void BeginStudioMutation(string reason)
        {if(StudioAdapter==null)throw new InvalidOperationException("此操作仅适用于 CharaStudio");StudioMutationActive=true;PauseOutput(reason);}
        internal void EndStudioMutation(string reason)
        {StudioMutationActive=false;if(StudioAdapter!=null){StudioAdapter.Refresh();nextScan=clock.Elapsed.TotalSeconds+1;}Pipeline.Status=reason;}
        internal void Calibrate(){Handoff.Cancel();if(!Pipeline.HasLiveSample){Pipeline.Status="当前没有可校准的活动数据";return;}Devices.Stop();Pipeline.StartCalibration();calibrationEnd=clock.Elapsed.TotalSeconds+6;}
        internal void EnableSync(){if(Demo)return;Sync.Enabled=true;Pipeline.Status="同步已启用 · 等待有效数据";}
        internal void PauseOutput(string reason){Pipeline.Disarm(reason);if(Devices!=null)Devices.Stop();}
        internal void Stop(string reason){Sync.Stop();Handoff.Cancel();PauseOutput(reason);}
        internal void ToggleDemo(){Stop("已切换数据模式");Devices.Disconnect();Demo=!Demo;SelectedId="";Pipeline.Reset(Demo?"演示数据 · 不连接设备":"等待真实场景");}
        // Combo shortcut: both mouse buttons on the desktop, or both VR triggers, jump the current
        // animation straight to its climax. SetPlay is the game's own state switch.
        private static readonly string[] ClimaxStates={"M_IN_Start","IN_Start","Orgasm_Start","Orgasm","WF_IN_Start","WS_IN_Start","SF_IN_Start","SS_IN_Start","M_OUT_Start","OUT_Start"};
        private bool climaxChord;
        private BepInEx.Configuration.ConfigEntry<bool> climaxMouse,climaxTriggers;
        private BepInEx.Configuration.ConfigEntry<BepInEx.Configuration.KeyboardShortcut> climaxKey;
        private void PollClimaxChord()
        {
            if(climaxKey==null)
            {
                climaxKey=Config.Bind("UI","ClimaxKey",new BepInEx.Configuration.KeyboardShortcut(KeyCode.None),"Keyboard shortcut for the climax animation (None disables it)");
                climaxMouse=Config.Bind("UI","ClimaxMouseChord",true,"Both mouse buttons together trigger the climax animation in the desktop main game");
                climaxTriggers=Config.Bind("VR","ClimaxBothTriggers",true,"Both VR controller triggers together trigger the climax animation");
            }
            if(!IsMainGame||adapter==null||!adapter.Active){climaxChord=false;return;}
            bool chord=climaxTriggers.Value&&Vr.Frame.BothTriggers||climaxMouse.Value&&!Vr.Frame.Active&&UnityEngine.Input.GetMouseButton(0)&&UnityEngine.Input.GetMouseButton(1);
            bool key=climaxKey.Value.MainKey!=KeyCode.None&&climaxKey.Value.IsDown();
            if((chord&&!climaxChord||key)&&(panel==null||!panel.PointerOverPanel))TriggerClimax();
            climaxChord=chord;
        }
        internal void TriggerClimax()
        {
            object action=GameHooks.CurrentAction;
            if(action==null){Logger.LogInfo("高潮快捷键：尚未捕获到本体动作控制器");return;}
            Animator animator=null;
            foreach(ActorPair pair in adapter.Pairs){if(pair.Target!=null)animator=pair.Target.animBody;break;}
            string state=null;
            if(animator!=null&&animator.runtimeAnimatorController!=null)
                foreach(string candidate in ClimaxStates)
                    if(animator.HasState(0,Animator.StringToHash(candidate))){state=candidate;break;}
            if(state==null)state=ClimaxStates[0];
            try
            {
                var method=action.GetType().GetMethod("SetPlay",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
                if(method==null){Logger.LogWarning("高潮快捷键：当前动作控制器没有 SetPlay");return;}
                method.Invoke(action,new object[]{state,true});
                Logger.LogInfo("高潮快捷键 → "+state);
            }
            catch(Exception ex){Logger.LogWarning("高潮快捷键失败："+ex.GetBaseException().Message);}
        }
        private Snapshot DemoFrame(double now)
        {
            Snapshot frame=new Snapshot{Session=session+"demo",Context="演示",Scene="数学示例",State="演示数据 · 不连接设备",Sequence=sequence++,SampleTime=now,Active=true};
            Candidate c=new Candidate{Id="demo",Label="示例参考点 → 示例目标点",ReferenceBone="reference",TargetBone="target",Active=true};
            c.Target.X=(float)(Math.Sin(now*1.7)*0.025);c.Target.Y=(float)(Math.Cos(now*1.3)*0.018);c.Target.Z=(float)(0.12+Math.Sin(now*3)*0.07);
            Quaternion q=Quaternion.Euler((float)Math.Sin(now)*18,(float)Math.Cos(now*1.5)*12,(float)Math.Sin(now*1.2)*24);
            c.Target.Qx=q.x;c.Target.Qy=q.y;c.Target.Qz=q.z;c.Target.Qw=q.w;frame.Candidates.Add(c);return frame;
        }
        private void OnDestroy(){if(NativePlayback!=null)NativePlayback.Release();if(hooks!=null)hooks.Dispose();if(Devices!=null)Devices.Dispose();if(panel!=null)panel.Cleanup();ShaftGeometry.Clear();}
    }

    internal sealed class BoneMap
    {
        private readonly Dictionary<string, Transform> map = new Dictionary<string, Transform>();
        public BoneMap(Transform root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (!map.ContainsKey(t.name)) map.Add(t.name, t);
        }
        public Transform Get(params string[] aliases)
        {
            foreach (string alias in aliases) { Transform t; if (map.TryGetValue(alias, out t) && t != null) return t; }
            return null;
        }
    }
    internal sealed class ActorPair
    {
        public ChaControl Reference, Target;
        public ActorPair(ChaControl reference, ChaControl target) { Reference = reference; Target = target; }
    }
    internal interface ISceneAdapter
    {
        string Context { get; }
        string State { get; }
        bool Active { get; }
        List<ActorPair> Pairs { get; }
        void Refresh();
    }
}
