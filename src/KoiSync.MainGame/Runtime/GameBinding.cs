using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.MainGame
{
    internal enum OpeningKind { Vaginal,Anal,Oral,LeftHand,RightHand,Breast,LeftHandVaginal,RightHandVaginal,LeftFoot,RightFoot,Intercrural,BothFeet,Buttocks,LeftArmpit,RightArmpit }

    internal sealed class GameBinding
    {
        internal readonly ChaControl Source,Target;
        internal readonly Transform Root,Opening;
        internal readonly Transform[] Chain;
        internal readonly GameShaftModel Model;
        internal readonly double ModelLength;
        private readonly OpeningKind openingKind;
        private readonly GameHandSource handSource;
        private readonly Dictionary<string,Transform> sourceBones;
        private readonly Component bp;
        private readonly object bpDanAgent,bpCollisionAgent;
        private readonly GameContactPoint contactPoint;
        private readonly Transform mount,leftThigh,rightThigh;
        private readonly ShaftContactTracker contact=new ShaftContactTracker();
        private readonly MotionVector[] path;
        private long sequence;
        private bool twistBasisReady,twistHalfTurn;
        private int surfaceDirection;

        internal GameBinding(ChaControl source,ChaControl target,OpeningKind sourceKind,OpeningKind targetKind,GameShaftModel model)
        {
            bool hand=GameHandSource.IsHand(sourceKind);
            if(source==null||target==null||source==target&&!hand&&model==null)
                throw new ArgumentException("请选择有效来源和目标");
            Source=source;Target=target;Model=model;openingKind=targetKind;
            bp=hand||model!=null?null:FindBp(source);
            if(bp==null&&!hand&&model==null){bpDanAgent=FindCoreBpDanAgent(source);bpCollisionAgent=FindCoreBpCollisionAgent(target);}
            sourceBones=Bones(source.transform);
            var targetBones=Bones(target.transform);
            Opening=OpeningBone(targetBones,targetKind);
            if(Opening==null)throw new ArgumentException("目标缺少对应入口节点");
            contactPoint=new GameContactPoint(targetBones,targetKind,Opening);
            if(!contactPoint.Alive)throw new ArgumentException("目标缺少完整入口标记");

            Root=model==null?Get(sourceBones,"cm_J_dan100_00","cm_J_dan101_00"):model.Root;
            var chain=model==null?(bp!=null?ReadBpChain(bp):ReadDanAgentChain(bpDanAgent)):model.Chain;
            if(hand)
            {
                handSource=new GameHandSource(sourceBones,sourceKind);
                double distance;
                chain=handSource.Closest(contactPoint.Position,out distance);
                if(!handSource.Alive||chain==null)throw new ArgumentException("来源缺少手指骨骼链");
                Root=chain[0];
            }
            if(chain==null||chain.Length<2)
            {
                var native=new List<Transform>();if(Root!=null)native.Add(Root);
                for(int i=101;i<=109;i++){var node=Get(sourceBones,"cm_J_dan"+i+"_00");if(node!=null&&!native.Contains(node))native.Add(node);}
                chain=native.ToArray();
            }
            if(Root==null||chain.Length<2)throw new ArgumentException("来源没有有效骨骼链");
            Chain=chain;path=new MotionVector[model==null?chain.Length:model.PointCount];
            mount=Get(sourceBones,"cf_j_waist02","cm_J_waist02","cm_j_waist02","cf_j_hips","cm_J_hips","cm_j_hips");
            leftThigh=Get(sourceBones,"cf_j_thigh00_L","cm_J_thigh00_L");
            rightThigh=Get(sourceBones,"cf_j_thigh00_R","cm_J_thigh00_R");
            double length=model!=null?model.BaseLength:(bp!=null?BaseLength(bp):BaseLengthFromAgent(bpDanAgent));
            if(length<=1e-7&&handSource==null&&bp==null&&bpDanAgent==null)
                length=NativeModelReference.Length(source,Root,Chain[Chain.Length-1]);
            ModelLength=length>1e-7?length:ReadModelLength();
            if(!ShaftStrokeMapping.ValidModelLength(ModelLength))throw new ArgumentException("等待有效来源模型长度");
        }

        internal bool Alive
        {
            get
            {
                if(Source==null||Target==null||Root==null||!contactPoint.Alive||!Source.gameObject.activeInHierarchy||!Target.gameObject.activeInHierarchy)return false;
                if(!Root.IsChildOf(Source.transform)||!Opening.IsChildOf(Target.transform))return false;
                if(Model!=null&&!Model.Visible||handSource!=null&&!handSource.Alive)return false;
                foreach(var node in Chain)if(node==null)return false;
                return true;
            }
        }

        internal NativeMotionSample Sample(double seconds,ActionCalibration calibration,double length,Transform nativeEntry,MotionVector entryOffset,out ActionEntry entry)
        {
            FillPath();
            var frame=SourceFrame();
            frame.Origin+=frame.Y*calibration.Origin.X+frame.Z*calibration.Origin.Y+frame.X*calibration.Origin.Z;

            MotionVector position,right,inward;
            contactPoint.Pose(out position,out right,out inward);
            if(contactPoint.Surface)
            {
                if(surfaceDirection==0)surfaceDirection=MotionVector.Dot(inward,path[path.Length-1]-path[0])<0?-1:1;
                inward=inward*surfaceDirection;
            }
            var landmark=ActionEntry.Frame(position,right,inward);
            var observed=landmark;
            string label=contactPoint.Label;
            if(nativeEntry!=null)
            {
                // The authored game null sets position. The anatomy supplies
                // the rigid orientation even if the null carries a rig offset.
                observed.Origin=V(nativeEntry.position);
                label="游戏交互锚点 · "+nativeEntry.name;
            }
            bool attached=BpMatchesEntry();
            if(attached)
            {
                Transform start=null,end=null;
                if(bp!=null)
                {
                    start=RuntimeAccess.Read(bp,"danEntryChild") as Transform;
                    end=RuntimeAccess.Read(bp,"danEndChild") as Transform;
                }
                else if(bpCollisionAgent!=null)
                {
                    if(openingKind==OpeningKind.Vaginal)
                    {start=RuntimeAccess.Read(bpCollisionAgent,"m_bpKokanTarget") as Transform;end=RuntimeAccess.Read(bpCollisionAgent,"m_innerKokan") as Transform;}
                    else if(openingKind==OpeningKind.Anal)
                    {start=RuntimeAccess.Read(bpCollisionAgent,"m_bpAnaTarget") as Transform;end=RuntimeAccess.Read(bpCollisionAgent,"m_innerTarget") as Transform;}
                    else if(openingKind==OpeningKind.Oral)
                    {start=RuntimeAccess.Read(bpCollisionAgent,"m_oralPullBone") as Transform;end=RuntimeAccess.Read(bpCollisionAgent,"m_innerHeadTarget") as Transform;}
                    if(start!=null&&nativeEntry==null)observed.Origin=V(start.position);
                }
                if(start!=null&&end!=null&&(V(start.position)-observed.Origin).Length<.035)
                {
                    var guide=V(end.position-start.position);
                    if(guide.Length>1e-5)observed=ActionEntry.Frame(observed.Origin,right,guide);
                }
                label+=bp!=null?" · BP 吸附":" · BP4.5 通道";
            }
            entry=new ActionEntry(landmark,observed,entryOffset,label);
            var targetFrame=entry.Calibrated;
            if(!twistBasisReady)
            {
                twistHalfTurn=Math.Abs(StudioShaftMotion.BodyAngles(frame,targetFrame)[0])>90;
                twistBasisReady=true;
            }

            return new NativeMotionSample{
                Studio=true,HasShaftPose=true,RootDistance=true,HasBodyFrame=true,HasTargetFrame=true,
                Origin=frame.Origin,End=path[path.Length-1],Target=targetFrame.Origin,
                AnchorRoot=path[0],AnchorEnd=path[path.Length-1],AnchorTarget=entry.Observed.Origin,
                ShaftPath=path,ShaftRestLength=length,ReferenceFrame=frame,BodyFrame=frame,
                TargetReferenceFrame=targetFrame,TargetBodyFrame=targetFrame,SourceRootDirection=frame.Y,
                SourceDeformedDirection=path[path.Length-1]-path[path.Length-2],
                ShaftTwistHalfTurn=twistHalfTurn,ShaftContact=contact,HasNativeTipContact=nativeEntry!=null,
                HasExplicitContact=attached&&(openingKind==OpeningKind.Vaginal||openingKind==OpeningKind.Anal||openingKind==OpeningKind.Oral),
                ContactTolerance=(Model!=null&&Model.PropFrame)?.055:ContactToleranceFor(openingKind),
                ContactSequence=++sequence,ContactDeltaTime=Numbers.Clamp(seconds,0,.1),
                PlaneWidth=calibration.L1*2,PlaneDepth=calibration.L2*2
            };
        }

        internal bool BpPenetrating {get{return BpMatchesEntry();}}

        private static double ContactToleranceFor(OpeningKind kind)
        {
            switch(kind)
            {
                case OpeningKind.Breast: return .060;
                case OpeningKind.Oral: return .062;
                case OpeningKind.LeftHand:
                case OpeningKind.RightHand: return .065;
                case OpeningKind.BothFeet:
                case OpeningKind.LeftFoot:
                case OpeningKind.RightFoot:
                case OpeningKind.Intercrural:
                case OpeningKind.Buttocks:
                case OpeningKind.LeftArmpit:
                case OpeningKind.RightArmpit: return .070;
                default: return 0;
            }
        }

        private bool BpMatchesEntry()
        {
            if(bp!=null)
            {
                if(!RuntimeAccess.Flag(bp,"danTargetsValid"))return false;
                var actor=RuntimeAccess.Read(RuntimeAccess.Read(bp,"collisionAgent"),"m_collisionCharacter") as ChaControl;
                return actor==Target&&Infer(Convert.ToString(RuntimeAccess.Read(bp,"danEntryParentName")),(OpeningKind)(-1))==openingKind;
            }
            if(bpDanAgent==null||bpCollisionAgent==null)return false;
            if(!RuntimeAccess.Flag(bpDanAgent,"m_danPenetration"))return false;
            var refTarget=RuntimeAccess.Read(bpDanAgent,"m_referenceTarget") as Transform;
            if(refTarget!=null)
            {
                if(!refTarget.IsChildOf(Target.transform))return false;
                var inferred=Infer(refTarget.name,(OpeningKind)(-1));
                if(inferred==(OpeningKind)(-1)&&refTarget.parent!=null)inferred=Infer(refTarget.parent.name,(OpeningKind)(-1));
                if(inferred==openingKind)return true;
            }
            return openingKind==OpeningKind.Vaginal||openingKind==OpeningKind.Anal||openingKind==OpeningKind.Oral;
        }

        internal void Commit(NativeMotionSample sample,NativeMotionResult measured){contact.Commit(sample,measured);}
        internal void ResetTransientMotion(){contact.Reset();}
        internal double ReadModelLength(){FillPath();return StudioShaftMotion.PathLength(path);}
        internal MotionVector ReadModelEndOffset()
        {
            FillPath();
            return ManualCalibrationMotion.LocalEnd(SourceFrame(),path[path.Length-1]);
        }
        private void FillPath()
        {
            if(Model!=null)Model.FillPath(path);
            else for(int i=0;i<Chain.Length;i++)path[i]=V(Chain[i].position);
        }
        private AlignmentFrame SourceFrame()
        {
            if(Model!=null&&Model.PropFrame)return ActionEntry.Frame(path[0],V(Root.right),path[path.Length-1]-path[0]);
            if(handSource!=null)return ActionEntry.Frame(V(Root.position),V(handSource.Index.position-handSource.Little.position),path[1]-path[0]);
            var reference=mount!=null?mount:Source.transform;
            Vector3 lateral=leftThigh!=null&&rightThigh!=null?rightThigh.position-leftThigh.position:reference.right;
            if(lateral.sqrMagnitude<1e-10f)lateral=reference.right;
            MotionVector latVec=V(lateral).Unit();
            MotionVector fwd=V(reference.forward);
            // Align shaft pitch inside the male pelvis sagittal plane to the actual
            // base penis chain so poses with elevated/angled shafts (cowgirl, sitting
            // lotus, oral, handjob) do not peg L1 or lose 30-50% axial depth.
            if(path!=null&&path.Length>=2)
            {
                int baseIdx=Math.Min(2,path.Length-1);
                MotionVector chord=path[baseIdx]-path[0];
                if(chord.Length<.015&&path.Length>2)chord=path[path.Length-1]-path[0];
                MotionVector sag=chord-latVec*MotionVector.Dot(chord,latVec);
                if(sag.Length>.015&&MotionVector.Dot(sag.Unit(),fwd.Unit())>-.25)fwd=sag;
            }
            return ActionEntry.Frame(V(Root.position),latVec,fwd);
        }

        internal double ReadBodyHeight()
        {
            var torso=new[]{Get(sourceBones,"cf_j_hips","cm_J_hips"),Get(sourceBones,"cf_j_spine01","cm_J_spine01"),Get(sourceBones,"cf_j_spine02","cm_J_spine02"),Get(sourceBones,"cf_j_spine03","cm_J_spine03"),Get(sourceBones,"cf_j_neck","cm_J_neck"),Get(sourceBones,"cf_j_head","cm_J_head")};
            foreach(var bone in torso)if(bone==null)return 0;
            double upper=0;for(int i=1;i<torso.Length;i++)upper+=Vector3.Distance(torso[i-1].position,torso[i].position);
            upper+=2*Vector3.Distance(torso[4].position,torso[5].position);
            double legs=0;int count=0;
            foreach(string side in new[]{"L","R"})
            {
                var chain=new[]{torso[0],Get(sourceBones,"cf_j_thigh00_"+side,"cm_J_thigh00_"+side),Get(sourceBones,"cf_j_leg01_"+side,"cm_J_leg01_"+side),Get(sourceBones,"cf_j_leg03_"+side,"cm_J_leg03_"+side),Get(sourceBones,"cf_j_foot_"+side,"cm_J_foot_"+side)};
                if(Array.Exists(chain,bone=>bone==null))continue;
                for(int i=1;i<chain.Length;i++)legs+=Vector3.Distance(chain[i-1].position,chain[i].position);
                legs+=Vector3.Distance(chain[3].position,chain[4].position);count++;
            }
            return count==0?0:upper+legs/count;
        }

        internal static Component FindBp(ChaControl actor)
        {foreach(Component component in actor.GetComponents<Component>())if(component!=null&&component.GetType().FullName=="Core_BetterPenetration.BetterPenetrationController")return component;return null;}
        internal static Component FindBpCached(GameBinding binding){return binding.bp;}
        private static object FindCoreBpDanAgent(ChaControl actor)
        {
            var t=RuntimeAccess.Find("Core_BetterPenetration.CoreGame");if(t==null)return null;
            var list=RuntimeAccess.Read(t,"danAgents") as IEnumerable;if(list==null)return null;
            foreach(var agent in list)if(agent!=null&&ReferenceEquals(RuntimeAccess.Read(agent,"m_danCharacter"),actor))return agent;
            return null;
        }
        private static object FindCoreBpCollisionAgent(ChaControl target)
        {
            var t=RuntimeAccess.Find("Core_BetterPenetration.CoreGame");if(t==null)return null;
            var list=RuntimeAccess.Read(t,"collisionAgents") as IEnumerable;if(list==null)return null;
            foreach(var agent in list)if(agent!=null&&ReferenceEquals(RuntimeAccess.Read(agent,"m_collisionCharacter"),target))return agent;
            return null;
        }
        internal static Transform[] ReadBpChain(Component controller)
        {return ReadDanAgentChain(RuntimeAccess.Read(controller,"danAgent"));}
        private static Transform[] ReadDanAgentChain(object danAgent)
        {
            if(danAgent==null)return null;
            var list=RuntimeAccess.Read(RuntimeAccess.Read(danAgent,"m_danPoints"),"danPoints") as IEnumerable;
            if(list==null)return null;var nodes=new List<Transform>();
            foreach(var point in list){var node=RuntimeAccess.Read(point,"transform") as Transform;if(node==null)return null;nodes.Add(node);}
            return nodes.ToArray();
        }
        private static double BaseLength(Component controller){return BaseLengthFromAgent(RuntimeAccess.Read(controller,"danAgent"));}
        private static double BaseLengthFromAgent(object danAgent)
        {
            if(danAgent==null)return 0;
            try{double value=Convert.ToDouble(RuntimeAccess.Read(danAgent,"m_baseDanLength"),CultureInfo.InvariantCulture);return Numbers.Finite(value)?value:0;}catch(FormatException){return 0;}catch(InvalidCastException){return 0;}
        }
        internal static Dictionary<string,Transform> Bones(Transform root)
        {var result=new Dictionary<string,Transform>();foreach(var node in root.GetComponentsInChildren<Transform>(true))if(!result.ContainsKey(node.name))result[node.name]=node;return result;}
        internal static Transform Get(Dictionary<string,Transform> bones,params string[] names)
        {foreach(string name in names){Transform node;if(bones.TryGetValue(name,out node)&&node!=null)return node;}return null;}
        internal static Transform OpeningBone(Dictionary<string,Transform> bones,OpeningKind kind)
        {
            switch(kind)
            {
                case OpeningKind.Anal:return Get(bones,"cf_j_ana","cf_J_Ana_Root","cm_J_ana","cm_J_Ana_Root");
                case OpeningKind.Oral:return Get(bones,"cf_J_Mouthup","a_n_mouth","cf_J_MouthLow","cf_J_MouthCavity");
                case OpeningKind.LeftHand:return Get(bones,"a_n_hand_L","cf_j_hand_L");
                case OpeningKind.RightHand:return Get(bones,"a_n_hand_R","cf_j_hand_R");
                case OpeningKind.Breast:return Get(bones,"cf_j_bust03_L","cf_j_bust02_L");
                case OpeningKind.LeftFoot:return Get(bones,"cf_j_foot_L","cf_j_leg03_L");
                case OpeningKind.RightFoot:return Get(bones,"cf_j_foot_R","cf_j_leg03_R");
                case OpeningKind.Intercrural:return Get(bones,"cf_j_thigh00_L");
                case OpeningKind.BothFeet:return Get(bones,"cf_j_foot_L","cf_j_leg03_L");
                case OpeningKind.Buttocks:return Get(bones,"cf_j_siri_L","cf_j_hips");
                case OpeningKind.LeftArmpit:return Get(bones,"cf_j_arm00_L","cf_j_shoulder_L");
                case OpeningKind.RightArmpit:return Get(bones,"cf_j_arm00_R","cf_j_shoulder_R");
                default:return Get(bones,"cf_J_Vagina_root","cf_j_vagina_root","cf_j_kokan","a_n_kokan");
            }
        }
        private static OpeningKind Infer(string name,OpeningKind fallback)
        {
            string text=(name??"").ToLowerInvariant();
            if(text.Contains("vagina")||text.Contains("kokan"))return OpeningKind.Vaginal;
            if(text.Contains("ana"))return OpeningKind.Anal;
            if(text.Contains("mouth")||text.Contains("head"))return OpeningKind.Oral;
            if(text.Contains("hand"))return text.EndsWith("_l",StringComparison.Ordinal)?OpeningKind.LeftHand:OpeningKind.RightHand;
            if(text.Contains("bust")||text.Contains("mune"))return OpeningKind.Breast;
            return fallback;
        }
        internal static MotionVector V(Vector3 value){return new MotionVector(value.x,value.y,value.z);}
    }
}
