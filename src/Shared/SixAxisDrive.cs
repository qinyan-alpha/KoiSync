using System;

namespace KoiSync.Core
{
    public enum DriveMappingMode { Absolute,Soft,Relative,Manual }
    // Six explicit channels, no learned ranges, animation profiles or history.
    public class SixAxisDrive
    {
        public readonly double[] Input = {0,.5,.5,.5,.5,.5};
        public readonly double[] Target = {0,.5,.5,.5,.5,.5};
        public readonly double[] Values = {0,.5,.5,.5,.5,.5};
        public readonly double[] Minimum = new double[6];
        public readonly double[] Maximum = {1,1,1,1,1,1};
        public readonly double[] Gain = {1,1,1,1,1,1};
        public readonly bool[] Enabled = {true,true,true,true,true,true};
        public bool SoftTwistLimit = true,SoftRollLimit = true,SoftPitchLimit = true;
        public readonly double[] RotationRequests = {.5,.5,.5};
        public double TwistRequestedDegrees { get { return (.5-RotationRequests[0])*240; } }
        public double PitchRequestedDegrees { get { return (RotationRequests[2]-.5)*50; } }
        public bool Ready { get; private set; }
        public bool RelativeRotation { get; private set; }
        public double PhysicalL0Minimum;
        public double SmoothingSeconds=.025;
        public double MaximumChangePerSecond=2;

        public void Reset() { Ready = false; }

        // Recorded files already contain the final L0 direction, gain and filtering.
        public void UpdateScript(double[] positions)
        {
            RelativeRotation=false;
            if(positions==null||positions.Length!=6)throw new ArgumentException("Missing script axes");
            for(int i=0;i<6;i++)
            {
                double value=Numbers.Finite(positions[i])?Numbers.Clamp(positions[i],0,1):i==0?1:.5;
                double low=i==0?Math.Max(Minimum[i],PhysicalL0Minimum):Minimum[i];
                if(low>Maximum[i])throw new ArgumentException("L0 travel limits do not overlap the player length limit");
                Input[i]=Target[i]=value;Values[i]=Numbers.Clamp(value,low,Maximum[i]);
            }
            for(int i=0;i<3;i++)RotationRequests[i]=Input[i+3];
            Ready=true;
        }

        public void ApplyL0Effect(AxisOscillation effect,double time)
        {
            if(effect==null||!Ready)return;
            ApplyL0EffectValue(effect.Value(time));
        }
        public void ApplyL0EffectValue(double position)
        {
            if(!Ready||!Numbers.Finite(position))return;
            double low=Math.Max(Minimum[0],PhysicalL0Minimum);
            double value=Numbers.Clamp(position,low,Maximum[0]);
            // Input remains the geometry read from the current frame. The
            // chapter effect replaces only the value offered to the device,
            // so the monitor can show teal input and orange output separately.
            Target[0]=Values[0]=value;
        }

        public void Update(double[] normalized, double seconds)
        {Update(normalized,seconds,false);}

        public void Update(double[] normalized, double seconds,bool relativeRotation)
        {Update(normalized,seconds,relativeRotation?DriveMappingMode.Relative:DriveMappingMode.Soft);}

        public void Update(double[] normalized, double seconds,DriveMappingMode mode)
        {
            if(normalized == null || normalized.Length != 6) throw new ArgumentException("Missing six-axis pose");
            for(int i=0;i<6;i++)
                if(!Numbers.Finite(normalized[i]) || !Numbers.Finite(Minimum[i]) || !Numbers.Finite(Maximum[i]) ||
                   Minimum[i]<0 || Maximum[i]>1 || Minimum[i]>Maximum[i] || !Numbers.Finite(Gain[i]) || Gain[i]<0)
                    throw new ArgumentException("Invalid channel " + i);
            if(!Numbers.Finite(PhysicalL0Minimum)||PhysicalL0Minimum<0||PhysicalL0Minimum>Maximum[0])throw new ArgumentException("L0 travel limits do not overlap the player length limit");
            if(!Numbers.Finite(SmoothingSeconds)||SmoothingSeconds<0||!Numbers.Finite(MaximumChangePerSecond)||MaximumChangePerSecond<0)throw new ArgumentException("Invalid output smoothing settings");
            double dt=Numbers.Finite(seconds)?Numbers.Clamp(seconds,0,.1):0;
            RelativeRotation=mode==DriveMappingMode.Relative;
            double alpha=SmoothingSeconds==0?1:1-Math.Exp(-dt/SmoothingSeconds);
            double step=MaximumChangePerSecond==0?1:MaximumChangePerSecond*dt;
            for(int i=0;i<6;i++)
            {
                double physical=Numbers.Clamp(normalized[i],0,1);
                // Physical depth increases inward; device L0 increases outward.
                Input[i]=i==0?1-physical:physical;
                double value=i==0?1-physical*Gain[i]:.5+(Input[i]-.5)*Gain[i];
                if(i>=3)
                {
                    // Preserve the unbounded inverse-kinematics request until
                    // AFTER gain. Clipping first erases over-range motion.
                    value=.5+(normalized[i]-.5)*Gain[i];
                    RotationRequests[i-3]=value;
                    if(mode==DriveMappingMode.Relative)value=CompressRotation(value,.4);
                    else if(mode==DriveMappingMode.Soft&&(i==3?SoftTwistLimit:i==4?SoftRollLimit:SoftPitchLimit))value=CompressRotation(value);
                }
                double low=i==0?Math.Max(Minimum[i],PhysicalL0Minimum):Minimum[i];
                Target[i]=Numbers.Clamp(value,low,Maximum[i]);
                // On first acquisition preview begins at the current pose. Device
                // output is separately armed by the user, after connection.
                if(!Ready) Values[i]=Target[i];
                else Values[i]=Numbers.Clamp(Values[i]+Numbers.Clamp((Target[i]-Values[i])*alpha,-step,step),low,Maximum[i]);
            }
            Ready=true;
        }

        // Keep the central 40% of each rotation's command range exact, then
        // use a monotonic C1 shoulder approaching each physical
        // endpoint. This is absolute, stateless and symmetric: seeks, pauses,
        // chapter changes and recording all get the same target at the same pose.
        // R0: +/-48° unchanged in the reference +/-120° mapping; R2: +/-10°
        // in +/-25°. R1 already uses the current travel-dependent inverse.
        // The final user travel limits and existing slew filter still apply.
        public static double CompressRotation(double position)
        {return CompressRotation(position,.2);}

        private static double CompressRotation(double position,double exactHalfRange)
        {
            if(!Numbers.Finite(position))throw new ArgumentException("Invalid rotation request");
            double offset=position-.5,amount=Math.Abs(offset);
            if(amount<=exactHalfRange)return position;
            double excess=amount-exactHalfRange,shoulder=.5-exactHalfRange;
            return .5+Math.Sign(offset)*(exactHalfRange+shoulder*(excess/(shoulder+excess)));
        }

        public static double[] PreviewAxes(double[] deviceAxes)
        {
            var result=(double[])deviceAxes.Clone();
            result[0]=1-result[0];
            return result;
        }
    }
}
