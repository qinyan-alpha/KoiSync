using System;
using System.Globalization;
using System.Xml;
using KoiSync.Core;

namespace KoiSync.Studio
{
    // A fixed pose per chapter, never a running average. Saving the pose makes
    // playback, seeking and recording independent of the order frames are read.
    public sealed class ChapterRotationReference
    {
        public double Time,Yaw,Roll,Pitch;
        public ChapterRotationReference Copy(){return new ChapterRotationReference{Time=Time,Yaw=Yaw,Roll=Roll,Pitch=Pitch};}
        public static bool CanCapture(NativeMotionResult measured)
        {return RotationReferenceMotion.CanCapture(measured);}
        public static ChapterRotationReference Capture(NativeMotionResult measured,double time)
        {
            if(!CanCapture(measured))throw new ArgumentException("A fully engaged pose is required for the rotation reference");
            var result=new ChapterRotationReference{Time=time,Yaw=measured.Raw[3],Roll=measured.Raw[4],Pitch=measured.Raw[5]};result.Validate();return result;
        }
        public double[] Apply(NativeMotionResult measured)
        {return Apply(measured,measured.Normalized);}
        public double[] Apply(NativeMotionResult measured,double[] normalized)
        {return RotationReferenceMotion.Apply(measured,normalized,Yaw,Roll,Pitch);}
        public static bool Equivalent(ChapterRotationReference a,ChapterRotationReference b)
        {return a==null||b==null||Math.Abs(a.Yaw-b.Yaw)<1e-8&&Math.Abs(a.Roll-b.Roll)<1e-8&&Math.Abs(a.Pitch-b.Pitch)<1e-8;}
        private void Validate()
        {
            if(!Numbers.Finite(Time)||Time<0||!Numbers.Finite(Yaw)||!Numbers.Finite(Roll)||!Numbers.Finite(Pitch)||Math.Abs(Yaw)>180.00001||Math.Abs(Roll)>180.00001||Math.Abs(Pitch)>90.00001)
                throw new ArgumentException("Invalid chapter rotation reference");
        }
        public void Write(XmlWriter xml)
        {
            Validate();xml.WriteStartElement("rotationReference");
            var names=new[]{"time","yaw","roll","pitch"};var values=new[]{Time,Yaw,Roll,Pitch};
            for(int i=0;i<names.Length;i++)xml.WriteAttributeString(names[i],values[i].ToString("R",CultureInfo.InvariantCulture));
            xml.WriteEndElement();
        }
        public static ChapterRotationReference Read(XmlElement node)
        {
            if(node==null)return null;
            var result=new ChapterRotationReference{Time=Number(node,"time"),Yaw=Number(node,"yaw"),Roll=Number(node,"roll"),Pitch=Number(node,"pitch")};result.Validate();return result;
        }
        private static double Number(XmlElement node,string key){return double.Parse(node.GetAttribute(key),CultureInfo.InvariantCulture);}
    }
}
