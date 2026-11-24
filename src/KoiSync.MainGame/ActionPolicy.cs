using System;
using System.Collections.Generic;
using System.IO;
using KoiSync.Core;

namespace KoiSync.MainGame
{
    public enum ActorSlot { Male, Male1, Female0, Female1 }
    public enum ActionSource { Shaft, LeftFinger, RightFinger, SceneAnchor, Pelvis, Head, InteractionItem, NoMotion, Unsupported, LeftHand, RightHand, AnimatedProp }
    public enum ActionLandmark { Anatomical, Buttocks, LeftArmpit, RightArmpit }

    public sealed class ActionDefinition
    {
        public string Key,Name,Reason;
        public ActorSlot Source=ActorSlot.Male,Target=ActorSlot.Female0;
        public ActorSlot? WeakSource;
        public ActionSource Effector=ActionSource.Shaft;
        public NativeTarget Contact=NativeTarget.VAGINAL;
        public ActionLandmark Landmark;
        public string Resource="";
        public bool AnalAllowed;
        public bool CenteredMotion;
        public bool IsDriven {get{return Effector!=ActionSource.NoMotion&&Effector!=ActionSource.Unsupported;}}
        public ActionDefinition Copy(){return (ActionDefinition)MemberwiseClone();}
    }

    // Fixed action IDs and participant slots. No camera ranking or contact classification.
    public sealed class ActionCatalogue
    {
        private readonly Dictionary<string,ActionDefinition> definitions=new Dictionary<string,ActionDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string,NativeTarget> extensions;
        private readonly Dictionary<string,ActionDefinition> resources=new Dictionary<string,ActionDefinition>(StringComparer.Ordinal);
        public int Count {get{return definitions.Count;}}
        public IEnumerable<ActionDefinition> Definitions {get{return definitions.Values;}}
        public ActionCatalogue(TextReader table,TextReader names):this(table,names,null,null){}
        public ActionCatalogue(TextReader table,TextReader names,TextReader nativeResources,TextReader extraActions)
        {
            extensions=NativeProfiles.Read(names);string line;
            while((line=table.ReadLine())!=null)
            {
                line=line.TrimStart('\uFEFF');if(line.Length==0||line[0]=='#')continue;
                var c=line.Split('\t');if(c.Length!=9&&c.Length!=10)throw new FormatException("Invalid main-game action row");
                var d=new ActionDefinition{Key=c[0],Name=c[1],Source=(ActorSlot)Enum.Parse(typeof(ActorSlot),c[2]),Target=(ActorSlot)Enum.Parse(typeof(ActorSlot),c[3]),Effector=(ActionSource)Enum.Parse(typeof(ActionSource),c[4]),Contact=(NativeTarget)Enum.Parse(typeof(NativeTarget),c[5]),AnalAllowed=c[6]=="true",CenteredMotion=c[7]=="true",Reason=c[8]};
                if(c.Length==10)d.WeakSource=(ActorSlot)Enum.Parse(typeof(ActorSlot),c[9]);
                if(definitions.ContainsKey(d.Key))throw new FormatException("Duplicate main-game action ID: "+d.Key);
                definitions.Add(d.Key,d);
            }
            if(nativeResources!=null)while((line=nativeResources.ReadLine())!=null)
            {
                if(line.Length==0||line[0]=='#')continue;var c=line.Split('\t');ActionDefinition d;
                if(c.Length!=2||!definitions.TryGetValue(c[0],out d))throw new FormatException("Invalid native resource alias");
                d.Resource=c[1];resources.Add(c[1],d);
            }
            if(extraActions!=null)while((line=extraActions.ReadLine())!=null)
            {
                if(line.Length==0||line[0]=='#')continue;var c=line.Split('\t');
                if(c.Length!=11)throw new FormatException("Invalid extension action row");
                var d=new ActionDefinition{Key=c[0],Name=c[1],Source=(ActorSlot)Enum.Parse(typeof(ActorSlot),c[2]),Target=(ActorSlot)Enum.Parse(typeof(ActorSlot),c[3]),Effector=(ActionSource)Enum.Parse(typeof(ActionSource),c[4]),Contact=(NativeTarget)Enum.Parse(typeof(NativeTarget),c[5]),AnalAllowed=c[6]=="true",CenteredMotion=c[7]=="true",Reason=c[8],Resource=c[9],Landmark=(ActionLandmark)Enum.Parse(typeof(ActionLandmark),c[10])};
                definitions.Add(d.Key,d);resources.Add(d.Resource,d);
            }
        }
        public ActionDefinition Resolve(string mode,int id,string originalName,bool anal)
        {return Resolve(mode,id,originalName,anal,null);}
        public ActionDefinition Resolve(string mode,int id,string originalName,bool anal,string resource)
        {
            string key=mode+"_"+id;ActionDefinition d=null;
            if(resource!=null)
            {
                if(resources.TryGetValue(resource,out d))d=d.Copy();
#if KKS
                else
                {
                    // KKS renames base H-scene bundles from KK's XX_00_00.unity3d to XX_01_00 / XX_71_00.
                    // Fall back through the curated KK key/name catalogue and KKS mode+name heuristics
                    // while preserving the KKS resource identity for per-action calibration storage.
                    d=ResolveKksFallback(mode,key,originalName);
                    d.Resource=resource;
                }
#else
                else d=new ActionDefinition{Key=AnimationResources.UnknownKey(resource),Name=originalName,Resource=resource,Effector=ActionSource.Unsupported,Reason="动画资源尚未分类，请指定来源和部位"};
#endif
            }
            else if(definitions.TryGetValue(key,out d)&&d.Name==originalName)d=d.Copy();
            else
            {
                NativeTarget contact;
                if(extensions.TryGetValue(originalName??"",out contact))
                {
                    int index=NativeProfiles.ActorIndex(contact);
                    // BREASTSWAP is singular in the legacy enum.
                    var primary=contact==NativeTarget.BREASTSWAP?NativeTarget.BREASTS:NativeProfiles.Primary(contact);
                    d=new ActionDefinition{Key=key,Name=originalName,Contact=primary,Target=index==1?ActorSlot.Female1:ActorSlot.Female0,Reason="固定扩展动作表"};
                }
                else if(mode=="sonyu"||mode=="sonyu3P"||mode=="sonyu3PMMF")
                    d=new ActionDefinition{Key=key,Name=originalName,AnalAllowed=true,Reason="游戏插入类型元数据"};
#if KKS
                else d=ResolveKksFallback(mode,key,originalName);
#else
                else d=new ActionDefinition{Key=key,Name=originalName,Effector=ActionSource.Unsupported,Reason="动作尚未分类，请指定来源和部位"};
#endif
            }
            if(anal&&d.Contact==NativeTarget.VAGINAL&&d.AnalAllowed){d.Contact=NativeTarget.ANAL;d.Key+="_anal";}
            return d;
        }
#if KKS
        private ActionDefinition ResolveKksFallback(string mode,string key,string originalName)
        {
            ActionDefinition d;
            if(definitions.TryGetValue(key,out d)&&d.Name==originalName)return d.Copy();
            NativeTarget contact;
            if(extensions.TryGetValue(originalName??"",out contact))
            {
                int index=NativeProfiles.ActorIndex(contact);
                var primary=contact==NativeTarget.BREASTSWAP?NativeTarget.BREASTS:NativeProfiles.Primary(contact);
                return new ActionDefinition{Key=key,Name=originalName,Contact=primary,Target=index==1?ActorSlot.Female1:ActorSlot.Female0,AnalAllowed=primary==NativeTarget.VAGINAL,Reason="KKS 扩展名称匹配"};
            }
            if(definitions.TryGetValue(key,out d))
            {
                var copy=d.Copy();
                if(!string.IsNullOrEmpty(originalName))copy.Name=originalName;
                copy.Reason="KKS 原生动作键值匹配";
                return copy;
            }
            string name=originalName??"";
            if(mode=="sonyu"||mode=="sonyu3P"||mode=="sonyu3PMMF")
                return new ActionDefinition{Key=key,Name=name,Source=ActorSlot.Male,Target=ActorSlot.Female0,Effector=ActionSource.Shaft,Contact=NativeTarget.VAGINAL,AnalAllowed=true,Reason="KKS 插入动作自动分类"};
            if(mode=="aibu")
                return new ActionDefinition{Key=key,Name=name,Source=ActorSlot.Male,Target=ActorSlot.Female0,Effector=ActionSource.InteractionItem,Contact=NativeTarget.VAGINAL,AnalAllowed=true,Reason="KKS 爱抚交互自动分类"};
            if(mode=="houshi"||mode=="houshi3P"||mode=="houshi3PMMF")
            {
                NativeTarget hContact=NativeTarget.ORAL;
                if(name.Contains("パイズリ"))hContact=NativeTarget.BREASTS;
                else if(name.Contains("足")||name.Contains("フット"))hContact=NativeTarget.LEFTFOOT;
                else if(name.Contains("素股"))hContact=NativeTarget.INTERCRURAL;
                else if(name.Contains("両手")||name.Contains("左手"))hContact=NativeTarget.LEFTHAND;
                else if(name.Contains("コキ")||name.Contains("いじり")||name.Contains("オナホ")||name.Contains("手"))hContact=NativeTarget.RIGHTHAND;
                return new ActionDefinition{Key=key,Name=name,Source=ActorSlot.Male,Target=ActorSlot.Female0,Effector=ActionSource.Shaft,Contact=hContact,Reason="KKS 奉仕动作自动分类"};
            }
            if(mode=="masturbation"||mode=="peeping")
                return new ActionDefinition{Key=key,Name=name,Source=ActorSlot.Female0,Target=ActorSlot.Female0,Effector=ActionSource.RightFinger,Contact=NativeTarget.VAGINAL,Reason="KKS 自慰/旁观动作自动分类"};
            if(mode=="lesbian")
                return new ActionDefinition{Key=key,Name=name,Source=ActorSlot.Female1,Target=ActorSlot.Female0,Effector=ActionSource.RightFinger,Contact=NativeTarget.VAGINAL,Reason="KKS 百合动作自动分类"};
            return new ActionDefinition{Key=key,Name=name,Effector=ActionSource.Unsupported,Reason="KKS 动作尚未分类，请指定来源和部位"};
        }
#endif
    }

