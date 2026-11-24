using System;

namespace KoiSync.Core
{
    // Keep the nearest equivalent revolution instead of folding every sample.
    public sealed class ContinuousAngle
    {
        private bool ready;
        private double value;
        private readonly double period;
        public ContinuousAngle(double period=360){this.period=period;}
        public void Reset(){ready=false;value=0;}
        public double Peek(double wrapped)
        {
            if(!Numbers.Finite(wrapped))return ready?value:0;
            return ready?value+Math.IEEERemainder(wrapped-value,period):Math.IEEERemainder(wrapped,period);
        }
        public double Resolve(double wrapped)
        {
            value=Peek(wrapped);
            ready=true;return value;
        }
    }
}
