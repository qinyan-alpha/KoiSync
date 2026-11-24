using System;
using System.Collections.Generic;

namespace KoiSync.Core
{
    public sealed class MotionCurve
    {
        public const double FrameRate=60;
        public sealed class Point{public double Time;public double[] Values;public string Source;}
        private readonly SortedDictionary<int,Point> points=new SortedDictionary<int,Point>();
        public int Revision{get;private set;}
        public int Count{get{return points.Count;}}
        public IEnumerable<Point> Points{get{return points.Values;}}
        public void Clear(){points.Clear();Revision++;}
        public void Add(double time,double duration,double[] values,string source)
        {
            if(!Numbers.Finite(time)||time<0||time>duration+.001||values==null||values.Length!=6)return;
            bool valid=false;foreach(double value in values){if(double.IsInfinity(value))return;if(Numbers.Finite(value))valid=true;}if(!valid)return;
            int frame=(int)Math.Round(time*FrameRate);points[frame]=new Point{Time=frame/FrameRate,Values=(double[])values.Clone(),Source=source};Revision++;
        }
        public Point At(double time)
        {Point point;return points.TryGetValue((int)Math.Round(time*FrameRate),out point)?point:null;}
    }
}
