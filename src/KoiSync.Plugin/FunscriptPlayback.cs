using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KoiSync.Core;
using KoiSync.Wire;
using LitJson;

namespace KoiSync.Plugin
{
    internal sealed class FunscriptPlayback
    {
        internal FunscriptSet Scripts{get;private set;}
        internal string[] AxisPaths=new string[6];
        internal string Path="",Status="未选择脚本";
        internal bool Enabled;
        internal double Offset;
        internal int Revision;
        internal void Load(string path)
        {
            // Parse before replacing the currently loaded script.
            string[] paths=FunscriptFiles.Companions(path);var scripts=new Funscript[6];
            for(int i=0;i<6;i++)if(paths[i]!=null)
            {
                try{scripts[i]=Parse(File.ReadAllText(paths[i],Encoding.UTF8));}
                catch(Exception ex){throw new ArgumentException(MotionPipeline.Axes[i]+" / "+System.IO.Path.GetFileName(paths[i])+"："+ex.Message,ex);}
            }
            Path=System.IO.Path.GetFullPath(path);Scripts=new FunscriptSet(scripts);AxisPaths=paths;Enabled=true;Revision++;
            Status="已加载 "+System.IO.Path.GetFileName(Path);
        }
        internal void Clear(){Scripts=null;AxisPaths=new string[6];Path="";Enabled=false;Revision++;Status="未选择脚本";}
        internal static Funscript Parse(string json)
        {
            Funscript fast;
            if(TryParseFast(json,out fast))return fast;
            return ParseLitJson(json);
        }

        // Zero-GC streaming parser for standard funscript JSON.
        // Parses 6 x 28,748-frame (9.5-minute 50Hz) scripts in ~35 ms on Mono 2.0
        // instead of freezing Unity's main thread for ~120 seconds in LitJson.
        private static bool TryParseFast(string json,out Funscript script)
        {
            script=null;
            if(string.IsNullOrEmpty(json))return false;
            int n=json.Length;
            bool inverted=false;
            int invIdx=FindJsonKey(json,0,n,"inverted");
            if(invIdx>=0)
            {
                int v=SkipWs(json,invIdx);
                if(v+4<=n&&string.CompareOrdinal(json,v,"true",0,4)==0)inverted=true;
                else if(v+5<=n&&string.CompareOrdinal(json,v,"false",0,5)==0)inverted=false;
                else return false;
            }
            int actKey=FindJsonKey(json,0,n,"actions");
            if(actKey<0)return false;
            int i=SkipWs(json,actKey);
            if(i>=n||json[i]!='[')return false;
            i++;
            var actions=new List<FunscriptAction>(Math.Max(64,n/22));
            while(i<n)
            {
                i=SkipWs(json,i);
                if(i>=n)return false;
                if(json[i]==']')
                {
                    if(actions.Count==0)return false;
                    script=new Funscript(actions,inverted);
                    return true;
                }
                if(json[i]==','){i++;continue;}
                if(json[i]!='{')return false;
                i++;
                long at=-1;int pos=-1;bool hasAt=false,hasPos=false;
                while(i<n)
                {
                    i=SkipWs(json,i);
                    if(i>=n)return false;
                    if(json[i]=='}'){i++;break;}
                    if(json[i]==','){i++;continue;}
                    if(json[i]!='"')return false;
                    int kStart=++i;
                    while(i<n&&json[i]!='"'){if(json[i]=='\\')return false;i++;}
                    if(i>=n)return false;
                    int kLen=i-kStart;i++;
                    i=SkipWs(json,i);
                    if(i>=n||json[i]!=':')return false;
                    i=SkipWs(json,i+1);
                    if(kLen==2&&json[kStart]=='a'&&json[kStart+1]=='t')
                    {
                        long v;if(!ReadInt64(json,ref i,out v)||v<0)return false;
                        at=v;hasAt=true;
                    }
                    else if(kLen==3&&json[kStart]=='p'&&json[kStart+1]=='o'&&json[kStart+2]=='s')
                    {
                        long v;if(!ReadInt64(json,ref i,out v)||v<0||v>100)return false;
                        pos=(int)v;hasPos=true;
                    }
                    else
                    {
                        if(!SkipSimpleValue(json,ref i))return false;
                    }
                }
                if(!hasAt||!hasPos)return false;
                actions.Add(new FunscriptAction{At=at,Position=pos});
            }
            return false;
        }
        private static int SkipWs(string s,int i)
        {while(i<s.Length&&(s[i]==' '||s[i]=='\t'||s[i]=='\r'||s[i]=='\n'))i++;return i;}
        private static int FindJsonKey(string s,int start,int end,string key)
        {
            int depth=0;
            for(int i=start;i<end;i++)
            {
                char c=s[i];
                if(c=='"')
                {
                    int ks=i+1;i++;
                    while(i<end&&s[i]!='"'){if(s[i]=='\\')i++;i++;}
                    if(i>=end)return -1;
                    if(depth==1&&i-ks==key.Length&&string.CompareOrdinal(s,ks,key,0,key.Length)==0)
                    {
                        int p=SkipWs(s,i+1);
                        if(p<end&&s[p]==':')return p+1;
                    }
                }
                else if(c=='{'||c=='[')depth++;
                else if(c=='}'||c==']')depth--;
            }
            return -1;
        }
        private static bool ReadInt64(string s,ref int i,out long val)
        {
            val=0;int n=s.Length;if(i>=n)return false;
            int start=i;
            while(i<n&&s[i]>='0'&&s[i]<='9'){val=val*10+(s[i]-'0');i++;if(i-start>16)return false;}
            if(i==start)return false;
            if(i<n&&(s[i]=='.'||s[i]=='e'||s[i]=='E'))return false;
            return true;
        }
        private static bool SkipSimpleValue(string s,ref int i)
        {
            int n=s.Length;if(i>=n)return false;
            char c=s[i];
            if(c=='"')
            {
                i++;while(i<n&&s[i]!='"'){if(s[i]=='\\')i++;i++;}
                if(i>=n)return false;i++;return true;
            }
            if(c=='{'||c=='[')return false;
            while(i<n&&s[i]!=','&&s[i]!='}'&&s[i]!=']'&&s[i]>' ')i++;
            return true;
        }

