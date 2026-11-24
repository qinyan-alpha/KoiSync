using System;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.MainGame
{
    // Authored p_item_dildo has offset skin bind poses: its bone pivots are
    // not its visible centreline. Cache material rings, then evaluate their
    // actual skin weights against the final renderer-owned bone matrices.
    internal sealed class PropMotionPath
    {
        private sealed class Vertex
        {internal int Index;internal readonly int[] Bones=new int[4];internal readonly float[] Weights=new float[4];internal readonly Vector3[] Points=new Vector3[4];}
        private readonly SkinnedMeshRenderer renderer;
        private readonly Transform[] bones;
        private readonly Matrix4x4[] matrices;
        private readonly Vertex[][] rings;
        private readonly Vector3[] rest;
        internal int Count {get{return rings.Length;}}
        internal int[][] VertexIndices
        {get{var result=new int[rings.Length][];for(int i=0;i<rings.Length;i++){result[i]=new int[rings[i].Length];for(int j=0;j<rings[i].Length;j++)result[i][j]=rings[i][j].Index;}return result;}}
        private PropMotionPath(SkinnedMeshRenderer renderer,Transform[] bones,Vertex[][] rings,Vector3[] rest)
        {this.renderer=renderer;this.bones=bones;this.rings=rings;this.rest=rest;matrices=new Matrix4x4[bones.Length];}
        internal double BaseLength
        {get{double n=0;for(int i=1;i<rest.Length;i++)n+=renderer.transform.TransformVector(rest[i]-rest[i-1]).magnitude;return n;}}
        internal static PropMotionPath Read(SkinnedMeshRenderer renderer)
        {
            var mesh=renderer.sharedMesh;if(mesh==null)return null;var vertices=mesh.vertices;var weights=mesh.boneWeights;var poses=mesh.bindposes;var bones=renderer.bones;
            if(vertices.Length==0||weights.Length!=vertices.Length||poses.Length!=bones.Length)return null;
            float low=float.MaxValue,high=float.MinValue;foreach(var v in vertices){low=Math.Min(low,v.y);high=Math.Max(high,v.y);}if(high-low<.02f)return null;
            var rings=new Vertex[7][];var rest=new Vector3[7];
            for(int ring=0;ring<rings.Length;ring++)
            {
                float y=low+(high-low)*ring/(rings.Length-1),nearest=float.MaxValue;
                foreach(var v in vertices)nearest=Math.Min(nearest,Math.Abs(v.y-y));
                var selected=new System.Collections.Generic.List<Vertex>();Vector3 center=Vector3.zero;
                for(int i=0;i<vertices.Length;i++)
                {
                    if(Math.Abs(vertices[i].y-y)>nearest+(high-low)*.004f)continue;
                    var w=weights[i];var point=new Vertex{Index=i};int[] indices={w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};float[] values={w.weight0,w.weight1,w.weight2,w.weight3};
                    for(int k=0;k<4;k++){if(values[k]>0&&(indices[k]<0||indices[k]>=bones.Length))return null;point.Bones[k]=indices[k];point.Weights[k]=values[k];if(values[k]>0)point.Points[k]=poses[indices[k]].MultiplyPoint3x4(vertices[i]);}
                    selected.Add(point);center+=vertices[i];
                }
                if(selected.Count==0)return null;rings[ring]=selected.ToArray();rest[ring]=center/selected.Count;
            }
            return new PropMotionPath(renderer,bones,rings,rest);
        }
        internal void Fill(MotionVector[] output)
        {
            if(output.Length!=rings.Length)throw new ArgumentException("Invalid authored prop path");
            for(int i=0;i<bones.Length;i++){if(bones[i]==null)throw new InvalidOperationException("原生道具骨链正在切换");matrices[i]=bones[i].localToWorldMatrix;}
            for(int ring=0;ring<rings.Length;ring++)
            {
                Vector3 center=Vector3.zero;foreach(var vertex in rings[ring])for(int k=0;k<4;k++)if(vertex.Weights[k]>0)center+=matrices[vertex.Bones[k]].MultiplyPoint3x4(vertex.Points[k])*vertex.Weights[k];
                output[ring]=GameBinding.V(center/rings[ring].Length);
            }
        }
    }
}
