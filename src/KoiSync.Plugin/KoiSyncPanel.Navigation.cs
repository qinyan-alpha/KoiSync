using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    public sealed partial class KoiSyncPanel
    {
        private enum WorkspacePage{Overview,Native,Studio,StudioReplace,Monitor,Geometry,Skeleton,Licenses,Experiment,VR,Playback,Stroke,Transitions,Shortcuts}
        private readonly Dictionary<WorkspacePage,GameObject> pages=new Dictionary<WorkspacePage,GameObject>();
        private readonly Dictionary<WorkspacePage,Button> navigation=new Dictionary<WorkspacePage,Button>();
        private RectTransform pageHost;
        private GameObject overviewPage,licensePage;
        private WorkspacePage activePage;
        private void NavigationButton(Transform sidebar,WorkspacePage page,string title,float y,UnityEngine.Events.UnityAction open)
        {navigation[page]=Button(sidebar,title,16,y,138,36,()=>{if(activePage==page)return;open();});}
        // Opens a sub-page, or returns to the workspace when that sub-page is already the visible one.
        private void TogglePage(WorkspacePage page,UnityEngine.Events.UnityAction open)
        {if(activePage==page)ShowOverview();else open();}
        private void DismissDropdowns()
        {
            foreach(WorkspaceDropdown dropdown in canvasObject.GetComponentsInChildren<WorkspaceDropdown>(true))dropdown.ClosePopup();
        }
        private void ActivatePage(WorkspacePage page)
        {
            DismissDropdowns();
            foreach(var entry in pages)entry.Value.SetActive(entry.Key==page);
            activePage=page;
            foreach(var entry in navigation)
            {
                entry.Value.GetComponent<Image>().color=entry.Key==page?new Color32(37,87,84,255):new Color32(28,44,60,255);
                entry.Value.GetComponentInChildren<Text>().color=entry.Key==page?Accent:Color.white;
            }
            if(Visible&&UnityEngine.EventSystems.EventSystem.current!=null)UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        }
        private RectTransform CreatePage(WorkspacePage page)
        {
            RectTransform root=Panel(pageHost,0,0,998,665,Background);root.name=page+" page";root.GetComponent<Image>().raycastTarget=true;
            pages.Add(page,root.gameObject);ActivatePage(page);return root;
        }
        private void ShowOverview(){ActivatePage(WorkspacePage.Overview);}
    }

    // Unity 5.6's stock dropdown uses a fixed popup sorting order. Keep both
    // popup and dismiss area relative to this workspace's canvas instead.
    public sealed class WorkspaceDropdown:Dropdown
    {
        public RectTransform BlockerArea;
        private GameObject ownedList,ownedBlocker;
        internal bool PopupOpen{get{return ownedList!=null&&ownedList.activeInHierarchy;}}
        public void ClosePopup()
        {
            // Unity 5.6 cancels the delayed Hide coroutine when its page is
            // disabled, leaving an invisible list that can intercept clicks.
            StopAllCoroutines();
            if(ownedList!=null){ownedList.SetActive(false);DestroyImmediate(ownedList);ownedList=null;}
            if(ownedBlocker!=null){ownedBlocker.SetActive(false);DestroyImmediate(ownedBlocker);ownedBlocker=null;}
        }
        protected override void OnDisable(){ClosePopup();base.OnDisable();}
        protected override void DestroyDropdownList(GameObject list)
        {if(ownedList==list)ownedList=null;if(list!=null)list.SetActive(false);base.DestroyDropdownList(list);}
        protected override void DestroyBlocker(GameObject blocker)
        {if(ownedBlocker==blocker)ownedBlocker=null;if(blocker!=null)blocker.SetActive(false);base.DestroyBlocker(blocker);}
        protected override GameObject CreateDropdownList(GameObject template)
        {
            GameObject list=base.CreateDropdownList(template);ownedList=list;
            Canvas canvas=list.GetComponent<Canvas>();if(canvas==null)canvas=list.AddComponent<Canvas>();
            Canvas root=GetComponentInParent<Canvas>().rootCanvas;canvas.overrideSorting=true;canvas.sortingOrder=root.sortingOrder+2;
            return list;
        }
        protected override GameObject CreateBlocker(Canvas rootCanvas)
        {
            // Show() can overwrite the cloned canvas order after CreateDropdownList.
            Transform list=transform.Find("Dropdown List");
            if(list!=null){Canvas popup=list.GetComponent<Canvas>();popup.overrideSorting=true;popup.sortingOrder=rootCanvas.sortingOrder+2;}
            GameObject blocker=base.CreateBlocker(rootCanvas);ownedBlocker=blocker;
            Canvas canvas=blocker.GetComponent<Canvas>();if(canvas!=null)canvas.sortingOrder=rootCanvas.sortingOrder+1;
            // Always cover the whole canvas: a blocker limited to one page left the sidebar dropdown
            // open forever when the click landed outside that area.
            RectTransform area=rootCanvas==null?BlockerArea:(RectTransform)rootCanvas.transform;
            if(area!=null)
            {
                RectTransform r=(RectTransform)blocker.transform;r.SetParent(area,false);r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
            }
            return blocker;
        }
    }
}
