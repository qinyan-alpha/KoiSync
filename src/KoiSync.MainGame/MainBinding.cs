using System;
using System.Collections;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.MainGame
{
    internal sealed class MainBinding
    {
        internal readonly ChaControl Source,Target;
        internal readonly GameBinding Shaft;
        internal readonly Transform Anchor;
        internal readonly Animator Clock;
        internal readonly NativeTarget Contact;
        internal readonly double ModelLength;
        private readonly ActorSlot sourceSlot,targetSlot;
        private readonly ActionLandmark landmark;
        internal string Description {get{return ActionLabels.Actor(sourceSlot)+" → "+ActionLabels.Actor(targetSlot)+" · "+ActionLabels.Source(effector)+" / "+ActionLabels.Contact(Contact,landmark);}}
        private readonly object nativeItem;
        private readonly string nativeTouch;
        private readonly Behaviour nativeLook;
        private readonly int nativeTargetIndex;
        private readonly GameContactPoint contactPoint;
        private readonly ActionSource effector;
        private readonly bool headMouthFallback;
        private MotionVector itemCenter,itemAxis;
        private bool itemCenterReady,itemAxisReady;
        internal bool AdaptivePrincipalAxis {get;private set;}
        internal ActionCalibration Calibration {get;private set;}
        internal ActionEntry Entry {get;private set;}
        internal bool NativeEntryBound {get;private set;}
        internal int ClockLayer {get{return nativeItem==null?0:ItemLayer(nativeItem,Clock);}}
        internal double Length {get{return Calibration.Length>0?Calibration.Length:ModelLength;}}
        internal bool Alive
        {
            get
            {
                return Target!=null&&Target.gameObject.activeInHierarchy&&
                    (nativeItem==null||ClockLayer>=0&&Convert.ToString(RuntimeAccess.Read(nativeItem,"kindTouch"))==nativeTouch)&&
                    (Shaft!=null?Shaft.Alive:Anchor!=null&&Anchor.gameObject.activeInHierarchy&&contactPoint.Alive);
            }
        }

        internal MainBinding(HSceneProc scene,ActionProfile profile,ChaControl source,ChaControl target,ActorSlot? sourceSlot=null)
        {
            Source=source;Target=target;Contact=profile.Contact;effector=profile.Effector;targetSlot=profile.Target;landmark=profile.Landmark;
            if(!Posed(target))throw new ArgumentException(UiText.T("等待目标角色最终动画姿态"));
            ActorSlot actualSource=sourceSlot??profile.Source;
            this.sourceSlot=actualSource;
            Calibration=profile.Calibration==null?new ActionCalibration():profile.Calibration.Copy();
            if(profile.Effector==ActionSource.Shaft||profile.Effector==ActionSource.LeftFinger||
               profile.Effector==ActionSource.RightFinger||profile.Effector==ActionSource.AnimatedProp)
            {
                if(!Posed(source))throw new ArgumentException(UiText.T("等待来源角色最终动画姿态"));
                var opening=Opening(profile.Contact,profile.Landmark);
                var kind=profile.Effector==ActionSource.LeftFinger?OpeningKind.LeftHandVaginal:
                    profile.Effector==ActionSource.RightFinger?OpeningKind.RightHandVaginal:opening;
                GameShaftModel visible=null;
                if(profile.Effector==ActionSource.Shaft)
                {
                    foreach(var model in GameShaftModel.Discover(source))if(model.Visible){visible=model;break;}
                    if((actualSource==ActorSlot.Male||actualSource==ActorSlot.Male1)&&
                        (profile.Target==ActorSlot.Female0||profile.Target==ActorSlot.Female1))
                    {
                        nativeLook=RuntimeAccess.Read(scene,actualSource==ActorSlot.Male1?"lookDan1":"lookDan") as Behaviour;
                        nativeTargetIndex=profile.Target==ActorSlot.Female1?1:0;
                    }
                }
                else if(profile.Effector==ActionSource.AnimatedProp)
                {
                    visible=GameShaftModel.AnimatedProp(source);
                    if(visible==null)throw new ArgumentException(UiText.T("等待固定原生动画道具 p_item_dildo"));
                    Clock=visible.Renderer.GetComponentInParent<Animator>();
                }
                Shaft=new GameBinding(source,target,kind,opening,visible);
                ModelLength=Shaft.ModelLength;
            }
            else
            {
                ModelLength=ShaftStrokeMapping.ReferenceInteraction;
                if(profile.Effector==ActionSource.InteractionItem)
                {
                    NativeTarget nativeContact;Animator itemClock;object item;
                    Anchor=InteractionAnchor(scene,profile.Target,out nativeContact,out itemClock,out item);
                    Clock=itemClock;nativeItem=item;nativeTouch=Convert.ToString(RuntimeAccess.Read(item,"kindTouch"));
                    if(!profile.ManualBinding)Contact=nativeContact;
                }
                else if(profile.Effector==ActionSource.SceneAnchor)Anchor=scene.transform;
                else
                {
                    if(!Posed(source))throw new ArgumentException(UiText.T("等待来源角色动画"));
                    var bones=GameBinding.Bones(source.transform);
                    Anchor=profile.Effector==ActionSource.Head?GameBinding.Get(bones,"cf_J_MouthBase_rx","cf_J_MouthCavity","cf_J_Mouthup","cm_J_MouthBase_rx","cf_j_head","cm_J_head"):
                        profile.Effector==ActionSource.LeftHand?GameBinding.Get(bones,"cf_j_hand_L","cm_J_hand_L"):
                        profile.Effector==ActionSource.RightHand?GameBinding.Get(bones,"cf_j_hand_R","cm_J_hand_R"):
                        GameBinding.Get(bones,"cf_J_Vagina_root","k_f_kokan_00","cf_j_hips","cf_j_waist02","cm_J_hips");
                    headMouthFallback=profile.Effector==ActionSource.Head&&Anchor!=null&&(Anchor.name=="cm_J_head"||Anchor.name=="cf_j_head");
                }
                if(Anchor==null)throw new ArgumentException(UiText.T(profile.Effector==ActionSource.InteractionItem?
                    "等待原生交互物件（当前未操作对应部位）":"来源缺少固定驱动节点"));
                var targetBones=GameBinding.Bones(target.transform);
                var opening=Opening(Contact,profile.Landmark);
                var node=GameBinding.OpeningBone(targetBones,opening);
                if(node==null)throw new ArgumentException(UiText.T("目标缺少实际入口骨骼"));
                contactPoint=new GameContactPoint(targetBones,opening,node);
                if(!contactPoint.Alive)throw new ArgumentException(UiText.T("目标缺少完整入口标记"));
                string anchorKey=profile.Effector+"|"+Anchor.name+"|"+(nativeTouch??"");
                if(Calibration.VirtualAnchor!=anchorKey)
                {
                    MotionVector pos,right,inward;
                    contactPoint.Pose(out pos,out right,out inward);
                    var frame=ActionEntry.Frame(pos,right,inward);
                    var local=Quaternion.Inverse(Anchor.rotation);
                    double span=Calibration.Span(Length);
                    // Capture once in the source's rigid coordinates. A later
                    // bind or reload restores the same ruler on the source.
                    Calibration.VirtualFrame=ActionEntry.Frame(
                        GameBinding.V(local*(V(pos-frame.Y*(span*.5))-Anchor.position)),
                        GameBinding.V(local*V(frame.X)),GameBinding.V(local*V(frame.Y)));
                    Calibration.VirtualAnchor=anchorKey;
                }
            }
        }

        internal bool UsesEndpoints(ActionProfile profile)
        {return profile.Mode==AxisMappingMode.Manual||(profile.Effector!=ActionSource.Shaft&&profile.Effector!=ActionSource.AnimatedProp)||profile.CenteredMotion;}

        internal void EnsureEndpoints()
        {
            if(Calibration.ManualEnd.HasValue)return;
            var end=ReadModelEndOffset();
            double distance=(end-Calibration.Origin).Length;
            Calibration.ManualEnd=ShaftStrokeMapping.ValidModelLength(distance)?end:
                Calibration.Origin+new MotionVector(Length,0,0);
        }

        internal MotionVector ReadModelEndOffset()
        {return Shaft!=null?Shaft.ReadModelEndOffset():new MotionVector(Length,0,0);}

        internal void ApplyCalibration(ActionCalibration calibration)
        {
            calibration.Validate();
            Calibration=calibration;
            ResetTransientMotion();
        }

        internal NativeMotionSample Sample(double dt,MotionVector entryOffset)
        {
            if(!Alive)throw new InvalidOperationException(UiText.T("当前动作绑定正在切换"));
            AdaptivePrincipalAxis=false;
            if(Shaft!=null)
            {
                var nativeEntry=NativeEntry();
                NativeEntryBound=nativeEntry!=null;
                ActionEntry sampled;
                var sample=Shaft.Sample(dt,Calibration,Length,nativeEntry,entryOffset,out sampled);
                Entry=sampled;
                if((effector==ActionSource.LeftFinger||effector==ActionSource.RightFinger)&&Calibration.Origin.Length<1e-7)
                {
                    // Finger actions combine axial insertion and tangential rubbing across the
                    // opening; track the live fingertip's real 3D principal axis in target space.
                    return BuildAdaptivePrincipalSample(sample.End,GameBinding.V(Shaft.Root.right),sampled.Observed,dt,false);
                }
                return sample;
            }
            var local=Calibration.VirtualFrame;
            var frame=ActionEntry.Frame(GameBinding.V(Anchor.position+Anchor.rotation*V(local.Origin)),
                GameBinding.V(Anchor.rotation*V(local.X)),GameBinding.V(Anchor.rotation*V(local.Y)));
            var anchoredRoot=frame.Origin;
            var anchoredEnd=anchoredRoot+frame.Y*Length;
            frame.Origin+=frame.Y*Calibration.Origin.X+frame.Z*Calibration.Origin.Y+frame.X*Calibration.Origin.Z;
            MotionVector pos,right,inward;
            contactPoint.Pose(out pos,out right,out inward);
            var observed=ActionEntry.Frame(pos,right,inward);
            Entry=new ActionEntry(observed,observed,entryOffset,contactPoint.Label);
            if(Calibration.Origin.Length<1e-7)
            {
                // Non-shaft effectors (InteractionItem, Head, Pelvis, SceneAnchor, Hand) frequently
                // move tangentially or change stage posture far from Idle. Resetting and tracking
                // the live 3D principal motion axis per stage eliminates Idle ruler drift and captures
                // 100% of licking, rubbing, scissoring, and pelvic sway travel.
                MotionVector sourcePos=headMouthFallback
                    ?GameBinding.V(Anchor.position+Anchor.forward*.085f-Anchor.up*.035f)
                    :GameBinding.V(Anchor.position);
                bool sceneSpace=effector==ActionSource.SceneAnchor;
                return BuildAdaptivePrincipalSample(sourcePos,GameBinding.V(Anchor.right),observed,dt,sceneSpace);
            }
            return new NativeMotionSample{
                Studio=true,HasShaftPose=true,RootDistance=true,HasBodyFrame=true,HasTargetFrame=true,
                Origin=frame.Origin,End=anchoredEnd,Target=Entry.Calibrated.Origin,
                AnchorRoot=anchoredRoot,AnchorEnd=anchoredEnd,AnchorTarget=pos,ShaftRestLength=Length,
                ReferenceFrame=frame,BodyFrame=frame,TargetReferenceFrame=Entry.Calibrated,TargetBodyFrame=Entry.Calibrated,
                PlaneWidth=Calibration.L1*2,PlaneDepth=Calibration.L2*2
            };
        }

        private NativeMotionSample BuildAdaptivePrincipalSample(MotionVector sourcePos,MotionVector sourceRight,AlignmentFrame observed,double dt,bool sceneSpace)
        {
            // For SceneAnchor (fixed world prop), measure target translation in scene coordinates
            // so pelvic rotation does not multiply a 2-meter world origin lever arm.
            MotionVector rel=sceneSpace
                ?GameBinding.V(Anchor.InverseTransformPoint(V(observed.Origin)))
                :observed.Local(sourcePos-observed.Origin);
            double ca=Numbers.Clamp(dt/.75,0.01,1);
            if(!itemCenterReady){itemCenter=rel;itemCenterReady=true;}
            else itemCenter=itemCenter+(rel-itemCenter)*ca;
            MotionVector disp=rel-itemCenter;
            if(disp.Length>.0025)
            {
                if(!itemAxisReady)
                {
                    MotionVector dir=disp.Unit();
                    if(dir.Y<0||(Math.Abs(dir.Y)<.2&&dir.Z<0))dir=dir*-1;
                    itemAxis=dir*disp.Length;itemAxisReady=true;
                }
                else
                {
                    MotionVector aligned=MotionVector.Dot(disp,itemAxis)<0?disp*-1:disp;
                    double aa=Numbers.Clamp(dt/.35,0.02,1);
                    itemAxis=itemAxis*(1-aa)+aligned*aa;
                }
            }
            MotionVector worldAxis=observed.Y;
            MotionVector worldLateral=new MotionVector();
            double along=0;
            if(itemAxisReady&&itemAxis.Length>.003)
            {
                MotionVector ax=itemAxis.Unit();
                along=MotionVector.Dot(disp,ax);
                MotionVector lat=disp-ax*along;
                if(sceneSpace)
                {
                    worldAxis=GameBinding.V(Anchor.TransformDirection(V(ax))).Unit();
                    worldLateral=GameBinding.V(Anchor.TransformDirection(V(lat)));
                }
                else
                {
                    worldAxis=(observed.X*ax.X+observed.Y*ax.Y+observed.Z*ax.Z).Unit();
                    worldLateral=observed.X*lat.X+observed.Y*lat.Y+observed.Z*lat.Z;
                }
            }
            MotionVector rightHint=sceneSpace?observed.X:(sourceRight.Length>1e-5?sourceRight:observed.X);
            if(Math.Abs(MotionVector.Dot(rightHint.Unit(),worldAxis))>.95)rightHint=observed.X;
            if(Math.Abs(MotionVector.Dot(rightHint.Unit(),worldAxis))>.95)rightHint=observed.Z;
            MotionVector synthRoot=Entry.Calibrated.Origin-worldAxis*(Length*.5-along)-worldLateral*.30;
            var synthFrame=ActionEntry.Frame(synthRoot,rightHint,worldAxis);
            MotionVector synthEnd=synthRoot+synthFrame.Y*Length;
            AdaptivePrincipalAxis=true;
            return new NativeMotionSample{
                Studio=true,HasShaftPose=true,RootDistance=true,HasBodyFrame=true,HasTargetFrame=true,
                Origin=synthRoot,End=synthEnd,Target=Entry.Calibrated.Origin,
                AnchorRoot=synthRoot,AnchorEnd=synthEnd,AnchorTarget=observed.Origin,ShaftRestLength=Length,
                ReferenceFrame=synthFrame,BodyFrame=synthFrame,TargetReferenceFrame=Entry.Calibrated,TargetBodyFrame=Entry.Calibrated,
                PlaneWidth=Calibration.L1*2,PlaneDepth=Calibration.L2*2
            };
        }

        private Transform NativeEntry()
        {
            if(nativeLook==null||!nativeLook.isActiveAndEnabled||Shaft.Model!=null||GameBinding.FindBpCached(Shaft)!=null)return null;
            var node=RuntimeAccess.Read(nativeLook,"transLookAtNull") as Transform;
            if(node==null)return null;
            object index=RuntimeAccess.Read(nativeLook,"numFemale");
            bool actorMatches=index is int&&(int)index==nativeTargetIndex&&node.IsChildOf(Target.transform);
            return ActionEntry.MatchesNative(Contact,RuntimeAccess.Flag(nativeLook,"bTopStick"),actorMatches,node.name,
                node.parent!=null?node.parent.name:null)?node:null;
        }

        internal void Commit(NativeMotionSample sample,NativeMotionResult result)
        {if(Shaft!=null)Shaft.Commit(sample,result);}

        internal void ResetTransientMotion()
        {if(Shaft!=null)Shaft.ResetTransientMotion();itemCenterReady=false;itemAxisReady=false;itemCenter=new MotionVector();itemAxis=new MotionVector();}

        internal static bool Posed(ChaControl actor)
        {return actor!=null&&actor.gameObject.activeInHierarchy&&actor.animBody!=null&&actor.animBody.runtimeAnimatorController!=null;}
        internal static OpeningKind Opening(NativeTarget contact,ActionLandmark landmark=ActionLandmark.Anatomical)
        {
            if(landmark==ActionLandmark.Buttocks)return OpeningKind.Buttocks;if(landmark==ActionLandmark.LeftArmpit)return OpeningKind.LeftArmpit;if(landmark==ActionLandmark.RightArmpit)return OpeningKind.RightArmpit;
            switch(contact){case NativeTarget.ANAL:return OpeningKind.Anal;case NativeTarget.ORAL:return OpeningKind.Oral;case NativeTarget.BREASTS:return OpeningKind.Breast;case NativeTarget.LEFTHAND:return OpeningKind.LeftHand;case NativeTarget.RIGHTHAND:return OpeningKind.RightHand;case NativeTarget.LEFTFOOT:return OpeningKind.LeftFoot;case NativeTarget.RIGHTFOOT:return OpeningKind.RightFoot;case NativeTarget.BOTH_FEET:return OpeningKind.BothFeet;case NativeTarget.INTERCRURAL:return OpeningKind.Intercrural;default:return OpeningKind.Vaginal;}
        }
        private static int ItemLayer(object item,Animator animator)
        {
            if(animator==null||animator.runtimeAnimatorController==null)return -1;
            object value=RuntimeAccess.Read(RuntimeAccess.Read(item,"layerAction"),"item");int layer=value is int?(int)value:-1;
            return ActionStages.ItemLayer(layer,animator.layerCount,layer>=0&&layer<animator.layerCount?animator.GetLayerWeight(layer):0);
        }
        private static Transform InteractionAnchor(HSceneProc scene,ActorSlot slot,out NativeTarget contact,out Animator clock,out object selected)
        {
            contact=NativeTarget.VAGINAL;clock=null;selected=null;
            var hand=RuntimeAccess.Read(scene,slot==ActorSlot.Female1?"hand1":"hand");var items=RuntimeAccess.Read(hand,"useAreaItems") as IEnumerable;
            if(items==null)return null;
            foreach(var item in items)
            {
                if(item==null)continue;string kind=Convert.ToString(RuntimeAccess.Read(item,"kindTouch"));if(kind!="kokan"&&kind!="anal")continue;
                var obj=RuntimeAccess.Read(item,"obj") as GameObject;if(obj==null||!obj.activeInHierarchy)continue;
                contact=kind=="anal"?NativeTarget.ANAL:NativeTarget.VAGINAL;clock=RuntimeAccess.Read(item,"anm") as Animator;
                if(ItemLayer(item,clock)<0)continue;selected=item;
                // Native hand prefabs keep their object root attached to the
                // target. Read the authored moving finger tip, not that root.
                if(obj.name=="p_handL"||obj.name=="p_handR")
                {
                    string side=obj.name=="p_handL"?"_L":"_R";var bones=GameBinding.Bones(obj.transform);
                    var tip=GameBinding.Get(bones,(kind=="anal"?"cf_j_index04":"cf_j_middle04")+side,"cf_j_middle03"+side);
                    if(tip!=null)return tip;
                }
                // This node is owned by the selected native interaction item.
                var sound=RuntimeAccess.Read(item,"transformSound") as Transform;if(sound!=null&&sound.IsChildOf(obj.transform))return sound;
                return obj.transform;
            }
            return null;
        }
        private static Vector3 V(MotionVector v){return new Vector3((float)v.X,(float)v.Y,(float)v.Z);}
    }
}
