using System;
using System.Collections.Generic;
using KoiSync.Wire;

namespace KoiSync.Core
{
    public sealed class MeasurementProfile
    {
        public NativeTarget Target=NativeTarget.AUTO;
        public double LengthScale=1,Gain=1,RangeStart=0,RangeEnd=1;
        public bool FixedRange,ReverseCollider;
        public MotionVector PlaneOffset,TargetOffset,RootOffset;
        public int AlignmentVersion;
        public MotionVector PlaneRotation,EndOffset;
        public MotionVector TargetPlaneOffset,TargetPlaneRotation;
        public double PlaneWidth,PlaneDepth;
        public double LateralSpan=2;
        // 0 = saved before the lateral span default moved from 3 to 2; migrated once on load.
        public int LayoutVersion;
        public int HiddenObjects;
        public NativeStrokeProfile NativeStroke=new NativeStrokeProfile();
        public double[] ChannelMinimum={0,0,0,0,0,0},ChannelMaximum={1,1,1,1,1,1},Multipliers={1,1,1,1,1,1};
        public MeasurementProfile Clone(){var c=(MeasurementProfile)MemberwiseClone();c.ChannelMinimum=(double[])ChannelMinimum.Clone();c.ChannelMaximum=(double[])ChannelMaximum.Clone();c.Multipliers=(double[])Multipliers.Clone();c.NativeStroke=NativeStroke.Clone();return c;}
        internal string MotionKey
        {
            get
            {
                string key=Target+":"+FixedRange+":"+ReverseCollider+":"+AlignmentVersion;
                foreach(double n in new[]{LengthScale,Gain,RangeStart,RangeEnd,PlaneOffset.X,PlaneOffset.Y,PlaneOffset.Z,TargetOffset.X,TargetOffset.Y,TargetOffset.Z,RootOffset.X,RootOffset.Y,RootOffset.Z,EndOffset.X,EndOffset.Y,EndOffset.Z,PlaneRotation.X,PlaneRotation.Y,PlaneRotation.Z,TargetPlaneOffset.X,TargetPlaneOffset.Y,TargetPlaneOffset.Z,TargetPlaneRotation.X,TargetPlaneRotation.Y,TargetPlaneRotation.Z})key+=":"+n.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                key+=":"+PlaneWidth.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+":"+PlaneDepth.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                key+=":"+LateralSpan.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                key+=":"+NativeStroke.AutoRange+":"+NativeStroke.NormalTravel+":"+NativeStroke.DeepTravel+":"+NativeStroke.ClimaxTravel+":"+NativeStroke.ClimaxFollowsGame+":"+NativeStroke.ClimaxEnabled+":"+NativeStroke.ClimaxMinimum+":"+NativeStroke.ClimaxMaximum+":"+NativeStroke.ClimaxChangesPerSecond.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                foreach(var array in new[]{ChannelMinimum,ChannelMaximum,Multipliers})foreach(double n in array)key+=":"+n.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                return key;
            }
        }
        public void Validate()
        {
            // One-time migration: the lateral span default moved from 3 to 2, and every older file
            // carries the old default explicitly, so a plain default change would never apply.
            if(LayoutVersion<1){if(Math.Abs(LateralSpan-3)<1e-9)LateralSpan=2;LayoutVersion=1;}
            if(!Numbers.Finite(LateralSpan)||LateralSpan<1||LateralSpan>20)throw new ArgumentException("L1/L2 映射跨度为 1–20 倍");
            if(NativeStroke==null)throw new ArgumentException("本体阶段行程配置缺失");NativeStroke.Validate();
            if(!Numbers.Finite(PlaneWidth)||!Numbers.Finite(PlaneDepth)||PlaneWidth<0||PlaneDepth<0||PlaneWidth>10||PlaneDepth>10||PlaneWidth>0&&PlaneWidth<.001||PlaneDepth>0&&PlaneDepth<.001)throw new ArgumentException("平面尺寸使用 0（自动）或 0.001–10 世界单位");
            if(HiddenObjects<0||HiddenObjects>31)throw new ArgumentException("隐藏对象配置无效");
            foreach(var values in new[]{ChannelMinimum,ChannelMaximum,Multipliers})if(values==null||values.Length!=6)throw new ArgumentException("需要六轴配置");
            for(int i=0;i<6;i++)if(!Numbers.Finite(ChannelMinimum[i])||!Numbers.Finite(ChannelMaximum[i])||ChannelMinimum[i]<0||ChannelMaximum[i]>1||ChannelMinimum[i]>ChannelMaximum[i]||!Numbers.Finite(Multipliers[i])||Multipliers[i]<0||Multipliers[i]>20)throw new ArgumentException("通道上下限为 000–999，倍率为 0–20");
            if(!Numbers.Finite(LengthScale)||LengthScale<.1||LengthScale>3||!Numbers.Finite(Gain)||Gain<.25||Gain>5)throw new ArgumentException("长度或倍率无效");
            if(!Numbers.Finite(RangeStart)||!Numbers.Finite(RangeEnd)||RangeStart<0||RangeEnd>1||RangeEnd<=RangeStart)throw new ArgumentException("插入范围要求 000 ≤ 起点 < 终点 ≤ 999");
            foreach(var p in new[]{PlaneOffset,TargetOffset,RootOffset,EndOffset,PlaneRotation,TargetPlaneOffset,TargetPlaneRotation})if(!Numbers.Finite(p.X)||!Numbers.Finite(p.Y)||!Numbers.Finite(p.Z))throw new ArgumentException("XYZ 偏移必须是有效数字");
            if(AlignmentVersion<0||AlignmentVersion>1)throw new ArgumentException("未知参考框架版本");
            if(!Enum.IsDefined(typeof(NativeTarget),Target)||Target==NativeTarget.BOTH_FEET)throw new ArgumentException("目标类型无效");
        }
        public void Apply(Candidate candidate)
        {
            Validate();if(candidate==null||candidate.Native==null)return;
            NativeMotionSample sample=candidate.Native;NativeMotionResult frame=NativeMotion.Solve(sample);
            if(sample.HasBodyFrame&&AlignmentVersion==1)
            {
                sample.ReferenceFrame=sample.BodyFrame.Adjust(PlaneOffset,PlaneRotation);
                if(sample.HasTargetFrame)sample.TargetReferenceFrame=sample.TargetBodyFrame.Adjust(TargetPlaneOffset,TargetPlaneRotation);
                if(sample.HasShaftPose&&Numbers.Finite(sample.ShaftRestLength)&&sample.ShaftRestLength>1e-7)sample.ShaftRestLength*=LengthScale;
                if(sample.RootDistance)
                {
                    sample.End=sample.Origin+(sample.End-sample.Origin)*LengthScale;
                    sample.Origin+=sample.ReferenceFrame.World(RootOffset);
                }
                else sample.Origin=sample.End+(sample.Origin-sample.End)*LengthScale+sample.ReferenceFrame.World(RootOffset);
                sample.End+=sample.ReferenceFrame.World(EndOffset);sample.Target+=sample.ReferenceFrame.World(TargetOffset);
                if(sample.RootDistance)
                {
                    sample.ReferenceFrame.Origin=sample.Origin+sample.BodyFrame.World(PlaneOffset);
                    sample.TargetReferenceFrame.Origin=sample.Target+sample.TargetBodyFrame.World(TargetPlaneOffset);
                }
                sample.DisplayPlane=sample.ReferenceFrame.Origin;sample.UseReferenceUp=true;sample.Adjusted=true;
            }
            else
            {
            sample.Origin=sample.End+(sample.Origin-sample.End)*LengthScale+World(RootOffset,frame);
            sample.Target+=World(TargetOffset,frame);
            sample.PlanePoint+=World(PlaneOffset,frame);
            sample.DisplayPlane=sample.Origin+World(PlaneOffset,frame);sample.Adjusted=true;
            }
            sample.Gain=Numbers.Clamp(sample.Gain*Gain,.25,5);sample.FixedRange=FixedRange;sample.RangeStart=RangeStart;sample.RangeEnd=RangeEnd;
            if(!sample.Studio)sample.Stroke=NativeStroke;
            sample.PlaneWidth=PlaneWidth;sample.PlaneDepth=PlaneDepth;sample.HiddenObjects=HiddenObjects;
            sample.LateralSpan=LateralSpan;
            for(int i=0;i<6;i++){double low=sample.Minimum[i],high=sample.Maximum[i];if(low>high)throw new ArgumentException("通道全局限幅无效");sample.Minimum[i]=Numbers.Clamp(ChannelMinimum[i],low,high);sample.Maximum[i]=Numbers.Clamp(ChannelMaximum[i],sample.Minimum[i],high);sample.Multipliers[i]=Multipliers[i];}
            NativeMotion.Solve(sample);
            candidate.Reference=new Pose{X=(float)sample.Origin.X,Y=(float)sample.Origin.Y,Z=(float)sample.Origin.Z};
            candidate.Target=new Pose{X=(float)sample.Target.X,Y=(float)sample.Target.Y,Z=(float)sample.Target.Z};
        }
        private static MotionVector World(MotionVector offset,NativeMotionResult f){return f.X*offset.X+f.Y*offset.Y+f.Z*offset.Z;}
        public void UpgradeStudio(NativeMotionSample sample)
        {
            if(AlignmentVersion==1||sample==null||!sample.HasBodyFrame)return;
            var old=NativeMotion.Solve(sample);var body=sample.BodyFrame;
            RootOffset=body.Local(World(RootOffset,old));TargetOffset=body.Local(World(TargetOffset,old));PlaneOffset=body.Local(World(PlaneOffset,old));
            AlignmentVersion=1;
        }
    }
    public sealed class TimelineSegment
    {
        internal const string NoSource="@none";
        public string Id=Guid.NewGuid().ToString("N"),Source="",Label="";
        public string Name="";
        public int ColorIndex=-1;
        public double Start,End;
        public MeasurementProfile Measurement=new MeasurementProfile();
        public string Title(){return !string.IsNullOrEmpty(Name)?Name:Source==NoSource?"无":string.IsNullOrEmpty(Source)?"镜头自动选源":Label;}
    }
    public sealed class SceneProfile
    {
        public int Version=1;
        public string Scene="";
        public List<TimelineSegment> Segments=new List<TimelineSegment>();
        public MeasurementProfile Defaults;
        public string DefaultSource="",DefaultLabel="";
        public TimelineSegment At(double time)
        {
            if(!Numbers.Finite(time))return null;
            for(int i=0;i<Segments.Count;i++){var s=Segments[i];if(time>=s.Start&&(time<s.End||i==Segments.Count-1&&time==s.End))return s;}
            return null;
        }
        public void Validate()
        {
            if(Version!=1||Segments==null)throw new ArgumentException("场景配置版本或分段无效");
            if(Defaults!=null)Defaults.Validate();
            Segments.Sort((a,b)=>a.Start.CompareTo(b.Start));double last=0;var ids=new HashSet<string>();
            int nextColor=0;foreach(var s in Segments)if(s!=null)nextColor=Math.Max(nextColor,s.ColorIndex+1);
            foreach(var s in Segments)
            {
                if(s==null||string.IsNullOrEmpty(s.Id)||!ids.Add(s.Id)||!Numbers.Finite(s.Start)||!Numbers.Finite(s.End)||s.Start<last||s.End<=s.Start)throw new ArgumentException("分段不能重叠、倒置或重复");
                if(s.Measurement==null)throw new ArgumentException("缺少分段测量配置");s.Measurement.Validate();last=s.End;
                if(s.ColorIndex<0)s.ColorIndex=nextColor++;
                s.Source=s.Source??"";s.Label=s.Label??"";s.Name=s.Name??"";
            }
        }
        public TimelineSegment Split(double time)
        {
            TimelineSegment s=At(time);if(s==null||time-s.Start<.001||s.End-time<.001)throw new ArgumentException("请把播放头移到段内再分段");
            var next=new TimelineSegment{Start=time,End=s.End,Source=s.Source,Label=s.Label,Name=s.Name,Measurement=s.Measurement.Clone()};s.End=time;Segments.Add(next);Validate();return next;
        }
        public TimelineSegment CreateGap(double time,double duration)
        {
            if(!Numbers.Finite(time)||!Numbers.Finite(duration)||time<0||time>=duration||At(time)!=null)throw new ArgumentException("请选择空白时间区域");
            double a=0,b=duration;foreach(var s in Segments){if(s.End<=time)a=Math.Max(a,s.End);if(s.Start>time)b=Math.Min(b,s.Start);}
            var created=new TimelineSegment{Start=a,End=b,Source=TimelineSegment.NoSource,Name="新时间段",Measurement=Defaults==null?new MeasurementProfile{AlignmentVersion=1}:Defaults.Clone()};
            Segments.Add(created);Validate();return created;
        }
        public double ResizeLimit(string id,bool left,double value,double duration)
        {
            var s=Segments.Find(x=>x.Id==id);if(s==null)throw new ArgumentException("时间段已不存在");
            double lo=0,hi=duration;foreach(var other in Segments)if(other!=s){if(other.End<=s.Start)lo=Math.Max(lo,other.End);if(other.Start>=s.End)hi=Math.Min(hi,other.Start);}
            if(left)hi=s.End-.001;else lo=s.Start+.001;
            return Numbers.Clamp(value,lo,hi);
        }
        public void Resize(string id,bool left,double value,double duration)
        {if(!Numbers.Finite(value))throw new ArgumentException("时间无效");var s=Segments.Find(x=>x.Id==id);double t=ResizeLimit(id,left,value,duration);if(left)s.Start=t;else s.End=t;Validate();}
        public void Move(string id,double center,double duration)
        {
            Validate();var s=Segments.Find(x=>x.Id==id);if(s==null||!Numbers.Finite(center))throw new ArgumentException("时间段或位置无效");
            double length=s.End-s.Start,a=Numbers.Clamp(center-length/2,0,Math.Max(0,duration-length)),b=a+length;
            if(!Segments.Exists(x=>x!=s&&a<x.End&&b>x.Start)){s.Start=a;s.End=b;Validate();return;}
            // Insert among blocks, preserving every duration and the existing gaps between slots.
            int old=Segments.IndexOf(s);var ordered=new List<TimelineSegment>(Segments);ordered.Remove(s);
            int dest=0;while(dest<ordered.Count&&center>(ordered[dest].Start+ordered[dest].End)*.5)dest++;ordered.Insert(dest,s);
            if(dest==old)return;
            var lengths=new Dictionary<string,double>();var gaps=new List<double>();
            for(int i=0;i<Segments.Count;i++){lengths[Segments[i].Id]=Segments[i].End-Segments[i].Start;if(i+1<Segments.Count)gaps.Add(Segments[i+1].Start-Segments[i].End);}
            double cursor=Segments[0].Start;for(int i=0;i<ordered.Count;i++){ordered[i].Start=cursor;ordered[i].End=cursor+lengths[ordered[i].Id];cursor=ordered[i].End+(i<gaps.Count?gaps[i]:0);}
            Segments=ordered;Validate();
        }
    }
    public sealed class AnimationProfile
    {public string Key="";public MeasurementProfile Measurement=new MeasurementProfile();}
    public sealed class AnimationProfiles
    {public int Version=1;public List<AnimationProfile> Animations=new List<AnimationProfile>();}

