using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using KoiSync.Plugin;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[BepInPlugin("local.koisync.vrchecks","KoiSync VR panel checks","0.1.0")]
[BepInDependency("local.koisync")]
public sealed class VrPanelChecks:BaseUnityPlugin
{
    private const BindingFlags F=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance;
    private readonly StringBuilder report=new StringBuilder();
    private KoiSyncPanel panel;
    private VrFrame frame;
    private StandaloneInputModule module;
    private GameObject rig,aim;
    private int checks;
    private static object Read(object o,string n){return o.GetType().GetField(n,F).GetValue(o);}
    private static object Call(object o,string n,params object[] args){return o.GetType().GetMethod(n,F).Invoke(o,args);}
    private void Check(bool value,string text){if(!value)throw new Exception(text);checks++;report.AppendLine("PASS "+text);}
    private void Awake(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"--koisync-vr-checks")>=0)StartCoroutine(Run());}
    private Button Button(string label){foreach(Button b in ((GameObject)Read(panel,"canvasObject")).GetComponentsInChildren<Button>())if(b.GetComponentInChildren<Text>().text==label)return b;throw new Exception("Missing button "+label);}
    private void PointAt(Component component)
    {
        RectTransform r=component.transform as RectTransform;Vector3 point=r==null?component.transform.position:r.TransformPoint(r.rect.center);
        aim.transform.rotation=Quaternion.LookRotation(point-aim.transform.position);frame.Aim=aim.transform;
    }
    private void Step(bool pressed){frame.Trigger=pressed;panel.UpdateVr(frame,false);module.UpdateModule();module.Process();}
    private void PointAtFraction(RectTransform rect,float fraction)
    {Vector3 target=rect.TransformPoint(new Vector3(rect.rect.xMin+8+(rect.rect.width-16)*fraction,rect.rect.center.y));aim.transform.rotation=Quaternion.LookRotation(target-aim.transform.position);}
    private void Click(Component component){PointAt(component);Step(false);Step(true);Step(false);Canvas.ForceUpdateCanvases();}
    private IEnumerator Run()
    {
        IEnumerator routine=Checks();
        while(true)
        {
            bool more;object wait;
            try{more=routine.MoveNext();wait=more?routine.Current:null;}
            catch(Exception ex){report.AppendLine("FAIL "+ex);File.WriteAllText(Path.Combine(Paths.GameRootPath,"vr-panel-checks.txt"),report.ToString());Application.Quit();yield break;}
            if(!more)yield break;yield return wait;
        }
    }
    private IEnumerator Checks()
    {
        Application.runInBackground=true;yield return new WaitForSecondsRealtime(10);
        {
            var host=UnityEngine.Object.FindObjectOfType<KoiSync.Plugin.Plugin>();host.StopAllCoroutines();host.enabled=false;panel=(KoiSyncPanel)Read(host,"panel");panel.SetVisible(true);
            Check(!host.Devices.Connected&&!host.Pipeline.Armed,"VR panel tests never connect or arm hardware");
            Check(host.VrOptions.ToggleKey.Value.MainKey==KeyCode.F7&&host.VrOptions.ToggleKey.Value.Modifiers.Count()==1,"Default VR toggle is Ctrl + F7");
            Check(host.VrOptions.RecenterKey.Value.Modifiers.Count()==2,"Recenter has its own Ctrl + Shift + F7 chord");
            rig=new GameObject("Synthetic headset camera");DontDestroyOnLoad(rig);Camera camera=rig.AddComponent<Camera>();camera.nearClipPlane=.01f;camera.farClipPlane=30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.02f,.035f,.05f);camera.cullingMask=1;
            aim=new GameObject("Synthetic controller ray");DontDestroyOnLoad(aim);
            var trackA=aim.AddComponent<OfficialTrackedFixture>();var trackB=rig.AddComponent<OfficialTrackedFixture>();
            var deviceA=new OfficialDeviceFixture{index=1};var deviceB=new OfficialDeviceFixture{index=2};
            var controllerA=new OfficialControllerFixture{trackedObj=trackA,device=deviceA};var controllerB=new OfficialControllerFixture{trackedObj=trackB,device=deviceB};
            var official=new OfficialCameraFixture{scrCamera=new OfficialSteamCameraFixture{camera=camera,head=rig.transform},scrControllerManager=new OfficialControllerManagerFixture{lstController=new ArrayList{controllerA,controllerB}}};
            VrFrame observed=VrRuntime.ReadOfficial(official,VrControllerShortcut.BothGrip,.02f);
            Check(observed.Active&&observed.Camera==camera&&observed.Head==rig.transform,"Official VR camera schema resolves the headset without VRGIN");
            deviceA.Mask=deviceB.Mask=4UL|(1UL<<33);observed=VrRuntime.ReadOfficial(official,VrControllerShortcut.BothGrip,.02f);
            Check(observed.Chord&&!observed.Trigger,"Official VR detects both side grips even with both upper triggers held");
            trackB.isValid=false;Check(!VrRuntime.ReadOfficial(official,VrControllerShortcut.BothGrip,.02f).Chord,"Official VR tracking loss cancels the chord");trackB.isValid=true;
            deviceB.index=1;Check(!VrRuntime.ReadOfficial(official,VrControllerShortcut.BothGrip,.02f).Chord,"Two wrappers for the same device cannot trigger a two-controller chord");deviceB.index=2;
            deviceB.connected=false;Check(!VrRuntime.ReadOfficial(official,VrControllerShortcut.BothGrip,.02f).Chord,"Disconnected official controller cannot trigger the shortcut");deviceB.connected=true;
            deviceA.Mask=deviceB.Mask=2;Check(VrRuntime.ReadOfficial(official,VrControllerShortcut.BothMenu,.02f).Chord,"Official VR alternate double-menu shortcut is read from native devices");
            deviceA.Mask=0;deviceB.Mask=1UL<<33;observed=VrRuntime.ReadOfficial(official,VrControllerShortcut.BothGrip,.02f);
            Check(observed.Trigger&&observed.Aim==trackB.transform,"Official VR uses tracked device pose and trigger for UI input");
            deviceA.hasTracking=deviceB.hasTracking=false;observed=VrRuntime.ReadOfficial(official,VrControllerShortcut.BothGrip,.02f);
            Check(observed.Active&&!observed.Chord&&observed.Aim==rig.transform,"Official headset remains usable with head pointer when controllers are unavailable");
            var actorRoot=new GameObject("Inactive official scene actor fixtures");actorRoot.SetActive(false);var actorA=new GameObject("Source actor");actorA.transform.SetParent(actorRoot.transform);var actorB=new GameObject("Target actor");actorB.transform.SetParent(actorRoot.transform);
            var sceneFixture=new OfficialSceneFixture{male=actorA.AddComponent<ChaControl>(),lstFemale=new List<ChaControl>{actorB.AddComponent<ChaControl>()}};
            ChaControl reference,target;
            Check(MainGameAdapter.TryActors(sceneFixture,null,true,0,out reference,out target)&&reference==sceneFixture.male&&target==sceneFixture.lstFemale[0],"Official scene resolves its own actor list without HFlag player metadata");
            Check(!MainGameAdapter.TryActors(sceneFixture,null,true,1,out reference,out target),"Missing official scene actor does not fall back to a different pair");
            Destroy(actorRoot);
            frame=new VrFrame{Camera=camera,Head=rig.transform,Aim=aim.transform,Status="Synthetic VR frame · no headset connected"};
            panel.UpdateVr(frame,true);Canvas.ForceUpdateCanvases();
            Canvas canvas=((GameObject)Read(panel,"canvasObject")).GetComponent<Canvas>();
            Check(canvas.renderMode==RenderMode.WorldSpace&&canvas.worldCamera==camera,"VR panel uses the supplied headset camera and world-space canvas");
            Check(Math.Abs(Vector3.Distance(canvas.transform.position,rig.transform.position)-host.VrOptions.Distance.Value)<.0001,"Opening places the panel at configured distance");
            Check(Math.Abs(canvas.transform.lossyScale.x*1200-host.VrOptions.Width.Value)<.0001,"VR workspace has configured physical width");
            module=((GameObject)Read(panel,"eventObject")).GetComponent<StandaloneInputModule>();module.forceModuleActive=true;module.ActivateModule();
            Check(Read(module,"m_InputOverride") is VrPanelInput,"VR attaches input only to KoiSync's owned input module");
            yield return null;yield return new WaitForEndOfFrame();
            PointAt(Button("VR / 快捷键"));Step(false);
            var vrPointer=(VrPanelInput)Read(panel,"vrInput");report.AppendLine("Ray hit="+vrPointer.Hit+" screen="+vrPointer.Position+" world="+vrPointer.HitPoint);
            var hits=new System.Collections.Generic.List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=vrPointer.Position},hits);foreach(var hit in hits)report.AppendLine("Hit "+hit.gameObject.name+" depth="+hit.depth+" module="+hit.module);
            Click(Button("VR / 快捷键"));Check(Read(panel,"vrPage")!=null&&((GameObject)Read(panel,"vrPage")).activeInHierarchy,"Controller ray and trigger navigate to VR settings");
            yield return null;yield return new WaitForEndOfFrame();
            var page=(GameObject)Read(panel,"vrPage");Dropdown choices=page.GetComponentInChildren<Dropdown>();Click(choices);Canvas.ForceUpdateCanvases();
            yield return null;yield return new WaitForEndOfFrame();
            Transform popup=choices.transform.Find("Dropdown List");Check(popup!=null,"World-space VR dropdown opens by ray click");
            Toggle choice=null;foreach(Toggle row in popup.GetComponentsInChildren<Toggle>())if(row.GetComponentInChildren<Text>().text=="仅使用键盘组合键")choice=row;
            Click(choice);Check(host.VrOptions.ControllerShortcut.Value==VrControllerShortcut.Disabled,"World-space dropdown options are selectable");host.VrOptions.ControllerShortcut.Value=VrControllerShortcut.BothGrip;
            Click(Button("同步工作台"));Click(Button("VR / 快捷键"));Check(choices.transform.Find("Dropdown List")==null,"VR navigation clears old popup ray blockers");
            if(host.StudioAdapter!=null)
            {
                host.Profiles.Scene=new KoiSync.Core.SceneProfile();host.Profiles.Revision++;TimelineState.Refresh();
                Click(Button("工作室实验"));yield return null;yield return new WaitForEndOfFrame();Click(Button("3D 对齐 / 时间轨道"));yield return null;yield return new WaitForEndOfFrame();
                Check(panel.AlignmentEditing&&((RectTransform)Read(panel,"alignmentWindow")).gameObject.activeInHierarchy,"VR ray opens laboratory floating alignment tools");
                Check(((GameObject)Read(panel,"hudObject")).activeInHierarchy&&((GameObject)Read(panel,"window")).activeInHierarchy,"VR HUD and workspace coexist with the floating tools on one world canvas");
                Click(Button("建立全长段"));Call(panel,"Refresh");Canvas.ForceUpdateCanvases();
                var strip=(TimelineStrip)Read(panel,"laboratoryTrack");Call(panel,"RefreshFloatingTools");
                var ruler=new GameObject("VR ruler target",typeof(RectTransform));ruler.transform.SetParent(strip.transform,false);var rulerRect=(RectTransform)ruler.transform;rulerRect.anchoredPosition=new Vector2(0,strip.rectTransform.rect.height*.38f);rulerRect.sizeDelta=new Vector2(5,5);Click(rulerRect);Destroy(ruler);
                Check(Math.Abs(TimelineState.Time-.5)<.02,"VR ray seeks Timeline by pointing at the ruler");
                Click(strip);yield return new WaitForSecondsRealtime(.1f);Click(strip);yield return new WaitForEndOfFrame();
                var trackMenu=(GameObject)Read(panel,"trackPopup");Check(trackMenu!=null,"VR double trigger opens the same context menu without a mouse");
                foreach(var b in trackMenu.GetComponentsInChildren<Button>()){var label=b.GetComponentInChildren<Text>();if(label!=null&&label.text.StartsWith("切割 ·")){Click(b);break;}}
                Check(host.Profiles.Scene.Segments.Count==2,"VR context menu splits an independent block");
                Click(Button("显示工作台"));yield return null;yield return new WaitForEndOfFrame();
                Check(panel.AlignmentEditing,"Showing VR workspace does not close alignment");
                Call(panel,"CloseWorkspace");
                Click(((RectTransform)Read(panel,"alignmentWindow")).GetComponentInChildren<Button>());
                Check(!panel.AlignmentEditing&&panel.LaboratoryOpen,"VR ray independently closes alignment while Timeline remains usable");
                Click(((RectTransform)Read(panel,"timelineWindow")).GetComponentInChildren<Button>());
                Click(((GameObject)Read(panel,"hudObject")).GetComponentInChildren<Button>());
                Check(!panel.Visible,"VR closes the last floating window without a mouse");
                panel.SetVisible(true);yield return null;yield return new WaitForEndOfFrame();
            }
            Click(Button("同步工作台"));yield return null;yield return new WaitForEndOfFrame();
            var range=(RangeSlider)Read(panel,"outputRange");range.SetRange(.1f,.9f);Canvas.ForceUpdateCanvases();
            PointAt(range.LowHandle);Step(false);Step(true);PointAtFraction((RectTransform)range.transform,.3f);Step(true);Step(false);
            Check(Math.Abs(range.Lower-.3f)<.02f&&Math.Abs(range.Upper-.9f)<.001f,"VR ray drags only the lower handle of the dual range");
            PointAt(range.HighHandle);Step(false);Step(true);PointAtFraction((RectTransform)range.transform,.7f);Step(true);Step(false);
            Check(Math.Abs(range.Upper-.7f)<.02f&&Math.Abs(range.Lower-.3f)<.02f,"VR ray independently drags the upper handle");
            PointAt(range.HighHandle);Step(false);Step(true);PointAtFraction((RectTransform)range.transform,.1f);Step(true);Step(false);
            Check(Math.Abs(range.Upper-range.Lower)<.001f,"VR drag cannot cross the two range endpoints");
            range.SetRange(.1f,.9f);Call(panel,"UpdateRange");Click(Button("VR / 快捷键"));
            var input=(VrPanelInput)Read(panel,"vrInput");input.ResetPointer();PointAt(Button("同步工作台"));frame.Trigger=true;panel.UpdateVr(frame,false);Check(!input.Down,"A held trigger cannot click when the panel opens");Step(false);
            Vector3 placed=canvas.transform.position;rig.transform.rotation=Quaternion.Euler(0,45,0);panel.UpdateVr(frame,false);Check(canvas.transform.position==placed,"Turning the head does not drag a world panel along");
            panel.RecenterVr();Check(Vector3.Dot((canvas.transform.position-rig.transform.position).normalized,rig.transform.forward)>.999f,"Recenter returns the workspace to headset view");
            rig.transform.rotation=Quaternion.identity;panel.RecenterVr();
            panel.SetVisible(false);Check(Read(module,"m_InputOverride")==null,"Hiding releases the VR input override");panel.SetVisible(true);Check(Read(module,"m_InputOverride") is VrPanelInput,"Reopening restores the VR input path");
            panel.UpdateVr(new VrFrame(),false);Canvas.ForceUpdateCanvases();Check(canvas.renderMode==RenderMode.ScreenSpaceOverlay&&Read(module,"m_InputOverride")==null,"Losing VR camera restores desktop rendering and input");
            panel.UpdateVr(frame,true);Call(panel,"ShowVrSettings");Canvas.ForceUpdateCanvases();
            Check(!host.Devices.Connected&&!host.Pipeline.Armed,"VR gestures never start device output");
        }
        yield return null;yield return new WaitForEndOfFrame();
        try
        {
            if(frame!=null&&frame.Camera!=null)
            {
                RenderTexture texture=new RenderTexture(1500,1000,24);frame.Camera.targetTexture=texture;frame.Camera.Render();
                RenderTexture old=RenderTexture.active;RenderTexture.active=texture;var image=new Texture2D(1500,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1500,1000),0,0);image.Apply();
                File.WriteAllBytes(Path.Combine(Paths.GameRootPath,"vr-panel-v0111.png"),image.EncodeToPNG());RenderTexture.active=old;frame.Camera.targetTexture=null;Destroy(image);texture.Release();Destroy(texture);
            }
        }
        catch(Exception ex){report.AppendLine("FAIL capture "+ex);}
        report.AppendLine("Completed "+checks+" simulated VR runtime checks; no physical headset validation");File.WriteAllText(Path.Combine(Paths.GameRootPath,"vr-panel-checks.txt"),report.ToString());
        if(rig!=null)Destroy(rig);if(aim!=null)Destroy(aim);Application.Quit();
    }
}
public sealed class OfficialTrackedFixture:MonoBehaviour{public bool isValid=true;}
public sealed class OfficialDeviceFixture
{
    public uint index;public bool valid=true,connected=true,hasTracking=true;public ulong Mask;
    public bool GetPress(ulong mask){return (Mask&mask)!=0;}
}
public sealed class OfficialControllerFixture{public OfficialTrackedFixture trackedObj;public OfficialDeviceFixture device;}
public sealed class OfficialControllerManagerFixture{public ArrayList lstController;}
public sealed class OfficialSteamCameraFixture{public Camera camera;public Transform head;}
public sealed class OfficialCameraFixture{public OfficialSteamCameraFixture scrCamera;public OfficialControllerManagerFixture scrControllerManager;}
public sealed class OfficialSceneFixture{public ChaControl male;public List<ChaControl> lstFemale;}
