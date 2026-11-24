using System;
using System.Collections.Generic;
using System.IO;

namespace KoiSync.Core
{
    public sealed class FunscriptAction
    {
        public long At;
        public int Position;
    }

    // Immutable, sorted positions. Playback always samples the owner's clock, including seeks and loops.
    public sealed class Funscript
    {
        private readonly FunscriptAction[] actions;
        public readonly bool Inverted;
        public int Count{get{return actions.Length;}}
        public double Duration{get{return actions[actions.Length-1].At/1000.0;}}
        public Funscript(IEnumerable<FunscriptAction> input,bool inverted)
        {
            var sorted=new SortedDictionary<long,int>();
            foreach(var a in input)
            {
                if(a==null||a.At<0||a.Position<0||a.Position>100)throw new ArgumentException("脚本时间必须非负，位置必须为 0–100");
                sorted[a.At]=a.Position;
            }
            if(sorted.Count==0)throw new ArgumentException("脚本 actions 为空");
            actions=new FunscriptAction[sorted.Count];int i=0;
            foreach(var a in sorted)actions[i++]=new FunscriptAction{At=a.Key,Position=a.Value};
            Inverted=inverted;
        }
        public double TimeAt(int index){return actions[index].At/1000.0;}
        public double PositionAt(int index){return (Inverted?100-actions[index].Position:actions[index].Position)/100.0;}
        public bool TryPosition(double seconds,out double position)
        {
            position=double.NaN;
            if(!Numbers.Finite(seconds)||seconds<0||seconds>Duration)return false;
            if(seconds<=TimeAt(0)){position=PositionAt(0);return true;}
            int left=0,right=Count-1;
            while(left+1<right){int middle=left+(right-left)/2;if(TimeAt(middle)<=seconds)left=middle;else right=middle;}
            if(left==right){position=PositionAt(left);return true;}
            double fraction=(seconds-TimeAt(left))/(TimeAt(right)-TimeAt(left));
            position=PositionAt(left)+(PositionAt(right)-PositionAt(left))*fraction;return true;
        }
    }

    public sealed class FunscriptSet
    {
        private readonly Funscript[] tracks;
        public Funscript this[int axis]{get{return tracks[axis];}}
        public int Count{get{int count=0;foreach(var t in tracks)if(t!=null)count++;return count;}}
        public double Duration{get{double duration=0;foreach(var t in tracks)if(t!=null)duration=Math.Max(duration,t.Duration);return duration;}}
        public FunscriptSet(Funscript[] value)
        {if(value==null||value.Length!=6)throw new ArgumentException("脚本组需要六轴");tracks=(Funscript[])value.Clone();}
        public bool TrySample(double seconds,out double[] positions)
        {
            positions=new double[6];bool valid=false;
            for(int i=0;i<6;i++)
            {
                positions[i]=double.NaN;
                if(tracks[i]!=null&&tracks[i].TryPosition(seconds,out positions[i]))valid=true;
            }
            return valid;
        }
    }

    public static class FunscriptFiles
    {
        private static readonly string[] names={"stroke","surge","sway","twist","roll","pitch"};
        public static int Axis(string marker)
        {
            for(int i=0;i<6;i++)if(string.Equals(marker,names[i],StringComparison.OrdinalIgnoreCase)||string.Equals(marker,MotionPipeline.Axes[i],StringComparison.OrdinalIgnoreCase))return i;
            return -1;
        }
        private static string Stem(string path,out int axis,out int rank)
        {
            string name=Path.GetFileNameWithoutExtension(path);int dot=name.LastIndexOf('.');
            axis=dot<0?-1:Axis(name.Substring(dot+1));rank=0;
            if(axis>=0){rank=string.Equals(name.Substring(dot+1),MotionPipeline.Axes[axis],StringComparison.OrdinalIgnoreCase)?1:2;return name.Substring(0,dot);}
            axis=0;return name;
        }
        public static string[] Companions(string selected)
        {
            selected=Path.GetFullPath(selected);int selectedAxis,ignored;string stem=Stem(selected,out selectedAxis,out ignored);
            bool jsonSelected=string.Equals(Path.GetExtension(selected),".json",StringComparison.OrdinalIgnoreCase);
            var result=new string[6];var ranks=new[]{int.MaxValue,int.MaxValue,int.MaxValue,int.MaxValue,int.MaxValue,int.MaxValue};
            string[] files=Directory.GetFiles(Path.GetDirectoryName(selected));Array.Sort(files,StringComparer.OrdinalIgnoreCase);
            foreach(string file in files)
            {
                string extension=Path.GetExtension(file);bool script=string.Equals(extension,".funscript",StringComparison.OrdinalIgnoreCase);
                if(!script&&(!jsonSelected||!string.Equals(extension,".json",StringComparison.OrdinalIgnoreCase)))continue;
                int axis,rank;string other=Stem(file,out axis,out rank);if(!string.Equals(stem,other,StringComparison.OrdinalIgnoreCase))continue;
                if(!script)rank+=10;
                if(rank<ranks[axis]){result[axis]=file;ranks[axis]=rank;}
            }
            result[selectedAxis]=selected;return result;
        }
    }
}
