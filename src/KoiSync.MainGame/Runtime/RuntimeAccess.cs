using System;
using System.Collections.Generic;
using System.Reflection;

namespace KoiSync.MainGame
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

}
