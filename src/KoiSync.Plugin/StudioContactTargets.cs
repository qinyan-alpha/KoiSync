using System;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.Plugin
{
    internal static class StudioContactTargets
    {
        private static Vector3 Point(BoneMap b,params string[] names)
        {var t=b.Get(names);if(t==null)throw new ArgumentException("缺少部位参考节点："+string.Join(" / ",names));return t.position;}
        internal static void Configure(StudioObject source,StudioObject target,NativeTarget profile,NativeMotionSample s)
        {
            if(profile==NativeTarget.VAGINAL||profile==NativeTarget.ANAL)return;
            var b=target.Bones;Vector3 p,twistA,twistB,tiltA,tiltB;string label;
            if(profile==NativeTarget.ORAL)
            {
                var mouth=b.Get("a_n_mouth","cf_J_MouthLow");p=mouth==null?(Point(b,"cf_J_Mouth_L")+Point(b,"cf_J_Mouth_R"))*.5f:mouth.position;
                twistA=Point(b,"a_n_earrings_L","cf_J_EarBase_L","cf_J_Mouth_L");twistB=Point(b,"a_n_earrings_R","cf_J_EarBase_R","cf_J_Mouth_R");
                tiltA=p;tiltB=Point(b,"cf_j_head");label="耳部线";
            }
            else if(profile==NativeTarget.BREASTS)
            {
                twistA=Point(b,"k_f_munenipL_00","cf_j_bnip02_L");twistB=Point(b,"k_f_munenipR_00","cf_j_bnip02_R");
                p=(twistA+twistB)*.5f;tiltA=p;tiltB=Point(b,"cf_j_head","cf_j_spine03");label="胸部线";
            }
            else if(profile==NativeTarget.LEFTHAND||profile==NativeTarget.RIGHTHAND)
            {
                string side=profile==NativeTarget.LEFTHAND?"L":"R";
                p=Point(b,"a_n_hand_"+side,"cf_j_hand_"+side);twistA=p;twistB=Point(b,"a_n_mid_"+side,"cf_j_middle01_"+side);
                tiltA=Point(b,"a_n_ind_"+side,"cf_j_index01_"+side);tiltB=twistB;label="掌指线";
            }
            else if(profile==NativeTarget.LEFTFOOT||profile==NativeTarget.RIGHTFOOT)
            {
                string side=profile==NativeTarget.LEFTFOOT?"L":"R";var foot=b.Get("cf_j_foot_"+side);if(foot==null)throw new ArgumentException("缺少脚部骨骼");
                p=foot.position;var toe=b.Get("cf_j_toes_"+side);twistA=tiltA=p;twistB=tiltB=toe!=null?toe.position:p+foot.forward*.04f;label="脚掌脚尖线";
            }
            else if(profile==NativeTarget.INTERCRURAL)
            {
                twistA=Point(b,"cf_j_thigh00_L");twistB=Point(b,"cf_j_thigh00_R");p=(twistA+twistB)*.5f;
                tiltA=p;tiltB=Point(b,"cf_j_hips");label="腿部线";
            }
            else throw new ArgumentException("尚无该交互类型的部位参考算法");
            var ownerBones=source==null?null:source.Bones;
            var hips=ownerBones==null?null:ownerBones.Get("cf_j_hips");var head=ownerBones==null?null:ownerBones.Get("cf_j_head","cf_j_spine03","cf_j_spine01");
            var left=ownerBones==null?null:ownerBones.Get("cf_j_thigh00_L");var right=ownerBones==null?null:ownerBones.Get("cf_j_thigh00_R");
            var origin=s.Origin;var middle=hips==null?origin:StudioSource.V(hips.position);
            var targetHead=b.Get("cf_j_head","cf_j_spine03","cf_j_spine01");var targetHip=b.Get("cf_j_hips");
            s.Target=StudioSource.V(p);s.AnchorTarget=s.Target;s.TargetBodyFrame.Origin=s.TargetReferenceFrame.Origin=s.Target;
            s.Contact=new StudioContactReference{
                SourceLeft=left==null?middle-s.BodyFrame.X*.05:StudioSource.V(left.position),SourceRight=right==null?middle+s.BodyFrame.X*.05:StudioSource.V(right.position),
                TwistStart=StudioSource.V(twistA),TwistEnd=StudioSource.V(twistB),TiltStart=StudioSource.V(tiltA),TiltEnd=StudioSource.V(tiltB),
                SourceTorso=head!=null&&hips!=null?StudioSource.V(head.position-hips.position):s.BodyFrame.Y,
                TargetTorso=targetHead!=null&&targetHip!=null?StudioSource.V(targetHead.position-targetHip.position):s.TargetBodyFrame.Y,Name=label};
            // Keep the source body plane: its normal is the pelvis facing axis (roughly the shaft aim),
            // so the plane stays coplanar with the male's upper body. Building the plane from the root
            // and the two thigh points instead made its normal run along the body axis, which turned the
            // plane perpendicular to the male in part-contact poses such as a standing blowjob.
            // The pelvis lateral line is still used as the in-plane X reference.
            var lateral=s.Contact.SourceRight-s.Contact.SourceLeft;
            if(lateral.Length>1e-7)
                s.BodyFrame=s.ReferenceFrame=AlignmentFrame.Build(s.Origin,lateral,s.BodyFrame.Y);
            s.FrameLabel="蓝面："+(source==null?"道具":source.DisplayName)+" · 根点 / 盆骨\n粉线："+target.DisplayName;
        }
    }
}
