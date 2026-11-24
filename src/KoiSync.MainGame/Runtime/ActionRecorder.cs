// Main-game snapshot of the 0.2.24 geometry implementation; independent of Studio development.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KoiSync.Core;

namespace KoiSync.MainGame
{
    public sealed class ActionRecorder
    {
        private readonly List<long> times=new List<long>();
        private readonly List<int[]> values=new List<int[]>();
        private double requestedStart=-1;
        private AxisOscillation previousPulse;
        public int Count{get{return times.Count;}}
        public double Start{get{return Count==0?0:times[0]/1000.0;}}
        public double End{get{return Count==0?0:times[Count-1]/1000.0;}}
        public void Clear(){times.Clear();values.Clear();requestedStart=-1;previousPulse=null;}
        public void Begin(double time){Clear();requestedStart=time;}
        public void ExtendEnd(double time)
        {
            long at=(long)Math.Round(time*1000);
            if(Count>0&&at>times[Count-1]){times.Add(at);values.Add((int[])values[values.Count-1].Clone());}
        }
        // Capture the final device values once per 20 ms of Timeline time.
        public bool Add(double time,double[] pose)
        {return Add(time,pose,null);}
        public bool Add(double time,double[] pose,AxisOscillation pulse)
        {
            if(!Numbers.Finite(time)||time<0||pose==null||pose.Length!=6)throw new ArgumentException("Invalid recording frame");
            long at=(long)Math.Round(time*1000);
            if(Count>0&&at<times[Count-1]-2)return false;
            if(Count>0&&at<=times[Count-1])return true;
            if(Count>0&&at-times[Count-1]<20&&pulse==null&&previousPulse==null)return true;
            var row=new int[6];for(int i=0;i<6;i++){if(!Numbers.Finite(pose[i]))throw new ArgumentException("Invalid recording axis");row[i]=(int)Math.Round(Numbers.Clamp(pose[i],0,1)*100);}
            if(pulse!=null)
            {
                // The Timeline getter may advance between solving and capture.
                // Derive this authored axis at its recorded time, and let an
                // exact turn win when both samples round to the same millisecond.
                row[0]=PulsePosition(pulse,time,at);
                if(Count>0)
                {
                    double turn=NearestTurn(pulse,times[Count-1]);
                    if(turn>=pulse.Start&&turn<=pulse.End&&(long)Math.Round(turn*1000)==times[Count-1])
                        values[Count-1][0]=(int)Math.Round(pulse.Value(turn)*100);
                }
            }
            if(Count==0&&requestedStart>=0&&requestedStart<time)
            {
                var initial=(int[])row.Clone();
                if(pulse!=null&&requestedStart>=pulse.Start&&requestedStart<=pulse.End)initial[0]=(int)Math.Round(pulse.Value(requestedStart)*100);
                times.Add((long)Math.Round(requestedStart*1000));values.Add(initial);
            }
            if(Count>0)
            {
                long from=times[Count-1];int[] before=values[Count-1];
                InsertTurns(previousPulse,from,at,before,row);
                if(pulse!=null&&!pulse.Same(previousPulse))InsertTurns(pulse,from,at,before,row);
            }
            Append(at,row);previousPulse=pulse;return true;
        }
        private static double NearestTurn(AxisOscillation pulse,long at)
        {return pulse.Start+Math.Round((at/1000.0-pulse.Start)*pulse.Hertz*2)/(pulse.Hertz*2);}
        private static int PulsePosition(AxisOscillation pulse,double time,long at)
        {
            double turn=NearestTurn(pulse,at);
            return (int)Math.Round(pulse.Value(turn>=pulse.Start&&turn<=pulse.End&&(long)Math.Round(turn*1000)==at?turn:time)*100);
        }
        private void InsertTurns(AxisOscillation pulse,long from,long to,int[] before,int[] after)
        {
            if(pulse==null||to<=from||pulse.End*1000<from||pulse.Start*1000>to)return;
            double start=Math.Max(from/1000.0,pulse.Start),end=Math.Min(to/1000.0,pulse.End);
            if(start>from/1000.0)InsertPulsePoint(start,pulse,from,to,before,after);
            for(double turn=pulse.NextTurn(start);turn<end-1e-9;turn=pulse.NextTurn(turn))InsertPulsePoint(turn,pulse,from,to,before,after);
            if(end<to/1000.0)InsertPulsePoint(end,pulse,from,to,before,after);
        }
        private void InsertPulsePoint(double time,AxisOscillation pulse,long from,long to,int[] before,int[] after)
        {
            long at=(long)Math.Round(time*1000);if(at<=times[Count-1]||at>=to)return;
            double ratio=(at-from)/(double)(to-from);var row=new int[6];
            for(int i=1;i<6;i++)row[i]=(int)Math.Round(before[i]+(after[i]-before[i])*ratio);
            row[0]=(int)Math.Round(pulse.Value(time)*100);Append(at,row);
        }
        private void Append(long at,int[] row)
        {if(Count>0&&at==times[Count-1])values[Count-1]=row;else{times.Add(at);values.Add(row);}}
        public void Save(string stem)
        {
            if(Count<2)throw new InvalidOperationException("Recording needs at least two frames");
            Directory.CreateDirectory(Path.GetDirectoryName(stem));
            var suffix=new[]{"",".surge",".sway",".twist",".roll",".pitch"};
            for(int axis=0;axis<6;axis++)
            {
                var text=new StringBuilder("{\"version\":\"1.0\",\"inverted\":false,\"range\":100,\"actions\":[");
                for(int i=0;i<Count;i++)
                {
                    if(i>0)text.Append(',');
                    text.Append("{\"at\":").Append(times[i].ToString(CultureInfo.InvariantCulture)).Append(",\"pos\":").Append(values[i][axis].ToString(CultureInfo.InvariantCulture)).Append('}');
                }
                text.Append("]}");string path=stem+suffix[axis]+".funscript";
                File.WriteAllText(path+".tmp",text.ToString(),new UTF8Encoding(false));
            }
            foreach(var name in suffix){string path=stem+name+".funscript";if(File.Exists(path))File.Replace(path+".tmp",path,null);else File.Move(path+".tmp",path);}
        }
    }
}
