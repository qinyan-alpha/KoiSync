using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using KoiSync.Core;
using KoiSync.Plugin;
using KoiSync.Wire;
using UnityEngine;

internal static class SkeletalAudit
{
    private static readonly Dictionary<string,AssetBundle> bundles=new Dictionary<string,AssetBundle>();
    private static readonly NativeTarget[] targets={NativeTarget.VAGINAL,NativeTarget.ANAL,NativeTarget.ORAL,NativeTarget.BREASTS,NativeTarget.LEFTHAND,NativeTarget.RIGHTHAND,NativeTarget.LEFTFOOT,NativeTarget.RIGHTFOOT,NativeTarget.INTERCRURAL};
    private sealed class Rig
    {
        internal GameObject Root;
        internal Animator Animator;
        internal StudioObject Actor;
        internal Rig(string name,GameObject body,GameObject head)
        {
            Root=new GameObject(name);var b=Copy(body.transform,Root.transform);var map=new BoneMap(b);
            var attach=map.Get("cf_s_head","cf_j_head");if(attach!=null&&head!=null){var h=Copy(head.transform,attach);h.localPosition=Vector3.zero;h.localRotation=Quaternion.identity;}
            Animator=b.gameObject.AddComponent<Animator>();var original=body.GetComponent<Animator>();if(original!=null)Animator.avatar=original.avatar;
            Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;Animator.applyRootMotion=false;
            Actor=new StudioObject{Root=Root.transform,Bones=new BoneMap(Root.transform),Character=true,Name=name,CardName=name};
        }
        internal void Set(HSceneProc.PathName basePath,HSceneProc.PathName path,List<int> layers)
        {
            var basis=Load<RuntimeAnimatorController>(basePath.assetpath,basePath.file);var extra=Load<RuntimeAnimatorController>(path.assetpath,path.file);
            Animator.runtimeAnimatorController=extra==null?basis:Illusion.Utils.Animator.SetupAnimatorOverrideController(basis,extra);
            Animator.Rebind();foreach(var layer in layers)if(layer>=0&&layer<Animator.layerCount)Animator.SetLayerWeight(layer,1);
            foreach(var parameter in Animator.parameters)if(parameter.type==AnimatorControllerParameterType.Float)Animator.SetFloat(parameter.nameHash,parameter.name.ToLower().Contains("speed")?1:.5f);
            Animator.Update(0);
        }
        internal bool Has(string state){return Animator.runtimeAnimatorController!=null&&Animator.HasState(0,Animator.StringToHash(state));}
        internal void Pose(string state,float time){Animator.Play(state,0,time);Animator.Update(0);}
    }
    private static Transform Copy(Transform original,Transform parent)
    {
        var go=new GameObject(original.name);go.transform.SetParent(parent,false);go.transform.localPosition=original.localPosition;go.transform.localRotation=original.localRotation;go.transform.localScale=original.localScale;
        foreach(Transform child in original)Copy(child,go.transform);return go.transform;
    }
    private static T Load<T>(string relative,string asset) where T:UnityEngine.Object
    {
        if(string.IsNullOrEmpty(relative)||string.IsNullOrEmpty(asset))return null;
        AssetBundle bundle;if(!bundles.TryGetValue(relative,out bundle)){bundle=AssetBundle.LoadFromFile(Path.Combine(BepInEx.Paths.GameRootPath,"abdata/"+relative));bundles[relative]=bundle;}
        return bundle==null?null:bundle.LoadAsset<T>(asset);
    }
    internal static IEnumerator Run(List<HSceneProc.AnimationListInfo> records)
    {
        Rig male=null,female=null,second=null;var log=new StringBuilder();
        Dictionary<string,NativeTarget> mapping;using(var reader=new StreamReader(@"E:\project\c#\KoiSync\src\Data\NativeAnimations.csv"))mapping=NativeProfiles.Read(reader);
        var summary=new StringBuilder("mode\tid\tname\tprofile\tstate\tmaleState\tfemaleState\tframes\tminL0\tmaxL0\tclampL0\trangeL1\trangeL2\trangeR0\trangeR1\trangeR2\tmaxR0Step\trootMove\ttargetMove\tnearestTarget\tnearestDistance\tmappedDistance\terror\n");
        var states=new StringBuilder("mode\tid\tname\tprofile\tstate\tmaleHasState\tfemaleHasState\tsecondHasState\tanalMetadata\n");
        var catalogStatus=new StringBuilder("mode\tid\tname\tmapped\tprofile\tmaleController\tfemaleController\tsecondController\tnotes\n");
        var rawPath=Path.Combine(NativeAudit.Output,"samples.tsv");
        var raw=new StreamWriter(rawPath,false,Encoding.UTF8);raw.WriteLine("mode\tid\tname\tprofile\tstate\tframe\tL0\tL1\tL2\tR0\tR1\tR2\tlength\tAx\tAy\tAz\tBx\tBy\tBz");
        try
        {
            var body=Load<GameObject>("chara/oo_base.unity3d","p_cf_body_bone");var head=Load<GameObject>("chara/oo_base.unity3d","p_cf_head_bone");
            if(body==null)throw new Exception("Body skeleton prefab missing");
            male=new Rig("audit male skeleton",body,head);female=new Rig("audit female skeleton",body,head);second=new Rig("audit second skeleton",body,head);
            log.AppendLine("Transform-only skeletons; no mesh renderers, physics, BP, IK, character shape, scene or device changes.");
            log.AppendLine("Skeleton nodes="+male.Root.GetComponentsInChildren<Transform>().Length+" avatar="+male.Animator.avatar);
            foreach(NativeTarget target in Enum.GetValues(typeof(NativeTarget)))try{log.AppendLine("Primary "+target+"="+NativeProfiles.Primary(target));}catch(Exception ex){log.AppendLine("Primary ERROR "+target+"="+ex.Message);}
        }
        catch(Exception ex){log.AppendLine("INIT ERROR "+ex);}
        int count=0;
        foreach(var info in records)
        {
            if(male==null)break;
            try
            {
                NativeTarget profile;bool mapped=mapping.TryGetValue(info.nameAnimation,out profile);
                male.Set(info.pathMaleBase,info.paramMale.path,info.paramMale.lstIdLayer);female.Set(info.pathFemaleBase,info.paramFemale.path,info.paramFemale.lstIdLayer);second.Set(info.pathFemaleBase1,info.paramFemale1.path,info.paramFemale1.lstIdLayer);
                catalogStatus.AppendLine(NativeAudit.Tsv(info.mode,info.id,info.nameAnimation,mapped,profile,Name(male),Name(female),Name(second),""));
                foreach(string state in StateNames())
                {
                    bool a=male.Has(state),b=female.Has(state),c=second.Has(state);if(!a&&!b&&!c)continue;
                    states.AppendLine(NativeAudit.Tsv(info.mode,info.id,info.nameAnimation,profile,state,a,b,c,info.paramFemale.isAnal));
                    if(!mapped||!a||!(NativeProfiles.ActorIndex(profile)==1?c:b))continue;
                    var target=NativeProfiles.ActorIndex(profile)==1?second:female;
                    Analyze(info,profile,state,male,target,summary,raw);
                }
            }
            catch(Exception ex){log.AppendLine(info.mode+"/"+info.id+" "+info.nameAnimation+" ERROR "+ex.GetBaseException());}
            count++;File.WriteAllText(Path.Combine(NativeAudit.Output,"progress.txt"),count+" / "+records.Count+" animations; audio muted");
            yield return null;
        }
        raw.Dispose();File.WriteAllText(Path.Combine(NativeAudit.Output,"skeletal-summary.tsv"),summary.ToString(),Encoding.UTF8);File.WriteAllText(Path.Combine(NativeAudit.Output,"states.tsv"),states.ToString(),Encoding.UTF8);File.WriteAllText(Path.Combine(NativeAudit.Output,"mapping-runtime.tsv"),catalogStatus.ToString(),Encoding.UTF8);File.WriteAllText(Path.Combine(NativeAudit.Output,"skeletal-run.txt"),log.ToString());
        if(male!=null)UnityEngine.Object.Destroy(male.Root);if(female!=null)UnityEngine.Object.Destroy(female.Root);if(second!=null)UnityEngine.Object.Destroy(second.Root);
        foreach(var bundle in bundles.Values)if(bundle!=null)bundle.Unload(false);bundles.Clear();File.WriteAllText(Path.Combine(NativeAudit.Output,"complete.txt"),DateTime.Now.ToString("s")+" tested "+count+" animations");
    }
    private static string Name(Rig rig){return rig.Animator.runtimeAnimatorController==null?"none":rig.Animator.runtimeAnimatorController.name;}
    private static List<string> StateNames()
    {
        var result=new List<string>();foreach(var prefix in new[]{"","A_"})foreach(var state in new[]{"Idle","WLoop","SLoop","MLoop","OLoop","Insert","Pull","IN_Start","IN_Loop","OUT_Start","OUT_Loop","M_IN_Start","M_IN_Loop","M_OUT_Start","M_OUT_Loop","WF_IN_Start","WS_IN_Start","WS_IN_A","SF_IN_Start","SS_IN_Start","SS_IN_A","IN_A","OUT_A","WOrgasm","SOrgasm","Orgasm","Drink","Vomit"})result.Add(prefix+state);return result;
    }
    private static void Analyze(HSceneProc.AnimationListInfo info,NativeTarget profile,string state,Rig male,Rig target,StringBuilder output,StreamWriter raw)
    {
        var min=new double[]{double.MaxValue,double.MaxValue,double.MaxValue,double.MaxValue,double.MaxValue,double.MaxValue};var max=new double[]{double.MinValue,double.MinValue,double.MinValue,double.MinValue,double.MinValue,double.MinValue};
        var closest=new double[targets.Length];double mappedDist=0,priorR0=0,step=0,rootMove=0,targetMove=0;int frames=0,clamped=0;string error="";MotionVector root0=new MotionVector(),target0=new MotionVector();var continuity=new ContinuousAngle(180);
        try{
            for(int i=0;i<=60;i++){
                male.Pose(state,i/60f);target.Pose(state,i/60f);
                var sample=ActorMeasurement.Sample(male.Actor,target.Actor,profile,1);sample.R0Continuity=continuity;var values=NativeMotion.Solve(sample);
                if(i==0){root0=sample.Origin;target0=sample.Target;}else{rootMove=Math.Max(rootMove,(sample.Origin-root0).Length);targetMove=Math.Max(targetMove,(sample.Target-target0).Length);step=Math.Max(step,Math.Abs(values.Raw[3]-priorR0));}priorR0=values.Raw[3];
                for(int axis=0;axis<6;axis++){min[axis]=Math.Min(min[axis],values.Normalized[axis]);max[axis]=Math.Max(max[axis],values.Normalized[axis]);}
                if(values.Normalized[0]<=0||values.Normalized[0]>=1)clamped++;mappedDist+=(sample.Target-sample.Origin).Length;
                if(i%10==0)for(int t=0;t<targets.Length;t++)try{var p=ActorMeasurement.Sample(male.Actor,target.Actor,targets[t],1);var d=p.Target-p.Origin;var shaft=p.End-p.Origin;double along=Numbers.Clamp(MotionVector.Dot(d,shaft)/(shaft.Length*shaft.Length),0,1);closest[t]+=(d-shaft*along).Length;}catch{closest[t]=double.MaxValue;}
                raw.WriteLine(NativeAudit.Tsv(info.mode,info.id,info.nameAnimation,profile,state,i,values.Normalized[0],values.Normalized[1],values.Normalized[2],values.Raw[3],values.Raw[4],values.Raw[5],values.Length,sample.Origin.X,sample.Origin.Y,sample.Origin.Z,sample.Target.X,sample.Target.Y,sample.Target.Z));frames++;
            }
        }catch(Exception ex){error=ex.GetBaseException().Message;}
        int best=0;for(int i=1;i<closest.Length;i++)if(closest[i]<closest[best])best=i;
        output.AppendLine(NativeAudit.Tsv(info.mode,info.id,info.nameAnimation,profile,state,male.Has(state),target.Has(state),frames,min[0],max[0],frames==0?0:(double)clamped/frames,max[1]-min[1],max[2]-min[2],max[3]-min[3],max[4]-min[4],max[5]-min[5],step,rootMove,targetMove,targets[best],closest[best]/7,frames==0?0:mappedDist/frames,error));
    }
}
