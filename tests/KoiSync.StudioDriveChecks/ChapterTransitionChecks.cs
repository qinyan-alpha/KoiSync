using KoiSync.Studio;

internal static class ChapterTransitionChecks
{
    public static void Run(Action<bool,string> check)
    {
        var fade=new ChapterTransition{Seconds=.5,Threshold=.08};
        var oldPose=new[]{.2,.5,.5,.5,.5,.5};var newPose=new[]{.8,.5,.3,.5,.5,.5};
        var frame=(double[])oldPose.Clone();fade.Apply(frame,1.0/60,false,false);
        check(Same(frame,oldPose),"No transition without a chapter boundary");

        frame=(double[])newPose.Clone();fade.Apply(frame,1.0/60,true,false);
        check(Same(frame,oldPose)&&fade.Active,"Large chapter jump starts from the previous output");
        check(System.Math.Abs(fade.LastJump-.6)<1e-9,"Largest axis jump is reported");

        double previous=frame[0],largestStep=0;bool monotonic=true;
        for(int i=0;i<40;i++)
        {
            frame=(double[])newPose.Clone();fade.Apply(frame,1.0/60,false,false);
            largestStep=System.Math.Max(largestStep,frame[0]-previous);monotonic&=frame[0]>=previous-1e-12;previous=frame[0];
        }
        check(monotonic&&largestStep<.6/30*1.6,"Fade is monotonic and spread over the duration");
        check(!fade.Active&&Same(frame,newPose),"Fade ends exactly on the new chapter pose");

        // The new chapter's own motion continues underneath the fading offset.
        var moving=new ChapterTransition{Seconds=.5,Threshold=.08};
        var a=new[]{.2,.5,.5,.5,.5,.5};moving.Apply(a,.02,false,false);
        var b=new[]{.8,.5,.5,.5,.5,.5};moving.Apply(b,.02,true,false);
        var c=new[]{.9,.5,.5,.5,.5,.5};moving.Apply(c,.25,false,false);
        check(System.Math.Abs(c[0]-(.9-.6*.5))<1e-9,"Half way keeps half of the jump and all new motion");

        var small=new ChapterTransition{Seconds=.5,Threshold=.08};
        var s1=new[]{.5,.5,.5,.5,.5,.5};small.Apply(s1,.02,false,false);
        var s2=new[]{.55,.5,.5,.5,.5,.5};small.Apply(s2,.02,true,false);
        check(!small.Active&&System.Math.Abs(s2[0]-.55)<1e-12,"Jump below the threshold passes through unchanged");

        var held=new ChapterTransition{Seconds=.5,Threshold=.08};
        var h1=new[]{.2,.5,.5,.5,.5,.5};held.Apply(h1,.02,false,true);
        var h2=new[]{.9,.5,.9,.5,.5,.5};held.Apply(h2,.02,true,true);
        check(System.Math.Abs(h2[0]-.9)<1e-12&&System.Math.Abs(h2[2]-.5)<1e-12,"Device-side L0 oscillation is not offset; other axes still fade");

        var reset=new ChapterTransition{Seconds=.5,Threshold=.08};
        var r1=new[]{.2,.5,.5,.5,.5,.5};reset.Apply(r1,.02,false,false);reset.Reset();
        var r2=new[]{.9,.5,.5,.5,.5,.5};reset.Apply(r2,.02,true,false);
        check(!reset.Active&&System.Math.Abs(r2[0]-.9)<1e-12,"Scene reset forgets the previous output");

        var chained=new ChapterTransition{Seconds=.5,Threshold=.08};
        var k1=new[]{.1,.5,.5,.5,.5,.5};chained.Apply(k1,.02,false,false);
        var k2=new[]{.9,.5,.5,.5,.5,.5};chained.Apply(k2,.02,true,false);chained.Apply(k2=new[]{.9,.5,.5,.5,.5,.5},.1,false,false);
        double before=k2[0];var k3=new[]{.2,.5,.5,.5,.5,.5};chained.Apply(k3,.02,true,false);
        check(System.Math.Abs(k3[0]-before)<1e-12,"A second boundary during a fade continues from the current output");
    }
    private static bool Same(double[] a,double[] b){for(int i=0;i<6;i++)if(System.Math.Abs(a[i]-b[i])>1e-12)return false;return true;}
}
