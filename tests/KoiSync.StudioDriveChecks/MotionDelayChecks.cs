using KoiSync.Core;

internal static class MotionDelayChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var buffer=new MotionDelayBuffer();bool pulse;
        double[] Pose(double p)=>new[]{p,p,p,p,p,p};
        buffer.Add(0,0,1,Pose(.2),null);buffer.Add(.1,.1,1,Pose(.8),null);
        var midway=buffer.Read(.1,.05,out pulse);
        check(midway.All(p=>Math.Abs(p-.5)<1e-10)&&!pulse,"Delay interpolates all six axes between actual observations");
        check(buffer.Read(.1,0,out pulse).All(p=>p==.8),"Zero delay reproduces the current device pose");
        buffer.Add(.11,.1,0,Pose(.65),null);
        check(buffer.Read(.5,.25,out pulse).All(p=>p==.65),"Pausing flushes old movement and holds the current pose immediately");
        buffer.Add(.6,30,1,Pose(.9),null);
        check(buffer.Read(.61,.25,out pulse).All(p=>p==.9),"Seek discards the old scene clock instead of replaying stale motion");
        buffer.Clear();check(buffer.Read(1,.06,out pulse)==null,"Stop clears every queued pose");
        var wave=new AxisOscillation(0,2,.35,.75,10);
        buffer.Add(0,0,1,Pose(.5),wave);buffer.Add(.1,.1,1,Pose(.5),wave);
        check(Math.Abs(buffer.Read(.1,.05,out pulse)[0]-.75)<1e-10&&pulse,"Delayed 10 Hz effect preserves analytic peaks instead of smoothing them flat");
        buffer.Add(.11,.025,0,Pose(.55),wave);
        check(Math.Abs(buffer.Read(.19,.06,out pulse)[0]-.55)<1e-10,"Paused pulse holds its Timeline phase despite output delay");
        buffer.Clear();buffer.Add(0,1.9,1,Pose(.2),wave);buffer.Add(.1,2,1,Pose(.6),null);
        check(Math.Abs(buffer.Read(.1,0,out pulse)[0]-.6)<1e-10&&!pulse,"Expired effect returns to normal pose at its exact ending");
    }
}
