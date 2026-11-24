using System.Collections.Generic;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.MainGame
{
    // Captured at the game's SetMale initialization event, before Lookat_dan
    // can pin the tip to an entry. A fixed model reference, never a running max.
    internal static class NativeModelReference
    {
        private sealed class Reference {internal Transform Root,Tip;internal double Length;}
        private static readonly Dictionary<int,Reference> references=new Dictionary<int,Reference>();
        internal static void Capture(object look)
        {
            var source=RuntimeAccess.Read(look,"male") as ChaControl;
            var root=RuntimeAccess.Read(look,"objDanBase") as GameObject;
            var tip=RuntimeAccess.Read(look,"objDanTop") as GameObject;
            if(source==null||root==null||tip==null)return;
            // The native look-at pivot is dan101; this adapter includes its
            // dan100 reference segment in the measured chain.
            var reference=RuntimeAccess.Read(RuntimeAccess.Read(look,"lookat"),"trfUpAxis") as Transform;
            var start=reference!=null&&reference.IsChildOf(source.transform)?reference:root.transform;
            Reference existing;
            if(references.TryGetValue(source.GetInstanceID(),out existing)&&existing.Root==start&&existing.Tip==tip.transform)return;
            double length=0;var node=tip.transform;int count=0;
            while(node!=start&&node.parent!=null&&count++<32){length+=Vector3.Distance(node.position,node.parent.position);node=node.parent;}
            if(node!=start)return;
            if(!Numbers.Finite(length)||length<.02||length>.5)return;
            references[source.GetInstanceID()]=new Reference{Root=start,Tip=tip.transform,Length=length};
        }
        internal static double Length(ChaControl source,Transform root,Transform tip)
        {
            Reference value;
            return source!=null&&references.TryGetValue(source.GetInstanceID(),out value)&&value.Root==root&&value.Tip==tip?value.Length:0;
        }
        internal static void Clear(){references.Clear();}
    }
}
