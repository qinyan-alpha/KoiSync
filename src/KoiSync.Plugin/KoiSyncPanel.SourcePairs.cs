using System;
using System.Collections.Generic;
using KoiSync.Core;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    public sealed partial class KoiSyncPanel
    {
        private void RefreshAlignmentPairs()
        {
            if(((WorkspaceDropdown)alignmentSourceBox).PopupOpen||((WorkspaceDropdown)alignmentPairBox).PopupOpen)return;
            string signature="";foreach(var s in host.StudioAdapter.Sources)signature+=s.SavedKey+s.PairLabel+s.NodeLabel;
            if(signature==alignmentSignature&&alignmentSources.Count>0)return;
            alignmentSignature=signature;alignmentRefreshing=true;
            try
            {
                var current=host.StudioAdapter.Sources.Find(s=>s.SavedKey==alignmentSource);
                if(alignmentPair==null)alignmentPair=current!=null?current.PairKey:alignmentSource==""?"":TimelineSegment.NoSource;
                alignmentPairs.Clear();alignmentPairs.Add("");alignmentPairs.Add(TimelineSegment.NoSource);
                alignmentPairBox.options.Clear();alignmentPairBox.options.Add(new Dropdown.OptionData("自动 · 镜头选择角色对与节点"));alignmentPairBox.options.Add(new Dropdown.OptionData("无 · 不指定数据源"));
                foreach(var s in host.StudioAdapter.Sources)if(s.Live&&!alignmentPairs.Contains(s.PairKey))
                {alignmentPairs.Add(s.PairKey);alignmentPairBox.options.Add(new Dropdown.OptionData(s.PairLabel));}
                if(!alignmentPairs.Contains(alignmentPair)){alignmentPairs.Add(alignmentPair);alignmentPairBox.options.Add(new Dropdown.OptionData("角色对已不可用"));}
                alignmentPairBox.value=alignmentPairs.IndexOf(alignmentPair);alignmentPairBox.RefreshShownValue();
                alignmentSources.Clear();alignmentSourceBox.options.Clear();
                if(alignmentPair==""){alignmentSources.Add("");alignmentSourceBox.options.Add(new Dropdown.OptionData("自动 · 镜头方向 / 距离，BP 优先"));}
                alignmentSources.Add(TimelineSegment.NoSource);alignmentSourceBox.options.Add(new Dropdown.OptionData("无 · 请选择该角色对的采样节点"));
                foreach(var s in host.StudioAdapter.Sources)if(s.Live&&s.PairKey==alignmentPair){alignmentSources.Add(s.SavedKey);alignmentSourceBox.options.Add(new Dropdown.OptionData(s.NodeLabel));}
                if(!alignmentSources.Contains(alignmentSource)){alignmentSources.Add(alignmentSource);alignmentSourceBox.options.Add(new Dropdown.OptionData("来源缺失 · "+alignmentSourceLabel));}
                alignmentSourceBox.value=Math.Max(0,alignmentSources.IndexOf(alignmentSource));alignmentSourceBox.RefreshShownValue();
            }
            finally{alignmentRefreshing=false;}
        }
        private Dropdown overviewPairBox;
        private string overviewPair="";
        private readonly List<string> overviewPairs=new List<string>();
        private void RefreshOverviewPairs()
        {
            if(host.StudioAdapter==null||host.Demo)return;
            if(((WorkspaceDropdown)overviewPairBox).PopupOpen||((WorkspaceDropdown)sources).PopupOpen)return;
            string sig="pairs:";foreach(var s in host.StudioAdapter.Sources)sig+=s.SavedKey+s.PairLabel;
            sig+=overviewPair;
            if(sourceSignature!=sig)
            {
                sourceSignature=sig;overviewPairs.Clear();overviewPairs.Add("");overviewPairBox.options.Clear();overviewPairBox.options.Add(new Dropdown.OptionData("先选择交互角色对…"));
                foreach(var s in host.StudioAdapter.Sources)if(s.Live&&!overviewPairs.Contains(s.PairKey)){overviewPairs.Add(s.PairKey);overviewPairBox.options.Add(new Dropdown.OptionData(s.PairLabel));}
                overviewPairBox.value=Math.Max(0,overviewPairs.IndexOf(overviewPair));overviewPairBox.RefreshShownValue();
                overviewSourceIds.Clear();sources.options.Clear();sources.options.Add(new Dropdown.OptionData("选择该角色对的采样节点…"));
                foreach(var s in host.StudioAdapter.Sources)if(s.Live&&s.PairKey==overviewPair){overviewSourceIds.Add(s.Id);sources.options.Add(new Dropdown.OptionData(s.NodeLabel));}
            }
            sources.value=overviewSourceIds.IndexOf(host.SelectedId)+1;sources.RefreshShownValue();
        }
    }
}
