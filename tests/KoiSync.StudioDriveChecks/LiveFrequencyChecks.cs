using KoiSync.Core;
using KoiSync.Studio;

internal static class LiveFrequencyChecks
{
    public static void Run(Action<bool,string> check)
    {
        foreach(double hz in new[]{.5,2.0,10})
        {
            var cycle=new LiveL0Cycle();
            for(int i=0;i<=3000;i++)
            {
                double t=i*.002;
                cycle.Observe(t,.02*Math.Sin(2*Math.PI*hz*t),true,.0001);
            }
            check(cycle.Ready&&Math.Abs(cycle.Hertz-hz)<hz*hz*.0021,"Actual cycle frequency within one sample period "+hz);
            check(Math.Abs(cycle.Phase-.5)<.001,"Actual position determines phase "+hz);
            double held=cycle.Phase;
            for(int i=1;i<=1000;i++)cycle.Observe(6+i*.002,0,true,.0001);
            check(Math.Abs(cycle.Phase-held)<.001&&cycle.Hertz==0,"Stopped geometry does not synthesize motion "+hz);
            cycle.Observe(8.002,0,false,.0001);
            check(cycle.Ready&&Math.Abs(cycle.Phase-held)<.001,"Pause retains phase "+hz);
            cycle.Observe(8.002,0,false,.0001);
            check(cycle.Ready,"Repeated paused frame retains observed range "+hz);
            cycle.Observe(1,0,true,.0001);
            check(!cycle.Ready,"Backward seek invalidates old extrema "+hz);
        }
        var noise=new LiveL0Cycle();
        for(int i=0;i<1000;i++)noise.Observe(i*.01,Math.Sin(i)*.00001,true,.0001);
        check(!noise.Ready&&noise.Hertz==0,"Subthreshold jitter cannot create cycles");
        noise.Observe(double.NaN,0,true,.0001);
        check(!noise.Ready,"Invalid sample resets estimator");
        var book=new ChapterBook();
        book.Observe(0,new Chapter{Source=1,Target=2,Manual=true});
        book.MarkClimax(1,5,10,"1:2:0",new ClimaxOptions(),"fixture");
        var effect=book.At(2).Climax;effect.FrequencyMode=ClimaxFrequencyMode.LiveL0;effect.Hertz=3.7;
        var restored=new ChapterBook();restored.FromXml(book.ToXml());
        check(restored.At(2).Climax.FrequencyMode==ClimaxFrequencyMode.LiveL0&&restored.At(2).Climax.Hertz==3.7,"Chapter frequency round trip");
        restored.Split(3).Climax.Hertz=7;
        check(restored.At(2).Climax.Hertz==3.7,"Split chapters own independent frequency");
        check(effect.Create(1,5,0,1)==null,"Actual mode never creates synthetic oscillation");
        var legacy=new System.Xml.XmlDocument();legacy.LoadXml("<climaxOptions enabled='true' min='350' max='750' hz='4.5'/>");
        var old=ClimaxOptions.Read(legacy.DocumentElement);
        check(old.FrequencyMode==ClimaxFrequencyMode.Fixed&&old.Hertz==4.5,"Legacy chapters retain fixed frequency");
        var fallback=new ClimaxOptions{Minimum=181,Maximum=691,Enabled=false,Detect=false,MaximumSeconds=8};
        var migrated=ClimaxOptions.Resolve(old,fallback);
        check(migrated.Minimum==181&&migrated.Maximum==691&&!migrated.Enabled&&!migrated.Detect&&migrated.MaximumSeconds==8&&migrated.Hertz==4.5,"Legacy settings preserve effective global values and chapter frequency");
        migrated.Enabled=true;migrated.Minimum=200;migrated.Maximum=650;migrated.MaximumSeconds=3;
        restored.At(2).Climax=migrated;string saved=restored.ToXml();
        var reloaded=new ChapterBook();reloaded.FromXml(saved);
        var local=ClimaxOptions.Resolve(reloaded.At(2).Climax,new ClimaxOptions());
        check(local.Enabled&&!local.Detect&&local.Minimum==200&&local.Maximum==650&&local.MaximumSeconds==3,"All local effect settings survive save and reload without global override");
        check(reloaded.At(3).Climax.Minimum!=200,"Editing one chapter does not change its neighbour");
        reloaded.Split(2.5).Climax.Maximum=400;
        check(reloaded.At(2).Climax.Maximum==650,"Split chapter ranges are independent");
        check(!ClimaxOptions.Equivalent(reloaded.At(2).Climax,reloaded.At(2.5).Climax),"Different local ranges cannot be compacted together");
        reloaded.FromXml(saved);
        check(reloaded.At(2).Climax.Maximum==650,"History snapshot restores chapter output settings");
        var localPulse=local.Create(1,4,0,1);
        check(Math.Abs(localPulse.Minimum-200d/999)<1e-12&&Math.Abs(localPulse.Maximum-650d/999)<1e-12,"Per-chapter limits drive the generated pulse");
        var d=new StudioDrive();d.Update(new[]{.2,.4,.5,.6,.5,.5},.02);
        double input=d.Input[0],side=d.Values[1];d.ApplyL0EffectValue(.75);
        check(d.Values[0]==.75&&d.Input[0]==input&&d.Values[1]==side,"Actual phase replaces only final L0");
    }
}
