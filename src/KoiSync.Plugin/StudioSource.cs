using System;
using System.Collections;
using System.Collections.Generic;
using KoiSync.Core;
using KoiSync.Wire;
using UnityEngine;

namespace KoiSync.Plugin
{
    internal sealed class StudioSource
    {
        internal StudioObject Source,Target;
        internal StudioLink Link;
        internal Transform Entry;
        internal Transform SourceAttachment,TargetAttachment;
        internal DynamicBoneCollider Collider;
        internal DynamicBone TargetPhysics;
        internal bool RequiresPhysics;
        internal bool Implicit;
        internal StudioGraph Graph;
        internal NativeTarget Profile;
        internal bool Bp;
        internal string Id,Label,Error="",Playback="";
        private ShaftReference shaft;
        private readonly ContinuousAngle shaftYaw=new ContinuousAngle(360);
        internal ShaftReference Geometry{get{return shaft??(shaft=ShaftGeometry.Resolve(this));}}
        internal string SavedKey
        {
            get{return Source.Id+"|"+Target.Id+"|"+(IsCollider?"collider":Bp?"bp":"chain")+"|"+Profile+"|"+(Link==null?"implicit|":NodeKey(Link.Parent)+"|"+NodeKey(Link.Child))+"|"+(Collider==null?"":NodeKey(Collider.transform));}
        }
        private string NodeKey(Transform node)
        {
            StudioObject owner=Graph.Owner(node);if(owner==null)return "missing";
            string path="";for(Transform p=node;p!=null&&p!=owner.Root;p=p.parent)
            {
                int ordinal=0;if(p.parent!=null)for(int i=0;i<p.GetSiblingIndex();i++)if(p.parent.GetChild(i).name==p.name)ordinal++;
                path="/"+p.name+"["+ordinal+"]"+path;
            }
            return owner.Id+path;
        }
        internal bool Live
        {
            get
            {
                if(!Source.Live||!Target.Live||!Implicit&&(Link==null||!Link.Live))return false;
                Transform anchor;
                if(SourceActor==Target)return false;
                if(SourceAttachment!=null&&(Graph.AttachedActor(SourceAttachment,out anchor,Link)!=Source||!StudioAdapter.AxialAnchor(Source,anchor)))return false;
                if(TargetAttachment!=null&&Graph.AttachedActor(TargetAttachment,out anchor,Link)!=Target)return false;
                if(RequiresPhysics&&TargetPhysics==null)return false;
                if(Collider!=null)return Collider.isActiveAndEnabled&&Collider.m_Bound==DynamicBoneCollider.Bound.Outside&&
                    ColliderOriginValid&&(TargetPhysics==null||TargetPhysics.isActiveAndEnabled&&TargetPhysics.m_Colliders!=null&&TargetPhysics.m_Colliders.Contains(Collider));
                return Source.HasChain&&(!Bp||Source.Bp!=null&&(Source.Bp as Behaviour).isActiveAndEnabled&&StudioMembers.Flag(Source.Bp,"danTargetsValid",false));
            }
        }
        internal bool IsCollider;
        internal Candidate Sample(double gain,double lengthScale,bool reverse,NativeTarget chosen,bool sceneActive)
        {
            Error="";
            try
            {
                if(IsCollider&&Collider==null)throw new ArgumentException("碰撞体已移除");
                if(!Live)throw new ArgumentException("绑定停用、对象失效或目标已解绑");
                var sample=new NativeMotionSample{Gain=gain,Studio=true};
                MotionVector[] path;double radius=0;MotionVector capA=new MotionVector(),capB=new MotionVector();
                if(IsCollider)
                {
                    ColliderFrame(Collider,reverse,sample,out capA,out capB,out radius);
                    path=new[]{sample.Origin,sample.End};
                }
                else
                {
                    Transform[] nodes=Bp?BpChain(Source.Bp):null;
                    if(nodes==null||nodes.Length<2)
                    {
                        Transform start=Source.Bones.Get("cm_J_dan100_00","cm_J_dan101_00"),end=Source.Bones.Get("cm_J_dan109_00");
                        if(start==null||end==null)throw new ArgumentException("缺少实际来源根端 / 末端骨骼");
                        var chain=new List<Transform>{start};
                        for(int n=101;n<=109;n++){Transform node=Source.Bones.Get("cm_J_dan"+n+"_00");if(node!=null&&!chain.Contains(node))chain.Add(node);}
                        if(chain[chain.Count-1]!=end)chain.Add(end);nodes=chain.ToArray();
                    }
                    path=new MotionVector[nodes.Length];for(int i=0;i<nodes.Length;i++){if(nodes[i]==null)throw new ArgumentException("来源骨骼已更换");path[i]=V(nodes[i].position);}
                    sample.Origin=path[0];sample.End=path[path.Length-1];
                    Transform left=Source.Bones.Get("cm_J_dan_f_L"),right=Source.Bones.Get("cm_J_dan_f_R");
                    sample.PlanePoint=left!=null&&right!=null?(V(left.position)+V(right.position))*.5:sample.Origin+V(nodes[0].up);
                }
                var geometry=Geometry;Transform root=geometry.Root;
                if(root==null)throw new ArgumentException("未定位来源挂载骨节点");
                sample.Origin=V(root.position);
                sample.End=geometry.End(Graph,reverse);
                sample.End=sample.Origin+(sample.End-sample.Origin)*lengthScale;
                sample.EndLabel=geometry.Native?"实际骨骼链末端":geometry.DrivenChain!=null?geometry.BoneModelName+" · 约束动骨末端":geometry.Model.Name+" · 网格端部估测";
                if(geometry.DrivenChain!=null){path=new MotionVector[geometry.DrivenChain.Length];for(int i=0;i<path.Length;i++)path[i]=V(geometry.DrivenChain[i].position);}
                else if(!geometry.Native||IsCollider)path=new[]{sample.Origin,sample.End};
                // A character shaft with an explicit entry constraint already tells us
                // both which actor it is attached to and which anatomical opening it uses.
                // A persisted global target must not redirect that relationship (for
                // example, a vaginal BP binding to the anal fallback). Manual targets are
                // retained for generic collider/helper sources that have no shaft binding.
                bool boundShaftTarget=!IsCollider&&(Entry!=null||Link!=null||Implicit);
                bool manualTarget=chosen!=NativeTarget.AUTO&&!boundShaftTarget;
                NativeTarget profile=manualTarget?NativeProfiles.Primary(chosen):Profile;
                Transform point=manualTarget?null:Entry;
                if(point==null&&profile==NativeTarget.VAGINAL)point=Target.Bones.Get("cf_J_Vagina_root","cf_j_vagina_root");
                if(point!=null)sample.Target=V(point.position);
                else if(profile==NativeTarget.VAGINAL||profile==NativeTarget.ANAL)MainGameAdapter.TargetFrame(Target.Bones,profile,sample);
                else sample.Target=V(Target.Root.position); // Contact-specific landmarks are filled below.
                sample.ShaftPath=path;
                sample.ShaftRestLength=Bp?BpRestLength(Source.Bp,path):StudioShaftMotion.PathLength(path);
                SetBodyFrame(sample,profile);
                StudioContactTargets.Configure(SourceActor,Target,profile,sample);
                sample.RotationBasis=profile+":"+root.GetInstanceID()+":"+geometry.Identity;
                var tilt=StudioRootMotion.TiltVector(sample);sample.TargetX=tilt.Length>1e-7?tilt.Unit():sample.TargetReferenceFrame.Y;sample.TargetZ=sample.TargetReferenceFrame.Z;
                NativeMotion.Solve(sample);
                bool timeline=Graph.Driven(Source)||Graph.Driven(Target)||Link!=null&&TimelineState.DrivesParameter(Link.Handle);
                bool playing=timeline?TimelineState.Playing:AnimatorActive(Source.Actor)||AnimatorActive(Target.Actor)||ItemAnimatorActive(Source)||ItemAnimatorActive(Target);
                Playback=timeline?"Timeline 绑定链":playing?"工作室动画":"绑定就绪 · 等待动画 / Timeline";
                return new Candidate{Id=Id,Label=Label,ReferenceBone=root.name+(geometry.Native?" · 原生根点":" · 挂载根点"),TargetBone=sample.Contact!=null?profile+" · "+sample.Contact.Name:point==null?profile+(manualTarget?" · 手动目标":" · 绑定目标"):boundShaftTarget?profile+" · 吸附目标 · "+point.name:point.name,
                    Reference=P(sample.Origin),Target=P(sample.Target),Native=sample,Active=sceneActive&&playing,
                    MotionKey="studio:"+Id+":"+sample.RotationBasis+":"+gain+":"+lengthScale+":"+reverse,
                    ReferencePath=path,ColliderA=capA,ColliderB=capB,ColliderRadius=radius};
            }
            catch(ArgumentException ex){Error=ex.Message;return null;}
        }
        private static bool AnimatorActive(ChaControl actor)
        {return actor!=null&&actor.animBody!=null&&actor.animBody.isActiveAndEnabled&&actor.animBody.speed!=0;}
        private void SetBodyFrame(NativeMotionSample sample,NativeTarget profile)
        {
            StudioObject owner=SourceActor;
            Transform fallback=Collider!=null?Collider.transform:Source.Root;
            string label;
            sample.BodyFrame=BodyFrame(owner,fallback,From(sample.Origin),out label);sample.FrameLabel="蓝面："+(owner==null?Source.Name:owner.DisplayName)+" · "+label;
            sample.ReferenceFrame=sample.BodyFrame;sample.HasBodyFrame=true;
            sample.AnchorRoot=sample.Origin;sample.AnchorEnd=sample.End;sample.AnchorTarget=sample.Target;
            if(Target!=null&&Target.Character){sample.TargetBodyFrame=BodyFrame(Target,Target.Root,From(sample.Target),out label);sample.TargetReferenceFrame=sample.TargetBodyFrame;sample.HasTargetFrame=true;sample.FrameLabel+="\n粉线："+Target.DisplayName;}
            if(!sample.HasTargetFrame)throw new ArgumentException("缺少目标身体参考框架");
            sample.TargetHip=sample.TargetBodyFrame.Origin;
            sample.BodyFrame.Origin=sample.ReferenceFrame.Origin=sample.Origin;
            sample.TargetBodyFrame.Origin=sample.TargetReferenceFrame.Origin=sample.Target;
            sample.UseReferenceUp=true;sample.RootDistance=true;
            if(!IsCollider&&(profile==NativeTarget.VAGINAL||profile==NativeTarget.ANAL)&&owner!=null&&owner.Character&&Target!=null&&Target.Character)
            {
                ConfigureShaftFrames(sample,owner,Target,profile,shaftYaw);
            }
        }
        internal Transform MeasurementRoot()
        {
            return Geometry.Root;
        }
        internal bool ColliderOriginValid
        {
            get
            {
                try{if(!Geometry.Native)return true;}catch(ArgumentException){return false;}
                Transform anchor;var owner=Graph.AttachedActor(Source.Root,out anchor);
                return owner!=null&&owner.HasChain&&StudioAdapter.AxialAnchor(owner,anchor);
            }
        }
        internal bool OwnsModelNode(Transform node)
        {
            var owner=Graph.Owner(node);
            for(var obj=Source;obj!=null&&!obj.Character;obj=obj.Parent)
            {
                foreach(var actor in Graph.Objects)if(actor.Character&&actor.Root.IsChildOf(obj.Root))return false;
                if(obj==owner)return true;
            }
            return false;
        }
        internal StudioObject SourceActor
        {
            get
            {
                if(Source.Character)return Source;
                Transform anchor;return Graph==null?null:Graph.AttachedActor(Source.Root,out anchor);
            }
        }
        internal string PairKey{get{var owner=SourceActor;return (owner??Source).Id+":"+Target.Id;}}
        internal string PairLabel
        {
            get
            {
                var owner=SourceActor;
                if(owner==null)return Target.DisplayName+" x 道具 "+Source.Name;
                return owner.Sex==1&&Target.Sex!=1?owner.DisplayName+" x "+Target.DisplayName:Target.DisplayName+" x "+owner.DisplayName;
            }
        }
        internal string NodeLabel{get{return (shaft!=null&&!shaft.Native?shaft.Label:Bp?"BP":IsCollider?"DynamicBoneCollider":"角色骨骼")+" · "+Profile+" · "+(Entry!=null?Entry.name:IsCollider?(Collider!=null?Collider.name:"节点已移除"):"实际骨骼链");}}
        internal static AlignmentFrame BodyFrame(StudioObject owner,Transform fallback,Vector3 center,out string label,bool nativePelvis=false)
        {
            Vector3 up=fallback.up,right=fallback.right;label="道具自身参考朝向";
            if(owner!=null)
            {
                Transform hips=owner.Bones.Get("cf_j_hips","cm_J_hips","cm_j_hips"),spine=owner.Bones.Get("cf_j_spine01","cm_J_spine01","cm_j_spine01","cf_j_spine02");
                Transform left=owner.Bones.Get("cf_j_thigh00_L","cm_J_thigh00_L","cm_j_thigh00_L"),r=owner.Bones.Get("cf_j_thigh00_R","cm_J_thigh00_R","cm_j_thigh00_R");
                if(hips!=null){center=hips.position;up=nativePelvis?hips.forward:hips.up;right=hips.right;label="腰部骨骼 · "+hips.name;}
                if(!nativePelvis&&hips!=null&&spine!=null&&(spine.position-hips.position).sqrMagnitude>1e-10f)up=(spine.position-hips.position).normalized;
                // Native thighs belong to waist02, below the hips/spine animation root.
                // Their reference plane must follow that pelvis joint when the torso bends.
                Transform pelvis=nativePelvis?owner.Bones.Get("cf_j_waist02","cm_J_waist02","cm_j_waist02"):null;
                if(pelvis!=null){center=pelvis.position;up=pelvis.forward;right=pelvis.right;label="盆骨参考 · "+pelvis.name;}
                if(left!=null&&r!=null&&(r.position-left.position).sqrMagnitude>1e-10f)right=(r.position-left.position).normalized;
            }
            if(Vector3.Cross(right,up).sqrMagnitude<1e-8f)right=Vector3.Cross(up,fallback.forward);
            if(Vector3.Cross(right,up).sqrMagnitude<1e-8f)right=Vector3.Cross(up,Mathf.Abs(up.x)<.9f?Vector3.right:Vector3.forward);
            return AlignmentFrame.Build(V(center),V(right),V(up));
        }
        internal static void ConfigureShaftFrames(NativeMotionSample sample,StudioObject owner,StudioObject target,NativeTarget profile,ContinuousAngle yaw)
        {
            string label;
            // waist02.forward is the native pelvis mounting normal. Do not read
            // the BP-reaimed root segment or the character's world rig axes.
            var mount=BodyFrame(owner,owner.Root,From(sample.Origin),out label,true);
            // Keep Unity LH (X=UserRight, Y=ShaftUp, Z=UserFwd) directly so L1, R1, and R2
            // match physical SR6 TCode v0.3 / nb-3d-simulator directions (L2 negates X in StudioShaftMotion).
            mount.Origin=sample.Origin;sample.BodyFrame=sample.ReferenceFrame=mount;
            Transform opening=profile==NativeTarget.VAGINAL?target.Bones.Get("cf_J_Vagina_root","cf_j_vagina_root"):target.Bones.Get("cf_j_ana","cf_J_Ana_Root");
            Transform pelvis=target.Bones.Get("cf_j_waist02","cf_j_hips","cm_J_hips","cm_j_hips");
            if(opening==null&&pelvis==null)throw new ArgumentException("Missing target mounting frame");
            MotionVector right=V(opening!=null?opening.right:pelvis.right);
            MotionVector inward=V(opening!=null?opening.up:pelvis.forward);
            string directionLabel="入口局部方向";
            Transform entryGuide=StudioMembers.Read(owner.Bp,"danEntryChild") as Transform;
            Transform endGuide=StudioMembers.Read(owner.Bp,"danEndChild") as Transform;
            if(entryGuide!=null&&endGuide!=null&&(V(entryGuide.position)-sample.Target).Length<.02)
            {
                MotionVector guide=V(endGuide.position-entryGuide.position);
                if(guide.Length>1e-5){inward=guide;directionLabel="BP entry/end";}
            }
            if(MotionVector.Cross(right,inward).Length<1e-7)right=mount.X;
            if(MotionVector.Cross(right,inward).Length<1e-7)right=mount.Z;
            sample.TargetBodyFrame=sample.TargetReferenceFrame=AlignmentFrame.Build(sample.Target,right,inward);
            sample.SourceRootDirection=mount.Y;
            var path=sample.ShaftPath;
            sample.SourceDeformedDirection=path!=null&&path.Length>1?path[path.Length-1]-path[path.Length-2]:mount.Y;
            if(!Numbers.Finite(sample.ShaftRestLength)||sample.ShaftRestLength<1e-7)sample.ShaftRestLength=StudioShaftMotion.PathLength(path);
            sample.HasShaftPose=true;sample.ShaftYaw=yaw;
            sample.FrameLabel="蓝面："+label+" · 设备安装框架\n粉线："+directionLabel+" · 位移/旋转共用参考";
        }
        private static Vector3 From(MotionVector p){return new Vector3((float)p.X,(float)p.Y,(float)p.Z);}
        private static bool ItemAnimatorActive(StudioObject obj)
        {Studio.OCIItem item=obj.Control as Studio.OCIItem;return item!=null&&item.animator!=null&&item.animator.isActiveAndEnabled&&item.animator.speed!=0&&item.animeSpeed!=0;}
        internal static Transform[] BpChain(Component controller)
        {
            if(controller==null)return null;
            object agent=StudioMembers.Read(controller,"danAgent"),points=StudioMembers.Read(agent,"m_danPoints");
            IEnumerable list=StudioMembers.Read(points,"danPoints") as IEnumerable;if(list==null)return null;
            var transforms=new List<Transform>();foreach(object point in list){Transform t=StudioMembers.Read(point,"transform") as Transform;if(t==null)return null;transforms.Add(t);}return transforms.ToArray();
        }
        internal static double BpRestLength(Component controller,MotionVector[] path)
        {
            object agent=StudioMembers.Read(controller,"danAgent"),value=StudioMembers.Read(agent,"m_baseDanLength");
            try
            {
                double length=Convert.ToDouble(value,System.Globalization.CultureInfo.InvariantCulture);
                if(Numbers.Finite(length)&&length>1e-7)return length;
            }
            catch(Exception){}
            return StudioShaftMotion.PathLength(path);
        }
        internal static void ColliderFrame(DynamicBoneCollider collider,bool reverse,NativeMotionSample sample,out MotionVector capA,out MotionVector capB,out double radius)
        {
            // Match this game's DynamicBoneCollider.Collide, including its radius/height and Z scale convention.
            Transform t=collider.transform;Vector3 axis=collider.m_Direction==DynamicBoneCollider.Direction.X?Vector3.right:collider.m_Direction==DynamicBoneCollider.Direction.Y?Vector3.up:Vector3.forward;
            float half=Mathf.Max(0,(collider.m_Height-collider.m_Radius)*.5f);
            Vector3 a=t.TransformPoint(collider.m_Center-axis*half),b=t.TransformPoint(collider.m_Center+axis*half);
            Vector3 direction=(b-a).sqrMagnitude>1e-12f?(b-a).normalized:t.TransformDirection(axis).normalized;
            radius=Math.Abs(collider.m_Radius*t.lossyScale.z);if(!Numbers.Finite(radius)||radius<=1e-7)throw new ArgumentException("碰撞体半径无效");
            capA=V(a);capB=V(b);sample.Origin=V(a-direction*(float)radius);sample.End=V(b+direction*(float)radius);
            if(reverse){MotionVector swap=sample.Origin;sample.Origin=sample.End;sample.End=swap;}
            sample.PlanePoint=sample.Origin+V(t.TransformDirection(collider.m_Direction==DynamicBoneCollider.Direction.Y?Vector3.forward:Vector3.up));
        }
        internal static NativeTarget InferProfile(string name)
        {
            string n=(name??"").ToLowerInvariant();int suffix=n.IndexOf('(');if(suffix>=0)n=n.Substring(0,suffix).TrimEnd();
            if(n.Contains("mouth")||n.Contains("head")||n.Contains("face"))return NativeTarget.ORAL;
            if(n.Contains("ana"))return NativeTarget.ANAL;
            if(n.Contains("bust")||n.Contains("mune")||n.Contains("bnip"))return NativeTarget.BREASTS;
            if(n.Contains("hand")||n.Contains("finger"))return n.EndsWith("_l")?NativeTarget.LEFTHAND:NativeTarget.RIGHTHAND;
            if(n.Contains("foot")||n.Contains("toes")||n=="cf_t_leg_l"||n=="cf_t_leg_r")return n.EndsWith("_l")?NativeTarget.LEFTFOOT:NativeTarget.RIGHTFOOT;
            return NativeTarget.VAGINAL;
        }
        internal static MotionVector V(Vector3 p){return new MotionVector(p.x,p.y,p.z);}
        private static Pose P(MotionVector p){return new Pose{X=(float)p.X,Y=(float)p.Y,Z=(float)p.Z};}
    }
}
