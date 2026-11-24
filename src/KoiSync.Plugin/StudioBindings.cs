using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace KoiSync.Plugin
{
    // Optional plugins stay optional: inspect their loaded runtime instances, never load another copy.
    internal static class StudioMembers
    {
        private static readonly Dictionary<Type,Dictionary<string,MemberInfo>> cache=new Dictionary<Type,Dictionary<string,MemberInfo>>();
        internal static Type FindType(string name)
        {
            Type fallback=null;string expected=name.IndexOf('.')<0?name:name.Substring(0,name.IndexOf('.'));
            foreach(Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type=assembly.GetType(name,false);if(type==null)continue;
                // Optional-plugin fixtures can have the same full type name.
                // Prefer the real plugin assembly regardless of load order.
                if(assembly.GetName().Name==expected)return type;
                if(fallback==null)fallback=type;
            }
            return fallback;
        }
        internal static object Read(object instance,string name)
        {
            if(instance==null)return null;
            Type type=instance as Type??instance.GetType();Dictionary<string,MemberInfo> members;
            if(!cache.TryGetValue(type,out members)){members=new Dictionary<string,MemberInfo>();cache.Add(type,members);}
            MemberInfo member;
            if(!members.TryGetValue(name,out member))
            {
                for(Type t=type;t!=null&&member==null;t=t.BaseType)
                {
                    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly;
                    member=(MemberInfo)t.GetField(name,flags)??t.GetProperty(name,flags);
                }
                members[name]=member;
            }
            FieldInfo field=member as FieldInfo;
            if(field!=null)return field.GetValue(instance is Type?null:instance);
            PropertyInfo property=member as PropertyInfo;
            return property==null?null:property.GetValue(instance is Type?null:instance,null);
        }
        internal static bool Flag(object instance,string name,bool fallback)
        {object value=Read(instance,name);return value is bool?(bool)value:fallback;}
    }

    internal sealed class StudioLink
    {
        internal readonly object Handle;
        internal readonly Transform Parent,Child;
        private readonly IList registry;
        private readonly Behaviour plugin;
        private readonly bool hasPlugin;
        internal StudioLink(object handle,IList registry,Behaviour plugin)
        {Handle=handle;this.registry=registry;this.plugin=plugin;hasPlugin=plugin!=null;Parent=StudioMembers.Read(handle,"parentTransform") as Transform;Child=StudioMembers.Read(handle,"childTransform") as Transform;}
        internal bool Live
        {
            get{return Parent!=null&&Child!=null&&Parent.gameObject.activeInHierarchy&&Child.gameObject.activeInHierarchy&&
                (!hasPlugin||plugin!=null&&plugin.isActiveAndEnabled)&&registry!=null&&registry.Contains(Handle)&&
                StudioMembers.Flag(Handle,"enabled",false)&&!StudioMembers.Flag(Handle,"destroyed",false)&&
                (StudioMembers.Flag(Handle,"position",false)||StudioMembers.Flag(Handle,"rotation",false)||StudioMembers.Flag(Handle,"scale",false))&&
                (StudioMembers.Read(Handle,"parentTransform") as Transform)==Parent&&(StudioMembers.Read(Handle,"childTransform") as Transform)==Child;}
        }
        internal bool Position{get{return StudioMembers.Flag(Handle,"position",false);}}
        internal string Id{get{return Parent.GetInstanceID()+":"+Child.GetInstanceID();}}
        internal string Describe()
        {return (Parent==null?"(无父端)":Parent.name)+" → "+(Child==null?"(无子端)":Child.name)+" | enabled="+StudioMembers.Flag(Handle,"enabled",false)+" position="+Position+" destroyed="+StudioMembers.Flag(Handle,"destroyed",false)+" registered="+(registry!=null&&registry.Contains(Handle));}
        internal static List<StudioLink> ReadAll(out string status)
        {
            List<StudioLink> raw=ReadAllRaw(out status);var result=new List<StudioLink>();
            foreach(var link in raw)if(link.Live)result.Add(link);
            status="NodeConstraints · "+result.Count+" 条启用约束";return result;
        }
        // Every handle the scene holds, including disabled ones: the diagnosis needs the raw list.
        internal static List<StudioLink> ReadAllRaw(out string status)
        {
            var result=new List<StudioLink>();Type type=StudioMembers.FindType("NodesConstraints.NodesConstraints");
            if(type==null){status="未加载 NodeConstraints 插件";return result;}
            object self=StudioMembers.Read(type,"_self");IList list=StudioMembers.Read(self,"_constraints") as IList;
            if(list==null){status="等待 NodeConstraints 场景数据";return result;}
            foreach(object handle in list){if(handle==null)continue;result.Add(new StudioLink(handle,list,self as Behaviour));}
            status="NodeConstraints 场景共 "+result.Count+" 条约束";return result;
        }
    }

    internal sealed class StudioObject
    {
        internal int Id;
        internal string Name;
        internal Transform Root;
        internal bool Character;
        internal ChaControl Actor;
        internal int Sex=-1;
        internal string CardName="";
        internal Studio.ObjectCtrlInfo Control;
        internal StudioObject Parent;
        internal BoneMap Bones;
        internal Component Bp;
        internal readonly List<DynamicBoneCollider> Colliders=new List<DynamicBoneCollider>();
        internal bool Live{get{return Root!=null&&Root.gameObject.activeInHierarchy;}}
        internal string DisplayName{get{return (Sex==1?"girl ":Sex==0?"boy ":"角色 ")+(string.IsNullOrEmpty(CardName)?Name:CardName)+"("+Name+")";}}
        internal bool HasChain
        {
            get
            {
                // KK shares skeleton names across bodies. A dormant chain is not a source.
                int sex=Actor==null?Sex:Actor.sex;
                if(!Character)return false;
                if(sex==1&&(Actor==null||Actor.fileStatus==null||!Actor.fileStatus.visibleSon&&!Actor.fileStatus.visibleSonAlways||
                    Bones==null||Bones.Get("k_f_dan_entry")==null||!StudioMembers.Flag(Bp,"danTargetsValid",false)))return false;
                Transform[] bp=StudioSource.BpChain(Bp);
                return bp!=null&&bp.Length>=2||Bones!=null&&Bones.Get("cm_J_dan100_00","cm_J_dan101_00")!=null&&Bones.Get("cm_J_dan109_00")!=null;
            }
        }
    }

    internal sealed class StudioGraph
    {
        internal readonly List<StudioObject> Objects;
        internal readonly List<StudioLink> Links;
        private readonly Dictionary<int,StudioObject> owners=new Dictionary<int,StudioObject>();
        internal StudioGraph(List<StudioObject> objects,List<StudioLink> links)
        {Objects=objects;Links=links;foreach(var obj in objects)if(obj.Root!=null)owners[obj.Root.GetInstanceID()]=obj;}
        internal StudioObject Owner(Transform node)
        {for(Transform t=node;t!=null;t=t.parent){StudioObject owner;if(owners.TryGetValue(t.GetInstanceID(),out owner))return owner;}return null;}
        // Follow actual attachment transforms and incoming position constraints only.
        // A shared Studio folder is neither an actor nor evidence of contact.
        internal StudioObject AttachedActor(Transform node,out Transform anchor,StudioLink ignore=null)
        {
            return AttachedActor(node,out anchor,ignore,new HashSet<int>());
        }
        private StudioObject AttachedActor(Transform node,out Transform anchor,StudioLink ignore,HashSet<int> visited)
        {
            anchor=null;
            for(Transform t=node;t!=null;t=t.parent)
            {
                if(!visited.Add(t.GetInstanceID()))return null;
                var owner=Owner(t);if(owner!=null&&owner.Character){anchor=t;return owner;}
                StudioObject attached=null;Transform attachedAnchor=null;bool constrained=false;
                foreach(var link in Links)
                {
                    if(link==ignore||!link.Live||!link.Position||link.Child!=t)continue;
                    constrained=true;Transform candidateAnchor;
                    var candidate=AttachedActor(link.Parent,out candidateAnchor,ignore,new HashSet<int>(visited));
                    if(candidate==null||attached!=null&&(attached!=candidate||attachedAnchor!=candidateAnchor))return null;
                    attached=candidate;attachedAnchor=candidateAnchor;
                }
                if(constrained){anchor=attachedAnchor;return attached;}
            }
            return null;
        }
        private static void Visit(StudioObject obj,List<StudioObject> found,Queue<StudioObject> pending)
        {if(!found.Contains(obj)){found.Add(obj);pending.Enqueue(obj);}}
        internal bool Driven(StudioObject start)
        {
            // Follow incoming constraints and scene parents: only ancestors can drive this object.
            var visited=new List<StudioObject>();var queue=new Queue<StudioObject>();queue.Enqueue(start);visited.Add(start);
            while(queue.Count>0)
            {
                var current=queue.Dequeue();if(TimelineState.Drives(current.Control))return true;
                if(current.Parent!=null)Visit(current.Parent,visited,queue);
                foreach(var link in Links)if(link.Live&&Owner(link.Child)==current)
                {if(TimelineState.DrivesParameter(link.Handle))return true;var parent=Owner(link.Parent);if(parent!=null)Visit(parent,visited,queue);}
            }
            return false;
        }
    }
}
