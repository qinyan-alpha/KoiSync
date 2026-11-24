using BepInEx.Configuration;
using KoiSync.Core;

namespace KoiSync.Plugin
{
    internal sealed class OutputChannels
    {
        private readonly ConfigEntry<bool>[] entries=new ConfigEntry<bool>[6];
        private readonly bool[] enabled;
        internal OutputChannels(ConfigFile config,bool[] destination)
        {
            enabled=destination;
            for(int i=0;i<6;i++)
            {
                int index=i;
                entries[i]=config.Bind("Output Channels",MotionPipeline.Axes[i],i==0,"Send this channel to TCode devices. Disabled channels remain visible in the monitor.");
                enabled[i]=entries[i].Value;
                entries[i].SettingChanged+=(sender,args)=>enabled[index]=entries[index].Value;
            }
        }
        internal void Set(int index,bool value){entries[index].Value=value;enabled[index]=value;}
    }
}
