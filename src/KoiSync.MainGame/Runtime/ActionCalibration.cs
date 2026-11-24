using System;
using System.Globalization;
using System.Xml;
using KoiSync.Core;

namespace KoiSync.MainGame
{
    public sealed class ActionCalibration
    {
        // Zero captures the source model's fixed length at binding. The
        // effective manual length is always calculated from the two points.
        public double Length,L1=.08,L2=.08;
        public MotionVector Origin,PlaneDegrees;
        public MotionVector? ManualEnd;
        public bool Linked=true;
        internal string VirtualAnchor;
        internal AlignmentFrame VirtualFrame;

        public ActionCalibration Copy(){return (ActionCalibration)MemberwiseClone();}
        public double Span(double fixedLength)
        {return ManualEnd.HasValue?ManualCalibrationMotion.Length(Origin,ManualEnd.Value):fixedLength;}

        internal void SetEndpoints(MotionVector origin,MotionVector end,double previousSpan)
        {
            ScaleRanges(previousSpan,ManualCalibrationMotion.Length(origin,end));
            Origin=origin;ManualEnd=end;
        }

        internal void ScaleRanges(double previous,double next)
        {
            if(!Linked||previous<=0||Math.Abs(previous-next)<1e-9)return;
            L1=Numbers.Clamp(L1*next/previous,.001,.5);
            L2=Numbers.Clamp(L2*next/previous,.001,.5);
        }

        public static ActionCalibration Read(XmlElement node){return Read(node,false);}
        internal static ActionCalibration Read(XmlElement node,bool legacy)
        {
            if(node==null)return null;
            var c=new ActionCalibration{
                Length=Number(node,"length"),L1=Number(node,"l1"),L2=Number(node,"l2"),
                Origin=new MotionVector(Number(node,"axial"),Number(node,"originL1"),Number(node,"originL2")),
                Linked=node.GetAttribute("linked")!="false"
            };
            if(node.HasAttribute("endAxial"))
            {
                c.ManualEnd=new MotionVector(Number(node,"endAxial"),Number(node,"endL1"),Number(node,"endL2"));
                // Schema 1's length mirrors the endpoint distance when both
                // points exist. Let the model supply its own automatic length.
                if(legacy)c.Length=0;
            }
            if(node.HasAttribute("planeX"))c.PlaneDegrees=ReadVector(node,"plane");
            var source=node.SelectSingleNode("virtualSource") as XmlElement;
            if(source!=null)
            {
                c.VirtualAnchor=source.GetAttribute("anchor");
                c.VirtualFrame=AlignmentFrame.Build(ReadVector(source,"origin"),ReadVector(source,"right"),ReadVector(source,"axis"));
            }
            c.Validate();return c;
        }

        public void Validate()
        {
            if(Length!=0&&!ShaftStrokeMapping.ValidModelLength(Length)||!ValidRange(L1)||!ValidRange(L2)||
               !Bounded(Origin,3)||!Bounded(PlaneDegrees,180)||Math.Abs(PlaneDegrees.Y)>90||Math.Abs(PlaneDegrees.Z)>90)
                throw new ArgumentException("当前动作的标尺、范围或平面角度无效");
            if(ManualEnd.HasValue)ManualCalibrationMotion.Length(Origin,ManualEnd.Value);
        }

        public void Write(XmlWriter xml)
        {
            Validate();xml.WriteStartElement("calibration");
            WriteNumber(xml,"length",Length);WriteNumber(xml,"l1",L1);WriteNumber(xml,"l2",L2);
            WriteNumber(xml,"axial",Origin.X);WriteNumber(xml,"originL1",Origin.Y);WriteNumber(xml,"originL2",Origin.Z);
            if(ManualEnd.HasValue)
            {
                var end=ManualEnd.Value;
                WriteNumber(xml,"endAxial",end.X);WriteNumber(xml,"endL1",end.Y);WriteNumber(xml,"endL2",end.Z);
            }
            if(PlaneDegrees.Length>0)WriteVector(xml,"plane",PlaneDegrees);
            xml.WriteAttributeString("linked",Linked?"true":"false");
            if(!string.IsNullOrEmpty(VirtualAnchor))
            {
                xml.WriteStartElement("virtualSource");xml.WriteAttributeString("anchor",VirtualAnchor);
                WriteVector(xml,"origin",VirtualFrame.Origin);WriteVector(xml,"right",VirtualFrame.X);WriteVector(xml,"axis",VirtualFrame.Y);
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }

        internal static bool Bounded(MotionVector value,double limit)
        {return Math.Abs(value.X)<=limit&&Math.Abs(value.Y)<=limit&&Math.Abs(value.Z)<=limit;}
        private static bool ValidRange(double value){return value>=.001&&value<=.5;}
        private static double Number(XmlElement node,string name){return double.Parse(node.GetAttribute(name),CultureInfo.InvariantCulture);}
        internal static MotionVector ReadVector(XmlElement node,string name)
        {return new MotionVector(Number(node,name+"X"),Number(node,name+"Y"),Number(node,name+"Z"));}
        private static void WriteNumber(XmlWriter xml,string name,double value)
        {xml.WriteAttributeString(name,value.ToString("R",CultureInfo.InvariantCulture));}
        internal static void WriteVector(XmlWriter xml,string name,MotionVector value)
        {WriteNumber(xml,name+"X",value.X);WriteNumber(xml,name+"Y",value.Y);WriteNumber(xml,name+"Z",value.Z);}
    }
}
