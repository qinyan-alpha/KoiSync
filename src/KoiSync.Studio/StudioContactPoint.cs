using System.Collections.Generic;
using KoiSync.Core;
using KoiSync.KK;
using UnityEngine;

namespace KoiSync.Studio
{
    internal sealed class StudioContactPoint : KkContactPoint
    {
        internal StudioContactPoint(Dictionary<string,Transform> bones,OpeningKind kind,Transform node,Transform entryProxy=null)
            : base(bones,(ContactLandmarkKind)kind,node,entryProxy) {}

        internal void Pose(MotionVector[] path,out MotionVector position,out MotionVector right,out MotionVector inward)
        {
            base.Pose(out position,out right,out inward);
            if(IsHand&&path!=null&&path.Length>1)
                position=ClosestHandPosition(path);
            bool external=Surface;
            if(external&&path!=null&&path.Length>1)
            {
                if(MotionVector.Dot(inward,path[path.Length-1]-path[0])<0)inward=inward*-1;
                // Landmarks sit inside tissue or fingertips. Once the shaft lies within
                // the grip/finger stroke envelope, its centre is the interaction centre.
                double nearest=double.MaxValue;MotionVector contact=position;
                for(int i=1;i<path.Length;i++)
                {
                    var segment=path[i]-path[i-1];double square=MotionVector.Dot(segment,segment);
                    double t=square<1e-14?0:Numbers.Clamp(MotionVector.Dot(position-path[i-1],segment)/square,0,1);
                    var point=path[i-1]+segment*t;double distance=(point-position).Length;
                    if(distance<nearest){nearest=distance;contact=point;}
                }
                double snapRadius=IsHand?.050:.020;
                if(nearest<=snapRadius)position=contact;
            }
        }
    }
}