    public static class ActionStages
    {
        public static bool IsTravel(string key,string state)
        {return key!=null&&key.StartsWith("peeping_",StringComparison.Ordinal)&&(state=="In"||state=="Out");}
        public static int ItemLayer(int actionLayer,int layerCount,double weight)
        {return actionLayer>=0&&actionLayer<layerCount&&Numbers.Finite(weight)&&weight>=.9?actionLayer:-1;}
        public static ActorSlot SourceFor(ActorSlot configured,ActorSlot? weakSource,string nativeStage,bool manual)
        {return !manual&&weakSource.HasValue&&nativeStage=="WLoop"?weakSource.Value:configured;}
    }
    internal static class ActionLabels
    {
        internal static string Stage(string stage)
        {
            switch(stage)
            {
                case "WLoop":return UiText.L("慢速循环","Slow loop");
                case "SLoop":return UiText.L("快速循环","Fast loop");
                case "A_WLoop":return UiText.L("慢速分支循环","Slow branch loop");
                case "A_SLoop":return UiText.L("快速分支循环","Fast branch loop");
                case "Idle":return UiText.L("待机","Idle");
                case "InsertIdle":return UiText.L("停留","Inserted idle");
                case "Loop":return UiText.L("循环","Loop");
                case "In":return UiText.L("入场","Entry");
                case "Out":return UiText.L("离场","Exit");
                default:return string.IsNullOrEmpty(stage)?UiText.L("等待动作","Waiting for action"):stage.StartsWith("state_",StringComparison.Ordinal)?UiText.L("原生交互动作","Native interaction"):stage;
            }
        }
        internal static string Actor(ActorSlot slot)
        {
            switch(slot)
            {
                case ActorSlot.Male:return UiText.L("男方 1","Male 1");
                case ActorSlot.Male1:return UiText.L("男方 2","Male 2");
                case ActorSlot.Female0:return UiText.L("女方 1","Female 1");
                default:return UiText.L("女方 2","Female 2");
            }
        }
        internal static string Source(ActionSource kind)
        {
            switch(kind)
            {
                case ActionSource.Shaft:return UiText.L("阴茎模型","Shaft model");
                case ActionSource.LeftFinger:return UiText.L("左手指链","Left finger chain");
                case ActionSource.RightFinger:return UiText.L("右手指链","Right finger chain");
                case ActionSource.SceneAnchor:return UiText.L("场景固定支撑","Scene anchor");
                case ActionSource.Pelvis:return UiText.L("骨盆","Pelvis");
                case ActionSource.Head:return UiText.L("头部","Head");
                case ActionSource.InteractionItem:return UiText.L("原生交互物件","Native interaction item");
                case ActionSource.NoMotion:return UiText.L("静止","Stationary");
                case ActionSource.Unsupported:return UiText.L("尚未分类","Unclassified");
                case ActionSource.LeftHand:return UiText.L("左手持物标尺","Left hand span");
                case ActionSource.RightHand:return UiText.L("右手持物标尺","Right hand span");
                case ActionSource.AnimatedProp:return UiText.L("原生动画道具","Animated prop");
                default:return kind.ToString();
            }
        }
        internal static string Contact(NativeTarget kind,ActionLandmark landmark)
        {
            if(landmark==ActionLandmark.Buttocks)return UiText.L("臀部表面","Buttocks");
            if(landmark==ActionLandmark.LeftArmpit)return UiText.L("左腋下","Left armpit");
            if(landmark==ActionLandmark.RightArmpit)return UiText.L("右腋下","Right armpit");
            return Contact(kind);
        }
        internal static string Contact(NativeTarget kind)
        {
            switch(kind)
            {
                case NativeTarget.VAGINAL:return UiText.L("阴部","Vaginal");
                case NativeTarget.ANAL:return UiText.L("肛门","Anal");
                case NativeTarget.ORAL:return UiText.L("口部","Mouth");
                case NativeTarget.BREASTS:return UiText.L("胸部","Breasts");
                case NativeTarget.LEFTHAND:return UiText.L("左手","Left hand");
                case NativeTarget.RIGHTHAND:return UiText.L("右手","Right hand");
                case NativeTarget.LEFTFOOT:return UiText.L("左脚","Left foot");
                case NativeTarget.RIGHTFOOT:return UiText.L("右脚","Right foot");
                case NativeTarget.BOTH_FEET:return UiText.L("双脚","Both feet");
                case NativeTarget.INTERCRURAL:return UiText.L("腿间","Intercrural");
                default:return kind.ToString();
            }
        }
    }
}
