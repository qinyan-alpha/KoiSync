using System;
using BepInEx.Configuration;
using KoiSync.Core;
using LitJson;

namespace KoiSync.Plugin
{
    // Settings that belong to every animation: the travel boost, the pull-out ceiling, the idle warm-up,
    // the climax pull-in and the six global axis limits. The per-animation profile keeps only what is
    // genuinely per animation: its measured stroke memory, its alignment values and its own axis
    // overrides. Those always win over the global values.
    internal sealed class GlobalSettings
    {
        private readonly ConfigFile config;
        private readonly ConfigEntry<string> profile;
        internal readonly ConfigEntry<float>[] AxisMinimum=new ConfigEntry<float>[6],AxisMaximum=new ConfigEntry<float>[6],AxisGain=new ConfigEntry<float>[6];
        internal NativeStrokeProfile Stroke=new NativeStrokeProfile();
        internal string Status="全局行程配置";
        internal GlobalSettings(ConfigFile config)
        {
            this.config=config;
            profile=config.Bind("Transitions","Profile","","Global travel boost, pull-out, idle and climax settings (JSON)");
            for(int i=0;i<6;i++)
            {
                string axis=MotionPipeline.Axes[i];
                AxisMinimum[i]=config.Bind("Global Axes",axis+"Min",0f,new ConfigDescription("Global "+axis+" lower limit for every animation",new AcceptableValueRange<float>(0,1)));
                AxisMaximum[i]=config.Bind("Global Axes",axis+"Max",1f,new ConfigDescription("Global "+axis+" upper limit for every animation",new AcceptableValueRange<float>(0,1)));
                AxisGain[i]=config.Bind("Global Axes",axis+"Gain",1f,new ConfigDescription("Global "+axis+" multiplier for every animation",new AcceptableValueRange<float>(0,20)));
            }
            Reload();
        }
        internal string File{get{return config.ConfigFilePath;}}
        internal void Reload()
        {
            try
            {
                NativeStrokeProfile loaded=string.IsNullOrEmpty(profile.Value)?new NativeStrokeProfile():JsonMapper.ToObject<NativeStrokeProfile>(profile.Value);
                if(loaded==null)throw new ArgumentException("内容为空");
                loaded.Memorized.Clear();loaded.Validate();Stroke=loaded;Status="已载入全局行程配置";
            }
            catch(Exception ex){Stroke=new NativeStrokeProfile();Status="全局行程配置读取失败，已使用默认值："+ex.GetBaseException().Message;}
        }
        internal void Save()
        {
            Stroke.Validate();profile.Value=JsonMapper.ToJson(Stroke);config.Save();
        }
        internal void ResetStroke(){Stroke=new NativeStrokeProfile();Save();}
        internal void ResetAxes()
        {
            for(int i=0;i<6;i++){AxisMinimum[i].Value=0;AxisMaximum[i].Value=1;AxisGain[i].Value=1;}
            config.Save();
        }
        // The animation keeps its own measurements and gains the global configuration everywhere else.
        internal NativeStrokeProfile ForAnimation(NativeStrokeProfile target)
        {
            NativeStrokeProfile copy=Stroke.Clone();
            copy.Memorized=target==null||target.Memorized==null?new System.Collections.Generic.List<StrokeMemory>():target.Memorized;
            return copy;
        }
        internal void ApplyTo(MotionPipeline pipeline)
        {
            if(pipeline==null)return;
            for(int i=0;i<6;i++)
            {
                float low=AxisMinimum[i].Value,high=Math.Max(low,AxisMaximum[i].Value);
                pipeline.AxisMinimum[i]=low;pipeline.AxisMaximum[i]=high;pipeline.AxisGain[i]=AxisGain[i].Value;
            }
        }
        internal bool AxisTouched(int axis)
        {return AxisMinimum[axis].Value>0||AxisMaximum[axis].Value<1||Math.Abs(AxisGain[axis].Value-1)>1e-6;}
    }
}
