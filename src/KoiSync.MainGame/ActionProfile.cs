using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using KoiSync.Core;

namespace KoiSync.MainGame
{
    internal static class MainGameVersion
    {
        internal const string Number="1.0.0";
        internal const string Solver="main-0.2.0";
    }

    public sealed class ActionProfile
    {
        public string Key,Name;
        public ActorSlot Source,Target;
        public ActorSlot? WeakSource;
        public ActionSource Effector;
        public NativeTarget Contact;
        public ActionLandmark Landmark;
        public string Resource="";
        public AxisMappingMode Mode=AxisMappingMode.Soft;
        public bool ManualBinding;
        public bool CenteredMotion,ReferenceReady;
        public double GameLength=.13;
        public ActionCalibration Calibration;
        public ActionRotationReference Rotation;
        public ActorSlot CalibrationSource {get;private set;}
        private readonly Dictionary<ActorSlot,ActionCalibration> calibrations=new Dictionary<ActorSlot,ActionCalibration>();
        private readonly Dictionary<ActorSlot,ActionRotationReference> rotations=new Dictionary<ActorSlot,ActionRotationReference>();
        private readonly Dictionary<NativeTarget,MotionVector> entryOffsets=new Dictionary<NativeTarget,MotionVector>();
        public MotionVector EntryOffset(NativeTarget contact)
        {MotionVector value;return entryOffsets.TryGetValue(contact,out value)?value:new MotionVector();}
        public void SetEntryOffset(NativeTarget contact,MotionVector offset)
        {
            if(!ActionCalibration.Bounded(offset,.5))throw new ArgumentException("入口偏移须在 ±500 mm 内");
            if(offset.Length<1e-9)entryOffsets.Remove(contact);else entryOffsets[contact]=offset;
        }
        internal void ClearEntryOffsets(){entryOffsets.Clear();}
        private void RememberCalibration(){calibrations[CalibrationSource]=Calibration;rotations[CalibrationSource]=Rotation;}
        public void SelectCalibration(ActorSlot slot)
        {
            if(slot==CalibrationSource)return;RememberCalibration();CalibrationSource=slot;
            calibrations.TryGetValue(slot,out Calibration);rotations.TryGetValue(slot,out Rotation);
        }
        public void ClearCalibrations(){Calibration=null;Rotation=null;calibrations.Clear();rotations.Clear();CalibrationSource=Source;ReferenceReady=false;}
        public static ActionProfile From(ActionDefinition d)
        {return new ActionProfile{Key=d.Key,Name=d.Name,Source=d.Source,WeakSource=d.WeakSource,CalibrationSource=d.Source,Target=d.Target,Effector=d.Effector,Contact=d.Contact,Landmark=d.Landmark,Resource=d.Resource,CenteredMotion=d.CenteredMotion,Mode=d.CenteredMotion?AxisMappingMode.Relative:AxisMappingMode.Soft};}
        public static string Folder(string root,string key)
        {
            if(string.IsNullOrEmpty(key)||key.IndexOfAny(Path.GetInvalidFileNameChars())>=0||key=="."||key=="..")throw new ArgumentException("Invalid action key");
            return Path.Combine(Path.Combine(root,"Actions"),key);
        }
        public void Save(string root)
        {
            if(ManualBinding||!WeakSource.HasValue)CalibrationSource=Source;
            RememberCalibration();
            var buffer=new StringBuilder();
            using(var xml=XmlWriter.Create(buffer,new XmlWriterSettings{OmitXmlDeclaration=true,Indent=true}))
            {
                xml.WriteStartElement("action");Write(xml,"schema",2);Write(xml,"solver",MainGameVersion.Solver);Write(xml,"key",Key);Write(xml,"name",Name);
                Write(xml,"source",Source);Write(xml,"target",Target);Write(xml,"effector",Effector);Write(xml,"contact",Contact);
                Write(xml,"landmark",Landmark);Write(xml,"resource",Resource);
                if(WeakSource.HasValue)Write(xml,"weakSource",WeakSource.Value);
                Write(xml,"mode",Mode);Write(xml,"manualBinding",ManualBinding);Write(xml,"gameLength",GameLength);
                Write(xml,"centeredMotion",CenteredMotion);Write(xml,"referenceReady",ReferenceReady);
                ActionCalibration baseCalibration;ActionRotationReference baseRotation;calibrations.TryGetValue(Source,out baseCalibration);rotations.TryGetValue(Source,out baseRotation);
                if(baseCalibration!=null)baseCalibration.Write(xml);if(baseRotation!=null)baseRotation.Write(xml);
                foreach(ActorSlot slot in Enum.GetValues(typeof(ActorSlot)))
                {
                    if(slot==Source)continue;ActionCalibration c;ActionRotationReference r;calibrations.TryGetValue(slot,out c);rotations.TryGetValue(slot,out r);if(c==null&&r==null)continue;
                    xml.WriteStartElement("sourceCalibration");Write(xml,"source",slot);if(c!=null)c.Write(xml);if(r!=null)r.Write(xml);xml.WriteEndElement();
                }
                foreach(var entry in entryOffsets)
                {xml.WriteStartElement("entryOffset");Write(xml,"contact",entry.Key);ActionCalibration.WriteVector(xml,"offset",entry.Value);xml.WriteEndElement();}
                xml.WriteEndElement();
            }
            string path=Path.Combine(Folder(root,Key),"action.xml");Directory.CreateDirectory(Path.GetDirectoryName(path));AtomicWrite(path,buffer.ToString());
        }
        public static ActionProfile Load(string root,ActionDefinition fallback)
        {
            string path=Path.Combine(Folder(root,fallback.Key),"action.xml");if(!File.Exists(path))return From(fallback);
            var doc=new XmlDocument();using(var reader=XmlReader.Create(path,new XmlReaderSettings{XmlResolver=null}))doc.Load(reader);
            var n=doc.DocumentElement;
            bool legacy=n.GetAttribute("schema")=="1";
            if(n.Name!="action"||!legacy&&n.GetAttribute("schema")!="2"||n.GetAttribute("key")!=fallback.Key)throw new FormatException("Invalid action profile");
            var p=new ActionProfile{Key=fallback.Key,Name=n.HasAttribute("name")?n.GetAttribute("name"):fallback.Name,Source=ReadEnum<ActorSlot>(n,"source"),Target=ReadEnum<ActorSlot>(n,"target"),Effector=ReadEnum<ActionSource>(n,"effector"),Contact=ReadEnum<NativeTarget>(n,"contact"),Mode=ReadEnum<AxisMappingMode>(n,"mode"),ManualBinding=n.GetAttribute("manualBinding")=="True",GameLength=double.Parse(n.GetAttribute("gameLength"),CultureInfo.InvariantCulture),Calibration=ActionCalibration.Read(n.SelectSingleNode("calibration") as XmlElement,legacy),Rotation=ActionRotationReference.Read(n.SelectSingleNode("rotationReference") as XmlElement)};
            if(!StrokeMapping.ValidLength(p.GameLength))throw new FormatException("Invalid action interaction length");
            p.CenteredMotion=n.GetAttribute("centeredMotion")=="True";p.ReferenceReady=n.GetAttribute("referenceReady")=="True";
            p.Landmark=n.HasAttribute("landmark")?ReadEnum<ActionLandmark>(n,"landmark"):ActionLandmark.Anatomical;p.Resource=fallback.Resource;
            p.WeakSource=n.HasAttribute("weakSource")?ReadEnum<ActorSlot>(n,"weakSource"):fallback.WeakSource;p.CalibrationSource=p.Source;
            foreach(XmlElement child in n.SelectNodes("sourceCalibration"))
            {var slot=ReadEnum<ActorSlot>(child,"source");p.calibrations[slot]=ActionCalibration.Read(child.SelectSingleNode("calibration") as XmlElement,legacy);p.rotations[slot]=ActionRotationReference.Read(child.SelectSingleNode("rotationReference") as XmlElement);}
            foreach(XmlElement child in n.SelectNodes("entryOffset"))
                p.SetEntryOffset(ReadEnum<NativeTarget>(child,"contact"),ActionCalibration.ReadVector(child,"offset"));
            if(legacy&&(p.Mode==AxisMappingMode.Manual||p.Effector!=ActionSource.Shaft||p.CenteredMotion))
            {
                RestoreLegacyEndpoint(p.Calibration);
                foreach(var c in p.calibrations.Values)RestoreLegacyEndpoint(c);
            }
            if(!p.ManualBinding)
            {
                bool sourceChanged=p.Source!=fallback.Source||p.Effector!=fallback.Effector;
                bool targetChanged=p.Target!=fallback.Target||p.Contact!=fallback.Contact||p.Landmark!=fallback.Landmark;
                p.Source=fallback.Source;p.Target=fallback.Target;p.Effector=fallback.Effector;p.Contact=fallback.Contact;p.Landmark=fallback.Landmark;
                if(fallback.CenteredMotion&&!p.CenteredMotion&&p.Mode==AxisMappingMode.Soft)p.Mode=AxisMappingMode.Relative;
                p.CenteredMotion=fallback.CenteredMotion;
                if(sourceChanged)p.ClearCalibrations();
                if(targetChanged){p.ClearEntryOffsets();if(p.Calibration!=null)p.Calibration.VirtualAnchor=null;}
                if(sourceChanged||targetChanged){p.Rotation=null;p.ReferenceReady=false;}
            }
            return p;
        }
        private static void RestoreLegacyEndpoint(ActionCalibration calibration)
        {
            if(calibration!=null&&!calibration.ManualEnd.HasValue&&calibration.Length>0)
                calibration.ManualEnd=calibration.Origin+new MotionVector(calibration.Length,0,0);
        }
        private static T ReadEnum<T>(XmlElement n,string key)
        {object v=Enum.Parse(typeof(T),n.GetAttribute(key));if(!Enum.IsDefined(typeof(T),v))throw new FormatException("Invalid action "+key);return (T)v;}
        private static void Write(XmlWriter xml,string key,object value)
        {xml.WriteAttributeString(key,Convert.ToString(value,CultureInfo.InvariantCulture));}
        internal static void AtomicWrite(string path,string text)
        {File.WriteAllText(path+".tmp",text,new UTF8Encoding(false));if(File.Exists(path))File.Replace(path+".tmp",path,null);else File.Move(path+".tmp",path);}
    }

