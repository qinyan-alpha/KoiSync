using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace KoiSync.Plugin
{
    internal static class TimelineState
    {
        private static Type type;
        private static PropertyInfo playing,time,duration;
        private static IDictionary registry;
        private static readonly List<Track> tracks=new List<Track>();
        private sealed class Track{internal object Key,Value,Parameter;internal Studio.ObjectCtrlInfo Owner;}
        public static bool HasActorTracks{get{foreach(var track in tracks)if(Live(track))return true;return false;}}
        public static bool Playing{get{try{return playing!=null&&(bool)playing.GetValue(null,null);}catch{return false;}}}
        public static bool Available{get{return type!=null&&StudioMembers.Read(type,"_self")!=null;}}
        public static double Time{get{try{return Convert.ToDouble(time.GetValue(null,null));}catch{return 0;}}}
        public static double Duration{get{try{return Convert.ToDouble(duration.GetValue(null,null));}catch{return 0;}}}
        public static void Seek(double value)
        {
            if(!Available)throw new InvalidOperationException("尚未加载 Timeline");
            MethodInfo method=type.GetMethod("Seek",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
            if(method==null)throw new InvalidOperationException("当前 Timeline 没有 Seek 接口");
            method.Invoke(null,new object[]{(float)Math.Max(0,Math.Min(Duration,value))});
        }
        public static void TogglePlaying(){if(playing!=null&&playing.CanWrite)playing.SetValue(null,!Playing,null);}
        public static void SetPlaying(bool value){if(playing==null||!playing.CanWrite)throw new InvalidOperationException("Timeline 播放控制不可用");playing.SetValue(null,value,null);}
        internal static List<double> CameraKeyTimes()
        {
            var result=new List<double>();
            foreach(var track in tracks)
            {
                string id=Convert.ToString(StudioMembers.Read(track.Value,"id"));
                if(id.IndexOf("camera",StringComparison.OrdinalIgnoreCase)<0&&(track.Owner==null||track.Owner.GetType().Name.IndexOf("Camera",StringComparison.OrdinalIgnoreCase)<0))continue;
                IDictionary keys=StudioMembers.Read(track.Value,"keyframes") as IDictionary;if(keys==null)continue;
                foreach(DictionaryEntry key in keys){double value=Convert.ToDouble(key.Key);if(value>=0&&value<=Duration&&!result.Contains(value))result.Add(value);}
            }
            result.Sort();return result;
        }
        internal static object ValueAt(IDictionary keys,double at)
        {
            object left=null,right=null;double lt=double.NegativeInfinity,rt=double.PositiveInfinity;
            foreach(DictionaryEntry row in keys){double t=Convert.ToDouble(row.Key);if(t<=at&&t>lt){lt=t;left=row.Value;}if(t>at&&t<rt){rt=t;right=row.Value;}}
            if(left==null)return right==null?null:StudioMembers.Read(right,"value");
            object a=StudioMembers.Read(left,"value");if(right==null)return a;object b=StudioMembers.Read(right,"value");
            float f=(float)((at-lt)/(rt-lt));var curve=StudioMembers.Read(left,"curve") as AnimationCurve;if(curve!=null)f=curve.Evaluate(f);
            if(a is Vector3&&b is Vector3)return Vector3.LerpUnclamped((Vector3)a,(Vector3)b,f);
            if(a is Quaternion&&b is Quaternion)return Quaternion.SlerpUnclamped((Quaternion)a,(Quaternion)b,f);
            if(a is float&&b is float)return Mathf.LerpUnclamped((float)a,(float)b,f);
            return a;
        }
        internal static bool CameraAt(double at,Camera fallback,out CameraObservation result,out string reason)
        {
            result=null;reason="";if(fallback==null){reason="没有活动摄像头";return false;}
            Vector3 origin=fallback.transform.position,offset=Vector3.zero;Quaternion rotation=fallback.transform.rotation;
            var studio=UnityEngine.Object.FindObjectOfType<Studio.Studio>();object data=studio==null?null:StudioMembers.Read(studio.cameraCtrl,"cameraData");
            if(data!=null){object p=StudioMembers.Read(data,"pos"),r=StudioMembers.Read(data,"rotate"),d=StudioMembers.Read(data,"distance");if(p is Vector3)origin=(Vector3)p;if(r is Vector3)rotation=Quaternion.Euler((Vector3)r);if(d is Vector3)offset=(Vector3)d;}
            Vector3? absolute=null;Quaternion? absoluteRotation=null;float fov=fallback.fieldOfView;int found=0;
            if(registry!=null)foreach(DictionaryEntry row in registry)
            {
                var track=row.Value;if(track==null||!StudioMembers.Flag(track,"enabled",false))continue;
                string id=Convert.ToString(StudioMembers.Read(track,"id"));if(id.IndexOf("camera",StringComparison.OrdinalIgnoreCase)<0)continue;
                var keys=StudioMembers.Read(track,"keyframes") as IDictionary;if(keys==null||keys.Count==0)continue;
                object value=ValueAt(keys,at);
                switch(id)
                {
                    case "cameraOPos":if(!(value is Vector3))return false;origin=(Vector3)value;break;
                    case "cameraORot":if(!(value is Quaternion))return false;rotation=(Quaternion)value;break;
                    case "cameraOZoom":if(!(value is float))return false;offset.z=(float)value;break;
                    case "cameraFOV":if(!(value is float))return false;fov=(float)value;break;
                    case "cameraPos":if(!(value is Vector3))return false;absolute=(Vector3)value;break;
                    case "cameraRot":if(!(value is Quaternion))return false;absoluteRotation=(Quaternion)value;break;
                    default:reason="镜头轨道暂不支持直接求值："+id;return false;
                }
                found++;
            }
            if(found==0){reason="没有可直接读取的镜头关键帧";return false;}
            result=new CameraObservation{Time=at,Position=absolute??origin+rotation*offset,Rotation=absoluteRotation??rotation,CameraId=fallback.GetInstanceID(),FieldOfView=fov,Aspect=fallback.aspect};return true;
        }
        public static bool Drives(ChaControl actor)
        {if(actor==null)return false;foreach(var track in tracks){var character=track.Owner as Studio.OCIChar;if(character!=null&&character.charInfo==actor&&Live(track))return true;}return false;}
        public static bool Drives(Studio.ObjectCtrlInfo owner)
        {if(owner==null)return false;foreach(var track in tracks)if(ReferenceEquals(track.Owner,owner)&&Live(track))return true;return false;}
        public static bool DrivesParameter(object parameter)
        {if(parameter==null)return false;foreach(var track in tracks)if(Live(track)&&Contains(track.Parameter,parameter,0))return true;return false;}
        private static bool Contains(object value,object wanted,int depth)
        {
            if(ReferenceEquals(value,wanted))return true;if(value==null||depth>2)return false;
            Array array=value as Array;if(array!=null)foreach(object item in array)if(Contains(item,wanted,depth+1))return true;
            return false;
        }
        private static bool Live(Track track)
        {
            if(registry==null||!registry.Contains(track.Key)||!ReferenceEquals(registry[track.Key],track.Value)||!StudioMembers.Flag(track.Value,"enabled",false))return false;
            ICollection keys=StudioMembers.Read(track.Value,"keyframes") as ICollection;return keys!=null&&keys.Count>=2;
        }
        public static string Description
        {
            get
            {
                if(!HasActorTracks)return "";
                try{return "Timeline "+(Playing?"播放":"暂停")+" · "+((float)time.GetValue(null,null)).ToString("F3")+" / "+((float)duration.GetValue(null,null)).ToString("F1")+" s";}
                catch{return "Timeline 状态不可用";}
            }
        }
        public static void Refresh()
        {
            tracks.Clear();registry=null;
            try
            {
                if(type==null){type=StudioMembers.FindType("Timeline.Timeline");if(type==null)return;playing=type.GetProperty("isPlaying");time=type.GetProperty("playbackTime");duration=type.GetProperty("duration");}
                object self=StudioMembers.Read(type,"_self");registry=StudioMembers.Read(self,"_interpolables") as IDictionary;if(registry==null)return;
                foreach(DictionaryEntry row in registry)
                {
                    if(row.Value==null)continue;
                    var track=new Track{Key=row.Key,Value=row.Value,Owner=StudioMembers.Read(row.Value,"oci") as Studio.ObjectCtrlInfo,Parameter=StudioMembers.Read(row.Value,"parameter")};
                    if(Live(track))tracks.Add(track);
                }
            }
            catch{tracks.Clear();registry=null;}
        }
    }
}
