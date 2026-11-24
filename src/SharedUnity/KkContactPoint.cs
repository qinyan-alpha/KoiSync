using System;
using System.Collections.Generic;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.KK
{
    internal enum ContactLandmarkKind { Vaginal,Anal,Oral,LeftHand,RightHand,Breast,LeftHandVaginal,RightHandVaginal,LeftFoot,RightFoot,Intercrural,BothFeet,Buttocks,LeftArmpit,RightArmpit }

    // Cache anatomical landmarks once. The chest channel and closed/open-hand grip
    // have no single native entrance bone; use their paired landmarks instead.
    internal class KkContactPoint
    {
        internal readonly Transform Node;
        protected readonly ContactLandmarkKind kind;
        private readonly Transform other,innerNode,innerOther,reference,index,tip,little,thumb,middleTip,ringTip,upperLip,lowerLip,leftLip,rightLip,cavity,entryProxy;
        private readonly KkMouthAnchor mouth;
        internal KkContactPoint(Dictionary<string,Transform> bones,ContactLandmarkKind kind,Transform node,Transform proxy=null)
        {
            this.kind=kind;Node=node;entryProxy=proxy;
            if(kind==ContactLandmarkKind.Breast)
            {other=Get(bones,"cf_j_bust03_R","cf_j_bust02_R");innerNode=Get(bones,"cf_j_bust02_L","cf_j_bust01_L");innerOther=Get(bones,"cf_j_bust02_R","cf_j_bust01_R");reference=Get(bones,"cf_j_spine03","cf_j_spine02");}
            else if(kind==ContactLandmarkKind.Intercrural)
            {other=Get(bones,"cf_j_thigh00_R");reference=Get(bones,"cf_j_hips","cf_j_waist02");}
            else if(kind==ContactLandmarkKind.BothFeet)
            {other=Get(bones,"cf_j_foot_R","cf_j_leg03_R");innerNode=Get(bones,"cf_j_toes_L","cf_j_toes01_L");innerOther=Get(bones,"cf_j_toes_R","cf_j_toes01_R");reference=Get(bones,"cf_j_hips");}
            else if(kind==ContactLandmarkKind.Buttocks)
            {other=Get(bones,"cf_j_siri_R");reference=Get(bones,"cf_j_hips","cf_j_waist02");}
            else if(kind==ContactLandmarkKind.LeftArmpit||kind==ContactLandmarkKind.RightArmpit)
            {reference=Get(bones,"cf_j_spine03","cf_j_spine02");}
            else if(kind==ContactLandmarkKind.LeftFoot||kind==ContactLandmarkKind.RightFoot)
            {string side=kind==ContactLandmarkKind.LeftFoot?"_L":"_R";innerNode=Get(bones,"cf_j_toes"+side,"cf_j_toes01"+side);reference=Get(bones,"cf_j_leg03"+side);}
            else if(kind==ContactLandmarkKind.Oral)
            {
                reference=Get(bones,"cf_J_MouthBase_rx","cf_j_head");
                upperLip=Get(bones,"cf_J_Mouthup");lowerLip=Get(bones,"cf_J_MouthLow");
                leftLip=Get(bones,"cf_J_Mouth_L");rightLip=Get(bones,"cf_J_Mouth_R");cavity=Get(bones,"cf_J_MouthCavity");
                Vector3 seed=upperLip!=null&&lowerLip!=null?(upperLip.position+lowerLip.position)*.5f:node.position;
                mouth=KkMouthAnchor.Resolve(Get(bones,"cf_O_face"),seed,reference!=null?reference.right:node.right);
            }
            else if(kind==ContactLandmarkKind.LeftHand||kind==ContactLandmarkKind.RightHand)
            {
                string side=kind==ContactLandmarkKind.LeftHand?"_L":"_R";
                index=Get(bones,"cf_j_index01"+side);tip=Get(bones,"cf_j_index02"+side);
                little=Get(bones,"cf_j_little01"+side,"cf_j_ring01"+side);thumb=Get(bones,"cf_j_thumb03"+side);
                other=Get(bones,"cf_j_index03"+side);reference=Get(bones,"cf_j_hand"+side);
                middleTip=Get(bones,"cf_j_middle03"+side,"cf_j_middle02"+side);
                ringTip=Get(bones,"cf_j_ring03"+side,"cf_j_ring02"+side);
            }
        }
        internal bool Alive{get{return Node!=null&&(kind!=ContactLandmarkKind.Breast||other!=null);}}
        internal bool IsHand{get{return kind==ContactLandmarkKind.LeftHand||kind==ContactLandmarkKind.RightHand;}}
        internal bool Surface
        {get{return kind==ContactLandmarkKind.Breast||kind==ContactLandmarkKind.LeftHand||kind==ContactLandmarkKind.RightHand||
                    kind==ContactLandmarkKind.Intercrural||kind==ContactLandmarkKind.LeftFoot||kind==ContactLandmarkKind.RightFoot||
                    kind==ContactLandmarkKind.BothFeet||kind==ContactLandmarkKind.Buttocks||kind==ContactLandmarkKind.LeftArmpit||kind==ContactLandmarkKind.RightArmpit;}}
        internal string Label
        {
            get
            {
                if(kind==ContactLandmarkKind.Oral)
                {
                    if(mouth!=null&&mouth.Alive)return "口部变形网格";
                    if(upperLip!=null&&lowerLip!=null||leftLip!=null&&rightLip!=null)return "唇部骨骼中点";
                }
                if(IsHand&&tip!=null&&thumb!=null)return "手部多点指尖/握持追踪";
                if(other!=null&&Surface)return Node.name+" / "+other.name;
                return Node.name;
            }
        }
        internal Vector3 Position
        {
            get
            {
                if(entryProxy!=null)return entryProxy.position;
                if(kind==ContactLandmarkKind.Oral)
                {
                    if(mouth!=null&&mouth.Alive)return mouth.Position;
                    if(upperLip!=null&&lowerLip!=null)return (upperLip.position+lowerLip.position)*.5f;
                    if(leftLip!=null&&rightLip!=null)return (leftLip.position+rightLip.position)*.5f;
                }
                if((kind==ContactLandmarkKind.Breast||kind==ContactLandmarkKind.BothFeet)&&other!=null)
                {
                    var tipMid=(Node.position+other.position)*.5f;
                    if(innerNode!=null&&innerOther!=null)return (tipMid+(innerNode.position+innerOther.position)*.5f)*.5f;
                    return tipMid;
                }
                if((kind==ContactLandmarkKind.LeftFoot||kind==ContactLandmarkKind.RightFoot)&&innerNode!=null)return (Node.position+innerNode.position)*.5f;
                if((kind==ContactLandmarkKind.Intercrural||kind==ContactLandmarkKind.Buttocks)&&other!=null)return (Node.position+other.position)*.5f;
                if(IsHand&&tip!=null&&thumb!=null)return (tip.position+thumb.position)*.5f;
                return Node.position;
            }
        }
        // Soft-min multi-landmark hand contact point: seamlessly tracks closed-fist handjobs,
        // open-palm rubbing, AND light index/middle fingertip glans teasing without jumping.
        internal MotionVector ClosestHandPosition(MotionVector[] path)
        {
            if(!IsHand||path==null||path.Length<2)return Motion(Position);
            var pts=new List<MotionVector>(6);
            if(tip!=null&&thumb!=null)pts.Add(Motion((tip.position+thumb.position)*.5f));
            if(other!=null)pts.Add(Motion(other.position));
            if(middleTip!=null)pts.Add(Motion(middleTip.position));
            if(ringTip!=null)pts.Add(Motion(ringTip.position));
            if(thumb!=null)pts.Add(Motion(thumb.position));
            if(reference!=null&&index!=null)pts.Add(Motion((reference.position+index.position)*.5f));
            if(pts.Count==0)return Motion(Position);

            double minDist=double.MaxValue;
            var dists=new double[pts.Count];
            for(int k=0;k<pts.Count;k++)
            {
                double d=DistanceToPolyline(path,pts[k]);
                dists[k]=d;
                if(d<minDist)minDist=d;
            }
            double wSum=0;
            MotionVector acc=new MotionVector();
            for(int k=0;k<pts.Count;k++)
            {
                double w=Math.Exp(-(dists[k]-minDist)/0.010);
                wSum+=w;
                acc=acc+pts[k]*w;
            }
            return wSum>1e-9?acc*(1.0/wSum):pts[0];
        }
        private static double DistanceToPolyline(MotionVector[] path,MotionVector p)
        {
            double best=double.MaxValue;
            for(int i=1;i<path.Length;i++)
            {
                var seg=path[i]-path[i-1];double sq=MotionVector.Dot(seg,seg);
                double t=sq<1e-14?0:Numbers.Clamp(MotionVector.Dot(p-path[i-1],seg)/sq,0,1);
                double d=(path[i-1]+seg*t-p).Length;
                if(d<best)best=d;
            }
            return best;
        }
        internal void Pose(out MotionVector position,out MotionVector right,out MotionVector inward)
        {
            position=Motion(Position);right=Motion(Node.right);inward=Motion(Node.up);
            if(kind==ContactLandmarkKind.Oral)
            {
                if(reference!=null){right=Motion(reference.right);inward=Motion(-reference.forward);}
                if(leftLip!=null&&rightLip!=null)right=Motion(rightLip.position-leftLip.position);
                if(cavity!=null&&(Motion(cavity.position)-position).Length>1e-5)inward=Motion(cavity.position)-position;
            }
            if((kind==ContactLandmarkKind.BothFeet||kind==ContactLandmarkKind.Buttocks)&&other!=null){right=Motion(other.position-Node.position);if(reference!=null)inward=Motion(reference.forward);}
            if((kind==ContactLandmarkKind.LeftArmpit||kind==ContactLandmarkKind.RightArmpit)&&reference!=null){right=Motion(reference.right);inward=Motion(reference.forward);}
            if(kind==ContactLandmarkKind.Intercrural&&other!=null){right=Motion(other.position-Node.position);if(reference!=null)inward=Motion(reference.forward);}
            if((kind==ContactLandmarkKind.LeftFoot||kind==ContactLandmarkKind.RightFoot)&&reference!=null){right=Motion(reference.right);inward=Motion(reference.forward);}
            if(kind==ContactLandmarkKind.Breast&&other!=null)
            {right=Motion(other.position-Node.position);if(reference!=null)inward=Motion(reference.up);}
            else if(IsHand&&index!=null&&little!=null)
            {
                inward=Motion(index.position-little.position);
                if(other!=null)right=Motion(index.position-other.position);
                else if(reference!=null)right=Motion(reference.right);
            }
        }
        protected static MotionVector Motion(Vector3 value){return new MotionVector(value.x,value.y,value.z);}
        private static Transform Get(Dictionary<string,Transform> bones,params string[] names)
        {foreach(var name in names){Transform value;if(bones.TryGetValue(name,out value)&&value!=null)return value;}return null;}
    }
}
