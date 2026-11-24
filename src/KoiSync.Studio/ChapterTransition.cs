using System;
using KoiSync.Core;

namespace KoiSync.Studio
{
    // Smooths the device output across a chapter change. A new chapter rebinds
    // and resets the live filter, so its first pose can differ greatly from
    // the last pose sent. When that jump exceeds the threshold, the difference
    // is kept as an offset and faded out with a smoothstep curve; the new
    // chapter's own motion continues unchanged underneath.
    public sealed class ChapterTransition
    {
        public double Seconds=.5;
        public double Threshold=.08;
        private readonly double[] offset=new double[6],last=new double[6];
        private bool hasLast;
        private double elapsed,duration;
        public bool Active {get{return duration>0&&elapsed<duration;}}
        public double Progress {get{return Active?elapsed/duration:1;}}
        public double LastJump {get;private set;}

        public void Reset(){hasLast=false;duration=0;elapsed=0;}

        // values: final six-axis output for this frame, edited in place.
        // boundary: the chapter differs from the one of the previous output.
        // holdL0: L0 is driven by a device-side oscillation and must not move.
        public void Apply(double[] values,double seconds,bool boundary,bool holdL0)
        {
            if(values==null||values.Length!=6)throw new ArgumentException("Missing six-axis output");
            double dt=Numbers.Finite(seconds)?Math.Max(0,seconds):0;
            if(boundary&&hasLast)
            {
                double jump=0;
                for(int i=holdL0?1:0;i<6;i++)jump=Math.Max(jump,Math.Abs(last[i]-values[i]));
                LastJump=jump;
                // A boundary during a fade always rebases onto the current
                // output; the old offset no longer matches the new chapter.
                if(Seconds>0&&(jump>Threshold||Active))
                {
                    for(int i=0;i<6;i++)offset[i]=i==0&&holdL0?0:last[i]-values[i];
                    elapsed=0;duration=Seconds;dt=0;
                }
            }
            if(Active)
            {
                elapsed+=dt;
                double p=Math.Min(1,elapsed/duration),weight=1-p*p*(3-2*p);
                for(int i=0;i<6;i++)if(i>0||!holdL0)values[i]=Numbers.Clamp(values[i]+offset[i]*weight,0,1);
                if(p>=1)duration=0;
            }
            Array.Copy(values,last,6);hasLast=true;
        }
    }
}
