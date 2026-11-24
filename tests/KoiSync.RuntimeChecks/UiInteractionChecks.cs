using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class TestPointer:BaseInput
{
    public Vector2 Position,Wheel;
    public bool Down,Up,Held;
    public int ButtonIndex;
    public override bool mousePresent{get{return true;}}
    public override Vector2 mousePosition{get{return Position;}}
    public override Vector2 mouseScrollDelta{get{return Wheel;}}
    public override bool GetMouseButtonDown(int button){return button==ButtonIndex&&Down;}
    public override bool GetMouseButtonUp(int button){return button==ButtonIndex&&Up;}
    public override bool GetMouseButton(int button){return button==ButtonIndex&&Held;}
    public override float GetAxisRaw(string name){return 0;}
    public override bool GetButtonDown(string name){return false;}
}

[BepInPlugin("local.koisync.uichecks","KoiSync UI interaction checks","0.1.0")]
[BepInDependency("local.koisync")]
public sealed class UiInteractionChecks:BaseUnityPlugin
{
    private const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private readonly StringBuilder report=new StringBuilder();
    private object panel;
    private StandaloneInputModule module;
    private TestPointer pointer;
    private bool baseline;
    private string Output{get{return Path.Combine(Paths.GameRootPath,baseline?"ui-baseline-v016.txt":Paths.ProcessName=="CharaStudio"?"ui-interaction-studio-v017.txt":"ui-interaction-v017.txt");}}
    private object Field(object o,string name){return o.GetType().GetField(name,F).GetValue(o);}
    private object Call(object o,string name,params object[] args){return o.GetType().GetMethod(name,F).Invoke(o,args);}
    private void Check(bool ok,string name){report.AppendLine((ok?"PASS ":"FAIL ")+name);File.WriteAllText(Output,report.ToString());if(!ok&&!baseline)throw new Exception(name);}
    private void Awake(){baseline=Array.IndexOf(Environment.GetCommandLineArgs(),"--koisync-ui-baseline")>=0;if(baseline||Array.IndexOf(Environment.GetCommandLineArgs(),"--koisync-ui-checks")>=0){Application.runInBackground=true;report.AppendLine("Run "+DateTime.Now.ToString("s")+" / "+Paths.ProcessName);File.WriteAllText(Output,report.ToString());StartCoroutine(Run());}}
    private Vector2 Point(Component c,float x=.5f,float y=.5f){RectTransform r=(RectTransform)c.transform;return RectTransformUtility.WorldToScreenPoint(null,r.TransformPoint(new Vector3(Mathf.Lerp(r.rect.xMin,r.rect.xMax,x),Mathf.Lerp(r.rect.yMin,r.rect.yMax,y))));}
    private GameObject Hit(Vector2 point){var data=new PointerEventData(EventSystem.current){position=point};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);return hits.Count==0?null:hits[0].gameObject;}
    private void Step(Vector2 p,bool down,bool held,bool up,Vector2 wheel)
    {
        pointer.Position=p;pointer.Down=down;pointer.Held=held;pointer.Up=up;pointer.Wheel=wheel;module.UpdateModule();module.Process();pointer.Down=pointer.Up=false;pointer.Wheel=Vector2.zero;
    }
    private void Click(Vector2 p){Step(p,true,true,false,Vector2.zero);Step(p,false,false,true,Vector2.zero);}
    private Button FindButton(string text)
    {
        foreach(Button button in ((GameObject)Field(panel,"window")).GetComponentsInChildren<Button>())if(button.GetComponentInChildren<Text>()!=null&&button.GetComponentInChildren<Text>().text==text)return button;
        throw new Exception("Missing visible button: "+text);
    }
    private void Nav(string text){Click(Point(FindButton(text)));Canvas.ForceUpdateCanvases();}
    private void OnePage()
    {
        IDictionary pages=(IDictionary)Field(panel,"pages");int active=0;foreach(DictionaryEntry page in pages)if(((GameObject)page.Value).activeInHierarchy)active++;
        Check(active==1,"Exactly one page is visible: "+Field(panel,"activePage"));
    }
    private void Capture(string name)
    {
        RectTransform win=((GameObject)Field(panel,"window")).GetComponent<RectTransform>();Vector3[] corners=new Vector3[4];win.GetWorldCorners(corners);
        int x=Mathf.RoundToInt(corners[0].x),y=Mathf.RoundToInt(corners[0].y),w=Mathf.RoundToInt(corners[2].x-corners[0].x),h=Mathf.RoundToInt(corners[2].y-corners[0].y);
        Texture2D texture=new Texture2D(w,h,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(x,y,w,h),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(Paths.GameRootPath,name),texture.EncodeToPNG());Destroy(texture);
    }
    private IEnumerator Run()
    {
        yield return new WaitForSecondsRealtime(10);
        var host=UnityEngine.Object.FindObjectOfType<KoiSync.Plugin.Plugin>();
        try{host.StopAllCoroutines();host.enabled=false;host.Sync.Enabled=true;panel=Field(host,"panel");Call(panel,"SetVisible",true);}
        catch(Exception ex){report.AppendLine(ex.ToString());File.WriteAllText(Output,report.ToString());yield break;}
        yield return null;
        IEnumerator checks=Checks(host);
        while(true)
        {
            bool more;object wait;
            try{more=checks.MoveNext();wait=more?checks.Current:null;}
            catch(Exception ex){report.AppendLine("ERROR "+ex);break;}
            if(!more)break;yield return wait;
        }
        File.WriteAllText(Output,report.ToString());
        if(!baseline){yield return new WaitForEndOfFrame();Capture(Paths.ProcessName=="CharaStudio"?"ui-studio-v017.png":"ui-navigation-v017.png");if(Paths.ProcessName!="CharaStudio"){Nav("本体同步");yield return new WaitForEndOfFrame();Capture("ui-native-v017.png");}}
    }
    private IEnumerator Checks(KoiSync.Plugin.Plugin host)
        {
            module=EventSystem.current.GetComponent<StandaloneInputModule>();Check(module!=null,"Panel has a working pointer input module");
            pointer=gameObject.AddComponent<TestPointer>();typeof(BaseInputModule).GetField("m_InputOverride",F).SetValue(module,pointer);module.forceModuleActive=true;module.ActivateModule();
            Toggle toggle=(Toggle)Field(panel,"debug");Vector2 label=Point(toggle,.75f,.5f);
            Check(KoiSync.Plugin.GameHooks.BlockCamera,"Visible workspace blocks game camera input");
            var cameraFixture=new GameObject("Inactive camera input fixture");cameraFixture.SetActive(false);var controlled=cameraFixture.AddComponent<Studio.CameraControl>();
            object blocked=typeof(Studio.CameraControl).GetMethod("InputMouseProc",F).Invoke(controlled,null);
            Check(blocked is bool&&!(bool)blocked,"Patched game camera mouse method returns without rotating or locking the cursor");Destroy(cameraFixture);
            GameObject hit=Hit(label);Check(hit!=null&&ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit)==toggle.gameObject,"Toggle label belongs to its click target");
            bool was=toggle.isOn;Click(label);Check(toggle.isOn!=was,"Pointer click on toggle label changes its value");
            var slider=(KoiSync.Plugin.RangeSlider)Field(panel,"outputRange");slider.SetRange(.1f,.9f);Call(panel,"UpdateRange");Canvas.ForceUpdateCanvases();float old=slider.Lower;Vector2 track=Point(slider,.33f,.85f);Click(track);Check(Math.Abs(slider.Lower-old)>.1f,"Pointer click in range track updates the nearest handle");
            if(!baseline&&Paths.ProcessName=="CharaStudio")
            {
                Nav("工作室绑定");yield return new WaitForEndOfFrame();OnePage();
                Dropdown groups=(Dropdown)Field(panel,"studioSources");Call(panel,"Refresh");Click(Point(groups));yield return new WaitForSecondsRealtime(.2f);
                Transform groupList=groups.transform.Find("Dropdown List");Check(groupList!=null,"Studio binding selector opens through pointer input");
                Toggle groupChoice=null;foreach(Toggle row in groupList.GetComponentsInChildren<Toggle>())if(row.GetComponentInChildren<Text>().text.StartsWith("碰撞体"))groupChoice=row;
                Check(groupChoice!=null,"Discovered collider group is presented as a selectable row");Click(Point(groupChoice));Call(panel,"Refresh");
                Check(host.StudioAdapter.Find(host.SelectedId).IsCollider,"Clicking a binding row selects that exact source group");
                Toggle reverse=(Toggle)Field(panel,"studioReverse");bool reverseBefore=reverse.isOn;Click(Point(reverse,.7f,.5f));Check(host.StudioOptions.ReverseCollider.Value!=reverseBefore,"Collider direction toggle responds across its label");
                Slider gain=((GameObject)Field(panel,"studioPage")).GetComponentsInChildren<Slider>()[0];Click(Point(gain,.7f,.5f));Check(host.StudioOptions.Gain.Value>3,"Studio gain slider updates the bound-source multiplier");
                Dropdown targetProfile=(Dropdown)Field(panel,"studioProfile");Click(Point(targetProfile));yield return new WaitForSecondsRealtime(.2f);Transform profileList=targetProfile.transform.Find("Dropdown List");
                Toggle targetChoice=null;foreach(Toggle row in profileList.GetComponentsInChildren<Toggle>())if(row.GetComponentInChildren<Text>().text=="左手")targetChoice=row;
                Click(Point(targetChoice));Check(host.StudioOptions.Target.Value==KoiSync.Core.NativeTarget.LEFTHAND,"Studio target override is selectable through pointer input");
                host.StudioOptions.Target.Value=KoiSync.Core.NativeTarget.AUTO;host.StudioOptions.Gain.Value=1;host.StudioOptions.ReverseCollider.Value=false;
                string selectedGroup=host.SelectedId;Nav("六轴监控");Nav("工作室绑定");yield return new WaitForEndOfFrame();Call(panel,"Refresh");
                Check(host.SelectedId==selectedGroup,"Leaving and returning to the binding page preserves the selected group");
                Check(targetProfile.value==0&&!reverse.isOn,"Studio controls refresh from the saved configuration without triggering edits");
                host.Frame.Candidates.Clear();host.StudioAdapter.Sample(host.Frame,host.StudioOptions);host.Selected=host.Frame.Candidates.Find(c=>c.Id==host.SelectedId);
                host.Frame.Sequence++;host.Frame.SampleTime+=.02;host.Pipeline.Update(host.Frame,host.Selected,0,KoiSync.Core.StrokeAxis.Z);Call(panel,"Refresh");
                Check(host.Selected!=null&&host.Pipeline.HasLiveSample,"Selected collider group remains available while interacting with the workspace");
                yield return new WaitForEndOfFrame();
                Toggle autoSource=(Toggle)Field(panel,"studioAuto");bool autoBefore=host.StudioOptions.AutoSource.Value;
                Click(Point(autoSource,.7f,.5f));Check(host.StudioOptions.AutoSource.Value!=autoBefore,"Studio camera-follow toggle responds through real pointer input");
                if(!host.StudioOptions.AutoSource.Value)Click(Point(autoSource,.7f,.5f));Call(panel,"Refresh");
                Check(autoSource.isOn,"Studio auto-selection toggle displays enabled camera following");
                Capture("ui-studio-bindings-v019.png");
                host.Experiment.Update(host.StudioAdapter.Find(host.SelectedId),host.Selected,1000);
                Nav("工作室实验");yield return new WaitForEndOfFrame();Call(panel,"Refresh");OnePage();
                Dropdown shapeDropdown=(Dropdown)Field(panel,"experimentShapes");Click(Point(shapeDropdown));yield return new WaitForSecondsRealtime(.2f);
                Transform shapeList=shapeDropdown.transform.Find("Dropdown List");Check(shapeList!=null,"Experiment shape dropdown opens through pointer input");
                Toggle shapeChoice=null;foreach(Toggle row in shapeList.GetComponentsInChildren<Toggle>())if(row.GetComponentInChildren<Text>().text.Contains("test.penetrate")){shapeChoice=row;break;}
                Check(shapeChoice!=null,"Experiment offers the real fixture blendshape by name");Click(Point(shapeChoice));
                host.Experiment.Update(host.StudioAdapter.Find(host.SelectedId),host.Selected,1000.1f);Call(panel,"Refresh");
                Check(host.Experiment.Selected!=null,"Pointer selects an exact renderer and shape");
                InputField weightFull=(InputField)Field(panel,"experimentFull");Click(Point(weightFull));yield return null;
                Check(EventSystem.current.currentSelectedGameObject==weightFull.gameObject,"Experiment endpoint input receives pointer focus");
                weightFull.ProcessEvent(Event.KeyboardEvent("^a"));weightFull.ProcessEvent(new Event{type=EventType.KeyDown,character='5'});weightFull.ProcessEvent(new Event{type=EventType.KeyDown,character='0'});Nav("应用端点");
                Check(host.Experiment.Full.Value==50,"Typed experiment endpoint is applied through its button");
                host.Experiment.Update(host.StudioAdapter.Find(host.SelectedId),host.Selected,1000.2f);Call(panel,"Refresh");
                Toggle preview=(Toggle)Field(panel,"experimentPreview");Click(Point(preview,.6f,.5f));Check(host.Experiment.AbsolutePreview,"Preview toggle responds across its label");
                host.Experiment.Update(host.StudioAdapter.Find(host.SelectedId),host.Selected,1000.3f);host.Frame.Sequence++;host.Frame.SampleTime+=.02;host.Pipeline.Update(host.Frame,host.Selected,0,KoiSync.Core.StrokeAxis.Z);Call(panel,"Refresh");
                Check(host.Pipeline.ShapePreview&&!host.Pipeline.Arm(),"UI absolute preview remains unable to arm devices");
                Nav("六轴监控");Call(panel,"Refresh");Check(((Text)Field(panel,"monitorSource")).text.Contains("形态键绝对"),"Six-axis monitor identifies experimental input and skeletal raw values");
                Nav("工作室实验");Call(panel,"Refresh");Check(host.Experiment.Selected!=null&&preview.isOn,"Experiment selection and mode survive page navigation");
                Canvas.ForceUpdateCanvases();yield return new WaitForSecondsRealtime(.25f);yield return new WaitForEndOfFrame();Capture("ui-studio-experiment-v0110.png");
                Nav("显示全部");Call(panel,"Refresh");Check(shapeDropdown.options.Count==host.Experiment.Shapes.Count+1,"Clear filter exposes all shapes on the bound objects");
                Click(Point(shapeDropdown));yield return new WaitForSecondsRealtime(.2f);Nav("工作室绑定");Check(shapeDropdown.transform.Find("Dropdown List")==null,"Leaving experiment closes its popup without a stray click blocker");
                host.Experiment.AbsolutePreview=false;host.Experiment.Full.Value=100;
                Nav("3D 显示");yield return new WaitForEndOfFrame();OnePage();
                Toggle display=((GameObject)Field(panel,"geometryPage")).GetComponentsInChildren<Toggle>()[0];bool previous=display.isOn;Click(Point(display,.7f,.5f));Check(display.isOn!=previous,"Studio display toggle responds to its label");
                Nav("姿态检查");yield return new WaitForEndOfFrame();OnePage();Nav("六轴详细值");OnePage();
                Nav("同步工作台");yield return new WaitForEndOfFrame();Dropdown devices=(Dropdown)Field(panel,"device");Click(Point(devices));yield return new WaitForSecondsRealtime(.2f);
                Transform popup=devices.transform.Find("Dropdown List");Check(popup!=null,"Studio device dropdown opens through pointer input");
                Toggle option=null;foreach(Toggle row in popup.GetComponentsInChildren<Toggle>())if(row.GetComponentInChildren<Text>().text=="Intiface")option=row;
                Click(Point(option));Check(devices.value==1&&((Dropdown)Field(panel,"transport")).value==2,"Protocol dropdown selects Intiface and its transport together");Nav("关于 / 许可");yield return new WaitForEndOfFrame();OnePage();
                ScrollRect scroll=((GameObject)Field(panel,"licensePage")).GetComponentInChildren<ScrollRect>();float before=scroll.content.anchoredPosition.y;Step(Point(scroll),false,false,false,new Vector2(0,-4));Check(Math.Abs(scroll.content.anchoredPosition.y-before)>.1f,"Studio license page scrolls through pointer module");
                Nav("同步工作台");yield return new WaitForEndOfFrame();Check(devices.transform.Find("Dropdown List")==null,"Studio navigation removes obsolete dropdown list");
                Nav("六轴监控");OnePage();Call(panel,"SetVisible",false);Check(!((GameObject)Field(panel,"eventObject")).activeSelf,"Studio hide releases input ownership");Call(panel,"SetVisible",true);OnePage();
                Check(!(bool)Field(Field(host,"Devices"),"Connected"),"Studio checks keep devices disconnected");
                host.Profiles.SceneSaved(Path.Combine(Paths.GameRootPath,"neutral-ui-timeline.png"));host.Profiles.Scene=new KoiSync.Core.SceneProfile();host.Profiles.Revision++;
                yield return null;yield return new WaitForEndOfFrame();
                Nav("分段 / 对齐");yield return new WaitForEndOfFrame();Call(panel,"Refresh");OnePage();
                Nav("建立全长段");Check(host.Profiles.Scene.Segments.Count==1,"Timeline page creates a full-duration segment");
                var strip=(KoiSync.Plugin.TimelineStrip)Field(panel,"playbackStrip");Call(panel,"Refresh");Click(Point(strip,.4f,.5f));
                Check(Math.Abs(TimelineStateTime()-.4)<.015,"Clicking segmented seek bar changes Timeline playback time");
                Nav("播放头切段");Check(host.Profiles.Scene.Segments.Count==2,"Timeline page splits at the selected playback time");
                Call(panel,"Refresh");InputField length=(InputField)Field(panel,"profileLength");Click(Point(length));
                Check(EventSystem.current.currentSelectedGameObject==length.gameObject,"Profile length field accepts pointer focus");length.text="1.35";
                var offsets=(InputField[,])Field(panel,"profileOffsets");offsets[1,0].text="0.006";offsets[2,1].text="0.002";
                ((Toggle)Field(panel,"profileFixed")).isOn=true;((InputField)Field(panel,"profileLow")).text="200";((InputField)Field(panel,"profileHigh")).text="800";
                Nav("应用并保存");var edited=host.Profiles.Scene.At(TimelineStateTime());
                Check(edited!=null&&Math.Abs(edited.Measurement.LengthScale-1.35)<.001&&Math.Abs(edited.Measurement.TargetOffset.X-.006)<.0001,"Applying segment editor stores effective length and XYZ offsets");
                Check(edited.Measurement.FixedRange&&Math.Abs(edited.Measurement.RangeStart-200.0/999)<.00001,"Editor saves explicit insertion range in player scale");
                Check(File.Exists(host.Profiles.SceneFile),"Timeline UI saves the scene sidecar file");
                var original=host.Profiles.Scene.Segments[0];Check(original.Measurement.LengthScale!=edited.Measurement.LengthScale,"Editing a split segment leaves its neighbour independent");
                host.EnableSync();Call(panel,"Refresh");Check(((Button)Field(panel,"arm")).GetComponentInChildren<Text>().text=="同步已启用","GUI reports persistent synchronization even without a connected device");
                yield return new WaitForEndOfFrame();Capture("ui-timeline-profiles-v0115.png");
                Call(panel,"SetVisible",false);Check(!KoiSync.Plugin.GameHooks.BlockCamera,"Closing workspace restores game camera input");Call(panel,"SetVisible",true);yield break;
            }
            Call(panel,"ShowAxisMonitor");Call(panel,"ShowNativeSettings");Call(panel,"ShowAxisMonitor");
            GameObject monitor=(GameObject)Field(panel,"monitorPage"),native=(GameObject)Field(panel,"nativePage");
            Check(monitor.activeInHierarchy&&!native.activeInHierarchy,"Navigating back activates one page and hides the other");
            if(!baseline)
            {
                OnePage();Nav("同步工作台");yield return new WaitForEndOfFrame();Nav("本体自动来源 · 配置 ↗");OnePage();yield return new WaitForEndOfFrame();
                foreach(Button b in native.GetComponentsInChildren<Button>())Check(b.GetComponentInChildren<Text>().text!="×","Content page has no separate close button");
                Dropdown profile=(Dropdown)Field(panel,"nativeProfile");
                Click(Point(profile));yield return new WaitForSecondsRealtime(.2f);Canvas.ForceUpdateCanvases();Transform list=profile.transform.Find("Dropdown List");
                Check(list!=null,"Pointer click opens the native dropdown");
                Canvas popup=list.GetComponent<Canvas>();Canvas workspace=((GameObject)Field(panel,"canvasObject")).GetComponent<Canvas>();
                Check(popup.sortingOrder>workspace.sortingOrder,"Dropdown popup sorts above the workspace");
                Toggle choice=null;foreach(Toggle row in list.GetComponentsInChildren<Toggle>())if(row.GetComponentInChildren<Text>().text=="VAGINAL")choice=row;
                Check(choice!=null,"Dropdown creates selectable option rows");Click(Point(choice));Check(profile.value==1,"Pointer selects a dropdown option");
                // Hide's fade list is disabled when navigating; it must not capture another page's input.
                Nav("六轴监控");OnePage();yield return new WaitForEndOfFrame();Nav("本体同步");OnePage();yield return new WaitForEndOfFrame();Check(profile.value==1,"Returning to a page preserves its selection");
                Click(Point(profile));yield return new WaitForSecondsRealtime(.2f);Check(profile.transform.Find("Dropdown List")!=null,"Dropdown can reopen after its page was hidden");
                Click(Point(native.transform,.96f,.92f));yield return new WaitForSecondsRealtime(.2f);Check(profile.transform.Find("Dropdown List")==null,"Clicking outside dropdown dismisses its list");
                InputField field=((InputField[])Field(panel,"nativeLow"))[0];Click(Point(field));
                yield return null;
                Check(EventSystem.current.currentSelectedGameObject==field.gameObject,"Pointer focuses a numeric input");
                field.ProcessEvent(Event.KeyboardEvent("^a"));field.ProcessEvent(new Event{type=EventType.KeyDown,character='2'});field.ProcessEvent(new Event{type=EventType.KeyDown,character='0'});field.ProcessEvent(new Event{type=EventType.KeyDown,character='0'});
                Check(field.text=="200","Focused input processes player-scale numeric characters");Nav("应用六轴限幅");
                object nativeSettings=Field(host,"Native");Array lows=(Array)Field(nativeSettings,"Minimum");object config=lows.GetValue(0);
                Check(Math.Abs((float)config.GetType().GetProperty("Value").GetValue(config,null)-200f/999)<.0001,"Clicking Apply converts player-scale limits to precise internal values");
                var channelRange=((KoiSync.Plugin.RangeSlider[])Field(panel,"nativeRanges"))[0];float beforeLow=channelRange.Lower;
                Click(Point(channelRange,.8f,.5f));Check(channelRange.Upper<.85f&&Math.Abs(channelRange.Lower-beforeLow)<.001f,"Native range upper handle changes independently of its lower handle");
                Nav("同步工作台");OnePage();yield return new WaitForEndOfFrame();
                var range=(KoiSync.Plugin.RangeSlider)Field(panel,"outputRange");Vector2 start=Point(range,.35f,.5f),end=Point(range,.7f,.5f);
                Step(start,true,true,false,Vector2.zero);Step(end,false,true,false,Vector2.zero);Step(end,false,false,true,Vector2.zero);Check(range.Lower>.6f,"Dragging lower range handle changes the lower limit");
                start=Point(range.LowHandle);end=Point(range,.99f,.5f);Step(start,true,true,false,Vector2.zero);Step(end,false,true,false,Vector2.zero);Step(end,false,false,true,Vector2.zero);
                Check(Math.Abs(range.Lower-range.Upper)<.001f,"Range handles cannot cross and produce an invalid interval");range.SetRange(.1f,.9f);Call(panel,"UpdateRange");
                Dropdown devices=(Dropdown)Field(panel,"device");Click(Point(devices));yield return new WaitForSecondsRealtime(.2f);Canvas.ForceUpdateCanvases();
                Nav("本体同步");OnePage();Check(((GameObject)Field(panel,"nativePage")).activeInHierarchy,"Sidebar navigation works while a dropdown is open");
                Nav("3D 显示");OnePage();yield return new WaitForEndOfFrame();GameObject geometry=(GameObject)Field(panel,"geometryPage");
                Toggle axes=geometry.GetComponentsInChildren<Toggle>()[0];bool before=axes.isOn;Click(Point(axes,.75f,.5f));Check(axes.isOn!=before,"3D setting toggle responds across its label");
                Nav("姿态检查");OnePage();yield return new WaitForEndOfFrame();Nav("六轴详细值");OnePage();Nav("关于 / 许可");OnePage();yield return new WaitForEndOfFrame();
                GameObject licenses=(GameObject)Field(panel,"licensePage");ScrollRect scroll=licenses.GetComponentInChildren<ScrollRect>();float scrollBefore=scroll.content.anchoredPosition.y;
                Step(Point(scroll),false,false,false,new Vector2(0,-4));Check(Math.Abs(scroll.content.anchoredPosition.y-scrollBefore)>.1f,"Mouse wheel scrolls the license page");
                Nav("同步工作台");yield return new WaitForEndOfFrame();InputField argument=(InputField)Field(panel,"argument");argument.text="invalid";
                GameObject connectHit=Hit(Point(FindButton("连接设备")));report.AppendLine("Connect hit="+(connectHit==null?"null":connectHit.name)+" activePage="+Field(panel,"activePage"));
                Check(connectHit!=null&&ExecuteEvents.GetEventHandler<IPointerClickHandler>(connectHit)==FindButton("连接设备").gameObject,"Closed dropdown cannot intercept the Connect button");
                Nav("连接设备");Call(panel,"Refresh");Call(panel,"Refresh");report.AppendLine("Connect feedback="+((Text)Field(panel,"connection")).text+" error="+Field(panel,"connectionError")+" argument="+argument.text+" demo="+Field(host,"Demo"));
                Check(((Text)Field(panel,"connection")).text.Contains("连接参数必须"),"Connection validation message survives subsequent refreshes");
                Check(host.Sync.Enabled&&!((Button)Field(panel,"arm")).interactable&&((Button)Field(panel,"arm")).GetComponentInChildren<Text>().text=="同步已启用","Session stays enabled while waiting for a valid connection");
                var previousHub=host.Devices;var pending=new PendingTransport();var release=new System.Threading.ManualResetEvent(false);
                host.Devices=new KoiSync.Plugin.DeviceHub(s=>{pending.Entered=true;release.WaitOne();return pending;},null,3);
                try
                {
                    argument.text="115200";Nav("连接设备");Call(panel,"Refresh");
                    Check(host.Devices.Connecting&&FindButton("取消连接")!=null,"Connect button changes into Cancel while opening");
                    Check(!((Button)Field(panel,"arm")).interactable,"Pending connection cannot enable synchronization");
                    float deadline=Time.realtimeSinceStartup+2;while(!pending.Entered&&Time.realtimeSinceStartup<deadline)yield return null;
                    Check(pending.Entered,"Fixture connection reaches the background opening call");
                    Nav("取消连接");Call(panel,"Refresh");
                    Check(!host.Devices.Connecting&&!host.Devices.Connected&&((Text)Field(panel,"connection")).text.Contains("已取消连接"),"Pointer click cancels a pending connection and updates its status");
                    Check(FindButton("连接设备")!=null,"Cancel restores the Connect action for retry");
                    release.Set();deadline=Time.realtimeSinceStartup+2;while(!pending.Disposed&&Time.realtimeSinceStartup<deadline)yield return null;
                    Check(pending.Disposed&&pending.Commands==0&&!host.Devices.Connected,"Late connection result is released without commands or UI resurrection");
                }
                finally{release.Set();host.Devices.Dispose();host.Devices=previousHub;release.Close();Call(panel,"Refresh");}
                Nav("六轴监控");object pageBefore=Field(panel,"activePage");Call(panel,"SetVisible",false);Check(!((GameObject)Field(panel,"eventObject")).activeSelf,"Hiding UI releases the owned input module");
                var foreign=new GameObject("UI test foreign event system");EventSystem external=foreign.AddComponent<EventSystem>();EventSystem.current=external;
                Call(panel,"SetVisible",true);Check(EventSystem.current.gameObject==(GameObject)Field(panel,"eventObject"),"Existing EventSystem without an input module cannot disable workspace input");
                Check(Field(panel,"activePage").Equals(pageBefore),"Hiding and reopening preserves the active page");
                Call(panel,"SetVisible",false);Check(EventSystem.current==external&&external.enabled,"Closing restores the previous game EventSystem");Call(panel,"SetVisible",true);
                OnePage();
                Check(!(bool)Field(Field(host,"Devices"),"Connected"),"All UI interaction checks keep devices disconnected");
                host.Profiles.NativeKey="fixture / editable native animation";host.Profiles.Animation(host.Profiles.NativeKey);yield return null;yield return new WaitForEndOfFrame();
                Nav("动画 / 对齐");yield return new WaitForEndOfFrame();Call(panel,"Refresh");OnePage();
                ((InputField)Field(panel,"profileLength")).text="1.7";((InputField[,])Field(panel,"profileOffsets"))[0,2].text="0.004";
                var targetDropdown=(Dropdown)Field(panel,"playbackTarget");var targetOptions=(KoiSync.Core.NativeTarget[])Field(panel,"playbackTargets");targetDropdown.value=Array.IndexOf(targetOptions,KoiSync.Core.NativeTarget.ORAL);
                Nav("应用并保存");Check(host.Profiles.NativeMeasurement.Target==KoiSync.Core.NativeTarget.ORAL&&Math.Abs(host.Profiles.NativeMeasurement.LengthScale-1.7)<.001,"Native profile editor saves source type and effective length for its animation");
                Check(File.Exists(host.Profiles.NativeFile)&&Math.Abs(host.Profiles.NativeMeasurement.PlaneOffset.Z-.004)<.0001,"Native profile editor saves local plane offset to the animation file");
                host.Profiles.NativeKey="fixture / second editable animation";host.Profiles.Animation(host.Profiles.NativeKey);Call(panel,"Refresh");
                Check(((InputField)Field(panel,"profileLength")).text=="1","Animation change loads that animation's independent alignment fields");
                host.Profiles.NativeKey="fixture / editable native animation";Call(panel,"Refresh");
                Check(((InputField)Field(panel,"profileLength")).text=="1.7","Returning to an animation restores its saved editor values");
                yield return new WaitForEndOfFrame();Capture("ui-native-profiles-v0115.png");
            }
        }
    private sealed class PendingTransport:KoiSync.Plugin.ITransport
    {
        public volatile bool Entered,Disposed;
        public int Commands;
        public void Send(string command,double value){Commands++;}
        public void Stop(){Commands++;}
        public void Tick(){}
        public void Dispose(){Disposed=true;}
    }
    private static double TimelineStateTime(){return KoiSync.Plugin.TimelineState.Time;}
}
