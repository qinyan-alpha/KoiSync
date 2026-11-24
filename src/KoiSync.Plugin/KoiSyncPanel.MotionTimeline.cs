using System;
using System.Collections.Generic;
using KoiSync.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    public sealed partial class KoiSyncPanel
    {
        private readonly MotionCurve motionCurve=new MotionCurve();
        private readonly SceneProfile nativeTrackScene=new SceneProfile();
        private string motionCurveKey="";
        private string nativeTrackKey="";
        private SceneProfile recordedScene;
        private readonly Dictionary<int,List<StudioAutoPartition.PoseRecord>> recordedChoices=new Dictionary<int,List<StudioAutoPartition.PoseRecord>>();
        private int lastChoiceFrame=-1;
        private Text curveNotice;
        private Dropdown curveChannel;
        internal Func<bool> PlaybackKeyDown=()=>UnityEngine.Input.GetKeyDown(KeyCode.Space);
        private BepInEx.Configuration.ConfigEntry<BepInEx.Configuration.KeyboardShortcut> playbackKey;
        // The playback key is configurable; Space is the default because the timeline is usually opened
        // from the animation page rather than while the game's own menu is focused.
        private string BindPlaybackKey()
        {
            if(playbackKey==null)
            {
                playbackKey=host.Config.Bind("UI","TimelineKey",new BepInEx.Configuration.KeyboardShortcut(KeyCode.Space),"Timeline play / pause key");
                PlaybackKeyDown=()=>playbackKey.Value.IsDown();
            }
            return playbackKey.Value.MainKey.ToString();
        }
        private void ShowMotionTimeline()
        {
            if(alignmentWindow==null)BuildStudioTools();
            timelineWindow.gameObject.SetActive(true);SetVisible(true);timelineWindow.SetAsLastSibling();
            if(!host.IsMainGame){toolsOpen=true;if(alignmentDraft==null)LoadAlignment("");}
            RefreshMotionTimeline();
        }
        // The button toggles: a second press hides the timeline again instead of re-showing it.
        private void ToggleMotionTimeline()
        {
            if(timelineWindow!=null&&timelineWindow.gameObject.activeSelf)CloseStudioWindow(timelineWindow);
            else ShowMotionTimeline();
        }
        private void BuildNativeTimeline()
        {
            timelineWindow=FloatingWindow("KoiSync timeline","当前本体动画 · 时间轴",1016,485,new Vector2(100,-220),()=>CloseStudioWindow(timelineWindow));
            var t=timelineWindow;trackStatus=Label(t,"等待当前动画",15,49,655,30,14,Accent);
            Button(t,BindPlaybackKey()+" 播放 / 暂停",682,46,145,30,ToggleMotionPlayback);Button(t,"显示全长",839,46,161,30,()=>laboratoryTrack.Fit());
            var track=Rect(t,"Native motion time track",15,90,985,165);laboratoryTrack=track.gameObject.AddComponent<TimelineStrip>();laboratoryTrack.Initialize(font);laboratoryTrack.ReadOnly=true;
            laboratoryTrack.Seek=time=>LabAction(()=>host.NativePlayback.Seek(time));
            BuildCurveControls(t,272);
            BuildFunscriptControls(t,326);
            timelineWindow.gameObject.SetActive(false);
        }
        private void BuildCurveControls(RectTransform t,float y)
        {
            curveChannel=Dropdown(t,15,y,158,new[]{"无曲线","L0","L1","L2","R0","R1","R2"},i=>{laboratoryTrack.CurveAxis=i-1;laboratoryTrack.SetVerticesDirty();});
            Button(t,"清除曲线",185,y,108,32,()=>{motionCurve.Clear();recordedChoices.Clear();lastChoiceFrame=-1;});
            curveNotice=Label(t,"",309,y+2,682,43,12,Muted);laboratoryTrack.Curve=motionCurve;
        }
        private void ToggleMotionPlayback()
        {try{if(host.IsMainGame)host.NativePlayback.Toggle();else TimelineState.TogglePlaying();}catch(Exception ex){if(trackStatus!=null)trackStatus.text=ex.Message;}}
        internal void PollTimelineInput()
        {
            if(timelineWindow==null||!timelineWindow.gameObject.activeInHierarchy){GameHooks.SuppressUiToggle=false;return;}
            // While the pointer rests on the timeline the game's own shortcut handlers stay out of the way,
            // so the playback key cannot also hide the interface.
            GameHooks.SuppressUiToggle=TimelineHovered;
            var selected=EventSystem.current==null?null:EventSystem.current.currentSelectedGameObject;
            if(selected!=null&&selected.GetComponent<InputField>()!=null)return;
            if(PlaybackKeyDown())ToggleMotionPlayback();
        }
        // True while the pointer is inside the timeline window.
        internal bool TimelineHovered
        {
            get
            {
                if(timelineWindow==null||!timelineWindow.gameObject.activeInHierarchy)return false;
                Canvas canvas=canvasObject==null?null:canvasObject.GetComponent<Canvas>();
                Camera camera=canvas!=null&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
                return RectTransformUtility.RectangleContainsScreenPoint(timelineWindow,UnityEngine.Input.mousePosition,camera);
            }
        }
        internal void RecordMotionCurve()
        {
            var source=MonitoredSource;double time,duration;string key;
            if(host.IsMainGame)
            {
                var playback=host.NativePlayback;if(playback==null||!playback.Available)return;
                time=playback.Time;duration=playback.Duration;
                key=playback.Key+":"+(ChannelProfile==null?"":ChannelProfile.MotionKey);
            }
            else
            {
                if(!TimelineState.Available)return;time=TimelineState.Time;duration=TimelineState.Duration;
                if(recordedScene!=host.Profiles.Scene){recordedScene=host.Profiles.Scene;recordedChoices.Clear();motionCurve.Clear();lastChoiceFrame=-1;}
                key=host.Profiles.ScenePath+":"+host.Profiles.Revision;
                int frame=(int)Math.Round(time*4);
                if(frame!=lastChoiceFrame&&TimelineState.Playing)
                {
                    lastChoiceFrame=frame;recordedChoices[frame]=StudioAutoPartition.Capture(host.StudioAdapter);
                }
            }
            key+=":"+host.ScriptPlayback.Enabled+":"+host.ScriptPlayback.Revision;
            if(motionCurveKey!=key){motionCurveKey=key;motionCurve.Clear();}
            if(source!=null&&host.Pipeline.HasDesired)motionCurve.Add(time,duration,host.Pipeline.Desired,source.Id);
        }
        private void RefreshMotionTimeline()
        {
            if(timelineWindow==null||!timelineWindow.gameObject.activeInHierarchy)return;
            double time,duration;
            if(host.IsMainGame)
            {
                var playback=host.NativePlayback;time=playback.Time;duration=playback.Duration;if(!playback.Available&&motionCurve.Count>0)motionCurve.Clear();
                laboratoryTrack.Set(nativeTrackScene,time,duration,"");
                if(playback.Available&&nativeTrackKey!=playback.Key){nativeTrackKey=playback.Key;laboratoryTrack.Fit();}
                if(!playback.Available)nativeTrackKey="";
                trackStatus.text=(playback.Available?playback.Label:"等待当前动画")+" · "+time.ToString("F3")+" / "+duration.ToString("F3")+" s · "+(playback.Paused?"暂停":"播放");
            }
            else
            {
                time=TimelineState.Time;duration=TimelineState.Duration;
                laboratoryTrack.ControllerPointer=vrWorld;laboratoryTrack.Set(host.Profiles.Scene,time,duration,alignmentKey);
                TrackKeyboard();
                trackStatus.text=string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0:F3} / {1:F3} s · {2} · 可见 {3:F2} s",time,duration,TimelineState.Playing?"播放":"暂停",laboratoryTrack.View.Span);
            }
            RefreshFunscriptControls();
            var point=motionCurve.At(time);int axis=laboratoryTrack.CurveAxis;
            string value=axis<0?"选择通道查看曲线":MotionPipeline.Axes[axis]+" "+(point==null?"—":Math.Round(point.Values[axis]*999).ToString("000"));
            curveNotice.text="目标 "+value+" · 已记录 "+motionCurve.Count+" 点 · 空白为未采样\n"+(host.ScriptPlayback.Scripts==null?"": "橙色：已导入脚本 · 青色：输出目标 · ")+"空格 播放 / 暂停 · 中键定位 · Ctrl + 滚轮缩放";laboratoryTrack.SetVerticesDirty();
        }
    }
}
