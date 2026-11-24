using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using KoiSync.Core;
using KoiSync.Plugin;
using UnityEngine;

namespace KoiSync.MainGame
{
    public sealed partial class Plugin
    {
        private readonly CycleCapture capture=new CycleCapture();
        private readonly ActionRecorder recorder=new ActionRecorder();
        private readonly FunscriptPlayback playback=new FunscriptPlayback();
        private bool replayRequested,takeActive;
        private string takeStage="",takeKey="",recordStatus="",lastTake="";
        private double takeDuration,maxGap;
        private ActionSource takeEffector;
        private AxisMappingMode takeMode;
        private string takePair="";
        private string takeResource="";
        private ActionLandmark takeLandmark;
        private readonly System.Collections.Generic.HashSet<string> completedStages=new System.Collections.Generic.HashSet<string>();
        internal void SetRecording(bool requested)
        {
            if(!requested){FinishTake(false,"stopped by user");capture.Reset();}
            else{ClearPlayback();completedStages.Clear();capture.Arm();recordStatus=L("已开始随画面录制：每个动作阶段保存一个完整循环，非循环段立即记录","Recording started: saves one full loop per stage; non-looping stages save immediately");}
            RecordRequested=requested;
        }
        private void RecordFrame(double progress,double[] values)
        {
            if(!RecordRequested||!Drive.Ready||phaseDuration<=.0001||completedStages.Contains(Profile.Key+":"+Stage+":"+bindingIdentity))return;
            var step=capture.Observe(progress,clockState.loop,advancing,transition);
            if(step==CaptureStep.Discontinuity){FinishTake(false,"animation time moved backwards");return;}
            if(step==CaptureStep.Complete)
            {
                // Do not invent a closing sample by copying the previous frame.
                if(!clockState.loop&&takeActive)recorder.Add(phaseTime,values);
                bool fullyObserved=recorder.Start<=Math.Max(.025,takeDuration*.025)&&takeDuration-recorder.End<=Math.Max(.1,takeDuration*.05)&&maxGap<=Math.Max(.15,takeDuration*.1);
                FinishTake(fullyObserved,"observed animation boundary");capture.Arm();return;
            }
            if(step==CaptureStep.Begin)
            {
                recorder.Begin(phaseTime);takeActive=true;takeStage=Stage;takeKey=Profile.Key;takeDuration=phaseDuration;maxGap=0;takeEffector=Profile.Effector;takeMode=Profile.Mode;takePair=bindingIdentity;takeResource=Profile.Resource;takeLandmark=Profile.Landmark;
            }
            if(takeActive&&(step==CaptureStep.Begin||step==CaptureStep.Sample))
            {
                if(recorder.Count>0)maxGap=Math.Max(maxGap,phaseTime-recorder.End);
                if(!recorder.Add(phaseTime,values)){FinishTake(false,"recording clock moved backwards");capture.Arm();}
                recordStatus=L("录制 ","Recording ")+takeStage+" · "+recorder.Count+L(" 帧 · "," frames · ")+recorder.End.ToString("F2",CultureInfo.InvariantCulture)+" s";
            }
        }
        internal void FinishTake(bool complete,string reason)
        {
            if(!takeActive)return;takeActive=false;
            if(recorder.Count<2){recorder.Clear();return;}
            try
            {
                string folder=Path.Combine(Path.Combine(ActionProfile.Folder(DataRoot,takeKey),"Recordings"),SafeFile(takeStage));Directory.CreateDirectory(folder);
                string stem=Path.Combine(folder,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff",CultureInfo.InvariantCulture));
                recorder.Save(stem);
                var text=new StringBuilder();using(var xml=XmlWriter.Create(text,new XmlWriterSettings{OmitXmlDeclaration=true,Indent=true}))
                {
                    xml.WriteStartElement("take");xml.WriteAttributeString("schema","1");xml.WriteAttributeString("solver",MainGameVersion.Solver);xml.WriteAttributeString("action",takeKey);xml.WriteAttributeString("stage",takeStage);
                    xml.WriteAttributeString("complete",complete?"true":"false");xml.WriteAttributeString("reason",reason);xml.WriteAttributeString("frames",recorder.Count.ToString(CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("start",recorder.Start.ToString("R",CultureInfo.InvariantCulture));xml.WriteAttributeString("end",recorder.End.ToString("R",CultureInfo.InvariantCulture));xml.WriteAttributeString("duration",takeDuration.ToString("R",CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("maxGap",maxGap.ToString("R",CultureInfo.InvariantCulture));xml.WriteAttributeString("source","final-values-before-transport-delay");xml.WriteAttributeString("pair",takePair);xml.WriteAttributeString("effector",takeEffector.ToString());xml.WriteAttributeString("mapping",takeMode.ToString());xml.WriteAttributeString("resource",takeResource);xml.WriteAttributeString("landmark",takeLandmark.ToString());xml.WriteEndElement();
                }
                ActionProfile.AtomicWrite(stem+".take.xml",text.ToString());lastTake=stem+".funscript";
                if(complete)ActionProfile.AtomicWrite(Path.Combine(folder,"latest.txt"),Path.GetFileName(lastTake));
                if(complete)completedStages.Add(takeKey+":"+takeStage+":"+bindingIdentity);
                SaveProfile();recordStatus=(complete?L("完整循环已保存","Full loop saved"):L("已保存实际片段","Partial take saved"))+" · "+recorder.Count+L(" 帧 · "," frames · ")+takeStage;
            }
            catch(Exception error){recordStatus=L("录制保存失败：","Recording save failed: ")+error.GetBaseException().Message;Logger.LogError(error);RecordRequested=false;}
            recorder.Clear();
        }
        private void ClearPlayback(){playback.Clear();replayRequested=false;}
        private void RestartCapture(string reason)
        {FinishTake(false,reason);completedStages.Clear();if(RecordRequested)capture.Arm();}
        private bool ApplyPlayback()
        {
            if(!replayRequested||RecordRequested)return false;
            if(playback.Scripts==null)
            {
                string folder=Path.Combine(Path.Combine(ActionProfile.Folder(DataRoot,Profile.Key),"Recordings"),SafeFile(Stage));string latest=Path.Combine(folder,"latest.txt");
                if(!File.Exists(latest)){recordStatus=L("当前阶段尚无完整录制，使用实时计算","No complete take for this stage yet; using live calculation");return false;}
                string selected=File.ReadAllText(latest).Trim();playback.Load(Path.IsPathRooted(selected)?selected:Path.Combine(folder,selected));
                string metadata=Path.ChangeExtension(playback.Path,"take.xml");
                if(File.Exists(metadata))
                {
                    var doc=new XmlDocument();using(var reader=XmlReader.Create(metadata,new XmlReaderSettings{ProhibitDtd=true,XmlResolver=null}))doc.Load(reader);
                    recordStatus=L("脚本来源：","Script source: ")+doc.DocumentElement.GetAttribute("solver")+" · "+doc.DocumentElement.GetAttribute("stage")+(doc.DocumentElement.HasAttribute("reference")?L(" · 默认人物参考"," · default body ref"):"");
                }
            }
            double[] values;if(!playback.Scripts.TrySample(phaseTime,out values))return false;
            Drive.PhysicalL0Minimum=0;Drive.UpdateScript(values);return true;
        }
        internal static string SafeFile(string name)
        {foreach(char c in Path.GetInvalidFileNameChars())name=name.Replace(c,'_');return name.Length==0?"state":name.TrimEnd('.');}
    }
}
