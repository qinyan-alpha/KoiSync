using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using KoiSync.Core;
using KoiSync.Plugin;
using KoiSync.Wire;
using UnityEngine;

[BepInPlugin("local.koisync.scenechecks","KoiSync real scene checks","0.1.0")]
[BepInDependency("local.koisync")]
[BepInDependency("com.joan6694.illusionplugins.timeline")]
[BepInDependency("com.joan6694.illusionplugins.nodesconstraints")]
public sealed class RealSceneChecks:BaseUnityPlugin
{
    private readonly StringBuilder report=new StringBuilder();
    private readonly Dictionary<Camera,int> masks=new Dictionary<Camera,int>();
    private string reportPath; private float heartbeat;
    private KoiSync.Plugin.Plugin host;
    private const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    private void Awake()
    {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"--koisync-real-scenes")<0)return;
        Application.runInBackground=true;reportPath=Path.Combine(Paths.GameRootPath,"real-scene-checks.txt");
        Camera.onPreCull+=BeforeCamera;Camera.onPostRender+=AfterCamera;
        Log("REAL SCENE CHECKS "+DateTime.Now+" | rendering withheld; text and numeric inspection only");var harmony=new HarmonyLib.Harmony("local.koisync.real-scene-probe");var method=typeof(AssetBundleManager).GetMethod("LoadAsset",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(string),typeof(string),typeof(Type),typeof(string)},null);
        if(method!=null)harmony.Patch(method,finalizer:new HarmonyLib.HarmonyMethod(typeof(RealSceneChecks),"AssetFailure"));StartCoroutine(Run());
    }
    private static Exception AssetFailure(Exception __exception,object[] __args){if(__exception!=null)Debug.LogWarning("SCENE ASSET FAILURE bundle="+__args[0]+" asset="+__args[1]+" manifest="+__args[3]+" : "+__exception.GetType().Name);return __exception;}
    private void Update(){if(reportPath==null||Time.realtimeSinceStartup<heartbeat)return;heartbeat=Time.realtimeSinceStartup+15;var studio=FindObjectOfType<Studio.Studio>();Log("STATUS scene="+UnityEngine.SceneManagement.SceneManager.GetActiveScene().name+" objects="+(studio==null?0:studio.dicObjectCtrl.Count)+" frame="+Time.frameCount);}
    private void BeforeCamera(Camera camera){if(!masks.ContainsKey(camera))masks[camera]=camera.cullingMask;camera.cullingMask=0;}
    private void AfterCamera(Camera camera){int mask;if(masks.TryGetValue(camera,out mask)){camera.cullingMask=mask;masks.Remove(camera);}}
    private void OnDestroy(){Camera.onPreCull-=BeforeCamera;Camera.onPostRender-=AfterCamera;}
    private void Log(string line){report.AppendLine(line);File.WriteAllText(reportPath,report.ToString(),Encoding.UTF8);Logger.LogInfo(line);}
    private IEnumerator Run()
    {
        yield return new WaitForSecondsRealtime(12);
        host=FindObjectOfType<KoiSync.Plugin.Plugin>();if(host==null){Log("FAIL Plugin missing");yield break;}
        host.StopAllCoroutines();host.enabled=false;host.Stop("隔离实景测试");
        var studio=FindObjectOfType<Studio.Studio>();for(int i=0;i<180&&studio==null;i++){yield return new WaitForSecondsRealtime(1);studio=FindObjectOfType<Studio.Studio>();}
        if(studio==null){Log("FAIL Studio unavailable");yield break;}
        Log("READY Unity "+Application.unityVersion+" | hardware connected="+host.Devices.Connected);
        foreach(string name in Array.IndexOf(Environment.GetCommandLineArgs(),"--koisync-pair-scenes")>=0?new[]{"021.png"}:Array.IndexOf(Environment.GetCommandLineArgs(),"--koisync-root-scenes")>=0?new[]{"002.png"}:new[]{"007.png","2024_0618_0304_27_777.png","2024_0902_0730_21_686.png"})
        {
            var routine=Scene(studio,name);bool failed=false;
            while(true)
            {
                bool more;object wait=null;try{more=routine.MoveNext();if(more)wait=routine.Current;}
                catch(Exception ex){Log("FAIL "+name+" "+ex);failed=true;break;}
                if(!more)break;yield return wait;
            }
            if(failed)Log("Continuing with the next independent scene after the load failure");
        }
        Log("DONE real scene checks | hardware connected="+host.Devices.Connected);Application.Quit();
    }
    private IEnumerator Scene(Studio.Studio studio,string name)
    {
        string path=Path.Combine(Paths.GameRootPath,"UserData/Studio/scene/fixtures/"+name);Log("LOAD "+name);
        if(!studio.LoadScene(path))throw new Exception("Studio LoadScene returned false");Log("LOADED "+name);
        // Community plugins restore bindings after 8 frames and Timeline after 20.
        // A wall-clock delay can elapse in only a few frames while loading assets.
        for(int frame=0;frame<45;frame++)yield return null;
        TimelineState.Refresh();if(TimelineState.Available)TimelineState.SetPlaying(false);
        var adapter=host.StudioAdapter;adapter.Refresh();
        if(name=="021.png")
        {
            foreach(string plugin in new[]{"NodesConstraints.NodesConstraints","Timeline.Timeline"})
            {
                Type type=StudioMembers.FindType(plugin);object self=StudioMembers.Read(type,"_self");
                var load=type.GetMethod("OnSceneLoad",F,null,new[]{typeof(string)},null);if(load!=null)load.Invoke(self,new object[]{path});
            }
            for(int frame=0;frame<45;frame++)yield return null;TimelineState.Refresh();TimelineState.SetPlaying(false);adapter.Refresh();
            Log("UNCHANGED ACTORS "+Describe(adapter));LogActors(studio,"original");DumpBindings(adapter);
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--koisync-geometry-scenes")>=0)
            {
                foreach(int id in new[]{137,3})
                {
                    var actor=adapter.Graph.Objects.Find(o=>o.Id==id);if(actor==null)throw new Exception("Missing original actor "+id);
                    var reference=ShaftGeometry.Resolve(new StudioSource{Source=actor,Graph=adapter.Graph});
                    if(reference.Native)throw new Exception("Expected the scene's actual prop mesh for actor "+id);
                    var endpoint=reference.End(adapter.Graph);double length=(endpoint-StudioSource.V(reference.Root.position)).Length;
                    Log("MESH GEOMETRY actor="+id+" model="+reference.Model.Id+"/"+reference.Model.Name+" root="+reference.Root.name+" renderers="+reference.Renderers.Length+" length="+length.ToString("F6")+" end="+endpoint.X.ToString("F4")+","+endpoint.Y.ToString("F4")+","+endpoint.Z.ToString("F4"));
                    if(!Numbers.Finite(length)||length<.01||length>2)throw new Exception("Implausible scene prop length");
                }
                Log("GEOMETRY COMPLETE original prop meshes; playback/pairing not certified by this isolated loader");yield break;
            }
            yield return Probe(adapter,studio,name,"original");Log("SCENE COMPLETE "+name+" original actors retained | hardware connected="+host.Devices.Connected);yield break;
        }
        Log("ORIGINAL "+name+" | "+Describe(adapter));
        LogActors(studio,"original");
        yield return Probe(adapter,studio,name,"original");
        var actors=new List<Studio.OCIChar>();foreach(var row in studio.dicObjectCtrl){var c=row.Value as Studio.OCIChar;if(c!=null)actors.Add(c);}
        string template=Path.Combine(Paths.GameRootPath,"UserData/chara/male/特殊模板 - 玻璃人.png");
        Log("REPLACE "+name+" | actors="+actors.Count);
        foreach(var actor in actors)
        {
            actor.ChangeChara(template);yield return new WaitForSecondsRealtime(.6f);
        }
        for(int frame=0;frame<30;frame++)yield return null;
        foreach(string plugin in new[]{"NodesConstraints.NodesConstraints","Timeline.Timeline"})
        {
            Type type=StudioMembers.FindType(plugin);object self=StudioMembers.Read(type,"_self");
            var load=type.GetMethod("OnSceneLoad",F,null,new[]{typeof(string)},null);
            if(load!=null){load.Invoke(self,new object[]{path});Log("REBIND saved scene paths after replacement: "+plugin);}
        }
        for(int frame=0;frame<45;frame++)yield return null;TimelineState.Refresh();TimelineState.SetPlaying(false);adapter.Refresh();
        int male=0,total=0;foreach(var row in studio.dicObjectCtrl){var c=row.Value as Studio.OCIChar;if(c!=null&&c.charInfo!=null){total++;if(c.charInfo.sex==0)male++;}}
        Log("GLASS "+name+" | male="+male+"/"+total+" | "+Describe(adapter));LogActors(studio,"glass");
        yield return Probe(adapter,studio,name,"glass");
        if(name=="002.png")yield return ProbeTwist(adapter);
        Log("SCENE COMPLETE "+name+" | hardware connected="+host.Devices.Connected);
    }
    private static string NodePath(Transform t)
    {string path="";for(;t!=null;t=t.parent)path=t.name+"/"+path;return path;}
    private void DumpBindings(StudioAdapter adapter)
    {
        var graph=adapter.Graph;var dump=new StringBuilder();
        foreach(var o in graph.Objects)
        {
            dump.AppendLine("OBJECT\t"+o.Id+"\tparent="+(o.Parent==null?"none":o.Parent.Id.ToString())+"\t"+o.DisplayName+"\tcharacter="+o.Character+"\tchain="+o.HasChain+"\tbp="+(o.Bp!=null)+"\tlive="+o.Live+"\t"+NodePath(o.Root));
            if(o.Character)foreach(string bone in new[]{"cm_J_dan100_00","cm_J_dan109_00","k_f_dan_entry","k_f_dan_end","cf_J_Vagina_root","cf_j_foot_L","cf_j_foot_R"}){var t=o.Bones.Get(bone);if(t!=null)dump.AppendLine("BONE\t"+o.Id+"\t"+bone+"\t"+t.position+"\t"+NodePath(t));}
            foreach(var c in o.Colliders)dump.AppendLine("COLLIDER\t"+o.Id+"\t"+c.GetInstanceID()+"\t"+c.m_Direction+"\t"+c.m_Bound+"\t"+c.transform.position+"\t"+NodePath(c.transform));
        }
        foreach(var link in graph.Links)
        {
            var a=graph.Owner(link.Parent);var b=graph.Owner(link.Child);
            dump.AppendLine("LINK\t"+(a==null?-1:a.Id)+"\t"+(b==null?-1:b.Id)+"\tposition="+link.Position+"\t"+NodePath(link.Parent)+"\t"+NodePath(link.Child));
        }
        foreach(var source in adapter.Sources)
        {
            var sample=source.Sample(1,1,false,NativeTarget.AUTO,true);
            dump.AppendLine("SOURCE\t"+source.SavedKey+"\towner="+(source.SourceActor==null?-1:source.SourceActor.Id)+"\t"+source.PairLabel+"\t"+source.NodeLabel+"\tvalid="+(sample!=null)+"\t"+source.Error);
        }
        File.WriteAllText(Path.Combine(Paths.GameRootPath,"021-bindings.txt"),dump.ToString(),Encoding.UTF8);
    }
    private IEnumerator ProbeTwist(StudioAdapter adapter)
    {
        var pipelines=new Dictionary<string,MotionPipeline>();var previous=new Dictionary<string,double>();var oldTrackers=new Dictionary<string,ContinuousAngle>();
        var table=new StringBuilder("time\tsource\toldBodyTwist\toldContinuous\toldPosition\tlineAngle\tcontinuousR0\tnormalizedR0\tmappedR0\n");
        int crossings=0,samples=0,centerSamples=0,centerValid=0;long sequence=0;
        // Dense Timeline sampling covers the reported boundary, then the full clip.
        for(double time=0;time<Math.Min(46,TimelineState.Duration);time+=.1)
        {
            TimelineState.Seek(time);yield return null;yield return new WaitForEndOfFrame();
            foreach(var source in adapter.Sources)
            {
                var candidate=source.Sample(1,1,false,NativeTarget.AUTO,true);if(candidate==null)continue;
                new MeasurementProfile{AlignmentVersion=1}.Apply(candidate);
                double wrapped=NativeMotion.Solve(candidate.Native).Raw[3];
                double oldTwist=BodyTwist.Measure(candidate.Native.ReferenceFrame,candidate.Native.TargetReferenceFrame,candidate.Native.End-candidate.Native.Origin);
                ContinuousAngle oldTracker;if(!oldTrackers.TryGetValue(source.SavedKey,out oldTracker)){oldTracker=new ContinuousAngle();oldTrackers.Add(source.SavedKey,oldTracker);}double oldAngle=oldTracker.Resolve(oldTwist);
                MotionPipeline pipeline;if(!pipelines.TryGetValue(source.SavedKey,out pipeline)){pipeline=new MotionPipeline{Minimum=0,Maximum=1};pipelines.Add(source.SavedKey,pipeline);}
                var snapshot=new Snapshot{Session="real-r0",Scene="002",State="paused",Sequence=++sequence,SampleTime=sequence*.02,Active=true};candidate.Active=true;
                pipeline.Update(snapshot,candidate,0,StrokeAxis.Z);double old;
                if(previous.TryGetValue(source.SavedKey,out old)&&Math.Abs(wrapped-old)>90)
                {crossings++;Log("R0 SEAM t="+time.ToString("F2")+" wrapped="+old.ToString("F3")+" -> "+wrapped.ToString("F3")+" continuous="+pipeline.Raw[3].ToString("F3")+" mapped="+ChannelMonitor.PositionText(pipeline.Values[3]));}
                previous[source.SavedKey]=wrapped;
                double display=NativeMotion.Solve(candidate.Native).Raw[3];if(Math.Abs(display-pipeline.Raw[3])>1e-6)throw new Exception("Display/output R0 mismatch");
                double normalized;pipeline.TryGetNativeInput(3,out normalized);
                table.AppendFormat(CultureInfo.InvariantCulture,"{0:F3}\t{1}\t{2:F6}\t{3:F6}\t{4:F6}\t{5:F6}\t{6:F6}\t{7:F6}\t{8:F6}\n",time,source.SavedKey,oldTwist,oldAngle,Numbers.Clamp(.5+oldAngle/360,0,1),wrapped,display,normalized,pipeline.Values[3]);samples++;
                if(time>=20&&time<=26){centerSamples++;if(normalized>.2&&normalized<.8)centerValid++;}
            }
        }
        File.WriteAllText(Path.Combine(Paths.GameRootPath,"002-glass-r0-v0120.tsv"),table.ToString(),Encoding.UTF8);
        if(samples==0)throw new Exception("No valid R0 samples");
        Log("R0 PELVIS LINES samples="+samples+" observedLineSeams="+crossings+" centralPositionsAt20to26s="+centerValid+"/"+centerSamples+" displayMatchesPipeline=true | hardware connected="+host.Devices.Connected);
        if(centerSamples==0||centerValid<.8*centerSamples)throw new Exception("Reported neutral interval is not centered");
    }
    private void LogActors(Studio.Studio studio,string stage)
    {
        foreach(var row in studio.dicObjectCtrl)
        {
            var c=row.Value as Studio.OCIChar;if(c==null||c.charInfo==null)continue;
            Log("ACTOR "+stage+" id="+row.Key+" sex="+c.charInfo.sex+" tree="+c.treeNodeObject.textName+" root="+c.charInfo.name+" card="+c.charInfo.fileParam.fullname+" visibleSource="+c.charInfo.fileStatus.visibleSon+"/"+c.charInfo.fileStatus.visibleSonAlways);
            var bones=new BoneMap(c.charInfo.transform);Log("BONES "+stage+" id="+row.Key+" chain="+(bones.Get("cm_J_dan100_00")!=null)+" bpEntry="+(bones.Get("k_f_dan_entry")!=null)+" targetRoot="+(bones.Get("cf_J_Vagina_root")!=null));
        }
    }
    private string Describe(StudioAdapter adapter)
    {
        var pairs=new HashSet<string>();int bp=0,collider=0;foreach(var s in adapter.Sources){pairs.Add(s.PairKey);if(s.Bp)bp++;if(s.IsCollider)collider++;}
        return adapter.Discovery+" | pairs="+pairs.Count+" bp="+bp+" collider="+collider+" duration="+TimelineState.Duration.ToString("F3",CultureInfo.InvariantCulture);
    }
    private IEnumerator Probe(StudioAdapter adapter,Studio.Studio studio,string name,string stage)
    {
        string file=Path.Combine(Paths.GameRootPath,Path.GetFileNameWithoutExtension(name)+"-"+stage+"-samples.tsv");
        var values=new StringBuilder("time\tpair\tsource\tvalid\tL0\tL1\tL2\tR0\tR1\tR2\tblueOwner\tpinkOwner\n");
        var seen=new HashSet<string>();var cameras=new List<CameraObservation>();int valid=0,wrongOwner=0,autoNone=0;
        double duration=TimelineState.Duration;var times=new List<double>{0};if(duration>.01)foreach(double f in new[]{.1,.25,.5,.75,.9,.99})times.Add(duration*f);
        if(name=="002.png"&&duration>42.127){times.Add(41.756);times.Add(42.127);times.Sort();}
        foreach(double t in times)
        {
            if(TimelineState.Available)TimelineState.Seek(t);yield return null;yield return new WaitForEndOfFrame();adapter.Refresh();
            Camera camera=studio.cameraCtrl.mainCmaera;if(camera!=null)cameras.Add(new CameraObservation{Time=t,Position=camera.transform.position,Rotation=camera.transform.rotation,CameraId=camera.GetInstanceID()});
            foreach(var s in adapter.Sources)
            {
                var sample=s.Sample(1,1,false,NativeTarget.AUTO,true);values.Append(t.ToString("F3",CultureInfo.InvariantCulture)).Append('\t').Append(s.PairLabel).Append('\t').Append(s.NodeLabel).Append('\t').Append(sample!=null);
                if(seen.Add(s.SavedKey))Log("SOURCE "+stage+" | "+s.PairLabel+" | "+s.NodeLabel+" | "+s.SavedKey+" | root="+(sample==null?s.Error:sample.ReferenceBone));
                if(sample!=null)
                {
                    var m=new MeasurementProfile{AlignmentVersion=1};m.Apply(sample);var result=NativeMotion.Solve(sample.Native);valid++;
                    if(name=="002.png")Log("ROOT "+stage+" t="+t.ToString("F3")+" source="+s.Source.Id+" root="+sample.ReferenceBone+" target="+sample.TargetBone+" distance="+result.Raw[0].ToString("F6")+" length="+result.Length.ToString("F6")+" L0="+ChannelMonitor.PositionText(result.Normalized[0]));
                    foreach(double n in result.Raw)values.Append('\t').Append(n.ToString("F6",CultureInfo.InvariantCulture));
                    var owner=s.SourceActor;values.Append('\t').Append(owner==null?s.Source.Name:owner.Name).Append('\t').Append(s.Target.Name);
                    if(owner!=null&&owner==s.Target)wrongOwner++;
                }
                else values.Append('\t').Append(s.Error);
                values.AppendLine();
            }
            string posture;var selected=StudioAutoPartition.Choose(adapter,camera,out posture);if(selected==null)autoNone++;
            Log("PROBE "+stage+" t="+t.ToString("F2")+" sources="+adapter.Sources.Count+" auto="+(selected==null?"无":selected.PairLabel+" :"+posture));
            File.WriteAllText(file,values.ToString(),Encoding.UTF8);
        }
        Log("NUMERIC "+stage+" | valid="+valid+" wrongOwner="+wrongOwner+" uniqueSources="+seen.Count+" noSourceProbes="+autoNone+"/"+times.Count+" cameraKeys="+TimelineState.CameraKeyTimes().Count+" | "+file);
        if(wrongOwner>0)throw new Exception("Source frame was attached to target actor");
    }
}
