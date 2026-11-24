using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using KoiSync.Core;

namespace KoiSync.Studio
{
    // User-authored offsets, keyed by scene actor ID. No sampled pose history.
    internal sealed class ModelOrigins
    {
        private readonly Dictionary<int,MotionVector> values=new Dictionary<int,MotionVector>();
        internal MotionVector Get(int actor){MotionVector value;return values.TryGetValue(actor,out value)?value:new MotionVector();}
        internal void Set(int actor,MotionVector offset)
        {
            if(actor<0||!Valid(offset.X)||!Valid(offset.Y)||!Valid(offset.Z))throw new ArgumentException("Invalid model origin offset");
            if(offset.Length<1e-9)values.Remove(actor);else values[actor]=offset;
        }
        private static bool Valid(double value){return Numbers.Finite(value)&&Math.Abs(value)<=.5;}
        internal void Clear(){values.Clear();}
        internal string ToXml()
        {
            var text=new StringBuilder();
            using(var xml=XmlWriter.Create(text,new XmlWriterSettings{OmitXmlDeclaration=true}))
            {
                xml.WriteStartElement("origins");
                foreach(var row in values)
                {
                    xml.WriteStartElement("origin");xml.WriteAttributeString("actor",row.Key.ToString(CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("axial",row.Value.X.ToString("R",CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("l1",row.Value.Y.ToString("R",CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("l2",row.Value.Z.ToString("R",CultureInfo.InvariantCulture));xml.WriteEndElement();
                }
                xml.WriteEndElement();
            }
            return text.ToString();
        }
        internal void FromXml(string text)
        {
            var parsed=new ModelOrigins();var document=new XmlDocument();
            using(var reader=XmlReader.Create(new StringReader(text),new XmlReaderSettings{ProhibitDtd=true,XmlResolver=null}))document.Load(reader);
            foreach(XmlElement node in document.SelectNodes("/origins/origin"))
                parsed.Set(int.Parse(node.GetAttribute("actor"),CultureInfo.InvariantCulture),new MotionVector(
                    double.Parse(node.GetAttribute("axial"),CultureInfo.InvariantCulture),double.Parse(node.GetAttribute("l1"),CultureInfo.InvariantCulture),double.Parse(node.GetAttribute("l2"),CultureInfo.InvariantCulture)));
            values.Clear();foreach(var row in parsed.values)values.Add(row.Key,row.Value);
        }
    }
}
