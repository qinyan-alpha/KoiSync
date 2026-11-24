using KoiSync.Core;
using KoiSync.Wire;
using System.Globalization;

internal static class NativeStrokeChecks
{
    internal static void Run(Action<bool,string> check,Func<NativeMotionSample> fixture,string[] args)
    {
        var settings=new NativeStrokeProfile();settings.Validate();
        foreach(string state in new[]{"WLoop","A_WLoop"})check(settings.Phase(state)==NativeStrokePhase.Normal,"Normal phase "+state);
        foreach(string state in new[]{"SLoop","MLoop","OLoop","A_OLoop"})check(settings.Phase(state)==NativeStrokePhase.Deep,"Deep phase "+state);
        foreach(string state in new[]{"M_IN_Start","M_IN_Loop","WF_IN_Loop","A_SS_IN_Start","Orgasm","IN_Loop"})check(settings.Phase(state)==NativeStrokePhase.Climax,"Climax phase "+state);
        foreach(string state in new[]{"Pull","OUT_Start","OUT_Loop","OUT_A","M_OUT_Start","M_OUT_Loop","A_M_OUT_Start"})check(settings.Phase(state)==NativeStrokePhase.Out,"Pull-out phase "+state);
        foreach(string state in new[]{"Idle","A_Idle","InsertIdle","IN_A","SS_IN_A","WS_IN_A"})check(settings.Phase(state)==NativeStrokePhase.Idle,"No-interaction phase "+state);
        check(settings.Phase("Insert")==NativeStrokePhase.Measured,"Insertion action keeps the raw measurement");
        check(settings.Phase("unknown")==NativeStrokePhase.Measured,"Unknown state stays on raw measurement");
        settings.StrongAsDeep=false;check(settings.Phase("SLoop")==NativeStrokePhase.Normal,"Player may keep strong loops in normal range");settings.StrongAsDeep=true;
        var saved=new MeasurementProfile();var copy=saved.Clone();copy.NativeStroke.NormalMaximum=777;check(saved.NativeStroke.NormalMaximum==800,"Animation stage configuration clone is independent");
        settings.ClimaxChangesPerSecond=double.NaN;bool rejected=false;try{settings.Validate();}catch(ArgumentException){rejected=true;}check(rejected,"Invalid frequency rejected");settings.ClimaxChangesPerSecond=10;
        // Pull-out ceiling, idle warm-up motion and climax pull-in (mapper contract; the pipeline clamps).
        var guard=new NativeStrokeMapper();var probe=fixture();probe.Studio=false;probe.Stroke=settings;probe.Gain=1;
        probe.AnimationState="OUT_Loop";
        guard.Map(probe,.95,.95,1d/60,true);
        check(guard.Ceiling<=.801,"Pull-out exposes the configured L0 ceiling");
        settings.OutLimitEnabled=false;guard.Reset();guard.Map(probe,.95,.95,1d/60,true);
        check(guard.Ceiling>=.999,"Disabling the pull-out limit removes the ceiling");settings.OutLimitEnabled=true;
        var idle=new NativeStrokeMapper();probe.AnimationState="Idle";double idleLow=1,idleHigh=0,idleMinRotation=1,idleMaxRotation=0;
        for(int i=0;i<260;i++){double v=idle.Map(probe,.5,.5,1d/60,true);idleLow=Math.Min(idleLow,v);idleHigh=Math.Max(idleHigh,v);idleMinRotation=Math.Min(idleMinRotation,idle.RotationR0);idleMaxRotation=Math.Max(idleMaxRotation,idle.RotationR0);}
        check(idleLow*999>=395&&idleHigh*999<=605,"Idle keeps a gentle 400-600 round trip");
        check(idle.HasRotation&&idleMinRotation*999>=445&&idleMaxRotation*999<=555,"Idle sweeps R0/R1 inside 450-550");
        var pull=new NativeStrokeMapper();probe.AnimationState="M_IN_Loop";
        pull.Map(probe,.95,.95,1d/60,true);
        check(pull.Ceiling<=.301,"Climax exposes the pull-in ceiling when the animation drifts high");
        check(pull.Scale<=.301,"Climax remaps the whole animation into 000-300 instead of clipping it");
        check(pull.HasRotation,"Climax sweeps R0/R1 while L0 is held low");
        var sample=fixture();sample.Studio=false;sample.Stroke=settings;sample.AnimationState="WLoop";sample.StableAnimation=true;sample.Gain=3;
        var pipe=new MotionPipeline{Minimum=0,Maximum=1};var c=new Candidate{Id="normal",Active=true,Native=sample};
        var f=new Snapshot{Session="stroke",Scene="main",Active=true};
        void Step(double progress,double distance,double length=.2,bool active=true)
        {f.Active=active;f.Sequence++;f.SampleTime+=1d/60;sample.AnimationProgress=progress;sample.Target=new(0,distance,0);sample.End=new(0,length,0);pipe.Update(f,c,0,StrokeAxis.Z);}
        double low=1,high=0;
        for(int i=0;i<=300;i++)
        {
            Step(i/60d,.08+.02*Math.Sin(i*Math.PI/30));
            if(i>240){low=Math.Min(low,pipe.Desired[0]);high=Math.Max(high,pipe.Desired[0]);}
        }
        check(Math.Abs((high-low)*999-400)<30,"A 200-travel ordinary loop is raised to the 400 travel target");
        check(high*999<640&&low*999>160,"Raised travel stays anchored around the captured midpoint");
        check(pipe.Arm(),"Expanded loop remains armable");
        sample.AnimationState="SLoop";low=1;high=0;var statusLog=new System.Text.StringBuilder();
        for(int i=0;i<=300;i++){Step(i/60d,.09+.03*Math.Sin(i*Math.PI/30));if(i>240){low=Math.Min(low,pipe.Desired[0]);high=Math.Max(high,pipe.Desired[0]);}if(i%60==0)statusLog.Append("["+i+"]"+pipe.NativeStrokeStatus+" ");}
        check(Math.Abs((high-low)*999-600)<45,"A 300-travel strong loop is raised to the 600 travel target ["+((high-low)*999).ToString("F1")+" | "+statusLog+"]");
        check(pipe.Armed,"Changing loop phase does not end synchronization");
        settings.ClimaxFollowsGame=false;
        sample.AnimationState="M_IN_Start";Step(0,.1);check(Math.Abs(pipe.Desired[0]*999-400)<.01,"Fully custom climax starts at its configured endpoint");
        low=1;high=0;int switches=0;double previous=pipe.Desired[0];
        for(int i=1;i<=60;i++){if(i==15)sample.AnimationState="M_IN_Loop";Step(i/60d,.1);if(Math.Abs(previous-pipe.Desired[0])>.01)switches++;previous=pipe.Desired[0];low=Math.Min(low,pipe.Values[0]);high=Math.Max(high,pipe.Values[0]);}
        check(switches==10,"10 changes per second, no restart at Start to Loop transition");
        check(low*999<401&&high*999>599,"Climax reaches approximately 400–600 after slew limiting");
        previous=pipe.Desired[0];for(int i=0;i<120;i++)Step(1,.1,.2,false);
        check(Math.Abs(previous-pipe.Desired[0])<1e-8&&!pipe.Armed,"Paused phase freezes the simulated clock and stops output");
        sample.AnimationState="IN_A";Step(0,.1);check(pipe.Desired[0]>=.4&&pipe.Desired[0]<=.601,"After-state enters the idle warm-up motion");
        settings.IdleMotionEnabled=false;pipe.Reset("test");Step(0,.1);
        check(Math.Abs(pipe.Desired[0]-.5)<1e-8,"Disabling idle motion restores measured L0");settings.IdleMotionEnabled=true;
        sample.AnimationState="M_IN_Loop";settings.ClimaxEnabled=false;pipe.Reset("test");Step(0,.1);
        check(pipe.Desired[0]<=.301,"Climax pull-in keeps capping L0 while the simulation is off");
        settings.ClimaxPullInEnabled=false;pipe.Reset("test");Step(0,.1);
        check(Math.Abs(pipe.Desired[0]-.5)<1e-8,"Disabling climax handling restores measured L0");
        settings.ClimaxEnabled=true;settings.ClimaxPullInEnabled=true;
        sample.Maximum[0]=.55;sample.Multipliers[0]=2;for(int i=0;i<20;i++)Step(i/60d,.1);
        check(pipe.Desired[0]<=.55&&pipe.Values[0]<=.55,"Player multipliers and limits also apply to climax");
        sample.Maximum[0]=1;sample.Multipliers[0]=1;
        sample.AnimationState="OUT_Loop";Step(0,.19);check(pipe.Desired[0]<=.801,"Pipeline clamps the pull-out phase at the ceiling");
        sample.AnimationState="Idle";double idlePipeLow=1,idlePipeHigh=0;
        for(int i=0;i<260;i++){Step(i/60d,.1);idlePipeLow=Math.Min(idlePipeLow,pipe.Desired[0]);idlePipeHigh=Math.Max(idlePipeHigh,pipe.Desired[0]);}
        check(idlePipeLow*999>=395&&idlePipeHigh*999<=605,"Pipeline emits the idle round trip in the 400-600 range");
        check(pipe.Values[3]>=.445&&pipe.Values[3]<=.555,"Pipeline applies the idle R0 sweep");
        sample.AnimationState="M_IN_Loop";settings.ClimaxFollowsGame=true;Step(0,.19);check(pipe.Desired[0]<=.301,"Pipeline clamps the climax at the pull-in ceiling");
        sample.AnimationState="WLoop";
        var mapper=new NativeStrokeMapper();sample=fixture();sample.Studio=false;sample.Stroke=settings;sample.StableAnimation=true;sample.AnimationState="WLoop";settings.Memorized.Clear();
        double mapped=0;
        for(int i=0;i<=120;i++){sample.AnimationProgress=i/60d;sample.Target=new(0,.07+i%2*.00001,0);mapped=mapper.Map(sample,.35,.35,1d/60,true);}
        check(mapped==.35,"Static pose and tiny jitter do not manufacture a full stroke");
        mapper.Reset();settings.Memorized.Clear();
        for(int i=0;i<180;i++)mapper.Map(sample,0.2+0.1*Math.Sin(i*Math.PI/30),0.2,1d/60,true);
        check(settings.Memorized.Count>=3,"Captured strokes are remembered in the animation profile");
        mapper.Reset();
        double seeded=mapper.Map(sample,.35,.35,1d/60,true);
        check(Math.Abs(seeded-.35)>.01,"A remembered profile enhances from the first frame");
        mapper.Reset();settings.Memorized.Clear();
        sample.FixedRange=true;
        settings.AutoRange=false;check(mapper.Map(sample,.35,.66,.02,true)==.66,"Fixed range mapping still feeds the output while enhancement is off");settings.AutoRange=true;
        check(mapper.Map(sample,.35,.66,.02,true)==.35,"Enhancement always measures the raw geometric L0");sample.FixedRange=false;
        sample.Studio=true;check(mapper.Map(sample,.35,.42,.02,true)==.42,"Studio is unchanged by main-game stage settings");
        sample.Studio=false;sample.Contact=new StudioContactReference();check(mapper.Map(sample,.35,.43,.02,true)>=.35,"Contact geometry remains the base of main-game mean enhancement");sample.Contact=null;
        mapper.Reset();sample.StableAnimation=false;
        for(int i=0;i<=120;i++){sample.AnimationProgress=i/60d;sample.Target=new(0,i%2*.1,0);mapped=mapper.Map(sample,.35,.35,.02,true);}
        check(mapped==.35,"Animator transition telemetry no longer feeds the travel estimator");
        mapper.Reset();sample.StableAnimation=true;
        for(int i=0;i<=120;i++){sample.AnimationProgress=i/60d;sample.Target=new(0,.08+.02*Math.Sin(i*Math.PI/30),0);sample.End=sample.Target*(1/.96);mapped=mapper.Map(sample,.96,.96,1d/60,true);}
        sample.Target=new(0,.1,0);check(Math.Abs(mapper.Map(sample,.96,.96,.02,true)-.96)<.00001,"Already high-valued animation is not remapped to arbitrary endpoints");
        settings.AutoRange=false;check(mapper.Map(sample,.2,.27,.02,true)==.27,"Disabling enhancement keeps the previous mapping");settings.AutoRange=true;
        settings.ClimaxFollowsGame=false;
        foreach(var scenario in new[]{(state:"WLoop",mean:.2,amplitude:.1,travel:400d),(state:"WLoop",mean:.55,amplitude:.1,travel:400d),(state:"SLoop",mean:.35,amplitude:.3,travel:600d)})
        {
            mapper.Reset();sample.AnimationState=scenario.state;double lo=1,hi=0;
            for(int i=0;i<240;i++)
            {
                double value=scenario.mean+scenario.amplitude*Math.Sin(i*Math.PI/30);
                double result=mapper.Map(sample,value,value,1d/60,true);if(i>=180){lo=Math.Min(lo,result);hi=Math.Max(hi,result);}
            }
            double raw=2*scenario.amplitude;
            check(Math.Abs((hi-lo)-Math.Max(raw,scenario.travel/999d))<.03,"Average stroke travel follows its target without lowering sufficient values: "+scenario.state+" / "+scenario.mean);
            check(hi<=1&&lo>=0,"Enhanced travel stays inside the channel range");
        }
        foreach(string phase in new[]{"WLoop","SLoop"})
        {
            mapper.Reset();sample.AnimationState=phase;double lo=1,hi=0;
            for(int i=0;i<240;i++)
            {
                double value=(100+40*Math.Sin(i*Math.PI/30))/999;
                double result=mapper.Map(sample,value,value,1d/60,true);if(i>=180){lo=Math.Min(lo,result);hi=Math.Max(hi,result);}
            }
            double target=(phase=="WLoop"?400d:600d)/999d;
            check(Math.Abs(mapper.AverageTravel*999-80)<15,"Stroke travel is measured from the local extremes: "+phase);
            check(Math.Abs((hi-lo)-target)<target*.15,"A small-travel native loop reaches its travel target: "+phase);
        }
        mapper.Reset();sample.AnimationState="WLoop";double sparseLow=1,sparseHigh=0;
        for(int i=0;i<40;i++)
        {
            sample.AnimationProgress=i%2==0?.7:.3;
            double result=mapper.Map(sample,(i%2==0?.08:.12),.1,.5,true);
            if(i>=30){sparseLow=Math.Min(sparseLow,result);sparseHigh=Math.Max(sparseHigh,result);}
        }
        check(sparseHigh-sparseLow>.2,"Sparse native sampling still recognizes strokes and enhances their travel");
        var lateral=fixture();var original=NativeMotion.Solve(lateral);lateral.Target=new(.09,.03,.12);original=NativeMotion.Solve(lateral);lateral.LateralSpan=3;var wide=NativeMotion.Solve(lateral);
        check(Math.Abs(wide.Normalized[1]-.5-(original.Normalized[1]-.5)/3)<1e-8&&Math.Abs(wide.Normalized[2]-.5-(original.Normalized[2]-.5)/3)<1e-8,"Threefold translation span divides both lateral displacements by three");
        check(wide.Raw[1]==original.Raw[1]&&wide.Raw[2]==original.Raw[2]&&wide.Normalized[4]==original.Normalized[4],"Expanded L1/L2 mapping preserves raw distances and R1 angles");
        var curve=new MotionCurve();curve.Add(.5,1,new[]{.2,.3,.4,.5,.6,.7},"A");check(curve.At(.5).Values[0]==.2&&curve.At(.2)==null,"Timeline keeps unmeasured frames absent instead of inventing zeros");
        curve.Add(.5,1,new[]{.8,.3,.4,.5,.6,.7},"B");check(curve.Count==1&&curve.At(.5).Source=="B","Replayed frame replaces its old source/value");curve.Clear();check(curve.Count==0,"Changing animation clears its curve");
        if(args.Length==2)Replay(args[0],args[1],check);
    }
    private static void Replay(string input,string output,Action<bool,string> check)
    {
        var groups=File.ReadLines(input).Skip(1).Select(l=>l.Split('\t')).Where(r=>(r[3]=="VAGINAL"||r[3]=="ANAL")&&new[]{"WLoop","SLoop","MLoop","OLoop"}.Contains(r[4])).GroupBy(r=>string.Join("\t",r.Take(5)));
        var report=new List<string>{"mode\tid\tname\tprofile\tstate\trawMin999\trawMax999\tnewMin999\tnewMax999\trootSpan\tbaseMean999\tnewMean999\tresult"};
        int expanded=0,stationary=0;
        foreach(var group in groups)
        {
            var rows=group.ToArray();var sample=new NativeMotionSample{Stroke=new NativeStrokeProfile(),RootDistance=true,StableAnimation=true,AnimationState=rows[0][4]};var mapper=new NativeStrokeMapper();
            double lo=1,hi=0,dlo=double.MaxValue,dhi=0,rawLo=double.MaxValue,rawHi=0,sum=0,baseSum=0;
            for(int cycle=0;cycle<3;cycle++)for(int i=0;i<60;i++)
            {
                var r=rows[i];double N(int col)=>double.Parse(r[col],CultureInfo.InvariantCulture);
                sample.Origin=new(N(13),N(14),N(15));sample.Target=new(N(16),N(17),N(18));sample.AnimationProgress=cycle+i/60d;
                double distance=(sample.Target-sample.Origin).Length;dlo=Math.Min(dlo,distance);dhi=Math.Max(dhi,distance);rawLo=Math.Min(rawLo,N(6));rawHi=Math.Max(rawHi,N(6));
                double value=mapper.Map(sample,N(6),N(6),1d/60,true);if(cycle==2){lo=Math.Min(lo,value);hi=Math.Max(hi,value);sum+=value;baseSum+=Math.Clamp(N(6),0,1);}
            }
            bool moving=rawHi-rawLo>.00001;double target=(sample.Stroke.Phase(sample.AnimationState)==NativeStrokePhase.Deep?600d:400d)/999d;
            if(moving)
            {
                double rawTravel=rawHi-rawLo,newTravel=hi-lo;
                check(newTravel>=rawTravel-.0001&&(rawTravel>=target-.0001||newTravel>=target*.8),"Recorded loop keeps sufficient travel or reaches its target: "+group.Key);
                expanded++;
            }else stationary++;
            report.Add(group.Key+"\t"+string.Join("\t",new[]{rawLo*999,rawHi*999,lo*999,hi*999,dhi-dlo,baseSum/60*999,sum/60*999,(rawHi-rawLo)*999,(hi-lo)*999}.Select(n=>n.ToString("F4",CultureInfo.InvariantCulture)))+"\t"+(moving?"travel checked":"static; unchanged"));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));File.WriteAllLines(output,report);
        Console.WriteLine("Recorded rig replay: "+expanded+" moving loop travels checked, "+stationary+" static loops unchanged; "+output);
    }
}
