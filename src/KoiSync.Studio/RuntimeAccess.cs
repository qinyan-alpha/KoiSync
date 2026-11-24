using System;
using System.Collections.Generic;
using System.Reflection;

namespace KoiSync.Studio
{
    internal static class RuntimeAccess
    {
        internal const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        private static readonly Dictionary<Type,Dictionary<string,MemberInfo>> members=new Dictionary<Type,Dictionary<string,MemberInfo>>();
        internal static object Read(object owner,string name)
        {
            if(owner==null)return null;
            Type type=owner as Type??owner.GetType();Dictionary<string,MemberInfo> cache;
            if(!members.TryGetValue(type,out cache)){cache=new Dictionary<string,MemberInfo>();members[type]=cache;}
            MemberInfo member;
            if(!cache.TryGetValue(name,out member))
            {
                member=(MemberInfo)type.GetField(name,Flags)??type.GetProperty(name,Flags);cache[name]=member;
            }
            var field=member as FieldInfo;if(field!=null)return field.GetValue(owner is Type?null:owner);
            var property=member as PropertyInfo;
            return property==null?null:property.GetValue(owner is Type?null:owner,null);
        }
        internal static bool Flag(object owner,string name)
        {object value=Read(owner,name);return value is bool&&(bool)value;}
        internal static Type Find(string fullName)
        {foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies()){var type=assembly.GetType(fullName,false);if(type!=null)return type;}return null;}
    }

    // Only static clock/playback properties. Never reads _interpolables or keys.
    internal sealed class StudioClock
    {
        private Type type;
        private bool resolved;
        private PropertyInfo position,duration,playing,speed;
        private MethodInfo seek,interpolate;
        private float cachedTimeScale=1f;
        internal void Resolve()
        {
            if(resolved)return;type=RuntimeAccess.Find("Timeline.Timeline");if(type==null)return;resolved=true;
            position=type.GetProperty("playbackTime",RuntimeAccess.Flags);duration=type.GetProperty("duration",RuntimeAccess.Flags);
            playing=type.GetProperty("isPlaying",RuntimeAccess.Flags);speed=type.GetProperty("playbackSpeed",RuntimeAccess.Flags);
            seek=type.GetMethod("Seek",RuntimeAccess.Flags,null,new[]{typeof(float)},null);
            interpolate=type.GetMethod("Interpolate",RuntimeAccess.Flags,null,new[]{typeof(bool)},null);
        }
        internal bool Available {get{return type!=null&&RuntimeAccess.Read(type,"_self")!=null;}}
        internal double Time {get{return Number(position,0);}}
        internal double Duration {get{return Number(duration,0);}}
        internal bool Playing {get{try{return Available&&playing!=null&&(bool)playing.GetValue(null,null);}catch{return false;}}}
        internal bool HasNativeSpeed {get{return speed!=null&&speed.CanWrite;}}
        internal bool CanSetSpeed {get{return Available;}}
        internal double Speed {get{return HasNativeSpeed?Number(speed,1):(double)UnityEngine.Time.timeScale;}}
        internal double EffectiveSpeed {get{return HasNativeSpeed?Number(speed,1)*UnityEngine.Time.timeScale:(double)UnityEngine.Time.timeScale;}}
        private double Number(PropertyInfo property,double fallback)
        {try{return Available&&property!=null?Convert.ToDouble(property.GetValue(null,null)):fallback;}catch{return fallback;}}
        internal void Seek(double value)
        {
            if(double.IsNaN(value)||double.IsInfinity(value))throw new ArgumentException(UiText.T("定位请输入秒数"));
            if(!Available||seek==null)throw new InvalidOperationException(UiText.T("Timeline 尚未就绪"));
            float target=(float)Math.Max(0,Math.Min(Duration,value));
            bool unchanged=(float)Time==target;
            seek.Invoke(null,new object[]{target});
            // Native Seek skips evaluation when the requested time is unchanged.
            if(unchanged)RefreshPose();
        }
        internal void RefreshPose()
        {
            if(!Available||interpolate==null)return;
            object owner=RuntimeAccess.Read(type,"_self");
            interpolate.Invoke(owner,new object[]{true});interpolate.Invoke(owner,new object[]{false});
        }
        internal void Stop()
        {
            if(!Available)throw new InvalidOperationException(UiText.T("Timeline 尚未就绪"));
            type.GetMethod("Stop",RuntimeAccess.Flags,null,Type.EmptyTypes,null).Invoke(null,null);
        }
        internal void Toggle()
        {
            if(!Available)throw new InvalidOperationException(UiText.T("Timeline 尚未就绪"));
            var method=type.GetMethod(Playing?"Pause":"Play",RuntimeAccess.Flags,null,Type.EmptyTypes,null);
            if(method==null)throw new InvalidOperationException(UiText.T("Timeline 播放接口不可用"));
            method.Invoke(method.IsStatic?null:RuntimeAccess.Read(type,"_self"),null);
            if(!Playing)RefreshPose();
        }
        internal void Play()
        {
            if(!Available)throw new InvalidOperationException(UiText.T("Timeline 尚未就绪"));
            if(Playing)return;
            var method=type.GetMethod("Play",RuntimeAccess.Flags,null,Type.EmptyTypes,null);
            if(method==null)throw new InvalidOperationException(UiText.T("Timeline 播放接口不可用"));
            method.Invoke(method.IsStatic?null:RuntimeAccess.Read(type,"_self"),null);
        }
        internal void Pause()
        {
            if(!Available)throw new InvalidOperationException(UiText.T("Timeline 尚未就绪"));
            if(!Playing)return;
            var method=type.GetMethod("Pause",RuntimeAccess.Flags,null,Type.EmptyTypes,null);
            if(method==null)throw new InvalidOperationException(UiText.T("Timeline 播放接口不可用"));
            method.Invoke(method.IsStatic?null:RuntimeAccess.Read(type,"_self"),null);
            RefreshPose();
        }
        internal void SetSpeed(float value)
        {
            if(!Available)return;
            float clamped=Math.Max(0.1f,Math.Min(4.0f,value));
            if(HasNativeSpeed)
            {
                speed.SetValue(null,Convert.ChangeType(clamped,speed.PropertyType),null);
                return;
            }
            cachedTimeScale=clamped;
            UnityEngine.Time.timeScale=clamped;
            try
            {
                object owner=RuntimeAccess.Read(type,"_self");
                object field=RuntimeAccess.Read(owner,"_speedInputField");
                if(field!=null)
                {
                    var textProp=field.GetType().GetProperty("text",RuntimeAccess.Flags);
                    if(textProp!=null&&textProp.CanWrite)textProp.SetValue(field,clamped.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture),null);
                }
            }
            catch{}
        }
        internal float CachedTimeScale {get{return cachedTimeScale;}}
    }
}
