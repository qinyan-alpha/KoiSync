using System;
using System.Collections.Generic;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.Plugin
{
    internal static class ActorMeasurement
    {
        internal static StudioObject Actor(ChaControl actor,BoneMap bones)
        {
            var result=new StudioObject{Id=actor.GetInstanceID(),Root=actor.transform,Actor=actor,Bones=bones,Character=true,Sex=actor.sex,Name=actor.name,CardName=actor.chaFile==null?actor.name:actor.chaFile.parameter.fullname};
            foreach(Component component in actor.transform.GetComponents<Component>())
                if(component!=null&&component.GetType().FullName=="Core_BetterPenetration.BetterPenetrationController"){result.Bp=component;break;}
            return result;
        }
        internal static NativeMotionSample Sample(StudioObject source,StudioObject target,NativeTarget profile,double gain,ContinuousAngle shaftYaw=null)
        {
            var start=source.Bones.Get("cm_J_dan100_00","cm_J_dan101_00");var end=source.Bones.Get("cm_J_dan109_00");
            if(start==null||end==null)throw new ArgumentException("缺少本体来源根端 / 末端骨骼");
            Transform[] nodes=StudioSource.BpChain(source.Bp);
            if(nodes==null||nodes.Length<2)
            {
                var chain=new List<Transform>{start};
                for(int n=101;n<=109;n++){Transform node=source.Bones.Get("cm_J_dan"+n+"_00");if(node!=null&&!chain.Contains(node))chain.Add(node);}
                if(chain[chain.Count-1]!=end)chain.Add(end);nodes=chain.ToArray();
            }
            var path=new MotionVector[nodes.Length];for(int i=0;i<nodes.Length;i++)path[i]=StudioSource.V(nodes[i].position);
            var s=new NativeMotionSample{Origin=path[0],End=path[path.Length-1],Gain=gain,RootDistance=true,EndLabel="骨骼链末端 · 来源 "+source.Root.name,ShaftPath=path};
            profile=NativeProfiles.Primary(profile);
            Transform point=null;
            if(profile==NativeTarget.VAGINAL)point=target.Bones.Get("cf_J_Vagina_root","cf_j_vagina_root","cf_j_kokan","a_n_kokan","cf_n_pee");
            else if(profile==NativeTarget.ANAL)point=target.Bones.Get("cf_j_ana");
            if((profile==NativeTarget.VAGINAL||profile==NativeTarget.ANAL)&&point==null)throw new ArgumentException("缺少目标根节点："+profile);
            s.Target=StudioSource.V(point==null?target.Root.position:point.position);
            string label;
            s.BodyFrame=StudioSource.BodyFrame(source,source.Root,start.position,out label,true);s.ReferenceFrame=s.BodyFrame;s.FrameLabel="蓝面："+source.DisplayName+" · "+label;
            s.TargetBodyFrame=StudioSource.BodyFrame(target,target.Root,AlignmentGizmo.V(s.Target),out label,true);s.TargetReferenceFrame=s.TargetBodyFrame;s.TargetHip=s.TargetBodyFrame.Origin;
            s.AnchorRoot=s.Origin;s.AnchorEnd=s.End;s.AnchorTarget=s.Target;
            s.BodyFrame.Origin=s.ReferenceFrame.Origin=s.Origin;s.TargetBodyFrame.Origin=s.TargetReferenceFrame.Origin=s.Target;
            s.HasBodyFrame=s.HasTargetFrame=s.UseReferenceUp=true;
            if(profile==NativeTarget.VAGINAL||profile==NativeTarget.ANAL)
            {
                s.ShaftRestLength=StudioSource.BpRestLength(source.Bp,path);
                StudioSource.ConfigureShaftFrames(s,source,target,profile,shaftYaw);
            }
            StudioContactTargets.Configure(source,target,profile,s);
            s.RotationBasis=profile+":"+start.GetInstanceID()+":"+end.GetInstanceID();
            var tilt=StudioRootMotion.TiltVector(s);s.TargetX=tilt.Length>1e-7?tilt.Unit():s.TargetReferenceFrame.Y;s.TargetZ=s.TargetReferenceFrame.Z;
            return s;
        }
    }
}
