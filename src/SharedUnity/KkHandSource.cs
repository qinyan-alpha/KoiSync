using System.Collections.Generic;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.KK
{
    // Source-side fingers are different from a target hand gripping a shaft.
    // Cache both common contact fingers; fix the selected chain at binding.
    internal class KkHandSource
    {

        internal readonly Transform Wrist,Index,Little;
        private readonly List<Transform[]> fingers=new List<Transform[]>();
        internal KkHandSource(Dictionary<string,Transform> bones,bool left)
        {
            string side=left?"_L":"_R";
            Wrist=Get(bones,"cf_j_hand"+side);Index=Get(bones,"cf_j_index01"+side);Little=Get(bones,"cf_j_little01"+side);
            foreach(string finger in new[]{"middle","index"})
            {
                var chain=new List<Transform>();
                for(int i=1;i<=4;i++){var node=Get(bones,"cf_j_"+finger+i.ToString("00")+side);if(node==null)break;chain.Add(node);}
                if(chain.Count>=3)fingers.Add(chain.ToArray());
            }
        }
        internal bool Alive{get{return Wrist!=null&&Index!=null&&Little!=null&&fingers.Count>0;}}
        internal Transform[] Closest(Vector3 entry,out double distance)
        {
            Transform[] best=null;distance=double.MaxValue;
            foreach(var chain in fingers)
            {
                var path=new MotionVector[chain.Length];bool valid=true;
                for(int i=0;i<chain.Length;i++){if(chain[i]==null){valid=false;break;}path[i]=Motion(chain[i].position);}
                if(!valid)continue;
                double d=StudioShaftMotion.ClosestDistance(path,Motion(entry));
                if(d<distance){distance=d;best=chain;}
            }
            return best;
        }
        private static MotionVector Motion(Vector3 point){return new MotionVector(point.x,point.y,point.z);}
        private static Transform Get(Dictionary<string,Transform> bones,string name)
        {Transform node;return bones.TryGetValue(name,out node)?node:null;}
    }
}
