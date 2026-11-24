using System;
using System.Collections.Generic;

namespace KoiSync.Core
{
    // Out: the penis is leaving the partner (pull-out animation). Idle: no interaction, the player is
    // still moving the pose around, so a gentle idle motion keeps the device wrapped instead of idle.
    public enum NativeStrokePhase { Measured, Normal, Deep, Climax, Out, Idle }

    // One remembered stroke: the extremes captured for a phase in a previous session.
    public sealed class StrokeMemory { public string Phase="";public double Low,High; }

    // Values use the same 000–999 scale as the player's channel monitor.
    // The enhancement target is the average stroke travel of the animation, not the
    // time average of L0: one stroke is (local maximum − local minimum) of L0.
    public sealed class NativeStrokeProfile
    {
        public bool AutoRange=true, StrongAsDeep=true, ClimaxEnabled=true;
        // Legacy fixed endpoints: kept so old files still load. The loop algorithm no longer uses them.
        public int NormalMinimum=200,NormalMaximum=800,DeepMinimum=100,DeepMaximum=900;
        public int ClimaxMinimum=400,ClimaxMaximum=600;
        public double ClimaxChangesPerSecond=10;
        // Average stroke travel targets. One stroke is a local maximum minus the following local minimum.
        public int NormalTravel=400,DeepTravel=600,ClimaxTravel=600;
        // true: measure the climax motion and enhance its travel like a loop.
        // false: synthesize the configured endpoints at the configured reversal rate.
        public bool ClimaxFollowsGame=true;
        // Remembered strokes per phase, so the next session reaches the travel target immediately
        // instead of re-observing the first seconds of every animation.
        public List<StrokeMemory> Memorized=new List<StrokeMemory>();
        internal int MemoryRevision;
        // Pull-out safety: above this ceiling the sleeve can slide off the player during the pull-out.
        public bool OutLimitEnabled=true;
        public int OutCeiling=800;
        // Idle (no interaction) warm-up: a gentle round trip so the player stays hard while adjusting.
        // IdleFollowsGame keeps the frequency measured from the animation, otherwise IdlePeriod is used.
        public bool IdleMotionEnabled=true;
        public bool IdleFollowsGame=false;
        public double IdlePeriod=2;
        public int IdleMinimum=400,IdleMaximum=600;
        public bool IdleRotationEnabled=true;
        public int IdleRotationMinimum=450,IdleRotationMaximum=550;
        // Climax pull-in: keep L0 low so the sleeve stays wrapped, optionally sweeping R0 quickly.
        public bool ClimaxPullInEnabled=true;
        public int ClimaxCeiling=300;
        public bool ClimaxRotationEnabled=true;
        public double ClimaxRotationPeriod=.6;
        public int ClimaxRotationMinimum=450,ClimaxRotationMaximum=550;
        public NativeStrokeProfile Clone()
        {
            var copy=(NativeStrokeProfile)MemberwiseClone();
            copy.Memorized=new List<StrokeMemory>();
            if(Memorized!=null)foreach(var memory in Memorized)if(memory!=null)copy.Memorized.Add(new StrokeMemory{Phase=memory.Phase,Low=memory.Low,High=memory.High});
            copy.MemoryRevision=0;
            return copy;
        }
        public void Validate()
        {
            if(NormalTravel<0||NormalTravel>999||DeepTravel<0||DeepTravel>999||ClimaxTravel<0||ClimaxTravel>999)throw new ArgumentException("行程目标为 000–999");
            var lo=new[]{NormalMinimum,DeepMinimum,ClimaxMinimum};var hi=new[]{NormalMaximum,DeepMaximum,ClimaxMaximum};
            for(int i=0;i<3;i++)if(lo[i]<0||hi[i]>999||lo[i]>hi[i])throw new ArgumentException("阶段行程要求 000 ≤ 下限 ≤ 上限 ≤ 999");
            if(!Numbers.Finite(ClimaxChangesPerSecond)||ClimaxChangesPerSecond<1||ClimaxChangesPerSecond>20)throw new ArgumentException("高潮换向频率为每秒 1–20 次");
            if(OutCeiling<0||OutCeiling>999)throw new ArgumentException("拔出动 L0 上限为 000–999");
            if(ClimaxCeiling<0||ClimaxCeiling>999)throw new ArgumentException("射精 L0 上限为 000–999");
            if(!Numbers.Finite(IdlePeriod)||IdlePeriod<.2||IdlePeriod>10)throw new ArgumentException("待机往返周期为 0.2–10 秒");
            if(!Numbers.Finite(ClimaxRotationPeriod)||ClimaxRotationPeriod<.1||ClimaxRotationPeriod>10)throw new ArgumentException("射精旋转周期为 0.1–10 秒");
            var ranges=new[]{new[]{IdleMinimum,IdleMaximum},new[]{IdleRotationMinimum,IdleRotationMaximum},new[]{ClimaxRotationMinimum,ClimaxRotationMaximum}};
            foreach(var range in ranges)if(range[0]<0||range[1]>999||range[0]>range[1])throw new ArgumentException("待机 / 旋转范围要求 000 ≤ 下限 ≤ 上限 ≤ 999");
            if(Memorized==null)Memorized=new List<StrokeMemory>();
            else Memorized.RemoveAll(m=>m==null||!Numbers.Finite(m.Low)||!Numbers.Finite(m.High)||m.Low<-.001||m.High>1.001||m.High<m.Low);
        }
        public NativeStrokePhase Phase(string state)
        {
            if(string.IsNullOrEmpty(state))return NativeStrokePhase.Measured;
            if(state.StartsWith("A_",StringComparison.Ordinal))state=state.Substring(2);
            switch(state)
            {
                case "WLoop":return NativeStrokePhase.Normal;
                case "SLoop":case "MLoop":return StrongAsDeep?NativeStrokePhase.Deep:NativeStrokePhase.Normal;
                case "OLoop":return NativeStrokePhase.Deep;
                // Pull-out states: any remaining OUT_* sequence as well as the Pull action.
                case "Pull":case "OUT_Start":case "OUT_Loop":case "OUT_A":
                case "M_OUT_Start":case "M_OUT_Loop":case "M_OUT_A":
                case "WS_OUT_Start":case "WS_OUT_Loop":case "WS_OUT_A":
                case "SS_OUT_Start":case "SS_OUT_Loop":case "SS_OUT_A":return NativeStrokePhase.Out;
                // No-interaction states: idle poses and the closing A_ states after the act. Insert itself is
                // the insertion action, not an idle pose, so it keeps the raw measurement.
                case "Idle":case "InsertIdle":case "IN_A":case "WS_IN_A":case "SS_IN_A":
                case "Drink_A":case "Vomit_A":return NativeStrokePhase.Idle;
                case "IN_Start":case "IN_Loop":
                case "M_IN_Start":case "M_IN_Loop":
                case "WF_IN_Start":case "WF_IN_Loop":case "WS_IN_Start":case "WS_IN_Loop":
                case "SF_IN_Start":case "SF_IN_Loop":case "SS_IN_Start":case "SS_IN_Loop":
                case "WOrgasm":case "SOrgasm":case "Orgasm":return NativeStrokePhase.Climax;
                default:return NativeStrokePhase.Measured;
            }
        }
    }

