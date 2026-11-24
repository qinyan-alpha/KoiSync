using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using KoiSync.Core;

namespace KoiSync.Plugin
{
    internal sealed class NativeSettings
    {
        private readonly ConfigFile config;
        internal readonly ConfigEntry<NativeTarget> Override;
        internal readonly ConfigEntry<float>[] Minimum=new ConfigEntry<float>[6],Maximum=new ConfigEntry<float>[6];
        private Dictionary<string,NativeTarget> profiles;
        internal string MappingStatus="",CurrentAnimation="",CurrentProfile="",SamplingStatus="等待本体场景";
        internal string MappingPath{get{return Path.Combine(Paths.ConfigPath,"KoiSync.NativeAnimations.csv");}}
        internal NativeSettings(ConfigFile config)
        {
            this.config=config;
            Override=config.Bind("Native","TargetOverride",NativeTarget.AUTO,"AUTO uses the embedded animation table and optional user overrides.");
            for(int i=0;i<6;i++)
            {
                Minimum[i]=config.Bind("Native Limits",MotionPipeline.Axes[i]+"Min",0f,new ConfigDescription("Channel clamp minimum",new AcceptableValueRange<float>(0,1)));
                Maximum[i]=config.Bind("Native Limits",MotionPipeline.Axes[i]+"Max",1f,new ConfigDescription("Channel clamp maximum",new AcceptableValueRange<float>(0,1)));
            }
            Reload();
        }
        internal void Reload()
        {
            try
            {
                Dictionary<string,NativeTarget> next;
                using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("KoiSync.NativeAnimations.csv"))
                using(var reader=new StreamReader(stream,Encoding.UTF8))next=NativeProfiles.Read(reader);
                if(File.Exists(MappingPath))using(var reader=new StreamReader(MappingPath,Encoding.UTF8))foreach(var pair in NativeProfiles.Read(reader))next[pair.Key]=pair.Value;
                profiles=next;MappingStatus="已载入 "+profiles.Count+" 条动画映射";
            }
            catch(Exception ex){MappingStatus="映射加载失败："+ex.Message;if(profiles==null)profiles=new Dictionary<string,NativeTarget>();}
        }
        internal bool Resolve(string animation,out NativeTarget target)
        {
            CurrentAnimation=animation??"";
            if(Override.Value!=NativeTarget.AUTO){target=Override.Value;return true;}
            return profiles.TryGetValue(CurrentAnimation,out target);
        }
        internal void SaveCurrent()
        {
            if(CurrentAnimation.Length==0||Override.Value==NativeTarget.AUTO||Override.Value==NativeTarget.BOTH_FEET)throw new InvalidOperationException("先选择当前动画的有效测量配置");
            var rows=new Dictionary<string,NativeTarget>();
            if(File.Exists(MappingPath))using(var reader=new StreamReader(MappingPath,Encoding.UTF8))rows=NativeProfiles.Read(reader);
            rows[CurrentAnimation]=Override.Value;var text=new StringBuilder("# KoiSync: animation name, target profile\n");
            foreach(var row in rows)text.Append(row.Key).Append(',').Append(row.Value).Append('\n');
            File.WriteAllText(MappingPath,text.ToString(),Encoding.UTF8);Override.Value=NativeTarget.AUTO;config.Save();Reload();
        }
    }
}
