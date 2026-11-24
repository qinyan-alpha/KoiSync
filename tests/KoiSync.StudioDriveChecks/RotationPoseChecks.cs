using KoiSync.Core;

static class RotationPoseChecks
{
    public static void Run(Action<bool,string> check)
    {
        var identity=AlignmentFrame.Build(new MotionVector(),new MotionVector(1,0,0),new MotionVector(0,1,0));
        var tip=new MotionVector(0,.2,0);var entry=new MotionVector(0,.1,0);
        var sample=new NativeMotionSample{HasShaftPose=true,HasTargetFrame=true,Studio=true,
            Origin=new MotionVector(),End=tip,ReferenceFrame=identity,ShaftPath=new[]{new MotionVector(),tip},
            ShaftRestLength=.2,PlaneWidth=.2,PlaneDepth=.2,LateralSpan=1,ShaftYaw=new ContinuousAngle(180)};
        foreach(bool halfTurn in new[]{false,true})
        foreach(double pitch in new[]{-110.0,-90.1,-90,-89.9,89.9,90,90.1,110})
        foreach(double twist in new[]{-12.0,0,12,95})
        {
            var expected=ContactPreviewMotion.BodyFrame(identity,entry,twist,0,pitch);
            sample.TargetReferenceFrame=StudioShaftMotion.TwistFrame(expected,halfTurn);
            sample.ShaftTwistHalfTurn=halfTurn;
            sample.ShaftYaw.Resolve(twist);
            foreach(var measured in new[]{ManualCalibrationMotion.Solve(sample,tip),NativeMotion.Solve(sample)})
            {
                var reconstructed=ContactPreviewMotion.BodyFrame(measured.DeviceFrame,entry,measured.Raw[3],measured.Raw[4],measured.Raw[5]);
                check((reconstructed.X-expected.X).Length<1e-6&&(reconstructed.Y-expected.Y).Length<1e-6&&(reconstructed.Z-expected.Z).Length<1e-6,
                    "Reclined pose keeps a coherent full rotation at pitch="+pitch+", twist="+twist+", reversed="+halfTurn);
                // Allow one device count for the reference linkage's numerical
                // offsets at the exactly vertical singular pose.
                check(Math.Abs(measured.Normalized[3]-(.5-twist/240))<1.0/999,
                    "R0 retains actual twist through the Euler pitch boundary: "+pitch+" / "+twist+" = "+measured.Normalized[3]);
            }
        }
    }
}
