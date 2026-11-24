using System;
using System.Collections.Generic;
using KoiSync.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    public sealed partial class KoiSyncPanel
    {
        private GameObject trackPopup;
        private bool cameraGesture,editorGesture;
        private int cameraInputFrame=-1;
        private bool CaptureSceneInput()
        {
            if(!Visible)return false;if(window.activeSelf||autoPartitionRunning)return true;
            if(vrWorld)return vrInput!=null&&(vrInput.Hit||vrInput.Held);
            if(cameraInputFrame!=Time.frameCount)
            {
                cameraInputFrame=Time.frameCount;
                bool held=UnityEngine.Input.GetMouseButton(0)||UnityEngine.Input.GetMouseButton(1)||UnityEngine.Input.GetMouseButton(2);
                if(!held){cameraGesture=editorGesture=false;}
                else if(!cameraGesture&&!editorGesture){editorGesture=CameraBlockedAt(UnityEngine.Input.mousePosition);cameraGesture=!editorGesture;}
            }
            if(editorGesture||alignmentGizmo!=null&&alignmentGizmo.Dragging||laboratoryTrack!=null&&laboratoryTrack.Dragging)return true;
            return !cameraGesture&&CameraBlockedAt(UnityEngine.Input.mousePosition);
        }
        internal bool CameraBlockedAt(Vector2 screen)
        {
            if(!Visible)return false;if(window.activeSelf)return true;
            if(AlignmentEditing&&alignmentGizmo.HitTest(screen))return true;
            if(EventSystem.current==null)return false;
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=screen},hits);
            foreach(var hit in hits)if(hit.gameObject.transform.IsChildOf(canvasObject.transform))return true;
            return false;
        }
        private void DismissTrackPopup(){if(trackPopup!=null){trackPopup.SetActive(false);Destroy(trackPopup);trackPopup=null;}}
        private RectTransform TrackPopup(Vector2 screen,float width,float height)
        {
            DismissDropdowns();DismissTrackPopup();
            var root=Rect(canvasObject.transform,"Timeline menu blocker",0,0,1500,950);root.anchorMin=Vector2.zero;root.anchorMax=Vector2.one;root.offsetMin=root.offsetMax=Vector2.zero;
            Image(root,Color.clear,false).raycastTarget=true;root.gameObject.AddComponent<PopupDismiss>().Dismiss=DismissTrackPopup;trackPopup=root.gameObject;
            var p=Panel(root,0,0,width,height,Card);p.name="Timeline context menu";p.GetComponent<Image>().raycastTarget=true;
            Vector2 local;var canvas=canvasObject.GetComponent<Canvas>();var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root,screen,camera,out local);
            p.anchoredPosition=new Vector2(Mathf.Clamp(local.x-root.rect.xMin,0,Mathf.Max(0,root.rect.width-width)), -Mathf.Clamp(root.rect.yMax-local.y,0,Mathf.Max(0,root.rect.height-height)));
            return p;
        }
        private void OpenTrackContext(double at,Vector2 screen)
        {
            if(autoPartitionRunning)return;
            var segment=host.Profiles.Scene.At(at);
            if(segment==null)
            {
                var p=TrackPopup(screen,245,50);
                Button(p,"建立新的时间编辑区域",8,8,229,34,()=>LabAction(()=>{DismissTrackPopup();SaveAlignment(true);var created=host.Profiles.Scene.CreateGap(at,TimelineState.Duration);host.Profiles.Changed(true);LoadAlignment(created.Id);}));return;
            }
            var menu=TrackPopup(screen,258,132);
            var split=Button(menu,"切割 · 此处 "+at.ToString("F3")+" s",8,8,242,34,()=>LabAction(()=>{DismissTrackPopup();SaveAlignment(true);var right=host.Profiles.Scene.Split(at);host.Profiles.Changed(true);LoadAlignment(right.Id);}));split.interactable=at-segment.Start>=.001&&segment.End-at>=.001;
            Button(menu,"删除所选时间段",8,49,242,34,()=>LabAction(()=>{DismissTrackPopup();DeleteTimelineSelection();}));
            Button(menu,"重命名该时间段",8,90,242,34,()=>RenameTimelineSegment(segment,screen));
        }
        private void RenameTimelineSegment(TimelineSegment segment,Vector2 screen)
        {
            var p=TrackPopup(screen,460,145);Label(p,"时间段名称",12,9,430,25,15,Accent);
            var input=Input(p,12,44,436,segment.Title());input.characterLimit=240;
            Button(p,"保存名称",12,94,210,34,()=>LabAction(()=>{segment.Name=input.text.Trim();host.Profiles.Changed(true);DismissTrackPopup();}));
            Button(p,"取消",234,94,214,34,DismissTrackPopup);input.ActivateInputField();
        }
        private void DeleteTimelineSelection()
        {
            if(autoPartitionRunning)return;
            SaveAlignment(true);var ids=new HashSet<string>(laboratoryTrack.SelectedIds);if(ids.Count==0&&AlignmentSegment!=null)ids.Add(AlignmentSegment.Id);
            host.Profiles.Scene.Segments.RemoveAll(s=>ids.Contains(s.Id));laboratoryTrack.SelectedIds.Clear();laboratoryTrack.CancelGesture();host.Profiles.Changed(true);LoadAlignment("");
        }
        private void TrackKeyboard()
        {
            if(timelineWindow==null||!timelineWindow.gameObject.activeInHierarchy||autoPartitionRunning)return;
            var selected=EventSystem.current==null?null:EventSystem.current.currentSelectedGameObject;
            if(selected!=null&&selected.GetComponent<InputField>()!=null)return;
            if(UnityEngine.Input.GetKeyDown(KeyCode.Escape)){DismissTrackPopup();laboratoryTrack.CancelGesture();return;}
            bool focus=selected!=null&&selected.transform.IsChildOf(timelineWindow);
            if(!focus)return;
            if(UnityEngine.Input.GetKeyDown(KeyCode.Delete)||(UnityEngine.Input.GetKey(KeyCode.LeftControl)||UnityEngine.Input.GetKey(KeyCode.RightControl))&&UnityEngine.Input.GetKeyDown(KeyCode.D))LabAction(DeleteTimelineSelection);
        }
    }
    public sealed class PopupDismiss:MonoBehaviour,IPointerClickHandler
    {
        internal Action Dismiss;
        public void OnPointerClick(PointerEventData e){if(e.button==PointerEventData.InputButton.Left&&Dismiss!=null)Dismiss();}
    }
}
