using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;
using BepInEx;
using KoiSync.MainGame;
using UnityEngine;

[BepInPlugin("local.koisync.maingame.live","KoiSync Main Game Live Probe","0.1.0")]
[BepInProcess("Koikatu")]
[BepInDependency("local.koisync.maingame")]
public sealed class LiveProbe:BaseUnityPlugin
{
    private static string Output=@"E:\project\c#\KoiSync\artifacts\main-game-v010";
    private const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    private bool active;
    private float next;
    private KoiSync.MainGame.Plugin host;
    private bool batchActive;
    private bool lastNativeSetPlay;
    private static readonly Dictionary<HSceneProc.AnimationListInfo,string> templates=new Dictionary<HSceneProc.AnimationListInfo,string>();
    private static readonly Dictionary<HSceneProc.AnimationListInfo,string> tags=new Dictionary<HSceneProc.AnimationListInfo,string>();
    private void Awake(){active=Array.IndexOf(Environment.GetCommandLineArgs(),"--koisync-main-validation")>=0;if(active){foreach(var arg in Environment.GetCommandLineArgs())if(arg.StartsWith("--koisync-main-output="))Output=Path.GetFullPath(arg.Substring("--koisync-main-output=".Length));Directory.CreateDirectory(Output);Application.runInBackground=true;AudioListener.volume=0;QualitySettings.vSyncCount=0;Application.targetFrameRate=60;HookCatalogue();}}
    private static void HookCatalogue()
    {
        var t=RuntimeAccess.Find("kPlug.CmpH.ExtraAnimManager");if(t==null)return;var h=new HarmonyLib.Harmony("local.koisync.maingame.live.catalogue");
        h.Patch(t.GetMethod("FakeAnimListInfo",F),null,new HarmonyLib.HarmonyMethod(typeof(LiveProbe),"CaptureTemplate"));
        foreach(var name in new[]{"AddToAnimListInfo_PlayerTong","AddToAnimListInfo_SecondGirlTong"})h.Patch(t.GetMethod(name,F),null,new HarmonyLib.HarmonyMethod(typeof(LiveProbe),"CaptureTag"));
    }
    private static void CaptureTemplate(HSceneProc.AnimationListInfo __result,object[] __args){if(__result!=null)templates[__result]=Convert.ToString(__args[0]);}
    private static void CaptureTag(object[] __args,MethodBase __originalMethod){var info=__args[0] as HSceneProc.AnimationListInfo;if(info!=null){string before;tags.TryGetValue(info,out before);tags[info]=(before??"")+__originalMethod.Name+";";}}
    private void Update()
    {
        if(!active||Time.realtimeSinceStartup<next)return;next=Time.realtimeSinceStartup+.5f;AudioListener.volume=0;
        if(host==null)host=FindObjectOfType<KoiSync.MainGame.Plugin>();
        try
        {
            string command=Path.Combine(Output,"command.txt");if(File.Exists(command)){string text=File.ReadAllText(command);File.Delete(command);Run(text.Split('\n'));}
            Snapshot();
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(Output,"probe-error.txt"),ex.ToString());}
    }
    private void Run(string[] args)
    {
        string op=args[0].Trim();
        if(op=="freeh"){var title=FindObjectOfType<TitleScene>();if(title!=null)typeof(TitleScene).GetMethod("OnOtherFreeH",F).Invoke(title,null);}
        else if(op=="dismiss-tutorial"){var tutorial=FindObjectOfType<Tutorial>();if(tutorial!=null)typeof(Tutorial).GetMethod("EndScene",F,null,Type.EmptyTypes,null).Invoke(tutorial,null);}
        else if(op=="call")
        {Type t=null;foreach(var a in AppDomain.CurrentDomain.GetAssemblies()){t=a.GetType(args[1].Trim(),false);if(t!=null)break;}if(t!=null){var o=FindObjectOfType(t);var result=t.GetMethod(args[2].Trim(),F,null,Type.EmptyTypes,null).Invoke(o,null);if(result is IEnumerator)StartCoroutine((IEnumerator)result);}}
        else if(op=="button")
        {foreach(var b in FindObjectsOfType<UnityEngine.UI.Button>())if(b.name==args[1].Trim()){b.onClick.Invoke();return;}}
        else if(op=="record")host.SetRecording(args.Length<2||args[1].Trim()!="false");
        else if(op=="capture")StartCoroutine(Capture(args.Length>1?args[1].Trim():"live.png"));
        else if(op=="action")
        {
            foreach(var info in CatalogueRows(false))
                if(Key(info)==args[1].Trim()){host.SelectAction(info);return;}
            foreach(var info in CatalogueRows(true))
                if(Key(info)==args[1].Trim()){SelectExtra(info);return;}
        }
        else if(op=="state")
        {
            string state=args[1].Trim();
            bool usedNative=false;
            try
            {
                var lstProc=Read(host.Scene,"lstProc") as System.Collections.IList;
                int mIdx=(int)host.Flags.mode;
                if(lstProc!=null&&mIdx>=0&&mIdx<lstProc.Count&&lstProc[mIdx]!=null)
                {
                    var procObj=lstProc[mIdx];
                    var sp=procObj.GetType().GetMethod("SetPlay",F,null,new[]{typeof(string),typeof(bool)},null);
                    if(sp!=null){sp.Invoke(procObj,new object[]{state,false});usedNative=true;}
                }
            }catch{}
            if(!usedNative)
            {
                foreach(ActorSlot slot in Enum.GetValues(typeof(ActorSlot))){var actor=host.Actor(slot);if(actor!=null&&actor.animBody!=null&&actor.animBody.HasState(0,Animator.StringToHash(state)))actor.AnimPlay(state);}
            }
            else
            {
                // Verify every present actor with this state actually entered it
                int hash=Animator.StringToHash(state);
                foreach(ActorSlot slot in Enum.GetValues(typeof(ActorSlot)))
                {
                    var actor=host.Actor(slot);
                    if(actor!=null&&actor.animBody!=null&&actor.animBody.HasState(0,hash)&&actor.animBody.GetCurrentAnimatorStateInfo(0).shortNameHash!=hash)
                        actor.AnimPlay(state);
                }
            }
            lastNativeSetPlay=usedNative;
            host.Flags.nowAnimStateName=state;host.Flags.speed=1;host.Flags.speedCalc=1;host.Flags.lockGugeFemale=host.Flags.lockGugeMale=true;host.Flags.gaugeFemale=host.Flags.gaugeMale=20;
        }
        else if(op=="setup-freeh")SetupFreeH(args);
        else if(op=="batch"){if(batchActive)throw new InvalidOperationException("A validation batch is already running");StartCoroutine(Batches((args.Length>1?args[1].Trim():"pair").Split(',')));}
        else if(op=="catalogue")WriteCatalogue();
        else if(op=="item-motion")StartCoroutine(ItemMotion());
        else if(op=="item-details")ItemDetails();
        else if(op=="rig")RigDetails();
        else if(op=="binding")
        {
            var b=host.Binding==null?null:host.Binding.Shaft;var report=new StringBuilder();
            if(b!=null)
            {
                report.AppendLine("calibrated="+host.Binding.Length+" fixed="+b.ModelLength+" observed="+b.ReadModelLength());
                foreach(var renderer in b.Source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mesh=renderer.sharedMesh;if(mesh==null)continue;var bones=renderer.bones;var poses=mesh.bindposes;
                    int first=Array.IndexOf(bones,b.Chain[0]),last=Array.IndexOf(bones,b.Chain[b.Chain.Length-1]);
                    if(first>=0&&last>=0&&poses.Length==bones.Length)report.AppendLine(renderer.name+" fixed="+renderer.transform.TransformVector(poses[last].inverse.MultiplyPoint3x4(Vector3.zero)-poses[first].inverse.MultiplyPoint3x4(Vector3.zero)).magnitude);
                }
            }
            File.WriteAllText(Path.Combine(Output,"binding.txt"),report.ToString());
        }
        else if(op=="inspect")Inspect(args.Length>1?args[1].Trim():"FreeHScene");
        else if(op=="visual-audit"){if(batchActive)throw new InvalidOperationException("A validation batch is already running");StartCoroutine(VisualAudit(args.Length>1?args[1].Trim():""));}
        else if(op=="camera-scout"){if(batchActive)throw new InvalidOperationException("A validation batch is already running");StartCoroutine(CameraScout(args.Length>1?args[1].Trim():""));}
    }
    private static object Read(object o,string n)
    {if(o==null)return null;var t=o as Type??o.GetType();var f=t.GetField(n,F);if(f!=null)return f.GetValue(o is Type?null:o);var p=t.GetProperty(n,F);return p==null?null:p.GetValue(o is Type?null:o,null);}
    private static void Set(object o,string n,object v)
    {var f=o.GetType().GetField(n,F);if(f!=null)f.SetValue(o,v);else o.GetType().GetProperty(n,F).SetValue(o,v,null);}
    private void SetupFreeH(string[] args)
    {
        var free=FindObjectOfType<FreeHScene>();if(free==null)throw new Exception("FreeH menu unavailable");
        Manager.Game.Instance.glSaveData.tutorialHash.Add(2);
        int mode=args.Length>1?int.Parse(args[1].Trim()):0;
        var female=new ChaFileControl();bool loaded=false;
        if(args.Length>2&&File.Exists(args[2].Trim()))loaded=female.LoadCharaFile(args[2].Trim(),1,false,true);
        if(!loaded)loaded=female.LoadFromAssetBundle("custom/presets_f_00.unity3d","ill_Default_Female",true,true);
        var male=new ChaFileControl();bool maleLoaded=male.LoadFromAssetBundle("custom/presets_m_00.unity3d","ill_Default_Male",true,true);
        var heroine=loaded?new SaveData.Heroine(female,true):new SaveData.Heroine(true);
        var player=maleLoaded?new SaveData.Player(male,true):new SaveData.Player(true);
        heroine.lewdness=100;heroine.hCount=100;heroine.isVirgin=false;heroine.isAnalVirgin=false;
        var member=Read(free,"member");Set(Read(member,"resultHeroine"),"Value",heroine);Set(Read(member,"resultPlayer"),"Value",player);
        Set(Read(member,"resultStage1"),"Value",3);Set(Read(member,"resultStage2"),"Value",3);
        if(mode==4)Set(Read(member,"resultDarkHeroine"),"Value",heroine);
        if(mode==2||mode==3)
        {
            var partnerFile=new ChaFileControl();bool pLoaded=partnerFile.LoadFromAssetBundle("custom/presets_f_00.unity3d","ill_Default_Female",true,true);
            var partnerHeroine=pLoaded?new SaveData.Heroine(partnerFile,true):new SaveData.Heroine(heroine.charFile,true);
            partnerHeroine.lewdness=100;partnerHeroine.hCount=100;partnerHeroine.isVirgin=false;partnerHeroine.isAnalVirgin=false;
            Set(Read(member,"resultPartner"),"Value",partnerHeroine);
        }
        Set(free,"mode",mode);Set(free,"heroine",heroine);Set(free,"player",player);Set(free,"mapNo",0);Set(free,"stageH1",3);Set(free,"stageH2",3);
        if(mode==2||mode==3)Set(free,"partner",Read(Read(member,"resultPartner"),"Value"));
        var toggle=Read(free,mode==1?"tglMasturbation3P":mode==2?"tglLesbian3P":mode==3?"tgl3P":mode==4?"tglDark3P":"tglNormal3P") as UnityEngine.UI.Toggle;if(toggle!=null)toggle.isOn=true;Set(free,"mode",mode);
        var button=Read(free,"enterButton") as UnityEngine.UI.Button;if(button==null)throw new Exception("Enter button unavailable");button.onClick.Invoke();
    }
    private void Inspect(string name)
    {
        Type type=null;foreach(var a in AppDomain.CurrentDomain.GetAssemblies()){type=a.GetType(name,false);if(type!=null)break;}
        var text=new StringBuilder();if(type!=null){var o=FindObjectOfType(type);foreach(var f in type.GetFields(F)){object v=null;try{v=f.GetValue(f.IsStatic?null:o);}catch{}text.AppendLine(f+" = "+v);}foreach(var m in type.GetMethods(F))text.AppendLine(m.ToString());}
        File.WriteAllText(Path.Combine(Output,"inspect.txt"),text.ToString());
    }
    private void Snapshot()
    {
        var text=new StringBuilder();using(var xml=XmlWriter.Create(text,new XmlWriterSettings{OmitXmlDeclaration=true,Indent=true}))
        {
            xml.WriteStartElement("live");A(xml,"frame",Time.frameCount);A(xml,"timeScale",Time.timeScale);A(xml,"scene",UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);A(xml,"plugin",host!=null);
            if(host!=null)
            {
                A(xml,"status",host.Status);A(xml,"action",host.Profile==null?"":host.Profile.Key);A(xml,"stage",host.Stage);A(xml,"samples",host.Samples);A(xml,"recording",host.RecordRequested);A(xml,"connected",host.Devices.Connected);A(xml,"sync",host.SyncRequested);A(xml,"binding",host.Binding!=null);
                A(xml,"phaseTime",Read(host,"phaseTime"));A(xml,"phaseDuration",Read(host,"phaseDuration"));
                if(host.Binding!=null){A(xml,"sourceAnchor",host.Binding.Anchor==null?"":host.Binding.Anchor.name);A(xml,"sourceClock",host.Binding.Clock==null?"":host.Binding.Clock.name);}
                if(host.Binding!=null)A(xml,"nativeTipBound",host.Binding.NativeEntryBound);
                if(host.Measured!=null){A(xml,"engagement",host.Measured.Engagement);A(xml,"contactDistance",host.Measured.ContactDistance);A(xml,"depth",host.Measured.TargetDepth);A(xml,"modelLength",host.Measured.Length);A(xml,"material",host.Measured.MaterialInsertion);A(xml,"inputL0",host.Drive.Input[0]);A(xml,"targetL0",host.Drive.Target[0]);}
                if(host.Sample!=null)
                {
                    var s=host.Sample;A(xml,"origin",Vec(s.Origin));A(xml,"entry",Vec(s.Target));A(xml,"end",Vec(s.End));A(xml,"inward",Vec(s.TargetReferenceFrame.Y));A(xml,"sourceY",Vec(s.ReferenceFrame.Y));
                }
                for(int i=0;i<6;i++)A(xml,"axis"+i,host.Drive.Values[i]);
                if(host.Binding!=null&&host.Sample!=null&&host.Profile.Effector==ActionSource.Shaft)
                {
                    if(host.Flags!=null&&host.Flags.nowAnimationInfo!=null){xml.WriteStartElement("nativeInfo");A(xml,"fileItem",host.Flags.nowAnimationInfo.paramFemale.fileItem);xml.WriteEndElement();}
                    var nativeItems=Read(Read(host.Scene,"item"),"dicItem") as IDictionary;
                    if(nativeItems!=null)foreach(DictionaryEntry itemRow in nativeItems)
                    {
                        var obj=Read(itemRow.Value,"objItem") as GameObject;if(obj==null||!obj.activeInHierarchy)continue;
                        xml.WriteStartElement("nativeItem");A(xml,"id",itemRow.Key);A(xml,"name",obj.name);A(xml,"position",obj.transform.position);
                        foreach(var t in obj.GetComponentsInChildren<Transform>(true)){xml.WriteStartElement("node");A(xml,"name",t.name);A(xml,"position",t.position);A(xml,"distance",KoiSync.Core.StudioShaftMotion.ClosestDistance(host.Sample.ShaftPath,GameBinding.V(t.position)));xml.WriteEndElement();}
                        xml.WriteEndElement();
                    }
                    var look=Read(host.Scene,host.ActiveSource==ActorSlot.Male1?"lookDan1":"lookDan");
                    foreach(var native in new[]{Read(look,"transLookAtNull") as Transform,Read(Read(look,"lookat"),"trfTarget") as Transform})
                    {if(native==null)continue;xml.WriteStartElement("nativeAnchor");A(xml,"name",native.name);A(xml,"parent",native.parent==null?"":native.parent.name);A(xml,"position",native.position);A(xml,"inTarget",native.IsChildOf(host.Binding.Target.transform));A(xml,"distance",KoiSync.Core.StudioShaftMotion.ClosestDistance(host.Sample.ShaftPath,GameBinding.V(native.position)));xml.WriteEndElement();}
                    var bones=new Dictionary<string,Transform>();foreach(var t in host.Binding.Target.GetComponentsInChildren<Transform>(true))if(!bones.ContainsKey(t.name))bones.Add(t.name,t);
                    foreach(var kind in new[]{OpeningKind.LeftHand,OpeningKind.RightHand})
                    {
                        Transform t;string name=kind==OpeningKind.LeftHand?"cf_j_hand_L":"cf_j_hand_R";if(!bones.TryGetValue(name,out t))continue;
                        var point=new GameContactPoint(bones,kind,t);KoiSync.Core.MotionVector position,right,inward;point.Pose(out position,out right,out inward);
                        xml.WriteStartElement("candidate");A(xml,"kind",kind);A(xml,"distance",KoiSync.Core.StudioShaftMotion.ClosestDistance(host.Sample.ShaftPath,position));A(xml,"axial",KoiSync.Core.MotionVector.Dot(position-host.Sample.Origin,host.Sample.ReferenceFrame.Y));xml.WriteEndElement();
                    }
                }
                foreach(ActorSlot slot in Enum.GetValues(typeof(ActorSlot)))
                {var a=host.Actor(slot);if(a==null)continue;xml.WriteStartElement("actor");A(xml,"slot",slot);A(xml,"name",a.name);A(xml,"active",a.gameObject.activeInHierarchy);if(a.animBody!=null){var s=a.animBody.GetCurrentAnimatorStateInfo(0);A(xml,"state",s.shortNameHash);A(xml,"progress",s.normalizedTime);A(xml,"duration",s.length);}xml.WriteEndElement();}
                foreach(var name in new[]{"lookDan","lookDan1"})
                {var look=Read(host.Scene,name) as Component;if(look==null)continue;var node=Read(look,"transLookAtNull") as Transform;xml.WriteStartElement("nativeRole");A(xml,"source",name=="lookDan"?"Male":"Male1");A(xml,"targetIndex",Read(look,"numFemale"));A(xml,"enabled",((Behaviour)look).enabled);A(xml,"pin",Read(look,"bTopStick"));A(xml,"entry",node==null?"":node.name);A(xml,"parent",node==null||node.parent==null?"":node.parent.name);xml.WriteEndElement();}
            }
            foreach(var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()){xml.WriteStartElement("root");A(xml,"name",root.name);xml.WriteEndElement();}
            xml.WriteEndElement();
        }
        File.WriteAllText(Path.Combine(Output,"live.xml"),text.ToString());
    }
    private static void A(XmlWriter xml,string name,object value){xml.WriteAttributeString(name,Convert.ToString(value,CultureInfo.InvariantCulture));}
    private static string Vec(KoiSync.Core.MotionVector v){return v.X.ToString("R",CultureInfo.InvariantCulture)+","+v.Y.ToString("R",CultureInfo.InvariantCulture)+","+v.Z.ToString("R",CultureInfo.InvariantCulture);}
    private string Key(HSceneProc.AnimationListInfo info)
    {return host.Catalogue.Resolve(info.mode.ToString(),info.id,info.nameAnimation,false,KoiSync.MainGame.Plugin.ResourceIdentity(info)).Key;}
    private IEnumerable<HSceneProc.AnimationListInfo> CatalogueRows(bool extra)
    {
        if(extra)
        {var t=RuntimeAccess.Find("kPlug.CmpH.ExtraAnimManager");var manager=t==null?null:FindObjectOfType(t);var entries=Read(manager,"extraAnims") as IEnumerable;if(entries!=null)foreach(HSceneProc.AnimationListInfo info in entries)yield return info;}
        else foreach(var entries in (IEnumerable)Read(host.Scene,"lstAnimInfo"))foreach(HSceneProc.AnimationListInfo info in (IEnumerable)entries)yield return info;
    }
    private bool Captured(string key,string stage)
    {foreach(string completed in (IEnumerable)Read(host,"completedStages"))if(completed.StartsWith(key+":"+stage+":",StringComparison.Ordinal))return true;return false;}
    private void SelectExtra(HSceneProc.AnimationListInfo info)
    {
        var translator=RuntimeAccess.Find("kPlug.DefAndParse.kTranslator");var names=translator.GetMethod("AnimNameMapper_H",F).Invoke(null,null) as IDictionary;
        var type=RuntimeAccess.Find("kPlug.CmpH.AnimSelector");var selector=FindObjectOfType(type);string name=Convert.ToString(names[info.paramFemale.path.file]);
        if(selector==null||string.IsNullOrEmpty(name))throw new InvalidOperationException("kPlug native selector unavailable");
        // Run the same native selection coroutine as its menu button. It releases
        // the previous pose's look/IK bindings before requesting the new action.
        Set(selector,"targetAnim",name);StartCoroutine((IEnumerator)type.GetMethod("IE_SetSelectedAnim",F).Invoke(selector,new object[]{name}));
    }
    private string Evidence(HSceneProc.AnimationListInfo info)
    {
        var text=new StringBuilder();text.Append('\t').Append(KoiSync.MainGame.Plugin.ResourceIdentity(host.Flags.nowAnimationInfo));
        text.Append('\t');foreach(var clip in host.Actor(ActorSlot.Female0).animBody.GetCurrentAnimatorClipInfo(0))if(clip.weight>.05f)text.Append(clip.clip.name).Append(';');
        var t=RuntimeAccess.Find("kPlug.CmpH.ExtraAnimManager");text.Append('\t').Append(t==null?"":Read(FindObjectOfType(t),"currentAnim"));
        text.Append('\t').Append(host.Measured==null?"":host.Measured.ContactDistance.ToString("R",CultureInfo.InvariantCulture));
        text.Append('\t');if(host.Binding!=null&&host.Sample!=null&&host.Sample.ShaftPath!=null)
        {
            var bones=new Dictionary<string,Transform>();foreach(var bone in host.Binding.Target.GetComponentsInChildren<Transform>(true))bones[bone.name]=bone;
            var kinds=new[]{OpeningKind.LeftHand,OpeningKind.RightHand,OpeningKind.LeftFoot,OpeningKind.RightFoot};var nodes=new[]{"cf_j_hand_L","cf_j_hand_R","cf_j_foot_L","cf_j_foot_R"};
            for(int i=0;i<kinds.Length;i++){Transform node;if(!bones.TryGetValue(nodes[i],out node))continue;var point=new GameContactPoint(bones,kinds[i],node);KoiSync.Core.MotionVector p,r,y;point.Pose(out p,out r,out y);text.Append(kinds[i]).Append('=').Append(KoiSync.Core.StudioShaftMotion.ClosestDistance(host.Sample.ShaftPath,p).ToString("F4",CultureInfo.InvariantCulture)).Append(';');}
        }
        text.Append('\t');foreach(var name in new[]{"lookDan","lookDan1"})
        {var look=Read(host.Scene,name) as Component;if(look==null)continue;var node=Read(look,"transLookAtNull") as Transform;text.Append(name).Append('=').Append(node==null?"":node.name).Append('/').Append(node==null||node.parent==null?"":node.parent.name).Append('/').Append(Read(look,"bTopStick")).Append(';');}
        return text.ToString();
    }
    private IEnumerator Batches(string[] groups)
    {batchActive=true;foreach(var group in groups)yield return Batch(group);batchActive=false;}
    private IEnumerator Batch(string group)
    {
        File.WriteAllText(Path.Combine(Output,"batch-"+group+"-started.txt"),DateTime.UtcNow.ToString("o"));
        File.WriteAllText(Path.Combine(Output,"batch-"+group+"-solver.txt"),"main-"+typeof(KoiSync.MainGame.Plugin).Assembly.GetName().Version.ToString(3));
        yield return new WaitForSecondsRealtime(2);
        // FreeH removes story-only peeping entries from the live menu.
        // Re-read the native catalogue for this opt-in playback fixture.
        if(group=="peeping")typeof(HSceneProc).GetMethod("CreateAllAnimationList",F).Invoke(host.Scene,null);
        bool extra=group.StartsWith("kplug-",StringComparison.Ordinal);string kind=extra?group.Substring(6):group;
        var rows=new List<HSceneProc.AnimationListInfo>();
        foreach(var info in CatalogueRows(extra))
        {
            string mode=info.mode.ToString();bool wanted=kind=="contact"?mode=="houshi"&&(info.id==32||info.id==33):kind=="motion"?(mode=="sonyu"||mode=="houshi")&&info.id==0:kind=="suspects"?mode=="houshi"&&Array.IndexOf(new[]{0,16,21,35,36,49},info.id)>=0:kind=="pair"?(mode=="sonyu"||mode=="houshi"):kind=="solo"?(mode=="masturbation"||mode=="peeping"):kind=="anal"?info.paramFemale.isAnal:kind=="multi"||kind=="fmf"||kind=="mmf"?mode.IndexOf("3P",StringComparison.Ordinal)>=0:mode==kind;
            if(wanted)rows.Add(info);
        }
        if(rows.Count==0)throw new InvalidOperationException("No native catalogue rows for "+group);
        var report=new StringBuilder("action\tname\tstage\tbound\tsamples\tstatus\trequested\tresource\tclips\tkplugCurrent\tcontactDistance\tlandmarks\tnativeRoles\n");
        foreach(var info in rows)
        {
            host.SetRecording(false);
            host.Flags.isAnalPlay=false;
            bool compatible=(bool)host.GetType().GetMethod("CanSelect",F).Invoke(host,new object[]{info});
            if(!compatible&&group!="peeping")continue;
            if(group=="contact"||extra)
            {
                foreach(var point in Resources.FindObjectsOfTypeAll<H.HPointData>())
                {
                    if(!point.gameObject.scene.IsValid()||!point.gameObject.scene.isLoaded)continue;
                    bool found=false;foreach(var category in info.lstCategory)if(Array.IndexOf(point.category,category.category)>=0)
                    {typeof(HSceneProc).GetMethod("ChangeCategory",F).Invoke(host.Scene,new object[]{point,category.category});found=true;break;}
                    if(found)break;
                }
                yield return new WaitForSecondsRealtime(1);
            }
            if(group=="peeping")
            {
                // The two story-only scenes have different base controllers.
                // Normal gameplay creates a fresh H scene for each; force the
                // native mode initialization when reusing this fixture scene.
                host.Flags.nowAnimationInfo=new HSceneProc.AnimationListInfo{mode=HFlag.EMode.none};
                typeof(HSceneProc).GetMethod("ChangeAnimator",F).Invoke(host.Scene,new object[]{info,false});
            }
            else if(extra)SelectExtra(info);
            else host.SelectAction(info);
            float waitUntil=Time.realtimeSinceStartup+5;
            string key=Key(info);while(Time.realtimeSinceStartup<waitUntil&&(host.Profile==null||host.Profile.Key!=key))yield return null;
            yield return new WaitForSecondsRealtime(.5f);
            if(info.mode.ToString().StartsWith("houshi",StringComparison.Ordinal))
            {host.Flags.click=(HFlag.ClickKind)5;yield return new WaitForSecondsRealtime(.5f);}
            if(group=="peeping")
            {
                string phase=info.id==0?"Loop":"Idle";host.SetRecording(true);Run(new[]{"state",phase});yield return null;
                if(!host.Actor(ActorSlot.Female0).animBody.HasState(0,Animator.StringToHash(phase)))throw new InvalidOperationException("Native peeping base controller lacks "+phase);
                long before=host.Samples;double duration=host.Actor(ActorSlot.Female0).animBody.GetCurrentAnimatorStateInfo(0).length;
                float end=Time.realtimeSinceStartup+(float)Math.Max(4,Math.Min(90,duration*2.5+1));
                while(Time.realtimeSinceStartup<end)yield return null;
                host.SetRecording(false);report.Append(info.mode+"_"+info.id).Append('\t').Append(info.nameAnimation).Append('\t').Append(phase).Append('\t').Append(host.Binding!=null).Append('\t').Append(host.Samples-before).Append('\t').Append(host.Status).Append('\t').Append("native-playback").AppendLine();
                File.WriteAllText(Path.Combine(Output,"batch-"+group+".tsv"),report.ToString());continue;
            }
            if(kind=="anal")host.Flags.isAnalPlay=true;
            if(info.mode.ToString().StartsWith("sonyu",StringComparison.Ordinal))
            {if(host.Flags.isInsertOK!=null&&host.Flags.isInsertOK.Length>0)host.Flags.isInsertOK[0]=true;host.Flags.isAnalInsertOK=true;host.Flags.click=kind=="anal"?HFlag.ClickKind.insert_anal:HFlag.ClickKind.insert;yield return new WaitForSecondsRealtime(1.2f);}
            if(group=="aibu")AttachNativeItem();
            if(group=="aibu")
            {
                yield return new WaitForSecondsRealtime(.5f);host.SetRecording(true);long before=host.Samples;float end=Time.realtimeSinceStartup+6,retry=0;
                while(Time.realtimeSinceStartup<end)
                {if(host.Binding==null&&Time.realtimeSinceStartup>=retry){AttachNativeItem();retry=Time.realtimeSinceStartup+.5f;}KeepItemInput();host.Flags.xy[2]=new Vector2(.5f,.5f+.4f*Mathf.Sin((end-Time.realtimeSinceStartup)*Mathf.PI*2));yield return null;}
                host.SetRecording(false);report.Append(key).Append('\t').Append(info.nameAnimation).Append('\t').Append(host.Stage).Append('\t').Append(host.Binding!=null).Append('\t').Append(host.Samples-before).Append('\t').Append(host.Status).Append('\t').Append("native-item").Append(Evidence(info)).AppendLine();
                File.WriteAllText(Path.Combine(Output,"batch-"+group+".tsv"),report.ToString());continue;
            }
            foreach(var plainState in new[]{"WLoop","SLoop"})
            {
                string state=(kind=="anal"?"A_":"")+plainState;
                var target=host.Actor(ActorSlot.Female0);if(target==null||target.animBody==null||!target.animBody.HasState(0,Animator.StringToHash(state)))continue;
                Run(new[]{"state",state});float settle=Time.realtimeSinceStartup+.3f;while(Time.realtimeSinceStartup<settle){KeepInput(state);yield return null;}host.SetRecording(true);
                long before=host.Samples;double duration=target.animBody.GetCurrentAnimatorStateInfo(0).length;
                float until=Time.realtimeSinceStartup+(float)Math.Max(3,Math.Min(12,duration*3+.5));string expected=key+(kind=="anal"?"_anal":"");while(Time.realtimeSinceStartup<until&&!Captured(expected,state)){KeepInput(state);yield return null;}
                host.SetRecording(false);report.Append(key).Append('\t').Append(info.nameAnimation).Append('\t').Append(host.Stage).Append('\t').Append(host.Binding!=null).Append('\t').Append(host.Samples-before).Append('\t').Append(host.Status.Replace('\t',' ')).Append('\t').Append(state).Append(Evidence(info)).AppendLine();
                File.WriteAllText(Path.Combine(Output,"batch-"+group+".tsv"),report.ToString());
                if(host.Profile!=null&&host.Profile.Effector==ActionSource.AnimatedProp)yield return PropCheck(state);
            }
        }
        File.WriteAllText(Path.Combine(Output,"batch-"+group+"-complete.txt"),DateTime.UtcNow.ToString("o"));
        if(kind=="anal")host.Flags.isAnalPlay=false;
    }
    private void AttachNativeItem()
    {
        var hand=Read(host.Scene,"hand");var areas=Read(hand,"dicAreaLayerInfos") as Array;var items=Read(hand,"dicItem") as IDictionary;if(areas==null||areas.Length<3||items==null)return;
        var layers=areas.GetValue(2) as IDictionary;if(layers==null)return;
        foreach(DictionaryEntry row in layers)
        {
            var ids=Read(row.Value,"idObjects") as int[];if(ids==null||ids.Length==0||!items.Contains(ids[0]))continue;
            var item=items[ids[0]];int id=Convert.ToInt32(Read(item,"idUse"));
            bool front=host.Flags.nowAnimationInfo.paramFemale.lstFrontAndBehind[0]==0;
            hand.GetType().GetMethod("SetItem",F).Invoke(hand,new object[]{id,2,row.Value,0,HandCtrl.AibuColliderKind.kokan,front,false});
            hand.GetType().GetMethod("SetLayerWeight",F).Invoke(hand,new object[]{id,row.Value,front,2});
            Set(hand,"actionUseItem",id);host.Flags.speedItem=1;return;
        }
    }
    private void KeepInput(string state)
    {
        bool strong=state.IndexOf("SLoop",StringComparison.Ordinal)>=0;
        // Native WaitSpeedProc derives speed from speedCalc and its click timer.
        // Keep native input alive; setting speed alone is overwritten next Update.
        host.Flags.speedCalc=.65f;host.Flags.timeNoClick=0;host.Flags.SpeedUpClick(0,1);
        host.Flags.speedHoushi=strong?1:0;host.Flags.speedLesbian=strong?2.25f:1.25f;
        host.Flags.lockGugeFemale=host.Flags.lockGugeMale=true;host.Flags.gaugeFemale=host.Flags.gaugeMale=20;
    }
    private void KeepItemInput(){host.Flags.speedItem=.65f;host.Flags.SpeedUpClickItemAibu(0,1,true);}
    private void ItemDetails()
    {
        var text=new StringBuilder();var hand=Read(host.Scene,"hand");
        foreach(var field in hand.GetType().GetFields(F))if(!field.FieldType.IsArray&&!typeof(IDictionary).IsAssignableFrom(field.FieldType))text.AppendLine(field.Name+" = "+field.GetValue(hand));
        var items=Read(hand,"useAreaItems") as IEnumerable;
        if(items!=null)foreach(var item in items)
        {
            if(item==null)continue;var obj=Read(item,"obj") as GameObject;if(obj==null||!obj.activeInHierarchy)continue;
            text.AppendLine("ITEM "+obj.name+" / "+Read(item,"kindTouch"));
            foreach(var name in new[]{"layerAction","layerIdle"}){var layer=Read(item,name);if(layer!=null)foreach(var field in layer.GetType().GetFields(F))text.AppendLine(name+"."+field.Name+" = "+field.GetValue(layer));}
            var animator=Read(item,"anm") as Animator;if(animator==null)continue;
            for(int i=0;i<animator.layerCount;i++){var state=animator.GetCurrentAnimatorStateInfo(i);text.AppendLine("LAYER "+i+" "+animator.GetLayerName(i)+" w="+animator.GetLayerWeight(i)+" hash="+state.shortNameHash+" time="+state.normalizedTime+" duration="+state.length+" loop="+state.loop);foreach(var clip in animator.GetCurrentAnimatorClipInfo(i))text.AppendLine("CLIP "+clip.clip.name+" / "+clip.clip.length+" / "+clip.weight);}
        }
        File.WriteAllText(Path.Combine(Output,"item-details.txt"),text.ToString());
    }
    private void RigDetails()
    {
        var text=new StringBuilder();text.AppendLine("action="+host.Profile.Key+" stage="+host.Stage+" entry="+(host.Sample==null?"":Vec(host.Sample.Target)));
        foreach(ActorSlot slot in Enum.GetValues(typeof(ActorSlot)))
        {
            var actor=host.Actor(slot);if(actor==null)continue;var root=actor.objBodyBone;var wanted=new HashSet<string>(new[]{"cf_j_hand_L","cf_j_hand_R","cf_j_index01_L","cf_j_index01_R","cf_j_middle01_L","cf_j_middle01_R","cf_j_middle04_L","cf_j_middle04_R","cf_j_ana","cf_J_Vagina_root","cm_J_dan100_00","cf_j_foot_L","cf_j_foot_R"});
            var body=actor.rendBody as SkinnedMeshRenderer;var rendered=body==null?new Transform[0]:body.bones;
            text.AppendLine("ACTOR "+slot+" body="+(root==null?"":root.name)+" renderer="+(body==null?"":body.name));
            foreach(var node in actor.GetComponentsInChildren<Transform>(true))if(wanted.Contains(node.name))
            {text.Append(slot).Append('\t').Append(node.name).Append('\t').Append(root!=null&&node.IsChildOf(root.transform)).Append('\t').Append(node.gameObject.activeInHierarchy).Append('\t').Append(node.position).Append('\t');if(host.Sample!=null)text.Append((GameBinding.V(node.position)-host.Sample.Target).Length.ToString("F4",CultureInfo.InvariantCulture));text.Append('\t').Append("rendered="+Array.IndexOf(rendered,node)).Append('\t');var p=node;for(int i=0;p!=null&&i<32;i++,p=p.parent)text.Append('/').Append(p.name);text.AppendLine();}
        }
        File.WriteAllText(Path.Combine(Output,"rig-"+host.Profile.Key+".txt"),text.ToString());
    }
    private IEnumerator PropCheck(string state)
    {
        var model=host.Binding==null||host.Binding.Shaft==null?null:host.Binding.Shaft.Model;if(model==null||!model.PropFrame)yield break;
        string key=host.Profile.Key;var rings=model.MaterialRings;var baked=new Mesh();double maxError=0;int frames=0;
        for(int frame=0;frame<20;frame++)
        {
            KeepInput(state);yield return new WaitForEndOfFrame();if(host.Profile==null||host.Profile.Key!=key)break;
            model.Renderer.BakeMesh(baked);var vertices=baked.vertices;
            for(int ring=0;ring<rings.Length;ring++)
            {Vector3 center=Vector3.zero;foreach(int index in rings[ring])center+=vertices[index];var world=model.Renderer.transform.TransformPoint(center/rings[ring].Length);maxError=Math.Max(maxError,(GameBinding.V(world)-host.Sample.ShaftPath[ring]).Length);}
            frames++;yield return new WaitForSecondsRealtime(.025f);
        }
        Destroy(baked);File.WriteAllText(Path.Combine(Output,"prop-check-"+key+"-"+state+".xml"),"<propCheck action=\""+key+"\" stage=\""+state+"\" frames=\""+frames+"\" maxWorldErrorMetres=\""+maxError.ToString("R",CultureInfo.InvariantCulture)+"\" reference=\"Unity SkinnedMeshRenderer.BakeMesh final-frame vertices\" />");
    }
    private IEnumerator ItemMotion()
    {
        AttachNativeItem();yield return new WaitForSecondsRealtime(.5f);
        var hand=Read(host.Scene,"hand");var items=Read(hand,"useAreaItems") as IEnumerable;
        GameObject obj=null;Animator animator=null;
        if(items!=null)foreach(var item in items)
        {if(item==null||Convert.ToString(Read(item,"kindTouch"))!="kokan")continue;var candidate=Read(item,"obj") as GameObject;if(candidate==null||!candidate.activeInHierarchy)continue;obj=candidate;animator=Read(item,"anm") as Animator;break;}
        if(obj==null)throw new InvalidOperationException("Native interaction item unavailable");
        var text=new StringBuilder("time\titem\tnode\tx\ty\tz\tlocalX\tlocalY\tlocalZ\tentryX\tentryY\tentryZ\n");
        var nodes=obj.GetComponentsInChildren<Transform>(true);float start=Time.realtimeSinceStartup;
        while(Time.realtimeSinceStartup-start<3)
        {
            KeepItemInput();foreach(var node in nodes){var p=node.position;var q=node.localPosition;var e=host.Sample==null?new KoiSync.Core.MotionVector():host.Sample.Target;text.AppendFormat(CultureInfo.InvariantCulture,"{0:R}\t{1}\t{2}\t{3:R}\t{4:R}\t{5:R}\t{6:R}\t{7:R}\t{8:R}\t{9:R}\t{10:R}\t{11:R}\n",Time.realtimeSinceStartup-start,obj.name,node.name,p.x,p.y,p.z,q.x,q.y,q.z,e.X,e.Y,e.Z);}
            yield return new WaitForSecondsRealtime(.025f);
        }
        File.WriteAllText(Path.Combine(Output,"item-motion.tsv"),text.ToString());
        var clips=new StringBuilder();if(animator!=null){foreach(var clip in animator.GetCurrentAnimatorClipInfo(0))clips.AppendLine(clip.clip.name+" / "+clip.clip.length+" / "+clip.weight);foreach(var p in animator.parameters)clips.AppendLine(p.name+" / "+p.type);}
        File.WriteAllText(Path.Combine(Output,"item-clips.txt"),clips.ToString());
    }
    private void WriteCatalogue()
    {
        var text=new StringBuilder("origin\tmode\tid\tname\tmaleBase\tmaleBundle\tmale\tfemaleBase\tfemaleBundle\tfemale\tsecondBase\tsecondBundle\tsecond\tanal\tkindHoushi\titem\ttemplate\ttags\tresource\n");
        foreach(var entries in (IEnumerable)Read(host.Scene,"lstAnimInfo"))foreach(HSceneProc.AnimationListInfo info in (IEnumerable)entries)CatalogueRow(text,"native",info);
        var extra=RuntimeAccess.Find("kPlug.CmpH.ExtraAnimManager");var manager=extra==null?null:FindObjectOfType(extra);var rows=Read(manager,"extraAnims") as IEnumerable;
        if(rows!=null)foreach(HSceneProc.AnimationListInfo info in rows)CatalogueRow(text,"kplug",info);
        File.WriteAllText(Path.Combine(Output,"catalogue.tsv"),text.ToString());
        var translator=RuntimeAccess.Find("kPlug.DefAndParse.kTranslator");if(translator!=null)foreach(var method in new[]{"Names","AnimNameMapper_H"})
        {var dict=translator.GetMethod(method,F).Invoke(null,null) as IDictionary;var names=new StringBuilder("key\tname\n");if(dict!=null)foreach(DictionaryEntry row in dict)names.Append(row.Key).Append('\t').Append(Convert.ToString(row.Value).Replace('\t',' ').Replace('\n',' ')).AppendLine();File.WriteAllText(Path.Combine(Output,"names-"+method+".tsv"),names.ToString());}
    }
    private static void CatalogueRow(StringBuilder text,string origin,HSceneProc.AnimationListInfo info)
    {
        string template,tag;templates.TryGetValue(info,out template);tags.TryGetValue(info,out tag);
        object[] fields={origin,info.mode,info.id,info.nameAnimation,info.pathMaleBase.file,info.paramMale.path.assetpath,info.paramMale.path.file,info.pathFemaleBase.file,info.paramFemale.path.assetpath,info.paramFemale.path.file,info.pathFemaleBase1.file,info.paramFemale1.path.assetpath,info.paramFemale1.path.file,info.paramFemale.isAnal,info.kindHoushi,info.paramFemale.fileItem,template,tag,KoiSync.MainGame.Plugin.ResourceIdentity(info)};
        for(int i=0;i<fields.Length;i++){if(i>0)text.Append('\t');text.Append(Convert.ToString(fields[i],CultureInfo.InvariantCulture).Replace('\t',' ').Replace('\n',' '));}text.AppendLine();
    }
    private IEnumerator Capture(string name)
    {
        yield return new WaitForEndOfFrame();var panel=(Rect)Read(host,"window");float scale=Math.Min(1,Math.Min(Screen.width/900f,Screen.height/(panel.height+20)));
        const float inset=8;int w=Mathf.RoundToInt((panel.width-inset*2)*scale),h=Mathf.RoundToInt((panel.height-inset*2)*scale);var image=new Texture2D(w,h,TextureFormat.RGB24,false);image.ReadPixels(new Rect(Mathf.Round((panel.x+inset)*scale),Screen.height-Mathf.Round((panel.y+panel.height-inset)*scale),w,h),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,name),image.EncodeToPNG());Destroy(image);
    }

    private void PrepareActorsForVisualAudit()
    {
        bool showMaleBody=host!=null&&host.Profile!=null&&
            (host.Profile.Source==ActorSlot.Male||host.Profile.Source==ActorSlot.Male1)&&
            (host.Profile.Effector==ActionSource.Head||host.Profile.Effector==ActionSource.LeftFinger||host.Profile.Effector==ActionSource.RightFinger);
        try
        {
            var etc=Manager.Config.EtcData;
            if(etc!=null)
            {
                etc.SimpleBody=false;
                etc.VisibleBody=showMaleBody;
                etc.VisibleSon=true;
                etc.IsMaleClothes=false;
                etc.IsMaleShoes=false;
                etc.Shield=false;
                etc.DepthOfField=false;
                etc.Map=false;
            }
        }catch{}
        // Hide room/map geometry cleanly so camera captures pure character + 3D preview on solid backdrop
        try
        {
            var mapObj=Read(host.Scene,"map");
            var mapRoot=Read(mapObj,"mapRoot") as GameObject;
            if(mapRoot!=null)mapRoot.SetActive(false);
        }catch{}
        try
        {
            foreach(var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if(root==null)continue;
                string rn=root.name.ToLowerInvariant();
                if(rn.StartsWith("map")||rn.Contains("maproot")||rn.Contains("hpoint")||rn.Contains("furniture")||rn.Contains("room"))
                    root.SetActive(false);
            }
        }catch{}
        foreach(ActorSlot slot in Enum.GetValues(typeof(ActorSlot)))
        {
            var actor=host.Actor(slot);if(actor==null)continue;
            bool isMale=(slot==ActorSlot.Male||slot==ActorSlot.Male1);
            try{actor.visibleAll=true;}catch{}
            try{actor.ChangeSimpleBodyDraw(false);}catch{}
            try{actor.SetClothesStateAll(3);}catch{}
            try
            {
                if(actor.fileStatus!=null)
                {
                    actor.fileStatus.visibleSimple=false;
                    actor.fileStatus.visibleBodyAlways=!isMale||showMaleBody;
                    actor.fileStatus.visibleSon=isMale;
                    actor.fileStatus.visibleSonAlways=isMale;
                    actor.fileStatus.visibleGomu=false;
                }
            }catch{}
            try{var uv=actor.GetType().GetMethod("UpdateVisible",F);if(uv!=null)uv.Invoke(actor,null);}catch{}
        }
    }

    private static Vector3 ToV3(KoiSync.Core.MotionVector v){return new Vector3((float)v.X,(float)v.Y,(float)v.Z);}

    private static string JsonEsc(string s)
    {
        if(string.IsNullOrEmpty(s))return "";
        return s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r"," ").Replace("\n"," ").Replace("\t"," ");
    }

    private static string ProjJson(Camera cam,Vector3 world,int w,int h)
    {
        var vp=cam.WorldToViewportPoint(world);
        float px=vp.x*w;float py=(1f-vp.y)*h;
        return string.Format(CultureInfo.InvariantCulture,"[{0:F1},{1:F1},{2:F3}]",px,py,vp.z);
    }

    private static void RenderAuditView(Camera cam,RenderTexture rt,Texture2D tex,string path)
    {
        var prevRt=RenderTexture.active;
        cam.targetTexture=rt;
        cam.Render();
        RenderTexture.active=rt;
        tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);
        tex.Apply();
        File.WriteAllBytes(path,tex.EncodeToPNG());
        cam.targetTexture=null;
        RenderTexture.active=prevRt;
    }

    private static void PositionAuditCameraDir(Camera cam,Vector3 focus,Vector3 dir,float dist)
    {
        Vector3 d=dir.sqrMagnitude>1e-5f?dir.normalized:Vector3.right;
        Vector3 up=Mathf.Abs(Vector3.Dot(d,Vector3.up))<0.92f?Vector3.up:Vector3.forward;
        cam.transform.position=focus+d*dist;
        cam.transform.LookAt(focus,up);
        cam.fieldOfView=38f;
        cam.nearClipPlane=0.03f;
        cam.farClipPlane=50f;
    }

    private struct AutoCamResult
    {
        public Vector3 BestDir;
        public Vector3 SecondDir;
        public float AngleDeg;
        public int VisPx;
        public int NearEntryPx;
        public float ShaftSpan;
    }

    private static Color32[] RenderSmallPixels(Camera cam,RenderTexture rt,Texture2D tex)
    {
        var prevRt=RenderTexture.active;
        cam.targetTexture=rt;
        cam.Render();
        RenderTexture.active=rt;
        tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);
        tex.Apply(false,false);
        cam.targetTexture=null;
        RenderTexture.active=prevRt;
        return tex.GetPixels32();
    }

    private AutoCamResult AutoSelectBestCamera(Camera cam,Vector3 focus,Vector3 vOrig,Vector3 vEnd,Vector3 vCal,Vector3 vInward,float dist)
    {
        const int SW=96,SH=54;
        Vector3 S=vEnd-vOrig;
        if(S.sqrMagnitude<1e-6f)S=vInward.sqrMagnitude>1e-6f?vInward:Vector3.forward;
        S.Normalize();
        Vector3 refVec=Mathf.Abs(Vector3.Dot(S,Vector3.up))<0.85f?Vector3.up:Vector3.forward;
        Vector3 U1=Vector3.Cross(S,refVec).normalized;
        Vector3 U2=Vector3.Cross(S,U1).normalized;

        // 24 strictly-perpendicular candidates (74..90 deg to shaft axis S -> ZERO dot-collapse!)
        var cands=new List<Vector3>(24);
        for(int k=0;k<12;k++){float a=k*30f*Mathf.Deg2Rad;cands.Add((Mathf.Cos(a)*U1+Mathf.Sin(a)*U2).normalized);}
        for(int k=0;k<6;k++){float a=k*60f*Mathf.Deg2Rad;cands.Add(((Mathf.Cos(a)*U1+Mathf.Sin(a)*U2)*0.961f-S*0.275f).normalized);}
        for(int k=0;k<6;k++){float a=(k*60f+30f)*Mathf.Deg2Rad;cands.Add(((Mathf.Cos(a)*U1+Mathf.Sin(a)*U2)*0.961f+S*0.275f).normalized);}

        // For non-shaft actions (aibu, oral cunnilingus, fingering, solo, lesbian, peeping, dildo),
        // pick the cleanest upright perpendicular (74..90 deg) angle facing the target opening (-vInward).
        if(host.Profile!=null&&host.Profile.Effector!=ActionSource.Shaft)
        {
            int bIdx=0;float bScore=-1e9f;
            for(int i=0;i<cands.Count;i++)
            {
                Vector3 V=cands[i];
                float horizon=1f-Mathf.Abs(V.y);
                float openFace=Vector3.Dot(V,-vInward);
                float perpScore=1f-Mathf.Abs(Vector3.Dot(V,S));
                float steepPen=Mathf.Abs(V.y)>0.52f?90f:0f;
                float sc=openFace*85f+horizon*65f+perpScore*30f-steepPen;
                if(sc>bScore){bScore=sc;bIdx=i;}
            }
            int sIdx=(bIdx+3)%cands.Count;float sBest=-1e9f;
            for(int i=0;i<cands.Count;i++)
            {
                Vector3 V=cands[i];
                if(Vector3.Dot(V,cands[bIdx])<0.55f)
                {
                    float sc=(1f-Mathf.Abs(V.y))*65f+Vector3.Dot(V,-vInward)*40f-(Mathf.Abs(V.y)>0.55f?80f:0f);
                    if(sc>sBest){sBest=sc;sIdx=i;}
                }
            }
            Vector3 bDir=cands[bIdx];
            float aDeg=Mathf.Acos(Mathf.Clamp(Mathf.Abs(Vector3.Dot(bDir,S)),0f,1f))*Mathf.Rad2Deg;
            return new AutoCamResult{BestDir=bDir,SecondDir=cands[sIdx],AngleDeg=aDeg,VisPx=-1,NearEntryPx=-1,ShaftSpan=1f};
        }

        // Collect active male penis/scrotum renderers (VisibleBody=false so enabled renderers near shaft are penis meshes)
        Vector3 shaftMid=(vOrig+vEnd)*0.5f;
        var penisRends=new List<Renderer>();
        foreach(var slot in new[]{ActorSlot.Male,ActorSlot.Male1})
        {
            var actor=host.Actor(slot);if(actor==null)continue;
            foreach(var r in actor.GetComponentsInChildren<Renderer>(true))
            {
                if(r==null||!r.enabled||!r.gameObject.activeInHierarchy)continue;
                string rn=r.name.ToLowerInvariant();
                if(rn.Contains("dan")||rn.Contains("son")||rn.Contains("tama")||rn.Contains("penis")||rn.Contains("cock")||r.bounds.SqrDistance(shaftMid)<0.09f)
                    penisRends.Add(r);
            }
        }

        var srt=new RenderTexture(SW,SH,24,RenderTextureFormat.ARGB32);
        var stex=new Texture2D(SW,SH,TextureFormat.RGB24,false);
        float[] scores=new float[cands.Count];
        int[] visArr=new int[cands.Count];
        int[] nearArr=new int[cands.Count];
        float[] spanArr=new float[cands.Count];
        try
        {
            for(int i=0;i<cands.Count;i++)
            {
                Vector3 V=cands[i];
                PositionAuditCameraDir(cam,focus,V,dist);
                Vector3 vpO=cam.WorldToViewportPoint(vOrig);
                Vector3 vpE=cam.WorldToViewportPoint(vEnd);
                Vector3 vpC=cam.WorldToViewportPoint(vCal);
                Vector2 pO=new Vector2(vpO.x*SW,vpO.y*SH);
                Vector2 pE=new Vector2(vpE.x*SW,vpE.y*SH);
                Vector2 pC=new Vector2(vpC.x*SW,vpC.y*SH);
                Vector2 seg=pE-pO;float segLen=Mathf.Max(1f,seg.magnitude);

                var pxWith=RenderSmallPixels(cam,srt,stex);
                for(int r=0;r<penisRends.Count;r++)penisRends[r].enabled=false;
                var pxWithout=RenderSmallPixels(cam,srt,stex);
                for(int r=0;r<penisRends.Count;r++)penisRends[r].enabled=true;

                int vis=0,nearE=0,shaftPx=0;
                float minT=1e9f,maxT=-1e9f;
                for(int y=0;y<SH;y++)
                {
                    int rowOff=y*SW;
                    for(int x=0;x<SW;x++)
                    {
                        var a=pxWith[rowOff+x];var b=pxWithout[rowOff+x];
                        int diff=Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b);
                        if(diff<=24)continue;
                        vis++;
                        float dxC=x-pC.x,dyC=y-pC.y;
                        if(dxC*dxC+dyC*dyC<=121f)nearE++;
                        float dxO=x-pO.x,dyO=y-pO.y;
                        float t=(dxO*seg.x+dyO*seg.y)/(segLen*segLen);
                        float perp=Mathf.Abs(dxO*seg.y-dyO*seg.x)/segLen;
                        if(perp<=7f&&t>=-0.25f&&t<=1.25f){shaftPx++;if(t<minT)minT=t;if(t>maxT)maxT=t;}
                    }
                }
                float span=shaftPx>=3?Mathf.Clamp01(maxT-minT):0f;
                float horizon=1f-Mathf.Abs(V.y);
                float perpScore=1f-Mathf.Abs(Vector3.Dot(V,S));
                float openTie=Mathf.Clamp(Vector3.Dot(V,-vInward),-0.35f,0.35f)*18f;
                float steepPen=Mathf.Abs(V.y)>0.58f?95f:0f;
                scores[i]=nearE*8.5f+span*160f+Mathf.Min(vis,260)*0.45f+horizon*78f+perpScore*22f+openTie-steepPen;
                visArr[i]=vis;nearArr[i]=nearE;spanArr[i]=span;
            }
        }
        finally
        {
            for(int r=0;r<penisRends.Count;r++)if(penisRends[r]!=null)penisRends[r].enabled=true;
            Destroy(stex);srt.Release();Destroy(srt);
        }

        int bestIdx=0;
        for(int i=1;i<cands.Count;i++)if(scores[i]>scores[bestIdx])bestIdx=i;
        int secIdx=(bestIdx+3)%cands.Count;
        float secBest=-1e9f;
        for(int i=0;i<cands.Count;i++)
        {
            if(Vector3.Dot(cands[i],cands[bestIdx])<0.55f&&scores[i]>secBest){secBest=scores[i];secIdx=i;}
        }
        Vector3 bd=cands[bestIdx];
        float angDeg=Mathf.Acos(Mathf.Clamp(Mathf.Abs(Vector3.Dot(bd,S)),0f,1f))*Mathf.Rad2Deg;
        return new AutoCamResult{BestDir=bd,SecondDir=cands[secIdx],AngleDeg=angDeg,VisPx=visArr[bestIdx],NearEntryPx=nearArr[bestIdx],ShaftSpan=spanArr[bestIdx]};
    }

    private static void PositionAuditCamera(Camera cam,Vector3 focus,Vector3 sideVec,Vector3 inward,float dist,string angle,float[] customCam=null)
    {
        Vector3 dir;
        float useDist=dist;
        if(angle=="side"&&customCam!=null&&customCam.Length>=4)
        {
            dir=(sideVec*customCam[0]-inward*customCam[1]+Vector3.up*customCam[2]).normalized;
            useDist=customCam[3];
        }
        else if(angle=="side")dir=(sideVec*0.86f-inward*0.42f+Vector3.up*0.18f).normalized;
        else if(angle=="front45")
        {
            float sign=(customCam!=null&&customCam.Length>=2&&customCam[1]<0f)?-1f:1f;
            dir=(sideVec*0.45f-inward*(0.84f*sign)+Vector3.up*(sign<0f?-0.20f:0.24f)).normalized;
        }
        else dir=(-sideVec*0.72f-inward*0.56f+Vector3.up*0.24f).normalized;
        if(dir.sqrMagnitude<1e-5f)dir=Vector3.right;
        cam.transform.position=focus+dir*useDist;
        cam.transform.LookAt(focus,Vector3.up);
        cam.fieldOfView=38f;
        cam.nearClipPlane=0.03f;
        cam.farClipPlane=50f;
    }

    private static float[] ParseCamArg(string spec,out string stripped)
    {
        stripped=spec;
        int idx=spec.IndexOf("@cam=",StringComparison.OrdinalIgnoreCase);
        if(idx<0)return null;
        stripped=spec.Substring(0,idx).Trim();
        string raw=spec.Substring(idx+5).Trim();
        var parts=raw.Split(',');
        if(parts.Length<4)return null;
        float[] v=new float[4];
        for(int i=0;i<4;i++)if(!float.TryParse(parts[i].Trim(),NumberStyles.Float,CultureInfo.InvariantCulture,out v[i]))return null;
        return v;
    }

    private static string Arr6(double[] a)
    {
        return string.Format(CultureInfo.InvariantCulture,"[{0:F4},{1:F4},{2:F4},{3:F4},{4:F4},{5:F4}]",a[0],a[1],a[2],a[3],a[4],a[5]);
    }

    private void StepParallelDrives(KoiSync.Core.SixAxisDrive rawD,KoiSync.Core.SixAxisDrive balD,KoiSync.Core.FeelTranslator balF,
        KoiSync.Core.SixAxisDrive intD,KoiSync.Core.FeelTranslator intF,double dt)
    {
        if(host.Sample==null||host.Measured==null||host.Profile==null)return;
        bool endpoints=host.Binding!=null&&host.Binding.UsesEndpoints(host.Profile);
        var mode=host.Profile.Mode;
        var reference=mode==KoiSync.MainGame.AxisMappingMode.Relative?host.Profile.Rotation:null;
        bool adapt=!endpoints;
        double player=KoiSync.Core.ShaftStrokeMapping.ReferenceLength;
        double stroke=KoiSync.Core.ShaftStrokeMapping.ReferenceStroke;
        var baseIn=adapt?KoiSync.Core.ShaftStrokeMapping.Adapt(host.Measured,player,host.Profile.GameLength,stroke,(KoiSync.Core.DriveMappingMode)mode,
            host.Sample.HasNativeTipContact,reference==null):host.Measured.Normalized;
        if(reference!=null)baseIn=reference.Apply(host.Measured,baseIn);
        var driveMode=(mode==KoiSync.MainGame.AxisMappingMode.Relative&&reference==null)?KoiSync.Core.DriveMappingMode.Soft:(KoiSync.Core.DriveMappingMode)mode;
        bool isNonShaft=host.Profile.Effector!=KoiSync.MainGame.ActionSource.Shaft&&host.Profile.Effector!=KoiSync.MainGame.ActionSource.AnimatedProp;

        rawD.PhysicalL0Minimum=adapt?Math.Max(0,1-player/stroke):0;
        rawD.Update(baseIn,dt,driveMode);

        var balIn=balF.Translate(baseIn,host.Measured,host.Sample,KoiSync.Core.FeelPreset.Balanced,isNonShaft,KoiSync.Core.NonShaftFeelMode.Foreplay,dt);
        balD.PhysicalL0Minimum=0;
        balD.Update(balIn,dt,driveMode);

        var intIn=intF.Translate(baseIn,host.Measured,host.Sample,KoiSync.Core.FeelPreset.Intense,isNonShaft,KoiSync.Core.NonShaftFeelMode.Foreplay,dt);
        intD.PhysicalL0Minimum=0;
        intD.Update(intIn,dt,driveMode);
    }

    private IEnumerator CameraScout(string specList)
    {
        batchActive=true;
        string sub="scout";
        string actualSpecs=specList;
        int pipe=specList.IndexOf('|');
        if(pipe>0){sub=specList.Substring(0,pipe).Trim();actualSpecs=specList.Substring(pipe+1).Trim();}
        string scoutRoot=Path.Combine(Path.Combine(Output,"visual-audit"),sub);
        Directory.CreateDirectory(scoutRoot);
        File.WriteAllText(Path.Combine(scoutRoot,"started.txt"),DateTime.UtcNow.ToString("o"));
        const int W=640,H=360;
        GameObject camGo=null;Camera auditCam=null;RenderTexture rt=null;Texture2D tex=null;
        var angles=new[]{
            new object[]{"A_sideR",  0.90f,  0.36f,  0.12f, 0.54f},
            new object[]{"B_sideL", -0.90f,  0.36f,  0.12f, 0.54f},
            new object[]{"C_lowR",   0.42f,  0.76f, -0.42f, 0.48f},
            new object[]{"D_lowL",  -0.42f,  0.76f, -0.42f, 0.48f},
            new object[]{"E_hiR",    0.48f,  0.76f,  0.38f, 0.52f},
            new object[]{"F_hiL",   -0.48f,  0.76f,  0.38f, 0.52f},
            new object[]{"G_frontL", 0.12f,  0.95f, -0.15f, 0.45f},
            new object[]{"H_rearR",  0.65f, -0.58f,  0.32f, 0.54f}
        };
        try
        {
            yield return new WaitForSecondsRealtime(1.5f);
            Set(host,"showWindow",false);Set(host,"showChannels",false);
            camGo=new GameObject("KoiSyncScoutCamera");
            auditCam=camGo.AddComponent<Camera>();auditCam.enabled=false;
            rt=new RenderTexture(W,H,24,RenderTextureFormat.ARGB32);
            tex=new Texture2D(W,H,TextureFormat.RGB24,false);

            var specs=actualSpecs.Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries);
            foreach(var rawSpec in specs)
            {
                string spec=rawSpec.Trim();if(spec.Length==0)continue;
                string keyPart=spec;string stage="SLoop";
                int colon=spec.IndexOf(':');
                if(colon>=0){keyPart=spec.Substring(0,colon).Trim();stage=spec.Substring(colon+1).Trim();}
                bool flipI=keyPart.EndsWith("@flip",StringComparison.OrdinalIgnoreCase);
                if(flipI)keyPart=keyPart.Substring(0,keyPart.Length-5);
                bool isAnal=keyPart.EndsWith("@anal",StringComparison.OrdinalIgnoreCase);
                string lookupKey=isAnal?keyPart.Substring(0,keyPart.Length-5):keyPart;

                HSceneProc.AnimationListInfo info=null;bool isExtra=false;
                foreach(var row in CatalogueRows(false))if(Key(row)==lookupKey){info=row;isExtra=false;break;}
                if(info==null)foreach(var row in CatalogueRows(true))if(Key(row)==lookupKey){info=row;isExtra=true;break;}
                if(info==null)continue;

                host.SetRecording(false);host.Flags.isAnalPlay=false;
                if(isExtra)
                {
                    foreach(var point in Resources.FindObjectsOfTypeAll<H.HPointData>())
                    {
                        if(!point.gameObject.scene.IsValid()||!point.gameObject.scene.isLoaded)continue;
                        bool found=false;foreach(var category in info.lstCategory)if(Array.IndexOf(point.category,category.category)>=0)
                        {typeof(HSceneProc).GetMethod("ChangeCategory",F).Invoke(host.Scene,new object[]{point,category.category});found=true;break;}
                        if(found)break;
                    }
                    yield return new WaitForSecondsRealtime(0.8f);
                }
                if(isExtra)SelectExtra(info);
                else
                {
                    host.SelectAction(info);
                    if(host.Flags.nowAnimationInfo!=null&&host.Flags.nowAnimationInfo.mode==info.mode&&host.Flags.nowAnimationInfo.id!=info.id)
                        typeof(HSceneProc).GetMethod("ChangeAnimator",F).Invoke(host.Scene,new object[]{info,false});
                }
                float waitUntil=Time.realtimeSinceStartup+6f;
                while(Time.realtimeSinceStartup<waitUntil&&(host.Profile==null||host.Profile.Key!=lookupKey))yield return null;
                yield return new WaitForSecondsRealtime(0.5f);
                PrepareActorsForVisualAudit();
                if(info.mode.ToString().StartsWith("houshi",StringComparison.Ordinal)){host.Flags.click=(HFlag.ClickKind)5;yield return new WaitForSecondsRealtime(0.5f);}
                if(isAnal)host.Flags.isAnalPlay=true;
                if(info.mode.ToString().StartsWith("sonyu",StringComparison.Ordinal))
                {if(host.Flags.isInsertOK!=null&&host.Flags.isInsertOK.Length>0)host.Flags.isInsertOK[0]=true;host.Flags.isAnalInsertOK=true;host.Flags.click=isAnal?HFlag.ClickKind.insert_anal:HFlag.ClickKind.insert;yield return new WaitForSecondsRealtime(1.0f);}
                PrepareActorsForVisualAudit();

                var targetActor=host.Actor(ActorSlot.Female0);
                if(targetActor!=null&&targetActor.animBody!=null&&targetActor.animBody.HasState(0,Animator.StringToHash(stage)))
                    Run(new[]{"state",stage});
                float settle=Time.realtimeSinceStartup+0.9f;
                double scanMin=1e9,scanMax=-1e9;
                while(Time.realtimeSinceStartup<settle)
                {
                    KeepInput(stage);yield return null;
                    if(host.Measured!=null){double d=host.Measured.TargetDepth;if(d<scanMin)scanMin=d;if(d>scanMax)scanMax=d;}
                }
                // Wait up to 1.2s for a mid-pullout phase so the penis shaft + interaction opening are both maximally visible
                double pullThresh=scanMin+Math.Max(0.005,(scanMax-scanMin)*0.25);
                float pullUntil=Time.realtimeSinceStartup+1.2f;
                while(Time.realtimeSinceStartup<pullUntil&&host.Measured!=null&&host.Measured.TargetDepth>pullThresh){KeepInput(stage);yield return null;}
                PrepareActorsForVisualAudit();
                yield return new WaitForEndOfFrame();

                var mainCam=Camera.main;
                if(mainCam==null||host.Sample==null||host.Measured==null)continue;
                var preview=Read(host,"preview") as GamePreview;
                if(preview!=null)preview.Hide();

                auditCam.CopyFrom(mainCam);auditCam.enabled=false;
                auditCam.clearFlags=CameraClearFlags.SolidColor;
                auditCam.backgroundColor=new Color(0.14f,0.16f,0.20f,1f);
                auditCam.cullingMask&=~((1<<11)|(1<<12));

                var s=host.Sample;
                Vector3 vOrig=ToV3(s.Origin),vEnd=ToV3(s.End),vCal=ToV3(s.Target);
                Vector3 vInward=ToV3(s.TargetReferenceFrame.Y).normalized;
                Vector3 vRight=ToV3(s.TargetReferenceFrame.X).normalized;
                Vector3 axis=vEnd-vOrig;
                Vector3 cand=axis.sqrMagnitude>1e-5f?Vector3.Cross(axis.normalized,Vector3.up):vRight;
                if(cand.magnitude<0.25f)cand=vRight;
                if(cand.sqrMagnitude<1e-5f)cand=Vector3.right;
                Vector3 sideVec=cand.normalized;
                Vector3 focus=Vector3.Lerp(vCal,(vOrig+vEnd)*0.5f,0.25f);

                string poseId=(isAnal?lookupKey+"_anal":lookupKey)+"__"+stage;
                string poseDir=Path.Combine(scoutRoot,poseId);
                Directory.CreateDirectory(poseDir);
                var metaSb=new StringBuilder();
                foreach(var ang in angles)
                {
                    string tag=(string)ang[0];
                    float[] cv=new[]{(float)ang[1],flipI?-(float)ang[2]:(float)ang[2],(float)ang[3],(float)ang[4]};
                    PositionAuditCamera(auditCam,focus,sideVec,vInward,cv[3],"side",cv);
                    RenderAuditView(auditCam,rt,tex,Path.Combine(poseDir,tag+".png"));
                    metaSb.AppendFormat(CultureInfo.InvariantCulture,"{{\"tag\":\"{0}\",\"cam\":\"{1:F2},{2:F2},{3:F2},{4:F2}\",\"orig\":{5},\"end\":{6},\"cal\":{7}}}\n",
                        tag,cv[0],cv[1],cv[2],cv[3],ProjJson(auditCam,vOrig,W,H),ProjJson(auditCam,vEnd,W,H),ProjJson(auditCam,vCal,W,H));
                }
                File.WriteAllText(Path.Combine(poseDir,"angles.jsonl"),metaSb.ToString(),Encoding.UTF8);
                host.Flags.isAnalPlay=false;
            }
            File.WriteAllText(Path.Combine(scoutRoot,"complete.txt"),DateTime.UtcNow.ToString("o"));
        }
        finally
        {
            if(tex!=null)Destroy(tex);
            if(rt!=null){rt.Release();Destroy(rt);}
            if(camGo!=null)Destroy(camGo);
            batchActive=false;
        }
    }

    private struct AuditWorkItem
    {
        public string Spec;
        public string LookupKey;
        public string UniqueKey;
        public HSceneProc.AnimationListInfo Info;
        public bool IsExtra;
        public bool IsAnal;
        public string[] Stages;
        public float[] CustomCam;
    }

    private List<AuditWorkItem> ExpandAuditItems(string actualSpecs,string auditRoot,ref int framesPerStage)
    {
        var items=new List<AuditWorkItem>();
        var seenKeys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var specs=actualSpecs.Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries);
        foreach(var rawSpec in specs)
        {
            string spec=rawSpec.Trim();if(spec.Length==0)continue;
            if(spec.StartsWith("@frames=",StringComparison.OrdinalIgnoreCase))
            {
                int fv;if(int.TryParse(spec.Substring(8).Trim(),out fv)&&fv>=8&&fv<=60)framesPerStage=fv;
                continue;
            }
            if(spec.StartsWith("@batch:",StringComparison.OrdinalIgnoreCase))
            {
                var bp=spec.Substring(7).Split(':');
                string grp=bp[0].Trim().ToLowerInvariant();
                int start=bp.Length>1?int.Parse(bp[1].Trim()):0;
                int count=bp.Length>2?int.Parse(bp[2].Trim()):999;
                var pool=new List<KeyValuePair<HSceneProc.AnimationListInfo,bool>>();
                if(grp=="all-peeping")
                {
                    try{typeof(HSceneProc).GetMethod("CreateAllAnimationList",F).Invoke(host.Scene,null);}catch{}
                }
                bool wantNative=(grp.StartsWith("native-")||grp.StartsWith("all-"));
                bool wantKplug=(grp.StartsWith("kplug-")||grp.StartsWith("all-"))&&grp!="all-aibu"&&grp!="all-peeping";
                foreach(var extraFlag in new[]{false,true})
                {
                    if(!extraFlag&&!wantNative)continue;
                    if(extraFlag&&!wantKplug)continue;
                    foreach(var row in CatalogueRows(extraFlag))
                    {
                        string m=row.mode.ToString();
                        bool match=
                            (grp=="native-sonyu"&&m=="sonyu")||
                            (grp=="native-houshi"&&m=="houshi")||
                            (grp=="kplug-sonyu"&&m=="sonyu")||
                            (grp=="kplug-houshi"&&m=="houshi")||
                            (grp=="all-3p"&&(m=="sonyu3P"||m=="houshi3P"))||
                            (grp=="all-mmf"&&(m=="sonyu3PMMF"||m=="houshi3PMMF"))||
                            (grp=="all-aibu"&&m=="aibu")||
                            (grp=="all-masturbation"&&m=="masturbation")||
                            (grp=="all-lesbian"&&m=="lesbian")||
                            (grp=="all-peeping"&&m=="peeping")||
                            (grp=="kplug-nonshaft-pair"&&m=="houshi"&&host.Catalogue.Resolve(m,row.id,row.nameAnimation,false,KoiSync.MainGame.Plugin.ResourceIdentity(row)).Effector!=ActionSource.Shaft);
                        if(!match)continue;
                        bool compat=true;
                        if(grp!="all-peeping")
                        {
                            try{compat=(bool)host.GetType().GetMethod("CanSelect",F).Invoke(host,new object[]{row});}catch{}
                        }
                        if(compat)pool.Add(new KeyValuePair<HSceneProc.AnimationListInfo,bool>(row,extraFlag));
                    }
                }
                int end=Math.Min(pool.Count,start+count);
                for(int idx=start;idx<end;idx++)
                {
                    var kv=pool[idx];
                    string lk=Key(kv.Key);
                    string uk=lk;
                    int suffix=2;
                    while(!seenKeys.Add(uk)){uk=lk+"_v"+suffix;suffix++;}
                    string[] stList=kv.Key.mode==HFlag.EMode.aibu?new[]{"aibu"}:
                        kv.Key.mode==HFlag.EMode.peeping?new[]{kv.Key.id==0?"Loop":"Idle"}:
                        new[]{"WLoop","SLoop"};
                    items.Add(new AuditWorkItem{
                        Spec=spec+"#"+idx,LookupKey=lk,UniqueKey=uk,Info=kv.Key,IsExtra=kv.Value,IsAnal=false,
                        Stages=stList,CustomCam=null
                    });
                }
                continue;
            }
            string specNoCam;
            float[] customCam=ParseCamArg(spec,out specNoCam);
            string keyPart=specNoCam;string stagesPart="WLoop,SLoop";
            int colon=specNoCam.IndexOf(':');
            if(colon>=0){keyPart=specNoCam.Substring(0,colon).Trim();stagesPart=specNoCam.Substring(colon+1).Trim();}
            bool isAnal=keyPart.EndsWith("@anal",StringComparison.OrdinalIgnoreCase);
            string lookupKey=isAnal?keyPart.Substring(0,keyPart.Length-5):keyPart;
            string[] stages=stagesPart.Split(new[]{','},StringSplitOptions.RemoveEmptyEntries);

            HSceneProc.AnimationListInfo info=null;bool isExtra=false;
            foreach(var row in CatalogueRows(false))if(Key(row)==lookupKey){info=row;isExtra=false;break;}
            if(info==null)foreach(var row in CatalogueRows(true))if(Key(row)==lookupKey){info=row;isExtra=true;break;}
            if(info==null){File.AppendAllText(Path.Combine(auditRoot,"errors.txt"),"NOT_FOUND: "+spec+"\n");continue;}
            string uniq=isAnal?lookupKey+"_anal":lookupKey;
            seenKeys.Add(uniq);
            items.Add(new AuditWorkItem{
                Spec=spec,LookupKey=lookupKey,UniqueKey=uniq,Info=info,IsExtra=isExtra,IsAnal=isAnal,Stages=stages,CustomCam=customCam
            });
        }
        return items;
    }

    private IEnumerator VisualAudit(string specList)
    {
        batchActive=true;
        string sub="default";
        string actualSpecs=specList;
        int pipe=specList.IndexOf('|');
        if(pipe>0){sub=specList.Substring(0,pipe).Trim();actualSpecs=specList.Substring(pipe+1).Trim();}
        string auditRoot=Path.Combine(Path.Combine(Output,"visual-audit"),sub);
        Directory.CreateDirectory(auditRoot);
        File.WriteAllText(Path.Combine(auditRoot,"started.txt"),DateTime.UtcNow.ToString("o"));
        const int W=640,H=360;
        int FramesPerStage=24;
        GameObject camGo=null;Camera auditCam=null;RenderTexture rt=null;Texture2D tex=null;
        var summary=new StringBuilder("spec\tkey\tname\tstage\tframes\tbound\tpreviewLoaded\tcontactMeanMm\tcontactMaxMm\tdepthMinMm\tdepthMaxMm\tmodelLenMm\tmatMean\tengMin\tl0Min\tl0Max\tfreqHz\tentrySource\n");
        try
        {
            yield return new WaitForSecondsRealtime(1.5f);
            Set(host,"showWindow",false);
            Set(host,"showChannels",false);
            var pe=Read(host,"previewEnabled");if(pe!=null)Set(pe,"Value",true);
            var xr=Read(host,"xray");if(xr!=null)Set(xr,"Value",true);
            var sdp=Read(host,"showDevicePose");if(sdp!=null)Set(sdp,"Value",false);

            camGo=new GameObject("KoiSyncVisualAuditCamera");
            auditCam=camGo.AddComponent<Camera>();
            auditCam.enabled=false;
            rt=new RenderTexture(W,H,24,RenderTextureFormat.ARGB32);
            tex=new Texture2D(W,H,TextureFormat.RGB24,false);

            int currentCategory=-1;
            var items=ExpandAuditItems(actualSpecs,auditRoot,ref FramesPerStage);
            foreach(var item in items)
            {
                string spec=item.Spec;
                float[] customCam=item.CustomCam;
                bool isAnal=item.IsAnal;
                string lookupKey=item.LookupKey;
                string uniqueKey=item.UniqueKey;
                string[] stages=item.Stages;
                HSceneProc.AnimationListInfo info=item.Info;
                bool isExtra=item.IsExtra;

                host.SetRecording(false);
                host.Flags.isAnalPlay=false;
                bool hasCurrentCat=false;
                if(info.lstCategory!=null&&currentCategory>=0)
                    foreach(var c in info.lstCategory)if(c.category==currentCategory){hasCurrentCat=true;break;}
                bool hasFloorCat=false;
                if(info.lstCategory!=null)
                    foreach(var c in info.lstCategory)if(c.category==0){hasFloorCat=true;break;}
                if(isExtra||(!hasCurrentCat&&!(currentCategory<0&&hasFloorCat)))
                {
                    try
                    {
                        foreach(var point in Resources.FindObjectsOfTypeAll<H.HPointData>())
                        {
                            if(!point.gameObject.scene.IsValid()||!point.gameObject.scene.isLoaded)continue;
                            bool found=false;
                            foreach(var category in info.lstCategory)if(Array.IndexOf(point.category,category.category)>=0)
                            {
                                typeof(HSceneProc).GetMethod("ChangeCategory",F).Invoke(host.Scene,new object[]{point,category.category});
                                currentCategory=category.category;found=true;break;
                            }
                            if(found)break;
                        }
                    }catch(Exception ex){File.AppendAllText(Path.Combine(auditRoot,"errors.txt"),"CAT_ERR "+uniqueKey+": "+ex.Message+"\n");}
                    yield return new WaitForSecondsRealtime(0.7f);
                }
                else if(currentCategory<0&&hasFloorCat)currentCategory=0;

                bool selOk=true;
                try
                {
                    if(info.mode==HFlag.EMode.peeping)
                    {
                        host.Flags.nowAnimationInfo=new HSceneProc.AnimationListInfo{mode=HFlag.EMode.none};
                        typeof(HSceneProc).GetMethod("ChangeAnimator",F).Invoke(host.Scene,new object[]{info,false});
                    }
                    else if(isExtra)SelectExtra(info);
                    else
                    {
                        host.SelectAction(info);
                        if(host.Flags.nowAnimationInfo!=null&&host.Flags.nowAnimationInfo.mode==info.mode&&host.Flags.nowAnimationInfo.id!=info.id)
                        {
                            typeof(HSceneProc).GetMethod("ChangeAnimator",F).Invoke(host.Scene,new object[]{info,false});
                        }
                    }
                }
                catch(Exception ex){selOk=false;File.AppendAllText(Path.Combine(auditRoot,"errors.txt"),"SEL_ERR "+uniqueKey+": "+ex.Message+"\n");}
                if(!selOk)continue;
                float waitUntil=Time.realtimeSinceStartup+6f;
                while(Time.realtimeSinceStartup<waitUntil&&(host.Profile==null||host.Profile.Key!=lookupKey))yield return null;
                yield return new WaitForSecondsRealtime(0.6f);
                PrepareActorsForVisualAudit();

                bool isAibu=info.mode==HFlag.EMode.aibu;
                if(info.mode.ToString().StartsWith("houshi",StringComparison.Ordinal))
                {host.Flags.click=(HFlag.ClickKind)5;yield return new WaitForSecondsRealtime(0.6f);}
                if(isAnal)host.Flags.isAnalPlay=true;
                if(info.mode.ToString().StartsWith("sonyu",StringComparison.Ordinal))
                {
                    if(host.Flags.isInsertOK!=null&&host.Flags.isInsertOK.Length>0)host.Flags.isInsertOK[0]=true;
                    host.Flags.isAnalInsertOK=true;
                    host.Flags.click=isAnal?HFlag.ClickKind.insert_anal:HFlag.ClickKind.insert;
                    yield return new WaitForSecondsRealtime(1.2f);
                }
                if(isAibu){AttachNativeItem();yield return new WaitForSecondsRealtime(0.6f);}
                PrepareActorsForVisualAudit();

                foreach(var rawStage in stages)
                {
                    string stage=rawStage.Trim();if(stage.Length==0)continue;
                    var targetActor=host.Actor(ActorSlot.Female0);
                    if(!isAibu)
                    {
                        if(targetActor==null||targetActor.animBody==null||!targetActor.animBody.HasState(0,Animator.StringToHash(stage)))
                        {File.AppendAllText(Path.Combine(auditRoot,"errors.txt"),"NO_STAGE: "+spec+" -> "+stage+"\n");continue;}
                        Run(new[]{"state",stage});
                    }

                    var rawDrive=new KoiSync.Core.SixAxisDrive{SmoothingSeconds=0.025,MaximumChangePerSecond=2.0};
                    var balDrive=new KoiSync.Core.SixAxisDrive{SmoothingSeconds=0.025,MaximumChangePerSecond=4.2};
                    var intDrive=new KoiSync.Core.SixAxisDrive{SmoothingSeconds=0.025,MaximumChangePerSecond=4.85};
                    var balFeel=new KoiSync.Core.FeelTranslator();
                    var intFeel=new KoiSync.Core.FeelTranslator();

                    double scanMin=1e9,scanMax=-1e9;
                    float settleEnd=Time.realtimeSinceStartup+1.5f;
                    while(Time.realtimeSinceStartup<settleEnd)
                    {
                        if(isAibu){if(host.Binding==null)AttachNativeItem();KeepItemInput();host.Flags.xy[2]=new Vector2(.5f,.5f+.4f*Mathf.Sin((settleEnd-Time.realtimeSinceStartup)*Mathf.PI*2f));}
                        else KeepInput(stage);
                        yield return null;
                        StepParallelDrives(rawDrive,balDrive,balFeel,intDrive,intFeel,Math.Max(0.005,Time.deltaTime));
                        if(host.Measured!=null){double d=host.Measured.TargetDepth;if(d<scanMin)scanMin=d;if(d>scanMax)scanMax=d;}
                    }
                    // Wait up to 0.9s for a mid-pullout moment so GPU auto-camera sees the exposed penis shaft clearly
                    if(!isAibu&&scanMax>scanMin+0.003)
                    {
                        double pullThresh=scanMin+(scanMax-scanMin)*0.30;
                        float pullUntil=Time.realtimeSinceStartup+0.9f;
                        while(Time.realtimeSinceStartup<pullUntil&&host.Measured!=null&&host.Measured.TargetDepth>pullThresh)
                        {
                            KeepInput(stage);yield return null;
                            StepParallelDrives(rawDrive,balDrive,balFeel,intDrive,intFeel,Math.Max(0.005,Time.deltaTime));
                        }
                    }
                    PrepareActorsForVisualAudit();

                    string caseId=uniqueKey+"__"+stage;
                    string caseDir=Path.Combine(auditRoot,caseId);
                    Directory.CreateDirectory(caseDir);

                    double durObj=0.0;try{durObj=Convert.ToDouble(Read(host,"phaseDuration"));}catch{}
                    double cycleSec=isAibu?1.0:(durObj>0.25&&durObj<4.0?durObj:1.2);
                    float stepSec=Mathf.Clamp((float)(cycleSec*1.15/FramesPerStage),0.035f,0.11f);

                    var preview=Read(host,"preview") as GamePreview;
                    Vector3 lockedSide=Vector3.right,lockedFocus=Vector3.zero,lockedInward=Vector3.forward;
                    Vector3 lockedDir=Vector3.right,lockedSecondDir=Vector3.forward;
                    AutoCamResult autoCam=new AutoCamResult{AngleDeg=90f,VisPx=-1,NearEntryPx=-1,ShaftSpan=0f};
                    bool rigLocked=false;
                    // Wider framing (0.68m) shows both genital interaction AND clear horizontal/vertical posture context
                    float dist=(host.Profile!=null&&(host.Profile.Contact==KoiSync.Core.NativeTarget.VAGINAL||host.Profile.Contact==KoiSync.Core.NativeTarget.ANAL))?0.68f:0.62f;

                    var jsonl=new StringBuilder();
                    double sumCd=0,maxCd=0,minDepth=1e9,maxDepth=-1e9,sumMat=0,minEng=1e9,minL0=1e9,maxL0=-1e9,lastModelLen=0;
                    int captured=0;string entrySource="";bool prevLoaded=false;
                    float startAnimTime=Time.time;
                    float nextTargetAnimTime=Time.time;

                    for(int fi=0;fi<FramesPerStage;fi++)
                    {
                        if(fi>0)
                        {
                            do
                            {
                                if(isAibu){if(host.Binding==null)AttachNativeItem();KeepItemInput();host.Flags.xy[2]=new Vector2(.5f,.5f+.4f*Mathf.Sin((Time.time-startAnimTime)*Mathf.PI*2f));}
                                else KeepInput(stage);
                                yield return null;
                                StepParallelDrives(rawDrive,balDrive,balFeel,intDrive,intFeel,Math.Max(0.005,Time.deltaTime));
                            }while(Time.time<nextTargetAnimTime);
                        }
                        nextTargetAnimTime=Time.time+stepSec;
                        if(isAibu){KeepItemInput();host.Flags.xy[2]=new Vector2(.5f,.5f+.4f*Mathf.Sin((Time.time-startAnimTime)*Mathf.PI*2f));}
                        else KeepInput(stage);

                        yield return new WaitForEndOfFrame();
                        float simTime=Time.time-startAnimTime;
                        var mainCam=Camera.main;
                        if(mainCam==null||host.Sample==null||host.Measured==null||host.Binding==null)continue;

                        auditCam.CopyFrom(mainCam);
                        auditCam.enabled=false;
                        auditCam.clearFlags=CameraClearFlags.SolidColor;
                        auditCam.backgroundColor=new Color(0.14f,0.16f,0.20f,1f);
                        // Cull map/background layers (11=Map, 12=MapNoHit) while preserving Chara(10) + items
                        auditCam.cullingMask&=~((1<<11)|(1<<12));

                        var s=host.Sample;var m=host.Measured;
                        Vector3 vOrig=ToV3(s.Origin),vEnd=ToV3(s.End),vCal=ToV3(s.Target);
                        Vector3 vInward=ToV3(s.TargetReferenceFrame.Y).normalized;
                        Vector3 vRight=ToV3(s.TargetReferenceFrame.X).normalized;
                        Vector3 vObs=vCal;
                        var entryObj=Read(host.Binding,"Entry");
                        if(entryObj!=null)
                        {
                            var obsF=(KoiSync.Core.AlignmentFrame)Read(entryObj,"Observed");
                            vObs=ToV3(obsF.Origin);
                            entrySource=Convert.ToString(Read(entryObj,"Source"))??"";
                        }
                        var devPose=Read(host,"devicePose") as KoiSync.Core.ContactPreviewPose;
                        var geomPose=Read(host,"geometryPose") as KoiSync.Core.ContactPreviewPose;
                        Vector3 vOut=devPose!=null?ToV3(devPose.Sleeve.Origin):vCal;

                        if(!rigLocked)
                        {
                            Vector3 axis=(vEnd-vOrig);
                            Vector3 cand=axis.sqrMagnitude>1e-5f?Vector3.Cross(axis.normalized,Vector3.up):vRight;
                            if(cand.magnitude<0.25f)cand=vRight;
                            if(cand.sqrMagnitude<1e-5f)cand=Vector3.right;
                            lockedSide=cand.normalized;
                            lockedInward=vInward.sqrMagnitude>1e-5f?vInward:Vector3.forward;
                            lockedFocus=Vector3.Lerp(vCal,(vOrig+vEnd)*0.5f,0.35f);
                            if(customCam==null)
                            {
                                if(preview!=null)preview.Hide();
                                autoCam=AutoSelectBestCamera(auditCam,lockedFocus,vOrig,vEnd,vCal,lockedInward,dist);
                                lockedDir=autoCam.BestDir;
                                lockedSecondDir=autoCam.SecondDir;
                            }
                            rigLocked=true;
                        }

                        // 1) Primary verified unobstructed perpendicular camera (tripod-locked, zero shake!)
                        if(customCam!=null)PositionAuditCamera(auditCam,lockedFocus,lockedSide,lockedInward,dist,"side",customCam);
                        else PositionAuditCameraDir(auditCam,lockedFocus,lockedDir,dist);
                        if(preview!=null)preview.Hide();
                        string cleanPath=Path.Combine(caseDir,string.Format("f{0:D2}_side_clean.png",fi));
                        RenderAuditView(auditCam,rt,tex,cleanPath);

                        // 2) Primary camera + 3D X-Ray sleeve & rod on exact same frame
                        if(preview!=null&&geomPose!=null)preview.Display(geomPose,true,true,"audit");
                        prevLoaded=preview!=null&&preview.Loaded;
                        string xrayPath=Path.Combine(caseDir,string.Format("f{0:D2}_side_xray.png",fi));
                        RenderAuditView(auditCam,rt,tex,xrayPath);

                        string pSideObs=ProjJson(auditCam,vObs,W,H);
                        string pSideCal=ProjJson(auditCam,vCal,W,H);
                        string pSideOrig=ProjJson(auditCam,vOrig,W,H);
                        string pSideEnd=ProjJson(auditCam,vEnd,W,H);
                        string pSideInw=ProjJson(auditCam,vCal+vInward*0.07f,W,H);
                        string pSideOut=ProjJson(auditCam,vOut,W,H);

                        // 3) Secondary perpendicular ortho X-Ray on ~6-8 key frames (also tripod-locked, zero dot-collapse!)
                        string pF45Obs="null",pF45Cal="null",pF45Orig="null",pF45End="null",pF45Inw="null";
                        bool hasFront45=(fi%Math.Max(2,FramesPerStage/6)==0);
                        if(hasFront45)
                        {
                            if(customCam!=null)PositionAuditCamera(auditCam,lockedFocus,lockedSide,lockedInward,dist*0.95f,"front45",customCam);
                            else PositionAuditCameraDir(auditCam,lockedFocus,lockedSecondDir,dist*0.96f);
                            string f45Path=Path.Combine(caseDir,string.Format("f{0:D2}_front45_xray.png",fi));
                            RenderAuditView(auditCam,rt,tex,f45Path);
                            pF45Obs=ProjJson(auditCam,vObs,W,H);
                            pF45Cal=ProjJson(auditCam,vCal,W,H);
                            pF45Orig=ProjJson(auditCam,vOrig,W,H);
                            pF45End=ProjJson(auditCam,vEnd,W,H);
                            pF45Inw=ProjJson(auditCam,vCal+vInward*0.07f,W,H);
                        }

                        double cdMm=m.ContactDistance*1000.0;
                        double depthMm=m.TargetDepth*1000.0;
                        double lenMm=m.Length*1000.0;
                        double l0=balDrive.Values[0];
                        double prog=0;
                        if(targetActor!=null&&targetActor.animBody!=null)prog=targetActor.animBody.GetCurrentAnimatorStateInfo(0).normalizedTime;

                        sumCd+=cdMm;if(cdMm>maxCd)maxCd=cdMm;
                        if(depthMm<minDepth)minDepth=depthMm;if(depthMm>maxDepth)maxDepth=depthMm;
                        sumMat+=m.MaterialInsertion;
                        if(m.Engagement<minEng)minEng=m.Engagement;
                        if(l0<minL0)minL0=l0;if(l0>maxL0)maxL0=l0;
                        lastModelLen=lenMm;captured++;

                        jsonl.AppendFormat(CultureInfo.InvariantCulture,
                            "{{\"f\":{0},\"t\":{1:F4},\"prog\":{2:F4},\"dur\":{3:F4},\"eng\":{4:F3},\"cdMm\":{5:F2},\"depthMm\":{6:F2},\"lenMm\":{7:F1},\"mat\":{8:F3},\"tipBound\":{9},\"inL0\":{10:F4},\"ax\":{11},\"rawAx\":{12},\"balAx\":{13},\"intAx\":{14},\"side\":{{\"obs\":{15},\"cal\":{16},\"orig\":{17},\"end\":{18},\"inw\":{19},\"out\":{20}}},\"f45\":{21},\"entrySrc\":\"{22}\",\"prev\":{23}}}\n",
                            fi,simTime,prog,cycleSec,m.Engagement,cdMm,depthMm,lenMm,m.MaterialInsertion,
                            host.Binding.NativeEntryBound?"true":"false",rawDrive.Input[0],
                            Arr6(balDrive.Values),Arr6(rawDrive.Values),Arr6(balDrive.Values),Arr6(intDrive.Values),
                            pSideObs,pSideCal,pSideOrig,pSideEnd,pSideInw,pSideOut,
                            hasFront45?string.Format(CultureInfo.InvariantCulture,"{{\"obs\":{0},\"cal\":{1},\"orig\":{2},\"end\":{3},\"inw\":{4}}}",pF45Obs,pF45Cal,pF45Orig,pF45End,pF45Inw):"null",
                            JsonEsc(entrySource),prevLoaded?"true":"false");
                    }

                    File.WriteAllText(Path.Combine(caseDir,"telemetry.jsonl"),jsonl.ToString(),Encoding.UTF8);
                    File.WriteAllText(Path.Combine(caseDir,"meta.json"),string.Format(CultureInfo.InvariantCulture,
                        "{{\"spec\":\"{0}\",\"key\":\"{1}\",\"name\":\"{2}\",\"stage\":\"{3}\",\"contact\":\"{4}\",\"effector\":\"{5}\",\"frames\":{6},\"cycleSec\":{7:F4},\"previewLoaded\":{8},\"previewStatus\":\"{11}\",\"entrySource\":\"{9}\",\"stepSec\":{10:F4},\"camAngleDeg\":{12:F1},\"penisVisPx\":{13},\"nearEntryPx\":{14},\"shaftSpan\":{15:F2},\"nativeSetPlay\":{16},\"autoDir\":[{17:F3},{18:F3},{19:F3}]}}\n",
                        JsonEsc(spec),JsonEsc(uniqueKey),JsonEsc(info.nameAnimation),JsonEsc(stage),
                        host.Profile!=null?host.Profile.Contact.ToString():"",host.Profile!=null?host.Profile.Effector.ToString():"",
                        captured,cycleSec,prevLoaded?"true":"false",JsonEsc(entrySource),stepSec,JsonEsc(preview!=null?preview.Status:"null"),
                        autoCam.AngleDeg,autoCam.VisPx,autoCam.NearEntryPx,autoCam.ShaftSpan,lastNativeSetPlay?"true":"false",
                        lockedDir.x,lockedDir.y,lockedDir.z),Encoding.UTF8);

                    double meanCd=captured>0?sumCd/captured:-1;
                    double meanMat=captured>0?sumMat/captured:-1;
                    double freqHz=cycleSec>0.05?1.0/cycleSec:0;
                    summary.AppendFormat(CultureInfo.InvariantCulture,
                        "{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7:F2}\t{8:F2}\t{9:F2}\t{10:F2}\t{11:F1}\t{12:F3}\t{13:F2}\t{14:F3}\t{15:F3}\t{16:F2}\t{17}\n",
                        spec,isAnal?lookupKey+"_anal":lookupKey,info.nameAnimation,stage,captured,host.Binding!=null,prevLoaded,
                        meanCd,maxCd,captured>0?minDepth:0,captured>0?maxDepth:0,lastModelLen,meanMat,captured>0?minEng:0,
                        captured>0?minL0:0,captured>0?maxL0:0,freqHz,entrySource);
                    File.WriteAllText(Path.Combine(auditRoot,"summary.tsv"),summary.ToString(),Encoding.UTF8);
                }
                host.Flags.isAnalPlay=false;
            }
            File.WriteAllText(Path.Combine(auditRoot,"complete.txt"),DateTime.UtcNow.ToString("o"));
        }
        finally
        {
            if(tex!=null)Destroy(tex);
            if(rt!=null){rt.Release();Destroy(rt);}
            if(camGo!=null)Destroy(camGo);
            batchActive=false;
        }
    }
}
