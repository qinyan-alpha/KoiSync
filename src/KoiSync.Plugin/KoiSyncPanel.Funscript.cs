using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    public sealed partial class KoiSyncPanel
    {
        private Toggle scriptEnabled;
        private InputField scriptOffset;
        private Text scriptNotice;
        private string scriptError="";
        private bool scriptRefreshing;
        private void BuildFunscriptControls(RectTransform parent,float y)
        {
            Button(parent,"选择 funscript…",15,y,154,32,ChooseFunscript);
            scriptEnabled=Toggle(parent,"脚本驱动六轴",182,y+3,182,false,value=>
            {
                if(scriptRefreshing)return;
                host.PauseOutput("已切换 Timeline 脚本来源");
                host.ScriptPlayback.Enabled=value&&host.ScriptPlayback.Scripts!=null;
                scriptError=value&&host.ScriptPlayback.Scripts==null?"请先选择 funscript 文件":"";
                RefreshFunscriptControls();
            });
            Label(parent,"起始偏移秒",382,y+5,93,24,12,Muted);scriptOffset=Input(parent,479,y,94,"0");
            scriptOffset.onEndEdit.AddListener(value=>
            {
                if(scriptRefreshing)return;double offset;
                if(!double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out offset)||!KoiSync.Core.Numbers.Finite(offset))
                {scriptError="起始偏移必须为有效秒数";return;}
                host.PauseOutput("已调整脚本起始时间");host.ScriptPlayback.Offset=offset;host.ScriptPlayback.Revision++;scriptError="";
                RefreshFunscriptControls();
            });
            Button(parent,"移除脚本",588,y,113,32,()=>
            {host.PauseOutput("已移除 Timeline 脚本");host.ScriptPlayback.Clear();scriptError="";RefreshFunscriptControls();});
            Label(parent,"正数延后开始 · 自动匹配其余轴",716,y+5,282,26,12,Muted);
            scriptNotice=Label(parent,"",15,y+39,985,103,12,Muted);scriptNotice.supportRichText=false;
            RefreshFunscriptControls();
        }
        private void ChooseFunscript()
        {
            host.PauseOutput("选择脚本 · 输出暂停");
            try
            {
                string directory=host.ScriptPlayback.Path.Length==0?host.Profiles.Root:Path.GetDirectoryName(host.ScriptPlayback.Path);
                string path=FunscriptFileDialog.Select(Directory.Exists(directory)?directory:BepInEx.Paths.GameRootPath);
                if(path!=null)ImportFunscript(path);
            }
            catch(Exception ex){scriptError="导入失败："+ex.GetBaseException().Message;}
            RefreshFunscriptControls();
        }
        internal void ImportFunscript(string path)
        {
            host.PauseOutput("已更换 Timeline 脚本");host.ScriptPlayback.Load(path);scriptError="";
            for(int i=0;i<6;i++)
            {
                bool loaded=host.ScriptPlayback.Scripts[i]!=null;
                host.OutputChannels.Set(i,loaded);
                if(channels[i]!=null)channels[i].isOn=loaded;
            }
            if(curveChannel!=null)curveChannel.value=1;
            RefreshFunscriptControls();
        }
        private void RefreshFunscriptControls()
        {
            if(scriptNotice==null)return;var playback=host.ScriptPlayback;
            scriptRefreshing=true;
            try
            {
                scriptEnabled.isOn=playback.Enabled;
                if(!scriptOffset.isFocused)scriptOffset.text=playback.Offset.ToString("0.###",CultureInfo.InvariantCulture);
                string details="";
                if(playback.Scripts!=null)for(int i=0;i<6;i++)
                {
                    details+=(i%2==0?"\n":"     ")+KoiSync.Core.MotionPipeline.Axes[i]+"："+(playback.AxisPaths[i]==null?"未找到（不发送）":Path.GetFileName(playback.AxisPaths[i])+(host.Pipeline.EnabledAxes[i]?"":"（发送关闭）"));
                }
                scriptNotice.text=scriptError.Length>0?scriptError:playback.Scripts==null?"选择任意一轴 funscript，自动加载同目录同名的其余轴脚本。本次加载保留到移除或退出游戏。":
                    playback.Scripts.Count+" / 6 轴 · 全长 "+playback.Scripts.Duration.ToString("F3",CultureInfo.InvariantCulture)+" s · "+(playback.Enabled?playback.Status:"脚本已停用")+details+
                    "\n脚本时间 = Timeline 时间 − 偏移；暂停停止输出，定位与循环跟随播放头；橙色为脚本曲线。";
                laboratoryTrack.Scripts=playback.Scripts;laboratoryTrack.ScriptOffset=playback.Offset;laboratoryTrack.SetVerticesDirty();
            }
            finally{scriptRefreshing=false;}
        }
    }
}
