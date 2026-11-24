using System;

namespace KoiSync.Core
{
    // An explicitly requested alternative script, never a measured motion and
    // never an automatic replacement for an author's original L0 track.
    public static class ScriptVibration
    {
        public static double At(double primary,double time,double start,double end,double amplitude,double hertz,double fade)
        {
            if(!Numbers.Finite(primary)||!Numbers.Finite(time)||!Numbers.Finite(start)||!Numbers.Finite(end)||!Numbers.Finite(amplitude)||!Numbers.Finite(hertz)||!Numbers.Finite(fade)||end<=start||amplitude<0||amplitude>.5||hertz<=0||hertz>15||fade<0)
                throw new ArgumentException("Invalid script vibration parameters");
            if(time<=start||time>=end)return primary;
            double ramp=fade==0?1:Math.Min(1,Math.Min((time-start)/fade,(end-time)/fade));
            double envelope=ramp*ramp*(3-2*ramp);
            return Numbers.Clamp(primary+amplitude*envelope*Math.Sin(2*Math.PI*hertz*(time-start)),0,1);
        }
    }
}
