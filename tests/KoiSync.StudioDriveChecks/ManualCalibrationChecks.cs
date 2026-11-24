using KoiSync.Core;
using KoiSync.Studio;

static class ManualCalibrationChecks
{
    public static void Run(Action<bool,string> check)
    {
        void Near(double a,double b,string label)=>check(Math.Abs(a-b)<1e-8,"Manual: "+label+" ("+a+")");
        void Point(MotionVector a,MotionVector b,string label)=>Near((a-b).Length,0,label);
        var root=new MotionVector();var frame=AlignmentFrame.Build(root,new MotionVector(1,0,0),new MotionVector(0,1,0));
        var sample=new NativeMotionSample{Origin=root,HasShaftPose=true,HasTargetFrame=true,ReferenceFrame=frame,
            TargetReferenceFrame=AlignmentFrame.Build(new MotionVector(.006,.04,.01),frame.X,frame.Y),
            ShaftPath=new[]{root,frame.Y*.02},PlaneWidth=.1,PlaneDepth=.12,LateralSpan=1};
        var tip=frame.Y*.2;
        var result=ManualCalibrationMotion.Solve(sample,tip);
        var pose=ManualCalibrationMotion.Preview(sample,result);
        Near(result.Length,.2,"configured endpoints determine length");
        Near(result.Normalized[0],.8,"80% depth even when visible BP chain ends before entry");
        Near(result.Normalized[1],.6,"L1 keeps its explicit range");
        Near(result.Normalized[2],.45,"L2 keeps its explicit range (UserLeft = -X)");
        Point(pose.Sleeve.Origin,sample.TargetReferenceFrame.Origin,"preview entrance is exact game anchor");
        Point(pose.RodRoot+pose.RodDirection*pose.RodLength,tip,"preview end is configured endpoint");
        Point(ManualCalibrationMotion.LocalEnd(frame,tip),new MotionVector(.2,0,0),"visible model tip converts to axial/L1/L2 endpoint coordinates");
        var dragOrigin=new MotionVector(-.04,.015,-.02);var dragEnd=new MotionVector(.18,.045,.01);
        var dragAxis=(dragEnd-dragOrigin).Unit();double dragLength=ManualCalibrationMotion.Length(dragOrigin,dragEnd);
        var movedOrigin=ManualCalibrationMotion.MoveOriginAlongAxis(dragOrigin,dragEnd,.03);
        Point(MotionVector.Cross(movedOrigin-dragOrigin,dragAxis),new MotionVector(),"origin drag stays on the exact origin-to-tip line");
        Point(movedOrigin+dragAxis*(dragLength-.03),dragEnd,"origin drag keeps the configured tip fixed");
        Near(ManualCalibrationMotion.Length(movedOrigin,dragEnd),dragLength-.03,"moving origin toward tip shortens player length");
        Near(ManualCalibrationMotion.Length(ManualCalibrationMotion.MoveOriginAlongAxis(dragOrigin,dragEnd,10),dragEnd),.001,"origin drag stops at 1 mm without crossing the tip");
        Near(ManualCalibrationMotion.Length(ManualCalibrationMotion.MoveOriginAlongAxis(dragOrigin,dragEnd,-10),dragEnd),.5,"origin drag is bounded at 500 mm");
        var planeAngles=new MotionVector(24,-13,12.5);
        var rotated=ManualCalibrationMotion.Solve(sample,tip,planeAngles);
        var rotatedPose=ManualCalibrationMotion.Preview(sample,rotated,planeAngles);
        var displayedAngles=StudioShaftMotion.BodyAngles(sample.TargetReferenceFrame,rotatedPose.Sleeve);
        Near(displayedAngles[0],planeAngles.X,"manual preview directly rotates the highlighted plane on R0");
        Near(displayedAngles[1],planeAngles.Y,"manual preview directly rotates the highlighted plane on R1");
        Near(displayedAngles[2],planeAngles.Z,"manual preview directly rotates the highlighted plane on R2");
        Near(rotated.Raw[3]-result.Raw[3],planeAngles.X,"chapter R0 plane angle changes the physical twist request");
        Near(rotated.Raw[4]-result.Raw[4],planeAngles.Y,"chapter R1 plane angle changes the physical roll request");
        Near(rotated.Raw[5]-result.Raw[5],planeAngles.Z,"chapter R2 plane angle changes the physical pitch request");
        Point(rotatedPose.Sleeve.Origin,sample.TargetReferenceFrame.Origin,"R0/R1/R2 plane editing keeps the game entrance fixed");
        var drive=new StudioDrive();drive.Update(result.Normalized,.02,AxisMappingMode.Manual);
        Near(drive.Target[0],.2,"deeper maps to smaller L0");
        // The same short scene stroke must occupy more output travel when the
        // endpoint span is shortened; changing endpoints is not a depth cap.
        double Stroke(double length,double shift)
        {
            var inputs=new double[2];
            for(int i=0;i<2;i++)
            {
                sample.Origin=frame.Y*shift;
                sample.TargetReferenceFrame.Origin=frame.Y*(.04+i*.01);
                var motion=ManualCalibrationMotion.Solve(sample,sample.Origin+frame.Y*length);
                drive.Reset();drive.Update(motion.Normalized,.02,AxisMappingMode.Manual);
                inputs[i]=drive.Target[0];
                Point(ManualCalibrationMotion.Preview(sample,motion).Sleeve.Origin,sample.TargetReferenceFrame.Origin,"stroke scaling leaves the entrance fixed");
            }
            return inputs[1]-inputs[0];
        }
        double wideStroke=Stroke(.2,0),shortStroke=Stroke(.1,0);
        Near(wideStroke,.05,"10 mm scene stroke spans 5% of 200 mm calibration");
        Near(shortStroke,.1,"10 mm scene stroke spans 10% of 100 mm calibration");
        Near(shortStroke,wideStroke*2,"halving endpoint distance doubles L0 motion");
        Near(Stroke(.1,.02),shortStroke,"moving both endpoints shifts depth without changing stroke scale");
        Near(Stroke(.08,.02),.125,"moving the origin toward a fixed tip increases L0 scale");
        sample.Origin=root;sample.TargetReferenceFrame.Origin=new MotionVector(.006,.04,.01);
        foreach(double length in new[]{.08,.13,.2,.35,.5})
        {
            var edited=ManualCalibrationMotion.Solve(sample,frame.Y*length);
            Near(edited.Normalized[0],1-.04/length,"direct L0 after length edit "+length);
            var editedPose=ManualCalibrationMotion.Preview(sample,edited);
            Point(editedPose.Sleeve.Origin,sample.TargetReferenceFrame.Origin,"end edit never moves entry "+length);
            Near(editedPose.RodLength,length,"end edit updates preview length "+length);
        }
        var originalTip=new MotionVector(.2,0,0); // local axial/L1/L2
        foreach(var offset in new[]{new MotionVector(.01,0,0),new MotionVector(-.03,.02,0),new MotionVector(.01,.025,-.02)})
        {
            sample.Origin=frame.Y*offset.X+frame.Z*offset.Y+frame.X*offset.Z;
            var worldTip=ManualCalibrationMotion.WorldEnd(sample,offset,originalTip);
            Point(worldTip,tip,"origin edits preserve the configured tip");
            result=ManualCalibrationMotion.Solve(sample,worldTip);pose=ManualCalibrationMotion.Preview(sample,result);
            Near(result.Length,(originalTip-offset).Length,"length follows both endpoints including transverse offset");
            Point(pose.RodRoot,sample.Origin,"preview origin follows edited start");
            Point(pose.RodRoot+pose.RodDirection*pose.RodLength,tip,"angled manual rod retains end");
            Point(pose.Sleeve.Origin,sample.TargetReferenceFrame.Origin,"origin edit never moves entry");
        }
        sample.Origin=root;
        foreach(double entry in new[]{-.02,0,.04,.2,.3})
        {
            sample.TargetReferenceFrame.Origin=frame.Y*entry;
            result=ManualCalibrationMotion.Solve(sample,tip);
            drive.Reset();drive.Update(result.Normalized,.02,AxisMappingMode.Manual);
            Near(drive.Values[0],Math.Clamp(entry/.2,0,1),"endpoint boundaries are direct and bounded "+entry);
            Point(ManualCalibrationMotion.Preview(sample,result).Sleeve.Origin,sample.TargetReferenceFrame.Origin,"out-of-span anchor remains visible for calibration");
        }
        drive.Minimum[0]=.25;drive.Maximum[0]=.8;
        drive.Reset();drive.Update(new[]{1d,.5,.5,.5,.5,.5},.02,AxisMappingMode.Manual);
        Near(drive.Values[0],.25,"manual still respects explicit output limits");
        drive.Update(new[]{0d,.5,.5,.5,.5,.5},.02,AxisMappingMode.Manual);
        check(drive.Values[0]>.25&&drive.Values[0]<=.29+1e-8,"Manual: existing output slew remains active");
        foreach(var invalid in new[]{new MotionVector(),new MotionVector(.0009,0,0),new MotionVector(.6,0,0),new MotionVector(double.NaN,0,0)})
        {
            bool rejected=false;try{ManualCalibrationMotion.Length(root,invalid);}catch(ArgumentException){rejected=true;}
            check(rejected,"Manual: invalid endpoint span rejected");
        }
        // Pelvis motion transforms both saved points without changing their span.
        var moved=ContactPreviewMotion.BodyFrame(frame,new MotionVector(1,2,3),37,21,-16);
        var savedOrigin=new MotionVector(-.03,.02,-.01);var savedEnd=new MotionVector(.17,.02,-.01);
        sample.ReferenceFrame=moved;sample.Origin=moved.Origin+moved.Y*savedOrigin.X+moved.Z*savedOrigin.Y+moved.X*savedOrigin.Z;
        var expectedEnd=moved.Origin+moved.Y*savedEnd.X+moved.Z*savedEnd.Y+moved.X*savedEnd.Z;
        Point(ManualCalibrationMotion.WorldEnd(sample,savedOrigin,savedEnd),expectedEnd,"endpoint follows male frame through translation and rotation");
        var calibration=new ChapterCalibration{Length=.2,L1=.05,L2=.06,R0Plane=24,R1Plane=-13,R2Plane=12.5,Origin=savedOrigin,ManualEnd=savedEnd};
        var book=new ChapterBook();book.Chapters.Add(new Chapter{Start=0,Source=1,Target=2,Opening=1,MappingMode=3,Manual=true,Calibration=calibration});
        var split=book.Split(3);Point(split.Calibration.ManualEnd.Value,savedEnd,"split keeps independent endpoint");
        split.Calibration.ManualEnd=new MotionVector(.18,.02,-.01);split.Calibration.Length=.21;
        Point(calibration.ManualEnd.Value,savedEnd,"split calibration is independently editable");
        var restored=new ChapterBook();restored.FromXml(book.ToXml());
        check(restored.Chapters.Count==2&&restored.At(1).MappingMode==3,"Manual: mode and chapters round trip");
        Point(restored.At(1).Calibration.Origin,savedOrigin,"origin survives save/reload");
        Point(restored.At(1).Calibration.ManualEnd.Value,savedEnd,"endpoint survives save/reload");
        Near(restored.At(1).Calibration.R0Plane,24,"manual R0 plane angle survives save/reload");
        Near(restored.At(1).Calibration.R1Plane,-13,"manual R1 plane angle survives save/reload");
        Near(restored.At(1).Calibration.R2Plane,12.5,"manual R2 plane angle survives save/reload");
        Near(restored.At(4).Calibration.Length,.21,"each chapter retains its own player length");
        var automatic=new ChapterBook();automatic.Chapters.Add(new Chapter{Start=0,MappingMode=2,Calibration=new ChapterCalibration{Length=.18,L1=.09,L2=.09}});
        restored.FromXml(automatic.ToXml());
        check(restored.At(0).MappingMode==2&&!restored.At(0).Calibration.ManualEnd.HasValue,"Manual: existing automatic calibration remains readable");
        Near(restored.At(0).Calibration.R0Plane,0,"old calibration defaults to a neutral R0 plane");
        Near(restored.At(0).Calibration.R1Plane,0,"old calibration defaults to a neutral R1 plane");
        Near(restored.At(0).Calibration.R2Plane,0,"old calibration defaults to a neutral R2 plane");

        // A 1 mm calibration must keep the whole normalized stroke, including
        // in a rotated/transformed scene; it is not a 20 mm model in the UI.
        var tinyFrame=ContactPreviewMotion.BodyFrame(frame,new MotionVector(3,-2,7),31,22,-17);
        var tiny=new NativeMotionSample{Origin=tinyFrame.Origin,ReferenceFrame=tinyFrame,HasShaftPose=true,HasTargetFrame=true,
            PlaneWidth=.002,PlaneDepth=.002,LateralSpan=1};
        var tinyEnd=tiny.Origin+tinyFrame.Y*.001;
        var tinyDrive=new StudioDrive{SmoothingSeconds=0,MaximumChangePerSecond=0};
        foreach(double fraction in new[]{0d,.25,.5,.75,1})
        {
            tiny.TargetReferenceFrame=new AlignmentFrame{Origin=tiny.Origin+tinyFrame.Y*(fraction*.001),X=tinyFrame.X,Y=tinyFrame.Y,Z=tinyFrame.Z};
            var measured=ManualCalibrationMotion.Solve(tiny,tinyEnd);
            tinyDrive.Update(measured.Normalized,.02,AxisMappingMode.Manual);
            Near(measured.Length,.001,"1 mm survives world-space precision");
            Near(tinyDrive.Values[0],fraction,"1 mm retains direct full-range L0 at "+fraction);
            var view=ManualCalibrationMotion.Preview(tiny,measured);
            Point(view.Sleeve.Origin,tiny.TargetReferenceFrame.Origin,"1 mm keeps the observed entry fixed");
            Point(view.RodRoot+view.RodDirection*view.RodLength,tinyEnd,"1 mm preview keeps the configured endpoint");
        }
        var tinyBook=new ChapterBook();tinyBook.Chapters.Add(new Chapter{Start=0,MappingMode=3,
            Calibration=new ChapterCalibration{Length=.001,L1=.001,L2=.001,Origin=dragOrigin,ManualEnd=dragOrigin+dragAxis*.001}});
        restored.FromXml(tinyBook.ToXml());
        Near(restored.At(0).Calibration.Length,.001,"1 mm chapter persists after XML round trip");
        Near(ManualCalibrationMotion.Length(restored.At(0).Calibration.Origin,restored.At(0).Calibration.ManualEnd.Value),.001,"1 mm endpoints persist after XML round trip");
        Near(StrokeMapping.Depth(new NativeMotionResult{Length=.001,TargetDepth=.0005,Engagement=1,MaterialInsertion=.5},.001,.13,.12,AxisMappingMode.Soft),.0005/.12,"automatic mapping accepts the same 1 mm model/player span");
    }
}
