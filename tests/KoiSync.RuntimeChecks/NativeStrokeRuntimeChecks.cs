using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using KoiSync.Core;
using KoiSync.Plugin;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[BepInPlugin("local.koisync.testmute","KoiSync test audio mute","0.1.0")]
public sealed class TestAudioMute:BaseUnityPlugin
{
    private bool active;
    private void Awake(){foreach(string arg in Environment.GetCommandLineArgs())if(arg.StartsWith("--koisync-")&&arg.EndsWith("-checks"))active=true;if(active)AudioListener.volume=0;}
    private void LateUpdate(){if(active)AudioListener.volume=0;}
}

[BepInPlugin("local.koisync.strokeruntime","KoiSync main stroke UI checks","0.1.0")]
[BepInDependency("local.koisync")]
public sealed class NativeStrokeRuntimeChecks:BaseUnityPlugin
{
    private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    private readonly StringBuilder report=new StringBuilder();private int count;
    private KoiSync.Plugin.Plugin host;private KoiSyncPanel panel;private NativePlaybackFixture playbackFixture;
    private string Output{get{return Path.Combine(Paths.GameRootPath,"native-stroke-checks.txt");}}
    private object Field(string name){return panel.GetType().GetField(name,F).GetValue(panel);}
    private object Call(string name,params object[] args){return panel.GetType().GetMethod(name,F).Invoke(panel,args);}
    private void Check(bool ok,string name){report.AppendLine((ok?"PASS ":"FAIL ")+name);if(!ok)throw new Exception(name);count++;}
    private void Awake(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"--koisync-stroke-checks")>=0){Application.runInBackground=true;StartCoroutine(Run());}}
    private IEnumerator Run()
    {
        yield return new WaitForSecondsRealtime(9);
        host=FindObjectOfType<KoiSync.Plugin.Plugin>();host.StopAllCoroutines();host.enabled=false;panel=(KoiSyncPanel)host.GetType().GetField("panel",F).GetValue(host);
        try
        {
            Check(host.IsMainGame&&!host.Devices.Connected&&!host.Devices.Connecting,"Main game test is disconnected");
            Check(AudioListener.volume==0,"BGM and all test audio are muted");
            Call("ShowAxisMonitor");panel.Refresh();Canvas.ForceUpdateCanvases();
            var channelSwitches=(Toggle[])Field("channels");
            for(int i=0;i<6;i++)channelSwitches[i].isOn=true;
            var restoredMask=new bool[6];var restoredChannels=new OutputChannels(new BepInEx.Configuration.ConfigFile(host.Config.ConfigFilePath,true),restoredMask);
            Check(Array.TrueForAll(restoredMask,v=>v),"All six UI channel selections persist in BepInEx configuration");
            channelSwitches[2].isOn=false;
            new OutputChannels(new BepInEx.Configuration.ConfigFile(host.Config.ConfigFilePath,true),restoredMask);
            Check(!restoredMask[2]&&restoredMask[1]&&restoredMask[5],"Restart preserves explicitly disabled channels as well as enabled ones");
            for(int i=0;i<6;i++)channelSwitches[i].isOn=i==0;
            host.Profiles.NativeKey="ui / mean regression v0126";host.Native.CurrentAnimation="动画行程测试";host.Native.CurrentProfile="VAGINAL";host.Native.SamplingStatus="离线测试";
            ((RangeSlider)Field("outputRange")).SetRange(.1f,.9f);Call("UpdateRange");
            panel.SetVisible(true);Call("ShowOverview");panel.Refresh();Canvas.ForceUpdateCanvases();
            Check(Field("chart")==null&&Field("raw")==null&&Field("sources")==null,"Main overview removes the duplicate graph, raw telemetry and source dropdown");
            var overview=(GameObject)Field("overviewPage");Check(overview.GetComponentsInChildren<Toggle>().Length==3,"Overview keeps debug, invert and auto-connect only");
        }
        catch(Exception ex){Finish(ex);yield break;}
        yield return new WaitForEndOfFrame();
        try
        {
            Capture("main-overview-v0126.png");Call("ShowStrokeSettings");panel.Refresh();Canvas.ForceUpdateCanvases();
            var page=(GameObject)Field("strokePage");Check(page.GetComponentsInChildren<RangeSlider>().Length==1,"The global travel page keeps one dual-handle range for the climax round trip");
            Check(page.GetComponentsInChildren<Slider>().Length==4,"Travel targets, climax travel and reversal rate are sliders");
            Check(page.GetComponentsInChildren<InputField>().Length==0,"The global travel page needs no numeric text fields");
            Check(((Dropdown)Field("strokeClimaxMode")).value==0,"Climax follows the game motion by default");
            ((Slider)Field("strokeNormalSlider")).value=500/999f;((Slider)Field("strokeDeepSlider")).value=700/999f;Call("FlushGlobalSettings");
            Check(host.Global.Stroke.NormalTravel==500&&host.Global.Stroke.DeepTravel==700,"Travel sliders edit the global configuration");
            var reloadedGlobal=new KoiSync.Plugin.GlobalSettings(host.Config);
            Check(reloadedGlobal.Stroke.NormalTravel==500&&reloadedGlobal.Stroke.DeepTravel==700,"The global travel configuration round-trips through the BepInEx file");
            host.Profiles.Animation("ui / second global animation");
            Check(host.Profiles.Animation(host.Profiles.NativeKey).Measurement.NativeStroke.NormalTravel==500&&host.Profiles.Animation("ui / second global animation").Measurement.NativeStroke.NormalTravel==500,"Every animation receives the global travel target");
            var ranges=(RangeSlider)Field("strokeClimaxRange");var r=(RectTransform)ranges.transform;
            Vector2 point=RectTransformUtility.WorldToScreenPoint(null,r.TransformPoint(new Vector3(r.rect.xMin+8+(r.rect.width-16)*.72f,0,0)));
            var data=new PointerEventData(EventSystem.current){position=point,button=PointerEventData.InputButton.Left};ranges.OnPointerDown(data);ranges.OnPointerUp(data);
            Check(Math.Abs(ranges.Upper-.72f)<.01&&host.Global.Stroke.ClimaxMaximum==719,"Pointer changes the global climax endpoint");
            ((Slider)Field("strokeFrequencySlider")).value=(float)((12-1)/19.0);Call("FlushGlobalSettings");
            var reloaded=new KoiSync.Plugin.GlobalSettings(host.Config);
            Check(reloaded.Stroke.ClimaxMaximum==719&&Math.Abs(reloaded.Stroke.ClimaxChangesPerSecond-12)<.001,"LitJSON saves and reloads the global stage settings in Unity 5.6");
            var store=new KoiSync.Plugin.PlaybackProfiles(Paths.GameRootPath);
            Check(!File.Exists(Path.Combine(Paths.GameRootPath,"UserData/KoiSync/Animations.json"))||store.Native.Animations.Count>0,"Independent animation storage remains available");
            Call("ShowTransitions");panel.Refresh();Canvas.ForceUpdateCanvases();
            var transitions=(GameObject)Field("transitionsPage");
            Check(transitions!=null&&transitions.GetComponentsInChildren<RangeSlider>().Length==3,"Pull-out / idle page uses one dual slider per range");
            Check(transitions.GetComponentsInChildren<Slider>().Length==4,"Pull-out ceiling and both periods are sliders");
            Check(transitions.GetComponentsInChildren<InputField>().Length==0,"The global transitions page needs no numeric text fields");
            Check(transitions.GetComponentsInChildren<Toggle>().Length==6,"Pull-out / idle page builds six switches");
            Check(((Toggle)Field("outLimitToggle")).isOn&&((Toggle)Field("idleMotionToggle")).isOn&&((Toggle)Field("climaxPullToggle")).isOn,"Pull-out limit, idle motion and climax pull-in default to on");
            ((Slider)Field("outCeilingSlider")).value=700/999f;Call("FlushGlobalSettings");
            var transitionStore=new KoiSync.Plugin.GlobalSettings(host.Config);
            Check(transitionStore.Stroke.OutCeiling==700&&transitionStore.Stroke.IdleMotionEnabled&&transitionStore.Stroke.ClimaxRotationEnabled,"Pull-out and idle settings persist as one global configuration");
            Check(host.Profiles.Animation(host.Profiles.NativeKey).Measurement.NativeStroke.OutCeiling==700,"The current animation follows the global pull-out ceiling");
            Call("ShowAxisMonitor");panel.Refresh();Canvas.ForceUpdateCanvases();
            var monitor=(GameObject)Field("monitorPage");
            Check(monitor.GetComponentsInChildren<Toggle>().Length==6&&Field("outputRange")!=null,"The value page owns the send switches and the global output range");
            Check(Array.Exists(monitor.GetComponentsInChildren<Text>(),t=>t.text.Contains("全局轴值限制")),"The value page titles the global limit, range and channel section");
            Call("ShowStrokeSettings");
        }
        catch(Exception ex){Finish(ex);yield break;}
        yield return new WaitForEndOfFrame();
        try
        {
            Capture("main-stroke-settings-v0126.png");Call("ShowHud");Call("ShowStudioTools");
            var hud=(GameObject)Field("hudObject");var alignment=(RectTransform)Field("alignmentWindow");
            Check(hud.activeInHierarchy&&alignment.gameObject.activeInHierarchy,"Stroke page, six-bar monitor and alignment can coexist");
            Call("CloseWorkspace");Check(hud.activeInHierarchy&&alignment.gameObject.activeInHierarchy,"Closing main workspace preserves both floating windows");
            Call("CloseStudioWindow",alignment);Call("ShowMotionTimeline");
            Check(!panel.LaboratoryOpen&&((RectTransform)Field("timelineWindow")).gameObject.activeInHierarchy,"Main timeline alone does not suspend device synchronization");
            playbackFixture=new NativePlaybackFixture(host.NativePlayback,panel,Check);
            var curve=(MotionCurve)Field("motionCurve");double duration=host.NativePlayback.Duration;
            for(int i=0;i<=120;i++)curve.Add(duration*i/120,duration,new[]{.5+.3*Math.Sin(i*Math.PI/60),.5,.5,.5,.5,.5},"fixture");
            ((Dropdown)Field("curveChannel")).value=1;Call("RefreshMotionTimeline");
            Check(Math.Abs(((TimelineStrip)Field("laboratoryTrack")).View.Span-duration)<.001,"New native animation fits the complete clip in the timeline viewport");
            Check(curve.Count>=Math.Min(100,Math.Floor(duration*MotionCurve.FrameRate))&&curve.Count>1,"Native timeline contains values at the clip's available frame resolution");
            Check(!host.Devices.Connected&&!host.Devices.Connecting,"No device connected during UI or storage tests");

        }
        catch(Exception ex){Finish(ex);yield break;}
        yield return new WaitForEndOfFrame();
        try{CaptureWindow((RectTransform)Field("timelineWindow"),"main-timeline-v0126.png");Finish(null);}catch(Exception ex){Finish(ex);}
    }
    private void CaptureWindow(RectTransform rect,string name)
    {
        Vector3[] corners=new Vector3[4];rect.GetWorldCorners(corners);int x=Mathf.RoundToInt(corners[0].x),y=Mathf.RoundToInt(corners[0].y),w=Mathf.RoundToInt(corners[2].x-corners[0].x),h=Mathf.RoundToInt(corners[2].y-corners[0].y);
        var image=new Texture2D(w,h,TextureFormat.RGB24,false);image.ReadPixels(new Rect(x,y,w,h),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Paths.GameRootPath,name),image.EncodeToPNG());Destroy(image);
    }
    private void Capture(string name)
    {
        Canvas.ForceUpdateCanvases();var win=(GameObject)Field("window");Vector3[] corners=new Vector3[4];win.GetComponent<RectTransform>().GetWorldCorners(corners);
        int x=Mathf.RoundToInt(corners[0].x),y=Mathf.RoundToInt(corners[0].y),w=Mathf.RoundToInt(corners[2].x-corners[0].x),h=Mathf.RoundToInt(corners[2].y-corners[0].y);
        var texture=new Texture2D(w,h,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(x,y,w,h),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(Paths.GameRootPath,name),texture.EncodeToPNG());Destroy(texture);
    }
    private void Finish(Exception error){if(playbackFixture!=null){playbackFixture.Dispose();playbackFixture=null;}if(error!=null)report.AppendLine("ERROR "+error);report.AppendLine("Completed "+count+" main stroke UI checks");File.WriteAllText(Output,report.ToString());Application.Quit();}
}