    // Animator progress, not a fabricated Timeline, determines capture boundaries.
    public sealed class CycleCapture
    {
        public static double ResolveDuration(double observed,double clipDuration)
        {return Numbers.Finite(observed)&&observed>.0001?observed:Numbers.Finite(clipDuration)&&clipDuration>.0001?clipDuration:0;}
        public bool Waiting {get;private set;}
        public bool Capturing {get;private set;}
        public double LastProgress {get;private set;}
        public void Arm(){Waiting=true;Capturing=false;LastProgress=double.NaN;}
        public void Reset(){Waiting=Capturing=false;LastProgress=double.NaN;}
        public CaptureStep Observe(double progress,bool loop,bool playing,bool transition)
        {
            if(!Numbers.Finite(progress)||progress<0||!playing||transition)return CaptureStep.Wait;
            double phase=loop?progress-Math.Floor(progress):Math.Min(1,progress);
            if(!double.IsNaN(LastProgress)&&progress<LastProgress-.01)
            {Arm();LastProgress=progress;return CaptureStep.Discontinuity;}
            bool wrapped=!double.IsNaN(LastProgress)&&Math.Floor(progress)>Math.Floor(LastProgress);
            double previous=LastProgress;LastProgress=progress;
            if(Waiting)
            {
                if(!loop||wrapped||phase<.02){Waiting=false;Capturing=true;return CaptureStep.Begin;}
                return CaptureStep.Wait;
            }
            if(Capturing&&loop&&wrapped){Capturing=false;return CaptureStep.Complete;}
            if(Capturing&&!loop&&phase>=.999){Capturing=false;return CaptureStep.Complete;}
            if(Capturing&&(double.IsNaN(previous)||progress>previous+1e-7))return CaptureStep.Sample;
            return CaptureStep.Wait;
        }
    }
    public enum CaptureStep { Wait,Begin,Sample,Complete,Discontinuity }
}
