using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using KoiSync.Core;
using KoiSync.Plugin;
using UnityEngine;
using GameStudio=global::Studio.Studio;
using OCIChar=global::Studio.OCIChar;

namespace KoiSync.Studio
{
    [BepInPlugin("local.koisync.studio","KoiSync Studio","1.0.0")]
    [BepInProcess("CharaStudio")]
    [BepInDependency("com.bepis.bepinex.sideloader",BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.joan6694.illusionplugins.timeline",BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.bepis.bepinex.extendedsave")]
    public sealed partial class Plugin:BaseUnityPlugin
    {
        private static Plugin current;
        private readonly Harmony harmony=new Harmony("local.koisync.studio");
        internal readonly StudioDrive Drive=new StudioDrive();
        internal DeviceHub Devices;
        internal StudioBinding Binding;
        internal NativeMotionSample Sample;
        internal NativeMotionResult Measured;
        internal readonly StudioClock Clock=new StudioClock();
        private readonly StudioBodyGrind bodyGrind=new StudioBodyGrind();
        internal StudioBodyGrind BodyGrind {get{return bodyGrind;}}
        private StudioGrindFrame activeGrindFrame;
        private StudioPreview preview;
        private readonly List<ChaControl> actors=new List<ChaControl>();
        private string[] actorNames=new string[0],ports=new string[0];
        private readonly object scanGate=new object();
        private string[] pendingPorts;
        private string pendingError;
        private bool scanPending,scanning;
        private int sourceIndex,targetIndex,transport;
        private OpeningKind fallback;
        private bool manualOpening;
        private string endpoint,argument,rodMillimetres="0",status=UiText.T("加载场景后，读取角色并绑定。");
        private bool showWindow=false,showPreview=true,xray=true,sendOutput;
        private ConfigEntry<int> savedTransport;
        private readonly ConfigEntry<string>[] savedEndpoint=new ConfigEntry<string>[3],savedArgument=new ConfigEntry<string>[3];
        private readonly ConfigEntry<float>[] savedMin=new ConfigEntry<float>[6],savedMax=new ConfigEntry<float>[6],savedGain=new ConfigEntry<float>[6];
        private readonly ConfigEntry<bool>[] savedEnabled=new ConfigEntry<bool>[6];
        private ConfigEntry<bool> previewSetting,xraySetting,twistLimitSetting,rollLimitSetting,pitchLimitSetting;
        private ConfigEntry<float> lengthSetting;
        private ConfigEntry<KeyboardShortcut> panelKey;
        internal int BindOperations {get;private set;}
        internal int ActorListReads {get;private set;}
        internal long SampleCount {get;private set;}
        internal int SolvedFrame {get;private set;}=-1;
        private double lastSolveTime;