    // Stroke travel enhancement.
    //
    // A stroke is the distance between one local maximum and the following local minimum of L0.
    // Completed strokes are kept in a short ring buffer; their average travel decides the gain.
    // The gain is anchored at the captured midpoint, so raising the travel does not push the
    // whole motion against the channel ceiling, and the result is clamped before output.
    internal sealed class NativeStrokeMapper
    {
        private sealed class Stroke { internal double Low,High,Time; }
        private const int Window=5;             // average over up to five completed strokes
        private const double WindowSeconds=1.5; // ... and never mix strokes older than this
        private const double Hysteresis=.004;   // 4 / 999: smaller wiggles are not a reversal
        private const double MinTravel=.002;    // below this the animation is static; never invent motion
        private const double MaxGain=20;        // same ceiling as the manual channel multipliers
        private readonly List<Stroke> strokes=new List<Stroke>();
        private Stroke open;
        private int direction;
        private NativeStrokePhase lastPhase=NativeStrokePhase.Measured;
        private double clock;
        private double lastStoreClock;
        private double lastStoreAverage;
        private bool climax;
        private double climaxTime;
        private double idleTime;
        private double climaxSweepTime;
        private double lastCloseClock;
        private double strokeInterval;
        internal string Status="";
        internal int Count{get{return strokes.Count;}}
        internal double AverageTravel{get{return Average();}}
        // R0/R1 sweep requested by the idle or climax logic; the pipeline applies it before limits.
        internal bool HasRotation;
        internal double RotationR0,RotationR1;
        // Highest L0 this phase may send. The pipeline clamps with it, so every return path is covered.
        internal double Ceiling=1;
        // Multiplier applied before the ceiling: the climax path remaps the whole animation into 000-300.
        internal double Scale=1;
        internal void Reset(){strokes.Clear();open=null;direction=0;lastPhase=NativeStrokePhase.Measured;clock=0;lastStoreClock=0;lastStoreAverage=0;climax=false;climaxTime=0;idleTime=0;climaxSweepTime=0;lastCloseClock=0;strokeInterval=0;HasRotation=false;Ceiling=1;Scale=1;Status="";}
        // Smooth round trip between two endpoints; period <= 0 keeps the lower endpoint.
        private static double Sweep(double time,double period,double low,double high)
        {
            if(!(period>.01)||high<=low)return low/999.0;
            double phase=time/period-Math.Floor(time/period);
            double shape=.5-.5*Math.Cos(phase*2*Math.PI);
            return (low+(high-low)*shape)/999.0;
        }
        internal double Map(NativeMotionSample sample,double geometric,double fallback,double dt,bool live)
        {
            var p=sample.Stroke;
            if(p==null||sample.Studio){Status="";climax=false;HasRotation=false;Ceiling=1;Scale=1;return fallback;}
            var phase=p.Phase(sample.AnimationState);
            if(live&&Numbers.Finite(dt)&&dt>0)clock+=dt;
            // A phase change is a different motion regime: strokes from the previous one must not veto it.
            if(phase!=lastPhase){lastPhase=phase;Seed(p);}
            if(strokes.Count==0&&open==null)Seed(p);
            HasRotation=false;
            Scale=1;
            // A fully custom climax uses the player's own endpoints, so only the measured path is capped.
            bool customClimax=phase==NativeStrokePhase.Climax&&p.ClimaxEnabled&&!p.ClimaxFollowsGame;
            // Pull-out safety: keep L0 below the configured ceiling so the sleeve cannot slide off.
            Ceiling=phase==NativeStrokePhase.Out&&p.OutLimitEnabled?p.OutCeiling/999.0:
                    phase==NativeStrokePhase.Climax&&p.ClimaxPullInEnabled&&!customClimax?p.ClimaxCeiling/999.0:1;
            // The climax remaps the whole animation into 000-<ceiling> instead of just clipping its top:
            // the sleeve stays wrapped and the motion keeps its shape.
            if(phase==NativeStrokePhase.Climax&&p.ClimaxPullInEnabled&&!customClimax)Scale=Ceiling;
            // Climax pull-in keeps L0 low; a quick R0/R1 sweep replaces the lost stimulation.
            if(phase==NativeStrokePhase.Climax&&p.ClimaxPullInEnabled&&p.ClimaxRotationEnabled)
            {
                if(live&&Numbers.Finite(dt)&&dt>0)climaxSweepTime+=dt;
                HasRotation=true;
                RotationR0=Sweep(climaxSweepTime,p.ClimaxRotationPeriod,p.ClimaxRotationMinimum,p.ClimaxRotationMaximum);
                RotationR1=Sweep(climaxSweepTime,p.ClimaxRotationPeriod,p.ClimaxRotationMinimum,p.ClimaxRotationMaximum);
            }
            // Idle: keep a gentle round trip so the player is not left limp while the pose is adjusted.
            if(phase==NativeStrokePhase.Idle&&p.IdleMotionEnabled)
            {
                if(live)idleTime+=dt;
                double period=p.IdleFollowsGame&&strokeInterval>.2?strokeInterval:p.IdlePeriod;
                double level=Sweep(idleTime,period,p.IdleMinimum,p.IdleMaximum);
                if(p.IdleRotationEnabled)
                {
                    HasRotation=true;
                    RotationR0=Sweep(idleTime,period,p.IdleRotationMinimum,p.IdleRotationMaximum);
                    RotationR1=Sweep(idleTime,period,p.IdleRotationMinimum,p.IdleRotationMaximum);
                }
                Status="无交互待机 · L0 "+p.IdleMinimum+"–"+p.IdleMaximum+" · "+period.ToString("0.##")+"s"+(HasRotation?" · R0/R1 "+p.IdleRotationMinimum+"–"+p.IdleRotationMaximum:"");
                return level;
            }
            if(phase==NativeStrokePhase.Climax&&p.ClimaxEnabled&&!p.ClimaxFollowsGame)
            {
                // Start/Loop changes are one continuous event; paused samples cannot advance its clock.
                if(!climax){climax=true;climaxTime=0;}else if(live)climaxTime+=dt;
                Status="高潮往返 · "+p.ClimaxMinimum+"–"+p.ClimaxMaximum;
                return ((long)Math.Floor(climaxTime*p.ClimaxChangesPerSecond+1e-8)%2==0?p.ClimaxMinimum:p.ClimaxMaximum)/999.0;
            }
            climax=false;climaxTime=0;
            if(!p.AutoRange||phase==NativeStrokePhase.Measured||phase==NativeStrokePhase.Idle||phase==NativeStrokePhase.Climax&&!p.ClimaxEnabled)
            {Status=phase==NativeStrokePhase.Climax?"高潮 · 保持原始测量":phase==NativeStrokePhase.Idle?"待机 · 保持原始测量":"原始测量";return fallback;}
            // The travel estimator and its target always use the raw geometric L0. The legacy fixed-range
            // mapping and its stage multiplier stay on the fallback path, which enhancement replaces.
            double original=Numbers.Clamp(geometric,0,1);
            if(live&&Numbers.Finite(original))Observe(original);
            if(strokes.Count>=3&&(clock-lastStoreClock>=2||Math.Abs(Average()-lastStoreAverage)>.002))
            {lastStoreAverage=Average();lastStoreClock=clock;Store(p);}
            if(strokes.Count<3)
            {
                Status="观察行程 · 已记录 "+strokes.Count+" / "+Window+" 次";
                return original;
            }
            int target=phase==NativeStrokePhase.Deep?p.DeepTravel:phase==NativeStrokePhase.Climax?p.ClimaxTravel:p.NormalTravel;
            double average=Average();
            if(average<MinTravel){Status="行程过小 "+Math.Round(average*999)+" / 目标 "+target+" · 保持原值";return original;}
            double wanted=Numbers.Clamp(target/999.0,0,1);
            double gain=Solve(wanted);
            Status="平均行程 "+Math.Round(average*999)+" / 目标 "+target+" · 补强 ×"+gain.ToString("0.##");
            return Numbers.Clamp(Apply(original,gain),0,1);
        }
        // Load the strokes remembered for this phase so the travel target applies from the first frame.
        private void Seed(NativeStrokeProfile p)
        {
            strokes.Clear();open=null;direction=0;
            if(p.Memorized==null)return;
            string phase=lastPhase.ToString();
            for(int i=0;i<p.Memorized.Count;i++)
            {
                var memory=p.Memorized[i];
                if(memory==null||memory.Phase!=phase)continue;
                strokes.Add(new Stroke{Low=Numbers.Clamp(memory.Low,0,1),High=Numbers.Clamp(memory.High,0,1),Time=0});
            }
            while(strokes.Count>Window)strokes.RemoveAt(0);
        }
        // Write the captured strokes back into the profile so they persist with the animation file.
        private void Store(NativeStrokeProfile p)
        {
            if(p.Memorized==null)p.Memorized=new List<StrokeMemory>();
            string phase=lastPhase.ToString();
            p.Memorized.RemoveAll(m=>m==null||m.Phase==phase);
            for(int i=0;i<strokes.Count;i++)p.Memorized.Add(new StrokeMemory{Phase=phase,Low=strokes[i].Low,High=strokes[i].High});
            p.MemoryRevision++;
        }
        // One stroke starts at an extreme and closes when the motion reverses past the hysteresis.
        private void Observe(double value)        {
            if(open==null){open=new Stroke{Low=value,High=value,Time=clock};direction=0;return;}
            if(value>open.High){open.High=value;direction=1;}
            if(value<open.Low){open.Low=value;direction=-1;}
            if(direction>0&&value<open.High-Hysteresis||direction<0&&value>open.Low+Hysteresis)
            {
                if(lastCloseClock>0)
                {
                    double interval=clock-lastCloseClock;
                    if(interval>.2&&interval<5)strokeInterval=interval;
                }
                lastCloseClock=clock;
                strokes.Add(open);
                while(strokes.Count>Window)strokes.RemoveAt(0);
                // Three strokes are the minimum the solver needs; the age cap only drops older ones.
                while(strokes.Count>3&&clock-strokes[0].Time>WindowSeconds)strokes.RemoveAt(0);
                open=new Stroke{Low=value,High=value,Time=clock};direction=0;
            }
        }
        private double Average()
        {
            double sum=0;int count=0;
            for(int i=0;i<strokes.Count;i++)
            {
                double travel=strokes[i].High-strokes[i].Low;if(travel<=0)continue;
                sum+=travel;count++;
            }
            return count==0?0:sum/count;
        }
        private double Middle()
        {
            if(strokes.Count==0)return .5;
            double low=double.MaxValue,high=double.MinValue;
            for(int i=0;i<strokes.Count;i++){low=Math.Min(low,strokes[i].Low);high=Math.Max(high,strokes[i].High);}
            return (low+high)*.5;
        }
        // Scale the captured strokes around their own midpoint, then slide the result back inside
        // the channel: a low-travel motion becomes a full usable stroke instead of a flat line.
        private void Transform(double gain,out double middle,out double shift)
        {
            if(strokes.Count==0){middle=.5;shift=0;return;}
            double low=double.MaxValue,high=double.MinValue;
            for(int i=0;i<strokes.Count;i++){low=Math.Min(low,strokes[i].Low);high=Math.Max(high,strokes[i].High);}
            middle=(low+high)*.5;
            double scaledLow=middle+(low-middle)*gain,scaledHigh=middle+(high-middle)*gain;
            shift=0;
            if(scaledLow<0)shift=-scaledLow;
            if(scaledHigh+shift>1)shift-=scaledHigh+shift-1;
            if(scaledLow+shift<0)shift=-scaledLow;
        }
        // Travel the buffer would reach at this gain, after the channel clamp is applied.
        private double TravelAfter(double gain)
        {
            double middle,shift;Transform(gain,out middle,out shift);
            double sum=0;int count=0;
            for(int i=0;i<strokes.Count;i++)
            {
                if(strokes[i].High-strokes[i].Low<=0)continue;
                double low=Numbers.Clamp(middle+(strokes[i].Low-middle)*gain+shift,0,1);
                double high=Numbers.Clamp(middle+(strokes[i].High-middle)*gain+shift,0,1);
                sum+=high-low;count++;
            }
            return count==0?0:sum/count;
        }
        private double Solve(double wanted)
        {
            if(TravelAfter(1)>=wanted)return 1;
            double lower=1,upper=1;
            while(upper<MaxGain&&TravelAfter(upper)<wanted)upper=Math.Min(MaxGain,upper*2);
            for(int i=0;i<24;i++)
            {
                double test=(lower+upper)*.5;
                if(TravelAfter(test)<wanted)lower=test;else upper=test;
            }
            return upper;
        }
        private double Apply(double value,double gain)
        {
            if(gain<=1)return value;
            double middle,shift;Transform(gain,out middle,out shift);
            return middle+(value-middle)*gain+shift;
        }
    }
}
