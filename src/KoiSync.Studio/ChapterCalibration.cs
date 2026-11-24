using System;
using System.Globalization;
using System.Xml;
using KoiSync.Core;

namespace KoiSync.Studio
{
    public sealed class ChapterCalibration
    {
        public double Length,L1,L2,R0Plane,R1Plane,R2Plane;
        public MotionVector Origin;
        public MotionVector? ManualEnd;
        public bool Linked=true;
        public ChapterCalibration Copy(){return new ChapterCalibration{Length=Length,L1=L1,L2=L2,R0Plane=R0Plane,R1Plane=R1Plane,R2Plane=R2Plane,Origin=Origin,ManualEnd=ManualEnd,Linked=Linked};}
        private static double Number(XmlElement node,string name){return double.Parse(node.GetAttribute(name),CultureInfo.InvariantCulture);}
        public static ChapterCalibration Read(XmlElement node)
        {
            if(node==null)return null;
            var c=new ChapterCalibration{Length=Number(node,"length"),L1=Number(node,"l1"),L2=Number(node,"l2"),
                Origin=new MotionVector(Number(node,"axial"),Number(node,"originL1"),Number(node,"originL2")),Linked=node.GetAttribute("linked")!="false"};
            if(node.HasAttribute("r0Plane"))c.R0Plane=Number(node,"r0Plane");
            if(node.HasAttribute("r1Plane"))c.R1Plane=Number(node,"r1Plane");
            if(node.HasAttribute("r2Plane"))c.R2Plane=Number(node,"r2Plane");
            if(node.HasAttribute("endAxial"))c.ManualEnd=new MotionVector(Number(node,"endAxial"),Number(node,"endL1"),Number(node,"endL2"));
            c.Validate();return c;
        }
        public void Validate()
        {
            if(!Numbers.Finite(Length)||Length<ManualCalibrationMotion.MinimumLength||Length>.5||!Numbers.Finite(L1)||L1<.001||L1>.5||!Numbers.Finite(L2)||L2<.001||L2>.5||
               !Numbers.Finite(R0Plane)||R0Plane< -180||R0Plane>180||!Numbers.Finite(R1Plane)||R1Plane< -90||R1Plane>90||!Numbers.Finite(R2Plane)||R2Plane< -90||R2Plane>90||
               !Numbers.Finite(Origin.X)||!Numbers.Finite(Origin.Y)||!Numbers.Finite(Origin.Z)||Math.Abs(Origin.X)>.5||Math.Abs(Origin.Y)>.5||Math.Abs(Origin.Z)>.5)
                throw new ArgumentException("Invalid chapter calibration");
            if(ManualEnd.HasValue&&Math.Abs(ManualCalibrationMotion.Length(Origin,ManualEnd.Value)-Length)>1e-7)
                throw new ArgumentException("Manual endpoints do not match chapter length");
        }
        public void Write(XmlWriter xml)
        {
            Validate();xml.WriteStartElement("calibration");
            var names=new[]{"length","l1","l2","axial","originL1","originL2"};var values=new[]{Length,L1,L2,Origin.X,Origin.Y,Origin.Z};
            for(int i=0;i<names.Length;i++)xml.WriteAttributeString(names[i],values[i].ToString("R",CultureInfo.InvariantCulture));
            if(Math.Abs(R0Plane)>1e-12)xml.WriteAttributeString("r0Plane",R0Plane.ToString("R",CultureInfo.InvariantCulture));
            if(Math.Abs(R1Plane)>1e-12)xml.WriteAttributeString("r1Plane",R1Plane.ToString("R",CultureInfo.InvariantCulture));
            if(Math.Abs(R2Plane)>1e-12)xml.WriteAttributeString("r2Plane",R2Plane.ToString("R",CultureInfo.InvariantCulture));
            if(ManualEnd.HasValue)
            {
                var end=ManualEnd.Value;
                xml.WriteAttributeString("endAxial",end.X.ToString("R",CultureInfo.InvariantCulture));
                xml.WriteAttributeString("endL1",end.Y.ToString("R",CultureInfo.InvariantCulture));
                xml.WriteAttributeString("endL2",end.Z.ToString("R",CultureInfo.InvariantCulture));
            }
            xml.WriteAttributeString("linked",Linked?"true":"false");xml.WriteEndElement();
        }
    }
}
