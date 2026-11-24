using System;
using System.Collections.Generic;
using System.Globalization;
using KoiSync.Core;
using KoiSync.Wire;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    public sealed partial class KoiSyncPanel
    {
        private RectTransform alignmentWindow,timelineWindow;
        private AlignmentGizmo alignmentGizmo;
        private MeasurementProfile alignmentDraft;
        private SceneProfile alignmentScene;
        private string alignmentKey="",alignmentSource="",alignmentSourceLabel="",alignmentSignature="";
        private readonly List<string> alignmentSources=new List<string>();
        private Dropdown alignmentSourceBox,alignmentTargetBox,alignmentPairBox;
        private readonly List<string> alignmentPairs=new List<string>();
        private string alignmentPair;
        private NativeTarget[] alignmentTargets;
        private readonly InputField[] alignmentValues=new InputField[3];
        private readonly string[] shownAlignmentValues=new string[3];
        private InputField alignmentLength,trackStart,trackEnd;
        private Toggle alignmentReverse,alignmentGeometry;
        private BepInEx.Configuration.ConfigEntry<BepInEx.Configuration.KeyboardShortcut> rotateKey,hideKey;
        private Text alignmentStatus,alignmentCoordinates,alignmentContext,trackStatus,trackSelection;
        private TimelineStrip laboratoryTrack;
        private bool toolsOpen,alignmentRefreshing;
        private string alignmentNativeKey="";
        internal bool LaboratoryOpen{get{return toolsOpen&&Visible&&alignmentWindow!=null&&alignmentWindow.gameObject.activeInHierarchy;}}
        internal bool AlignmentEditing{get{return LaboratoryOpen&&alignmentWindow.gameObject.activeSelf;}}
        private TimelineSegment AlignmentSegment{get{return host.Profiles.Scene.Segments.Find(s=>s.Id==alignmentKey);}}
        private void LabAction(Action action)
        {try{action();}catch(Exception ex){alignmentStatus.text=ex.GetBaseException().Message;}}
        private RectTransform FloatingWindow(string name,string title,float width,float height,Vector2 position,Action close)
        {
            var r=Panel(canvasObject.transform,0,0,width,height,Background);r.name=name;r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=position;r.GetComponent<Image>().raycastTarget=true;
            var header=Panel(r,0,0,width,40,Card);header.GetComponent<Image>().raycastTarget=true;header.gameObject.AddComponent<PanelDrag>().Target=r;
            Label(header,title,14,7,width-65,29,17,Accent,FontStyle.Bold);Button(header,"×",width-40,5,30,30,()=>close());
            foreach(float edge in new[]{0f,width-7}){var border=Rect(r,"Move window border",edge,40,7,height-47);Image(border,Color.clear,false).raycastTarget=true;border.gameObject.AddComponent<PanelDrag>().Target=r;}
            AddResizeHandle(r);Canvas.ForceUpdateCanvases();var available=((RectTransform)r.parent).rect.size;
            if(available.x>0&&available.y>0){float scale=Mathf.Min(1,Mathf.Min(available.x/width,available.y/height));r.localScale=new Vector3(scale,scale,1);var bounds=(available-r.rect.size*scale)*.5f;r.anchoredPosition=new Vector2(Mathf.Clamp(position.x,-bounds.x,bounds.x),Mathf.Clamp(position.y,-bounds.y,bounds.y));}
            return r;
        }
        private void AddResizeHandle(RectTransform r)
        {
            // Bound to the bottom-right corner: a floating window that grows keeps its grip in the corner.
            var handle=Rect(r,"Resize window",0,0,23,23);handle.anchorMin=handle.anchorMax=new Vector2(1,0);handle.pivot=new Vector2(1,0);handle.anchoredPosition=Vector2.zero;
            Image(handle,new Color32(49,84,103,255),true).raycastTarget=true;
            Label(handle,"◢",3,1,20,21,15,Accent);handle.gameObject.AddComponent<WindowResize>().Target=r;
        }
        private void ShowStudioTools()
        {
            if(host.IsMainGame&&string.IsNullOrEmpty(host.Profiles.NativeKey))return;
            if(alignmentWindow==null)BuildStudioTools();
            bool alreadyOpen=toolsOpen;
            DismissDropdowns();SetVisible(true);toolsOpen=true;alignmentWindow.gameObject.SetActive(true);
            // 3D 对齐 opens the body alignment window only. The animation timeline and the six-value
            // window have their own entries and must not follow this button.
            if(!host.IsMainGame){if(timelineWindow!=null)timelineWindow.gameObject.SetActive(true);ShowHud();}
            alignmentWindow.SetAsLastSibling();if(timelineWindow!=null&&timelineWindow.gameObject.activeSelf)timelineWindow.SetAsLastSibling();RefreshLayers();
            host.DebugVisible=true;host.PauseOutput("3D 对齐编辑 · 预览，保留同步启用");
            if(!alreadyOpen){var current=host.Profiles.CurrentSegment;LoadAlignment(current==null?"":current.Id);}RefreshFloatingTools();
        }
        private void BuildStudioTools()
        {
            if(rotateKey==null)
            {
                rotateKey=host.Config.Bind("UI","RotateKey",new BepInEx.Configuration.KeyboardShortcut(KeyCode.R),"Alignment editor: switch between move and rotate");
                hideKey=host.Config.Bind("UI","HideKey",new BepInEx.Configuration.KeyboardShortcut(KeyCode.H),"Alignment editor: hide or show the selected element");
            }
            alignmentGizmo=gameObject.AddComponent<AlignmentGizmo>();
            alignmentGizmo.RotateKeyDown=()=>rotateKey.Value.IsDown();alignmentGizmo.HideKeyDown=()=>hideKey.Value.IsDown();alignmentGizmo.Changed=ReadGizmoValues;alignmentGizmo.SelectionChanged=()=>{ReadGizmoValues();RefreshLayers();};
            alignmentGizmo.Finished=()=>LabAction(()=>SaveAlignment(false));alignmentGizmo.InputBlocked=PointerOverInterface;
            float top=host.IsMainGame?91:133;
            alignmentTop=top;
            alignmentWindow=FloatingWindow("KoiSync alignment",host.IsMainGame?"3D 对齐 · 本体":"3D 对齐 · 工作室",570,top+330,new Vector2(-330,177),()=>CloseStudioWindow(alignmentWindow));
            var p=alignmentWindow;
            Button(p,"数值",host.IsMainGame?466:400,5,61,30,ToggleHud);
            if(!host.IsMainGame)Button(p,"轨道",466,5,61,30,()=>{if(timelineWindow.gameObject.activeSelf)CloseStudioWindow(timelineWindow);else{timelineWindow.gameObject.SetActive(true);timelineWindow.SetAsLastSibling();}});
            if(host.IsMainGame)alignmentContext=Label(p,"",14,45,542,43,13,Muted);
            else
            {
                alignmentPairBox=Dropdown(p,14,47,542,new[]{"选择角色对"},i=>{
                    if(alignmentRefreshing||i<0||i>=alignmentPairs.Count)return;
                    alignmentPair=alignmentPairs[i];alignmentSource=alignmentPair==""?"":TimelineSegment.NoSource;alignmentSourceLabel="";alignmentSignature="";alignmentGizmo.CancelDrag();LabAction(()=>SaveAlignment(false));RefreshFloatingTools();
                });
                alignmentSourceBox=Dropdown(p,14,88,542,new[]{"选择数据源"},i=>{
                    if(alignmentRefreshing||i<0||i>=alignmentSources.Count)return;
                    alignmentGizmo.CancelDrag();alignmentSource=alignmentSources[i];var source=host.StudioAdapter.Sources.Find(v=>v.SavedKey==alignmentSource);alignmentSourceLabel=source==null?"":source.Label;LabAction(()=>SaveAlignment(false));
                });
            }
            BuildLayers(p,top);
            alignmentCoordinates=Label(p,"局部 XYZ",195,top+3,150,25,12,Muted);
            Button(p,"移动 / 旋转 · "+rotateKey.Value.MainKey,387,top,169,30,()=>alignmentGizmo.ToggleRotation());
            for(int i=0;i<3;i++)
            {
                float x=195+i*121;Label(p,new[]{"X","Y","Z"}[i],x,top+45,19,23,12,Accent);alignmentValues[i]=Input(p,x+20,top+38,91,"0");
                alignmentValues[i].onEndEdit.AddListener(v=>{if(!alignmentRefreshing)LabAction(()=>{WriteGizmoValues();SaveAlignment(false);});});
            }
            var targets=new List<NativeTarget>();var labels=new List<string>();foreach(NativeTarget target in Enum.GetValues(typeof(NativeTarget)))if(target!=NativeTarget.BOTH_FEET&&(host.IsMainGame||NativeProfiles.ActorIndex(target)==0)){targets.Add(target);labels.Add(target==NativeTarget.AUTO?"AUTO":target.ToString());}alignmentTargets=targets.ToArray();
            alignmentTargetBox=Dropdown(p,195,top+79,361,labels.ToArray(),i=>{if(alignmentRefreshing||alignmentDraft==null)return;alignmentDraft.Target=alignmentTargets[i];LabAction(()=>SaveAlignment(false));});
            Label(p,"长度 ×",195,top+125,69,25,12,Muted);alignmentLength=Input(p,265,top+118,95,"1");
            alignmentLength.onEndEdit.AddListener(v=>{if(!alignmentRefreshing)LabAction(()=>SaveAlignment(true));});
            alignmentReverse=Toggle(p,"反向末端",398,top+126,159,false,v=>{if(alignmentRefreshing||alignmentDraft==null)return;alignmentDraft.ReverseCollider=v;LabAction(()=>SaveAlignment(false));});
            BuildPlaneControls(p,top+160);
            // 3D wireframe for the whole game; off by default so play stays clean.
            alignmentGeometry=Toggle(p,"显示 3D 线框",195,top+198,240,false,v=>{if(alignmentRefreshing)return;host.ViewSettings.ShowWireframe=v;host.ViewSettings.Save(host.Config);});
            alignmentRangeButton=Button(p,"更多轴范围配置 ▾",14,top+236,240,30,ToggleAlignmentRanges);
            Label(p,"单个动画的范围设置优先于全局",270,top+240,286,24,11,Muted);
            BuildAlignmentRangeControls(p,top+272);
            alignmentFooter=Rect(p,"Alignment footer",0,top+266,570,120);
            Button(alignmentFooter,"重置所选",14,0,124,31,()=>alignmentGizmo.ResetSelected());
            Button(alignmentFooter,"重置全部对齐",151,0,163,31,()=>LabAction(()=>{
                alignmentDraft.PlaneOffset=alignmentDraft.PlaneRotation=alignmentDraft.RootOffset=alignmentDraft.EndOffset=alignmentDraft.TargetOffset=alignmentDraft.TargetPlaneOffset=alignmentDraft.TargetPlaneRotation=new MotionVector();
                alignmentDraft.LengthScale=alignmentDraft.Gain=1;alignmentDraft.FixedRange=false;alignmentDraft.PlaneWidth=alignmentDraft.PlaneDepth=0;alignmentLength.text="1";ReadPlaneControls();ReadGizmoValues();ReadAlignmentRanges();SaveAlignment(false);
            }));
            Button(alignmentFooter,"应用并保存",327,0,229,31,()=>LabAction(()=>SaveAlignment(true)));
            alignmentStatus=Label(alignmentFooter,"",14,36,542,22,11,Muted);
            SetAlignmentExpanded(false);
            if(host.IsMainGame){BuildNativeTimeline();alignmentWindow.gameObject.SetActive(false);return;}
            timelineWindow=FloatingWindow("KoiSync timeline","工作室实验 · Timeline",1016,565,new Vector2(234,-315),()=>CloseStudioWindow(timelineWindow));
            var t=timelineWindow;
            Button(t,"工作台",636,5,100,30,ShowWorkspace);
            Button(t,"3D 对齐",748,5,100,30,()=>{alignmentWindow.gameObject.SetActive(true);alignmentWindow.SetAsLastSibling();});
            Button(t,"数值监控",860,5,100,30,ToggleHud);
            trackStatus=Label(t,"",15,49,461,26,13,Accent);
            Button(t,"自动分区…",482,46,187,30,OpenAutoPartition);
            Button(t,BindPlaybackKey()+" 播放 / 暂停",682,46,145,30,ToggleMotionPlayback);Button(t,"显示全长",839,46,161,30,()=>laboratoryTrack.Fit());
            var track=Rect(t,"Laboratory time track",15,83,985,170);laboratoryTrack=track.gameObject.AddComponent<TimelineStrip>();laboratoryTrack.Initialize(font);
            laboratoryTrack.Seek=time=>LabAction(()=>TimelineState.Seek(time));
            laboratoryTrack.Select=time=>LabAction(()=>{SaveAlignment(true);var s=host.Profiles.Scene.At(time);LoadAlignment(s==null?"":s.Id);});
            laboratoryTrack.Context=(time,screen)=>LabAction(()=>OpenTrackContext(time,screen));
            laboratoryTrack.Edited=id=>LabAction(()=>{SaveAlignment(true);host.Profiles.Changed(true);LoadAlignment(id);});
            Label(t,"左键选择 · 边缘拖动范围 · 长按整段移动 · 中键定位 · 右键菜单 · Ctrl + 滚轮缩放 · Del / Ctrl+D 删除",15,264,981,24,12,Muted);
            Button(t,"建立全长段",15,298,132,31,()=>LabAction(CreateTimelineSegment));
            trackStart=Input(t,160,297,93,"0");trackEnd=Input(t,267,297,93,"0");
            Button(t,"应用起止秒",374,297,131,32,()=>LabAction(ApplyTimelineBounds));
            Button(t,"删除所选段",519,297,132,32,()=>LabAction(DeleteTimelineSelection));
            trackSelection=Label(t,"",666,297,333,48,12,Muted);
            BuildCurveControls(t,352);
            BuildFunscriptControls(t,406);
            alignmentWindow.gameObject.SetActive(false);timelineWindow.gameObject.SetActive(false);
        }
        private void LoadAlignment(string key)
        {
            alignmentGizmo.CancelDrag();alignmentScene=host.Profiles.Scene;alignmentKey=key;var s=AlignmentSegment;
            alignmentNativeKey=host.Profiles.NativeKey;
            var original=host.IsMainGame?host.Profiles.NativeMeasurement:s==null?alignmentScene.Defaults:s.Measurement;
            alignmentDraft=original==null?new MeasurementProfile{AlignmentVersion=1,LengthScale=host.StudioOptions.LengthScale.Value,Gain=host.StudioOptions.Gain.Value,Target=host.StudioOptions.Target.Value,ReverseCollider=host.StudioOptions.ReverseCollider.Value}:original.Clone();
            if(host.IsMainGame&&host.Native.Override.Value!=NativeTarget.AUTO)alignmentDraft.Target=host.Native.Override.Value;
            alignmentSource=s==null?alignmentScene.DefaultSource:s.Source;alignmentSourceLabel=s==null?alignmentScene.DefaultLabel:s.Label;
            alignmentGizmo.Draft=alignmentDraft;alignmentGizmo.Preview=null;alignmentSignature="";alignmentPair=null;
            alignmentRefreshing=true;
            alignmentLength.text=alignmentDraft.LengthScale.ToString("G",CultureInfo.InvariantCulture);
            alignmentReverse.isOn=alignmentDraft.ReverseCollider;alignmentGeometry.isOn=host.ViewSettings.ShowWireframe;
            alignmentTargetBox.value=Math.Max(0,Array.IndexOf(alignmentTargets,alignmentDraft.Target));alignmentTargetBox.RefreshShownValue();
            if(trackStart!=null){trackStart.text=s==null?"0":s.Start.ToString("F3",CultureInfo.InvariantCulture);trackEnd.text=s==null?"0":s.End.ToString("F3",CultureInfo.InvariantCulture);}
            ReadPlaneControls();ReadAlignmentRanges();
            alignmentRefreshing=false;ReadGizmoValues();alignmentStatus.text="";
        }
        private void WriteGizmoValues(){if(alignmentDraft==null)return;bool changed=false;for(int i=0;i<3;i++)if(alignmentValues[i].text!=shownAlignmentValues[i])changed=true;if(changed)alignmentGizmo.Coordinates=new MotionVector(ProfileNumber(alignmentValues[0]),ProfileNumber(alignmentValues[1]),ProfileNumber(alignmentValues[2]));}
        private void ReadGizmoValues()
        {
            if(alignmentGizmo==null||alignmentCoordinates==null)return;alignmentRefreshing=true;
            try{var v=alignmentGizmo.Coordinates;double[] values={v.X,v.Y,v.Z};for(int i=0;i<3;i++)alignmentValues[i].text=shownAlignmentValues[i]=values[i].ToString("0.######",CultureInfo.InvariantCulture);RefreshLayers();
                alignmentCoordinates.text=alignmentGizmo.Rotating?"旋转 °":alignmentGizmo.PlaneSelected?"世界 XYZ":"局部 XYZ";}
            finally{alignmentRefreshing=false;}
        }
        private void SaveAlignment(bool readInputs)
        {
            if(alignmentDraft==null||alignmentScene!=host.Profiles.Scene||host.IsMainGame&&alignmentNativeKey!=host.Profiles.NativeKey)return;
            if(readInputs)
            {
                WriteGizmoValues();var m=alignmentDraft.Clone();m.LengthScale=ProfileNumber(alignmentLength);m.Validate();alignmentDraft=m;alignmentGizmo.Draft=m;
            }
            alignmentDraft.Validate();
            if(host.IsMainGame){host.Profiles.Animation(alignmentNativeKey).Measurement=alignmentDraft.Clone();host.Native.Override.Value=NativeTarget.AUTO;host.Profiles.Changed(false);alignmentStatus.text=host.Profiles.Status;return;}
            var s=AlignmentSegment;
            if(s==null){alignmentScene.Defaults=alignmentDraft.Clone();alignmentScene.DefaultSource=alignmentSource;alignmentScene.DefaultLabel=alignmentSourceLabel;}
            else{s.Measurement=alignmentDraft.Clone();s.Source=alignmentSource;s.Label=alignmentSourceLabel;}
            host.Profiles.Changed(true);alignmentStatus.text=host.Profiles.Status;
        }
        private void CreateTimelineSegment()
        {
            if(host.Profiles.Scene.Segments.Count>0)throw new ArgumentException("已有时间块，请在需要的位置按右键分割");
            if(!TimelineState.Available||TimelineState.Duration<=.001)throw new ArgumentException("请先打开 Timeline 并设置总时长");
            SaveAlignment(true);var s=new TimelineSegment{Start=0,End=TimelineState.Duration,Source=alignmentSource,Label=alignmentSourceLabel,Measurement=alignmentDraft.Clone()};host.Profiles.Scene.Segments.Add(s);host.Profiles.Changed(true);LoadAlignment(s.Id);
        }
        private void ApplyTimelineBounds()
        {
            var s=AlignmentSegment;if(s==null)throw new ArgumentException("先选择一个时间块");
            double a=trackStart.text==s.Start.ToString("F3",CultureInfo.InvariantCulture)?s.Start:ProfileNumber(trackStart),b=trackEnd.text==s.End.ToString("F3",CultureInfo.InvariantCulture)?s.End:ProfileNumber(trackEnd);
            if(a<0||b<=a)throw new ArgumentException("终点必须大于起点");foreach(var other in host.Profiles.Scene.Segments)if(other!=s&&a<other.End&&b>other.Start)throw new ArgumentException("时间块不能重叠");
            s.Start=a;s.End=b;host.Profiles.Scene.Validate();host.Profiles.Changed(true);
        }
        private void CloseStudioWindow(RectTransform closing)
        {
            if(host.IsMainGame&&closing==timelineWindow){DismissDropdowns();closing.gameObject.SetActive(false);toolsOpen=alignmentWindow.gameObject.activeSelf;HideIfEmpty();return;}
            if(!toolsOpen)return;
            if(autoPartitionRunning){cancelAutoPartition=true;return;}
            try{SaveAlignment(true);}catch(Exception ex){alignmentStatus.text=ex.Message;return;}
            DismissDropdowns();DismissTrackPopup();if(laboratoryTrack!=null)laboratoryTrack.CancelGesture();closing.gameObject.SetActive(false);
            toolsOpen=alignmentWindow.gameObject.activeSelf||timelineWindow!=null&&timelineWindow.gameObject.activeSelf;
            alignmentGizmo.Editing=AlignmentEditing;alignmentGizmo.CancelDrag();alignmentGizmo.Redraw();HideIfEmpty();
        }
        private bool PointerOverInterface()
        {
            if(EventSystem.current==null)return false;var data=new PointerEventData(EventSystem.current){position=UnityEngine.Input.mousePosition};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);return hits.Count>0;
        }
        internal bool PointerOverPanel{get{return PointerOverInterface();}}

        internal Candidate AlignmentPreview(Camera camera)
        {
            if(alignmentGizmo==null)return null;alignmentGizmo.Editing=AlignmentEditing;alignmentGizmo.ViewCamera=camera;alignmentGizmo.Extent=host.ViewSettings.Extent;
            if(!LaboratoryOpen){alignmentGizmo.Redraw();return null;}
            if(host.IsMainGame)
            {
                if(alignmentNativeKey!=host.Profiles.NativeKey)LoadAlignment("");
                var native=host.PreviewNative(alignmentDraft);alignmentGizmo.Preview=native;alignmentGizmo.Redraw();return native;
            }
            if(alignmentScene!=host.Profiles.Scene)LoadAlignment("");
            var source=alignmentSource.Length==0?host.StudioAdapter.Find(host.SelectedId):host.StudioAdapter.Sources.Find(s=>s.SavedKey==alignmentSource);
            Candidate candidate=null;
            try
            {
                if(source!=null)candidate=source.Sample(1,1,alignmentDraft.ReverseCollider,alignmentDraft.Target,true);
                if(source!=null&&candidate==null)alignmentStatus.text=source.Error;
                if(candidate!=null){bool upgrade=alignmentDraft.AlignmentVersion==0;alignmentDraft.UpgradeStudio(candidate.Native);if(upgrade)ReadGizmoValues();alignmentDraft.Apply(candidate);}
            }
            catch(ArgumentException ex){alignmentStatus.text=ex.Message;candidate=null;}
            bool first=alignmentGizmo.Preview==null;alignmentGizmo.Preview=candidate;if(first&&candidate!=null)ReadGizmoValues();alignmentGizmo.Redraw();return candidate;
        }
        private void RefreshFloatingTools()
        {
            RefreshMotionTimeline();
            if(!LaboratoryOpen||alignmentWindow==null)return;
            if(alignmentScene!=host.Profiles.Scene||host.IsMainGame&&alignmentNativeKey!=host.Profiles.NativeKey)LoadAlignment("");
            if(host.IsMainGame)
            {
                // The concrete animation type of the current action, right below the window title.
                alignmentContext.text=host.Profiles.NativeKey+"\n类型 "+alignmentDraft.Target+(host.Global.AxisTouched(0)?" · 本动画已自定义轴范围":" · 轴范围跟随全局");
            }
            else
            {
            RefreshAlignmentPairs();
            double time=TimelineState.Time;laboratoryTrack.ControllerPointer=vrWorld;laboratoryTrack.Set(host.Profiles.Scene,time,TimelineState.Duration,alignmentKey);
            trackStatus.text=string.Format(CultureInfo.InvariantCulture,"{0:F3} / {1:F3} s  ·  {2}  ·  可见 {3:F2} s",time,TimelineState.Duration,TimelineState.Playing?"播放":"暂停",laboratoryTrack.View.Span);
            var segment=AlignmentSegment;trackSelection.text=segment==null?"编辑：场景默认配置":string.Format(CultureInfo.InvariantCulture,"编辑块：{0:F3} — {1:F3} s",segment.Start,segment.End);
            }
            RefreshLayers();
            var candidate=alignmentGizmo.Preview;
            bool editPending=false;for(int i=0;i<3;i++)if(alignmentValues[i].isFocused||alignmentValues[i].text!=shownAlignmentValues[i])editPending=true;
            if(candidate!=null&&alignmentGizmo.PlaneSelected&&!alignmentGizmo.Rotating&&!alignmentGizmo.Dragging&&!editPending)ReadGizmoValues();
            if(laboratoryTrack!=null&&Math.Abs(laboratoryTrack.SelectionStart-laboratoryTrack.SelectionEnd)>.001)trackSelection.text+="\n选区 "+Math.Min(laboratoryTrack.SelectionStart,laboratoryTrack.SelectionEnd).ToString("F2")+" — "+Math.Max(laboratoryTrack.SelectionStart,laboratoryTrack.SelectionEnd).ToString("F2");
        }
    }
}