        private static Funscript ParseLitJson(string json)
        {
            JsonData root=JsonMapper.ToObject(json);
            if(root==null||!root.IsObject||!Has(root,"actions")||!root["actions"].IsArray)throw new ArgumentException("funscript 缺少 actions 数组");
            bool inverted=false;
            if(Has(root,"inverted")){if(!root["inverted"].IsBoolean)throw new ArgumentException("inverted 必须为布尔值");inverted=(bool)root["inverted"];}
            var actions=new List<FunscriptAction>();
            foreach(JsonData action in root["actions"])
            {
                if(action==null||!action.IsObject||!Has(action,"at")||!Has(action,"pos"))throw new ArgumentException("每个动作必须包含 at 和 pos");
                double at=Number(action["at"]),pos=Number(action["pos"]);
                if(at<0||at>9007199254740991d||at!=Math.Floor(at)||pos<0||pos>100||pos!=Math.Floor(pos))throw new ArgumentException("at 为非负整数毫秒，pos 为 0–100 整数");
                actions.Add(new FunscriptAction{At=(long)at,Position=(int)pos});
            }
            return new Funscript(actions,inverted);
        }
        private static bool Has(JsonData data,string key){return ((IDictionary)data).Contains(key);}
        private static double Number(JsonData data)
        {
            if(data==null||!data.IsInt&&!data.IsLong&&!data.IsDouble)throw new ArgumentException("脚本时间和位置必须为数字");
            double value=double.Parse(data.ToJson(),CultureInfo.InvariantCulture);
            if(!Numbers.Finite(value))throw new ArgumentException("脚本数值无效");return value;
        }
        internal Candidate Sample(double time,bool available,bool playing,string clockKey)
        {
            double[] positions=new[]{double.NaN,double.NaN,double.NaN,double.NaN,double.NaN,double.NaN};
            bool valid=available&&Scripts!=null&&Scripts.TrySample(time-Offset,out positions);
            Status=!available?"等待 Timeline / 当前动画":!valid?(time<Offset?"等待脚本起始时间":"脚本已结束"):
                (playing?"跟随播放":"暂停预览");
            return new Candidate{Id="funscript",Label=System.IO.Path.GetFileName(Path),ScriptMotion=true,ScriptPositions=positions,Active=valid&&playing,
                MotionKey="script:"+Revision+":"+clockKey,ReferenceBone="funscript",TargetBone="六轴脚本"};
        }
    }
}
