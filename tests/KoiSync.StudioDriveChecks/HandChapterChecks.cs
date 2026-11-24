using KoiSync.Studio;
internal static class HandChapterChecks
{
    public static void Run(Action<bool,string> check)
    {
        var book=new ChapterBook();
        var shaft=new Chapter{Source=1,Target=2,Opening=4,Calibration=new ChapterCalibration{Length=.20,L1=.10,L2=.10},StrokeCalibration=ChapterStrokeCalibration.FromBody(1.7)};
        book.Observe(0,shaft);
        check(book.CorrectAutomaticOpening(0,new Chapter{Source=1,Target=2,Opening=7}),"Confirmed source fingers replace a stale target-hand label");
        check(shaft.Calibration==null&&shaft.StrokeCalibration==null,"Shaft length and origin are not inherited by a source finger");
        shaft.Calibration=new ChapterCalibration{Length=.08,L1=.04,L2=.04};
        shaft.StrokeCalibration=new ChapterStrokeCalibration{GameLength=.08,BodyHeight=0,FromHeight=false,FromFinger=true};
        var split=book.Split(2);
        check(split.Opening==7&&split.StrokeCalibration.FromFinger&&split.Calibration.Length==.08,"Split chapter preserves the source-hand interaction and its calibration");
        split.StrokeCalibration.GameLength=.07;
        check(shaft.StrokeCalibration.GameLength==.08,"Finger interaction lengths remain independent between chapters");
        var restored=new ChapterBook();restored.FromXml(book.ToXml());
        check(restored.Chapters.Count==2&&restored.At(2).Opening==7&&restored.At(2).StrokeCalibration.FromFinger,"Source-hand type and preset survive metadata reload");
        check(!restored.CorrectAutomaticOpening(2,new Chapter{Source=1,Target=2,Opening=6}),"Automatic detection preserves a manually assigned source hand");
        check(restored.CorrectAutomaticOpening(0,new Chapter{Source=1,Target=2,Opening=6})&&restored.At(0).Calibration==null,"Switching source hands does not reuse the other hand's origin");
        bool invalid=false;
        try{restored.FromXml("<chapters><chapter time='0' source='1' target='2' opening='8'/></chapters>");}catch(ArgumentException){invalid=true;}
        check(invalid&&restored.At(2).Opening==7,"Unknown interaction values are rejected without losing the loaded chapters");
    }
}
