using System;
using System.Globalization;
using System.Xml;
using KoiSync.Core;

namespace KoiSync.Studio
{
    public sealed class ChapterStrokeCalibration
    {
        public double GameLength,BodyHeight;
        public bool FromHeight=true,FromFinger;
        public ChapterStrokeCalibration Copy(){return new ChapterStrokeCalibration{GameLength=GameLength,BodyHeight=BodyHeight,FromHeight=FromHeight,FromFinger=FromFinger};}
        public static ChapterStrokeCalibration FromBody(double height)
        {
            // A scene-scale preset: 130 mm at a 1.70 m skeleton. This is not a
            // biological prediction from height. Manual chapter values win.
            double h=Numbers.Finite(height)&&height>=.5&&height<=5?height:1.70;
            return new ChapterStrokeCalibration{GameLength=ShaftStrokeMapping.FromBodyHeight(h),BodyHeight=height==h?h:0};
        }
        public static bool Equivalent(ChapterStrokeCalibration a,ChapterStrokeCalibration b)
        {return a==null||b==null||Math.Abs(a.GameLength-b.GameLength)<1e-8&&Math.Abs(a.BodyHeight-b.BodyHeight)<1e-8&&a.FromHeight==b.FromHeight&&a.FromFinger==b.FromFinger;}
        public void Write(XmlWriter xml)
        {
            Validate();xml.WriteStartElement("strokeCalibration");xml.WriteAttributeString("gameLength",GameLength.ToString("R",CultureInfo.InvariantCulture));
            xml.WriteAttributeString("bodyHeight",BodyHeight.ToString("R",CultureInfo.InvariantCulture));xml.WriteAttributeString("fromHeight",FromHeight?"true":"false");if(FromFinger)xml.WriteAttributeString("fromFinger","true");xml.WriteEndElement();
        }
        public static ChapterStrokeCalibration Read(XmlElement node)
        {
            if(node==null)return null;
            var c=new ChapterStrokeCalibration{GameLength=double.Parse(node.GetAttribute("gameLength"),CultureInfo.InvariantCulture),BodyHeight=double.Parse(node.GetAttribute("bodyHeight"),CultureInfo.InvariantCulture),FromHeight=node.GetAttribute("fromHeight")!="false",FromFinger=node.GetAttribute("fromFinger")=="true"};c.Validate();return c;
        }
        public void Validate()
        {if(!Numbers.Finite(GameLength)||GameLength<.02||GameLength>.5||!Numbers.Finite(BodyHeight)||BodyHeight<0||BodyHeight>5)throw new ArgumentException("Invalid chapter stroke calibration");}
    }
    public static class StrokeMapping
    {
        public const double ReferenceLength=ShaftStrokeMapping.ReferenceLength;
        public const double ReferenceStroke=ShaftStrokeMapping.ReferenceStroke;
        public static bool ValidLength(double value){return ShaftStrokeMapping.ValidLength(value);}
        public static bool ValidModelLength(double value){return ShaftStrokeMapping.ValidModelLength(value);}
        public static double Depth(NativeMotionResult pose,double player,double game,double stroke,AxisMappingMode mode)
        {return ShaftStrokeMapping.Depth(pose,player,game,stroke,(DriveMappingMode)mode);}
        public static double SoftDepth(double depth,double limit){return ShaftStrokeMapping.SoftDepth(depth,limit);}
    }
}
