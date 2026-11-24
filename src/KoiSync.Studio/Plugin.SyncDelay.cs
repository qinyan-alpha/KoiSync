using System;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        private ConfigEntry<float> syncDelaySetting,outputSmoothingSetting,outputRateSetting;
        private void InitializeSyncDelay()
        {
            syncDelaySetting=Config.Bind("Device","SyncDelayMs",60f,new ConfigDescription("Device-only interpolated delay; scene preview and recorded Timeline timestamps remain current. 0 disables the buffer.",new AcceptableValueRange<float>(0,250)));
            outputSmoothingSetting=Config.Bind("Axes","OutputSmoothingMs",25f,new ConfigDescription("Live output exponential smoothing time constant in ms. 0 disables smoothing. Recorded script playback bypasses this filter.",new AcceptableValueRange<float>(0,1000)));
            outputRateSetting=Config.Bind("Axes","OutputRatePercentPerSecond",200f,new ConfigDescription("Live output maximum change in percent of full command range per second. 0 disables this software rate limit. Script playback bypasses this filter.",new AcceptableValueRange<float>(0,2000)));
            Devices.DelaySeconds=syncDelaySetting.Value/1000.0;
            Drive.SmoothingSeconds=outputSmoothingSetting.Value/1000.0;
            Drive.MaximumChangePerSecond=outputRateSetting.Value/100.0;
        }
        private void DrawSyncDelay(Rect card)
        {
            float top=Card(card,L("同步与输出响应","Synchronization and output response"));
            DrawResponseSetting(top,L("设备发送延迟 ms","Device send delay ms"),syncDelaySetting,250,0,"ms");
            DrawResponseSetting(top+34,L("实时输出平滑 ms","Live output smoothing ms"),outputSmoothingSetting,1000,1,"ms");
            DrawResponseSetting(top+68,L("输出速度上限 % / 秒","Output rate limit % / s"),outputRateSetting,2000,2,"% / s");
            GUI.Label(new Rect(32,top+102,950,40),L("发送延迟只影响设备发送，不改变监控轨道或录制时间。平滑越小、速度上限越大，实时输出跟随越快；0 关闭对应处理。脚本回放不重复平滑，但仍受发送延迟影响。","Send delay affects device transmission only, not monitor rails or recording time. Lower smoothing and higher rate limits follow live targets faster; 0 disables that stage. Script playback bypasses live smoothing but retains send delay."),wrapSmall);
        }
        private void DrawResponseSetting(float y,string name,ConfigEntry<float> setting,float maximum,int stage,string unit)
        {
            GUI.Label(new Rect(32,y+2,300,28),name,label);
            float previous=setting.Value;
            float slider=GUI.HorizontalSlider(new Rect(340,y+9,500,20),previous,0,maximum);
            if(slider!=previous)
            {
                float value=(float)Math.Round(slider);
                if(value!=previous)
                {
                    setting.Value=value;
                    // Preserve queued samples while adjusting an active delay.
                    // Crossing zero changes whether samples are buffered.
                    if(stage==0){if((previous==0)!=(value==0))Devices.Stop();Devices.DelaySeconds=value/1000.0;}
                    else if(stage==1)Drive.SmoothingSeconds=value/1000.0;
                    else Drive.MaximumChangePerSecond=value/100.0;
                }
            }
            string display=setting.Value.ToString("0.#",CultureInfo.InvariantCulture)+" "+unit;
            if(setting.Value==0)display+=" ("+L("关闭","Off")+")";
            GUI.Label(new Rect(856,y+4,126,28),display,small);
        }
    }
}
