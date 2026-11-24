using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using KoiSync.Core;
using KoiSync.Plugin;
using KoiSync.Wire;
using UnityEngine;

[BepInPlugin("local.koisync.studiochecks","KoiSync studio binding checks","0.1.0")]
[BepInDependency("local.koisync")]
public sealed class StudioBindingChecks:BaseUnityPlugin
{
    private readonly StringBuilder report=new StringBuilder();
    private int checks;
    private GameObject world;
    private readonly List<StudioObject> objects=new List<StudioObject>();
    private readonly ArrayList constraints=NodesConstraints.NodesConstraints._self._constraints;
    private bool ui;
    private void Awake(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"--koisync-studio-checks")>=0)StartCoroutine(Run());}
    private void Check(bool value,string message){if(!value)throw new Exception(message);checks++;report.AppendLine("PASS "+message);}
    private void Near(double a,double b,string message){Check(Math.Abs(a-b)<.0001,message+" ("+a.ToString("F6")+")");}
    private IEnumerator Run()
    {
        ui=Array.IndexOf(Environment.GetCommandLineArgs(),"--koisync-vr-checks")>=0||Array.IndexOf(Environment.GetCommandLineArgs(),"--koisync-ui-checks")>=0||Array.IndexOf(Environment.GetCommandLineArgs(),"--koisync-editor-checks")>=0;
        Application.runInBackground=true;yield return null;
        try
        {
            var host=UnityEngine.Object.FindObjectOfType<KoiSync.Plugin.Plugin>();Check(host!=null,"Plugin loaded in actual Unity runtime "+Application.unityVersion);
            host.StopAllCoroutines();host.enabled=false;Check(!host.Devices.Connected,"No device connection during binding tests");
            world=new GameObject("KoiSync synthetic binding fixtures");DontDestroyOnLoad(world);
            var male=Actor("Source",0,true);var target=Actor("Target",.03f,false);
            Transform entry=Node(male.Root,"k_f_dan_entry",new Vector3(.03f,0,0));Transform end=Node(male.Root,"k_f_dan_end",new Vector3(7,8,9));
            var entryLink=Link(target.Bones.Get("cf_J_Vagina_root"),entry);Link(target.Bones.Get("cf_j_hips"),end);
            var bp=male.Root.gameObject.AddComponent<KoiSyncBpFixture>();var points=new List<KoiSyncBpPoint>();
            for(int i=0;i<9;i++)points.Add(new KoiSyncBpPoint{transform=male.Root.Find("cm_J_dan"+(101+i)+"_00")});
            bp.danAgent=new KoiSyncBpAgent{m_danPoints=new KoiSyncBpPoints{danPoints=points}};male.Bp=bp;
            var adapter=Discover();Check(adapter.Sources.Count==1&&adapter.Sources[0].Bp,"Entry/end constraints form one BP group, not two groups");
            var source=adapter.Sources[0];var sample=source.Sample(1,1,false,NativeTarget.AUTO,true);
            Check(sample!=null&&sample.Native.Studio&&sample.ReferencePath.Length==9,"BP source reads all actual chain transforms");
            Near((sample.Native.End-sample.Native.Origin).Length,.1,"Actual chain endpoints determine reference length, not dan_end control");
            Near(NativeMotion.Solve(sample.Native).Normalized[0],.3,"BP entry offset is measured in the actual reference frame");
            var staleManualTarget=source.Sample(1,1,false,NativeTarget.ANAL,true);
            Check(staleManualTarget!=null&&staleManualTarget.Native.RotationBasis.StartsWith("VAGINAL:")&&staleManualTarget.TargetBone=="VAGINAL · 吸附目标 · "+entry.name,"An explicit penile entry binding overrides a stale global target profile and identifies the attached target");
            Near((staleManualTarget.Native.Target-StudioSource.V(entry.position)).Length,0,"The solver follows the object attached to the penile entry");
            Near(NativeMotion.Solve(staleManualTarget.Native).Normalized[0],.3,"A stale manual target cannot change bound BP insertion depth");
            bp.danTargetsValid=false;Check(source.Sample(1,1,false,NativeTarget.AUTO,true)==null,"Uninitialized BP controller cannot supply a valid group");bp.danTargetsValid=true;
            var before=NativeMotion.Solve(sample.Native);world.transform.position=new Vector3(5,-3,7);world.transform.rotation=Quaternion.Euler(17,33,-28);
            var after=NativeMotion.Solve(source.Sample(1,1,false,NativeTarget.AUTO,true).Native);
            for(int i=0;i<6;i++)Near(after.Normalized[i],before.Normalized[i],"Bound frame rigid transform invariance "+MotionPipeline.Axes[i]);
            world.transform.position=Vector3.zero;world.transform.rotation=Quaternion.identity;
            entryLink.enabled=false;Check(source.Sample(1,1,false,NativeTarget.AUTO,true)==null,"Disabling a binding invalidates it without waiting for discovery");entryLink.enabled=true;
            constraints.Remove(entryLink);Check(source.Sample(1,1,false,NativeTarget.AUTO,true)==null,"Removing a binding invalidates its cached group immediately");constraints.Add(entryLink);
            var unrelated=Actor("Unrelated",2,false);adapter=Discover();Check(adapter.Sources.Count==1,"Unbound actor does not enter the source list");
            var secondMale=Actor("Source",3,true);var secondTarget=Actor("Target",3.03f,false);
            Link(secondTarget.Bones.Get("cf_J_Vagina_root"),secondMale.Bones.Get("cf_j_hips"));
            adapter=Discover();Check(adapter.Sources.Count==2,"Two separate relationships remain separate selectable groups despite duplicate names");
            var native=adapter.Sources.Find(s=>s.Source==secondMale);Check(native!=null&&!native.Bp&&!native.IsCollider,"Native actor bindings use the character-chain path");
            Near(NativeMotion.Solve(native.Sample(1,1,false,NativeTarget.AUTO,true).Native).Normalized[0],.3,"Native studio chain uses the same length-normalized solver");
            var staleNativeTarget=native.Sample(1,1,false,NativeTarget.ANAL,true);Check(staleNativeTarget!=null&&staleNativeTarget.Native.RotationBasis.StartsWith("VAGINAL:")&&staleNativeTarget.TargetBone.StartsWith("VAGINAL · 吸附目标"),"A native penile chain also keeps its explicitly attached target");
            var prop=Object("Collider prop",false);var collider=prop.Root.gameObject.AddComponent<DynamicBoneCollider>();
            collider.m_Direction=DynamicBoneCollider.Direction.X;collider.m_Bound=DynamicBoneCollider.Bound.Outside;collider.m_Center=new Vector3(.05f,0,0);collider.m_Radius=.01f;collider.m_Height=.09f;prop.Colliders.Add(collider);
            var attachment=Link(male.Bones.Get("cm_J_dan100_00"),prop.Root);Prism(prop.Root,.1f);
            var physics=target.Root.gameObject.AddComponent<DynamicBone>();physics.m_Root=target.Bones.Get("cf_J_Vagina_root");physics.m_Colliders=new List<DynamicBoneCollider>{collider};
            adapter=Discover();var colliderSource=adapter.Sources.Find(s=>s.IsCollider);Check(colliderSource!=null&&colliderSource.Target==target,"J694-style collider follows attachment, target binding and actual DynamicBone reference");
            var colliderSample=colliderSource.Sample(1,1,false,NativeTarget.AUTO,true);Check(colliderSample!=null&&colliderSample.ColliderRadius>0,"Collider source supplies live geometry and a valid six-axis sample");
            Near((colliderSample.Native.End-colliderSample.Native.Origin).Length,.1,"Attached collider model length comes from its mesh tip");
            var modelRoot=new GameObject("Penis00").transform;modelRoot.SetParent(prop.Root,false);modelRoot.position=target.Bones.Get("cf_J_Vagina_root").position;
            var rootSample=colliderSource.Sample(1,1,false,NativeTarget.AUTO,true);
            Check(rootSample!=null&&rootSample.ReferenceBone.StartsWith("cm_J_dan100_00"),"Attachment bone is authoritative even when the model also has a different Penis00");
            Near(NativeMotion.Solve(rootSample.Native).Normalized[0],.3,"Root distance uses the attachment bone rather than the arbitrary internal model node");
            var rootTarget=target.Bones.Get("cf_J_Vagina_root");Vector3 rootTargetPosition=rootTarget.position;rootTarget.position=attachment.parentTransform.position;
            Near(NativeMotion.Solve(colliderSource.Sample(1,1,false,NativeTarget.AUTO,true).Native).Normalized[0],0,"Coincident attachment and target roots show L0 000");rootTarget.position=rootTargetPosition;
            collider.m_Height=.7f;Near(colliderSource.Sample(1,1,false,NativeTarget.AUTO,true).Native.End.X,.1,"Changing collider height cannot change actual mesh length");collider.m_Height=.09f;
            Near((rootSample.Native.ReferenceFrame.Origin-rootSample.Native.Origin).Length,0,"Studio blue frame is anchored to the selected model root");
            Near((rootSample.Native.TargetReferenceFrame.Origin-rootSample.Native.Target).Length,0,"Studio pink frame is anchored to the target root");
            var manualTarget=colliderSource.Sample(1,1,false,NativeTarget.ANAL,true);Check(manualTarget!=null&&manualTarget.TargetBone.StartsWith("ANAL"),"Manual target labels never claim the automatic binding node");
            modelRoot.name="unused-root";Destroy(modelRoot.gameObject);
            MotionVector capA,capB;double radius;var geometry=new NativeMotionSample();prop.Root.localScale=new Vector3(2,3,4);
            StudioSource.ColliderFrame(collider,false,geometry,out capA,out capB,out radius);
            Near((geometry.End-geometry.Origin).Length,.24,"Collider measurement matches nonuniform runtime scale");
            Vector3 particle=new Vector3((float)capB.X+(float)radius*.5f,(float)capB.Y,(float)capB.Z);collider.Collide(ref particle,0);
            Near(particle.x,geometry.End.X,"Sampled cap agrees with actual DynamicBoneCollider.Collide correction");
            var reverse=new NativeMotionSample();StudioSource.ColliderFrame(collider,true,reverse,out capA,out capB,out radius);
            Near((reverse.Origin-geometry.End).Length,0,"Reverse collider option swaps measured endpoints");prop.Root.localScale=Vector3.one;
            collider.m_Bound=DynamicBoneCollider.Bound.Inside;Check(colliderSource.Sample(1,1,false,NativeTarget.AUTO,true)==null,"Changing collider boundary mode invalidates an Outside source");collider.m_Bound=DynamicBoneCollider.Bound.Outside;
            attachment.enabled=false;Check(colliderSource.Sample(1,1,false,NativeTarget.AUTO,true)==null,"Detached collider does not continue driving its prior target");attachment.enabled=true;
            physics.m_Colliders.Remove(collider);Check(colliderSource.Sample(1,1,false,NativeTarget.AUTO,true)==null,"Removing target's collider reference invalidates the selected physics group");physics.m_Colliders.Add(collider);
            var helper=Object("Timeline helper",false);Link(helper.Root,male.Bones.Get("cf_j_hips"));
            Timeline.Timeline._self._interpolables[1]=new KoiSyncTimelineTrack{oci=helper.Control};Timeline.Timeline.isPlaying=true;
            TimelineState.Refresh();adapter=Discover();source=adapter.Sources.Find(s=>s.Bp);
            Check(source.Sample(1,1,false,NativeTarget.AUTO,true).Active,"Timeline helper track drives a source through incoming constraints");
            Timeline.Timeline.isPlaying=false;Check(!source.Sample(1,1,false,NativeTarget.AUTO,true).Active,"Timeline pause immediately stops the associated source");
            Timeline.Timeline._self._interpolables.Clear();Timeline.Timeline.isPlaying=true;
            Timeline.Timeline._self._interpolables[2]=new KoiSyncTimelineTrack{oci=unrelated.Control};TimelineState.Refresh();
            Check(!source.Sample(1,1,false,NativeTarget.AUTO,true).Active,"Unrelated Timeline playback cannot activate a bound group");
            Timeline.Timeline._self._interpolables[3]=new KoiSyncTimelineTrack{parameter=entryLink};TimelineState.Refresh();
            Check(source.Sample(1,1,false,NativeTarget.AUTO,true).Active,"Constraint parameter tracks work even without a character or item owner");
            Timeline.Timeline._self._interpolables[3].enabled=false;Check(!source.Sample(1,1,false,NativeTarget.AUTO,true).Active,"Disabling a Timeline track takes effect immediately");
            Timeline.Timeline._self._interpolables[3].enabled=true;Timeline.Timeline.isPlaying=true;
            var pipeline=host.Pipeline;var frame=new Snapshot{Session="studio-fixture",Context="CharaStudio",Scene="neutral",State="bound",Active=true};
            for(int i=0;i<40;i++){entry.localPosition=new Vector3(i%2==0?.02f:.04f,0,0);var c=source.Sample(1,1,false,NativeTarget.AUTO,true);frame.SampleTime+=.02;frame.Sequence++;pipeline.Update(frame,c,0,StrokeAxis.Z);}
            Check(pipeline.NativeMode&&pipeline.Calibrated&&pipeline.ReferenceContext=="工作室","Studio bindings share automatic reference-range calibration");
            Check(pipeline.Arm(),"Calibrated playing group can be explicitly armed");
            Timeline.Timeline.isPlaying=false;frame.SampleTime+=.02;frame.Sequence++;pipeline.Update(frame,source.Sample(1,1,false,NativeTarget.AUTO,true),0,StrokeAxis.Z);
            Check(!pipeline.Armed&&!pipeline.HasLiveSample,"Pausing bound Timeline stops a previously armed pipeline");
            Timeline.Timeline.isPlaying=true;frame.SampleTime+=.02;frame.Sequence++;pipeline.Update(frame,source.Sample(1,1,false,NativeTarget.AUTO,true),0,StrokeAxis.Z);
            Check(!pipeline.Armed,"Resuming a bound scene does not re-arm output automatically");
            Check(!host.Devices.Connected,"No hardware touched by studio verification");
            var shapeNode=Node(target.Root,"Neutral shape fixture",Vector3.zero);
            var renderer=shapeNode.gameObject.AddComponent<SkinnedMeshRenderer>();
            var mesh=new Mesh{name="Neutral triangle"};mesh.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up};mesh.triangles=new[]{0,1,2};
            mesh.AddBlendShapeFrame("test.penetrate",100,new[]{Vector3.forward,Vector3.zero,Vector3.zero},new Vector3[3],new Vector3[3]);
            mesh.AddBlendShapeFrame("test.open",100,new[]{Vector3.zero,Vector3.forward,Vector3.zero},new Vector3[3],new Vector3[3]);renderer.sharedMesh=mesh;renderer.SetBlendShapeWeight(0,25);
            var duplicateNode=Node(target.Root,"Neutral shape fixture",Vector3.zero);var duplicate=duplicateNode.gameObject.AddComponent<SkinnedMeshRenderer>();duplicate.sharedMesh=mesh;duplicate.SetBlendShapeWeight(0,90);
            var experiment=host.Experiment;experiment.Zero.Value=0;experiment.Full.Value=100;
            sample=source.Sample(1,1,false,NativeTarget.AUTO,true);experiment.Update(source,sample,100);
            Check(experiment.Shapes.Count==4&&experiment.Selected==null,"Manual shape discovery lists both renderers without silently choosing a name");
            var chosen=experiment.Shapes.Find(s=>s.Renderer==renderer&&s.Index==0);
            Check(chosen.Id!=experiment.Shapes.Find(s=>s.Renderer==duplicate&&s.Index==0).Id,"Duplicate mesh/shape names retain separate renderer identities");
            experiment.Select(chosen);experiment.Update(source,sample,100.1f);Near(experiment.Weight,25,"Reads real Unity SkinnedMeshRenderer weight");Near(experiment.Absolute,.25,"Model weight maps to an explicit absolute ratio");
            renderer.SetBlendShapeWeight(0,80);experiment.AbsolutePreview=true;sample=source.Sample(1,1,false,NativeTarget.AUTO,true);experiment.Update(source,sample,100.2f);
            Near(experiment.Absolute,.8,"Independent runtime weight changes are sampled");Near(experiment.ObservedMax-experiment.ObservedMin,55,"Observation records the actual variation span");
            Check(sample.Native.ShapePreview&&sample.Native.ShapeL0==.8,"Explicit preview supplies only the selected shape channel");
            Near(renderer.GetBlendShapeWeight(0),80,"Observation leaves model weights unchanged");
            renderer.sharedMesh=UnityEngine.Object.Instantiate(mesh);sample=source.Sample(1,1,false,NativeTarget.AUTO,true);experiment.Update(source,sample,100.3f);
            Check(double.IsNaN(experiment.Absolute)&&sample.Native.ShapePreview&&double.IsNaN(sample.Native.ShapeL0),"Replacing mesh invalidates the prior shape without automatic fallback");
            experiment.Rescan();experiment.Update(source,source.Sample(1,1,false,NativeTarget.AUTO,true),101.4f);
            Check(experiment.Selected==chosen&&double.IsNaN(experiment.Absolute),"Rescan cannot silently rebind an invalid selection");
            experiment.Select(experiment.Shapes.Find(s=>s.Renderer==renderer&&s.Index==0));experiment.Update(source,source.Sample(1,1,false,NativeTarget.AUTO,true),101.5f);
            Check(Numbers.Finite(experiment.Absolute)&&!experiment.AbsolutePreview,"Manual replacement selection exits the previous preview mode");
            shapeNode.gameObject.SetActive(false);double unavailable;Check(!experiment.Selected.Read(out unavailable),"Inactive shape object cannot keep supplying a valid weight");shapeNode.gameObject.SetActive(true);
            experiment.Update(colliderSource,colliderSource.Sample(1,1,false,NativeTarget.AUTO,true),102);
            Check(experiment.Selected==null&&!experiment.AbsolutePreview,"Switching binding groups clears session-local shape selection");
            renderer.SetBlendShapeWeight(0,25);experiment.Zero.Value=0;experiment.Full.Value=100;
            var cameraNode=new GameObject("Synthetic focus camera");cameraNode.transform.SetParent(world.transform,false);var focusCamera=cameraNode.AddComponent<Camera>();focusCamera.fieldOfView=60;focusCamera.aspect=1.5f;cameraNode.transform.position=new Vector3(0,0,-2);
            var focused=new StudioFocus();var focusFrame=new Snapshot{Active=true};
            var firstCandidate=source.Sample(1,1,false,NativeTarget.AUTO,true);var nextCandidate=native.Sample(1,1,false,NativeTarget.AUTO,true);firstCandidate.Active=nextCandidate.Active=true;
            focusFrame.Candidates.Add(firstCandidate);focusFrame.Candidates.Add(nextCandidate);
            Check(focused.Choose(adapter,focusFrame,focusCamera,"",0)==source.Id,"Camera projection selects the visible BP group and excludes the offscreen group");
            cameraNode.transform.position=new Vector3(3,0,-2);
            Check(focused.Choose(adapter,focusFrame,focusCamera,source.Id,1)==source.Id,"Moving camera toward the other group starts a stable wait");
            Check(focused.Choose(adapter,focusFrame,focusCamera,source.Id,1.7)==native.Id,"Centered native vagina source outranks the old offscreen BP group");
            cameraNode.transform.rotation=Quaternion.Euler(0,180,0);
            Check(focused.Choose(adapter,focusFrame,focusCamera,"",2)=="","Camera rear hemisphere cannot acquire a source");cameraNode.transform.rotation=Quaternion.identity;
            nextCandidate.Active=false;Check(focused.Choose(adapter,focusFrame,focusCamera,"",3)=="","Paused binding cannot be automatically selected");nextCandidate.Active=true;
            focusCamera.enabled=false;Check(focused.Choose(adapter,focusFrame,focusCamera,"",4)=="","Disabled camera cannot drive automatic selection");Destroy(cameraNode);
            typeof(KoiSync.Plugin.Plugin).GetField("adapter",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(host,adapter);
            host.SetAutoSource(true);host.Select(source.Id);Check(!host.StudioOptions.AutoSource.Value&&host.SelectedId==source.Id,"Manual source choice disables camera selection and locks the group");
            host.SetAutoSource(true);Check(host.StudioOptions.AutoSource.Value,"Camera following can be re-enabled without losing manual selection");
            string sceneCard=Path.Combine(Paths.GameRootPath,"neutral-profile-card.png");
            var installedHooks=(GameHooks)typeof(KoiSync.Plugin.Plugin).GetField("hooks",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(host);installedHooks.Refresh();
            Timeline.Timeline._self.OnSceneLoad(sceneCard);
            Check(host.Profiles.ScenePath==Path.GetFullPath(sceneCard),"Timeline scene-load callback selects the matching profile file");
            host.Profiles.Scene=new SceneProfile{Scene="neutral-profile-card.png"};
            var segment=new TimelineSegment{Start=0,End=1,Source=source.SavedKey,Label=source.Label,Measurement=new MeasurementProfile{FixedRange=true,RangeStart=.2,RangeEnd=.7,LengthScale=1.2,TargetOffset=new MotionVector(.003,.004,.005)}};
            host.Profiles.Scene.Segments.Add(segment);host.Profiles.SaveScene();
            Check(File.Exists(host.Profiles.SceneFile)&&host.Profiles.SceneFile.Contains(Path.Combine("KoiSync","cincaoizz")),"Scene settings save under UserData/KoiSync/cincaoizz using the card name");
            var reloaded=new PlaybackProfiles(Paths.GameRootPath);reloaded.LoadScene(sceneCard);
            Check(reloaded.Scene.Segments.Count==1,"Scene JSON round-trips under actual Unity CLR 2");
            var persisted=reloaded.Scene.Segments[0];Check(persisted.Source==source.SavedKey&&persisted.Measurement.FixedRange,"Stored scene restores stable source and range mode");Near(persisted.Measurement.TargetOffset.Z,.005,"Stored XYZ offset keeps precision");
            string stableKey=source.SavedKey;Transform oldEntry=entryLink.childTransform;
            int oldIndex=oldEntry.GetSiblingIndex();oldEntry.SetSiblingIndex(0);Check(source.SavedKey==stableKey,"Unrelated sibling order changes cannot invalidate a named source path");oldEntry.SetSiblingIndex(oldIndex);
            entryLink.childTransform=Node(male.Root,"Temporary replacement",Vector3.zero);
            Check(source.SavedKey==stableKey,"Stored identity belongs to captured binding endpoints, not the constraint object's runtime hash");entryLink.childTransform=oldEntry;
            target.Sex=1;male.Sex=0;male.CardName="Source Card";target.CardName="Target Card";
            Node(target.Root,"cm_J_dan100_00",Vector3.zero);Node(target.Root,"cm_J_dan109_00",Vector3.right*.1f);target.Bones=new BoneMap(target.Root);
            Check(!target.HasChain,"Female dormant shared skeleton is not a valid source chain");
            Check(Discover().Sources.TrueForAll(s=>s.Source!=target),"Bidirectional constraints cannot reverse a dormant target into the source role");
            target.Sex=0;Check(target.HasChain&&Discover().Sources.TrueForAll(s=>s.Source!=target),"Explicit target endpoint preserves direction even when both actors use male skeletons");target.Sex=1;
            var poseSource=Actor("Pose source",6,true);var poseTarget=Actor("Pose target",6.1f,false);
            var leg=new GameObject("cf_s_leg03_R").transform;leg.SetParent(poseSource.Root,false);
            var hand=new GameObject("cf_t_hand_L(work)").transform;hand.SetParent(poseTarget.Root,false);
            var poseLink=Link(leg,hand);Check(Discover().Sources.TrueForAll(s=>s.Source!=poseSource),"Leg-to-hand pose constraints do not create a measurement source");constraints.Remove(poseLink);
            Check(StudioSource.InferProfile("cf_t_hand_L(work)")==NativeTarget.LEFTHAND&&StudioSource.InferProfile("cf_t_hand_R(work)")==NativeTarget.RIGHTHAND,"Studio work-node suffix preserves left and right targets");
            var roles=source.Sample(1,1,false,NativeTarget.AUTO,true);
            Near((roles.Native.BodyFrame.Origin-roles.Native.Origin).Length,0,"Source pelvis orientation is displayed at its measurement root");
            Near((roles.Native.TargetBodyFrame.Origin-roles.Native.Target).Length,0,"Target pelvis orientation is displayed at its measurement root");
            Check(source.PairLabel.Contains("girl Target Card(")&&source.PairLabel.Contains("boy Source Card("),"Pair labels contain role, card name and Studio tree name");
            Check(colliderSource.SourceActor==male&&colliderSource.PairKey==source.PairKey,"Attached collider and BP nodes share the same actor-pair group");
            int objectCount=objects.Count;var savedConstraints=new ArrayList(constraints);
            var shared=Object("Shared scene folder",false);male.Parent=target.Parent=secondMale.Parent=secondTarget.Parent=shared;
            Check(Discover().Sources.Count==3,"Shared scene folders do not create cross-pair candidates");
            var footLeft=Node(target.Root,"cf_j_foot_L",new Vector3(.04f,0,0));
            var footIk=Node(target.Root,"cf_t_leg_L(work)",new Vector3(9,9,9));target.Bones=new BoneMap(target.Root);
            var waistHelper=Object("Foot helper",false);waistHelper.Root.SetParent(male.Bones.Get("cf_j_hips"),false);waistHelper.Parent=male;
            var nestedHelper=Object("Nested foot controller",false);nestedHelper.Parent=waistHelper;nestedHelper.Root.SetParent(waistHelper.Root,false);
            var footLink=Link(nestedHelper.Root,footIk);
            var detected=Discover();var footSource=detected.Sources.Find(s=>s.Source==male&&s.Target==target&&s.Profile==NativeTarget.LEFTFOOT);
            Check(footSource!=null,"Nested waist helper to foot IK resolves the actual source and target actors");
            Check(detected.Sources.Exists(s=>s.Bp&&s.Target==target),"BP and a foot binding on the same pair coexist");
            Near((footSource.Sample(1,1,false,NativeTarget.AUTO,true).Native.Target-StudioSource.V(footLeft.position)).Length,0,"Foot sampling uses the physical foot bone, not the helper position");
            var footBefore=footSource.Sample(1,1,false,NativeTarget.AUTO,true);
            Check(footBefore.Native.Contact!=null&&source.Sample(1,1,false,NativeTarget.AUTO,true).Native.Contact==null,"Only non-penetration targets select the body-part algorithm");
            footLeft.localRotation=Quaternion.Euler(45,0,0);var footAfter=footSource.Sample(1,1,false,NativeTarget.AUTO,true);
            Check(Math.Abs(NativeMotion.Solve(footAfter.Native).Raw[5]-NativeMotion.Solve(footBefore.Native).Raw[5])>1,"Foot rotation changes its channel even with unchanged target pelvis");footLeft.localRotation=Quaternion.identity;
            Node(target.Root,"a_n_hand_L",new Vector3(.01f,.02f,.03f));Node(target.Root,"a_n_ind_L",new Vector3(.02f,.03f,.04f));Node(target.Root,"a_n_mid_L",new Vector3(.01f,.04f,.05f));
            Node(target.Root,"a_n_mouth",new Vector3(0,.1f,0));Node(target.Root,"a_n_earrings_L",new Vector3(-.03f,.14f,0));Node(target.Root,"a_n_earrings_R",new Vector3(.03f,.14f,0));Node(target.Root,"cf_j_head",new Vector3(0,.17f,0));
            var breastLeft=Node(target.Root,"k_f_munenipL_00",new Vector3(-.04f,.07f,.03f));var breastRight=Node(target.Root,"k_f_munenipR_00",new Vector3(.04f,.07f,.03f));target.Bones=new BoneMap(target.Root);
            foreach(var kind in new[]{NativeTarget.ORAL,NativeTarget.BREASTS,NativeTarget.LEFTHAND})
            {
                var part=source.Sample(1,1,false,kind,true);Check(part!=null&&part.Native.Contact!=null,"Old-project landmarks work without unrelated main-game nodes: "+kind);
                Check(part.Native.Origin.Equals(footBefore.Native.Origin)&&Math.Abs((part.Native.End-part.Native.Origin).Length-(footBefore.Native.End-footBefore.Native.Origin).Length)<.0001,"Source root and length are shared across body-part algorithms: "+kind);
                if(kind==NativeTarget.BREASTS)Near((part.Native.Target-StudioSource.V((breastLeft.position+breastRight.position)*.5f)).Length,0,"Breast reference uses midpoint rather than sum of world positions");
            }
            nestedHelper.Root.SetParent(shared.Root,false);Check(!footSource.Live,"Reparenting a helper immediately invalidates cached source provenance");nestedHelper.Root.SetParent(waistHelper.Root,false);
            var independent=Object("NC-driven helper",false);var driver=Link(male.Bones.Get("cf_j_hips"),independent.Root);Link(independent.Root,footIk);
            footLink.enabled=false;var indirectFoot=Discover().Sources.Find(s=>s.Profile==NativeTarget.LEFTFOOT&&s.Source==male);
            Check(indirectFoot!=null&&indirectFoot.Sample(1,1,false,NativeTarget.AUTO,true)!=null,"Incoming helper constraints resolve an axial source without a physical actor parent");
            driver.enabled=false;Check(!indirectFoot.Live,"Disabling an intermediate driver invalidates the cached foot binding");driver.enabled=true;
            var conflicting=Link(secondMale.Bones.Get("cf_j_hips"),independent.Root);
            Check(!Discover().Sources.Exists(s=>s.Profile==NativeTarget.LEFTFOOT),"Ambiguous helper drivers cannot select the first actor arbitrarily");constraints.Remove(conflicting);footLink.enabled=true;
            var wrist=Object("Wrist collider",false);wrist.Parent=target;wrist.Root.SetParent(Node(target.Root,"a_n_wrist_L",Vector3.zero),false);
            var wristCollider=wrist.Root.gameObject.AddComponent<DynamicBoneCollider>();wristCollider.m_Bound=DynamicBoneCollider.Bound.Outside;wrist.Colliders.Add(wristCollider);
            Link(wrist.Root,target.Bones.Get("cf_j_kokan"));Link(wrist.Root,secondTarget.Bones.Get("cf_j_kokan"));physics.m_Colliders.Add(wristCollider);
            Check(Discover().Sources.TrueForAll(s=>s.Source!=wrist&&s.SourceActor!=s.Target),"Wrist colliders cannot borrow dormant roots or create self and cross-pair sources");
            physics.m_Colliders.Remove(wristCollider);
            var sourceProp=Object("Source attachment",false);sourceProp.Parent=secondMale;sourceProp.Root.SetParent(secondMale.Bones.Get("cm_J_dan100_00"),false);
            var targetProp=Object("Target attachment",false);targetProp.Parent=unrelated;targetProp.Root.SetParent(unrelated.Bones.Get("cf_j_kokan"),false);
            Link(sourceProp.Root,Node(targetProp.Root,"Decorative joint",Vector3.zero));
            Check(Discover().Sources.Exists(s=>s.Source==secondMale&&s.Target==unrelated&&s.Profile==NativeTarget.VAGINAL),"A link between attached props resolves both anatomical anchors without using prop names");
            var isolatedModel=Object("Independent model",false);Prism(isolatedModel.Root,.1f);
            var ownCollider=isolatedModel.Root.gameObject.AddComponent<DynamicBoneCollider>();ownCollider.m_Bound=DynamicBoneCollider.Bound.Outside;ownCollider.m_Radius=.01f;ownCollider.m_Height=.1f;isolatedModel.Colliders.Add(ownCollider);
            Link(isolatedModel.Root,unrelated.Bones.Get("cf_j_kokan"));var independentSource=Discover().Sources.Find(s=>s.Source==isolatedModel);
            Check(independentSource!=null&&independentSource.SourceActor==null&&independentSource.Sample(1,1,false,NativeTarget.AUTO,true)!=null,"An unnamed standalone mesh with explicit collider binding needs no Penis00");
            Check(StudioSource.InferProfile("cf_t_leg_L(work)")==NativeTarget.LEFTFOOT&&StudioSource.InferProfile("cf_t_leg_R(work)")==NativeTarget.RIGHTFOOT,"Studio foot IK work nodes preserve left and right sides");
            constraints.Clear();foreach(var c in savedConstraints)constraints.Add(c);
            male.Parent=target.Parent=secondMale.Parent=secondTarget.Parent=null;
            for(int i=objects.Count-1;i>=objectCount;i--){objects[i].Root.gameObject.SetActive(false);objects.RemoveAt(i);}
            var sourceAgain=Discover().Sources.Find(s=>s.Bp);Check(sourceAgain.SavedKey==stableKey,"Rediscovering bindings preserves the persisted identity");
            TimelineState.Seek(.75);Near(TimelineState.Time,.75,"Timeline Seek uses the real API shape");
            var later=host.Profiles.Scene.Split(.5);later.Source=native.SavedKey;later.Label=native.Label;host.Profiles.Changed(true);
            host.UpdateStudioSource(frame,null,200);Check(host.SelectedId==native.Id,"Current Timeline segment overrides camera selection with its saved source");
            TimelineState.Seek(.25);host.UpdateStudioSource(frame,null,201);Check(host.SelectedId==source.Id,"Backward seeking restores the earlier segment source");
            host.EnableSync();host.SetAutoSource(true);Check(segment.Source==""&&host.Sync.Enabled,"Auto-source toggle edits the current segment without disabling the session");
            host.Select(source.Id);Check(segment.Source==stableKey&&host.Sync.Enabled,"Manual binding choice updates the segment and keeps synchronization enabled");
            segment.Source=TimelineSegment.NoSource;segment.Name="无";segment.ColorIndex=9;host.Profiles.Changed(true);host.UpdateStudioSource(frame,null,201.5);
            Check(host.SelectedId.StartsWith("none:")&&host.Sync.Enabled,"No-source interval retains sync intent without choosing an available actor");
            var noneStore=new PlaybackProfiles(Paths.GameRootPath);noneStore.LoadScene(host.Profiles.ScenePath);Check(noneStore.Scene.Segments[0].Source==TimelineSegment.NoSource&&noneStore.Scene.Segments[0].Name=="无"&&noneStore.Scene.Segments[0].ColorIndex==9,"None state, custom name and independent color round-trip together");
            segment.Source="missing-binding";host.UpdateStudioSource(frame,null,202);Check(host.SelectedId.StartsWith("missing:"),"Unavailable saved binding cannot silently drive a different group");segment.Source=stableKey;
            Timeline.Timeline._self.OnSceneSave(Path.Combine(Paths.GameRootPath,"neutral-profile-save-as.png"));Check(File.Exists(host.Profiles.SceneFile),"Timeline scene-save callback creates settings for the new card name");
            host.Profiles.NativeKey="fixture / action A";host.Profiles.NativeMeasurement.Target=NativeTarget.ORAL;host.Profiles.NativeMeasurement.RootOffset=new MotionVector(.01,.02,.03);
            host.Profiles.NativeKey="fixture / action B";host.Profiles.NativeMeasurement.Target=NativeTarget.VAGINAL;host.Profiles.SaveNative();
            reloaded=new PlaybackProfiles(Paths.GameRootPath);Check(reloaded.Animation("fixture / action A").Measurement.Target==NativeTarget.ORAL&&reloaded.Animation("fixture / action B").Measurement.Target==NativeTarget.VAGINAL,"Main-game animations retain separate saved source types");
            Near(reloaded.Animation("fixture / action A").Measurement.RootOffset.Y,.02,"Main-game offsets survive a new store instance");
            Check(File.Exists(reloaded.AnimationFile("fixture / action A"))&&File.Exists(reloaded.AnimationFile("fixture / action B")),"Each animation has its own independent JSON file");
            var animationA=reloaded.Animation("fixture / action A").Measurement;animationA.PlaneWidth=.24;animationA.PlaneDepth=.35;animationA.HiddenObjects=5;animationA.Multipliers[0]=2;animationA.ChannelMinimum[1]=.2;animationA.ChannelMaximum[1]=.7;reloaded.SaveNative();
            var isolatedStore=new PlaybackProfiles(Paths.GameRootPath);var savedA=isolatedStore.Animation("fixture / action A").Measurement;Near(savedA.PlaneWidth,.24,"Animation plane width round-trips");Near(savedA.PlaneDepth,.35,"Animation plane depth round-trips");Check(savedA.HiddenObjects==5,"Animation object layer visibility round-trips");Near(savedA.Multipliers[0],2,"Animation channel multiplier round-trips");Near(savedA.ChannelMaximum[1],.7,"Animation channel limit round-trips");Near(isolatedStore.Animation("fixture / action B").Measurement.Multipliers[0],1,"Other animation multiplier remains independent");
            Check(reloaded.AnimationFile("a/b")!=reloaded.AnimationFile("a%002Fb"),"Escaped animation filenames do not collide");
            foreach(var profile in new[]{NativeTarget.VAGINAL,NativeTarget.ORAL,NativeTarget.BREASTS,NativeTarget.LEFTHAND})
            {
                var mainSample=ActorMeasurement.Sample(male,target,profile,1);Check(mainSample.RootDistance&&!mainSample.Studio&&mainSample.HasBodyFrame,"Main actor sampling has editable shared reference frame "+profile);
                var pelvis=male.Bones.Get("cm_J_waist02","cf_j_waist02");
                if(pelvis!=null&&profile==NativeTarget.VAGINAL)Check(Math.Abs(MotionVector.Dot(mainSample.BodyFrame.Y,StudioSource.V(pelvis.forward)))>.99,"Main actor plane follows pelvis facing axis "+profile);
                Check((mainSample.Contact==null)==(profile==NativeTarget.VAGINAL),"Main actor chooses the corresponding body-part solver "+profile);
                var mainResult=NativeMotion.Solve(mainSample);Check(Numbers.Finite(mainResult.Normalized[0]),"Main actor produces a finite absolute stroke "+profile);
            }
            host.Profiles.Scene=new SceneProfile();host.Profiles.Revision++;host.SelectedId=source.Id;
            if(ui)
            {
                typeof(KoiSync.Plugin.Plugin).GetField("adapter",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(host,adapter);
                adapter.Discovery="合成节点验证 · "+adapter.Sources.Count+" 组绑定";host.SelectedId=source.Id;host.Selected=source.Sample(1,1,false,NativeTarget.AUTO,true);host.Frame=frame;
                adapter.Sample(frame,host.StudioOptions);
            }
            MainActorChecks.Run(Check);ShaftGeometryChecks.Run(Check);
            report.AppendLine("PASS: "+checks+" studio binding checks");
        }
        catch(Exception ex){report.AppendLine("FAIL "+ex);}
        finally
        {
            if(!ui){constraints.Clear();Timeline.Timeline._self._interpolables.Clear();TimelineState.Refresh();if(world!=null)Destroy(world);}
            File.WriteAllText(Path.Combine(Paths.GameRootPath,"studio-binding-checks.txt"),report.ToString());
        }
        if(!ui)Application.Quit();
    }
    private StudioAdapter Discover(){string status;var graph=new StudioGraph(objects,StudioLink.ReadAll(out status));var adapter=new StudioAdapter();adapter.Discover(graph);return adapter;}
    private StudioObject Object(string name,bool character)
    {
        var go=new GameObject(name);go.transform.SetParent(world.transform,false);
        var obj=new StudioObject{Id=objects.Count+1,Name=name,Root=go.transform,Character=character,Control=new Studio.OCIItem(),Bones=new BoneMap(go.transform)};objects.Add(obj);return obj;
    }
    private StudioObject Actor(string name,float x,bool source)
    {
        var obj=Object(name,true);obj.Root.localPosition=new Vector3(x,0,0);
        Node(obj.Root,"cf_j_hips",Vector3.up);Node(obj.Root,"cf_j_kokan",Vector3.zero);Node(obj.Root,"cf_j_ana",new Vector3(0,0,-.1f));Node(obj.Root,"cf_J_Vagina_root",Vector3.zero);
        if(source)
        {
            Node(obj.Root,"cm_J_dan100_00",Vector3.zero);
            for(int i=0;i<9;i++)Node(obj.Root,"cm_J_dan"+(101+i)+"_00",new Vector3(i*.0125f,0,0));
            Node(obj.Root,"cm_J_dan_f_L",new Vector3(0,-.01f,-.1f));Node(obj.Root,"cm_J_dan_f_R",new Vector3(0,.01f,-.1f));
        }
        obj.Bones=new BoneMap(obj.Root);return obj;
    }
    private static Transform Node(Transform root,string name,Vector3 position){var go=new GameObject(name);go.transform.SetParent(root,false);go.transform.localPosition=position;return go.transform;}
    private static MeshRenderer Prism(Transform root,float length)
    {
        var child=Node(root,"Measured mesh",Vector3.zero);var mesh=new Mesh();var points=new Vector3[8];
        for(int i=0;i<8;i++)points[i]=new Vector3((i&1)==0?0:length,(i&2)==0?-.005f:.005f,(i&4)==0?-.005f:.005f);
        mesh.vertices=points;mesh.triangles=new[]{0,2,4,2,6,4,1,5,3,3,5,7};mesh.RecalculateBounds();child.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;return child.gameObject.AddComponent<MeshRenderer>();
    }
    private KoiSyncConstraintFixture Link(Transform parent,Transform child){var link=new KoiSyncConstraintFixture{parentTransform=parent,childTransform=child};constraints.Add(link);return link;}
}
public sealed class KoiSyncConstraintFixture
{public bool enabled=true,destroyed=false,position=true,rotation=false,scale=false;public Transform parentTransform,childTransform;}
public sealed class KoiSyncBpFixture:MonoBehaviour{public KoiSyncBpAgent danAgent;public bool danTargetsValid=true;}
public sealed class KoiSyncBpAgent{public KoiSyncBpPoints m_danPoints;}
public sealed class KoiSyncBpPoints{public List<KoiSyncBpPoint> danPoints;}
public sealed class KoiSyncBpPoint{public Transform transform;}
public class KoiSyncTimelineModel{public object parameter;public string id="fixture";}
public sealed class KoiSyncTimelineTrack:KoiSyncTimelineModel
{public Studio.ObjectCtrlInfo oci;public bool enabled=true;public SortedList<float,object> keyframes=new SortedList<float,object>{{0,new object()},{1,new object()}};}
public sealed class KoiSyncTimelineKeyframe { public object value;public AnimationCurve curve=AnimationCurve.Linear(0,0,1,1); }
// Exact field schemas verified against local DLLs. These fixtures are never shipped with the release plugin.
namespace NodesConstraints{public sealed class NodesConstraints{public static NodesConstraints _self=new NodesConstraints();public ArrayList _constraints=new ArrayList();}}
namespace Timeline
{
    public sealed class Timeline
    {
        public static Timeline _self=new Timeline();public Dictionary<int,KoiSyncTimelineTrack> _interpolables=new Dictionary<int,KoiSyncTimelineTrack>();
        public static bool isPlaying{get;set;}public static float playbackTime{get;set;}public static float duration{get{return 1;}}
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]public static void Seek(float value){playbackTime=value;if(SeekAction!=null)SeekAction(value);}
        public static Action<float> SeekAction; public string lastPath;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]public void OnSceneLoad(string path){lastPath=path;}
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]public void OnSceneSave(string path){lastPath=path;}
    }
}
