using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace KoiSync.Studio
{
    public sealed class Chapter
    {
        public double Start;
        public int Source=-1,Target=-1,Opening;
        public string Name="待指定对象";
        public string CustomName="";
        public bool Manual;
        public bool CalibrationEdited;
        public bool EffectsEdited;
        public string SourceName="",TargetName="";
        public ChapterCalibration Calibration;
        public ChapterRotationReference RotationReference;
        public ChapterStrokeCalibration StrokeCalibration;
        public int MappingMode=-1;
        public ChapterPhase Phase;
        public bool ClimaxDetected;
        public string ClimaxEvidence="";
        public ClimaxOptions Climax;
        public ChapterGrindOptions Grind;
        public double DepthP05=double.NaN,DepthP95=double.NaN,PeakHz;
        public bool NeedsReview;
        public string ReviewNote="";
        public bool IsClimax {get{return Phase==ChapterPhase.Climax||Phase==ChapterPhase.Automatic&&ClimaxDetected;}}
        public string Identity {get{return Source+":"+Target+":"+Opening;}}
        public Chapter Copy(double time)
        {return new Chapter{Start=time,Source=Source,Target=Target,Opening=Opening,Name=Name,CustomName=CustomName,Manual=Manual,CalibrationEdited=CalibrationEdited,EffectsEdited=EffectsEdited,
            SourceName=SourceName,TargetName=TargetName,Calibration=Calibration==null?null:Calibration.Copy(),RotationReference=RotationReference==null?null:RotationReference.Copy(),
            StrokeCalibration=StrokeCalibration==null?null:StrokeCalibration.Copy(),MappingMode=MappingMode,Phase=Phase,ClimaxDetected=ClimaxDetected,ClimaxEvidence=ClimaxEvidence,Climax=Climax==null?null:Climax.Copy(),
            Grind=Grind==null?null:Grind.Copy(),DepthP05=DepthP05,DepthP95=DepthP95,PeakHz=PeakHz,NeedsReview=NeedsReview,ReviewNote=ReviewNote};}
    }
    public sealed class AttachmentEvent
    {
        public double Time;
        public string Constraint;
        public bool Enabled;
        public Chapter Binding;
    }
    public sealed class ChapterBook
    {
        public readonly List<Chapter> Chapters=new List<Chapter>();
        public void RebuildAutomatic()
        {
            // Explicit edits survive rebuilding observations from playback/keys.
            Chapters.RemoveAll(c=>!c.Manual&&!c.CalibrationEdited&&!c.EffectsEdited&&string.IsNullOrEmpty(c.CustomName)&&c.MappingMode<0&&c.Phase==ChapterPhase.Automatic&&
                (c.StrokeCalibration==null||c.StrokeCalibration.FromHeight||c.StrokeCalibration.FromFinger));
        }
        public Chapter At(double time)
        {Chapter current=null;foreach(var item in Chapters){if(item.Start>time+.00001)break;current=item;}return current;}
        public void Observe(double time,Chapter binding,bool replaceManual=false)
        {
            if(double.IsNaN(time)||double.IsInfinity(time)||time<0||binding==null)return;
            var current=At(time);if(current!=null&&((current.Manual&&!replaceManual)||current.Identity==binding.Identity))return;
            if(current!=null&&Math.Abs(current.Start-time)<.0001)binding.CustomName=current.CustomName;
            binding.Start=time;Chapters.RemoveAll(c=>Math.Abs(c.Start-time)<.0001);Chapters.Add(binding);Sort();CompactAutomatic();
        }
        public bool CorrectAutomaticOpening(double time,Chapter confirmed)
        {
            if(!Finite(time)||time<0||confirmed==null)return false;
            var current=At(time);
            if(current==null||current.Manual||current.Source!=confirmed.Source||current.Target!=confirmed.Target||current.Opening==confirmed.Opening)return false;
            // Only the caller's continuously confirmed contact may correct a
            // stale automatic boundary. Retain its calibration and manual edits.
            // A source finger uses a different chain and origin from a shaft,
            // and left/right fingers may have separate chapter calibration.
            if(current.Opening>=6||confirmed.Opening>=6){current.Calibration=null;current.StrokeCalibration=null;}
            current.Opening=confirmed.Opening;current.Name=confirmed.Name;current.RotationReference=null;CompactAutomatic();return true;
        }
        public void CompactAutomatic()
        {
            // Replaying a visibility boundary can observe it a few frames earlier
            // than the previous pass. Keep one boundary without erasing deliberate
            // manual splits or a distinct calibration for the same pair.
            for(int i=1;i<Chapters.Count;)
            {
                var before=Chapters[i-1];var next=Chapters[i];
                if(!before.Manual&&!next.Manual&&before.CalibrationEdited==next.CalibrationEdited&&before.EffectsEdited==next.EffectsEdited&&string.IsNullOrEmpty(before.CustomName)&&string.IsNullOrEmpty(next.CustomName)&&before.Identity==next.Identity&&before.MappingMode==next.MappingMode&&before.Phase==next.Phase&&before.ClimaxDetected==next.ClimaxDetected&&ClimaxOptions.Equivalent(before.Climax,next.Climax)&&SameCalibration(before.Calibration,next.Calibration)&&ChapterRotationReference.Equivalent(before.RotationReference,next.RotationReference)&&ChapterStrokeCalibration.Equivalent(before.StrokeCalibration,next.StrokeCalibration))
                {
                    if(before.Calibration==null&&next.Calibration!=null)before.Calibration=next.Calibration.Copy();
                    if(before.RotationReference==null&&next.RotationReference!=null)before.RotationReference=next.RotationReference.Copy();
                    if(before.StrokeCalibration==null&&next.StrokeCalibration!=null)before.StrokeCalibration=next.StrokeCalibration.Copy();
                    Chapters.RemoveAt(i);
                }
                else i++;
            }
        }
        private static bool SameCalibration(ChapterCalibration a,ChapterCalibration b)
        {
            if(a==null||b==null)return true;
            return Math.Abs(a.Length-b.Length)<1e-8&&Math.Abs(a.L1-b.L1)<1e-8&&Math.Abs(a.L2-b.L2)<1e-8&&
                Math.Abs(a.R0Plane-b.R0Plane)<1e-8&&Math.Abs(a.R1Plane-b.R1Plane)<1e-8&&Math.Abs(a.R2Plane-b.R2Plane)<1e-8&&
                (a.Origin-b.Origin).Length<1e-8&&a.Linked==b.Linked&&a.ManualEnd.HasValue==b.ManualEnd.HasValue&&
                (!a.ManualEnd.HasValue||(a.ManualEnd.Value-b.ManualEnd.Value).Length<1e-8);
        }
        public Chapter Split(double time)
        {
            if(double.IsNaN(time)||double.IsInfinity(time)||time<0)throw new ArgumentException("Invalid chapter time");
            var old=At(time);var chapter=old==null?new Chapter{Start=time,Name="手动章节"}:old.Copy(time);chapter.Manual=true;
            Chapters.RemoveAll(c=>Math.Abs(c.Start-time)<.0001);Chapters.Add(chapter);Sort();return chapter;
        }
        // Overlay an explicit event without changing actor selection, manual
        // binding or calibration. Insert the ending boundary before the start.
        public bool MarkClimax(double start,double end,double duration,string identity,ClimaxOptions defaults,string evidence)
        {
            if(!Finite(start)||!Finite(end)||start<0||end<=start||end>duration)return false;
            var at=At(start);if(at==null||at.Identity!=identity||at.Phase!=ChapterPhase.Automatic)return false;
            bool changed=false;
            if(end<duration-.00001)
            {
                var after=At(end);
                if(after!=null&&Math.Abs(after.Start-end)>.00001&&after.Identity==identity&&after.Phase==ChapterPhase.Automatic&&!after.ClimaxDetected)
                {Chapters.Add(after.Copy(end));changed=true;}
            }
            if(Math.Abs(at.Start-start)>.00001&&!at.ClimaxDetected){at=at.Copy(start);Chapters.Add(at);changed=true;}
            foreach(var chapter in Chapters)
                if(chapter.Start>=at.Start-.00001&&chapter.Start<end-.00001&&chapter.Identity==identity&&chapter.Phase==ChapterPhase.Automatic&&!chapter.ClimaxDetected)
                {chapter.ClimaxDetected=true;chapter.ClimaxEvidence=evidence;chapter.Climax=chapter.Climax??defaults.Copy();changed=true;}
            Sort();return changed;
        }
        public bool ObserveClimax(double time,bool active,string identity,ClimaxOptions defaults,string evidence)
        {
            var at=At(time);if(at==null||at.Identity!=identity||at.Phase!=ChapterPhase.Automatic||at.ClimaxDetected==active)return false;
            var next=at.Copy(time);next.ClimaxDetected=active;next.ClimaxEvidence=active?evidence:"";
            if(active)next.Climax=next.Climax??defaults.Copy();
            Chapters.RemoveAll(c=>Math.Abs(c.Start-time)<.00001);Chapters.Add(next);Sort();return true;
        }
        public void ReadAttachmentEvents(List<AttachmentEvent> events)
        {
            var manual=Chapters.FindAll(c=>c.Manual||!string.IsNullOrEmpty(c.CustomName)||c.Calibration!=null||c.RotationReference!=null||c.StrokeCalibration!=null||c.MappingMode>=0||c.Phase!=ChapterPhase.Automatic||c.ClimaxDetected||c.Climax!=null);
            Chapters.Clear();events.Sort((a,b)=>a.Time.CompareTo(b.Time));var active=new Dictionary<string,Chapter>();
            for(int i=0;i<events.Count;)
            {
                double time=events[i].Time;
                do{var item=events[i];if(item.Enabled)active[item.Constraint]=item.Binding;else active.Remove(item.Constraint);i++;}
                while(i<events.Count&&Math.Abs(events[i].Time-time)<.00001);
                var unique=new Dictionary<string,Chapter>();foreach(var item in active.Values)if(item!=null)unique[item.Identity]=item;
                Chapter next=null;
                if(unique.Count==1)foreach(var value in unique.Values)next=new Chapter{Source=value.Source,Target=value.Target,Opening=value.Opening,Name=value.Name};
                if(next==null)next=new Chapter{Name=unique.Count==0?"无吸附对象":"多组吸附 · 请指定本章对象"};
                Observe(time,next);
            }
            foreach(var chapter in manual){Chapters.RemoveAll(c=>Math.Abs(c.Start-chapter.Start)<.0001);Chapters.Add(chapter);}
            Sort();
        }
        public void SetBounds(Chapter chapter,double start,double end,double duration)
        {
            int index=Chapters.IndexOf(chapter);
            if(index<0||!Finite(start)||!Finite(end)||!Finite(duration)||start<0||end>duration||end-start<.001)
                throw new ArgumentException("Invalid chapter bounds");
            if(index==0&&Math.Abs(start-chapter.Start)>.00001)throw new ArgumentException("First chapter start is fixed");
            if(index>0&&start<=Chapters[index-1].Start+.001)throw new ArgumentException("Chapter start overlaps previous boundary");
            if(index+1<Chapters.Count)
            {
                double nextEnd=index+2<Chapters.Count?Chapters[index+2].Start:duration;
                if(end>=nextEnd-.001)throw new ArgumentException("Chapter end overlaps next boundary");
                Chapters[index+1].Start=end;Chapters[index+1].Manual=true;
            }
            else if(Math.Abs(end-duration)>.00001)throw new ArgumentException("Last chapter end follows scene duration");
            chapter.Start=start;chapter.Manual=true;
        }
        private static bool Finite(double value){return !double.IsNaN(value)&&!double.IsInfinity(value);}
        public Chapter Remove(Chapter chapter)
        {
            int index=Chapters.IndexOf(chapter);if(index<0)return null;
            double start=chapter.Start;Chapters.RemoveAt(index);
            if(Chapters.Count==0)return null;
            // Removing metadata leaves animation time intact. A neighbour owns
            // the released interval; pin it so analysis does not recreate it.
            var neighbour=Chapters[Math.Max(0,index-1)];
            if(index==0)neighbour.Start=start;
            // Deleting an automatically detected effect is also an explicit
            // decision about that interval, not merely about actor binding.
            if(chapter.IsClimax&&!neighbour.IsClimax)neighbour.Phase=ChapterPhase.Normal;
            neighbour.Manual=true;return neighbour;
        }
        private void Sort(){Chapters.Sort((a,b)=>a.Start.CompareTo(b.Start));}
        public string ToXml()
        {
            var text=new StringWriter(CultureInfo.InvariantCulture);
            using(var xml=XmlWriter.Create(text,new XmlWriterSettings{OmitXmlDeclaration=true}))
            {
                xml.WriteStartElement("chapters");xml.WriteAttributeString("version","2");
                foreach(var c in Chapters)
                {
                    xml.WriteStartElement("chapter");xml.WriteAttributeString("time",c.Start.ToString("R",CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("source",c.Source.ToString());xml.WriteAttributeString("target",c.Target.ToString());xml.WriteAttributeString("opening",c.Opening.ToString());
                    xml.WriteAttributeString("manual",c.Manual?"true":"false");xml.WriteAttributeString("name",c.Name??"");
                    if(c.CalibrationEdited)xml.WriteAttributeString("calibrationEdited","true");
                    if(c.EffectsEdited)xml.WriteAttributeString("effectsEdited","true");
                    if(!string.IsNullOrEmpty(c.CustomName))xml.WriteAttributeString("customName",c.CustomName);
                    xml.WriteAttributeString("sourceName",c.SourceName??"");xml.WriteAttributeString("targetName",c.TargetName??"");
                    if(c.MappingMode>=0)xml.WriteAttributeString("mappingMode",c.MappingMode.ToString());
                    xml.WriteAttributeString("phase",((int)c.Phase).ToString());xml.WriteAttributeString("climaxDetected",c.ClimaxDetected?"true":"false");
                    if(c.ClimaxEvidence.Length>0)xml.WriteAttributeString("climaxEvidence",c.ClimaxEvidence);
                    if(!double.IsNaN(c.DepthP05)&&!double.IsNaN(c.DepthP95)&&c.DepthP95>c.DepthP05)
                    {xml.WriteAttributeString("depthP05",c.DepthP05.ToString("R",CultureInfo.InvariantCulture));xml.WriteAttributeString("depthP95",c.DepthP95.ToString("R",CultureInfo.InvariantCulture));}
                    if(c.PeakHz>0)xml.WriteAttributeString("peakHz",c.PeakHz.ToString("R",CultureInfo.InvariantCulture));
                    if(c.NeedsReview)xml.WriteAttributeString("needsReview","true");
                    if(!string.IsNullOrEmpty(c.ReviewNote))xml.WriteAttributeString("reviewNote",c.ReviewNote);
                    if(c.Climax!=null)c.Climax.Write(xml);
                    if(c.Grind!=null)c.Grind.Write(xml);
                    if(c.Calibration!=null)c.Calibration.Write(xml);if(c.RotationReference!=null)c.RotationReference.Write(xml);if(c.StrokeCalibration!=null)c.StrokeCalibration.Write(xml);xml.WriteEndElement();
                }
                xml.WriteEndElement();
            }
            return text.ToString();
        }
        public void FromXml(string text)
        {
            var parsed=new List<Chapter>();var document=new XmlDocument();
            var settings=new XmlReaderSettings{ProhibitDtd=true,XmlResolver=null};
            using(var reader=XmlReader.Create(new StringReader(text),settings))document.Load(reader);
            foreach(XmlElement node in document.SelectNodes("/chapters/chapter"))
            {
                var chapter=new Chapter{Start=double.Parse(node.GetAttribute("time"),CultureInfo.InvariantCulture),Source=int.Parse(node.GetAttribute("source")),
                    Target=int.Parse(node.GetAttribute("target")),Opening=int.Parse(node.GetAttribute("opening")),Manual=node.GetAttribute("manual")=="true",Name=node.GetAttribute("name"),CustomName=node.GetAttribute("customName"),
                    SourceName=node.GetAttribute("sourceName"),TargetName=node.GetAttribute("targetName"),Calibration=ChapterCalibration.Read(node.SelectSingleNode("calibration") as XmlElement),
                    RotationReference=ChapterRotationReference.Read(node.SelectSingleNode("rotationReference") as XmlElement),StrokeCalibration=ChapterStrokeCalibration.Read(node.SelectSingleNode("strokeCalibration") as XmlElement)};
                chapter.MappingMode=node.HasAttribute("mappingMode")?int.Parse(node.GetAttribute("mappingMode")):-1;
                chapter.CalibrationEdited=node.GetAttribute("calibrationEdited")=="true";
                chapter.EffectsEdited=node.GetAttribute("effectsEdited")=="true";
                chapter.Phase=node.HasAttribute("phase")?(ChapterPhase)int.Parse(node.GetAttribute("phase")):ChapterPhase.Automatic;
                chapter.ClimaxDetected=node.GetAttribute("climaxDetected")=="true";chapter.ClimaxEvidence=node.GetAttribute("climaxEvidence");
                double parsedVal;
                if(node.HasAttribute("depthP05")&&double.TryParse(node.GetAttribute("depthP05"),NumberStyles.Float,CultureInfo.InvariantCulture,out parsedVal))chapter.DepthP05=parsedVal;
                if(node.HasAttribute("depthP95")&&double.TryParse(node.GetAttribute("depthP95"),NumberStyles.Float,CultureInfo.InvariantCulture,out parsedVal))chapter.DepthP95=parsedVal;
                if(node.HasAttribute("peakHz")&&double.TryParse(node.GetAttribute("peakHz"),NumberStyles.Float,CultureInfo.InvariantCulture,out parsedVal))chapter.PeakHz=parsedVal;
                chapter.NeedsReview=node.GetAttribute("needsReview")=="true";
                chapter.ReviewNote=node.GetAttribute("reviewNote")??"";
                chapter.Climax=ClimaxOptions.Read(node.SelectSingleNode("climaxOptions") as XmlElement);
                chapter.Grind=ChapterGrindOptions.Read(node.SelectSingleNode("grindOptions") as XmlElement);
                if(double.IsNaN(chapter.Start)||double.IsInfinity(chapter.Start)||chapter.Start<0||chapter.Opening<0||chapter.Opening>7||chapter.MappingMode< -1||chapter.MappingMode>3||(int)chapter.Phase<0||(int)chapter.Phase>2)throw new ArgumentException("Invalid chapter metadata");
                parsed.Add(chapter);
            }
            Chapters.Clear();Chapters.AddRange(parsed);Sort();CompactAutomatic();
        }
    }
}
