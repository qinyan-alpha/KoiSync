using System;
using System.Collections.Generic;

namespace KoiSync.Core
{
    public sealed class FocusOption
    {
        public string Id;
        public double ScreenRadius,Distance;
        public int Priority;
        public double Cost{get{return ScreenRadius*2+Math.Log(1+Distance)*.55-Priority*.1;}}
    }
    public sealed class SourceFocus
    {
        private string pending="";
        private double since;
        public string Pending{get{return pending;}}
        public void Reset(){pending="";since=0;}
        public string Choose(IList<FocusOption> options,string selected,double now)
        {
            FocusOption best=null,current=null;
            foreach(var option in options)
            {
                if(!Numbers.Finite(option.Cost)||option.Distance<0||option.ScreenRadius<0)continue;
                if(option.Id==selected)current=option;
                if(best==null||option.Cost<best.Cost-1e-8||Math.Abs(option.Cost-best.Cost)<1e-8&&string.CompareOrdinal(option.Id,best.Id)<0)best=option;
            }
            if(best==null||best.Id==selected||current!=null&&best.Cost+.06>=current.Cost){Reset();return selected;}
            if(string.IsNullOrEmpty(selected)){Reset();return best.Id;}
            if(pending!=best.Id||now<since){pending=best.Id;since=now;return selected;}
            if(now-since<.65)return selected;
            Reset();return best.Id;
        }
    }
    // A camera switch can continue an already enabled session after the new source calibrates.
    // Manual stop, unavailable playback, preview and disconnect cancel this continuation.
    public sealed class SourceHandoff
    {
        private double deadline;
        private double[] previous;
        public bool Pending{get;private set;}
        public void Cancel(){Pending=false;previous=null;}
        public void Begin(MotionPipeline pipeline,bool connected,double now)
        {
            bool continuing=connected&&(pipeline.Armed||Pending);
            if(!continuing){Cancel();return;}
            if(!Pending){previous=(double[])pipeline.Values.Clone();deadline=now+6;}
            Pending=true;
        }
        public bool Update(MotionPipeline pipeline,bool connected,double now)
        {
            if(!Pending)return false;
            if(!connected||!pipeline.HasLiveSample||pipeline.ShapePreview||now>deadline){Cancel();return false;}
            if(!pipeline.Calibrated)return false;
            pipeline.ContinueFrom(previous);bool armed=pipeline.Arm();Cancel();return armed;
        }
    }
}
