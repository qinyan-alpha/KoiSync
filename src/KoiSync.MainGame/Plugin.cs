using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using KoiSync.Core;
using KoiSync.Plugin;
using UnityEngine;

namespace KoiSync.MainGame
{
    [BepInPlugin("local.koisync.maingame","KoiSync Main Game",MainGameVersion.Number)]
#if KKS
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
#else
    [BepInProcess("Koikatu")]
#endif
    [BepInDependency("com.bepis.bepinex.sideloader",BepInDependency.DependencyFlags.SoftDependency)]
    public sealed partial class Plugin:BaseUnityPlugin
    {
        internal readonly GameDrive Drive=new GameDrive();
        internal DeviceHub Devices;
        internal MainBinding Binding;
        internal NativeMotionSample Sample;
        internal NativeMotionResult Measured;
        internal ActionProfile Profile;
        internal ActorSlot ActiveSource;
        internal HSceneProc Scene;
        internal HFlag Flags;
        internal ActionCatalogue Catalogue;
        internal string Status="等待本体 H 场景",Stage="",DataRoot;
        internal bool SyncRequested,RecordRequested;
        internal long Samples;
        internal int SolvedFrame=-1;
        private GamePreview preview;
        private float nextSearch,nextBind;
        private double lastSolve;
        private string actionIdentity="",bindingIdentity="";
        private int stageHash,controllerId;
        private Animator clock;
        private int clockLayer;
        private AnimatorStateInfo clockState;
        private double phaseTime,phaseDuration;
        private bool advancing,transition,centeredStageReady;
        private double previousProgress=double.NaN;
        private ConfigEntry<float> playerLength,deviceStroke,sendDelay;
        private ConfigEntry<float> smoothing,outputRate,outerCeilingL0;
        private ConfigEntry<int> feelPreset,nonShaftFeelMode;
        private ConfigEntry<bool> lengthAdaptation;
        private readonly FeelTranslator feel=new FeelTranslator();
        public FeelTranslator Feel {get{return feel;}}
        public double ActiveOuterCeilingL0
        {
            get{return outerCeilingL0==null?FeelTranslator.DefaultOuterCeiling:Numbers.Clamp(outerCeilingL0.Value/100.0,0.55,0.99);}
            set
            {
                double clamped=Numbers.Clamp(Numbers.Finite(value)?value:FeelTranslator.DefaultOuterCeiling,0.55,0.99);
                if(outerCeilingL0!=null)outerCeilingL0.Value=(float)Math.Round(clamped*100.0,1);
                feel.OuterCeiling=clamped;
                if(Timeline!=null&&Timeline.Active)Timeline.UpdateOuterCeiling(clamped);
                if(MathEngine!=null&&MathEngine.Active)MathEngine.UpdateOuterCeiling(clamped);
            }
        }
        public FeelPreset ActiveFeelPreset
        {
            get{return feelPreset==null?FeelPreset.Balanced:(FeelPreset)Mathf.Clamp(feelPreset.Value,0,2);}
            set{if(feelPreset!=null){feelPreset.Value=(int)value;ApplyFeelRateFloor(value);}}
        }
        public NonShaftFeelMode ActiveNonShaftFeelMode
        {
            get{return nonShaftFeelMode==null?NonShaftFeelMode.Foreplay:(NonShaftFeelMode)Mathf.Clamp(nonShaftFeelMode.Value,0,2);}
            set{if(nonShaftFeelMode!=null)nonShaftFeelMode.Value=(int)value;}
        }
        private ConfigEntry<bool> previewEnabled,xray;
        private ConfigEntry<int> transport,argument;
        private ConfigEntry<string> endpoint;
        private readonly ConfigEntry<float>[] min=new ConfigEntry<float>[6],max=new ConfigEntry<float>[6],gain=new ConfigEntry<float>[6];
        private readonly ConfigEntry<bool>[] enabledAxes=new ConfigEntry<bool>[6];
        private static readonly string[] Axes={"L0","L1","L2","R0","R1","R2"};
        private static readonly double[] Parked={1,.5,.5,.5,.5,.5};
        private readonly double[] outputValues=new double[6];
        private bool outputValid,outputFromScript;
        private double PlayerLength {get{return ValidLengthSetting(playerLength,ShaftStrokeMapping.ReferenceLength,true);}}
        private double DeviceStroke {get{return ValidLengthSetting(deviceStroke,ShaftStrokeMapping.ReferenceStroke,false);}}

