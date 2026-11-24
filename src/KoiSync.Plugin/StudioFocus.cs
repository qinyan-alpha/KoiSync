using System.Collections.Generic;
using System.Globalization;
using KoiSync.Core;
using KoiSync.Wire;
using UnityEngine;

namespace KoiSync.Plugin
{
    internal sealed class StudioFocus
    {
        private readonly SourceFocus selector=new SourceFocus();
        internal string Status="自动选择 · 等待相机";
        internal void Reset(){selector.Reset();}
        internal string Choose(StudioAdapter adapter,Snapshot frame,Camera camera,string selected,double now)
        {
            if(camera==null||!camera.isActiveAndEnabled){selector.Reset();Status="自动选择 · 等待活动相机";return selected;}
            var options=new List<FocusOption>();
            if(frame.Active)foreach(var candidate in frame.Candidates)
            {
                StudioSource binding=adapter.Find(candidate.Id);if(binding==null||!candidate.Active||candidate.Native==null)continue;
                MotionVector p=candidate.Native.Target;Vector3 point=new Vector3((float)p.X,(float)p.Y,(float)p.Z);
                Vector3 view=camera.WorldToViewportPoint(point);
                if(view.z<camera.nearClipPlane||view.z>camera.farClipPlane||view.x<0||view.x>1||view.y<0||view.y>1)continue;
                double scale=Mathf.Max(.0001f,Mathf.Abs(camera.transform.lossyScale.y));
                options.Add(new FocusOption{Id=candidate.Id,ScreenRadius=new Vector2((view.x-.5f)*2,(view.y-.5f)*2).magnitude,
                    Distance=Vector3.Distance(camera.transform.position,point)/scale,Priority=binding.Bp?2:binding.Profile==NativeTarget.VAGINAL?1:0});
            }
            string chosen=selector.Choose(options,selected,now);
            FocusOption focused=options.Find(o=>o.Id==chosen);
            Status=options.Count==0?"自动选择 · 视野内暂无播放中的绑定，保留当前组":selector.Pending.Length>0?"自动选择 · 镜头已转向另一组，等待稳定 0.65 秒":
                "自动选择 · "+camera.name+(focused==null?"":" · 距离 "+focused.Distance.ToString("F2",CultureInfo.InvariantCulture)+" u · "+(focused.Priority==2?"BP":focused.Priority==1?"vagina":"其他绑定"));
            return chosen;
        }
    }
}
