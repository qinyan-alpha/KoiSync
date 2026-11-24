using System;
using UnityEngine;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        private bool chapterMenu,renamingChapter,focusChapterName;
        private Vector2 chapterMenuPosition;
        private string renameText="";
        private void OpenChapterMenu(Chapter chapter)
        {
            ClosePopups();SelectChapter(chapter);chapterMenu=true;
            chapterMenuPosition=new Vector2(Input.mousePosition.x,Screen.height-Input.mousePosition.y)/uiScale-new Vector2(window.x,window.y);
        }
        private void BeginChapterRename()
        {
            if(selectedChapter==null)return;
            chapterMenu=false;renamingChapter=true;focusChapterName=true;renameText=ChapterBaseTitle(selectedChapter);
        }
        private void RenameSelectedChapter(string name)
        {
            if(selectedChapter==null||!Book.Chapters.Contains(selectedChapter))return;
            RememberChapters();selectedChapter.CustomName=(name??"").Trim();
            SelectChapter(selectedChapter);MarkSceneDataDirty();renamingChapter=false;textEditing=false;
        }
        private void DeleteSelectedChapter()
        {
            if(selectedChapter==null||!Book.Chapters.Contains(selectedChapter))return;
            RememberChapters();var next=Book.Remove(selectedChapter);SelectChapter(next);chapterMenu=false;appliedChapter="";Devices.Stop();
            if(next==null){SetAutomaticGrouping(false);ReleaseBinding(L("章节已删除","Chapter deleted"),false);}
            MarkSceneDataDirty();
        }
        private void DrawChapterMenu()
        {
            if(selectedChapter==null||!Book.Chapters.Contains(selectedChapter)){chapterMenu=renamingChapter=false;return;}
            var e=Event.current;
            if(renamingChapter)
            {
                var area=new Rect(260,112,494,162);Popup(area);
                GUI.Label(new Rect(278,125,450,26),L("重命名章节","Rename chapter"),title);
                GUI.SetNextControlName("ks-text-chapter-name");renameText=GUI.TextField(new Rect(278,163,458,30),renameText,100,textField);
                textEditing=true;if(focusChapterName){GUI.FocusControl("ks-text-chapter-name");focusChapterName=false;}
                bool save=e.type==EventType.KeyDown&&(e.keyCode==KeyCode.Return||e.keyCode==KeyCode.KeypadEnter);
                bool cancel=e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape;
                if(B(new Rect(490,214,118,32),L("保存名称","Save name"))||save){RenameSelectedChapter(renameText);if(save)e.Use();}
                if(B(new Rect(618,214,118,32),UiText.T("取消"))||cancel){renamingChapter=false;textEditing=false;GUI.FocusControl(null);if(cancel)e.Use();}
                return;
            }
            var menu=new Rect(Mathf.Clamp(chapterMenuPosition.x,12,window.width-242),Mathf.Clamp(chapterMenuPosition.y,42,window.height-91),226,79);
            if(e.type==EventType.MouseDown&&!menu.Contains(e.mousePosition)||e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape)
            {chapterMenu=false;e.Use();return;}
            Popup(menu);
            if(B(new Rect(menu.x+6,menu.y+6,214,30),L("重命名章节","Rename chapter")))BeginChapterRename();
            if(B(new Rect(menu.x+6,menu.y+43,214,30),L("删除所选章节","Delete selected chapter")))DeleteSelectedChapter();
        }
    }
}
