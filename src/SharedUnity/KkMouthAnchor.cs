using System;
using System.Collections.Generic;
using UnityEngine;

namespace KoiSync.KK
{
    // Lip transforms are rig pivots, including on native heads; blend shapes can
    // move the rendered opening without moving them. Cache the skin opening once,
    // then evaluate its weighted centre without baking or scanning a mesh.
    internal sealed class KkMouthAnchor
    {
        private sealed class ShapeFrame {internal float Weight;internal Vector3[] Deltas;}
        private sealed class Shape {internal int Index;internal ShapeFrame[] Frames;}
        private static readonly Dictionary<Transform,KkMouthAnchor> cache=new Dictionary<Transform,KkMouthAnchor>();
        internal readonly SkinnedMeshRenderer Renderer;
        internal readonly int[] Vertices;
        private readonly Mesh mesh;
        private readonly Transform[] bones;
        private readonly float[] weights;
        private readonly Vector3[] points,work;
        private readonly Shape[] shapes;
        private int lastFrame=-1;
        private Vector3 position;
        internal bool Alive{get{return Renderer!=null&&Renderer.sharedMesh==mesh;}}
        internal static void Clear(){cache.Clear();}
        internal static KkMouthAnchor Resolve(Transform face,Vector3 seed,Vector3 right)
        {
            if(face==null)return null;
            KkMouthAnchor result;if(cache.TryGetValue(face,out result))return result;
            var renderer=face.GetComponent<SkinnedMeshRenderer>();
            result=renderer==null?null:Create(renderer,seed,right);cache[face]=result;return result;
        }
        private KkMouthAnchor(SkinnedMeshRenderer renderer,int[] selected)
        {
            Renderer=renderer;mesh=renderer.sharedMesh;Vertices=selected;bones=renderer.bones;
            weights=new float[bones.Length];points=new Vector3[bones.Length];work=new Vector3[bones.Length];
            var poses=mesh.bindposes;var allWeights=mesh.boneWeights;var vertices=mesh.vertices;
            foreach(int vertex in selected)
            {
                var w=allWeights[vertex];
                for(int j=0;j<4;j++)
                {int b=Bone(w,j);float weight=Weight(w,j)/selected.Length;if(weight<=0)continue;weights[b]+=weight;points[b]+=poses[b].MultiplyPoint3x4(vertices[vertex])*weight;}
            }
            var list=new List<Shape>();var delta=new Vector3[vertices.Length];
            for(int s=0;s<mesh.blendShapeCount;s++)
            {
                var frames=new List<ShapeFrame>();bool affects=false;
                for(int f=0;f<mesh.GetBlendShapeFrameCount(s);f++)
                {
                    mesh.GetBlendShapeFrameVertices(s,f,delta,null,null);var local=new Vector3[bones.Length];
                    foreach(int vertex in selected)
                    {
                        var w=allWeights[vertex];
                        for(int j=0;j<4;j++)
                        {int b=Bone(w,j);float weight=Weight(w,j)/selected.Length;if(weight>0)local[b]+=poses[b].MultiplyVector(delta[vertex])*weight;}
                    }
                    foreach(var value in local)if(value.sqrMagnitude>1e-18f)affects=true;
                    frames.Add(new ShapeFrame{Weight=mesh.GetBlendShapeFrameWeight(s,f),Deltas=local});
                }
                if(affects)list.Add(new Shape{Index=s,Frames=frames.ToArray()});
            }
            shapes=list.ToArray();
        }
        internal Vector3 Position
        {
            get
            {
                if(lastFrame==Time.frameCount)return position;
                Array.Copy(points,work,points.Length);
                foreach(var shape in shapes)
                {
                    float weight=Renderer.GetBlendShapeWeight(shape.Index);if(Mathf.Abs(weight)<1e-8f)continue;
                    var frames=shape.Frames;int upper=0;
                    while(upper<frames.Length-1&&weight>frames[upper].Weight)upper++;
                    ShapeFrame a=upper>0?frames[upper-1]:null,b=frames[upper];
                    float low=a==null?0:a.Weight,span=b.Weight-low;if(Mathf.Abs(span)<1e-8f)continue;
                    float blend=(weight-low)/span;
                    for(int i=0;i<work.Length;i++)work[i]+=(a==null?Vector3.zero:a.Deltas[i])*(1-blend)+b.Deltas[i]*blend;
                }
                position=Vector3.zero;
                for(int i=0;i<bones.Length;i++)if(weights[i]>0&&bones[i]!=null)position+=bones[i].TransformVector(work[i])+bones[i].position*weights[i];
                lastFrame=Time.frameCount;return position;
            }
        }
        private static KkMouthAnchor Create(SkinnedMeshRenderer renderer,Vector3 seed,Vector3 right)
        {
            var mesh=renderer.sharedMesh;if(mesh==null||!mesh.isReadable)return null;
            var bones=renderer.bones;
            // Native lip bones also use blend shapes. Their presence alone does
            // not prove that the midpoint of the bone pivots is the visible rim.
            var materials=renderer.sharedMaterials;int skin=-1;
            for(int i=0;i<materials.Length&&i<mesh.subMeshCount;i++)
                if(materials[i]!=null&&materials[i].name.StartsWith("cf_m_face_00",StringComparison.Ordinal)){skin=i;break;}
            if(skin<0)return null;
            var vertices=mesh.vertices;var weights=mesh.boneWeights;var poses=mesh.bindposes;
            if(weights.Length!=vertices.Length||poses.Length!=bones.Length)return null;
            var edges=new Dictionary<long,int>();var triangles=mesh.GetTriangles(skin);
            for(int i=0;i<triangles.Length;i+=3)
            {Edge(edges,triangles[i],triangles[i+1]);Edge(edges,triangles[i+1],triangles[i+2]);Edge(edges,triangles[i+2],triangles[i]);}
            var neighbours=new Dictionary<int,List<int>>();
            foreach(var edge in edges)if(edge.Value==1)
            {int a=(int)(edge.Key>>32),b=(int)(edge.Key&0xffffffff);Link(neighbours,a,b);Link(neighbours,b,a);}
            var seen=new HashSet<int>();int[] best=null;float nearest=.03f;
            foreach(var root in neighbours.Keys)
            {
                if(!seen.Add(root))continue;var pending=new Stack<int>();var loop=new List<int>();bool closed=true;pending.Push(root);
                while(pending.Count>0)
                {int v=pending.Pop();loop.Add(v);if(neighbours[v].Count!=2)closed=false;foreach(int n in neighbours[v])if(seen.Add(n))pending.Push(n);}
                if(!closed||loop.Count<8||loop.Count>256)continue;
                Vector3 centre=Vector3.zero;float min=float.MaxValue,max=float.MinValue;
                foreach(int index in loop)
                {
                    Vector3 point=Vector3.zero;var w=weights[index];
                    for(int j=0;j<4;j++){int bi=Bone(w,j);float weight=Weight(w,j);if(weight>0&&bi<bones.Length&&bones[bi]!=null)point+=bones[bi].TransformPoint(poses[bi].MultiplyPoint3x4(vertices[index]))*weight;}
                    centre+=point;float x=Vector3.Dot(point-seed,right);min=Mathf.Min(min,x);max=Mathf.Max(max,x);
                }
                centre/=loop.Count;float distance=(centre-seed).magnitude;
                // Exclude the facial seam, nostrils, eyes and outer head edge.
                if(max-min>=.006f&&max-min<=.08f&&min<0&&max>0&&distance<nearest){nearest=distance;best=loop.ToArray();}
            }
            return best==null?null:new KkMouthAnchor(renderer,best);
        }
        private static int Bone(BoneWeight w,int i){return i==0?w.boneIndex0:i==1?w.boneIndex1:i==2?w.boneIndex2:w.boneIndex3;}
        private static float Weight(BoneWeight w,int i){return i==0?w.weight0:i==1?w.weight1:i==2?w.weight2:w.weight3;}
        private static void Edge(Dictionary<long,int> edges,int a,int b)
        {long key=((long)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);int count;edges.TryGetValue(key,out count);edges[key]=count+1;}
        private static void Link(Dictionary<int,List<int>> links,int a,int b)
        {List<int> list;if(!links.TryGetValue(a,out list)){list=new List<int>();links[a]=list;}list.Add(b);}
    }
}
