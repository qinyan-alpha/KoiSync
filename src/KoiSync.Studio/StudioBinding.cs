using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.Studio
{
    internal enum OpeningKind { Vaginal,Anal,Oral,LeftHand,RightHand,Breast,LeftHandVaginal,RightHandVaginal }

    // Created by an explicit Bind operation. Holds only this pair's transforms;
    // no scene graph, collider search, mesh baking or periodic rediscovery.
    internal sealed class StudioBinding
    {
        internal readonly ChaControl Source,Target;
        internal readonly Transform Root,Opening;
        internal readonly Transform[] Chain;
        internal readonly OpeningKind Kind;
        internal string Description {get{return (attached?UiText.T("跟随吸附 · "):UiText.T("指定角色 · "))+Name(Source)+" → "+Name(Target)+" · "+(IsHandSource?Root.name:Model==null?"BP":Model.DisplayName)+" · "+Opening.name;}}
        internal readonly StudioShaftModel Model;
        private readonly StudioHandSource handSource;
        internal bool IsHandSource{get{return handSource!=null;}}
        internal double RestLength {get;private set;}
        // Offset components follow the device axes: axial, L1, L2 (metres).
        internal MotionVector OriginOffset;
        internal MotionVector? ManualEndOffset;
        internal double L1HalfRange,L2HalfRange;
        private readonly Dictionary<string,Transform> sourceBones,targetBones;
        private readonly Component bp;
        private readonly StudioContactPoint contactPoint;
        private readonly bool attached,bpAttached;
        private readonly Transform entryProxy;
        private readonly Transform mount,sourceHips,leftThigh,rightThigh,targetPelvis;
        // Pelvis lateral lines have two equivalent directions. Euler swing
        // branches can exchange them by 180 degrees without any axial twist.
        // Unwrap that ambiguity while retaining continuous real twist motion.
        private readonly ContinuousAngle yaw=new ContinuousAngle(180);
        private readonly ShaftContactTracker contact=new ShaftContactTracker();
        private readonly MotionVector[] path;
        private long sequence;
        private bool twistBasisReady,twistHalfTurn;

        internal StudioBinding(ChaControl source,ChaControl requestedTarget,OpeningKind fallback,double physicalLength,bool preferAttachment=true,bool preserveRequestedTarget=false,StudioShaftModel model=null,bool preserveRequestedOpening=false)
        {
            if(source==null||requestedTarget==null||source==requestedTarget)throw new ArgumentException(UiText.T("请选择两个不同角色"));
            bool hand=StudioHandSource.IsHand(fallback);if(hand){preferAttachment=false;model=null;}
            Source=source;bp=hand?null:FindBp(source);Model=model;
            var actual=RuntimeAccess.Read(RuntimeAccess.Read(bp,"collisionAgent"),"m_collisionCharacter") as ChaControl;
            string bpOpening=Convert.ToString(RuntimeAccess.Read(bp,"danEntryParentName"));
            if(model==null&&preferAttachment&&actual!=null&&actual!=source&&(!preserveRequestedTarget||actual==requestedTarget)&&(!preserveRequestedOpening||Infer(bpOpening,fallback)==fallback)&&RuntimeAccess.Flag(bp,"danTargetsValid")) {Target=actual;attached=bpAttached=true;}
            else Target=requestedTarget;
            sourceBones=Bones(source.transform);targetBones=Bones(Target.transform);
            string bindingName=attached?Convert.ToString(RuntimeAccess.Read(bp,"danEntryParentName")):"";
            string candidateName=bindingName;Transform candidateProxy=null;
            Transform constrainedOpening=preferAttachment?FindExplicitEntry(sourceBones,Target,model,ref candidateName,out candidateProxy):null;
            if(constrainedOpening!=null&&preserveRequestedOpening&&Infer(candidateName,fallback)!=fallback)
            {constrainedOpening=null;candidateProxy=null;candidateName=bindingName;}
            bindingName=candidateName;entryProxy=candidateProxy;
            attached=attached||constrainedOpening!=null;
            Kind=Infer(bindingName,fallback);
            // An entry proxy is authoritative. A constraint on an intermediate
            // prop bone identifies the pair/type, not the anatomical entrance.
            Opening=entryProxy!=null?constrainedOpening:Kind==OpeningKind.Oral||model!=null?OpeningBone(targetBones,Kind):constrainedOpening??(!string.IsNullOrEmpty(bindingName)?Get(targetBones,bindingName):null)??OpeningBone(targetBones,Kind);
            if(Opening==null)throw new ArgumentException(UiText.T("目标缺少对应入口节点"));
            contactPoint=new StudioContactPoint(targetBones,hand?OpeningKind.Vaginal:Kind,Opening,entryProxy);
            Root=model==null?Get(sourceBones,"cm_J_dan100_00","cm_J_dan101_00"):model.Root;
            var chain=model==null?ReadBpChain(bp):model.Chain;
            if(hand)
            {
                handSource=new StudioHandSource(sourceBones,Kind);double distance;
                chain=handSource.Closest(contactPoint.Position,out distance);
                if(!handSource.Alive||chain==null)throw new ArgumentException(UiText.T("来源缺少手指骨骼链"));
                Root=chain[0];
            }
            if(chain==null||chain.Length<2)
            {
                var native=new List<Transform>();if(Root!=null)native.Add(Root);
                for(int i=101;i<=109;i++){var node=Get(sourceBones,"cm_J_dan"+i+"_00");if(node!=null&&!native.Contains(node))native.Add(node);}
                chain=native.ToArray();
            }
            if(Root==null||chain.Length<2)throw new ArgumentException(UiText.T("来源没有有效阴茎骨骼链"));
            Chain=chain;path=new MotionVector[chain.Length];
            mount=Get(sourceBones,"cf_j_waist02","cm_J_waist02","cm_j_waist02");
            sourceHips=Get(sourceBones,"cf_j_hips","cm_J_hips","cm_j_hips");
            leftThigh=Get(sourceBones,"cf_j_thigh00_L","cm_J_thigh00_L");rightThigh=Get(sourceBones,"cf_j_thigh00_R","cm_J_thigh00_R");
            targetPelvis=Get(targetBones,"cf_j_waist02","cf_j_hips","cf_j_head");
            RestLength=ReadBaseModelLength();
            SetLength(physicalLength);
        }

        internal void SetLength(double metres)
        {if(metres>0){if(!Numbers.Finite(metres)||metres<ManualCalibrationMotion.MinimumLength||metres>.5)throw new ArgumentException(UiText.T("阴茎长度须为 1–500 mm"));RestLength=metres;}contact.Reset();yaw.Reset();}
        internal void ResetTransientMotion(){contact.Reset();yaw.Reset();}
        internal double ReadModelLength(){FillPath();return StudioShaftMotion.PathLength(path);}
        internal double ReadBodyHeight()
        {
            // Sum articulated segments instead of an animated vertical bounding
            // box; lying down or bending the knees must not shrink the preset.
            var torso=new[]{Get(sourceBones,"cf_j_hips","cm_J_hips"),Get(sourceBones,"cf_j_spine01","cm_J_spine01"),Get(sourceBones,"cf_j_spine02","cm_J_spine02"),Get(sourceBones,"cf_j_spine03","cm_J_spine03"),Get(sourceBones,"cf_j_neck","cm_J_neck"),Get(sourceBones,"cf_j_head","cm_J_head")};
            foreach(var bone in torso)if(bone==null)return 0;
            double upper=0;for(int i=1;i<torso.Length;i++)upper+=Vector3.Distance(torso[i-1].position,torso[i].position);
            upper+=2*Vector3.Distance(torso[4].position,torso[5].position);
            double legs=0;int count=0;
            foreach(string side in new[]{"L","R"})
            {
                var chain=new[]{torso[0],Get(sourceBones,"cf_j_thigh00_"+side,"cm_J_thigh00_"+side),Get(sourceBones,"cf_j_leg01_"+side,"cm_J_leg01_"+side),Get(sourceBones,"cf_j_leg03_"+side,"cm_J_leg03_"+side),Get(sourceBones,"cf_j_foot_"+side,"cm_J_foot_"+side)};
                bool valid=true;foreach(var bone in chain)if(bone==null)valid=false;if(!valid)continue;
                double length=0;for(int i=1;i<chain.Length;i++)length+=Vector3.Distance(chain[i-1].position,chain[i].position);
                // Approximate sole-to-ankle allowance using the foot segment.
                length+=Vector3.Distance(chain[3].position,chain[4].position);legs+=length;count++;
            }
            return count==0?0:upper+legs/count;
        }
        internal double ReadBaseModelLength()
        {FillPath();double length=IsHandSource?0:Model==null?BaseLength(bp):Model.BaseLength;return length>1e-7?length:StudioShaftMotion.PathLength(path);}
        internal MotionVector ReadModelEndOffset()
        {
            FillPath();
            var frame=SourceReferenceFrame(V(Root.position));
            return ManualCalibrationMotion.LocalEnd(frame,path[path.Length-1]);
        }

        internal bool Alive
        {
            get
            {
                if(Source==null||Target==null||Root==null||Opening==null||!contactPoint.Alive||!Source.gameObject.activeInHierarchy||!Target.gameObject.activeInHierarchy)return false;
                bool rootOwned=Model!=null?Model.Alive:Root.IsChildOf(Source.transform);
                if(!rootOwned||!Opening.IsChildOf(Target.transform))return false;
                if(Model!=null&&!Model.Visible)return false;
                if(IsHandSource&&!handSource.Alive)return false;
                if(bpAttached&&bp!=null)
                {
                    if(!RuntimeAccess.Flag(bp,"danTargetsValid"))return false;
                    var actual=RuntimeAccess.Read(RuntimeAccess.Read(bp,"collisionAgent"),"m_collisionCharacter") as ChaControl;
                    if(actual!=null&&actual!=Target)return false;
                }
                foreach(var node in Chain)if(node==null)return false;
                return true;
            }
        }

        internal NativeMotionSample Sample(double seconds)
        {
            if(!Alive)throw new InvalidOperationException(UiText.T("绑定已失效，请重新绑定当前角色"));
            FillPath();MotionVector origin=V(Root.position);
            var frame=SourceReferenceFrame(origin);
            origin=origin+frame.Y*OriginOffset.X+frame.Z*OriginOffset.Y+frame.X*OriginOffset.Z;
            frame.Origin=origin;
            MotionVector entry,right,inward;contactPoint.Pose(path,out entry,out right,out inward);
            Transform entryGuide=RuntimeAccess.Read(bp,"danEntryChild") as Transform,endGuide=RuntimeAccess.Read(bp,"danEndChild") as Transform;
            if(Model==null&&(bpAttached||entryProxy!=null)&&entryGuide!=null&&endGuide!=null&&(V(entryGuide.position)-entry).Length<.02)
            {var guide=V(endGuide.position-entryGuide.position);if(guide.Length>1e-5)inward=guide;}
            if(inward.Length<1e-7)inward=targetPelvis!=null?V(targetPelvis.forward):frame.Y;
            var targetFrame=SafeFrame(entry,right,inward);
            if(!twistBasisReady)
            {
                // Resolve only the fixed 180-degree bone-axis ambiguity. Keep
                // the chosen basis through motion, length edits and contact loss.
                twistHalfTurn=Math.Abs(StudioShaftMotion.BodyAngles(frame,targetFrame)[0])>90;
                twistBasisReady=true;
            }
            return new NativeMotionSample{Studio=true,HasShaftPose=true,RootDistance=true,HasBodyFrame=true,HasTargetFrame=true,
                Origin=origin,End=path[path.Length-1],Target=entry,AnchorRoot=origin,AnchorEnd=path[path.Length-1],AnchorTarget=entry,
                ShaftPath=path,ShaftRestLength=RestLength,ReferenceFrame=frame,BodyFrame=frame,
                TargetReferenceFrame=targetFrame,TargetBodyFrame=targetFrame,SourceRootDirection=frame.Y,
                SourceDeformedDirection=path[path.Length-1]-path[path.Length-2],ShaftYaw=yaw,ShaftTwistHalfTurn=twistHalfTurn,ShaftContact=contact,
                HasExplicitContact=attached&&(Kind==OpeningKind.Vaginal||Kind==OpeningKind.Anal),
                ContactTolerance=(Kind==OpeningKind.LeftHand||Kind==OpeningKind.RightHand||Kind==OpeningKind.Breast)?0.068:(Kind==OpeningKind.Oral?0.085:0.0),
                ContactSequence=++sequence,ContactDeltaTime=Numbers.Clamp(seconds,0,.1),LateralSpan=1,
                PlaneWidth=L1HalfRange*2,PlaneDepth=L2HalfRange*2};
        }

        internal void Commit(NativeMotionSample sample,NativeMotionResult measured)
        {contact.Commit(sample,measured);if(measured.Engagement<=0)yaw.Reset();else yaw.Resolve(measured.Raw[3]);}

        private void FillPath(){for(int i=0;i<Chain.Length;i++)path[i]=V(Chain[i].position);}
        private AlignmentFrame SourceReferenceFrame(MotionVector origin)
        {
            Transform reference=mount!=null?mount:sourceHips!=null?sourceHips:Source.transform;
            Vector3 lateral=leftThigh!=null&&rightThigh!=null?rightThigh.position-leftThigh.position:reference.right;
            if(lateral.sqrMagnitude<1e-10f)lateral=reference.right;
            if(Model!=null&&Model.ExternalItem&&path!=null&&path.Length>=2)
            {
                MotionVector shaftForward=path[1]-path[0];
                if(shaftForward.Length<1e-6)shaftForward=path[path.Length-1]-path[0];
                if(shaftForward.Length>1e-6)
                {
                    // Unity LH (right, up) -> AlignmentFrame.Build gives Z = Cross_RH(right, up) = UserFwd
                    // (away from belly toward knees/SR6 servo box), matching SR6DeviceRotation's negated base_local
                    // (-UserRight, -ShaftUp, -UserFwd) so R0/R1/R2 and L1 align directly with physical SR6 hardware.
                    return SafeFrame(origin,V(lateral),shaftForward);
                }
            }
            var frame=SafeFrame(origin,V(lateral),V(reference.forward));
            if(IsHandSource)frame=SafeFrame(origin,V(handSource.Index.position-handSource.Little.position),path[1]-path[0]);
            return frame;
        }
        private static AlignmentFrame SafeFrame(MotionVector origin,MotionVector right,MotionVector inward)
        {
            if(MotionVector.Cross(right,inward).Length<1e-7)right=Math.Abs(inward.Unit().X)<.9?new MotionVector(1,0,0):new MotionVector(0,0,1);
            return AlignmentFrame.Build(origin,right,inward);
        }
        private static Transform FindExplicitEntry(Dictionary<string,Transform> bones,ChaControl target,StudioShaftModel model,ref string targetName,out Transform proxy)
        {
            proxy=null;var entry=Get(bones,"k_f_dan_entry");
            var type=RuntimeAccess.Find("NodesConstraints.NodesConstraints");
            var list=RuntimeAccess.Read(RuntimeAccess.Read(type,"_self"),"_constraints") as IEnumerable;if(list==null)return null;
            foreach(var handle in list)
            {
                if(handle==null||!RuntimeAccess.Flag(handle,"enabled")||!RuntimeAccess.Flag(handle,"position"))continue;
                var parent=RuntimeAccess.Read(handle,"parentTransform") as Transform;
                var child=RuntimeAccess.Read(handle,"childTransform") as Transform;
                if(parent==null||!parent.IsChildOf(target.transform)||child==null)continue;
                if(model!=null)
                {
                    if(!model.Contains(child))continue;
                    targetName=AnatomicalName(parent,target.transform);return parent;
                }
                if(child!=entry)continue;
                targetName=AnatomicalName(parent,target.transform);
                // A constraint attached directly to an anatomical entrance identifies
                // that entrance. Its child may include an animation/control offset
                // (including an offset beyond the shaft); that is not tissue position.
                bool anatomicalEntrance=string.Equals(parent.name,"cf_J_Vagina_root",StringComparison.OrdinalIgnoreCase)||
                    string.Equals(parent.name,"cf_j_kokan",StringComparison.OrdinalIgnoreCase)||
                    string.Equals(parent.name,"a_n_kokan",StringComparison.OrdinalIgnoreCase)||
                    string.Equals(parent.name,"cf_j_ana",StringComparison.OrdinalIgnoreCase)||
                    string.Equals(parent.name,"a_n_ana",StringComparison.OrdinalIgnoreCase);
                proxy=anatomicalEntrance?null:child;return parent;
            }
            return null;
        }
        internal static Component FindBp(ChaControl actor)
        {foreach(Component component in actor.GetComponents<Component>())if(component!=null&&component.GetType().FullName=="Core_BetterPenetration.BetterPenetrationController")return component;return null;}
        internal static Component FindBpCached(StudioBinding binding){return binding.bp;}
        internal static Transform[] ReadBpChain(Component controller)
        {
            var list=RuntimeAccess.Read(RuntimeAccess.Read(RuntimeAccess.Read(controller,"danAgent"),"m_danPoints"),"danPoints") as IEnumerable;
            if(list==null)return null;var nodes=new List<Transform>();
            foreach(var point in list){var node=RuntimeAccess.Read(point,"transform") as Transform;if(node==null)return null;nodes.Add(node);}
            return nodes.ToArray();
        }
        private static double BaseLength(Component controller)
        {try{double value=Convert.ToDouble(RuntimeAccess.Read(RuntimeAccess.Read(controller,"danAgent"),"m_baseDanLength"),CultureInfo.InvariantCulture);return Numbers.Finite(value)?value:0;}catch{return 0;}}
        private static Dictionary<string,Transform> Bones(Transform root)
        {var result=new Dictionary<string,Transform>();foreach(var node in root.GetComponentsInChildren<Transform>(true))if(!result.ContainsKey(node.name))result[node.name]=node;return result;}
        private static Transform Get(Dictionary<string,Transform> bones,params string[] names)
        {foreach(string name in names){Transform node;if(bones.TryGetValue(name,out node)&&node!=null)return node;}return null;}
        private static Transform OpeningBone(Dictionary<string,Transform> bones,OpeningKind kind)
        {
            switch(kind)
            {
                case OpeningKind.Anal:return Get(bones,"cf_j_ana","cf_J_Ana_Root");
                case OpeningKind.Oral:return Get(bones,"cf_J_Mouthup","a_n_mouth","cf_J_MouthLow","cf_J_MouthCavity");
                case OpeningKind.LeftHand:return Get(bones,"a_n_hand_L","cf_j_hand_L");
                case OpeningKind.RightHand:return Get(bones,"a_n_hand_R","cf_j_hand_R");
                case OpeningKind.Breast:return Get(bones,"cf_j_bust03_L","cf_j_bust02_L");
                default:return Get(bones,"cf_J_Vagina_root","cf_j_vagina_root","cf_j_kokan","a_n_kokan");
            }
        }
        internal static OpeningKind Infer(string name,OpeningKind fallback)
        {
            string text=(name??"").ToLowerInvariant();
            if(text.Contains("vagina")||text.Contains("kokan"))return OpeningKind.Vaginal;
            if(text.Contains("anal")||text.Contains("cf_j_ana")||text.Contains("a_n_ana")||text=="ana")return OpeningKind.Anal;
            if(text.Contains("mouth")||text.Contains("head"))return OpeningKind.Oral;
            if(text.Contains("hand"))return text.EndsWith("_l")||text.Contains("_l_")||text.Contains("left")?OpeningKind.LeftHand:OpeningKind.RightHand;
            if(text.Contains("bust")||text.Contains("mune"))return OpeningKind.Breast;
            return fallback;
        }
        internal static string AnatomicalName(Transform node,Transform actor)
        {
            string original=node==null?"":node.name;
            while(node!=null&&node!=actor)
            {if(Infer(node.name,(OpeningKind)(-1))!=(OpeningKind)(-1))return node.name;node=node.parent;}
            return original;
        }
        internal static string Name(ChaControl actor)
        {
            if(actor==null)return "—";
            string name=actor.chaFile!=null?actor.chaFile.parameter.fullname:null;
            return string.IsNullOrEmpty(name)||name.Trim().Length==0?(actor.sex==0?UiText.T("男性"):UiText.T("女性")):name.Trim();
        }
        internal static MotionVector V(Vector3 value){return new MotionVector(value.x,value.y,value.z);}
    }
}
