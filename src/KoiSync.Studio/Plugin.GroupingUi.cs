using UnityEngine;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        private string cameraEvidence="";
        private void RebuildAutomaticChapters()
        {
            if(!autoCameraPairs){status=L("请先开启自动分析 / 分章","Enable analysis / chapters first");return;}
            RememberChapters();Book.RebuildAutomatic();chapterSelection.Reset();cameraChoice=null;nextPairCheck=0;
            ResetClimax();ReadAttachmentChapters();SelectChapter(Book.At(Clock.Time));MarkSceneDataDirty();
        }
        private void SetAutomaticGrouping(bool enabled)
        {
            if(autoCameraPairs==enabled)return;
            autoCameraPairs=enabled;
            ResetClimax();
            Devices.Stop();
            if(enabled){if(actors.Count==0)ReadActors();CachePairCandidates();followChapters=true;manualOpening=false;manualActorPair=false;}
            else ResetPairCandidates();
            nextPairCheck=0;appliedChapter="";MarkSceneDataDirty();
        }
        private void RecheckAutomaticGrouping()
        {
            if(!autoCameraPairs){status=L("请先开启自动分析 / 分章","Enable analysis / chapters first");return;}
            var chapter=Book.At(Clock.Time);
            ReadClimaxCues();
            if(chapter!=null&&chapter.Manual){RememberChapters();chapter.Manual=false;}
            followChapters=true;manualOpening=false;manualActorPair=false;nextPairCheck=0;appliedChapter="";cameraChoice=null;MarkSceneDataDirty();
            status=L("已重新启用本章自动分组","Automatic grouping enabled for this chapter");
        }
        private string GroupingStatus()
        {
            if(UseScripts)return L("脚本回放 · 跟随已保存章节","Script · saved chapter bindings");
            if(!autoCameraPairs)return L("按手动选择 / 已保存章节","Manual / saved chapter bindings");
            if(!followChapters||manualOpening)return L("已暂停 · 可重新识别本章","Paused · re-detect to resume");
            var chapter=Book.At(Clock.Time);
            if(chapter!=null&&chapter.Manual)return L("本章使用手动配对","Manual pair for this chapter");
            if(Binding!=null&&Measured!=null&&Measured.GeometricEngagement<=0)
                return L("已选组 · 接触未确认 ","Pair found · contact unconfirmed ")+(Measured.ContactDistance*1000).ToString("F0")+" mm";
            return cameraEvidence.Length==0?L("可见角色优先 · 画面中心","Visible actors · view centre"):cameraEvidence;
        }
        private static string ActorGroup(global::Studio.TreeNodeObject node)
        {
            if(node==null||node.parent==null)return "";
            node=node.parent;while(node.parent!=null)node=node.parent;
            return node.textName;
        }
        private string ActorDisplayName(ChaControl actor)
        {
            int key=Key(actor);string group="";global::Studio.ObjectCtrlInfo info;
            var studio=global::Studio.Studio.Instance;
            if(studio!=null&&studio.dicObjectCtrl.TryGetValue(key,out info))group=ActorGroup(info.treeNodeObject);
            return (group.Length==0?"":"["+group+"] ")+StudioBinding.Name(actor)+"  #"+key;
        }
    }
}
