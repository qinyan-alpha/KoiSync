using System;

namespace KoiSync.Core
{
    // Final device coordinates. One Hz means one complete out-and-back cycle.
    // Timeline time makes pauses and seeks deterministic; no Unity APIs here.
    public sealed class AxisOscillation
    {
        public readonly double Start,End,Minimum,Maximum,Hertz;
        public AxisOscillation(double start,double end,double minimum,double maximum,double hertz)
        {
            if(!Numbers.Finite(start)||!Numbers.Finite(end)||start<0||end<=start||
                !Numbers.Finite(minimum)||!Numbers.Finite(maximum)||minimum<0||maximum>1||minimum>maximum||
                !Numbers.Finite(hertz)||hertz<.1||hertz>20)throw new ArgumentException("Invalid L0 oscillation");
            Start=start;End=end;Minimum=minimum;Maximum=maximum;Hertz=hertz;
        }
        public double Value(double time)
        {
            if(!Numbers.Finite(time))throw new ArgumentException("Invalid oscillation time");
            double phase=(Numbers.Clamp(time,Start,End)-Start)*Hertz;
            phase-=Math.Floor(phase);
            return Minimum+(Maximum-Minimum)*(1-Math.Abs(2*phase-1));
        }
        public double NextTurn(double time)
        {return Start+(Math.Floor(Math.Max(0,time-Start)*Hertz*2+1e-7)+1)/(Hertz*2);}
        public bool Same(AxisOscillation other)
        {return other!=null&&Start==other.Start&&End==other.End&&Minimum==other.Minimum&&Maximum==other.Maximum&&Hertz==other.Hertz;}
    }
}
