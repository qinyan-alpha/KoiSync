using System;
using System.Collections.Generic;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.Plugin
{
    internal sealed class ShaftReference
    {
        internal StudioObject Model,Actor;
        internal Transform Root;
        internal Renderer[] Renderers;
        internal bool Native;
        internal Transform[] DrivenChain;
        internal Transform BendStart,BendEnd;
        internal string BoneModelName;
        internal string Label{get{return Native?"原生骨骼链":DrivenChain!=null?"约束动骨 · "+BoneModelName:"道具网格 · "+Model.Name;}}
        internal string Identity{get{return DrivenChain!=null?"bones:"+Root.GetInstanceID():Model==null?"native":"mesh:"+Model.Id;}}
        internal MotionVector End(StudioGraph graph,bool reverse=false)
        {
            if(Root==null)throw new ArgumentException("来源挂载根节点已移除");
            if(DrivenChain!=null)
            {
                Transform end=DrivenChain[DrivenChain.Length-1];
                if(end==null||!end.gameObject.activeInHierarchy||!Root.gameObject.activeInHierarchy)
                    throw new ArgumentException("约束动骨模型已移除或停用，请重新扫描");
                return StudioSource.V(end.position);
            }
            if(Native)
            {
                var end=Actor.Bones.Get("cm_J_dan109_00");if(end==null||graph.Owner(end)!=Actor)throw new ArgumentException("缺少原生末端骨骼");
                return StudioSource.V(end.position);
            }
            if(!Model.Live)throw new ArgumentException("来源道具已停用");
            Transform anchor;var current=graph.AttachedActor(Model.Root,out anchor);
            if(current!=Actor||(Actor!=null&&anchor!=Root))throw new ArgumentException("道具挂载骨节点已改变，请重新扫描");
            return ShaftGeometry.Tip(Renderers,Root.position,reverse);
        }
    }
    internal static class ShaftGeometry
    {
        private sealed class VertexCache{internal Renderer Renderer;internal Mesh Shared;internal int Frame;internal Matrix4x4 Matrix;internal Vector3[] Points;}
        private static readonly Dictionary<int,VertexCache> vertices=new Dictionary<int,VertexCache>();
        private static Mesh scratch;
        internal static void Clear(){vertices.Clear();if(scratch!=null)UnityEngine.Object.Destroy(scratch);scratch=null;}
        private static bool Hint(string name)
        {string n=(name??"").ToLowerInvariant();return n.Contains("penis")||n.Contains("dildo")||n.Contains("dong")||n.Contains("cm_j_dan")||n.Contains("阴茎")||n.Contains("男根");}
        private static Renderer[] Meshes(StudioObject model,StudioGraph graph,out bool hint)
        {
            hint=Hint(model.Name)||Hint(model.Root.name);var all=new List<Renderer>();var shaft=new List<Renderer>();
            foreach(var r in model.Root.GetComponentsInChildren<Renderer>(true))
            {
                if(!r.enabled||!r.gameObject.activeInHierarchy||graph.Owner(r.transform)!=model)continue;
                var skin=r as SkinnedMeshRenderer;var filter=r.GetComponent<MeshFilter>();
                if(skin==null&&(!(r is MeshRenderer)||filter==null||filter.sharedMesh==null)||skin!=null&&skin.sharedMesh==null)continue;
                bool named=Hint(r.name);if(skin!=null)foreach(var b in skin.bones)if(b!=null&&Hint(b.name)){named=true;break;}
                all.Add(r);if(named){shaft.Add(r);hint=true;}
            }
            return (shaft.Count>0?shaft:all).ToArray();
        }
        private static int SharedBranch(StudioObject a,StudioObject b)
        {
            int distance=0;
            for(var p=a;p!=null&&!p.Character;p=p.Parent,distance++)
            {int other=0;for(var q=b;q!=null&&!q.Character;q=q.Parent,other++)if(p==q)return 1000-distance-other;}
            return 0;
        }
        internal static ShaftReference Resolve(StudioSource source)
        {
            var graph=source.Graph;var actor=source.SourceActor;
            // Some scene cards carry a constrained accessory skeleton beneath
            // a character attachment. It is not a separate Studio object, and
            // the dormant native BP skeleton cannot describe its visible pose.
            // Require bones actually used by a visible skinned mesh attached
            // to this source actor. Hidden duplicate rigs must not compete.
            if(!source.IsCollider&&source.Source.Character&&!source.Implicit)
            {
                var driven=ResolveDrivenChain(source,actor);
                if(driven!=null)return driven;
            }
            var context=source.SourceAttachment==null?source.Source:graph.Owner(source.SourceAttachment);
            ShaftReference best=null;int bestScore=-1;bool ambiguous=false;
            foreach(var obj in graph.Objects)
            {
                if(obj.Character||!obj.Live)continue;
                Transform root;var owner=graph.AttachedActor(obj.Root,out root);
                if(owner!=actor||owner!=null&&!StudioAdapter.AxialAnchor(owner,root)&&!source.IsCollider)continue;
                int branch=SharedBranch(context,obj);
                bool named;var renderers=Meshes(obj,graph,out named);if(renderers.Length==0)continue;
                // Unnamed meshes need a specific common controller/model scope. Generic
                // objects elsewhere under the actor are not inferred to be this source.
                bool own=source.IsCollider&&source.OwnsModelNode(obj.Root);
                if(!named&&!own&&branch<=0)continue;
                if(!named&&!Elongated(renderers))continue;
                if(owner==null&&!own)continue;
                int score=(own?2000:0)+branch+(named?100:0);
                if(score>bestScore){bestScore=score;best=new ShaftReference{Model=obj,Actor=owner,Root=root??obj.Root,Renderers=renderers};ambiguous=false;}
                else if(score==bestScore)ambiguous=true;
            }
            if(ambiguous)throw new ArgumentException("同一来源存在多个道具网格，无法确定末端；请明确道具绑定");
            if(best!=null)return best;
            if(actor==null||!actor.HasChain)throw new ArgumentException("没有可读取的来源道具网格或原生骨骼链");
            var nativeRoot=actor.Bones.Get("cm_J_dan100_00","cm_J_dan101_00");
            if(nativeRoot==null||graph.Owner(nativeRoot)!=actor)throw new ArgumentException("没有本角色所属的原生根节点");
            return new ShaftReference{Actor=actor,Root=nativeRoot,Native=true};
        }
        private static ShaftReference ResolveDrivenChain(StudioSource source,StudioObject actor)
        {
            if(actor==null||source.Target==null)return null;
            var candidates=new List<Transform>();
            foreach(var skin in actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if(!skin.enabled||!skin.gameObject.activeInHierarchy||skin.sharedMesh==null)continue;
                foreach(var bone in skin.bones)
                {
                    if(bone==null||!bone.IsChildOf(actor.Root))continue;
                    Transform first=bone;while(first!=null&&first!=actor.Root&&first.name!="shaft_00")first=first.parent;
                    if(first==null||first==actor.Root||candidates.Contains(first))continue;
                    bool named=false;for(Transform t=first;t!=null&&t!=actor.Root;t=t.parent)if(Hint(t.name)){named=true;break;}
                    if(named&&first.gameObject.activeInHierarchy)candidates.Add(first);
                }
            }
            Transform selected=null;Transform[] selectedChain=null;int selectedContacts=-1;bool ambiguous=false;
            foreach(var first in candidates)
            {
                var chain=new List<Transform>{first};Transform point=first;
                for(int n=1;n<64;n++)
                {
                    string name="shaft_"+n.ToString("00");Transform next=null;
                    for(int k=0;k<point.childCount;k++)if(point.GetChild(k).name==name){next=point.GetChild(k);break;}
                    if(next==null)break;chain.Add(next);point=next;
                }
                if(chain.Count<3)continue;
                int contacts=0;foreach(var link in source.Graph.Links)
                    if(link.Live&&link.Position&&link.Child.IsChildOf(first)&&TargetDrives(source,link))contacts++;
                if(contacts>selectedContacts){selected=first;selectedChain=chain.ToArray();selectedContacts=contacts;ambiguous=false;}
                else if(contacts==selectedContacts)ambiguous=true;
            }
            if(ambiguous)throw new ArgumentException("同一来源存在多条实际显示的模型骨骼链，请明确模型绑定");
            if(selected==null)return null;
            // Use the first contact-driven segment. The complete root-to-tip
            // chord includes hidden bends and can erase a visible root swing.
            Transform bendEnd=null,bendStart=null;
            for(int i=1;i<selectedChain.Length&&bendEnd==null;i++)foreach(var link in source.Graph.Links)
                if(link.Live&&link.Position&&link.Child==selectedChain[i]&&TargetDrives(source,link)){bendStart=selectedChain[i-1];bendEnd=selectedChain[i];break;}
            return new ShaftReference{Actor=actor,Root=selected,DrivenChain=selectedChain,BendStart=bendStart??selectedChain[0],BendEnd=bendEnd??selectedChain[1],BoneModelName=selected.parent==null?selected.name:selected.parent.name,Native=false};
        }
        private static bool TargetDrives(StudioSource source,StudioLink link)
        {
            if(link.Parent.IsChildOf(source.Target.Root))return true;
            Transform anchor;return source.Graph.AttachedActor(link.Parent,out anchor,link)==source.Target;
        }
        private static bool Elongated(Renderer[] renderers)
        {
            foreach(var r in renderers)
            {
                var skin=r as SkinnedMeshRenderer;var mesh=skin!=null?skin.sharedMesh:r.GetComponent<MeshFilter>().sharedMesh;
                var size=mesh.bounds.size;float max=Mathf.Max(size.x,Mathf.Max(size.y,size.z));float middle=size.x+size.y+size.z-max-Mathf.Min(size.x,Mathf.Min(size.y,size.z));
                if(max>1e-6&&max>middle*1.5f)return true;
            }
            return false;
        }
        private static Vector3[] WorldVertices(Renderer r)
        {
            if(r==null||!r.enabled||!r.gameObject.activeInHierarchy)throw new ArgumentException("来源网格已移除或隐藏");
            var skin=r as SkinnedMeshRenderer;var filter=r.GetComponent<MeshFilter>();var shared=skin!=null?skin.sharedMesh:filter==null?null:filter.sharedMesh;
            if(shared==null)throw new ArgumentException("来源网格已移除");VertexCache cache;
            if(vertices.TryGetValue(r.GetInstanceID(),out cache)&&cache.Renderer==r&&cache.Shared==shared&&cache.Frame==Time.frameCount&&cache.Matrix==r.localToWorldMatrix)return cache.Points;
            Vector3[] points;
            try
            {
                if(skin!=null){if(scratch==null)scratch=new Mesh{name="KoiSync measurement scratch",hideFlags=HideFlags.HideAndDontSave};skin.BakeMesh(scratch);points=scratch.vertices;}
                else points=shared.vertices;
            }
            catch(Exception ex){throw new ArgumentException("无法读取实际道具网格："+ex.GetType().Name);}
            if(points.Length<3)throw new ArgumentException("道具网格没有可读取的顶点");
            for(int i=0;i<points.Length;i++)points[i]=r.transform.TransformPoint(points[i]);
            vertices[r.GetInstanceID()]=new VertexCache{Renderer=r,Shared=shared,Frame=Time.frameCount,Matrix=r.localToWorldMatrix,Points=points};
            if(vertices.Count>64){var stale=new List<int>();foreach(var pair in vertices)if(pair.Value.Renderer==null||Time.frameCount-pair.Value.Frame>120)stale.Add(pair.Key);foreach(int key in stale)vertices.Remove(key);}
            return points;
        }
        internal static MotionVector Tip(Renderer[] renderers,Vector3 root,bool reverse=false)
        {
            var clouds=new List<Vector3[]>();MotionVector mean=new MotionVector();int count=0;
            foreach(var renderer in renderers){var points=WorldVertices(renderer);clouds.Add(points);foreach(var p in points)mean+=StudioSource.V(p);count+=points.Length;}
            if(count<3)throw new ArgumentException("缺少来源道具顶点");mean=mean*(1.0/count);
            double xx=0,xy=0,xz=0,yy=0,yz=0,zz=0;
            foreach(var points in clouds)foreach(var p in points){var v=StudioSource.V(p)-mean;xx+=v.X*v.X;xy+=v.X*v.Y;xz+=v.X*v.Z;yy+=v.Y*v.Y;yz+=v.Y*v.Z;zz+=v.Z*v.Z;}
            MotionVector axis=xx>=yy&&xx>=zz?new MotionVector(1,0,0):yy>=zz?new MotionVector(0,1,0):new MotionVector(0,0,1);
            for(int i=0;i<16;i++){var next=new MotionVector(xx*axis.X+xy*axis.Y+xz*axis.Z,xy*axis.X+yy*axis.Y+yz*axis.Z,xz*axis.X+yz*axis.Y+zz*axis.Z);if(next.Length<1e-12)throw new ArgumentException("道具网格长度退化");axis=next.Unit();}
            if(MotionVector.Dot(axis,mean-StudioSource.V(root))<0)axis=axis*-1;
            if(reverse)axis=axis*-1;
            double min=double.PositiveInfinity,max=double.NegativeInfinity;
            foreach(var points in clouds)foreach(var p in points){double d=MotionVector.Dot(StudioSource.V(p)-mean,axis);min=Math.Min(min,d);max=Math.Max(max,d);}
            var tip=new MotionVector();int cap=0;double threshold=max-(max-min)*.025;
            foreach(var points in clouds)foreach(var p in points)if(MotionVector.Dot(StudioSource.V(p)-mean,axis)>=threshold){tip+=StudioSource.V(p);cap++;}
            if(cap==0)throw new ArgumentException("无法定位道具端部");tip=tip*(1.0/cap);
            // Center the distal cap at the actual furthest vertex plane. No collider bounds are used.
            return tip+axis*(max-MotionVector.Dot(tip-mean,axis));
        }
    }
}