        private void Awake()
        {
            InitializeLanguage();
            InitializeInput();
            DataRoot=Path.Combine(Paths.GameRootPath,"UserData/KoiSyncMainGame");Directory.CreateDirectory(DataRoot);
            EmbeddedDependencies.Initialize();Devices=new DeviceHub(s=>Logger.LogInfo(s));
            using(var table=new StreamReader(GetType().Assembly.GetManifestResourceStream("KoiSync.MainGame.Actions.tsv")))
            using(var names=new StreamReader(GetType().Assembly.GetManifestResourceStream("KoiSync.NativeAnimations.csv")))
            using(var resources=new StreamReader(GetType().Assembly.GetManifestResourceStream("KoiSync.MainGame.NativeResources.tsv")))
            using(var extras=new StreamReader(GetType().Assembly.GetManifestResourceStream("KoiSync.MainGame.KPlugActions.tsv")))Catalogue=new ActionCatalogue(table,names,resources,extras);
            playerLength=Config.Bind("Mapping","PlayerLengthMm",(float)(ShaftStrokeMapping.ReferenceLength*1000));
            deviceStroke=Config.Bind("Mapping","DeviceStrokeMm",(float)(ShaftStrokeMapping.ReferenceStroke*1000));
            lengthAdaptation=Config.Bind("Mapping","AdaptLength",false);
            feelPreset=Config.Bind("Mapping","FeelPreset",(int)FeelPreset.Balanced);
            nonShaftFeelMode=Config.Bind("Mapping","NonShaftFeelMode",(int)NonShaftFeelMode.Foreplay);
            outerCeilingL0=Config.Bind("Mapping","OuterCeilingL0",(float)(FeelTranslator.DefaultOuterCeiling*100.0),"L0 外限 (55..99%, 默认 99%)");
            feel.OuterCeiling=ActiveOuterCeilingL0;
            previewEnabled=Config.Bind("View","Preview",true);xray=Config.Bind("View","XRay",true);
            transport=Config.Bind("Device","Transport",0);endpoint=Config.Bind("Device","Endpoint","COM4");argument=Config.Bind("Device","Argument",115200);
            sendDelay=Config.Bind("Device","DelayMs",0f);
            smoothing=Config.Bind("Axes","OutputSmoothingMs",25f);
            outputRate=Config.Bind("Axes","OutputRatePercentPerSecond",450f);
            ApplyFeelRateFloor(ActiveFeelPreset);
            ApplyResponseSettings();
            InitializeUi();
            for(int i=0;i<6;i++)
            {
                min[i]=Config.Bind("Axes",Axes[i]+"Min",0f);max[i]=Config.Bind("Axes",Axes[i]+"Max",1f);gain[i]=Config.Bind("Axes",Axes[i]+"Gain",1f);enabledAxes[i]=Config.Bind("Axes",Axes[i]+"Enabled",true);
                Drive.Minimum[i]=Numbers.Clamp(min[i].Value,0,1);Drive.Maximum[i]=Numbers.Clamp(max[i].Value,Drive.Minimum[i],1);Drive.Gain[i]=Numbers.Clamp(gain[i].Value,0,20);Drive.Enabled[i]=enabledAxes[i].Value;
                gainText[i]=Drive.Gain[i].ToString("G5",CultureInfo.InvariantCulture);
            }
            preview=gameObject.AddComponent<GamePreview>();
            foreach(var definition in Catalogue.Definitions)
            {
                if(!File.Exists(Path.Combine(ActionProfile.Folder(DataRoot,definition.Key),"action.xml")))ActionProfile.From(definition).Save(DataRoot);
                if(definition.AnalAllowed){var branch=definition.Copy();branch.Key+="_anal";branch.Contact=NativeTarget.ANAL;if(!File.Exists(Path.Combine(ActionProfile.Folder(DataRoot,branch.Key),"action.xml")))ActionProfile.From(branch).Save(DataRoot);}
            }
            Camera.onPreCull+=BeforeCamera;StartCoroutine(FrameEnd());
            InitializeDirector();
            RefreshCalibrationText();
            Logger.LogInfo("KoiSync Main Game "+MainGameVersion.Number+" · shared Studio solver · "+Catalogue.Count+" classified native / kPlug actions");
        }
        private void Update()
        {
            UpdateUi();
            PumpDirector();
            if(Scene==null&&Time.realtimeSinceStartup>=nextSearch)
            {nextSearch=Time.realtimeSinceStartup+1;Scene=FindObjectOfType<HSceneProc>();}
        }
        private void LateUpdate()
        {
            // Fallback for non-shaft poses without Lookat_dan; guarded by Time.frameCount so
            // shaft poses already processed inside Lookat_dan.LateUpdate Prefix never double-apply.
            if(BodyGrind!=null)BodyGrind.ApplyForCurrentFrame();
        }
        private void BeforeCamera(Camera camera){if(camera==Camera.main&&SolvedFrame!=Time.frameCount)SolveFrame();}
        private IEnumerator FrameEnd(){var wait=new WaitForEndOfFrame();while(true){yield return wait;if(SolvedFrame!=Time.frameCount)SolveFrame();ProcessEndOfFrameWork();}}
        private void SolveFrame()
        {
            SolvedFrame=Time.frameCount;double now=Time.realtimeSinceStartup,dt=lastSolve>0?Math.Max(0,now-lastSolve):1d/60;lastSolve=now;
            try
            {
                if(Scene==null||Scene.isEnd||!Scene.gameObject.activeInHierarchy){EndSession();return;}
                Flags=RuntimeAccess.Read(Scene,"flags") as HFlag;
                if(Flags==null||Flags.nowAnimationInfo==null||Flags.nowAnimationInfo.mode==HFlag.EMode.none||string.IsNullOrEmpty(Flags.nowAnimationInfo.nameAnimation)){Invalidate(L("等待动作载入","Waiting for animation to load"));return;}
                EnsureKsPlugCatalogueSynced();
                var info=Flags.nowAnimationInfo;var definition=ResolveActionDefinition(info,Flags.isAnalPlay);
                string identity=Scene.GetInstanceID()+":"+definition.Key;
                if(identity!=actionIdentity)
                {
                    if(MathEngine.Active)DirectorStopMathLab(L("动作切换，自动结束 SR6 数学实验台","Action changed; stopped SR6 Math Lab"));
                    if(Timeline.Active)DirectorStopTimeline(L("动作切换，自动结束时间轴变式","Action changed; stopped Timeline variant"));
                    PhaseMap.Reset();
                    if(BodyGrind!=null)BodyGrind.ClearCache();
                    FinishTake(false,"action changed");SaveProfile();Profile=null;actionIdentity=bindingIdentity="";ReleaseBinding();
                    Profile=ActionProfile.Load(DataRoot,definition);actionIdentity=identity;
                    ActiveSource=Profile.Source;RefreshCatalogue();ResetStage();nextBind=0;RefreshCalibrationText();
                    Status=UiText.T(definition.Reason);
                }
                var target=Actor(Profile.Target);
                if(!MainBinding.Posed(target)){Invalidate(L("等待固定目标角色 ","Waiting for target actor ")+ActionLabels.Actor(Profile.Target));return;}
                string nativePhase=target.animBody.GetCurrentAnimatorStateInfo(0).IsName("WLoop")?"WLoop":"";
                ActiveSource=ActionStages.SourceFor(Profile.Source,Profile.WeakSource,nativePhase,Profile.ManualBinding);
                if(Profile.CalibrationSource!=ActiveSource)
                {FinishTake(false,"fixed native source stage changed");SaveProfile();Profile.SelectCalibration(ActiveSource);ReleaseBinding();bindingIdentity="";nextBind=0;}
                var source=Actor(ActiveSource);
                string pair=(source==null?0:source.GetInstanceID())+":"+target.GetInstanceID()+":"+ActiveSource+":"+Profile.Target+":"+Profile.Effector+":"+Profile.Contact+":"+Profile.Landmark;
                if(pair!=bindingIdentity){FinishTake(false,"participant changed");ReleaseBinding();bindingIdentity=pair;nextBind=0;}
                if(Profile.Effector==ActionSource.Unsupported){Invalidate(L("动作未分类：请在工作台指定驱动来源","Action unclassified: assign a motion source in Workbench"));return;}
                if(Profile.Effector!=ActionSource.NoMotion&&(Binding==null||!Binding.Alive))
                {
                    if(Binding!=null){FinishTake(false,"native interaction changed");ReleaseBinding();if(RecordRequested)capture.Arm();}
                    if(Time.realtimeSinceStartup<nextBind){Devices.Stop();return;}
                    nextBind=Time.realtimeSinceStartup+.5f;
                    Binding=new MainBinding(Scene,Profile,source,target,ActiveSource);
                    centeredStageReady=false;
                    if(Binding.UsesEndpoints(Profile))Binding.EnsureEndpoints();
                    CaptureCalibration();RefreshCalibrationText();
                }
                clock=Binding!=null&&Binding.Clock!=null&&Binding.Clock.runtimeAnimatorController!=null?Binding.Clock:target.animBody;clockLayer=Binding!=null&&clock==Binding.Clock?Math.Max(0,Binding.ClockLayer):0;clockState=clock.GetCurrentAnimatorStateInfo(clockLayer);transition=clock.IsInTransition(clockLayer);
                string state=Flags.nowAnimStateName??"";
                if(Profile.Key.StartsWith("peeping_",StringComparison.Ordinal))foreach(var name in new[]{"In","Loop","Idle","Out"})if(clockState.IsName(name)){state=name;break;}
                if(state.Length==0||clockState.shortNameHash!=Animator.StringToHash(state))state="state_"+clockState.shortNameHash;
                int controller=clock.runtimeAnimatorController.GetInstanceID();
                if(!Timeline.Active&&!MathEngine.Active&&(clockState.fullPathHash!=stageHash||controller!=controllerId))
                {
                    FinishTake(false,"animation stage changed");stageHash=clockState.fullPathHash;controllerId=controller;Stage=state;
                    centeredStageReady=false;
                    phaseDuration=ReadPhaseDuration();
                    capture.Reset();if(RecordRequested)capture.Arm();previousProgress=double.NaN;playback.Clear();ResetMotionHistory();
                }
                if(!Numbers.Finite(phaseDuration)||phaseDuration<=.0001)phaseDuration=ReadPhaseDuration();
                if(!takeActive&&!Timeline.Active&&!MathEngine.Active&&!state.StartsWith("state_",StringComparison.Ordinal))Stage=state;
                double progress=clockState.normalizedTime;
                if(!Numbers.Finite(progress)||phaseDuration<=.0001){Invalidate(L("等待有效动画时钟","Waiting for valid animation clock"));return;}
                phaseTime=(clockState.loop?progress-Math.Floor(progress):Math.Min(1,Math.Max(0,progress)))*phaseDuration;
                if(!Timeline.Active&&!MathEngine.Active&&!double.IsNaN(previousProgress)&&progress<previousProgress-.01)
                {
                    FinishTake(false,"animation time moved backwards");
                    if(RecordRequested)capture.Arm();
                    ResetMotionHistory();
                }
                advancing=clock.enabled&&clock.speed!=0&&Time.timeScale>0&&!double.IsNaN(previousProgress)&&progress>previousProgress+1e-7;
                double animationRate=advancing&&dt>0?(progress-previousProgress)*phaseDuration/dt:0;
                previousProgress=progress;
                if(ActionStages.IsTravel(Profile.Key,Stage))
                {FinishTake(false,"native travel phase");ResetMotionHistory();ClearPreview();Status=L("旁观入场 / 离场，等待动作阶段","Peeping entry / exit; waiting for action phase");return;}
                outputFromScript=false;
                if(Profile.Effector==ActionSource.NoMotion)
                {Drive.PhysicalL0Minimum=0;Drive.UpdateScript(Parked);Status=L("此动作配置为静止","This action is configured as stationary");}
                else
                {
                    if(Profile.CenteredMotion&&!Profile.ManualBinding&&!centeredStageReady&&(transition||NativeProfiles.Stage(Stage)<0))
                    {ResetMotionHistory();ClearPreview();Status=L("等待动作开始后捕获固定标尺","Waiting for action start to capture reference span");return;}
                    var entryOffset=Profile.EntryOffset(Binding.Contact);
                    Sample=Binding.Sample(dt,entryOffset);
                    if(Profile.CenteredMotion&&!Profile.ManualBinding&&!centeredStageReady)
                    {
                        var calibration=Binding.Calibration.Copy();
                        double span=ShaftStrokeMapping.ReferenceInteraction;
                        calibration.Origin+=ManualCalibrationMotion.LocalEnd(Sample.ReferenceFrame,Sample.Target)-new MotionVector(span*.5,0,0);
                        calibration.ManualEnd=calibration.Origin+new MotionVector(span,0,0);
                        Binding.ApplyCalibration(calibration);
                        centeredStageReady=true;
                        if(!Profile.ReferenceReady){Profile.ReferenceReady=true;SaveProfile();}
                        RefreshCalibrationText();ResetMotionHistory();
                        Sample=Binding.Sample(dt,entryOffset);
                    }
                    Sample.AnimationState=Stage;Sample.AnimationProgress=progress;Sample.AnimationDuration=phaseDuration;Sample.StableAnimation=!transition;
                    bool endpoints=Binding.UsesEndpoints(Profile);
                    SR6MathFrame mathFrame=activeMathFrame;
                    TimelineFrame tlFrame=activeTimelineFrame;
                    if(mathFrame.Active)
                    {
                        Sample.AnimationProgress=mathFrame.ScrubPhase;Sample.StableAnimation=true;
                    }
                    else if(tlFrame.Active)
                    {
                        Sample.AnimationProgress=tlFrame.ScrubPhase;Sample.StableAnimation=true;
                    }
                    Measured=ActionMotion.Solve(Sample,Binding.Calibration,Profile.Mode,endpoints,Binding.AdaptivePrincipalAxis);
                    if(!endpoints)Binding.Commit(Sample,Measured);
                    if(!tlFrame.Active&&!mathFrame.Active)ObserveNormalPhaseDepth(progress);
                    Samples++;
                    try
                    {
                        outputFromScript=ApplyPlayback();
                        if(!outputFromScript)
                        {
                            if(mathFrame.Active&&mathFrame.PhysicalAxes!=null)
                            {
                                // MathEngine already projected onto exact SR6 workspace & applied C2 dynamics smoothing!
                                Drive.PhysicalL0Minimum=0;
                                Drive.Update(mathFrame.PhysicalAxes,dt,DriveMappingMode.Absolute);
                            }
                            else if(tlFrame.Active&&tlFrame.PhysicalAxes!=null)
                            {
                                Drive.PhysicalL0Minimum=0;
                                Drive.Update(tlFrame.PhysicalAxes,dt,DriveMappingMode.Soft);
                            }
                            else UpdateDrive(dt,endpoints);
                        }
                    }
                    catch(ArgumentException error)
                    {
                        // Keep the valid observation available for calibration
                        // when the user's output limits cannot be applied.
                        FinishTake(false,"invalid output mapping");if(RecordRequested)capture.Arm();
                        Drive.Reset();outputValid=false;Devices.Stop();UpdatePreview();
                        Status=error.Message;return;
                    }
                    Status=Binding.Description+(transition?L(" · 过渡中"," · Transitioning"):"");
                }
                // Preview, recording and transport all refer to these final
                // channel values. The transport owns the only delay buffer.
                Array.Copy(Drive.Values,outputValues,6);outputValid=true;
                UpdatePreview();
                if(!EditingDrive)RecordFrame(progress,outputValues);
                if(SyncRequested&&Devices.Connected&&!EditingDrive&&CanSendAxes())
                    Devices.Offer(outputValues,Drive.Enabled,DeviceKind.SR6,null,progress*phaseDuration,animationRate);
                else Devices.Stop();
            }
            catch(ArgumentException error){Invalidate(error.GetBaseException().Message);}
            catch(Exception error){Invalidate(error.GetBaseException().Message);if(Time.realtimeSinceStartup>=nextError){Logger.LogWarning(error);nextError=Time.realtimeSinceStartup+5;}}
        }
        private float nextError;
        internal static string ResourceIdentity(HSceneProc.AnimationListInfo info)
        {
            return AnimationResources.Identity(info.mode.ToString(),info.id,
                info.pathFemaleBase.assetpath,info.pathFemaleBase.file,info.paramFemale.path.assetpath,info.paramFemale.path.file,
                info.pathMaleBase.assetpath,info.pathMaleBase.file,info.paramMale.path.assetpath,info.paramMale.path.file,
                info.pathFemaleBase1.assetpath,info.pathFemaleBase1.file,info.paramFemale1.path.assetpath,info.paramFemale1.path.file);
        }
        private double ReadPhaseDuration()
        {
            double observed=clockState.length;if(Numbers.Finite(observed)&&observed>.0001)return observed;
            double total=0,weight=0;foreach(var info in clock.GetCurrentAnimatorClipInfo(clockLayer))
            {if(info.clip==null||!Numbers.Finite(info.clip.length)||info.clip.length<=.0001||info.weight<=0)continue;total+=info.clip.length*info.weight;weight+=info.weight;}
            return CycleCapture.ResolveDuration(observed,weight>0?total/weight:0);
        }
        private void UpdateDrive(double dt,bool endpoints)
        {
            if(Profile.Mode==AxisMappingMode.Relative&&Profile.Rotation==null&&!transition&&ActionRotationReference.CanCapture(Measured))
            {
                Profile.Rotation=ActionRotationReference.Capture(Measured,phaseTime);SaveProfile();
            }
            double fGauge=Flags!=null?Flags.gaugeFemale:0;
            double mGauge=Flags!=null?Flags.gaugeMale:0;
            DirectorActionEvent dirEvent;
            ActionMotion.Drive(Drive,Sample,Measured,Profile,lengthAdaptation.Value&&!endpoints,PlayerLength,DeviceStroke,dt,feel,ActiveFeelPreset,ActiveNonShaftFeelMode,Director,fGauge,mGauge,out dirEvent);
            if(dirEvent!=null)ApplyDirectorEvent(dirEvent);
        }
        internal ChaControl Actor(ActorSlot slot)
        {
            if(Scene==null)return null;
            if(slot==ActorSlot.Male||slot==ActorSlot.Male1)return RuntimeAccess.Read(Scene,slot==ActorSlot.Male?"male":"male1") as ChaControl;
            var list=RuntimeAccess.Read(Scene,"lstFemale") as IList;int index=slot==ActorSlot.Female1?1:0;return list!=null&&list.Count>index?list[index] as ChaControl:null;
        }
        private void Invalidate(string message)
        {FinishTake(false,"invalid observation");ReleaseBinding();Status=message;previousProgress=double.NaN;if(RecordRequested&&!capture.Waiting)capture.Arm();}
        private void ResetMotionHistory()
        {if(Binding!=null)Binding.ResetTransientMotion();feel.Reset();Drive.Reset();outputValid=false;Devices.Stop();}
        private void ReleaseBinding()
        {
            Binding=null;Sample=null;Measured=null;lastSolve=0;ResetMotionHistory();ClearPreview();
            if(RecordRequested)capture.Arm();
        }
        private void ResetStage(){stageHash=controllerId=0;Stage="";clock=null;previousProgress=double.NaN;capture.Reset();ClearPlayback();}
        private void EndSession()
        {
            ResetDirectorSessionStats();
            if(MathEngine.Active)DirectorStopMathLab(L("场景结束","Scene ended"));
            if(Timeline.Active||timelineActorsFrozen)DirectorStopTimeline(L("场景结束","Scene ended"));
            if(BodyGrind!=null)BodyGrind.Reset();
            if(Expressions!=null)Expressions.ReleaseAll();
            RestoreKPlugGirlThink();
            ResetKsPlugSessionCache();
            EventBus.ResetSession();
            PhaseMap.Reset();
            if(actionIdentity.Length>0){FinishTake(false,"scene ended");SaveProfile();ReleaseBinding();KoiSync.KK.KkMouthAnchor.Clear();NativeModelReference.Clear();ResetStage();Profile=null;Flags=null;actionIdentity=bindingIdentity="";Status=L("等待本体 H 场景","Waiting for Main Game H-scene");}
            if(Scene!=null)Scene=null;
        }
        internal void CaptureCalibration()
        {
            if(Profile==null||Binding==null)return;
            Profile.Calibration=Binding.Calibration.Copy();
        }
        internal void SaveProfile(){if(Profile!=null){CaptureCalibration();Profile.Save(DataRoot);}}
        private void OnApplicationQuit()
        {ShutdownVoiceSidecarOnGameExit();}
        private void OnDestroy()
        {ShutdownVoiceSidecarOnGameExit();DisposeDirector();Camera.onPreCull-=BeforeCamera;FinishTake(false,"plugin unloaded");FinishDriveEdit();SaveProfile();ReleaseInput();if(Devices!=null)Devices.Dispose();DestroyUi();}
    }
}
