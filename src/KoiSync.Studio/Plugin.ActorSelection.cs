using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using GameStudio=global::Studio.Studio;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        private bool manualActorPair;

        private void SelectActor(int index,bool source)
        {
            if(index<0||index>=actors.Count)return;
            if(source)sourceIndex=index;else targetIndex=index;
            manualActorPair=true;actorPicker=0;
            ReleaseBinding(UiText.T("角色已切换，请点击绑定。"),false);
            SelectStudioActor(index);
        }

        private void SelectChapterActor(int index,bool source)
        {
            if(index<0||index>=actors.Count)return;
            if(source)chapterSource=index;else chapterTarget=index;
            SelectStudioActor(index);
        }

        // Only explicit picker actions call this. Automatic chapter changes must
        // not change the user's Studio selection or the Timeline camera.
        private void SelectStudioActor(int index)
        {
            try
            {
                var studio=GameStudio.Instance;global::Studio.ObjectCtrlInfo info;
                if(studio==null||!studio.dicObjectCtrl.TryGetValue(Key(actors[index]),out info)||info.treeNodeObject==null)return;
                var node=info.treeNodeObject;
                for(var parent=node.parent;parent!=null;parent=parent.parent)parent.treeState=global::Studio.TreeNodeObject.TreeState.Open;
                // false keeps an already selected actor selected on repeated clicks.
                studio.treeNodeCtrl.SelectSingle(node,false);
                StartCoroutine(RevealStudioActor(node));
            }
            catch(Exception error){Logger.LogWarning("Studio actor selection: "+error.GetBaseException().Message);}
        }

        private IEnumerator RevealStudioActor(global::Studio.TreeNodeObject node)
        {
            // Let the native tree layout settle after opening ancestor folders.
            yield return null;
            var studio=GameStudio.Instance;
            if(node==null||studio==null||studio.treeNodeCtrl.selectNode!=node)yield break;
            var scroll=RuntimeAccess.Read(studio.treeNodeCtrl,"scrollRect") as ScrollRect;
            if(scroll==null||scroll.content==null)yield break;
            Canvas.ForceUpdateCanvases();scroll.StopMovement();
            var viewport=scroll.viewport!=null?scroll.viewport:scroll.transform as RectTransform;
            if(viewport==null||node.rectNode==null)yield break;
            var corners=new Vector3[4];node.rectNode.GetWorldCorners(corners);
            float bottom=viewport.InverseTransformPoint(corners[0]).y,top=viewport.InverseTransformPoint(corners[1]).y;
            float move=top>viewport.rect.yMax?viewport.rect.yMax-top:bottom<viewport.rect.yMin?viewport.rect.yMin-bottom:0;
            scroll.content.anchoredPosition+=new Vector2(0,move);
        }
    }
}
