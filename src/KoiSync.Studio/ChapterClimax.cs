using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using KoiSync.Core;

namespace KoiSync.Studio
{
    public enum ChapterPhase { Automatic,Normal,Climax }
    public enum ClimaxFrequencyMode { Fixed,LiveL0,AutoMotion,Simulated }
    public sealed class ClimaxOptions
    {
        public const double ActiveMotionMinHz=1.35;
        public const double ActiveMotionMinAmpPct=16.0;
        public bool Enabled=true;
        public bool LocalSettings,Detect=true;
        public double MaximumSeconds=6;
        public double Minimum=350,Maximum=750,Hertz=10;
        public ClimaxFrequencyMode FrequencyMode;
        public static ClimaxOptions Resolve(ClimaxOptions saved,ClimaxOptions defaults)
        {
            if(saved!=null&&saved.LocalSettings)return saved.Copy();
            var value=defaults.Copy();value.LocalSettings=true;
            if(saved!=null){value.Hertz=saved.Hertz;value.FrequencyMode=saved.FrequencyMode;}
            return value;
        }
        public ClimaxOptions Copy(){return new ClimaxOptions{Enabled=Enabled,Minimum=Minimum,Maximum=Maximum,Hertz=Hertz,FrequencyMode=FrequencyMode,LocalSettings=LocalSettings,Detect=Detect,MaximumSeconds=MaximumSeconds};}
        public void Validate()
        {
            if(!Numbers.Finite(Minimum)||!Numbers.Finite(Maximum)||Minimum<0||Maximum>999||Minimum>Maximum||
                !Numbers.Finite(Hertz)||Hertz<.1||Hertz>20||(int)FrequencyMode<0||(int)FrequencyMode>3||!Numbers.Finite(MaximumSeconds)||MaximumSeconds<.1||MaximumSeconds>60)throw new ArgumentException("L0: 0–999, minimum ≤ maximum; Hz: 0.1–20; seconds: 0.1–60");
        }
        public bool ShouldUseFixedOscillation(double chapterPeakHz,double chapterAmpPct,bool liveCycleReady,double liveCycleHz)
        {
            if(!Enabled)return false;
            if(FrequencyMode==ClimaxFrequencyMode.Fixed)return true;
            if(FrequencyMode==ClimaxFrequencyMode.Simulated||FrequencyMode==ClimaxFrequencyMode.LiveL0)return false;
            if(Numbers.Finite(chapterPeakHz)&&chapterPeakHz>.01)
            {
                bool hasAmp=Numbers.Finite(chapterAmpPct);
                bool activeThrust=chapterPeakHz>=ActiveMotionMinHz&&(!hasAmp||chapterAmpPct>=ActiveMotionMinAmpPct);
                return !activeThrust;
            }
            if(liveCycleReady&&Numbers.Finite(liveCycleHz)&&liveCycleHz>=ActiveMotionMinHz)return false;
            return true;
        }
        public AxisOscillation Create(double start,double end,double low,double high)
        {
            Validate();if(!Enabled||end<=start||(FrequencyMode!=ClimaxFrequencyMode.Fixed&&FrequencyMode!=ClimaxFrequencyMode.AutoMotion))return null;
            return new AxisOscillation(start,end,Numbers.Clamp(Minimum/999,low,high),Numbers.Clamp(Maximum/999,low,high),Hertz);
        }
        public static bool Equivalent(ClimaxOptions a,ClimaxOptions b)
        {return a==null||b==null?a==b:a.Enabled==b.Enabled&&a.Minimum==b.Minimum&&a.Maximum==b.Maximum&&a.Hertz==b.Hertz&&a.FrequencyMode==b.FrequencyMode&&a.LocalSettings==b.LocalSettings&&a.Detect==b.Detect&&a.MaximumSeconds==b.MaximumSeconds;}
        public void Write(XmlWriter xml)
        {
            Validate();xml.WriteStartElement("climaxOptions");xml.WriteAttributeString("enabled",Enabled?"true":"false");
            xml.WriteAttributeString("localSettings",LocalSettings?"true":"false");xml.WriteAttributeString("detect",Detect?"true":"false");xml.WriteAttributeString("maximumSeconds",MaximumSeconds.ToString("R",CultureInfo.InvariantCulture));
            xml.WriteAttributeString("min",Minimum.ToString("R",CultureInfo.InvariantCulture));xml.WriteAttributeString("max",Maximum.ToString("R",CultureInfo.InvariantCulture));
            xml.WriteAttributeString("hz",Hertz.ToString("R",CultureInfo.InvariantCulture));xml.WriteAttributeString("frequencyMode",((int)FrequencyMode).ToString(CultureInfo.InvariantCulture));xml.WriteEndElement();
        }
        public static ClimaxOptions Read(XmlElement node)
        {
            if(node==null)return null;
            var value=new ClimaxOptions{Enabled=node.GetAttribute("enabled")!="false",Minimum=double.Parse(node.GetAttribute("min"),CultureInfo.InvariantCulture),
                Maximum=double.Parse(node.GetAttribute("max"),CultureInfo.InvariantCulture),Hertz=double.Parse(node.GetAttribute("hz"),CultureInfo.InvariantCulture)};
            if(node.HasAttribute("frequencyMode"))value.FrequencyMode=(ClimaxFrequencyMode)int.Parse(node.GetAttribute("frequencyMode"),CultureInfo.InvariantCulture);
            value.LocalSettings=node.GetAttribute("localSettings")=="true";value.Detect=node.GetAttribute("detect")!="false";
            if(node.HasAttribute("maximumSeconds"))value.MaximumSeconds=double.Parse(node.GetAttribute("maximumSeconds"),CultureInfo.InvariantCulture);
            value.Validate();return value;
        }
    }
    public sealed class ClimaxInterval
    {
        public double Start,End;
        public ClimaxInterval(double start,double end){Start=start;End=end;}
    }
    public static class ClimaxCues
    {
        // Brief gaps between successive bursts of the same event belong to
        // one episode. A distinct pause still ends the detected chapter.
        public const double EpisodeGap=1.5;
        // Verified against Studio.Info.dicAnimeLoadInfo. OLoop and *_A/*_B
        // are pre-climax/afterglow, and must never trigger this effect.
        public static bool Animation(string clip)
        {
            string name=(clip??"").ToLowerInvariant();
            if(AnimationName(name))return true;
            foreach(string prefix in new[]{"l_","m_","s_"})if(name.StartsWith(prefix)){name=name.Substring(2);break;}
            if(name.StartsWith("a_"))name=name.Substring(2);
            return AnimationName(name);
        }
        private static bool AnimationName(string name)
        {
            if(name.StartsWith("a_"))name=name.Substring(2);
            if(name=="orgasm"||name=="worgasm"||name=="sorgasm"||name=="orgasm_start"||name=="orgasm_loop")return true;
            foreach(string prefix in new[]{"m_in_","m_out_","wf_in_","ws_in_","sf_in_","ss_in_","in_"})
                if(name==prefix+"start"||name==prefix+"loop")return true;
            return name=="oral_idle_in"||name=="oral_idle";
        }
        public static bool Shooting(string name)
        {
            name=(name??"").ToLowerInvariant();
            // Deliberately narrow asset labels; a puddle, drip or skin overlay
            // is not an event. Generic words such as "cum" alone do not qualify.
            return name.Contains("cum juice")&&name.Contains("(shooting)");
        }
        public static bool Marker(string name)
        {return (name??"").StartsWith("[KoiSync:Climax]",StringComparison.OrdinalIgnoreCase);}
        public static List<ClimaxInterval> Windows(IList<double> times,IList<bool> values,double duration,double maximum,double joinGap)
        {
            var windows=new List<ClimaxInterval>();double start=-1;bool state=false;
            for(int i=0;i<times.Count;i++)
            {
                if(!Numbers.Finite(times[i])||times[i]<0||times[i]>duration)continue;
                bool next=values[i];
                if(next&&!state)start=times[i];
                if(!next&&state&&start>=0)AddWindow(windows,start,Math.Min(times[i],start+maximum),joinGap);
                state=next;
            }
            if(state&&start>=0)AddWindow(windows,start,Math.Min(duration,start+maximum),joinGap);
            return windows;
        }
        private static void AddWindow(List<ClimaxInterval> list,double start,double end,double gap)
        {
            if(end<=start)return;
            if(list.Count>0&&start<=list[list.Count-1].End+gap)list[list.Count-1].End=Math.Max(end,list[list.Count-1].End);
            else list.Add(new ClimaxInterval(start,end));
        }
    }
}
