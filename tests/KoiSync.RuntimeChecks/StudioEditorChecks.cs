using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using KoiSync.Core;
using KoiSync.Plugin;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[BepInPlugin("local.koisync.editorchecks","KoiSync studio editor checks","0.1.0")]
[BepInDependency("local.koisync")]
public sealed class StudioEditorChecks:BaseUnityPlugin
{
    private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    private readonly StringBuilder report=new StringBuilder();
    private KoiSyncPanel panel;
    private KoiSync.Plugin.Plugin host;
    private TestPointer pointer;
    private StandaloneInputModule module;
    private int count;
    private string Output{get{return Path.Combine(Paths.GameRootPath,"studio-editor-checks.txt");}}
    private object Field(object o,string n){return o.GetType().GetField(n,F).GetValue(o);}
    private object Call(object o,string n,params object[] args){return o.GetType().GetMethod(n,F).Invoke(o,args);}
    private void Check(bool ok,string message){report.AppendLine((ok?"PASS ":"FAIL ")+message);File.WriteAllText(Output,report.ToString());if(!ok)throw new Exception(message);count++;}
    private void Awake(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"--koisync-editor-checks")>=0){Application.runInBackground=true;StartCoroutine(Run());}}
    private IEnumerator Run()
    {
        yield return new WaitForSecondsRealtime(9);host=FindObjectOfType<KoiSync.Plugin.Plugin>();panel=(KoiSyncPanel)Field(host,"panel");panel.SetVisible(true);yield return null;
        module=EventSystem.current.GetComponent<StandaloneInputModule>();pointer=gameObject.AddComponent<TestPointer>();typeof(BaseInputModule).GetField("m_InputOverride",F).SetValue(module,pointer);module.forceModuleActive=true;module.ActivateModule();
        var steps=Checks();while(true){bool more;object wait;try{more=steps.MoveNext();wait=more?steps.Current:null;}catch(Exception ex){report.AppendLine("ERROR "+ex);break;}if(!more)break;yield return wait;}
        report.AppendLine("Completed "+count+" studio editor checks");File.WriteAllText(Output,report.ToString());Application.Quit();
    }
    private Vector2 Point(Component c,float x=.5f,float y=.5f){var r=(RectTransform)c.transform;return RectTransformUtility.WorldToScreenPoint(null,r.TransformPoint(new Vector3(Mathf.Lerp(r.rect.xMin,r.rect.xMax,x),Mathf.Lerp(r.rect.yMin,r.rect.yMax,y))));}
    private void Step(Vector2 p,bool down,bool held,bool up,Vector2 wheel,int button=0){pointer.ButtonIndex=button;pointer.Position=p;pointer.Down=down;pointer.Held=held;pointer.Up=up;pointer.Wheel=wheel;module.UpdateModule();module.Process();pointer.Down=pointer.Up=false;pointer.Wheel=Vector2.zero;}
    private void Click(Vector2 p,int button=0){Step(p,true,true,false,Vector2.zero,button);Step(p,false,false,true,Vector2.zero,button);Canvas.ForceUpdateCanvases();}
    private Button Button(string name){foreach(var b in ((GameObject)Field(panel,"canvasObject")).GetComponentsInChildren<Button>())if(b.GetComponentInChildren<Text>().text==name)return b;throw new Exception("No visible button "+name);}
    private void Press(string name){Click(Point(Button(name)));}
    private IEnumerator Checks()
    {
        Check(host.StudioAdapter!=null&&!host.Devices.Connected,"Studio editor uses synthetic scene and disconnected devices");
        Check(!File.ReadAllText(Path.Combine(Paths.GameRootPath,"studio-binding-checks.txt")).Contains("FAIL"),"Existing binding, scene callbacks and storage regression passed");
        var sources=(WorkspaceDropdown)Field(panel,"sources");for(int i=0;i<18;i++)sources.options.Add(new Dropdown.OptionData("Long source label for scroll fixture / source "+i));
        Click(Point(sources));yield return new WaitForSecondsRealtime(.2f);var popup=sources.transform.Find("Dropdown List");Check(popup!=null,"Source combobox opens through the actual input module");
        var scroll=popup.GetComponent<ScrollRect>();float old=scroll.content.anchoredPosition.y;Step(Point(scroll),false,false,false,new Vector2(0,-1));
        Check(scroll.content.anchoredPosition.y-old>=40,"One wheel notch scrolls approximately one complete source row");
        Check(!scroll.inertia&&scroll.scrollSensitivity==46,"Source popup has immediate deterministic scrolling");
        Call(panel,"Refresh");Check(scroll.content.anchoredPosition.y-old>=40,"Telemetry refresh does not reset the open popup scroll position");sources.ClosePopup();yield return null;
        Press("工作室实验");yield return null;Check(((GameObject)Field(panel,"playbackPage"))==null,"Studio has no timeline page in the main workspace");
        Press("3D 对齐 / 时间轨道");yield return new WaitForEndOfFrame();
        var small=(RectTransform)Field(panel,"alignmentWindow");var timeline=(RectTransform)Field(panel,"timelineWindow");
        var workspace=(GameObject)Field(panel,"window");var hud=(GameObject)Field(panel,"hudObject");
        Check(small.gameObject.activeInHierarchy&&timeline.gameObject.activeInHierarchy&&workspace.activeInHierarchy&&hud.activeInHierarchy,"Workspace, alignment, Timeline and six-axis HUD coexist");
        Call(panel,"ShowAxisMonitor");Call(panel,"Refresh");Check(((GameObject)Field(panel,"monitorPage")).activeInHierarchy&&small.gameObject.activeInHierarchy&&hud.activeInHierarchy,"Detailed axis monitor remains visible alongside the editors and HUD");
        Call(panel,"CloseWorkspace");Check(!workspace.activeInHierarchy&&timeline.gameObject.activeInHierarchy&&small.gameObject.activeInHierarchy&&hud.activeInHierarchy,"Closing workspace leaves every floating window open");
        Check(panel.CameraBlockedAt(Point(small)),"Floating tool hit captures camera input"); Check(!panel.CameraBlockedAt(new Vector2(Screen.width*.5f,Screen.height-15)),"Empty scene outside floating tools permits native camera input");
        var header=small.GetComponentInChildren<PanelDrag>();Vector2 before=small.anchoredPosition,p=Point(header,.4f,.5f);
        Step(p,true,true,false,Vector2.zero);Step(p+new Vector2(38,-22),false,true,false,Vector2.zero);Step(p+new Vector2(38,-22),false,true,false,Vector2.zero);Step(p+new Vector2(38,-22),false,false,true,Vector2.zero);
        Check((small.anchoredPosition-before).magnitude>10,"Floating alignment title supports actual pointer dragging");
        yield return new WaitForEndOfFrame();
        var resize=small.GetComponentInChildren<WindowResize>();float scale=small.localScale.x;p=Point(resize);
        var resizeHits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=p},resizeHits);report.AppendLine("Resize at "+p+" window "+small.anchoredPosition+" scale "+scale+" hit "+(resizeHits.Count==0?"none":resizeHits[0].gameObject.name));
        Check(resizeHits.Count>0&&ExecuteEvents.GetEventHandler<IDragHandler>(resizeHits[0].gameObject)==resize.gameObject,"Resize corner is the actual pointer drag target");
        Step(p,true,true,false,Vector2.zero);Step(p+new Vector2(36,-24),false,true,false,Vector2.zero);Step(p+new Vector2(65,-45),false,true,false,Vector2.zero);Step(p+new Vector2(65,-45),false,false,true,Vector2.zero);report.AppendLine("Resize resulting scale "+small.localScale.x);
        Check(small.localScale.x>scale,"Floating tool can be resized with its corner handle");small.localScale=Vector3.one;small.anchoredPosition=new Vector2(-275,45);Canvas.ForceUpdateCanvases();
        host.Profiles.SceneSaved(Path.Combine(Paths.GameRootPath,"neutral-editor-scene.png"));
        var sourceBox=(Dropdown)Field(panel,"alignmentSourceBox");Call(panel,"RefreshFloatingTools");var pairBox=(Dropdown)Field(panel,"alignmentPairBox");var first=host.StudioAdapter.Sources[0];pairBox.value=((List<string>)Field(panel,"alignmentPairs")).IndexOf(first.PairKey);Call(panel,"RefreshFloatingTools");
        Check((string)Field(panel,"alignmentSource")==TimelineSegment.NoSource,"Choosing an actor pair waits for a specific node instead of inheriting another pair");
        sourceBox.value=((List<string>)Field(panel,"alignmentSources")).IndexOf(first.SavedKey);
        Press("建立全长段");Check(host.Profiles.Scene.Segments.Count==1,"Track creates a full-duration block with current source settings");
        var strip=(TimelineStrip)Field(panel,"laboratoryTrack");Call(panel,"RefreshFloatingTools");double oldTime=TimelineState.Time;
        Click(Point(strip,.3f,.4f));Check(Math.Abs(TimelineState.Time-oldTime)<.0001,"Left selection does not move the Timeline playback head");
        Click(Point(strip,.4f,.4f),2);Check(Math.Abs(TimelineState.Time-.4)<.015,"Middle click seeks the actual Timeline playhead");
        Click(Point(strip,.6f,.4f),1);yield return new WaitForEndOfFrame();
        Check(host.Profiles.Scene.Segments.Count==1,"Right click opens context without immediately splitting");
        foreach(var button in ((GameObject)Field(panel,"trackPopup")).GetComponentsInChildren<Button>()){var txt=button.GetComponentInChildren<Text>();if(txt!=null&&txt.text.StartsWith("切割 ·")){Click(Point(button));break;}}
        Check(host.Profiles.Scene.Segments.Count==2&&Math.Abs(host.Profiles.Scene.Segments[1].Start-.6)<.015,"Right click splits at the pointed time, independent of playhead");
        Check(Math.Abs(TimelineState.Time-.4)<.015,"Right splitting leaves playback time unchanged");
        double span=strip.View.Span;strip.ControlHeld=()=>true;Step(Point(strip,.5f,.4f),false,false,false,new Vector2(0,2));
        Check(strip.View.Span<span&&strip.View.Start>0,"Ctrl wheel zooms the track around the pointer");strip.ControlHeld=()=>false;
        double start=strip.View.Start;Step(Point(strip,.5f,.4f),false,false,false,new Vector2(0,-1));Check(strip.View.Start>start,"Unmodified wheel pans the visible time range");
        strip.Fit();Call(panel,"RefreshFloatingTools");yield return null;Check(strip.GetComponentsInChildren<Text>().Length>1,"Track renders readable numbered ruler ticks and source labels");
        p=Point(strip,.15f,.4f);Step(p,true,true,false,Vector2.zero);Step(Point(strip,.45f,.4f),false,true,false,Vector2.zero);Step(Point(strip,.48f,.4f),false,true,false,Vector2.zero);Step(Point(strip,.48f,.4f),false,false,true,Vector2.zero);
        Check(Math.Abs(strip.SelectionEnd-strip.SelectionStart)<.0001&&!strip.Dragging,"Mouse release clears the selection overlay and all drag capture");
        Click(Point(strip,.75f,.4f));Call(panel,"RefreshFloatingTools");var right=host.Profiles.Scene.Segments[1];
        ((InputField)Field(panel,"alignmentLength")).text="1.2";Press("应用并保存");
        Check(Math.Abs(right.Measurement.LengthScale-1.2)<.0001,"Length is stored on the selected time block");
        Check(host.Profiles.Scene.Segments[0].Measurement.LengthScale!=right.Measurement.LengthScale,"Editing a selected block does not change its neighbour");
        var camObject=new GameObject("Neutral editor camera");var cam=camObject.AddComponent<Camera>();cam.depth=1000;cam.cullingMask=0;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color32(19,31,44,255);cam.nearClipPlane=.001f;
        var chosen=host.StudioAdapter.Sources[0];var rawCandidate=chosen.Sample(1,1,false,NativeTarget.AUTO,true);
        var hips=chosen.Source.Bones.Get("cf_j_hips");if(hips!=null)hips.position=AlignmentGizmo.V(rawCandidate.Native.Origin)+new Vector3(0,.03f,0);var targetHips=chosen.Target.Bones.Get("cf_j_hips");if(targetHips!=null)targetHips.position=AlignmentGizmo.V(rawCandidate.Native.Target)+new Vector3(0,.05f,.03f);
        Vector3 center=AlignmentGizmo.V(rawCandidate.Native.Origin)+new Vector3(.04f,.02f,0);cam.transform.position=center+new Vector3(.27f,.23f,-.5f);cam.transform.LookAt(center);
        var preview=panel.AlignmentPreview(cam);Check(preview!=null&&preview.Native.HasBodyFrame&&preview.Native.HasTargetFrame,"Actual bound nodes provide source and target pelvis frames");
        var g=(AlignmentGizmo)Field(panel,"alignmentGizmo");g.Choose(AlignmentPart.Root);g.Redraw();
        Vector3 anchor=chosen.Source.Bones.Get("cm_J_dan101_00").position;float length=(float)Field(g,"handleLength");Vector3 root=g.Position,axis=AlignmentGizmo.V(preview.Native.ReferenceFrame.X);
        g.PointerDown(cam.WorldToScreenPoint(root+axis*length*.65f));Check(g.Dragging,"3D translation handle is selectable by its scene ray");
        double rootX=g.Draft.RootOffset.X;g.PointerDrag(cam.WorldToScreenPoint(root+axis*(length*.65f+.012f)));g.CancelDrag();g.Finished();
        Check(Math.Abs(g.Draft.RootOffset.X-rootX-.012)<.001,"Dragging an XYZ handle changes the calculation offset in blue-plane coordinates");
        Check((chosen.Source.Bones.Get("cm_J_dan101_00").position-anchor).sqrMagnitude==0,"Gizmo editing never changes the source bone transform");
        preview=panel.AlignmentPreview(cam);var point=AlignmentGizmo.V(preview.Native.End);Check(g.Pick(cam.WorldToScreenPoint(point),cam.ScreenPointToRay(cam.WorldToScreenPoint(point)))==AlignmentPart.End,"Actual endpoint marker can be selected in 3D");
        g.Choose(AlignmentPart.Plane);g.ToggleRotation();Check(g.Rotating,"R action switches a selected reference plane to rotation rings");
        var xyz=(InputField[])Field(panel,"alignmentValues");xyz[1].text="25";Press("应用并保存");preview=panel.AlignmentPreview(cam);
        Check(Math.Abs(g.Draft.PlaneRotation.Y-25)<.001&&Math.Abs(preview.Native.ReferenceFrame.X.Z)>.3,"Small-window rotation values rotate the actual reference frame");
        g.Redraw();length=(float)Field(g,"handleLength");Vector3 ringOffset=(Vector3.right*.65f+Vector3.up*.76f).normalized*length;MotionVector rotationBefore=g.Draft.PlaneRotation;
        g.PointerDown(cam.WorldToScreenPoint(g.Position+ringOffset));Check(g.Dragging,"Rotation ring is selectable through the scene pointer");
        g.PointerDrag(cam.WorldToScreenPoint(g.Position+Quaternion.AngleAxis(30,Vector3.forward)*ringOffset));g.CancelDrag();g.Finished();
        Check((g.Draft.PlaneRotation-rotationBefore).Length>5,"Dragging a rotation ring changes the persisted reference orientation");g.Value=rotationBefore;g.Finished();
        g.Choose(AlignmentPart.TargetPlane);g.ToggleRotation();xyz[0].text="35";Press("应用并保存");preview=panel.AlignmentPreview(cam);
        Check(Math.Abs(g.Draft.TargetPlaneRotation.X-35)<.001,"Interaction-side pelvis line alignment is independently editable");
        var store=new PlaybackProfiles(Paths.GameRootPath);store.LoadScene(host.Profiles.ScenePath);
        Check(Math.Abs(store.Scene.Segments[1].Measurement.PlaneRotation.Y-25)<.001&&Math.Abs(store.Scene.Segments[1].Measurement.TargetPlaneRotation.X-35)<.001,"Scene reload preserves both reference rotations and the selected block");
        g.Choose(AlignmentPart.Root);Press("重置所选");Check(g.Draft.RootOffset.Length==0,"Reset selected returns only that calculation point to its anchor");
        Check(Math.Abs(g.Draft.PlaneRotation.Y-25)<.001,"Reset point retains other reference alignments");
        g.Choose(AlignmentPart.Plane);preview=panel.AlignmentPreview(cam);var geometry=(DebugGeometry)Field(host,"geometry");geometry.RenderCamera=cam;geometry.Display(preview,true,0);Call(panel,"RefreshFloatingTools");
        yield return new WaitForEndOfFrame();
        var layerRows=(Button[])Field(panel,"layerRows");
        Check(layerRows.Length==5&&Array.TrueForAll(layerRows,row=>row.transform.IsChildOf(small)),"All five selectable object rows are merged into the alignment window");
        Check(small.rect.height<450,"Merged alignment window remains compact");
        g.SetObjectVisible(AlignmentPart.End,false);preview=panel.AlignmentPreview(cam);Check(!preview.Native.ObjectVisible((int)AlignmentPart.End),"H action persists selected object visibility into the sample");
        var hiddenEnd=cam.WorldToScreenPoint(AlignmentGizmo.V(preview.Native.End));Check(g.Pick(hiddenEnd,cam.ScreenPointToRay(hiddenEnd))!=AlignmentPart.End,"Hidden endpoint is excluded from 3D picking");g.SetObjectVisible(AlignmentPart.End,true);
        ((InputField)Field(panel,"planeWidth")).text="0.3";((InputField)Field(panel,"planeWidth")).onEndEdit.Invoke("0.3");preview=panel.AlignmentPreview(cam);Check(Math.Abs(preview.Native.Width-.3)<.001,"Plane width numeric input controls the actual reference plane");
        Capture("studio-editor-v0119.png");
        Check(panel.CameraBlockedAt(cam.WorldToScreenPoint(g.Position)),"Hovering a 3D handle captures native camera input");
        var colors=host.Profiles.Scene.Segments;Check(colors[0].ColorIndex!=colors[1].ColorIndex,"Split blocks with the same source retain distinct default colors");
        Click(Point(strip,.75f,.4f),1);yield return new WaitForEndOfFrame();Press("重命名该时间段");yield return new WaitForEndOfFrame();
        var nameInput=((GameObject)Field(panel,"trackPopup")).GetComponentInChildren<InputField>();nameInput.text="自定义测试段";Press("保存名称");Check(right.Name=="自定义测试段","Context rename changes the segment name without changing source");
        p=Point(strip,.602f,.4f);Step(p,true,true,false,Vector2.zero);Step(Point(strip,.68f,.4f),false,true,false,Vector2.zero);Step(Point(strip,.7f,.4f),false,true,false,Vector2.zero);Step(Point(strip,.7f,.4f),false,false,true,Vector2.zero);
        Check(Math.Abs(right.Start-.7)<.015&&Math.Abs(right.End-1)<.001,"Left edge dragging resizes the block and leaves a gap");
        Click(Point(strip,.65f,.4f),1);yield return new WaitForEndOfFrame();
        var menuButtons=((GameObject)Field(panel,"trackPopup")).GetComponentsInChildren<Button>();Check(menuButtons.Length==1,"Empty area context contains exactly one creation command");
        Press("建立新的时间编辑区域");Check(host.Profiles.Scene.Segments.Count==3&&host.Profiles.Scene.At(.65).Source==TimelineSegment.NoSource,"Blank context creates a no-source interval only inside the gap");
        Call(panel,"RefreshFloatingTools");yield return new WaitForEndOfFrame();
        p=Point(strip,.83f,.4f);Step(p,true,true,false,Vector2.zero);yield return new WaitForSecondsRealtime(.36f);yield return new WaitForEndOfFrame();Check(strip.Lifted,"Long hold lifts the selected block to its fixed elevated row");
        Step(Point(strip,.1f,.4f),false,true,false,Vector2.zero);Step(Point(strip,.12f,.4f),false,true,false,Vector2.zero);Step(Point(strip,.12f,.4f),false,false,true,Vector2.zero);
        Check(host.Profiles.Scene.Segments[0]==right&&right.Name=="自定义测试段"&&!strip.Dragging,"Dropping a lifted block reorders it with its source and custom name");
        Call(panel,"DeleteTimelineSelection");Check(!host.Profiles.Scene.Segments.Contains(right),"Selection delete removes the reordered block");
        Call(panel,"RefreshFloatingTools");p=Point(strip,.1f,.4f);Step(p,true,true,false,Vector2.zero);Step(Point(strip,.2f,.4f),false,true,false,Vector2.zero);Step(new Vector2(Screen.width-1,1),false,true,false,Vector2.zero);Step(new Vector2(Screen.width-1,1),false,false,true,Vector2.zero);
        Check(!strip.Dragging&&strip.SelectionStart==strip.SelectionEnd,"Leaving the track while marquee dragging cancels the blue overlay");
        var observations=new List<CameraObservation>{new CameraObservation{Time=0,Position=Vector3.zero,Rotation=Quaternion.identity},new CameraObservation{Time=.2,Position=new Vector3(.01f,0,0),Rotation=Quaternion.Euler(0,1,0)},new CameraObservation{Time=.5,Position=new Vector3(3,0,0),Rotation=Quaternion.identity}};
        var cuts=StudioAutoPartition.Boundaries(observations,1,40,.1,1);Check(cuts.Count==3&&cuts[1]==.5,"Large camera change adds a partition while small jitter does not");
        var cameraKeys=new KoiSyncTimelineTrack{id="cameraPos"};cameraKeys.keyframes.Clear();cameraKeys.keyframes[0]=new KoiSyncTimelineKeyframe{value=new Vector3(0,0,-2),curve=new AnimationCurve(new Keyframe(0,0,0,float.PositiveInfinity),new Keyframe(1,1))};cameraKeys.keyframes[.5f]=new KoiSyncTimelineKeyframe{value=new Vector3(3,0,-2)};Timeline.Timeline._self._interpolables[77]=cameraKeys;TimelineState.Refresh();
        Check(TimelineState.CameraKeyTimes().Contains(.5),"Camera keyframe times are read from Timeline's actual registry schema");
        cam.tag="MainCamera";var savedMain=Camera.main;var studio=FindObjectOfType<Studio.Studio>();
        var realCamera=studio.cameraCtrl.mainCmaera;Vector3 realPosition=realCamera.transform.position;Quaternion realRotation=realCamera.transform.rotation;
        int seekCalls=0;Timeline.Timeline.SeekAction=value=>{seekCalls++;realCamera.transform.position=new Vector3(value<.5?0:3,0,-2);realCamera.transform.rotation=Quaternion.Euler(0,180,0);var data=StudioMembers.Read(studio.cameraCtrl,"cameraData");data.GetType().GetField("pos",F).SetValue(data,realCamera.transform.position);data.GetType().GetField("rotate",F).SetValue(data,new Vector3(0,180,0));data.GetType().GetField("distance",F).SetValue(data,Vector3.zero);};
        TimelineState.Seek(.37);TimelineState.SetPlaying(true);int beforeSeeks=seekCalls;Vector3 beforeCamera=realCamera.transform.position;
        CameraObservation offlineCamera;string cameraReason;Check(TimelineState.CameraAt(.75,realCamera,out offlineCamera,out cameraReason)&&Math.Abs(offlineCamera.Position.x-3)<.001,"Camera pose is interpolated directly from keyframe values");
        var curveKeys=new System.Collections.Generic.SortedList<float,object>{{0,new KoiSyncTimelineKeyframe{value=0f,curve=AnimationCurve.Linear(0,0,1,.5f)}},{1,new KoiSyncTimelineKeyframe{value=10f}}};Check(Math.Abs((float)TimelineState.ValueAt(curveKeys,.5)-2.5f)<.001,"Direct keyframe evaluation honors the left easing curve");
        var savedSegments=host.Profiles.Scene.Segments;
        Call(panel,"OpenAutoPartition");yield return new WaitForEndOfFrame();((InputField)Field(panel,"automaticMinimum")).text="0.1";Press("生成并替换轨道");
        for(int wait=0;wait<100&&panel.LaboratoryBusy;wait++){yield return null;yield return new WaitForEndOfFrame();}
        Check(!panel.LaboratoryBusy,"Automatic analysis finishes with bounded Timeline sampling");
        Check(Math.Abs(TimelineState.Time-.37)<.001&&TimelineState.Playing&&seekCalls==beforeSeeks&&realCamera.transform.position==beforeCamera,"Automatic partition never seeks or mutates the camera or playback state");
        Check(host.Profiles.Scene.Segments.Count==2&&host.Profiles.Scene.Segments.TrueForAll(s=>s.Source==TimelineSegment.NoSource&&s.Name=="无"),"Camera partitions without visible reliable sources are explicitly None");
        Check(!host.Devices.Connected,"Automatic analysis never connects to a device");
        Call(panel,"OpenAutoPartition");yield return new WaitForEndOfFrame();Press("撤销上次自动分区");Check(ReferenceEquals(host.Profiles.Scene.Segments,savedSegments),"Undo restores the exact prior interval objects and configurations");
        Call(panel,"OpenAutoPartition");yield return new WaitForEndOfFrame();Press("生成并替换轨道");Button("取消分析").onClick.Invoke();
        for(int wait=0;wait<10&&panel.LaboratoryBusy;wait++){yield return null;yield return new WaitForEndOfFrame();}
        Check(!panel.LaboratoryBusy&&ReferenceEquals(host.Profiles.Scene.Segments,savedSegments)&&Math.Abs(TimelineState.Time-.37)<.001,"Cancellation preserves existing segments and restores time");
        Timeline.Timeline.SeekAction=null;realCamera.transform.position=realPosition;realCamera.transform.rotation=realRotation;
        host.EnableSync();panel.ToggleWorkspace();Check(workspace.activeInHierarchy&&panel.AlignmentEditing&&timeline.gameObject.activeInHierarchy&&hud.activeInHierarchy,"Showing workspace retains independent tools and HUD");
        yield return null;yield return new WaitForEndOfFrame();var collapsePoint=Point(Button("收起"));var collapseHits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=collapsePoint},collapseHits);report.AppendLine("Collapse hit="+(collapseHits.Count==0?"none":collapseHits[0].gameObject.name)+" point="+collapsePoint);Capture("studio-editor-coexist-v0119.png");Press("收起");Check(!workspace.activeInHierarchy&&panel.AlignmentEditing,"Workspace collapse button closes only the workspace");
        Click(Point(small.GetComponentInChildren<Button>()));Call(panel,"Refresh");
        Check(!panel.AlignmentEditing&&panel.LaboratoryOpen&&timeline.gameObject.activeInHierarchy&&hud.activeInHierarchy,"Alignment close button retains Timeline and HUD");
        strip.Fit();Call(panel,"RefreshFloatingTools");Click(Point(strip,.2f,.4f),2);Check(Math.Abs(TimelineState.Time-TimelineState.Duration*.2)<.025,"Timeline seeking still works with alignment closed");
        Press("3D 对齐");Check(panel.AlignmentEditing,"Timeline can reopen alignment without closing itself");
        Click(Point(timeline.GetComponentInChildren<Button>()));Check(panel.AlignmentEditing&&!timeline.gameObject.activeInHierarchy,"Timeline close button retains alignment");
        Click(Point(hud.GetComponentInChildren<Button>()));Call(panel,"Refresh");Check(!hud.activeInHierarchy&&small.gameObject.activeInHierarchy,"HUD close button stays closed through telemetry refresh");
        Press("轨道");Press("数值监控");Check(hud.activeInHierarchy&&timeline.gameObject.activeInHierarchy,"Timeline can reopen an independently closed HUD");
        var hudRect=(RectTransform)hud.transform;var hudHeader=hud.GetComponentInChildren<PanelDrag>();var hudBefore=hudRect.anchoredPosition;p=Point(hudHeader,.3f,.5f);
        Step(p,true,true,false,Vector2.zero);Step(p+new Vector2(-35,-25),false,true,false,Vector2.zero);Step(p+new Vector2(-35,-25),false,true,false,Vector2.zero);Step(p+new Vector2(-35,-25),false,false,true,Vector2.zero);
        Check((hudRect.anchoredPosition-hudBefore).magnitude>10,"HUD supports pointer dragging independently");
        var hudResize=hud.GetComponentInChildren<WindowResize>();float hudScale=hudRect.localScale.x;p=Point(hudResize);
        Step(p,true,true,false,Vector2.zero);Step(p+new Vector2(20,-20),false,true,false,Vector2.zero);Step(p+new Vector2(35,-30),false,true,false,Vector2.zero);Step(p+new Vector2(35,-30),false,false,true,Vector2.zero);
        Check(hudRect.localScale.x>hudScale,"HUD supports resizing independently");
        panel.ToggleWorkspace();Check(workspace.activeInHierarchy&&hud.activeInHierarchy&&timeline.gameObject.activeInHierarchy&&small.gameObject.activeInHierarchy,"F7 opens workspace without closing independent windows");
        panel.ToggleWorkspace();Check(hud.activeInHierarchy&&timeline.gameObject.activeInHierarchy&&small.gameObject.activeInHierarchy&&!workspace.activeInHierarchy,"F7 closes only workspace and retains independent windows");
        hud.transform.SetAsLastSibling();var fader=hud.GetComponentsInChildren<ChannelFader>()[0];Call(panel,"Refresh");
        Check(hud.GetComponentsInChildren<ChannelFader>().Length==6,"Value window contains six vertical dual-limit controls");
        Check(hud.GetComponentsInChildren<InputField>().Length==6&&hudRect.rect.height<350,"Compact value window retains only six multiplier inputs below its bars");
        Check(!Array.Exists(hud.GetComponentsInChildren<Text>(),label=>label.text.Contains("输入")||label.text.Contains("目标")||label.text.Contains("原始")||label.text.Contains("工作台")),"Compact value window omits redundant telemetry and navigation text");
        p=Point(fader,.8f,.82f);Step(p,true,true,false,Vector2.zero);Step(Point(fader,.8f,.7f),false,true,false,Vector2.zero);Step(Point(fader,.8f,.7f),false,false,true,Vector2.zero);
        Check(host.Global.AxisMaximum[0].Value>.6&&host.Global.AxisMaximum[0].Value<.8,"Dragging the vertical upper handle saves the global axis limit");
        var banks=(IList)Field(panel,"channelBanks");var bank=banks[0];var gains=(InputField[])Field(bank,"Gain");gains[0].text="2";gains[0].onEndEdit.Invoke("2");Check(Math.Abs(host.Global.AxisGain[0].Value-2)<.001,"Typing multiplier 2 stores the global axis gain");
        yield return new WaitForEndOfFrame();Capture("channel-editors-v0123.png");
        yield return null;yield return new WaitForEndOfFrame();
        Click(Point(small.GetComponentInChildren<Button>()));Click(Point(timeline.GetComponentInChildren<Button>()));Check(!panel.LaboratoryOpen&&hud.activeInHierarchy&&host.Sync.Enabled,"HUD alone retains synchronization intent without editor mode");
        Check(!panel.CameraBlockedAt(new Vector2(Screen.width*.5f,Screen.height-15)),"HUD alone permits native camera input outside the window");
        Check(!panel.WorkspaceVisible,"HUD alone leaves scene label visibility available");
        Click(Point(hud.GetComponentInChildren<Button>()));Check(!panel.Visible,"Closing the final window releases UI ownership");
        panel.SetVisible(true);Check(workspace.activeInHierarchy&&!hud.activeInHierarchy,"Hotkey reopens workspace after all windows were closed");
        panel.SetVisible(false);Check(!GameHooks.BlockCamera,"Closing UI releases game camera input");
        Check(!host.Devices.Connected,"Editor validation does not open a hardware connection");Destroy(camObject);
        panel.Cleanup();Destroy(panel);yield return null;
        typeof(KoiSync.Plugin.Plugin).GetField("adapter",F).SetValue(host,new MainGameAdapter());
        panel=host.gameObject.AddComponent<KoiSyncPanel>();panel.Initialize(host);typeof(KoiSync.Plugin.Plugin).GetField("panel",F).SetValue(host,panel);
        host.Profiles.NativeKey="ui / normal animation";host.Profiles.Animation(host.Profiles.NativeKey).Measurement=new MeasurementProfile{AlignmentVersion=1,Target=NativeTarget.VAGINAL};
        panel.SetVisible(true);yield return null;module=EventSystem.current.GetComponent<StandaloneInputModule>();typeof(BaseInputModule).GetField("m_InputOverride",F).SetValue(module,pointer);module.forceModuleActive=true;module.ActivateModule();
        Call(panel,"ShowStudioTools");yield return new WaitForEndOfFrame();
        Check(Field(panel,"timelineWindow")!=null&&((TimelineStrip)Field(panel,"laboratoryTrack")).ReadOnly,"Main animation editor uses a current-clip timeline without Studio segments");
        Check(panel.AlignmentEditing&&!((GameObject)Field(panel,"hudObject")).activeInHierarchy&&!((RectTransform)Field(panel,"timelineWindow")).gameObject.activeInHierarchy,"Main 3D 对齐 opens the body editor only, without the timeline or the value window");
        g=(AlignmentGizmo)Field(panel,"alignmentGizmo");g.Choose(AlignmentPart.Root);xyz=(InputField[])Field(panel,"alignmentValues");xyz[0].text="0.012";xyz[0].onEndEdit.Invoke("0.012");
        Check(Math.Abs(host.Profiles.NativeMeasurement.RootOffset.X-.012)<.00001,"Main floating XYZ editor saves the current animation root coordinate");
        ((InputField)Field(panel,"planeWidth")).text="0.28";((InputField)Field(panel,"planeWidth")).onEndEdit.Invoke("0.28");g.SetObjectVisible(AlignmentPart.End,false);
        var mainBanks=(IList)Field(panel,"channelBanks");var mainBank=mainBanks[0];Call(panel,"Refresh");((InputField[])Field(mainBank,"Gain"))[0].text="2";((InputField[])Field(mainBank,"Gain"))[0].onEndEdit.Invoke("2");
        Check(Math.Abs(host.Global.AxisGain[0].Value-2)<.001,"Main value multiplier is stored in the global axis configuration");
        var savedMainProfile=new PlaybackProfiles(Paths.GameRootPath).Animation("ui / normal animation").Measurement;
        Check(savedMainProfile.HiddenObjects==4&&Math.Abs(savedMainProfile.PlaneWidth-.28)<.00001&&Math.Abs(savedMainProfile.RootOffset.X-.012)<.00001,"Independent animation JSON contains layer visibility, size and coordinates");
        host.Profiles.NativeKey="ui / second animation";host.Profiles.Animation(host.Profiles.NativeKey).Measurement=new MeasurementProfile{AlignmentVersion=1,Target=NativeTarget.LEFTFOOT};Call(panel,"RefreshFloatingTools");
        Check(g.Draft.Target==NativeTarget.LEFTFOOT&&g.Draft.RootOffset.Length==0&&g.Draft.HiddenObjects==0&&g.Draft.Multipliers[0]==1,"Switching main animation restores its own source, coordinates and multiplier");
        host.Profiles.NativeKey="ui / normal animation";Call(panel,"RefreshFloatingTools");Check(g.Draft.HiddenObjects==4&&Math.Abs(g.Draft.PlaneWidth-.28)<.00001,"Returning to an animation restores its independent visibility and size");
        var basis=AlignmentFrame.Build(new MotionVector(),new MotionVector(1,0,0),new MotionVector(0,1,0));
        var mainSample=new NativeMotionSample{RootDistance=true,HasBodyFrame=true,HasTargetFrame=true,UseReferenceUp=true,Origin=new MotionVector(),End=new MotionVector(0,.2,0),Target=new MotionVector(.02,.065,.025),TargetHip=new MotionVector(.04,.15,.08),BodyFrame=basis,ReferenceFrame=basis,TargetBodyFrame=basis,TargetReferenceFrame=basis};
        mainSample.AnchorRoot=mainSample.Origin;mainSample.AnchorEnd=mainSample.End;mainSample.AnchorTarget=mainSample.Target;
        var mainCandidate=new KoiSync.Wire.Candidate{Id="numeric-main-fixture",Active=true,Native=mainSample,ReferenceBone="原生根节点 · 数值夹具",TargetBone="目标根节点 · 数值夹具"};g.Draft.Apply(mainCandidate);g.Preview=host.Selected=mainCandidate;
        host.Frame=new KoiSync.Wire.Snapshot{Session="main-ui",Scene="numeric-fixture",Sequence=1,SampleTime=1,Active=true,State="本体动画配置 · 合成数值验证"};host.Pipeline.Minimum=0;host.Pipeline.Maximum=1;host.Pipeline.Update(host.Frame,mainCandidate,0,StrokeAxis.Z);Call(panel,"Refresh");
        var meter=((GameObject)Field(panel,"hudObject")).GetComponentsInChildren<ChannelFader>()[0];Check(meter.Valid&&meter.Value>.4&&meter.Value<.9,"Main value column displays the calculated nonzero progress and exact target line");
        panel.ToggleWorkspace();Check(!panel.WorkspaceVisible&&panel.AlignmentEditing,"F7 preserves the main animation alignment editor");
        yield return new WaitForEndOfFrame();Capture("main-animation-editor-v0123.png");
        Check(!host.Devices.Connected,"Main animation editor tests do not open devices");
    }
    private void Capture(string name){var texture=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(Paths.GameRootPath,name),texture.EncodeToPNG());Destroy(texture);}
}
