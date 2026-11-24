using System;
using System.Collections.Generic;
using KoiSync.Core;
using KoiSync.Wire;
using UnityEngine;

namespace KoiSync.Plugin
{
    internal sealed class StudioAdapter:ISceneAdapter
    {
        internal readonly List<StudioSource> Sources=new List<StudioSource>();
        internal string Discovery="等待工作室绑定",Sampling="";
        internal string ScanStage="idle";
        internal StudioGraph Graph;
        private void Stage(string value)
        {
            ScanStage=value;
        }
        public string Context{get{return "CharaStudio";}}
        public string State{get{return "工作室 / 绑定关系采样";}}
        public bool Active{get{return Sources.Count>0;}}
        public List<ActorPair> Pairs{get;private set;}=new List<ActorPair>();
        public void Refresh()
        {
            Stage("timeline");Sources.Clear();Pairs.Clear();TimelineState.Refresh();
            var studio=UnityEngine.Object.FindObjectOfType<Studio.Studio>();
            if(studio==null||studio.dicObjectCtrl==null){Discovery="等待工作室场景";Diagnosis="等待工作室场景：没有找到 Studio.Studio 或 dicObjectCtrl 为空";return;}
            var objects=new List<StudioObject>();var controls=new Dictionary<Studio.ObjectCtrlInfo,StudioObject>();
            Stage("objects 0 / "+studio.dicObjectCtrl.Count);
            int objectIndex=0;
            foreach(var row in studio.dicObjectCtrl)
            {
                objectIndex++;if(objectIndex==studio.dicObjectCtrl.Count||objectIndex%25==0)Stage("objects "+objectIndex+" / "+studio.dicObjectCtrl.Count);
                Studio.ObjectCtrlInfo control=row.Value;var character=control as Studio.OCIChar;var item=control as Studio.OCIItem;
                Transform root=character!=null&&character.charInfo!=null?character.charInfo.transform:item!=null&&item.objectItem!=null?item.objectItem.transform:control.guideObject==null?null:control.guideObject.transformTarget;
                if(root==null)continue;
                // Only characters are queried by bone name. Building a recursive BoneMap for every
                // item/folder repeatedly walks the same nested scene trees and can stall large cards.
                var obj=new StudioObject{Id=row.Key,Name=control.treeNodeObject==null?root.name:control.treeNodeObject.textName,Root=root,Character=character!=null,Actor=character==null?null:character.charInfo,Control=control,Bones=character==null?null:new BoneMap(root)};
                if(obj.Actor!=null){obj.Sex=obj.Actor.sex;obj.CardName=obj.Actor.fileParam.fullname;}
                if(obj.Character)foreach(Component component in root.GetComponents<Component>())if(component!=null&&component.GetType().FullName=="Core_BetterPenetration.BetterPenetrationController"){obj.Bp=component;break;}
                objects.Add(obj);controls[control]=obj;
            }
            Stage("parents");
            foreach(var obj in objects){StudioObject parent;if(obj.Control.parentInfo!=null&&controls.TryGetValue(obj.Control.parentInfo,out parent))obj.Parent=parent;}
            Stage("constraints");
            string status;List<StudioLink> raw=StudioLink.ReadAllRaw(out status);var links=new List<StudioLink>();
            foreach(var link in raw)if(link.Live)links.Add(link);
            Stage("relationships "+links.Count);
            Graph=new StudioGraph(objects,links);
            // Resolve explicit character/BP relationships first. Most intercourse scene cards have
            // this authoritative binding and never need the expensive all-scene collider fallback.
            Discover(Graph);
            if(Sources.Count==0)
            {
                Stage("collider fallback");
                foreach(var collider in UnityEngine.Object.FindObjectsOfType<DynamicBoneCollider>())
                {
                    StudioObject owner=Graph.Owner(collider.transform);
                    if(owner!=null&&!owner.Character)owner.Colliders.Add(collider);
                }
            }
            if(Sources.Count==0){Stage("collider relationships");Discover(Graph);}
            Discovery=(raw.Count==0?status:"NodeConstraints · "+links.Count+" 条启用 / 共 "+raw.Count+" 条")+" · "+Sources.Count+" 组可测量绑定";
            if(Sources.Count>1)Discovery+=" · 支持镜头自动选择 / 手动锁定";
            if(diagnosisRequested){diagnosisRequested=false;BuildDiagnosis(status,raw,links,objects);}
            Stage("complete · "+Sources.Count+" sources");
        }
        private bool diagnosisRequested;
        internal void RequestDiagnosis(){diagnosisRequested=true;}
        internal string Diagnosis="等待诊断";
        // Explains what the scene actually offers: every node constraint, the characters and the bindings
        // that survived discovery. This is the page to look at when no measurement source appears.
        private void BuildDiagnosis(string status,List<StudioLink> raw,List<StudioLink> live,List<StudioObject> objects)
        {
            var text=new System.Text.StringBuilder();
            text.Append("NodesConstraints：").Append(status).Append("（启用 ").Append(live.Count).Append(" / 共 ").Append(raw.Count).Append("）\n");
            for(int i=0;i<raw.Count&&i<40;i++)text.Append("  [").Append(i).Append("] ").Append(raw[i].Describe()).Append("\n      判定：").Append(Explain(raw[i])).Append("\n");
            if(raw.Count==0)text.Append("  （场景内没有任何节点约束：工作室的插入关系必须由 NodesConstraints 建立，姿势本身不产生测量数据）\n");
            foreach(var obj in objects)if(obj.Character)
                text.Append("角色：").Append(obj.DisplayName).Append(" sex=").Append(obj.Sex).Append(" dan入口=").Append(obj.Bones!=null&&obj.Bones.Get("k_f_dan_entry")!=null)
                    .Append(" 可用链=").Append(obj.HasChain).Append(" BP=").Append(obj.Bp!=null).Append(" 碰撞体=").Append(obj.Colliders.Count).Append('\n');
            text.Append("可测量绑定 ").Append(Sources.Count).Append(" 组\n");
            for(int i=0;i<Sources.Count&&i<20;i++)text.Append("  - ").Append(Sources[i].Label).Append(Sources[i].Live?"":"（当前失效）").Append('\n');
            Diagnosis=text.ToString();
        }
        internal string Explain(StudioLink link)
        {
            if(!link.Live)return "约束未生效（未勾选、对象已删除或不在约束表内）";
            if(!link.Position)return "只锁定旋转，没有位置约束，不能作为测量来源";
            Transform entry=link.Child.name=="k_f_dan_entry"?link.Child:link.Parent.name=="k_f_dan_entry"?link.Parent:null;
            if(entry!=null)
            {
                Transform targetNode=entry==link.Child?link.Parent:link.Child,anchor;
                StudioObject source=Graph==null?null:Graph.Owner(entry);
                StudioObject target=Graph==null?null:Graph.AttachedActor(targetNode,out anchor,link);
                if(source==null)return "ダン入口一端不属于任何角色对象";
                if(!source.Character)return "ダン入口一端挂在场景物件上（"+source.Name+"）";
                if(!source.HasChain)return "来源角色没有可用的ダン链：男性需要可见的ダン，或 BP 控制器 danTargetsValid";
                if(target==null)return "另一端没有附着到角色：目标端要绑定到角色骨骼，不能只挂在文件夹或物件上";
                if(source==target)return "约束两端是同一个角色";
                return "可用：贯通绑定 "+source.Name+" → "+target.Name;
            }
            if(Graph!=null)foreach(bool forward in new[]{true,false})
            {
                Transform targetNode=forward?link.Child:link.Parent,sourceNode=forward?link.Parent:link.Child;
                Transform sa,ta;StudioObject source=Graph.AttachedActor(sourceNode,out sa,link),target=Graph.AttachedActor(targetNode,out ta,link);
                if(source==null||target==null||source==target||!source.Character||!source.HasChain)continue;
                if(!AxialAnchor(source,sa)||!TargetNode(ta==null?"":ta.name))continue;
                return "可用：角色骨骼绑定 "+source.Name+" → "+target.Name+" · "+StudioSource.InferProfile(ta.name);
            }
            return "未命中规则：需要一端是男性ダン入口 k_f_dan_entry，或源端锚点为 dan/kokan/hips/waist/spine 且目标端为 口/阴道/肛门/手/脚/胸 骨骼";
        }
        internal void WriteDiagnosis(string path)
        {
            try{string directory=System.IO.Path.GetDirectoryName(path);if(!string.IsNullOrEmpty(directory))System.IO.Directory.CreateDirectory(directory);System.IO.File.WriteAllText(path,Diagnosis,System.Text.Encoding.UTF8);}
            catch(Exception){}
        }
        internal void Discover(StudioGraph graph)
        {
            Graph=graph;Sources.Clear();Pairs.Clear();var ids=new HashSet<string>();var bpPairs=new HashSet<string>();
            // The child is normally the entry control. Supporting the reverse direction costs no guessing.
            foreach(var link in graph.Links)
            {
                if(!link.Live||!link.Position)continue;
                Transform entry=link.Child.name=="k_f_dan_entry"?link.Child:link.Parent.name=="k_f_dan_entry"?link.Parent:null;
                if(entry==null)continue;StudioObject source=graph.Owner(entry);
                Transform targetNode=entry==link.Child?link.Parent:link.Child,targetAnchor;
                var target=graph.AttachedActor(targetNode,out targetAnchor,link);
                if(source==null||!source.Character||!source.HasChain||target==null||source==target)continue;
                {
                    var binding=new StudioSource{Source=source,Target=target,Link=link,Entry=entry,Bp=source.Bp!=null,Profile=StudioSource.InferProfile(targetAnchor.name),Graph=graph,TargetAttachment=targetNode};
                    binding.Id="studio:entry:"+link.Id+":"+target.Id;binding.Label=(binding.Bp?"BP":"角色骨骼")+" · "+source.Name+" → "+target.Name+" · "+binding.Profile;
                    Add(binding,ids);bpPairs.Add(source.Id+":"+target.Id+":"+binding.Profile);
                }
            }
            // Native chains use the main-game frame definitions, but pairing comes only from scene bindings.
            foreach(var link in graph.Links)
            {
                if(!link.Live||!link.Position||link.Child.name=="k_f_dan_end"||link.Parent.name=="k_f_dan_end"||link.Child.name=="k_f_dan_entry"||link.Parent.name=="k_f_dan_entry")continue;
                DiscoverCharacter(link,link.Child,bpPairs,ids);DiscoverCharacter(link,link.Parent,bpPairs,ids);
            }
            if(Sources.Count==0)DiscoverImplicitBp(graph,ids);
            Dictionary<StudioObject,DynamicBone[]> characterPhysics=null;
            foreach(var source in graph.Objects)foreach(var collider in source.Colliders)
            {
                if(characterPhysics==null)
                {
                    characterPhysics=new Dictionary<StudioObject,DynamicBone[]>();
                    foreach(var obj in graph.Objects)if(obj.Character)characterPhysics[obj]=obj.Root.GetComponentsInChildren<DynamicBone>(true);
                }
                if(collider==null||!collider.isActiveAndEnabled||collider.m_Bound!=DynamicBoneCollider.Bound.Outside)continue;
                var origin=new StudioSource{Source=source,Graph=graph,Collider=collider,IsCollider=true};
                if(!origin.ColliderOriginValid)continue;
                StudioLink anchor=null;
                for(StudioObject attached=source;attached!=null&&anchor==null;attached=attached.Parent)
                {
                    foreach(var link in graph.Links)if(link.Live&&link.Position&&(graph.Owner(link.Child)==attached||graph.Owner(link.Parent)==attached)&&link.Child.name!="k_f_dan_end"&&link.Parent.name!="k_f_dan_end"){anchor=link;break;}
                    if(attached.Character)break;
                }
                if(anchor==null)continue;
                foreach(var target in graph.Objects)
                {
                    if(!target.Character||target==origin.SourceActor||!target.Live)continue;
                    bool found=false;
                    foreach(var physics in characterPhysics[target])
                    {
                        if(!physics.isActiveAndEnabled||physics.m_Root==null||physics.m_Colliders==null||!physics.m_Colliders.Contains(collider)||!TargetNode(physics.m_Root.name))continue;
                        NativeTarget profile=StudioSource.InferProfile(physics.m_Root.name);
                        AddCollider(source,target,collider,anchor,physics,profile,ids);found=true;
                    }
                    // Explicit target constraints also cover scenes whose affected bone list is populated later.
                    if(!found)foreach(var link in graph.Links)
                    {
                        if(!link.Live||!link.Position)continue;
                        Transform point=origin.OwnsModelNode(link.Parent)?link.Child:origin.OwnsModelNode(link.Child)?link.Parent:null,targetAnchor;
                        if(point==null||graph.AttachedActor(point,out targetAnchor,link)!=target||!TargetNode(targetAnchor.name))continue;
                        AddCollider(source,target,collider,link,null,StudioSource.InferProfile(targetAnchor.name),ids,point);
                    }
                }
            }
            Sources.Sort((a,b)=>string.CompareOrdinal(a.Id,b.Id));
        }
        private void DiscoverImplicitBp(StudioGraph graph,HashSet<string> ids)
        {
            // Cards driven entirely by Timeline can have no NodeConstraints relationship.
            // Only infer a pair when the scene is unambiguous: one valid male BP chain and
            // one female target with a vaginal landmark. Multi-actor scenes still require
            // an explicit constraint so KoiSync never guesses the partner.
            var sources=new List<StudioObject>();var targets=new List<StudioObject>();
            foreach(var obj in graph.Objects)if(obj.Character&&obj.Live)
            {
                if(obj.Sex==0&&obj.Bp!=null&&obj.HasChain)sources.Add(obj);
                else if(obj.Sex==1&&obj.Bones!=null&&obj.Bones.Get("cf_J_Vagina_root","cf_j_vagina_root")!=null)targets.Add(obj);
            }
            Stage("implicit "+sources.Count+" male / "+targets.Count+" female");
            if(sources.Count!=1||targets.Count!=1)return;
            StudioObject source=sources[0],target=targets[0];Transform entry=target.Bones.Get("cf_J_Vagina_root","cf_j_vagina_root");
            var binding=new StudioSource{Source=source,Target=target,Entry=entry,Bp=true,Implicit=true,Profile=NativeTarget.VAGINAL,Graph=graph};
            binding.Id="studio:implicit-bp:"+source.Id+":"+target.Id+":VAGINAL";
            binding.Label="BP · "+source.Name+" → "+target.Name+" · VAGINAL · 双角色自动配对";
            Add(binding,ids);
            Stage("implicit source added");
        }
        private void DiscoverCharacter(StudioLink link,Transform targetNode,HashSet<string> bpPairs,HashSet<string> ids)
        {
            // The target endpoint gives the relationship its direction. This remains
            // meaningful when both actors use a male/template body with the same bones.
            Transform sourceNode=targetNode==link.Child?link.Parent:link.Child;
            Transform sourceAnchor,targetAnchor;
            var source=Graph.AttachedActor(sourceNode,out sourceAnchor,link);
            var target=Graph.AttachedActor(targetNode,out targetAnchor,link);
            if(source==null||target==null||source==target||!source.HasChain||!TargetNode(targetAnchor.name))return;
            // A hand following another actor's leg is a pose constraint, not a
            // measurement origin. Require an axial/root anchor on the source side.
            if(!AxialAnchor(source,sourceAnchor))return;
            {
                NativeTarget profile=StudioSource.InferProfile(targetAnchor.name);
                if(bpPairs.Contains(source.Id+":"+target.Id+":"+profile))return;
                var binding=new StudioSource{Source=source,Target=target,Link=link,Profile=profile,Graph=Graph,Entry=TargetPoint(target,profile,targetAnchor),SourceAttachment=sourceNode,TargetAttachment=targetNode};
                binding.Id="studio:chain:"+source.Id+":"+target.Id+":"+profile;
                binding.Label="角色骨骼 · "+source.Name+" → "+target.Name+" · "+profile;Add(binding,ids);
            }
        }
        private void AddCollider(StudioObject source,StudioObject target,DynamicBoneCollider collider,StudioLink link,DynamicBone physics,NativeTarget profile,HashSet<string> ids,Transform targetAttachment=null)
        {
            Transform entry=TargetPoint(target,profile,physics==null?null:physics.m_Root);
            var binding=new StudioSource{Source=source,Target=target,Link=link,Collider=collider,IsCollider=true,TargetPhysics=physics,RequiresPhysics=physics!=null,Entry=entry,Profile=profile,Graph=Graph,TargetAttachment=targetAttachment};
            binding.Id="studio:collider:"+collider.GetInstanceID()+":"+target.Id+":"+profile;
            binding.Label="碰撞体 · "+source.Name+" → "+target.Name+" · "+profile;Add(binding,ids);
        }
        private void Add(StudioSource source,HashSet<string> ids)
        {if(!ids.Add(source.Id))return;Sources.Add(source);if(source.Source.Actor!=null&&source.Target.Actor!=null)Pairs.Add(new ActorPair(source.Source.Actor,source.Target.Actor));}
        internal static bool AxialAnchor(StudioObject actor,Transform anchor)
        {
            if(actor==null||anchor==null)return false;string n=anchor.name.ToLowerInvariant();
            return anchor==actor.Root||n.Contains("dan")||n.Contains("kokan")||n.Contains("hips")||n.Contains("waist")||n.Contains("spine");
        }
        private static Transform TargetPoint(StudioObject target,NativeTarget profile,Transform anchor)
        {
            if(profile==NativeTarget.LEFTFOOT||profile==NativeTarget.RIGHTFOOT)return target.Bones.Get("cf_j_foot_"+(profile==NativeTarget.LEFTFOOT?"L":"R"));
            if(profile==NativeTarget.VAGINAL)return target.Bones.Get("cf_J_Vagina_root","cf_j_vagina_root","cf_j_kokan")??anchor;
            if(profile==NativeTarget.ANAL)return target.Bones.Get("cf_j_ana","cf_J_Ana_Root")??anchor;
            if(profile==NativeTarget.ORAL)return target.Bones.Get("cf_J_MouthLow")??anchor;
            return anchor!=null&&anchor.name.IndexOf("(work)",StringComparison.OrdinalIgnoreCase)<0?anchor:null;
        }
        private static bool TargetNode(string name)
        {
            string n=name.ToLowerInvariant().Replace("(work)","");return n.Contains("vagina")||n.Contains("kokan")||n.Contains("mouth")||n.Contains("ana")||n.Contains("hand")||n.Contains("foot")||n.Contains("toes")||n=="cf_t_leg_l"||n=="cf_t_leg_r"||n.Contains("bust")||n.Contains("mune");
        }
        internal void Sample(Snapshot frame,StudioSettings settings,MeasurementProfile preset=null)
        {
            Sampling="";
            foreach(var source in Sources)
            {
                MeasurementProfile measure=preset??new MeasurementProfile{AlignmentVersion=1,Gain=settings.Gain.Value,LengthScale=settings.LengthScale.Value,ReverseCollider=settings.ReverseCollider.Value,Target=settings.Target.Value};
                Candidate candidate=source.Sample(1,1,measure.ReverseCollider,measure.Target,frame.Active);
                if(candidate!=null)try{if(measure.AlignmentVersion==0){measure=measure.Clone();measure.UpgradeStudio(candidate.Native);}measure.Apply(candidate);}catch(ArgumentException ex){source.Error=ex.Message;candidate=null;}
                if(candidate!=null)frame.Candidates.Add(candidate);else if(Sampling.Length<600)Sampling+=source.Label+"："+source.Error+"\n";
            }
        }
        internal StudioSource Find(string id){return Sources.Find(s=>s.Id==id);}
    }
}
