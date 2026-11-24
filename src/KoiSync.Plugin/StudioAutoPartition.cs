using System;
using System.Collections.Generic;
using KoiSync.Core;
using KoiSync.Wire;
using UnityEngine;

namespace KoiSync.Plugin
{
    internal sealed class StudioAutoPartition
    {
        internal static List<double> Schedule(double duration,IList<double> keys)
        {
            var times=new SortedDictionary<double,bool>();times[0]=true;times[duration]=true;
            double step=Math.Max(.25,duration/3600);
            for(double t=step;t<duration;t+=step)times[t]=true;
            foreach(double t in keys)if(t>0&&t<duration){times[Math.Max(0,t-.002)]=true;times[t]=true;}
            return new List<double>(times.Keys);
        }
        internal static List<double> Boundaries(IList<CameraObservation> frames,double distance,double angle,double minimum,double duration)
        {
            var result=new List<double>{0};if(frames.Count==0){result.Add(duration);return result;}
            CameraObservation anchor=frames[0];
            for(int i=1;i<frames.Count;i++)
            {
                var now=frames[i];bool jump=now.CameraId!=anchor.CameraId||Vector3.Distance(anchor.Position,now.Position)>=distance||Quaternion.Angle(anchor.Rotation,now.Rotation)>=angle;
                if(jump&&now.Time-result[result.Count-1]>=minimum&&duration-now.Time>=minimum){result.Add(now.Time);anchor=now;}
            }
            result.Add(duration);return result;
        }
        internal static StudioSource Choose(StudioAdapter adapter,Camera camera,out string posture)
        {
            posture="";if(camera==null||!camera.isActiveAndEnabled)return null;
            return Choose(adapter,new CameraObservation{Position=camera.transform.position,Rotation=camera.transform.rotation,FieldOfView=camera.fieldOfView,Aspect=camera.aspect},out posture);
        }
        internal sealed class PoseRecord
        {
            internal string Source,Pair,Label,Posture,Name;
            internal NativeTarget Profile;
            internal bool Bp;
            internal MotionVector Root,End,Target;
            internal double Cost;
        }
        internal static List<PoseRecord> Capture(StudioAdapter adapter)
        {
            var result=new List<PoseRecord>();
            foreach(var source in adapter.Sources)
            {
                var c=source.Sample(1,1,false,NativeTarget.AUTO,true);if(c==null||c.Native==null)continue;var s=c.Native;string posture=Posture(source,s);
                result.Add(new PoseRecord{Source=source.SavedKey,Pair=source.PairKey,Label=source.Label,Profile=source.Profile,Bp=source.Bp,Root=s.Origin,End=s.End,Target=s.Target,Posture=posture,Name=source.PairLabel+" :"+posture});
            }
            return result;
        }
        internal static StudioSource Choose(StudioAdapter adapter,CameraObservation camera,out string posture)
        {
            var best=Choose(Capture(adapter),camera);posture=best==null?"":best.Posture;
            return best==null?null:adapter.Sources.Find(s=>s.SavedKey==best.Source);
        }
        internal static PoseRecord Choose(IList<PoseRecord> poses,CameraObservation camera)
        {
            if(camera==null)return null;var options=new List<PoseRecord>();
            foreach(var s in poses)
            {
                MotionVector shaft=s.End-s.Root;double length=shaft.Length;if(length<1e-5)continue;
                double along=MotionVector.Dot(s.Target-s.Root,shaft.Unit())/length;
                double radial=((s.Target-s.Root)-shaft.Unit()*(along*length)).Length/length;if(along<-.25||along>1.4||radial>.8)continue;
                Vector3 point=AlignmentGizmo.V(s.Target),local=Quaternion.Inverse(camera.Rotation)*(point-camera.Position);
                if(local.z<=.01f||local.z>10000)continue;
                float half=Mathf.Tan(camera.FieldOfView*.5f*Mathf.Deg2Rad)*local.z;
                var screen=new Vector2(.5f+local.x/(2*half*camera.Aspect),.5f+local.y/(2*half));if(screen.x<0||screen.x>1||screen.y<0||screen.y>1)continue;
                s.Cost=new FocusOption{ScreenRadius=new Vector2((screen.x-.5f)*2,(screen.y-.5f)*2).magnitude,Distance=local.magnitude,Priority=s.Bp?2:s.Profile==NativeTarget.VAGINAL?1:0}.Cost+radial*.65+(s.Bp?-.25:0);options.Add(s);
            }
            options.Sort((a,b)=>a.Cost.CompareTo(b.Cost));if(options.Count==0)return null;var best=options[0];
            foreach(var other in options)if(other!=best&&(other.Pair!=best.Pair||other.Profile!=best.Profile)&&other.Cost-best.Cost<.12)return null;
            return best;
        }
        internal static string Posture(StudioSource source,NativeMotionSample s)
        {
            switch(source.Profile)
            {
                case NativeTarget.ORAL:return "口部";
                case NativeTarget.ANAL:return "后部";
                case NativeTarget.LEFTHAND:case NativeTarget.RIGHTHAND:return "手部";
                case NativeTarget.LEFTFOOT:case NativeTarget.RIGHTFOOT:return "足部";
                case NativeTarget.BREASTS:return "胸部";
                case NativeTarget.VAGINAL:
                    if(s.HasBodyFrame&&s.HasTargetFrame)
                    {
                        double facing=MotionVector.Dot(s.BodyFrame.Z,s.TargetBodyFrame.Z),upright=MotionVector.Dot(s.BodyFrame.Y,s.TargetBodyFrame.Y);
                        if(facing<-.4&&upright>.1)return "正常位（骨骼推测）";
                        if(facing>.4&&upright>.1)return "背向位（骨骼推测）";
                    }
                    return "VAGINAL（姿态未分类）";
                default:return source.Profile.ToString();
            }
        }
    }
    internal sealed class CameraObservation
    {internal double Time;internal Vector3 Position;internal Quaternion Rotation;internal int CameraId;internal float FieldOfView=60,Aspect=1.6f;}
}
