// Main-game snapshot of the 0.2.24 geometry implementation; independent of Studio development.
using System;
using System.Collections.Generic;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.MainGame
{
    // Alternate Studio props can supply the visible shaft while the native BP
    // mesh is hidden. Resolve renderer-owned bones once, never by scene-wide
    // bone names: copies of a prop have identical names but different poses.
    internal sealed class GameShaftModel
    {
        internal readonly SkinnedMeshRenderer Renderer;
        internal readonly Transform[] Chain;
        internal readonly bool PropFrame;
        private readonly Vector3[] restSegments;
        private readonly PropMotionPath propPath;
        internal int PointCount {get{return propPath==null?Chain.Length:propPath.Count;}}
        internal int[][] MaterialRings {get{return propPath==null?null:propPath.VertexIndices;}}
        internal void FillPath(MotionVector[] output){if(propPath!=null)propPath.Fill(output);else for(int i=0;i<Chain.Length;i++)output[i]=GameBinding.V(Chain[i].position);}
        internal Transform Root{get{return Chain[0];}}
        internal bool Visible{get{return Renderer!=null&&Renderer.enabled&&Renderer.gameObject.activeInHierarchy;}}
        private GameShaftModel(SkinnedMeshRenderer renderer,Transform[] chain,Vector3[] segments,bool prop=false,PropMotionPath propPath=null)
        {Renderer=renderer;Chain=chain;restSegments=segments;PropFrame=prop;this.propPath=propPath;}
        internal bool Contains(Transform node){return node!=null&&Array.IndexOf(Chain,node)>=0;}
        internal double BaseLength
        {
            get
            {
                if(propPath!=null)return propPath.BaseLength;
                double result=0;
                foreach(var segment in restSegments)result+=Renderer.transform.TransformVector(segment).magnitude;
                return result;
            }
        }
        internal static List<GameShaftModel> Discover(ChaControl actor)
        {
            var result=new List<GameShaftModel>();
            foreach(var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var model=Read(renderer);if(model!=null)result.Add(model);
            }
            return result;
        }
        internal static GameShaftModel AnimatedProp(ChaControl actor)
        {
            // kPlug 3.4's authored p_item_dildo uses a seven-bone spine rig.
            // These names are scoped to that prop's renderer, never the actor's
            // identically named body bones. Its own animation is the source.
            foreach(var root in actor.GetComponentsInChildren<Transform>(true))
            {
                if(root.name!="p_item_dildo"||!root.gameObject.activeInHierarchy)continue;
                foreach(var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if(renderer.name!="o_body_a"||renderer.bones.Length!=7)continue;
                    var indices=new List<int>();var bones=renderer.bones;
                    foreach(string name in new[]{"cf_j_hips","cf_j_spine01","cf_j_spine02","cf_j_spine03","cf_j_neck","cf_j_head"})
                    {int index=Index(bones,name);if(index<0){indices.Clear();break;}indices.Add(index);}
                    if(indices.Count==6){var model=FromIndices(renderer,indices,true);if(model!=null&&model.Visible)return model;}
                }
            }
            return null;
        }
        private static GameShaftModel Read(SkinnedMeshRenderer renderer)
        {
            // Recognize complete renderer-owned rigs, including the Human01
            // replacement used by cards that hide the native BP mesh. A name
            // on an unrelated object or a hidden duplicate cannot supply nodes.
            var mesh=renderer.sharedMesh;if(mesh==null)return null;
            var bones=renderer.bones;var indices=new List<int>();
            int root=Index(bones,"dick_base"),tip=Index(bones,"penor_e");
            if(root>=0&&tip>=0)
            {
                indices.Add(root);
                for(int i=0;i<10;i++)
                {
                    int index=Index(bones,"shaft_"+i.ToString("00"));if(index<0)return null;
                    if(i>0&&bones[index].parent!=bones[indices[indices.Count-1]])return null;
                    indices.Add(index);
                }
                int glans=Index(bones,"peno_glan");if(glans<0||bones[glans].parent!=bones[indices[indices.Count-1]]||bones[tip].parent!=bones[glans])return null;
                indices.Add(glans);indices.Add(tip);
            }
            else
            {
                foreach(string name in new[]{"Penis00","Penis01","Penis02","Penis03","Penis04","Penis05","PenisTip00","PenisTip01","PenisTip01_end"})
                {
                    int index=Index(bones,name);if(index<0)return null;
                    if(indices.Count>0&&bones[index].parent!=bones[indices[indices.Count-1]])return null;
                    indices.Add(index);
                }
            }
            return FromIndices(renderer,indices,false);
        }
        private static GameShaftModel FromIndices(SkinnedMeshRenderer renderer,List<int> indices,bool prop)
        {
            var mesh=renderer.sharedMesh;if(mesh==null)return null;var bones=renderer.bones;var poses=mesh.bindposes;if(poses.Length!=bones.Length)return null;
            var chain=new Transform[indices.Count];var segments=new Vector3[indices.Count-1];
            for(int i=0;i<indices.Count;i++)
            {
                chain[i]=bones[indices[i]];
                if(i>0)segments[i-1]=poses[indices[i]].inverse.MultiplyPoint3x4(Vector3.zero)-poses[indices[i-1]].inverse.MultiplyPoint3x4(Vector3.zero);
            }
            var path=prop?PropMotionPath.Read(renderer):null;if(prop&&path==null)return null;
            var model=new GameShaftModel(renderer,chain,segments,prop,path);
            return Numbers.Finite(model.BaseLength)&&model.BaseLength>.001?model:null;
        }
        private static int Index(Transform[] bones,string name)
        {for(int i=0;i<bones.Length;i++)if(bones[i]!=null&&bones[i].name==name)return i;return -1;}
    }
}
