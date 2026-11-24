using System;

namespace KoiSync.Core
{
    // Observe geometry, never authored device output. The output phase is the
    // current observed position between confirmed extrema, not a free-running
    // oscillator extrapolated from a measured frequency.
    public sealed class LiveL0Cycle
    {
        private bool seeded,hasLow,hasHigh,previousPlaying;
        private int direction;
        private double lastTime,position,extreme,extremeTime,low,high,lastLow=-1,lastHigh=-1;
        private double motionPosition,motionTime,period;
        public bool Ready {get;private set;}
        public double Hertz {get;private set;}
        public double Phase {get{return Ready?Numbers.Clamp((position-low)/(high-low),0,1):0;}}
        public void Reset(){seeded=hasLow=hasHigh=Ready=false;direction=0;lastLow=lastHigh=-1;period=Hertz=0;}
        public void Observe(double time,double value,bool playing,double threshold)
        {
            if(!Numbers.Finite(time)||!Numbers.Finite(value)||!Numbers.Finite(threshold)||threshold<=0){Reset();return;}
            double dt=time-lastTime;
            if(!seeded||dt< -1e-7||dt>.5||!playing&&!previousPlaying&&Math.Abs(dt)>1e-7)
            {
                Reset();seeded=true;previousPlaying=playing;lastTime=extremeTime=motionTime=time;position=extreme=motionPosition=value;return;
            }
            position=value;previousPlaying=playing;
            if(dt<=1e-7)return;
            lastTime=time;
            if(Math.Abs(value-motionPosition)>=threshold){motionPosition=value;motionTime=time;}
            if(direction==0)
            {
                if(Math.Abs(value-extreme)>=threshold){direction=value>extreme?1:-1;extreme=value;extremeTime=time;}
            }
            else if(direction>0)
            {
                if(value>=extreme){extreme=value;extremeTime=time;}
                else if(extreme-value>=threshold)
                {
                    high=extreme;hasHigh=true;Period(extremeTime,lastHigh);lastHigh=extremeTime;
                    direction=-1;extreme=value;extremeTime=time;
                }
            }
            else
            {
                if(value<=extreme){extreme=value;extremeTime=time;}
                else if(value-extreme>=threshold)
                {
                    low=extreme;hasLow=true;Period(extremeTime,lastLow);lastLow=extremeTime;
                    direction=1;extreme=value;extremeTime=time;
                }
            }
            Ready=hasLow&&hasHigh&&high-low>=threshold*2;
            if(time-motionTime>Math.Max(.35,period*.75))Hertz=0;
        }
        private void Period(double now,double previous)
        {
            double elapsed=now-previous;
            if(previous>=0&&elapsed>=.02&&elapsed<=30){period=elapsed;Hertz=1/elapsed;}
        }
    }
}