        private void Awake()
        {
            current=this;InitializeLanguage();EmbeddedDependencies.Initialize();
            savedTransport=Config.Bind("Device","Transport",0,"0 Serial, 1 UDP, 2 Intiface (L0)");
            for(int i=0;i<3;i++)
            {
                savedEndpoint[i]=Config.Bind("Device","Endpoint"+i,i==0?"COM4":i==1?"127.0.0.1":"ws://127.0.0.1:12345");
                savedArgument[i]=Config.Bind("Device","Argument"+i,i==0?"115200":i==1?"8000":"0");
            }
            transport=Math.Max(0,Math.Min(2,savedTransport.Value));LoadEndpoint();
            previewSetting=Config.Bind("View","Models",true);xraySetting=Config.Bind("View","XRay",true);
            InitializeModelPreview();
            twistLimitSetting=Config.Bind("Axes","R0SoftLimit",true,"Keep the central 40% of twist travel exact; smoothly compress larger requests. Recorded scripts bypass this mapping.");
            rollLimitSetting=Config.Bind("Axes","R1SoftLimit",true,"Keep the central 40% of roll travel exact; smoothly compress larger requests using the current SR6 carrier geometry. Recorded scripts bypass this mapping.");
            pitchLimitSetting=Config.Bind("Axes","R2SoftLimit",true,"Keep +/-10 deg exact, smoothly compress larger pitch into the reference SR6 +/-25 deg range. Recorded scripts bypass this mapping.");
            Drive.SoftTwistLimit=twistLimitSetting.Value;Drive.SoftRollLimit=rollLimitSetting.Value;
            Drive.SoftPitchLimit=pitchLimitSetting.Value;
            rotationReferenceSetting=Config.Bind("Axes","AutoRotationReference",false,"Optional second mode: save a fixed rotation reference per chapter, retain the central 80% of relative travel and softly limit the rest. Scripts already contain the final output and bypass this mode.");
            mappingModeSetting=Config.Bind("Axes","MappingMode",rotationReferenceSetting.Value?2:1,"0 Absolute, 1 Soft, 2 As faithful as possible, 3 Manual endpoints (game entry; length derived from endpoints).");
            InitializeStrokeMapping();
            InitializeFeel();
            lengthSetting=Config.Bind("Model","FixedLengthMm",0f,"0 captures the BP rest length once at binding");
            InitializeCalibration();
            InitializeHotkeys();
            InitializeClimax();
            InitializeChapterTransition();
            InitializeDirector();
            showPreview=previewSetting.Value;xray=xraySetting.Value;rodMillimetres=lengthSetting.Value.ToString("G6",CultureInfo.InvariantCulture);
            for(int i=0;i<6;i++)
            {
                string axis=AxisNames[i];savedMin[i]=Config.Bind("Axes",axis+"Min",0f);savedMax[i]=Config.Bind("Axes",axis+"Max",1f);
                savedGain[i]=Config.Bind("Axes",axis+"Gain",1f);savedEnabled[i]=Config.Bind("Axes",axis+"Enabled",true);
                Drive.Minimum[i]=Numbers.Clamp(savedMin[i].Value,0,1);Drive.Maximum[i]=Numbers.Clamp(savedMax[i].Value,Drive.Minimum[i],1);
                Drive.Gain[i]=Numbers.Clamp(savedGain[i].Value,0,20);Drive.Enabled[i]=savedEnabled[i].Value;
                gainText[i]=Drive.Gain[i].ToString("G4",CultureInfo.InvariantCulture);
            }
            Devices=new DeviceHub(message=>Logger.LogInfo(message));preview=gameObject.AddComponent<StudioPreview>();
            InitializeSyncDelay();
            ExtensibleSaveFormat.ExtendedSave.SceneBeingSaved+=SaveChapters;
            ExtensibleSaveFormat.ExtendedSave.SceneBeingLoaded+=LoadChapters;
            ExtensibleSaveFormat.ExtendedSave.SceneBeingImported+=ImportChapters;
            PatchScene("LoadScene",new[]{typeof(string)});PatchScene("LoadSceneCoroutine",new[]{typeof(string)});PatchScene("ImportScene",new[]{typeof(string)});PatchScene("InitScene",Type.EmptyTypes);
            Type camera=typeof(GameStudio).Assembly.GetType("Studio.CameraControl");
            if(camera!=null)foreach(string name in new[]{"InputMouseProc","InputMouseWheelZoomProc"})
            {var method=AccessTools.Method(camera,name);if(method!=null&&method.ReturnType==typeof(bool))harmony.Patch(method,new HarmonyMethod(typeof(Plugin),"CameraInput"));}
            Camera.onPreCull+=BeforeCameraRender;
            StartCoroutine(AtFrameEnd());
            Logger.LogInfo("KoiSync Studio 1.0.0 · per-chapter climax calibration settings · devices disconnected");
        }
        private void Start(){Clock.Resolve();}
        private void PatchScene(string name,Type[] parameters)
        {foreach(var method in typeof(GameStudio).GetMethods(RuntimeAccess.Flags))if(method.Name==name)harmony.Patch(method,new HarmonyMethod(typeof(Plugin),"BeforeSceneChange"));}
        private static void BeforeSceneChange(MethodBase __originalMethod,object[] __args)
        {
            if(current==null)return;
            current.bodyGrind.Reset();
            string path=(__originalMethod.Name=="LoadScene"||__originalMethod.Name=="LoadSceneCoroutine")&&__args.Length>0?__args[0] as string:null;
            current.BeforeScriptSceneChange(path);current.ReleaseBinding(UiText.T("场景正在加载…"),true);
        }
        private static bool CameraInput(ref bool __result)
        {if(current==null||!current.PointerInside)return true;__result=false;return false;}
        private void Update()
        {
            bodyGrind.RestorePreviousFrame();
            UpdateHotkeys();
            lock(scanGate)
            {
                if(scanPending){scanPending=false;scanning=false;ports=pendingPorts??new string[0];status=deviceNote=pendingError??(UiText.T("找到 ")+ports.Length+UiText.T(" 个串口"));}
            }
            UpdateSceneFiles();
            UpdateDirector();
            AdvanceStepBakeSeek();
        }
        private void BeforeCameraRender(Camera camera)
        {if(camera==Camera.main&&SolvedFrame!=Time.frameCount)SolveFrame();}
        private IEnumerator AtFrameEnd()
        {
            var wait=new WaitForEndOfFrame();
            while(true)
            {
                yield return wait;
                if(StepBakeRunning){CaptureStepBakeFrame();RunDirectorEndOfFrame();continue;}
                // Keep recording/output alive when no main camera renders.
                if(SolvedFrame!=Time.frameCount)SolveFrame();
                RunDirectorEndOfFrame();
                // Restore bone offsets immediately after frame render & end-of-frame captures so
                // next frame's FixedUpdate/Update/Timeline/IK/DynamicBone always see clean poses!
                bodyGrind.RestorePreviousFrame();
            }
        }
        private void SolveFrame()
        {
                SolvedFrame=Time.frameCount;
                if(scenePending||StepBakeRunning){climaxPulse=null;climaxLiveValue=double.NaN;liveL0Cycle.Reset();Devices.Stop();lastSolveTime=0;return;}
                try{UpdateChapterBinding();}catch(Exception error){ReleaseBinding(error.GetBaseException().Message,false);}
                if(Binding==null&&!UseScripts)
                {
                    lastSolveTime=0;climaxPulse=null;climaxLiveValue=double.NaN;liveL0Cycle.Reset();Devices.Stop();
                    if(recording){Drive.UpdateScript(new[]{1d,.5,.5,.5,.5,.5});Array.Copy(Drive.Values,outputValues,6);RecordFrame();}
                    return;
                }
                double now=Time.realtimeSinceStartup;
                double dt=lastSolveTime>0?now-lastSolveTime:1.0/60;lastSolveTime=now;
                climaxPulse=null;climaxLiveValue=double.NaN;
                try
                {
                    if(Binding!=null&&UseScripts&&(!Binding.Alive||!ActorVisible(Binding.Source)||!ActorVisible(Binding.Target)))
                    {Binding=null;Sample=null;Measured=null;preview.Hide();}
                    if(Binding!=null)
                    {
                        Sample=Binding.Sample(dt);Measured=MappingMode==AxisMappingMode.Manual?SolveManualCalibration(Sample):NativeMotion.Solve(Sample);Binding.Commit(Sample,Measured);SampleCount++;
                    }
                    bool valid;
                    ObserveLiveL0Frequency();
                    Drive.PhysicalL0Minimum=0;
                    if(UseScripts)valid=ApplyScriptFrame();
                    else if(motionSource==MotionSource.Script&&!recording){Drive.Reset();valid=false;}
                    else{valid=Measured!=null;if(valid){UpdateClimax();UpdateLiveDrive(dt);ApplyClimaxPulse();}}
                    if(valid)UpdateOutputTransition(dt);
                    ApplyStudioBodyGrind(dt);
                    // Preview chooses its own source without changing the
                    // snapshot consumed by recording and device transmission.
                    UpdateModelPreview();
                    RecordFrame();
                    if(valid&&sendOutput&&Devices.Connected)Devices.Offer(outputValues,UseScripts?scriptEnabled:Drive.Enabled,DeviceKind.SR6,UseScripts?null:climaxPulse,Clock.Time,Clock.Playing?Clock.EffectiveSpeed:0);
                    else Devices.Stop();
                }
                catch(Exception error){Logger.LogWarning(UiText.T("绑定读取停止：")+error);ReleaseBinding(error.GetBaseException().Message,false);}
        }
        internal void ReadActors()
        {
            ResetClimax();
            ReleaseBinding(UiText.T("选择角色后点击绑定。"),false);manualActorPair=false;actors.Clear();actorKeys.Clear();actorNames=new string[0];var names=new List<string>();
            var studio=GameStudio.Instance;if(studio==null||studio.dicObjectCtrl==null){status=UiText.T("Studio 场景尚未就绪");return;}
            ActorListReads++;
            foreach(var pair in studio.dicObjectCtrl)
            {var actor=pair.Value as OCIChar;if(actor==null||actor.charInfo==null)continue;actors.Add(actor.charInfo);actorKeys[actor.charInfo]=pair.Key;names.Add(ActorDisplayName(actor.charInfo));}
            actorNames=names.ToArray();sourceIndex=actors.FindIndex(a=>a.sex==0);if(sourceIndex<0)sourceIndex=0;
            targetIndex=actors.FindIndex(a=>a.sex==1);if(targetIndex<0)targetIndex=actors.Count>1?1:0;
            Clock.Resolve();ResetPairCandidates();if(autoCameraPairs)CachePairCandidates();status=UiText.T("读取到 ")+actors.Count+UiText.T(" 个角色；绑定时优先使用 BP 的吸附对象。");
        }
        internal void BindPair()
        {
            try
            {
                if(actors.Count<2||actors.Exists(actor=>actor==null))ReadActors();
                if(actors.Count<2)throw new ArgumentException(UiText.T("当前场景需要两个角色才能绑定"));
                var chapter=Book.At(Clock.Time);
                bool pin=manualOpening||manualActorPair||chapter!=null&&chapter.Manual&&chapter.Source==Key(actors[sourceIndex])&&chapter.Target==Key(actors[targetIndex]);
                ReleaseBinding(UiText.T("绑定中"),false);
                Binding=new StudioBinding(actors[sourceIndex],actors[targetIndex],fallback,0,!manualOpening,pin,StudioHandSource.IsHand(fallback)?null:VisibleShaft(actors[sourceIndex]));ApplyBindingCalibration();BindOperations++;
                if(pin)
                {
                    RememberChapters();chapter=chapter??Book.Split(0);
                    var chosen=MakeChapter(Binding.Source,Binding.Target,Binding.Kind);
                    if(chapter.Identity!=chosen.Identity){chapter.RotationReference=null;chapter.StrokeCalibration=null;}
                    chapter.Source=chosen.Source;chapter.Target=chosen.Target;chapter.Opening=chosen.Opening;chapter.Name=chosen.Name;
                    chapter.SourceName=chosen.SourceName;chapter.TargetName=chosen.TargetName;chapter.Manual=true;MarkSceneDataDirty();
                }
                sourceIndex=actors.IndexOf(Binding.Source);targetIndex=actors.IndexOf(Binding.Target);manualActorPair=false;
                if(Book.Chapters.Count==0)
                {var initial=MakeChapter(Binding.Source,Binding.Target,Binding.Kind);initial.Manual=!autoCameraPairs;Book.Observe(0,initial);}
                appliedChapter="";followChapters=!manualOpening;previousChapterTime=-1;CaptureChapterCalibration();
                Drive.Reset();status=Binding.Description;
            }
            catch(Exception error){status=error.GetBaseException().Message;}
        }
        private void SelectOpening(int index)
        {
            manualOpening=index>=0;
            fallback=manualOpening?(OpeningKind)index:OpeningKind.Vaginal;
            BindPair();
        }
        internal void ReleaseBinding(string reason,bool clearActors)
        {
            followChapters=false;if(Devices!=null)Devices.Stop();Binding=null;Sample=null;Measured=null;Drive.Reset();Feel.Reset();
            if(preview!=null)preview.Hide();status=reason;
            if(clearActors){ResetChapterTransition();manualActorPair=false;manualOpening=false;openingPicker=false;ResetChapterEditor();modelOrigins.Clear();actors.Clear();actorKeys.Clear();actorNames=new string[0];actorPicker=0;Book.Chapters.Clear();appliedChapter="";ResetPairCandidates();}
        }
        private double ParseLength()
        {
            string text=rodMillimetres.Trim();
            if(text.EndsWith("mm",StringComparison.OrdinalIgnoreCase))text=text.Substring(0,text.Length-2).Trim();
            double value;if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||!Numbers.Finite(value)||value!=0&&(value<1||value>500))throw new ArgumentException(UiText.T("阴茎长度填 0 自动标定，或 1–500 mm"));
            return value;
        }
        private void ApplyLength()
        {
            try
            {
                double mm=ParseLength();rodMillimetres=mm.ToString("G6",CultureInfo.InvariantCulture);lengthSetting.Value=(float)mm;
                if(Binding!=null)
                {
                    // Recalibrate zero directly; rebinding would restore the
                    // chapter's previous length instead of reading the model.
                    double length=mm==0?Binding.ReadBaseModelLength():mm/1000;
                    if(ManualCalibrationActive)SetManualLength(length,true);
                    else{Binding.ManualEndOffset=null;Binding.SetLength(length);SyncMappingLength(length,true);}
                    rodMillimetres=(length*1000).ToString("G6",CultureInfo.InvariantCulture);
                }
                else if(mm>0)SyncMappingLength(mm/1000,true);
                if(mappingDirty)SaveMappingRanges();Drive.Reset();Devices.Stop();Config.Save();CaptureChapterCalibration();ProtectChapterCalibration();
                status=mm==0?UiText.T("阴茎长度使用自动标定"):UiText.T("阴茎长度已设为 ")+rodMillimetres+" mm";
            }
            catch(Exception error){status=error.Message;}
        }
        private void LoadEndpoint(){endpoint=savedEndpoint[transport].Value;argument=savedArgument[transport].Value;}
        private void ChangeTransport(int index){if(index==transport)return;serialPortPicker=false;Devices.Disconnect();transport=index;LoadEndpoint();savedTransport.Value=index;}
        private void ConnectDevice()
        {
            
            if(Devices.Connected||Devices.Connecting){Devices.Disconnect();return;}
            try
            {
                int number;if(!int.TryParse(argument,out number)||transport==0&&(number<1200||number>2000000)||transport==1&&(number<1||number>65535)||transport==2&&number<0)throw new ArgumentException(UiText.T("连接参数无效"));
                savedEndpoint[transport].Value=endpoint.Trim();savedArgument[transport].Value=argument;savedTransport.Value=transport;Config.Save();deviceNote="";
                Devices.Connect(new DeviceSettings{Transport=transport,Endpoint=endpoint.Trim(),Argument=number,Kind=DeviceKind.SR6});
            }
            // The device page shows this note; the shared status line is
            // overwritten by the next chapter binding.
            catch(Exception error){status=deviceNote=error.GetBaseException().Message;}
        }
        private void ScanDevices()
        {
            if(scanning)return;scanning=true;
            ThreadPool.QueueUserWorkItem(_=>
            {
                string[] found=null;string error=null;try{found=SerialTransport.GetPortNames();}catch(Exception e){error=e.Message;}
                lock(scanGate){pendingPorts=found;pendingError=error;scanPending=true;}
            });
        }
        private void SaveAxes()
        {
            Devices.Stop();
            for(int i=0;i<6;i++){savedMin[i].Value=(float)Drive.Minimum[i];savedMax[i].Value=(float)Drive.Maximum[i];savedGain[i].Value=(float)Drive.Gain[i];savedEnabled[i].Value=Drive.Enabled[i];}
            twistLimitSetting.Value=Drive.SoftTwistLimit;rollLimitSetting.Value=Drive.SoftRollLimit;pitchLimitSetting.Value=Drive.SoftPitchLimit;
            Config.Save();
        }
        private void ApplyStudioBodyGrind(double dt)
        {
            var chapter=RotationChapter()??(UseScripts&&Clock.Available?Book.At(Clock.Time):null);
            ChaControl target=Binding!=null?Binding.Target:(chapter!=null?Actor(chapter.Target):null);
            ChaControl source=Binding!=null?Binding.Source:(chapter!=null?Actor(chapter.Source):null);
            if(target!=null&&!ActorVisible(target))target=null;
            if(source!=null&&!ActorVisible(source))source=null;
            OpeningKind opening=Binding!=null?Binding.Kind:(chapter!=null?(OpeningKind)chapter.Opening:OpeningKind.Vaginal);
            bool direct=chapter!=null&&!chapter.IsClimax&&StudioGrindEngine.IsDirectInsertionOpening(chapter.Opening)&&chapter.Grind!=null&&chapter.Grind.Enabled&&chapter.Grind.LinkSkeleton&&(target!=null||source!=null);
            if(!direct||StepBakeRunning)
            {
                bodyGrind.ApplyForFrame(null,null,OpeningKind.Vaginal,null,default(StudioGrindFrame),null,(float)dt);
                return;
            }
            // In Script playback mode, evaluate the exact same deterministic grind manifold at Clock.Time
            // so the character's hips & soft-tissue DynamicBone weights dance in 1:1 visual lockstep with the script!
            var gf=UseScripts?StudioGrindEngine.Evaluate(Clock.Available?Clock.Time:0,Numbers.Clamp(1.0-outputValues[0],0,1),chapter.Grind):activeGrindFrame;
            bodyGrind.ApplyForFrame(target,source,opening,Sample,gf,chapter.Grind,(float)dt);
        }
        private void OnDestroy()
        {CancelStepBake();bodyGrind.Reset();ShutdownDirector();Camera.onPreCull-=BeforeCameraRender;if(recording)FinishRecording();SaveSceneData();ReleaseBinding(UiText.T("已卸载"),true);ExtensibleSaveFormat.ExtendedSave.SceneBeingSaved-=SaveChapters;ExtensibleSaveFormat.ExtendedSave.SceneBeingLoaded-=LoadChapters;ExtensibleSaveFormat.ExtendedSave.SceneBeingImported-=ImportChapters;harmony.UnpatchSelf();if(Devices!=null)Devices.Dispose();if(current==this)current=null;if(uiFont!=null)Destroy(uiFont);DestroyTheme();}
    }
}
