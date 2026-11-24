using System;
using System.Collections.Generic;

namespace KoiSync.Core
{
    // Device-only interpolation buffer. Timestamps use a monotonic wall clock;
    // the independent scene clock preserves pause, speed and event phase.
    public sealed class MotionDelayBuffer
    {
        private sealed class Frame
        {internal double Wall,Time,Rate;internal double[] Values;internal AxisOscillation Pulse;}
        private readonly List<Frame> frames=new List<Frame>();
        public void Clear(){frames.Clear();}
        public void Add(double wall,double time,double rate,double[] values,AxisOscillation pulse)
        {
            if(!Numbers.Finite(wall)||!Numbers.Finite(time)||!Numbers.Finite(rate)||rate<0||values==null||values.Length!=6)throw new ArgumentException("Invalid motion buffer frame");
            foreach(double value in values)if(!Numbers.Finite(value))throw new ArgumentException("Invalid motion buffer channel");
            if(frames.Count>0)
            {
                var last=frames[frames.Count-1];double elapsed=wall-last.Wall;
                if(rate==0||last.Rate==0||elapsed<0||elapsed>MotionPipeline.FreshnessSeconds||time<last.Time-.001||Math.Abs(time-last.Time-elapsed*last.Rate)>.12)frames.Clear();
            }
            frames.Add(new Frame{Wall=wall,Time=time,Rate=rate,Values=(double[])values.Clone(),Pulse=pulse});
            while(frames.Count>2&&(frames.Count>256||frames[1].Wall<wall-1))frames.RemoveAt(0);
        }
        public double[] Read(double wall,double delay,out bool oscillating)
        {
            oscillating=false;if(frames.Count==0)return null;
            var last=frames[frames.Count-1];double at=last.Rate==0?last.Wall:wall-Numbers.Clamp(delay,0,.25);
            var a=frames[0];var b=a;
            for(int i=1;i<frames.Count;i++){b=frames[i];if(b.Wall>=at)break;a=b;}
            double fraction=b.Wall>a.Wall?Numbers.Clamp((at-a.Wall)/(b.Wall-a.Wall),0,1):0;
            double time=a.Time+(b.Time-a.Time)*fraction;
            if(at>=last.Wall){a=b=last;time=last.Time+Math.Max(0,at-last.Wall)*last.Rate;fraction=0;}
            var values=new double[6];for(int i=0;i<6;i++)values[i]=a.Values[i]+(b.Values[i]-a.Values[i])*fraction;
            var pulse=a.Pulse!=null&&time>=a.Pulse.Start&&time<a.Pulse.End?a.Pulse:b.Pulse;
            if(pulse!=null&&time>=pulse.Start&&time<pulse.End){values[0]=pulse.Value(time);oscillating=true;}
            return values;
        }
    }
}
