using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using KoiSync.Core;
using KoiSync.Wire;
using UnityEngine;

namespace KoiSync.Plugin
{
    internal sealed class StudioShape
    {
        internal SkinnedMeshRenderer Renderer;
        internal Mesh Mesh;
        internal Transform Owner;
        internal int Index;
        internal string Id,Name,Label;
        internal bool Read(out double weight)
        {
            weight=double.NaN;
            if(Renderer==null||Owner==null||!Owner.gameObject.activeInHierarchy||!Renderer.gameObject.activeInHierarchy||
               !Renderer.transform.IsChildOf(Owner)||Renderer.sharedMesh!=Mesh||Mesh==null||Index>=Mesh.blendShapeCount||Mesh.GetBlendShapeName(Index)!=Name)return false;
            weight=Renderer.GetBlendShapeWeight(Index);return Numbers.Finite(weight);
        }
    }

    // Read-only scene observation. Shape selections and preview mode are session-local.
    internal sealed class StudioExperiment
    {
        internal readonly ConfigEntry<float> Zero,Full;
        internal readonly List<StudioShape> Shapes=new List<StudioShape>();
        internal StudioShape Selected;
        internal bool AbsolutePreview,ShowDirection=true;
        internal string Filter="penetr",Context="",Status="请先选择工作室绑定组";
        internal double Weight=double.NaN,Absolute=double.NaN,ObservedMin=double.NaN,ObservedMax=double.NaN;
        internal int Samples;
        internal NativeMotionResult Skeletal;
        internal BpDirectionObservation Bp=new BpDirectionObservation();
        private string scope="";
        private float nextScan;
        internal StudioExperiment(ConfigFile config)
        {
            Zero=config.Bind("Studio Experiment","WeightAtZero",0f,"Blendshape weight mapped to L0 000; preview only");
            Full=config.Bind("Studio Experiment","WeightAtFull",100f,"Blendshape weight mapped to L0 999; reversed endpoints are allowed");
        }
        internal void Select(StudioShape shape){Selected=shape;AbsolutePreview=false;ResetObservation();}
        internal void ResetObservation(){ObservedMin=ObservedMax=double.NaN;Samples=0;}
        internal void Rescan(){nextScan=0;}
        internal void Update(StudioSource source,Candidate candidate,float now)
        {
            string key=source==null?"":source.Id+":"+(source.Source.Root==null?0:source.Source.Root.GetInstanceID())+":"+(source.Target.Root==null?0:source.Target.Root.GetInstanceID());
            if(key!=scope){scope=key;Shapes.Clear();Selected=null;AbsolutePreview=false;nextScan=0;ResetObservation();}
            Context=source==null?"未选择绑定组":source.Label;
            Weight=Absolute=double.NaN;Skeletal=null;Bp=new BpDirectionObservation();
            if(source==null){Status="请先选择工作室绑定组";return;}
            if(now>=nextScan){Shapes.Clear();Scan(source.Target,"目标");if(source.Source!=source.Target)Scan(source.Source,"来源");nextScan=now+1;}
            if(candidate!=null&&candidate.Native!=null){Skeletal=NativeMotion.Solve(candidate.Native);Bp=BpDirectionObservation.Read(source,candidate.Native);}
            if(Selected==null)Status="手动选择一个形态键；未选择时保留骨骼输入";
            else if(!Selected.Read(out Weight))Status="所选形态键已失效；请重新扫描并选择，不自动替换";
            else
            {
                Samples++;ObservedMin=double.IsNaN(ObservedMin)?Weight:Math.Min(ObservedMin,Weight);ObservedMax=double.IsNaN(ObservedMax)?Weight:Math.Max(ObservedMax,Weight);
                Absolute=ShapeReference.Normalize(Weight,Zero.Value,Full.Value);
                Status=!Numbers.Finite(Absolute)?"端点无效：两个权重需为不同的有限数值":
                    "已读取最终权重 · "+(ObservedMax-ObservedMin<.0001?"本次观察尚未变化":"本次观察检测到变化");
                if(Absolute<0||Absolute>1)Status+=" · 超出端点范围，预览将限幅";
            }
            if(candidate!=null&&candidate.Native!=null&&AbsolutePreview)
            {
                candidate.Native.ShapePreview=true;candidate.Native.ShapeL0=Absolute;
                candidate.MotionKey+="|shape-preview:"+(Selected==null?"missing":Selected.Id)+":"+Zero.Value+":"+Full.Value;
            }
        }
        private void Scan(StudioObject owner,string role)
        {
            if(owner.Root==null)return;
            foreach(SkinnedMeshRenderer renderer in owner.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh=renderer.sharedMesh;if(mesh==null)continue;
                for(int i=0;i<mesh.blendShapeCount;i++)
                {
                    string name=mesh.GetBlendShapeName(i);
                    Shapes.Add(new StudioShape{Renderer=renderer,Mesh=mesh,Owner=owner.Root,Index=i,Name=name,
                        Id=renderer.GetInstanceID()+":"+mesh.GetInstanceID()+":"+i+":"+name,
                        Label=role+" · "+renderer.name+" #"+renderer.GetInstanceID()+" / "+name});
                }
            }
        }
    }

