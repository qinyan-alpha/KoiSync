using System;
using System.Collections.Generic;
using System.Globalization;

namespace KoiSync.Wire
{
    public sealed class Pose { public float X,Y,Z,Qx,Qy,Qz,Qw=1; }
    public sealed class Candidate
    {
        public string Id="",Label="",ReferenceBone="",TargetBone="",MotionKey="";
        public bool Active;
        public bool ScriptMotion;
        public double[] ScriptPositions;
        public KoiSync.Core.NativeMotionSample Native;
        public KoiSync.Core.MotionVector[] ReferencePath;
        public KoiSync.Core.MotionVector ColliderA,ColliderB;
        public double ColliderRadius;
        public Pose Reference=new Pose(),Target=new Pose();
    }
    public sealed class Snapshot
    {
        public string Session="",Context="",Scene="",State="";
        public long Sequence;
        public double SampleTime;
        public bool Active;
        public List<Candidate> Candidates=new List<Candidate>();
    }
}
namespace KoiSync.Core
{
    using KoiSync.Wire;
    public enum StrokeAxis { Z,Y,X }
    public enum DeviceKind { SR6,OSR2,SSR1,Handy }
    public static class Numbers
    {
        public static double Clamp(double v,double a,double b) { return Math.Max(a,Math.Min(b,v)); }
        public static bool Finite(double v) { return !double.IsNaN(v)&&!double.IsInfinity(v); }
    }
    public static class RelativeMotion
    {
        public static double[] Solve(Candidate c,StrokeAxis axis)
        {
            Pose a=c.Reference,b=c.Target;
            double n=a.Qx*a.Qx+a.Qy*a.Qy+a.Qz*a.Qz+a.Qw*a.Qw;
            if(n<0.5||n>1.5) throw new ArgumentException("Invalid reference rotation");
            double scale=1/Math.Sqrt(n),x=-a.Qx*scale,y=-a.Qy*scale,z=-a.Qz*scale,w=a.Qw*scale;
            double dx=b.X-a.X,dy=b.Y-a.Y,dz=b.Z-a.Z;
            double tx=2*(y*dz-z*dy),ty=2*(z*dx-x*dz),tz=2*(x*dy-y*dx);
            double px=dx+w*tx+y*tz-z*ty,py=dy+w*ty+z*tx-x*tz,pz=dz+w*tz+x*ty-y*tx;
            double qx=w*b.Qx+x*b.Qw+y*b.Qz-z*b.Qy,qy=w*b.Qy-x*b.Qz+y*b.Qw+z*b.Qx;
            double qz=w*b.Qz+x*b.Qy-y*b.Qx+z*b.Qw,qw=w*b.Qw-x*b.Qx-y*b.Qy-z*b.Qz;
            double qn=Math.Sqrt(qx*qx+qy*qy+qz*qz+qw*qw);
            if(qn<0.7||qn>1.3) throw new ArgumentException("Invalid target rotation");
            qx/=qn; qy/=qn; qz/=qn; qw/=qn;
            double rx=Math.Atan2(2*(qw*qx+qy*qz),1-2*(qx*qx+qy*qy))*180/Math.PI;
            double ry=Math.Asin(Numbers.Clamp(2*(qw*qy-qz*qx),-1,1))*180/Math.PI;
            double rz=Math.Atan2(2*(qw*qz+qx*qy),1-2*(qy*qy+qz*qz))*180/Math.PI;
            double[] result=axis==StrokeAxis.X ? new[]{px,py,pz,rx,ry,rz} : axis==StrokeAxis.Y ? new[]{py,pz,px,ry,rz,rx} : new[]{pz,py,px,rz,ry,rx};
            foreach(double v in result) if(!Numbers.Finite(v)) throw new ArgumentException("Non-finite pose");
            return result;
        }
    }
    public sealed class MotionPipeline
    {
        public const double FreshnessSeconds=0.25;
        public static readonly string[] Axes={"L0","L1","L2","R0","R1","R2"};
        private readonly double[] low=new double[6],high=new double[6],lastRaw=new double[6];
        private string identity="";
        private long sequence=-1;
        private double previousTime=-1;
        private double[] filtered;
        private bool unwrapReady;
        private int samples;
        private double nativeMin=1,nativeMax=0;
        private double[] nativeInput;
        private string rotationIdentity="";
        private object rotationContext;
        private ContinuousAngle studioR0=new ContinuousAngle(180);
        private readonly NativeStrokeMapper nativeStroke=new NativeStrokeMapper();
        private readonly ShaftContactTracker shaftContact=new ShaftContactTracker();
        public string NativeStrokeStatus{get{return nativeStroke.Status;}}
        public bool NativeMode {get;private set;}
        public bool ScriptMode {get;private set;}
        public bool ShapePreview {get;private set;}
        public string ReferenceContext {get;private set;}="本体";
        public double NativeLength {get;private set;}
        public double NativeGain {get;private set;}
        public double NativeMidpoint {get{return (nativeMin+nativeMax)*.5;}}
        public bool TryGetNativeRange(out double minimum,out double maximum)
        {minimum=nativeMin;maximum=nativeMax;return NativeMode&&Calibrated&&!ShapePreview&&nativeMax>nativeMin;}
        public bool TryGetNativeInput(int axis,out double value)
        {
            value=double.NaN;
            if(!NativeMode||nativeInput==null||axis<0||axis>=6)return false;
            value=nativeInput[axis];return Numbers.Finite(value);
        }
        public bool Calibrating {get;private set;}
        public bool Calibrated {get;private set;}
        public bool Armed {get;private set;}
        public bool HasLiveSample {get;private set;}
        public readonly bool[] AxisCalibrated=new bool[6];
        public readonly bool[] EnabledAxes={true,false,false,false,false,false};
        public bool InvertStroke;
        public double Minimum=0.1,Maximum=0.9,MaxUnitsPerSecond=2;
        // Global per-axis limits and gains. They govern every animation; an animation that carries its own
        // channel values keeps them, so a per-animation setting always wins over the global one.
        public readonly double[] AxisMinimum={0,0,0,0,0,0},AxisMaximum={1,1,1,1,1,1},AxisGain={1,1,1,1,1,1};
        public double[] Raw=new double[6],Values={0.5,0.5,0.5,0.5,0.5,0.5};
        public double[] Desired=new double[6];
        public bool HasDesired {get;private set;}
        public string Status="等待场景数据";
        public bool TryGetCalibrationRange(int axis,out double minimum,out double maximum)
        {
            minimum=maximum=double.NaN;
            if(NativeMode||axis<0||axis>=6||!Calibrated||!AxisCalibrated[axis])return false;
            minimum=low[axis];maximum=high[axis];return true;
        }
        public bool TryGetMappedValue(int axis,out double value)
        {
            value=double.NaN;
            if(axis<0||axis>=6||!Calibrated||Calibrating||!HasLiveSample||filtered==null||!AxisCalibrated[axis])return false;
            value=Values[axis];return Numbers.Finite(value);
        }
        public void Disarm(string reason) {Armed=false;Status=reason;}
        internal void ContinueFrom(double[] positions)
        {
            if(positions==null)return;
            filtered=(double[])positions.Clone();double lo=Numbers.Clamp(Minimum,0,1),hi=Numbers.Clamp(Maximum,lo,1);
            for(int i=0;i<filtered.Length;i++)filtered[i]=Numbers.Clamp(filtered[i],lo,hi);
            Values=(double[])filtered.Clone();
        }
        public bool Arm()
        {
            Armed=Calibrated&&HasLiveSample&&!Calibrating&&!ShapePreview;
            Status=ShapePreview?"形态键绝对值实验 · 仅预览":Armed?(ScriptMode?"funscript 同步运行中":"同步运行中"):"请先校准当前运动源";
            return Armed;
        }
        public void Reset(string reason)
        {
            Disarm(reason);Calibrated=false;Calibrating=false;HasLiveSample=false;HasDesired=false;
            Array.Clear(AxisCalibrated,0,6);filtered=null;unwrapReady=false;sequence=-1;previousTime=-1;
            nativeMin=1;nativeMax=0;nativeInput=null;NativeMode=false;ScriptMode=false;ShapePreview=false;NativeLength=0;NativeGain=0;samples=0;
            nativeStroke.Reset();shaftContact.Reset();
        }
        public void StartCalibration()
        {
            if(NativeMode){Reset(ReferenceContext+"自动范围已重置 · 等待有效运动");return;}
            Disarm("正在采集范围 · 请播放完整动作循环");Calibrated=false;Calibrating=true;samples=0;
            for(int i=0;i<6;i++){low[i]=double.PositiveInfinity;high[i]=double.NegativeInfinity;AxisCalibrated[i]=false;}
        }
        public bool FinishCalibration()
        {
            Calibrating=false;
            for(int i=0;i<6;i++) AxisCalibrated[i]=samples>=15&&high[i]-low[i]>=(i<3?0.002:1.0);
            Calibrated=AxisCalibrated[0];filtered=null;
            Status=Calibrated?"校准完成 · 可启用同步":"主轴运动不足 · 检查目标点或投影轴";
            return Calibrated;
        }
        public bool Update(Snapshot f,Candidate source,double age,StrokeAxis axis)
        {
            // Main-game phase names are telemetry. The adapter's MotionKey identifies the actual animation and rig.
            string stateKey=source!=null&&source.ScriptMotion?"script":source!=null&&source.Native!=null&&!source.Native.Studio?"native":f.State;
            string key=f.Session+"|"+f.Scene+"|"+stateKey+"|"+(source==null?"":source.Id+":"+source.MotionKey+":"+(source.Native!=null&&source.Native.Studio&&source.Native.ShapePreview))+"|"+axis;
            if(identity!=key){Reset("运动源或动作变化 · 请校准");identity=key;}
            HasLiveSample=source!=null&&f.Active&&source.Active&&age>=0&&age<=FreshnessSeconds;
            if(!HasLiveSample)
            {
                Disarm(age>FreshnessSeconds?"数据超时 · 已停止输出":"播放已暂停 · 可查看当前姿态");
                if(Calibrating){Calibrating=false;Calibrated=false;}
            }
            if(source==null)
            {
                Disarm(age>FreshnessSeconds?"数据超时 · 已停止输出":"场景暂停或运动源不可用");
                if(Calibrating){Calibrating=false;Calibrated=false;}return false;
            }
            if(f.Sequence<=sequence)return Armed;
            if(previousTime>=0&&f.SampleTime<=previousTime){Reset("采样时钟异常");return false;}
            double dt=previousTime<0?1.0/60:Numbers.Clamp(f.SampleTime-previousTime,0.001,FreshnessSeconds);
            sequence=f.Sequence;previousTime=f.SampleTime;
            if(source.ScriptMotion)return UpdateScript(source.ScriptPositions,dt);
            if(source.Native!=null)
            {
                if(source.Native.Studio||source.Native.RootDistance)
                {
                    // Calibration/preset changes do not represent a new physical revolution.
                    string rotationKey=f.Session+"|"+f.Scene+"|"+source.Id+"|"+source.Native.RotationBasis;
                    if(rotationIdentity!=rotationKey||!ReferenceEquals(rotationContext,source.Native.RotationContext)){rotationIdentity=rotationKey;rotationContext=source.Native.RotationContext;studioR0=new ContinuousAngle(180);}
                    source.Native.R0Continuity=studioR0;
                }
                return UpdateNative(source.Native,dt);
            }
            double[] raw;
            try {raw=RelativeMotion.Solve(source,axis);}catch(ArgumentException){Reset("骨骼数据无效");return false;}
            for(int i=3;i<6;i++)if(unwrapReady)raw[i]=lastRaw[i]+Math.IEEERemainder(raw[i]-lastRaw[i],360);
            Array.Copy(raw,lastRaw,6);unwrapReady=true;Raw=raw;
            // Inspection remains live while playback/output is paused.
            if(!HasLiveSample)
            {
                Disarm(age>FreshnessSeconds?"数据超时 · 已停止输出":"播放已暂停 · 可查看当前姿态");
                if(Calibrating){Calibrating=false;Calibrated=false;}return false;
            }
            if(Calibrating)
            {
                samples++;for(int i=0;i<6;i++){low[i]=Math.Min(low[i],raw[i]);high[i]=Math.Max(high[i],raw[i]);}return false;
            }
            if(!Calibrated)return false;
            double min=Numbers.Clamp(Minimum,0,1),max=Numbers.Clamp(Maximum,min,1);
            double[] target=new double[6];
            for(int i=0;i<6;i++)
            {
                double value=AxisCalibrated[i]?Numbers.Clamp((raw[i]-low[i])/(high[i]-low[i]),0,1):0.5;
                if(i==0&&InvertStroke)value=1-value;
                target[i]=min+value*(max-min);
            }
            if(filtered==null)filtered=target;
            else for(int i=0;i<6;i++)
            {
                double alpha=1-Math.Exp(-dt/0.035),step=MaxUnitsPerSecond*dt;
                filtered[i]=Numbers.Clamp(filtered[i]+Numbers.Clamp((target[i]-filtered[i])*alpha,-step,step),min,max);
            }
            Values=(double[])filtered.Clone();return Armed;
        }
        private bool UpdateScript(double[] positions,double dt)
        {
            ScriptMode=NativeMode=true;ShapePreview=false;ReferenceContext="funscript";NativeLength=0;NativeGain=1;
            Array.Clear(AxisCalibrated,0,6);
            Calibrated=false;Calibrating=false;
            if(positions==null||positions.Length!=6){Reset("六轴脚本数据无效");return false;}
            nativeInput=(double[])positions.Clone();Raw=new double[6];Desired=new double[6];
            double globalLo=Numbers.Clamp(Minimum,0,1),globalHi=Numbers.Clamp(Maximum,globalLo,1);
            double[] target=new double[6];
            for(int i=0;i<6;i++)
            {
                double position=positions[i];Raw[i]=position*100;
                bool valid=Numbers.Finite(position)&&position>=0&&position<=1;
                AxisCalibrated[i]=valid;Calibrated|=valid;
                if(!valid){Desired[i]=double.NaN;target[i]=filtered==null?.5:filtered[i];continue;}
                if(!Numbers.Finite(AxisMinimum[i])||!Numbers.Finite(AxisMaximum[i])||AxisMinimum[i]>AxisMaximum[i]||!Numbers.Finite(AxisGain[i])||AxisGain[i]<0||AxisGain[i]>20)
                {Reset("脚本通道配置无效");return false;}
                double lo=Numbers.Clamp(AxisMinimum[i],globalLo,globalHi),hi=Numbers.Clamp(AxisMaximum[i],lo,globalHi);
                double value=(i==0&&InvertStroke?1-position:position)*AxisGain[i];
                Desired[i]=target[i]=Numbers.Clamp(value,lo,hi);
                // Script timestamps already define the motion speed. Applying the scene-sampling
                // slew limit here attenuates fast strokes instead of following the authored curve.
            }
            HasDesired=Calibrated;
            if(!Calibrated){HasLiveSample=false;Disarm("脚本当前时间无动作 · 等待播放");return false;}
            if(!HasLiveSample)return false;
            filtered=target;Values=(double[])target.Clone();return Armed;
        }
        private bool UpdateNative(NativeMotionSample sample,double dt)
        {
            ScriptMode=false;
            NativeMotionResult result;
            ReferenceContext=sample.Studio?"工作室":"本体";
            if(sample.HasShaftPose){sample.ShaftContact=shaftContact;sample.ContactSequence=sequence;sample.ContactDeltaTime=HasLiveSample?dt:0;}
            try{result=NativeMotion.Solve(sample);}catch(ArgumentException){Reset(ReferenceContext+"参考坐标无效");return false;}
            if(sample.HasShaftPose&&HasLiveSample)shaftContact.Commit(sample,result);
            if(sample.HasShaftPose&&sample.ShaftYaw!=null)
            {if(result.Engagement<=0)sample.ShaftYaw.Reset();else sample.ShaftYaw.Resolve(result.Raw[3]);}
            NativeMode=true;NativeLength=result.Length;NativeGain=sample.Gain;Raw=result.Raw;nativeInput=result.Normalized;
            ShapePreview=sample.Studio&&sample.ShapePreview;
            if(ShapePreview)
            {
                Disarm("形态键绝对值实验 · 仅预览");nativeInput[0]=sample.ShapeL0;
                if(!Numbers.Finite(sample.ShapeL0)){Calibrated=false;Array.Clear(AxisCalibrated,0,6);filtered=null;Status="形态键或端点无效 · 实验预览暂停";return false;}
                Calibrated=true;for(int i=0;i<6;i++)AxisCalibrated[i]=true;
            }
            if(!HasLiveSample&&!sample.RootDistance)return false;
            if(!Numbers.Finite(sample.Gain)||sample.Gain<.25||sample.Gain>5){Reset(ReferenceContext+"倍率无效");return false;}
            double stroke=nativeInput[0];
            if(!ShapePreview&&sample.RootDistance)
            {Calibrated=true;for(int i=0;i<6;i++)AxisCalibrated[i]=true;}
            if(!ShapePreview&&sample.FixedRange)
            {
                if(!Numbers.Finite(sample.RangeStart)||!Numbers.Finite(sample.RangeEnd)||sample.RangeEnd<=sample.RangeStart){Reset("已保存插入范围无效");return false;}
                Calibrated=true;nativeMin=sample.RangeStart;nativeMax=sample.RangeEnd;for(int i=0;i<6;i++)AxisCalibrated[i]=true;
            }
            else if(!ShapePreview&&stroke>=0&&stroke<=1){nativeMin=Math.Min(nativeMin,stroke);nativeMax=Math.Max(nativeMax,stroke);samples++;}
            if(!Calibrated&&samples>=15&&(nativeMax-nativeMin)*result.Length>=.002)
            {
                Calibrated=true;for(int i=0;i<6;i++)AxisCalibrated[i]=true;
                Status=ReferenceContext+"自动范围就绪 · 可启用同步";
            }
            if(!Calibrated){Status=ReferenceContext+"自动采集参考范围 · 等待有效运动";return false;}
            double l0=ShapePreview?Numbers.Clamp(stroke,0,1):sample.FixedRange?.5+((stroke-sample.RangeStart)/(sample.RangeEnd-sample.RangeStart)-.5)*sample.Gain:.5+(stroke-(sample.RootDistance?.5:NativeMidpoint))*sample.Gain;
            l0=nativeStroke.Map(sample,stroke,l0,dt,HasLiveSample);
            // Pull-out / climax ceilings come from the phase logic; the clamp covers every return path.
            l0=Numbers.Clamp(Math.Min(l0*nativeStroke.Scale,nativeStroke.Ceiling),0,1);
            double[] target=(double[])nativeInput.Clone();target[0]=InvertStroke?1-l0:l0;
            // Idle and climax sweeps drive the rotation channels directly, before multipliers and limits.
            // An axis the player switched off stays untouched: the six channel switches also govern them.
            if(nativeStroke.HasRotation)
            {
                if(EnabledAxes[3])target[3]=nativeStroke.RotationR0;
                if(EnabledAxes[4])target[4]=nativeStroke.RotationR1;
            }
            for(int i=0;i<6;i++)
            {
                if(!Numbers.Finite(sample.Minimum[i])||!Numbers.Finite(sample.Maximum[i])||sample.Minimum[i]>sample.Maximum[i]){Reset("本体通道限幅无效");return false;}
                double globalLo=Numbers.Clamp(Minimum,0,1),globalHi=Numbers.Clamp(Maximum,globalLo,1);
                // Untouched channels follow the global axis configuration; an animation that saved its own
                // limits or gain keeps them unchanged.
                bool own=!(sample.Minimum[i]<=0&&sample.Maximum[i]>=1&&Math.Abs(sample.Multipliers[i]-1)<1e-9);
                double axisLo=own?sample.Minimum[i]:AxisMinimum[i],axisHi=own?sample.Maximum[i]:AxisMaximum[i],gain=own?sample.Multipliers[i]:AxisGain[i];
                double lo=Numbers.Clamp(axisLo,globalLo,globalHi),hi=Numbers.Clamp(axisHi,lo,globalHi);
                if(!Numbers.Finite(lo)||!Numbers.Finite(hi)){Reset("通道限幅无效");return false;}
                if(!Numbers.Finite(gain)||gain<0||gain>20){Reset("通道倍率无效");return false;}
                target[i]=Numbers.Clamp(sample.HasShaftPose&&i>0?.5+(target[i]-.5)*gain:target[i]*gain,lo,hi);Desired[i]=target[i];
                if(filtered!=null){bool pulse=i==0&&!sample.Studio&&sample.Stroke!=null&&sample.Stroke.ClimaxEnabled&&sample.Stroke.Phase(sample.AnimationState)==NativeStrokePhase.Climax;double alpha=pulse?1:1-Math.Exp(-dt/.035),step=MaxUnitsPerSecond*dt;target[i]=Numbers.Clamp(filtered[i]+Numbers.Clamp((target[i]-filtered[i])*alpha,-step,step),lo,hi);}
            }
            HasDesired=true;if(!HasLiveSample)return false;
            filtered=target;Values=(double[])target.Clone();return Armed;
        }
    }
    public static class TCode
    {
        public static int[] SupportedAxes(DeviceKind kind)
        {
            if(kind==DeviceKind.SR6)return new[]{0,1,2,3,4,5};
            if(kind==DeviceKind.OSR2)return new[]{0,4,5};
            if(kind==DeviceKind.SSR1)return new[]{0};
            return new int[0];
        }
        public static string Encode(double[] values,bool[] enabled,DeviceKind kind,int duration)
        {
            if(kind==DeviceKind.Handy)throw new NotSupportedException("Handy requires Intiface");
            List<string> parts=new List<string>();
            foreach(int i in SupportedAxes(kind))
            {
                if(!enabled[i])continue;
                if(!Numbers.Finite(values[i]))throw new ArgumentException("Invalid axis");
                int position=(int)Math.Round(Numbers.Clamp(values[i],0,0.9999)*10000);
                parts.Add(MotionPipeline.Axes[i]+position.ToString("D4",CultureInfo.InvariantCulture)+"I"+((int)Numbers.Clamp(duration,5,250)).ToString(CultureInfo.InvariantCulture));
            }
            return parts.Count==0?"":string.Join(" ",parts.ToArray())+"\n";
        }
    }
}