    public sealed class SyncSession
    {
        public bool Enabled;
        private string source="";
        private double[] last;
        private bool reseed;
        public void Stop(){Enabled=false;last=null;source="";reseed=false;}
        public bool Update(MotionPipeline pipeline,Snapshot frame,Candidate candidate,bool connected,StrokeAxis axis)
        {
            string key=frame.Session+"|"+frame.Scene+"|"+(candidate==null?"":candidate.Id+candidate.MotionKey);
            if(key!=source){source=key;reseed=true;}
            bool output=pipeline.Update(frame,candidate,0,axis);
            if(!Enabled){if(pipeline.Armed)pipeline.Disarm("同步未启用");return false;}
            if(!connected||!pipeline.HasLiveSample||!pipeline.Calibrated||pipeline.Calibrating||pipeline.ShapePreview)
            {pipeline.Disarm(pipeline.ShapePreview?"同步已启用 · 实验仅预览":!connected?"同步已启用 · 等待设备":"同步已启用 · 等待有效来源 / 播放 / 范围");reseed=true;return false;}
            if(reseed&&last!=null)
            {
                double[] start=(double[])last.Clone();
                if(candidate!=null&&candidate.Native!=null)for(int i=0;i<6;i++)start[i]=Numbers.Clamp(start[i],candidate.Native.Minimum[i],candidate.Native.Maximum[i]);
                pipeline.ContinueFrom(start);
            }
            reseed=false;if(!pipeline.Armed)output=pipeline.Arm();else output=true;
            if(output)last=(double[])pipeline.Values.Clone();return output;
        }
    }
}