    internal sealed class BpDirectionObservation
    {
        internal string Status="该绑定来源没有可用的 BP 控制器",AutoTarget="—",EntryName="—",EndName="—";
        internal bool Valid;
        internal MotionVector Entry,End;
        internal double Deviation,Yaw,Pitch,TipDeviation=double.NaN;
        internal static BpDirectionObservation Read(StudioSource source,NativeMotionSample sample)
        {
            var result=new BpDirectionObservation();Component controller=source.Source.Bp;
            if(controller==null)return result;
            object mode=StudioMembers.Read(controller,"DanAutoTarget");int index=mode is int?(int)mode:-1;
            result.AutoTarget=index>=0&&index<4?new[]{"Off","Vaginal","Anal","Oral"}[index]:"未知";
            var behaviour=controller as Behaviour;
            if(behaviour==null||!behaviour.isActiveAndEnabled||!StudioMembers.Flag(controller,"danTargetsValid",false)){result.Status="BP 控制器未启用或目标未初始化";return result;}
            Transform entry=StudioMembers.Read(controller,"danEntryChild") as Transform,end=StudioMembers.Read(controller,"danEndChild") as Transform;
            result.EntryName=Convert.ToString(StudioMembers.Read(controller,"danEntryParentName"));result.EndName=Convert.ToString(StudioMembers.Read(controller,"danEndParentName"));
            object collision=StudioMembers.Read(StudioMembers.Read(controller,"collisionAgent"),"m_collisionCharacter");
            if(source.Target.Actor==null||!ReferenceEquals(collision,source.Target.Actor)){result.Status="BP 当前对象与所选绑定目标不一致；不混用方向";return result;}
            if(entry==null||end==null||!entry.gameObject.activeInHierarchy||!end.gameObject.activeInHierarchy){result.Status="BP 入口 / 内部控制点不可用";return result;}
            try
            {
                result.Entry=StudioSource.V(entry.position);result.End=StudioSource.V(end.position);
                var angles=DirectionComparison.Measure(sample,result.Entry,result.End);
                result.Deviation=angles.Deviation;result.Yaw=angles.Yaw;result.Pitch=angles.Pitch;
                Transform[] chain=StudioSource.BpChain(controller);
                if(chain!=null&&chain.Length>=2)
                {
                    MotionVector tip=StudioSource.V(chain[chain.Length-1].position-chain[chain.Length-2].position);
                    if(tip.Length>=1e-7)result.TipDeviation=MotionVector.Angle(tip,result.End-result.Entry);
                }
                result.Valid=true;result.Status="BP 控制点方向可比较；两点不能确定轴向扭转";
            }
            catch(ArgumentException){result.Status="BP 控制点重合或方向退化";}
            return result;
        }
    }
}
