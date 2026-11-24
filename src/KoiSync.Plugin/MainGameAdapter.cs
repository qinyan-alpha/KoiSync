// Target-frame definitions adapted from KKS_SexRobotController under MIT.
// Copyright (c) 2021 hs2robotics; Copyright (c) 2026 illusive-groax.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using KoiSync.Core;
using KoiSync.Wire;
using UnityEngine;
using Object=UnityEngine.Object;

namespace KoiSync.Plugin
{
    internal sealed class MainGameAdapter:ISceneAdapter
    {
        private HFlag flags;
        private Component scene;
        private readonly bool officialVr;
        internal readonly NativePlayback Playback=new NativePlayback();
        private readonly ContinuousAngle shaftYaw=new ContinuousAngle(360);
        private string shaftYawKey="";
        internal MainGameAdapter(bool officialVr=false){this.officialVr=officialVr;}
        private bool SceneEnded{get{return scene==null||StudioMembers.Flag(scene,"isEnd",true);}}
        private readonly Dictionary<int,BoneMap> maps=new Dictionary<int,BoneMap>();
        public string Context{get{return officialVr?"KoikatuVR":"Koikatu";}}
        public List<ActorPair> Pairs{get;private set;}=new List<ActorPair>();
        public string State{get{return flags==null?"等待本体场景":flags.mode+"/"+(flags.nowAnimationInfo==null?"loading":flags.nowAnimationInfo.nameAnimation)+"/"+flags.nowAnimStateName;}}
        public bool Active{get{return flags!=null&&!SceneEnded&&scene.gameObject.activeInHierarchy;}}
        public void Refresh()
        {
            if(scene==null)
            {
                Type type=officialVr?StudioMembers.FindType("VRHScene"):typeof(HSceneProc);
                if(type!=null)scene=Object.FindObjectOfType(type) as Component;
            }
            // Official VR owns its actors and flags on VRHScene; an arbitrary HFlag may belong to a previous scene.
            flags=scene==null?null:StudioMembers.Read(scene,"flags") as HFlag;
            if(flags==null&&!officialVr&&scene!=null)flags=Object.FindObjectOfType<HFlag>();
            maps.Clear();
        }
        private BoneMap Map(ChaControl actor)
        {
            BoneMap map;int id=actor.GetInstanceID();
            if(!maps.TryGetValue(id,out map)){map=new BoneMap(actor.transform);maps[id]=map;}return map;
        }
        private static Transform Required(BoneMap map,params string[] names)
        {
            Transform t=map.Get(names);if(t==null)throw new ArgumentException("缺少节点："+string.Join(" / ",names));return t;
        }
        private static MotionVector Point(BoneMap map,params string[] names){return V(Required(map,names).position);}
        private static MotionVector V(Vector3 p){return new MotionVector(p.x,p.y,p.z);}
        private static Pose P(MotionVector p){return new Pose{X=(float)p.X,Y=(float)p.Y,Z=(float)p.Z};}
        internal void Sample(Snapshot frame,NativeSettings settings,PlaybackProfiles profiles=null,MeasurementProfile preview=null)
        {
            Pairs.Clear();settings.CurrentAnimation=flags==null||flags.nowAnimationInfo==null?"":flags.nowAnimationInfo.nameAnimation;settings.CurrentProfile="—";
            settings.SamplingStatus="等待本体角色";
            if(profiles!=null&&flags!=null&&flags.nowAnimationInfo!=null)profiles.NativeKey=flags.mode+" / "+flags.nowAnimationInfo.nameAnimation;
            if(flags==null||SceneEnded||!scene.gameObject.activeInHierarchy||flags.nowAnimationInfo==null){Playback.Release();frame.Active=false;frame.State=State+" · "+settings.SamplingStatus;return;}
            try
            {
                MeasurementProfile preset=preview??(profiles==null?null:profiles.NativeMeasurement);
                NativeTarget profile;
                if(preview!=null&&preview.Target!=NativeTarget.AUTO)profile=preview.Target;
                else if(settings.Override.Value!=NativeTarget.AUTO)profile=settings.Override.Value;
                else if(preset!=null&&preset.Target!=NativeTarget.AUTO)profile=preset.Target;
                else if(!settings.Resolve(flags.nowAnimationInfo.nameAnimation,out profile))throw new ArgumentException("动画未收录 · 在动画 / 对齐页指定数据源后保存");
                settings.CurrentProfile=profile.ToString();int index=NativeProfiles.ActorIndex(profile);
                ChaControl reference,target;
                if(!TryActors(scene,flags,officialVr,index,out reference,out target))throw new ArgumentException("映射指定的角色不存在");
                if(!reference.gameObject.activeInHierarchy||!target.gameObject.activeInHierarchy)throw new ArgumentException("角色暂未激活");
                string poseStatus;
                if(!TryPosedActors(reference,target,out poseStatus))throw new ArgumentException(poseStatus);
                Pairs.Add(new ActorPair(reference,target));BoneMap a=Map(reference),b=Map(target);
                Transform start=Required(a,"cm_J_dan100_00","cm_J_dan101_00"),end=Required(a,"cm_J_dan109_00");
                string yawKey=reference.GetInstanceID()+":"+target.GetInstanceID()+":"+profile;
                if(yawKey!=shaftYawKey){shaftYawKey=yawKey;shaftYaw.Reset();}
                var sample=ActorMeasurement.Sample(ActorMeasurement.Actor(reference,a),ActorMeasurement.Actor(target,b),profile,1,shaftYaw);
                NativeMotion.Solve(sample); // Reject incomplete or collinear reference frames before exposing a candidate.
                for(int i=0;i<6;i++){sample.Minimum[i]=settings.Minimum[i].Value;sample.Maximum[i]=settings.Maximum[i].Value;}
                Animator animator=target.animBody;
                var participants=new List<Animator>{reference.animBody,target.animBody};
                IList females=StudioMembers.Read(scene,"lstFemale") as IList;if(females!=null)foreach(var obj in females){var actor=obj as ChaControl;if(actor!=null)participants.Add(actor.animBody);}
                var secondMale=StudioMembers.Read(scene,"male1") as ChaControl;if(secondMale!=null)participants.Add(secondMale.animBody);
                Playback.Bind(flags.mode+":"+flags.nowAnimationInfo.nameAnimation+":"+flags.nowAnimStateName+":"+animator.runtimeAnimatorController.GetInstanceID(),flags.nowAnimationInfo.nameAnimation+" / "+flags.nowAnimStateName,animator,participants);
                sample.AnimationState=flags.nowAnimStateName;
                sample.AnimationDuration=animator.GetCurrentAnimatorStateInfo(0).length;
                sample.StableAnimation=!animator.IsInTransition(0)&&MatchesState(animator.GetCurrentAnimatorStateInfo(0),flags.nowAnimStateName);
                sample.AnimationProgress=animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                sample.AnimationSeekRevision=Playback.SeekRevision;
                bool playing=frame.Active&&reference.animBody.enabled&&reference.animBody.speed!=0&&animator.enabled&&animator.speed!=0&&
                    (MatchesState(animator.GetCurrentAnimatorStateInfo(0),flags.nowAnimStateName)||
                     animator.IsInTransition(0)&&MatchesState(animator.GetNextAnimatorStateInfo(0),flags.nowAnimStateName));
                string controller=animator==null||animator.runtimeAnimatorController==null?"none":animator.runtimeAnimatorController.GetInstanceID().ToString();
                var c=new Candidate{Id=reference.GetInstanceID()+":"+target.GetInstanceID()+":"+profile,Label="本体 · "+reference.name+" → "+target.name+" · "+profile,ReferenceBone=start.name+" · 来源 "+reference.name,TargetBone=profile+(sample.Contact==null?" · 根点":" · "+sample.Contact.Name)+" · 目标 "+target.name,Reference=P(sample.Origin),Target=P(sample.Target),Native=sample,Active=playing,
                    MotionKey=flags.mode+":"+flags.nowAnimationInfo.nameAnimation+":"+RuntimeHelpers.GetHashCode(flags.nowAnimationInfo)+":"+controller+":"+start.GetInstanceID()+":"+end.GetInstanceID()};
                if(preset!=null){preset.UpgradeStudio(sample);preset.Apply(c);}
                c.ReferencePath=sample.ShaftPath??new[]{sample.AnchorRoot,sample.AnchorEnd};
                frame.Candidates.Add(c);settings.SamplingStatus=playing?(sample.Stroke!=null?"参考解算 · "+sample.Stroke.Phase(sample.AnimationState):"参考解算 · 动画播放中"):"参考解算 · 当前阶段未播放";
            }
            catch(ArgumentException ex){Playback.Release();frame.Active=false;settings.SamplingStatus=ex.Message;}
            frame.State=State+" · "+settings.SamplingStatus;
        }
        internal static bool TryActors(object owner,HFlag state,bool vr,int index,out ChaControl reference,out ChaControl target)
        {
            reference=null;target=null;if(index<0)return false;
            // Both HSceneProc and VRHScene own the actors used by the current animation.
            // Metadata can retain a previous actor after a replacement or scene transition.
            IList females=StudioMembers.Read(owner,"lstFemale") as IList;
            if(vr||females!=null)
            {
                reference=StudioMembers.Read(owner,"male") as ChaControl;
                if(females!=null&&index<females.Count)target=females[index] as ChaControl;
            }
            else if(state!=null)
            {
                reference=state.player==null?null:state.player.chaCtrl;
                if(state.lstHeroine!=null&&index<state.lstHeroine.Count&&state.lstHeroine[index]!=null)target=state.lstHeroine[index].chaCtrl;
            }
            return reference!=null&&target!=null&&reference!=target;
        }
        private static bool MatchesState(AnimatorStateInfo state,string name)
        {return !string.IsNullOrEmpty(name)&&(state.shortNameHash==Animator.StringToHash(name)||state.IsName(name));}
        internal static bool TryPosedActors(ChaControl reference,ChaControl target,out string status)
        {
            status="";
            if(reference==null||target==null||reference==target){status="交互双方角色无效";return false;}
            // A hidden actor may still have a fully animated body. Visibility is not a pose test.
            // Caress-only scenes keep an active male object with a null controller and rest-pose bones.
            if(reference.animBody==null||reference.animBody.runtimeAnimatorController==null){status="来源角色未加载身体动画 · 同步等待";return false;}
            if(target.animBody==null||target.animBody.runtimeAnimatorController==null){status="目标角色未加载身体动画 · 同步等待";return false;}
            return true;
        }
        internal static void TargetFrame(BoneMap b,NativeTarget profile,NativeMotionSample s)
        {
            MotionVector x,z,p;
            switch(profile)
            {
                case NativeTarget.VAGINAL:case NativeTarget.ANAL:case NativeTarget.INTERCRURAL:
                    MotionVector hip=Point(b,"cf_j_hips"),center=Point(b,"cf_j_kokan","a_n_kokan","cf_n_pee"),rear=Point(b,"cf_j_ana");
                    p=profile==NativeTarget.VAGINAL?center:profile==NativeTarget.ANAL?rear:hip;
                    x=profile==NativeTarget.INTERCRURAL?center-rear:hip-p;
                    z=MotionVector.Cross(x,profile==NativeTarget.INTERCRURAL?hip-center:rear-center);break;
                case NativeTarget.ORAL:
                    MotionVector lips=(Point(b,"cf_J_Mouthup")+Point(b,"cf_J_MouthLow"))*.5,right=Point(b,"cf_J_Mouth_R");
                    p=(Point(b,"cf_J_Mouth_L")+right)*.5;x=p-lips;
                    z=MotionVector.Cross(x,MotionVector.Cross(x,right-p));break;
                case NativeTarget.BREASTS:
                    MotionVector left=Point(b,"k_f_munenipL_00","cf_j_bnip02_L"),r=Point(b,"k_f_munenipR_00","cf_j_bnip02_R");
                    p=(left+r)*.5;MotionVector y=Point(b,"cf_d_bust00")-p;x=MotionVector.Cross(y,r-p);z=MotionVector.Cross(y,x);break;
                case NativeTarget.LEFTHAND:case NativeTarget.RIGHTHAND:
                    string hand=profile==NativeTarget.LEFTHAND?"L":"R";MotionVector middle=Point(b,"cf_j_middle01_"+hand);
                    p=Point(b,"a_n_hand_"+hand,"cf_j_hand_"+hand);x=middle-Point(b,"cf_j_ring01_"+hand);z=MotionVector.Cross(x,p-middle);break;
                case NativeTarget.LEFTFOOT:case NativeTarget.RIGHTFOOT:
                    string side=profile==NativeTarget.LEFTFOOT?"L":"R";Transform foot=Required(b,"cf_j_foot_"+side);
                    p=V(foot.position);x=Point(b,"cf_j_toes_"+side)-p;
                    // Upstream used an absolute world position as an axis. Use a local direction so moving the scene cannot change rotation.
                    z=MotionVector.Cross(x,V(foot.up));break;
                default:throw new ArgumentException("此测量配置尚未实现");
            }
            s.Target=p;s.TargetX=x.Unit();s.TargetZ=z.Unit();
        }
    }
}
