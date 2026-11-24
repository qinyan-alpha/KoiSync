using KoiSync.Studio;

internal static class ChapterEditingChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var book=new ChapterBook();
        book.Observe(0,new Chapter{Source=1,Target=2,Name="automatic"});
        book.Observe(5,new Chapter{Source=1,Target=3,CustomName="A < B & \"口\""});
        book.Observe(10,new Chapter{Source=1,Target=4});
        var loaded=new ChapterBook();loaded.FromXml(book.ToXml());
        check(loaded.At(6).CustomName=="A < B & \"口\""&&!loaded.At(6).Manual,"Rename survives XML escaping without pinning automatic binding");
        loaded.CorrectAutomaticOpening(6,new Chapter{Source=1,Target=3,Opening=2,Name="corrected"});
        check(loaded.At(6).CustomName=="A < B & \"口\""&&loaded.At(6).Opening==2,"Automatic contact correction preserves the user's title");
        loaded.Observe(5,new Chapter{Source=1,Target=5});
        check(loaded.At(6).CustomName=="A < B & \"口\"","Replacing a boundary preserves its user title");
        var named=loaded.At(6);named.Target=2;loaded.CompactAutomatic();
        check(loaded.Chapters.Contains(named),"A renamed chapter boundary is not silently compacted");
        var split=loaded.Split(7);check(split.CustomName==named.CustomName,"Split preserves user title");
        loaded.ReadAttachmentEvents(new List<AttachmentEvent>{new(){Time=0,Constraint="a",Enabled=true,Binding=new Chapter{Source=8,Target=9}}});
        check(loaded.At(6).CustomName==named.CustomName,"Reading BP chapters retains renamed records");
        loaded.FromXml(book.ToXml());string before=loaded.ToXml();
        var middle=loaded.At(6);var next=loaded.Remove(middle);
        check(next==loaded.At(0)&&next.Manual&&loaded.At(6)==next&&loaded.At(10).Target==4,"Deleting a middle chapter extends its predecessor without moving later animation time");
        loaded.FromXml(before);check(loaded.Chapters.Count==3&&loaded.At(6).CustomName==middle.CustomName,"Undo snapshot restores deleted named chapter");
        next=loaded.Remove(loaded.At(0));
        check(next.Start==0&&loaded.At(0).Target==3&&loaded.Chapters.Count==2,"Deleting the first chapter keeps coverage from the original start");
        loaded.Remove(loaded.At(10));check(loaded.At(500)==next&&loaded.Chapters.Count==1,"Deleting the final chapter leaves the preceding interval open to scene end");
        check(loaded.Remove(next)==null&&loaded.Chapters.Count==0,"Deleting the only chapter safely produces an empty book");
        check(loaded.Remove(next)==null&&loaded.Chapters.Count==0,"Stale delete selection leaves an empty book intact");

        var modes=new ChapterBook();
        modes.Observe(0,new Chapter{Source=1,Target=2,MappingMode=0});
        modes.Chapters.Add(new Chapter{Start=10,Source=1,Target=2,MappingMode=2});
        modes.CompactAutomatic();
        check(modes.Chapters.Count==2,"Different chapter synchronization preferences preserve their boundary");
        var copied=modes.Split(12);check(copied.MappingMode==2,"Split inherits chapter synchronization preference");
        loaded.FromXml(modes.ToXml());
        check(loaded.At(1).MappingMode==0&&loaded.At(11).MappingMode==2&&loaded.At(13).MappingMode==2,"Chapter synchronization preferences survive save and reload");
        loaded.FromXml(book.ToXml());check(loaded.At(1).MappingMode==-1,"Existing scene chapters inherit the configured preference");
        loaded.Chapters[1].CalibrationEdited=true;loaded.Chapters[2].MappingMode=1;
        var replayed=new ChapterBook();replayed.FromXml(loaded.ToXml());replayed.RebuildAutomatic();
        check(replayed.Chapters.Count==2&&replayed.Chapters[0].CalibrationEdited&&replayed.Chapters[1].MappingMode==1,"Rebuilding automatic observations preserves named/calibrated chapters and synchronization preferences");
    }
}
