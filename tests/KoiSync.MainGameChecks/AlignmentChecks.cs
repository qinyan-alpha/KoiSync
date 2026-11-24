using System.Xml.Linq;
using KoiSync.Core;
using KoiSync.MainGame;
using Studio = KoiSync.Studio;

internal static class AlignmentChecks
{
    private static readonly AlignmentFrame Identity=AlignmentFrame.Build(new(),new(1,0,0),new(0,1,0));
    private static bool Close(double a,double b,double tolerance=1e-9)=>Math.Abs(a-b)<tolerance;
    private static bool Close(MotionVector a,MotionVector b)=>(a-b).Length<1e-9;

    internal static void Run(Action<bool,string> check,string root)
    {
        EntryAndPinning(check);
        EndpointsAndPreview(check);
        StudioParity(check);
        MigrateProfiles(check,root);
        DeepBottomOutAndFeelTranslation(check);
    }

    private static void EntryAndPinning(Action<bool,string> check)
    {
        check(ActionEntry.MatchesNative(NativeTarget.VAGINAL,true,true,"k_f_kokan_00","cf_j_kokan"),"Use the current authored vaginal null");
        check(ActionEntry.MatchesNative(NativeTarget.ANAL,true,true,"k_f_ana_00","cf_j_ana"),"Use the current authored anal null");
        check(ActionEntry.MatchesNative(NativeTarget.ORAL,true,true,"k_f_head_00","cf_s_head"),"Use the current authored oral null");
        check(!ActionEntry.MatchesNative(NativeTarget.ORAL,false,true,"k_f_head_00","cf_s_head"),"An inactive attachment does not replace anatomy");
        check(!ActionEntry.MatchesNative(NativeTarget.VAGINAL,true,false,"k_f_kokan_00","cf_j_kokan"),"Never borrow the other actor's native null");
        check(!ActionEntry.MatchesNative(NativeTarget.VAGINAL,true,true,"k_f_ana_00","cf_j_ana"),"The anal null cannot drive the vaginal branch");
        check(!ActionEntry.MatchesNative(NativeTarget.BREASTS,true,true,"k_f_kokan_00","cf_j_kokan"),"A native insertion null cannot override a surface binding");

        var landmark=Identity;landmark.Origin=new(0,.1123356,0);
        var authored=Identity;authored.Origin=new(0,.1,0);
        var entry=new ActionEntry(landmark,authored,new(),"native");
        var path=new MotionVector[]{new(),new(0,.05,0),new(0,.0999999,0)};
        var sample=Sample(entry,.1776528,path);
        var profile=new ActionProfile{Mode=AxisMappingMode.Soft,GameLength=.13};
        var drive=new GameDrive();
        var unpinned=ActionMotion.Solve(sample,new(),profile.Mode,false);
        check(unpinned.MaterialInsertion==0&&unpinned.Normalized[0]==0,"No material crossing leaves an unbound L0 withdrawn");

        sample.HasNativeTipContact=true;
        var pinned=ActionMotion.Solve(sample,new(),profile.Mode,false);
        ActionMotion.Drive(drive,sample,pinned,profile,true,.1384,.12,.016);
        check(pinned.MaterialInsertion==0&&pinned.Engagement>.999&&drive.Values[0]<.55,"A matching pinned tip drives depth without fabricating material");
        check(Close(pinned.Length,.1776528)&&Close(pinned.VisibleLength,.0999999),"Fixed model length and compressed visible chain remain distinct");
        check(Close((entry.Landmark.Origin-entry.Observed.Origin).Length,.0123356),"The historical landmark gap remains separately measurable");
        var far=new ActionEntry(landmark,authored,new(0,0,.12),"native");
        var distant=Sample(far,sample.ShaftRestLength,path);distant.HasNativeTipContact=true;
        var detached=ActionMotion.Solve(distant,new(),profile.Mode,false);
        check(detached.Engagement==0&&detached.Normalized[0]==0,"A pin is bounded by the finite chain distance");
    }

    private static void EndpointsAndPreview(Action<bool,string> check)
    {
        var observed=ContactPreviewMotion.BodyFrame(Identity,new(0,.05,0),25,10,-12);
        var entry=new ActionEntry(observed,observed,new(.002,.003,-.005),"landmark");
        check(Close(entry.Observed.Origin,observed.Origin),"Target calibration preserves the measured world anchor");
        check(Close(entry.Calibrated.Origin-observed.Origin,observed.Y*.002+observed.Z*.003-observed.X*.005),
            "Entry adjustment follows the target rigid frame and axis order");
        var calibration=new ActionCalibration{Length=.18,L1=.08,L2=.05,ManualEnd=new(.20,0,0)};
        var profile=new ActionProfile{Mode=AxisMappingMode.Manual,GameLength=.25};
        var drive=new GameDrive{SmoothingSeconds=0,MaximumChangePerSecond=0};
        var path=new MotionVector[]{new(),new(0,.17,0)};

        double Position(double travel,ActionCalibration c,double player=.035)
        {
            var reference=Identity;reference.Origin=new(0,travel,0);
            var e=new ActionEntry(reference,reference,new(),"fixture");
            var sample=Sample(e,.18,path);
            var pose=ActionMotion.Solve(sample,c,profile.Mode,true);
            ActionMotion.Drive(drive,sample,pose,profile,false,player,.12,.016);
            return drive.Values[0];
        }
        check(Close(Position(0,calibration),0)&&Close(Position(.2,calibration),1),"L0 is flipped once from inward depth to outward output");
        check(Close(Position(.06,calibration)-Position(.05,calibration),.05),"10 mm across a 200 mm span maps to 5 percent");
        var shortCalibration=calibration.Copy();
        shortCalibration.SetEndpoints(new(),new(.1,0,0),calibration.Span(.18));
        check(Close(shortCalibration.Length,.18)&&Close(shortCalibration.Span(.18),.1),"Manual tip edits do not overwrite the model length");
        check(Close(shortCalibration.L1,.04)&&Close(shortCalibration.L2,.025),"Linked half ranges scale once with endpoint distance");
        check(Close(Position(.06,shortCalibration)-Position(.05,shortCalibration),.1),"10 mm across a 100 mm span maps to 10 percent");
        check(Close(Position(.05,shortCalibration,.035),Position(.05,shortCalibration,.3)),"Manual L0 is independent of the automatic player length");
        shortCalibration.Linked=false;
        var existingEnd=shortCalibration.ManualEnd.Value;
        var origin=ManualCalibrationMotion.MoveOriginAlongAxis(shortCalibration.Origin,existingEnd,.025);
        shortCalibration.SetEndpoints(origin,existingEnd,shortCalibration.Span(.18));
        check(Close(shortCalibration.ManualEnd.Value,existingEnd)&&Close(shortCalibration.Span(.18),.075)&&Close(shortCalibration.L1,.04),
            "The source-local tip stays fixed during origin editing and unlinked ranges remain fixed");

        var observedEntry=Sample(entry,.18,path);
        var solved=ActionMotion.Solve(observedEntry,calibration,profile.Mode,true);
        var geometry=ManualCalibrationMotion.Preview(observedEntry,solved,new MotionVector(14,-6,8));
        var output=ContactPreviewMotion.Solve(observedEntry,GameDrive.PreviewAxes(new[]{.85,.1,.9,.55,.7,.2}),.23,solved,.12);
        check(Close(geometry.Sleeve.Origin,entry.Calibrated.Origin)&&Close(observedEntry.AnchorTarget,entry.Observed.Origin),
            "Rotating the manual plane leaves observation and the calibrated entry in place");
        check((output.Sleeve.Origin-entry.Calibrated.Origin).Length>.02,"Output reconstruction exposes a real residual rather than moving the entry");
        check(ActionEntry.AngleBetween(geometry.Sleeve,output.Sleeve)>1,"Output orientation residual is measurable independently");
    }

    private static void StudioParity(Action<bool,string> check)
    {
        var profile=new ActionProfile{GameLength=.122};
        var game=new GameDrive{SmoothingSeconds=.07,MaximumChangePerSecond=.9};
        var studio=new Studio.StudioDrive{SmoothingSeconds=.07,MaximumChangePerSecond=.9};
        game.Gain[4]=studio.Gain[4]=.85;game.Minimum[1]=studio.Minimum[1]=.18;
        foreach(AxisMappingMode mode in new[]{AxisMappingMode.Absolute,AxisMappingMode.Soft,AxisMappingMode.Relative})
        {
            profile.Mode=mode;game.Reset();studio.Reset();profile.Rotation=null;
            Studio.ChapterRotationReference studioReference=null;
            for(int i=0;i<6;i++)
            {
                var target=ContactPreviewMotion.BodyFrame(Identity,new(.004,.082+i*.005,.003),20+i*3,7+i,12-i);
                var sample=Sample(new ActionEntry(target,target,new(),"fixture"),.16,new MotionVector[]{new(),new(0,.145,0)});
                var measured=ActionMotion.Solve(sample,new(),mode,false);
                if(i==0&&mode==AxisMappingMode.Relative)
                {
                    profile.Rotation=ActionRotationReference.Capture(measured,0);
                    studioReference=Studio.ChapterRotationReference.Capture(measured,0);
                }
                ActionMotion.Drive(game,sample,measured,profile,true,.108,.12,.016);
                studio.PhysicalL0Minimum=1-.108/.12;
                var normalized=(double[])measured.Normalized.Clone();
                normalized[0]=Studio.StrokeMapping.Depth(measured,.108,.122,.12,(Studio.AxisMappingMode)mode);
                if(studioReference!=null)normalized=studioReference.Apply(measured,normalized);
                else Array.Copy(SR6DeviceRotation.Positions(measured.Raw[3],measured.Raw[4],measured.Raw[5],1-normalized[0],normalized[1],normalized[2]),0,normalized,3,3);
                studio.Update(normalized,.016,(Studio.AxisMappingMode)mode);
                check(game.Values.Zip(studio.Values).All(pair=>Close(pair.First,pair.Second)),"Same six mapped and filtered values as Studio "+mode+" frame "+i);
            }
        }
        var before=(double[])game.Values.Clone();
        var next=new[]{.8,.7,.6,.5,.4,.3};
        var queue=new MotionDelayBuffer();
        queue.Add(0,.99,1,before,null);queue.Add(.02,1.01,1,next,null);
        var delayed=queue.Read(.02,.01,out _);
        check(delayed.Zip(before.Zip(next)).All(pair=>Close(pair.First,(pair.Second.First+pair.Second.Second)*.5)),
            "The send buffer survives a continuous native loop when given unwrapped time");
        check(game.Values.SequenceEqual(before),"The device-only delay leaves the recorded final values intact");
    }

    private static void MigrateProfiles(Action<bool,string> check,string root)
    {
        var definition=new ActionDefinition{Key="migration-fixture",Name="Catalogue name",WeakSource=ActorSlot.Male1,Contact=NativeTarget.ORAL};
        string folder=ActionProfile.Folder(root,definition.Key);Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,"action.xml");
        File.WriteAllText(path,"""
          <action schema="1" solver="main-0.1.5" key="migration-fixture" name="Custom action name" source="Male" target="Female0" effector="Shaft" contact="ORAL" mode="Manual" manualBinding="True" gameLength="0.142" centeredMotion="False" referenceReady="False" weakSource="Male1">
            <calibration length="0.08" l1="0.035" l2="0.06" axial="0.012" originL1="-0.01" originL2="0.005" endAxial="0.092" endL1="-0.01" endL2="0.005" linked="false" />
            <rotationReference time="0.22" yaw="24" roll="-8" pitch="12" />
            <sourceCalibration source="Male1">
              <calibration length="0.16" l1="0.09" l2="0.07" axial="0.01" originL1="0.01" originL2="0" linked="true" />
              <rotationReference time="0.29" yaw="-31" roll="3" pitch="-2" />
            </sourceCalibration>
          </action>
          """);
        var migrated=ActionProfile.Load(root,definition);
        check(migrated.Name=="Custom action name"&&migrated.ManualBinding&&migrated.Mode==AxisMappingMode.Manual&&Close(migrated.GameLength,.142),
            "Schema 1 keeps the saved action identity, explicit mode and interaction length");
        check(migrated.Calibration.Length==0&&Close(migrated.Calibration.Span(.19),.08)&&!migrated.Calibration.Linked,
            "Schema 1 explicit endpoints keep their exact span and no longer become an automatic length override");
        check(Close(migrated.Calibration.Origin,new(.012,-.01,.005))&&Close(migrated.Rotation.Yaw,24),"Schema 1 source-local origin and fixed rotation survive");
        migrated.SelectCalibration(ActorSlot.Male1);
        check(Close(migrated.Calibration.Length,.16)&&Close(migrated.Calibration.Span(.18),.16)&&Close(migrated.Rotation.Yaw,-31),
            "Legacy implicit endpoint and alternate rotation remain available");
        migrated.SelectCalibration(ActorSlot.Male);
        migrated.SetEntryOffset(NativeTarget.ORAL,new(.002,-.005,.008));
        migrated.SetEntryOffset(NativeTarget.ANAL,new(-.001,0,0));
        migrated.Calibration.PlaneDegrees=new(8,9,-24);
        migrated.Calibration.VirtualAnchor="test source";
        migrated.Calibration.VirtualFrame=AlignmentFrame.Build(new(.21,-.05,.1),new(0,0,1),new(0,1,0));
        migrated.Save(root);
        var saved=XDocument.Load(path).Root!;
        var reload=ActionProfile.Load(root,definition);
        check((string)saved.Attribute("schema")=="2"&&(string)saved.Attribute("solver")=="main-0.2.0",
            "Schema 2 identifies the shipping solver");
        check(Close(reload.Calibration.PlaneDegrees,new(8,9,-24))&&Close(reload.EntryOffset(NativeTarget.ORAL),new(.002,-.005,.008))&&
            Close(reload.EntryOffset(NativeTarget.ANAL),new(-.001,0,0)),"Manual plane and per-contact entry offsets survive reload");
        check(reload.Calibration.VirtualAnchor=="test source"&&Close(reload.Calibration.VirtualFrame.Origin,new(.21,-.05,.1))&&
            Close(reload.Calibration.VirtualFrame.X,new(0,0,1)),"Virtual origin and basis reattach in source coordinates");
        reload.SelectCalibration(ActorSlot.Male1);
        check(Close(reload.Calibration.Span(.18),.16)&&Close(reload.Rotation.Yaw,-31),"Schema 2 retains the second source's calibration");
    }

    private static void DeepBottomOutAndFeelTranslation(Action<bool,string> check)
    {
        // 1. Layer-1 deep bottom-out (cowgirl SLoop): entry drops 50 mm past root with 100% material inside
        var path=new[]{new MotionVector(0,0,0),new MotionVector(0,.09,0),new MotionVector(0,.1777,0)};
        var buriedEntry=new ActionEntry(
            AlignmentFrame.Build(new(.012,-.048,0),new(1,0,0),new(0,1,0)),
            AlignmentFrame.Build(new(.012,-.048,0),new(1,0,0),new(0,1,0)),new(),"native");
        var buried=Sample(buriedEntry,.1777,path);buried.HasNativeTipContact=true;
        var buriedRes=ActionMotion.Solve(buried,new(),AxisMappingMode.Soft,false);
        check(buriedRes.Engagement>.99&&buriedRes.ContactDistance<.020,"Deep bottom-out cowgirl stays engaged via radial shaft distance instead of dropping to 0");

        // 2. Layer-1 soft-tissue breast tolerance
        var breastEntry=new ActionEntry(
            AlignmentFrame.Build(new(.022,.08,0),new(1,0,0),new(0,1,0)),
            AlignmentFrame.Build(new(.022,.08,0),new(1,0,0),new(0,1,0)),new(),"bust");
        var breastSample=Sample(breastEntry,.1777,path);breastSample.ContactTolerance=.055;
        var breastRes=ActionMotion.Solve(breastSample,new(),AxisMappingMode.Soft,false);
        check(breastRes.Engagement>.99,"Soft-tissue breast contact tolerance prevents engagement sag at 22 mm");

        // 3. Layer-2 FeelTranslator: Raw is exact pass-through
        var feel=new FeelTranslator();
        var rawAxes=new[]{.4,.5,.5,.5,.5,.5};
        var s=new NativeMotionSample{AnimationState="SLoop",AnimationDuration=.55};
        var m=new NativeMotionResult{Length=.1777,TargetDepth=.07,Engagement=1,MaterialInsertion=.4,Normalized=rawAxes};
        check(ReferenceEquals(feel.Translate(rawAxes,m,s,FeelPreset.Raw,false,.016),rawAxes),"Raw preset is an exact pass-through");

        // 4. Layer-2 FeelTranslator: shallow 18 mm stroke expands cleanly into full 0..99 window without exceeding 99
        feel.Reset();
        double minDevL0=1,maxDevL0=0;
        for(int i=0;i<90;i++)
        {
            double phase=2*Math.PI*i/30.0;
            double depth=.045+.009*Math.Sin(phase); // 36..54 mm (18 mm shallow stroke)
            var mm=new NativeMotionResult{Length=.1777,TargetDepth=depth,Engagement=1,MaterialInsertion=.2,Normalized=new[]{depth/.1777,.5,.5,.5,.5,.5}};
            var outAx=feel.Translate(mm.Normalized,mm,s,FeelPreset.Balanced,false,.018);
            if(i>=35){double devL0=1-outAx[0];minDevL0=Math.Min(minDevL0,devL0);maxDevL0=Math.Max(maxDevL0,devL0);}
        }
        check(maxDevL0<=.995&&(maxDevL0-minDevL0)>=.52,"Balanced feel expands shallow 18 mm SLoop into usable >=52-unit stroke inside full 0..99 range");

        // 5. Layer-2 FeelTranslator: deep bottom-out cowgirl (215..228 mm > 177.7 mm) wakes up from clamped flatline
        feel.Reset();
        minDevL0=1;maxDevL0=0;
        for(int i=0;i<90;i++)
        {
            double phase=2*Math.PI*i/30.0;
            double depth=.2215+.0065*Math.Sin(phase); // 215..228 mm buried bounce
            var mm=new NativeMotionResult{Length=.1777,TargetDepth=depth,Engagement=1,MaterialInsertion=1,Normalized=new[]{1.0,.5,.5,.5,.5,.5}};
            var outAx=feel.Translate(mm.Normalized,mm,s,FeelPreset.Balanced,false,.018);
            if(i>=35){double devL0=1-outAx[0];minDevL0=Math.Min(minDevL0,devL0);maxDevL0=Math.Max(maxDevL0,devL0);}
        }
        check(minDevL0<=.05&&(maxDevL0-minDevL0)>=.55,"Un-squished deep bottom-out bounce wakes up deep-anchored at L0~0 with full rhythmic travel");

        // 6. Layer-2 FeelTranslator: static pose (< 1 mm noise) never fabricates motion
        feel.Reset();
        double staticSpanMin=1,staticSpanMax=0;
        for(int i=0;i<60;i++)
        {
            double depth=.090+.0003*Math.Sin(i);
            var mm=new NativeMotionResult{Length=.1777,TargetDepth=depth,Engagement=1,MaterialInsertion=.5,Normalized=new[]{.5,.5,.5,.5,.5,.5}};
            var outAx=feel.Translate(mm.Normalized,mm,s,FeelPreset.Intense,false,.018);
            staticSpanMin=Math.Min(staticSpanMin,outAx[0]);staticSpanMax=Math.Max(staticSpanMax,outAx[0]);
        }
        check((staticSpanMax-staticSpanMin)<.01,"Static pose stays static without inventing noise strokes");

        // 7. Layer-2 FeelTranslator: Climax locks deep inside 0..26 device L0
        feel.Reset();
        var sc=new NativeMotionSample{AnimationState="IN_Loop",AnimationDuration=.22};
        double cMaxL0=0;
        for(int i=0;i<60;i++)
        {
            double depth=.11+.02*Math.Sin(i*.6);
            var mm=new NativeMotionResult{Length=.1777,TargetDepth=depth,Engagement=1,MaterialInsertion=.6,Normalized=new[]{.6,.5,.5,.5,.5,.5}};
            var outAx=feel.Translate(mm.Normalized,mm,sc,FeelPreset.Intense,false,.016);
            if(i>=20)cMaxL0=Math.Max(cMaxL0,1-outAx[0]);
        }
        check(cMaxL0<=.265,"Climax stage deep-locks device L0 inside <=26% bottom zone");

        // 8. Layer-2 FeelTranslator: Foreplay stays gentle inside outer/surface window [0.22..0.99]
        feel.Reset();
        var sf=new NativeMotionSample{AnimationState="aibu",AnimationDuration=1.0};
        double fMinL0=1,fMaxL0=0;
        for(int i=0;i<80;i++)
        {
            double depth=.065+.025*Math.Sin(2*Math.PI*i/40.0);
            var mm=new NativeMotionResult{Length=.130,TargetDepth=depth,Engagement=1,MaterialInsertion=.5,Normalized=new[]{.5,.5,.5,.5,.5,.5}};
            var outAx=feel.Translate(mm.Normalized,mm,sf,FeelPreset.Balanced,true,.025);
            if(i>=35){double devL0=1-outAx[0];fMinL0=Math.Min(fMinL0,devL0);fMaxL0=Math.Max(fMaxL0,devL0);}
        }
        check(fMinL0>=.20&&fMaxL0<=.995&&(fMaxL0-fMinL0)>=.32,"Foreplay stays in gentle surface window [20..99] without slamming to deep bottom");

        // 9. Layer-2 FeelTranslator: Plan A fast non-shaft SLoop preserves deep [0..22] headroom for insertion contrast
        feel.Reset();
        var sFastNonShaft=new NativeMotionSample{AnimationState="SLoop",AnimationDuration=.60};
        double nsMinL0=1,nsMaxL0=0;
        for(int i=0;i<80;i++)
        {
            double depth=.065+.025*Math.Sin(2*Math.PI*i/32.0);
            var mm=new NativeMotionResult{Length=.130,TargetDepth=depth,Engagement=1,MaterialInsertion=.5,Normalized=new[]{.5,.5,.5,.5,.5,.5}};
            var outAx=feel.Translate(mm.Normalized,mm,sFastNonShaft,FeelPreset.Balanced,true,NonShaftFeelMode.Foreplay,.018);
            if(i>=35){double devL0=1-outAx[0];nsMinL0=Math.Min(nsMinL0,devL0);nsMaxL0=Math.Max(nsMaxL0,devL0);}
        }
        check(nsMinL0>=.25&&nsMaxL0<=.995&&(nsMaxL0-nsMinL0)>=.38,"Plan A Foreplay SLoop stays >=26% outer window, leaving 0..25% exclusively for true insertion contrast");

        // 10. Layer-2 FeelTranslator: Plan B FullSync reaches full 0..99 range; Silent parks gently at outer L0=99
        feel.Reset();
        double fsMinL0=1,fsMaxL0=0;
        for(int i=0;i<80;i++)
        {
            double depth=.065+.025*Math.Sin(2*Math.PI*i/32.0);
            var mm=new NativeMotionResult{Length=.130,TargetDepth=depth,Engagement=1,MaterialInsertion=.5,Normalized=new[]{.5,.5,.5,.5,.5,.5}};
            var outAx=feel.Translate(mm.Normalized,mm,sFastNonShaft,FeelPreset.Balanced,true,NonShaftFeelMode.FullSync,.018);
            if(i>=35){double devL0=1-outAx[0];fsMinL0=Math.Min(fsMinL0,devL0);fsMaxL0=Math.Max(fsMaxL0,devL0);}
        }
        check(fsMinL0<=.16&&(fsMaxL0-fsMinL0)>=.65,"Plan B FullSync unlocks full insertion-scale stroke range on non-shaft poses");

        var silentOut=feel.Translate(rawAxes,m,sFastNonShaft,FeelPreset.Balanced,true,NonShaftFeelMode.Silent,.018);
        check(Math.Abs((1-silentOut[0])-FeelTranslator.DefaultOuterCeiling)<.001&&Math.Abs(silentOut[5]-.5)<.001,"Non-shaft Silent mode parks cleanly at outer L0=99 with neutral rotations");

        // 11. Layer-2 FeelTranslator: Stale startup spike recovers rapidly without crushing Balanced amplitude
        feel.Reset();
        var sWLoop=new NativeMotionSample{AnimationState="WLoop",AnimationDuration=1.333};
        double recMinL0=1,recMaxL0=0;
        for(int i=0;i<160;i++)
        {
            double dt=.025;
            double depth=i<3?.185:.065+.015*Math.Sin(2*Math.PI*i/53.0); // huge initial 185mm posture spike then steady +-15mm oscillation
            var mm=new NativeMotionResult{Length=.130,TargetDepth=depth,Engagement=1,MaterialInsertion=.5,Normalized=new[]{.5,.5,.5,.5,.5,.5}};
            var outAx=feel.Translate(mm.Normalized,mm,sWLoop,FeelPreset.Balanced,true,NonShaftFeelMode.Foreplay,dt);
            if(i>=100){double devL0=1-outAx[0];recMinL0=Math.Min(recMinL0,devL0);recMaxL0=Math.Max(recMaxL0,devL0);}
        }
        check((recMaxL0-recMinL0)>=.44&&recMinL0>=.25&&recMaxL0<=.995,"Stale transition spike sheds quickly within ~1.5 cycles so Balanced WLoop recovers full Plan A amplitude");

        // 12. Layer-2 FeelTranslator: Slow 0.75 Hz SS_IN_Loop gets rich deep throb instead of tiny 33 u/s micro-creep
        feel.Reset();
        var sSlowIn=new NativeMotionSample{AnimationState="SS_IN_Loop",AnimationDuration=1.333};
        double slowInMin=1,slowInMax=0;
        for(int i=0;i<100;i++)
        {
            double depth=.11+.03*Math.Sin(2*Math.PI*i/50.0);
            var mm=new NativeMotionResult{Length=.1777,TargetDepth=depth,Engagement=1,MaterialInsertion=.6,Normalized=new[]{.6,.5,.5,.5,.5,.5}};
            var outAx=feel.Translate(mm.Normalized,mm,sSlowIn,FeelPreset.Balanced,false,.025);
            if(i>=40){double devL0=1-outAx[0];slowInMin=Math.Min(slowInMin,devL0);slowInMax=Math.Max(slowInMax,devL0);}
        }
        check(slowInMin<=.06&&(slowInMax-slowInMin)>=.38,"Slow 0.75 Hz SS_IN_Loop produces rich deep pulsing instead of collapsing into a tiny 22-unit micro-flutter");

        // 13. AI DirectorModulator: Multi-step edging sequence, smooth ramping, EdgeGuard brake, TTL watchdog, and immediate abort
        var dir=new DirectorModulator();
        dir.StartSequence("寸止测试",new[]{
            new DirectorStep{Name="深插冲刺",DurationSeconds=1.0,RampSeconds=.2,Mode=DirectorMode.Envelope,L0Min=.00,L0Max=.95,GameStage="SLoop",GameSpeed=.9},
            new DirectorStep{Name="悬停挑逗",DurationSeconds=1.0,RampSeconds=.2,Mode=DirectorMode.Hold,HoldL0=.76,FlutterHz=4.0,FlutterAmp=.04,GameStage="WLoop",GameSpeed=.2}
        });
        DirectorActionEvent evFirst=null,evSecond=null;
        double step1Min=1,step1Max=0,step2Min=1,step2Max=0;
        for(int i=0;i<130;i++)
        {
            double inDevL0=.495+.48*Math.Sin(2*Math.PI*i/20.0);
            var phys=new[]{1-inDevL0,.5,.5,.5,.5,.5};
            DirectorActionEvent ev;
            var res=dir.Apply(phys,.02,30,30,out ev);
            if(ev!=null){if(evFirst==null)evFirst=ev;else evSecond=ev;}
            double devL0=1-res[0];
            if(i>=15&&i<45){step1Min=Math.Min(step1Min,devL0);step1Max=Math.Max(step1Max,devL0);}
            if(i>=70&&i<95){step2Min=Math.Min(step2Min,devL0);step2Max=Math.Max(step2Max,devL0);}
        }
        check(evFirst!=null&&evFirst.GameStage=="SLoop"&&evSecond!=null&&evSecond.GameStage=="WLoop","Director sequence emits per-step game control events cleanly");
        check((step1Max-step1Min)>=.60&&step2Min>=.66&&step2Max<=.84,"Director sequence drives full deep stroke in step 1 then smoothly holds outer flutter tease in step 2");
        check(!dir.Active,"Director sequence smoothly fades back to Passthrough after final step finishes");

        // 14. AI DirectorModulator: Real-time 60Hz EdgeGuard triggers instantaneously on gauge >= 85%
        dir.ConfigureEdgeGuard(new DirectorEdgeGuard{Enabled=true,FemaleThreshold=85,MaleThreshold=85,BrakeDurationSeconds=2.0,BrakeL0Min=.54,BrakeL0Max=.80,BrakeStage="WLoop",BrakeSpeed=.15});
        DirectorActionEvent egEvent=null;
        double egMinL0=1;
        for(int i=0;i<40;i++)
        {
            double inDevL0=.40+.40*Math.Sin(2*Math.PI*i/15.0);
            DirectorActionEvent ev;
            var res=dir.Apply(new[]{1-inDevL0,.5,.5,.5,.5,.5},.02,i>=5?88:40,30,out ev);
            if(ev!=null&&egEvent==null)egEvent=ev;
            if(i>=25)egMinL0=Math.Min(egMinL0,1-res[0]);
        }
        check(egEvent!=null&&egEvent.Reason=="edge_guard_brake"&&egEvent.GameStage=="WLoop"&&egEvent.RollbackFemaleMax.HasValue&&egEvent.RollbackFemaleMax.Value<=65&&egEvent.RollbackMaleMax.HasValue&&egEvent.RollbackMaleMax.Value<=65&&dir.EdgeBrakeActive&&egMinL0>=.50,"60Hz EdgeGuard fires immediately on gauge>=85%, rolls back gauges below OLoop threshold, and restricts L0 to safe outer tease window");
        dir.AbortImmediate("F9 test");
        check(!dir.Active&&!dir.EdgeBrakeActive,"AbortImmediate instantaneously clears AI override and EdgeGuard brake");

        // 15. PhaseDepthMap: learns circular 1-cycle depth profile and maps arbitrary depths accurately
        var pmap=new PhaseDepthMap();
        for(int i=0;i<64;i++)
        {
            double p=i/64.0;
            // Synthetic cowgirl cycle: shallowest at p=0.25 (30 mm), deepest at p=0.75 (160 mm)
            double d=.095-.065*Math.Sin(2*Math.PI*p);
            pmap.Observe("sonyu_4|SLoop",p,d);
        }
        check(pmap.Ready&&Math.Abs(pmap.ShallowPhase-.25)<.04&&Math.Abs(pmap.DeepPhase-.75)<.04,"PhaseDepthMap accurately identifies shallowest and deepest phases");
        double pTip=pmap.MapDepthToPhase(.05,true,true);
        double pMid=pmap.MapDepthToPhase(.50,true,true);
        double pDeep=pmap.MapDepthToPhase(.95,true,true);
        check(pTip>=.22&&pTip<=.36&&pMid>=.44&&pMid<=.56&&pDeep>=.64&&pDeep<=.78,"PhaseDepthMap maps tip/mid/deep smoothly onto the monotonic inward arc");

        // 16. TimelineEngine: TipRub (high-freq shallow), NineShallowOneDeep (9 shallow + 1 deep plunge), DeepGrind (R0/R1/R2 circular waist grind)
        var tl=new TimelineEngine();
        tl.Start("变式全测",new[]{
            TimelineStep.FromPreset(TimelinePreset.TipRub,1.2),
            TimelineStep.FromPreset(TimelinePreset.DeepGrind,1.2)
        });
        double trMaxDepth=0,trMinL0=1,dgMinDepth=1,dgR0Min=1,dgR0Max=0,dgR1Min=1,dgR1Max=0;
        for(int i=0;i<140;i++)
        {
            var fr=tl.Step(.02,pmap,null);
            if(!fr.Active)continue;
            if(i>=12&&i<55){trMaxDepth=Math.Max(trMaxDepth,fr.DepthNorm);trMinL0=Math.Min(trMinL0,fr.DeviceL0);}
            if(i>=75&&i<115)
            {
                dgMinDepth=Math.Min(dgMinDepth,fr.DepthNorm);
                dgR0Min=Math.Min(dgR0Min,fr.PhysicalAxes[3]);dgR0Max=Math.Max(dgR0Max,fr.PhysicalAxes[3]);
                dgR1Min=Math.Min(dgR1Min,fr.PhysicalAxes[4]);dgR1Max=Math.Max(dgR1Max,fr.PhysicalAxes[4]);
            }
        }
        check(trMaxDepth<=.30&&trMinL0>=.45,"TipRub strictly restricts BOTH animation depth and SR6 L0 to shallow tip zone");
        check(dgMinDepth>=.72&&(dgR0Max-dgR0Min)>=.30&&(dgR1Max-dgR1Min)>=.35,"DeepGrind locks animation deep while generating rich circular R0 twist + R1/R2 waist grind");
        check(!tl.Active,"TimelineEngine cleanly terminates and signals actor restore after final variant finishes");

        // 17. TimelineEngine: Upgraded yellow-band NineShallowOneDeep executes substantial shallow strokes (~44% depth) then plunges full-stroke to bottom (L0~0)
        var nsod=TimelineStep.FromPreset(TimelinePreset.NineShallowOneDeep,8.0);
        nsod.ShallowCount=3;nsod.FreqHz=2.5;nsod.DeepHoldSeconds=.4;
        tl.Start("三浅一深快测",new[]{nsod});
        double first3Min=1,first3Max=0,plungeMax=0,plungeMinL0=1;
        for(int i=0;i<220;i++)
        {
            var fr=tl.Step(.02,pmap,null);
            if(i>=8&&i<52){first3Min=Math.Min(first3Min,fr.DepthNorm);first3Max=Math.Max(first3Max,fr.DepthNorm);}
            if(i>=60&&i<145){plungeMax=Math.Max(plungeMax,fr.DepthNorm);plungeMinL0=Math.Min(plungeMinL0,fr.DeviceL0);}
        }
        check(first3Max>=.34&&first3Max<=.50&&(first3Max-first3Min)>=.28&&plungeMax>=.92&&plungeMinL0<=.08,"NineShallowOneDeep delivers yellow-band shallow strokes then plunges full-stroke to bottom (L0~0)");

        // 18. TimelineEngine: Loop=true never expires + TuneActive mutates parameters live without stopping or popping
        var loopStep=TimelineStep.FromPreset(TimelinePreset.DeepGrind,1.0);
        loopStep.Loop=true;loopStep.GrindPattern=GrindPattern.Figure8;
        tl.Start("循环8字扭腰",new[]{loopStep},true);
        double f8LatMin=1,f8LatMax=-1,f8FwdMin=1,f8FwdMax=-1,lagDiffSum=0;
        for(int i=0;i<120;i++)
        {
            var fr=tl.Step(.02,pmap,null);
            if(i==60)check(tl.TuneActive(s=>{s.GrindHz=2.2;s.GrindPattern=GrindPattern.Circle;s.BodyGrindScale=1.2;}),"TuneActive succeeds live on active loop");
            if(i>=15&&i<58)
            {
                f8LatMin=Math.Min(f8LatMin,fr.GrindLat);f8LatMax=Math.Max(f8LatMax,fr.GrindLat);
                f8FwdMin=Math.Min(f8FwdMin,fr.GrindFwd);f8FwdMax=Math.Max(f8FwdMax,fr.GrindFwd);
                lagDiffSum+=Math.Abs(fr.GrindYaw-fr.GrindYawLag);
            }
        }
        check(tl.Active,"Loop=true keeps TimelineEngine alive well past nominal DurationSeconds");
        check((f8LatMax-f8LatMin)>=1.2&&(f8FwdMax-f8FwdMin)>=.9&&lagDiffSum>1.0,"Procedural 3D body-grind kinematics emit full Figure-8 / Circle orbit + non-zero 0.11s phase-lagged spine wave");
        tl.Stop("end loop test");

        // 19. FeelTranslator: Configurable OuterCeiling (cup anti-slip limit) replaces hardcoded 80%
        feel.Reset();feel.OuterCeiling=.90;
        var sCeil=new NativeMotionSample{Studio=true,HasShaftPose=true,HasTargetFrame=true,ShaftRestLength=.1777,AnimationState="SLoop"};
        double ceilMaxL0=0;
        for(int i=0;i<160;i++)
        {
            double depth=.07+.05*Math.Sin(2*Math.PI*i/25.0);
            var mm=new NativeMotionResult{Length=.1777,TargetDepth=depth,Engagement=1,MaterialInsertion=.5,Normalized=new[]{.5,.5,.5,.5,.5,.5}};
            var outAx=feel.Translate(mm.Normalized,mm,sCeil,FeelPreset.Balanced,false,.025);
            if(i>=40)ceilMaxL0=Math.Max(ceilMaxL0,1-outAx[0]);
        }
        check(ceilMaxL0>=.85&&ceilMaxL0<=.91,"Configurable FeelTranslator.OuterCeiling=0.90 extends outer stroke ceiling cleanly to ~90%");
        feel.OuterCeiling=FeelTranslator.DefaultOuterCeiling;

        // 20. PhaseDepthMap: SetMoverExcursion auto-detects Female mover (cowgirl) vs Male mover (missionary) & ignores tiny <18mm drift
        pmap.SetMoverExcursion(.18,.01,true);
        check(pmap.MoverMeasured&&pmap.FemaleMoverShare>=.85,"PhaseDepthMap detects Female active mover on cowgirl-like pelvis excursion");
        pmap.SetMoverExcursion(.015,.16,false);
        check(pmap.MoverMeasured&&pmap.FemaleMoverShare<=.15,"PhaseDepthMap detects Male active mover on missionary-like pelvis excursion");
        pmap.SetMoverExcursion(.007,.005,false);
        check(!pmap.MoverMeasured&&pmap.FemaleMoverShare<=.20,"PhaseDepthMap ignores sub-18mm passive drift and falls back to native isFemaleInitiative");

        // 21. TimelineEngine v0.2.2: ClimaxEjaculation 3-phase in-place sprint -> deep ejaculation pulses + decaying spasms -> afterglow
        var climaxStep=TimelineStep.FromPreset(TimelinePreset.ClimaxEjaculation,10.0,.85);
        tl.Start("高潮射精测",new[]{climaxStep});
        bool seenP1=false,seenP2=false,seenP3=false;
        double maxSpasmP2=0,endSpasmP3=1,minDepthP2Steady=1;
        int p2Frames=0,maxPulses=0;
        for(int i=0;i<500;i++)
        {
            var fr=tl.Step(.02,pmap,null);
            if(!fr.Active)continue;
            if(fr.ClimaxPhase==1)seenP1=true;
            if(fr.ClimaxPhase==2)
            {
                seenP2=true;p2Frames++;
                maxSpasmP2=Math.Max(maxSpasmP2,fr.SpasmAmp);
                if(p2Frames>8)minDepthP2Steady=Math.Min(minDepthP2Steady,fr.DepthNorm);
                maxPulses=Math.Max(maxPulses,fr.EjaculationPulseCount);
            }
            if(fr.ClimaxPhase==3){seenP3=true;endSpasmP3=fr.SpasmAmp;}
        }
        check(seenP1&&seenP2&&seenP3&&maxPulses>=4&&maxSpasmP2>=.85&&minDepthP2Steady>=.85&&endSpasmP3<=.12,"ClimaxEjaculation runs 3 phases with >=4 deep ejaculation pulses, strong decaying body spasms, and soft afterglow");

        // 22. TimelineEngine v0.2.2: Markov Organic stochastic engine smoothly transitions across contrasting sub-modes
        var orgStep=TimelineStep.FromPreset(TimelinePreset.Organic,30.0,.90);
        orgStep.OrganicSeed=42;orgStep.OrganicIntensity=.65;
        tl.Start("有机随机测",new[]{orgStep},true);
        var seenSubModes=new System.Collections.Generic.HashSet<string>();
        double orgMinL0=1,orgMaxL0=0;
        for(int i=0;i<1200;i++)
        {
            var fr=tl.Step(.025,pmap,null);
            if(fr.Active)
            {
                if(!string.IsNullOrEmpty(tl.OrganicSubMode))seenSubModes.Add(tl.OrganicSubMode);
                orgMinL0=Math.Min(orgMinL0,fr.DeviceL0);
                orgMaxL0=Math.Max(orgMaxL0,fr.DeviceL0);
            }
        }
        check(tl.Active&&tl.OrganicActive&&seenSubModes.Count>=3&&orgMinL0<=.22&&orgMaxL0>=.72,"Markov Organic engine smoothly weaves >=3 contrasting rhythm modes within [0..outerCeiling]");

        // 23. TimelineEngine v0.2.2: UpdateOuterCeiling live-syncs active step L0Max immediately
        var liveStep=TimelineStep.FromPreset(TimelinePreset.TipRub,10.0,.80);
        tl.Start("外限实时同步",new[]{liveStep});
        for(int i=0;i<30;i++)tl.Step(.02,pmap,null);
        tl.UpdateOuterCeiling(.95);
        double syncedMaxL0=0;
        for(int i=0;i<80;i++){var fr=tl.Step(.02,pmap,null);syncedMaxL0=Math.Max(syncedMaxL0,fr.DeviceL0);}
        check(syncedMaxL0>=.90,"UpdateOuterCeiling live-syncs active Timeline outer ceiling from 80% to 95% immediately");

        // 24. TimelineEngine v0.2.6: Generalized N-shallow M-deep (1浅1深) alternates shallow and deep strokes cleanly
        var oneS1D=TimelineStep.FromPreset(TimelinePreset.NineShallowOneDeep,8.0,.85);
        oneS1D.ShallowCount=1;oneS1D.DeepCount=1;oneS1D.FreqHz=2.0;oneS1D.DeepHoldSeconds=0.1;
        tl.Start("一浅一深",new[]{oneS1D});
        double s1Max=0,d1Max=0;
        for(int i=0;i<110;i++)
        {
            var fr=tl.Step(.02,pmap,null);
            if(i>=4&&i<=24)s1Max=Math.Max(s1Max,fr.DepthNorm);
            if(i>=35&&i<=105)d1Max=Math.Max(d1Max,fr.DepthNorm);
        }
        check(s1Max>=.32&&s1Max<=.52&&d1Max>=.92,"Generalized 1浅1深 executes 1 shallow stroke then 1 full deep plunge");

        // 25. TimelineEngine v0.2.6: WaistOnly + LineFB holds fixed depth and drives pure straight front-back (L1+R2) with zero circle/R0/R1!
        var waistStep=TimelineStep.FromPreset(TimelinePreset.WaistOnly,6.0,.85);
        waistStep.HoldDepth=0.85;waistStep.GrindPattern=GrindPattern.LineFB;waistStep.GrindAmp=0.38;waistStep.TwistAmp=0.0;
        tl.Start("定深直线前后摆",new[]{waistStep});
        double wMinD=1,wMaxD=0,wMaxLat=0,wMaxYaw=0,wMaxRoll=0,wMaxPitch=0;
        for(int i=0;i<120;i++)
        {
            var fr=tl.Step(.02,pmap,null);
            if(i>=35)
            {
                wMinD=Math.Min(wMinD,fr.DepthNorm);wMaxD=Math.Max(wMaxD,fr.DepthNorm);
                wMaxLat=Math.Max(wMaxLat,Math.Abs(fr.GrindLat));
                wMaxYaw=Math.Max(wMaxYaw,Math.Abs(fr.GrindYaw));
                wMaxRoll=Math.Max(wMaxRoll,Math.Abs(fr.GrindRoll));
                wMaxPitch=Math.Max(wMaxPitch,Math.Abs(fr.GrindPitch));
            }
        }
        check((wMaxD-wMinD)<=.02&&wMaxLat<=1e-6&&wMaxYaw<=1e-6&&wMaxRoll<=1e-6&&wMaxPitch>=.90,"WaistOnly+LineFB holds depth constant and drives pure straight front-back pitch/L1 without circle or R0/R1");
        tl.Stop("done v0.2.6 tests");

        // 26. Phase 1 PoseCatalogClassifier: Strict DirectPenetration gating + Option B facing-priority resolution + exact-match detection
        var sampleCat=new System.Collections.Generic.List<PoseCatalogEntry>{
            PoseCatalogClassifier.Classify("kplug_khs_f_60","kPlug · 女后背后仰骑乘位","khs_f_60","sonyu",60,"Shaft","VAGINAL",true,true,true),
            PoseCatalogClassifier.Classify("sonyu_1","開脚正常位","開脚正常位","sonyu",1,"Shaft","VAGINAL",false,true,false),
            PoseCatalogClassifier.Classify("sonyu_0","正常位","正常位","sonyu",0,"Shaft","VAGINAL",false,true,false),
            PoseCatalogClassifier.Classify("sonyu_23","種付けプレス","種付けプレス","sonyu",23,"Shaft","VAGINAL",false,true,false),
            PoseCatalogClassifier.Classify("sonyu_24","マングリ正常位","マングリ正常位","sonyu",24,"Shaft","VAGINAL",false,true,false),
            PoseCatalogClassifier.Classify("sonyu_4","騎乗位","騎乗位","sonyu",4,"Shaft","VAGINAL",false,true,true),
            PoseCatalogClassifier.Classify("sonyu_27","ソファ騎乗位","ソファ騎乗位","sonyu",27,"Shaft","VAGINAL",false,true,true),
            PoseCatalogClassifier.Classify("kplug_khs_f_133","kPlug · 蛙蹲骑乘","khs_f_133","sonyu",133,"Shaft","VAGINAL",true,true,true),
            PoseCatalogClassifier.Classify("sonyu_2","後背位","後背位","sonyu",2,"Shaft","VAGINAL",true,true,false),
            PoseCatalogClassifier.Classify("houshi_7","片手フェラ","片手フェラ","houshi",7,"Shaft","ORAL",false,true,false),
            PoseCatalogClassifier.Classify("aibu_1","仰向け愛撫","仰向け愛撫","aibu",1,"InteractionItem","VAGINAL",false,true,false)
        };
        var houshiEntry=sampleCat.Find(x=>x.Key=="houshi_7");
        var aibuEntry=sampleCat.Find(x=>x.Key=="aibu_1");
        var sonyuEntry=sampleCat.Find(x=>x.Key=="sonyu_0");
        check(sonyuEntry.IsDirectPenetration&&!houshiEntry.IsDirectPenetration&&!aibuEntry.IsDirectPenetration,"PoseCatalogClassifier strictly limits IsDirectPenetration to sonyu* Shaft VAGINAL/ANAL (excludes houshi & aibu)");

        var rMiss=PoseCatalogClassifier.ResolveQuery(sampleCat,"传教士位",true,true);
        check(rMiss.BestMatch!=null&&rMiss.BestMatch.Key=="sonyu_0"&&!rMiss.IsExactMatch&&rMiss.Alternatives.Count>=3,"Broad '传教士位' picks face-to-face classic sonyu_0, marks IsExactMatch=false, and returns sibling missionary variants");

        var rCow=PoseCatalogClassifier.ResolveQuery(sampleCat,"乘骑位",true,true);
        check(rCow.BestMatch!=null&&rCow.BestMatch.Key=="sonyu_4"&&rCow.BestMatch.Facing=="face_to_face"&&!rCow.IsExactMatch&&rCow.Alternatives.Count>=2,"Broad '乘骑位' prioritizes face-to-face sonyu_4 over back-facing kPlug cowgirl and returns sibling alternatives");

        var rExact=PoseCatalogClassifier.ResolveQuery(sampleCat,"开腿正常位",true,true);
        check(rExact.BestMatch!=null&&rExact.BestMatch.Key=="sonyu_1"&&rExact.IsExactMatch,"Specific '开腿正常位' pinpoints sonyu_1 with IsExactMatch=true");

        var rExactFrog=PoseCatalogClassifier.ResolveQuery(sampleCat,"蛙蹲骑乘",true,true);
        check(rExactFrog.BestMatch!=null&&rExactFrog.BestMatch.Key=="kplug_khs_f_133"&&rExactFrog.IsExactMatch,"Specific '蛙蹲骑乘' pinpoints kPlug pose kplug_khs_f_133 with IsExactMatch=true");

        // 27. Phase 3 SR6MathManifoldEngine: ALL 5 6-DOF mathematical trajectory families remain 100% inside SR6 reachable linkage workspace & C2 velocity bounds
        var mEng=new SR6MathManifoldEngine();
        var allFams=new[]{
            SR6MathFamily.VirtualPivotWave,
            SR6MathFamily.VariablePitchHelix,
            SR6MathFamily.LissajousKnot6D,
            SR6MathFamily.BeatFlutterResonance,
            SR6MathFamily.BoundedSmoothChaos
        };
        for(int fi=0;fi<allFams.Length;fi++)
        {
            var mp=new SR6MathParams{Family=allFams[fi],FreqHz=2.0,OrbitAmp=1.0,HelixTwist=1.0,PivotSweepHz=0.35,KnotP=0,OuterCeilingL0=0.82};
            mEng.Start(mp);
            bool allSafe=true,allFinite=true;
            double[] mins={1,1,1,1,1,1},maxs={0,0,0,0,0,0};
            double maxJump=0;
            double[] prev=null;
            for(int i=0;i<220;i++)
            {
                var fr=mEng.Step(0.02,pmap,null);
                if(!fr.Active||fr.DeviceAxes==null){allFinite=false;break;}
                if(!SR6MathManifoldEngine.IsWithinSR6Workspace(fr.DeviceAxes[0],fr.DeviceAxes[1],fr.DeviceAxes[2],fr.DeviceAxes[4]))
                    allSafe=false;
                for(int ax=0;ax<6;ax++)
                {
                    double v=fr.DeviceAxes[ax];
                    if(!Numbers.Finite(v)||v<0.0||v>1.0)allFinite=false;
                    if(i>=20){mins[ax]=Math.Min(mins[ax],v);maxs[ax]=Math.Max(maxs[ax],v);}
                    if(prev!=null)maxJump=Math.Max(maxJump,Math.Abs(v-prev[ax]));
                }
                prev=(double[])fr.DeviceAxes.Clone();
            }
            bool multiDofRich=(maxs[0]-mins[0])>=.20&&(maxs[3]-mins[3])>=.22&&(maxs[4]-mins[4])>=.16&&(maxs[5]-mins[5])>=.18;
            check(allFinite&&allSafe&&maxJump<=.12&&multiDofRich,"SR6MathManifoldEngine."+allFams[fi]+" achieves rich 6-DOF motion while staying 100% inside SR6 workspace & C2 velocity limits");
        }

        // 28. Phase 3 VirtualPivotWave: Conjugate (L1, R2) phase cleanly inverts sign between Base-Pin (z_p=0) and Tip-Pin (z_p=1)
        double covBase=0,covTip=0;
        mEng.Start(new SR6MathParams{Family=SR6MathFamily.VirtualPivotWave,FreqHz=1.2,PivotZ=0.02,PivotSweepHz=0.0,OrbitAmp=0.85});
        for(int i=0;i<150;i++){var fr=mEng.Step(0.02,pmap,null);if(i>=30)covBase+=(fr.DeviceAxes[1]-.5)*(fr.DeviceAxes[5]-.5);}
        mEng.Start(new SR6MathParams{Family=SR6MathFamily.VirtualPivotWave,FreqHz=1.2,PivotZ=0.98,PivotSweepHz=0.0,OrbitAmp=0.85});
        for(int i=0;i<150;i++){var fr=mEng.Step(0.02,pmap,null);if(i>=30)covTip+=(fr.DeviceAxes[1]-.5)*(fr.DeviceAxes[5]-.5);}
        check(covBase>0.2&&covTip<-0.2,"VirtualPivotWave conjugate translation-rotation coupling cleanly flips fulcrum between base-pin (z_p=0) and tip-pin (z_p=1)");

        // 29. Phase 3 Golden-Ratio 6D Lissajous non-repeating manifold check
        mEng.Start(new SR6MathParams{Family=SR6MathFamily.LissajousKnot6D,FreqHz=1.0,KnotP=0,OrbitAmp=0.85,HelixTwist=0.9});
        double[] snap1=null,snap2=null;
        for(int i=0;i<155;i++)
        {
            var fr=mEng.Step(0.02,pmap,null);
            if(i==50)snap1=(double[])fr.DeviceAxes.Clone();
            if(i==100)snap2=(double[])fr.DeviceAxes.Clone(); // exactly 1 full L0 cycle later (1.0s)
        }
        double cycleDiff=0;
        for(int ax=1;ax<6;ax++)cycleDiff+=Math.Abs(snap2[ax]-snap1[ax]);
        check(cycleDiff>=0.25,"Golden-Ratio 6D Lissajous manifold explores distinct non-repeating 5D orientation/sway states across consecutive L0 cycles");

        // 30. Regression (P0-4): SR6MathManifoldEngine C2 filter stays unconditionally stable at 20 FPS (dt = 0.05s) without Nyquist oscillation
        mEng.Start(new SR6MathParams{Family=SR6MathFamily.VirtualPivotWave,FreqHz=1.2,OrbitAmp=0.85,HelixTwist=0.8});
        int signFlips20=0;
        double prevAcc0=0;
        bool stable20=true;
        for(int i=0;i<80;i++)
        {
            var fr=mEng.Step(0.05,pmap,null);
            for(int ax=0;ax<6;ax++)if(!Numbers.Finite(fr.DeviceAxes[ax])||fr.DeviceAxes[ax]<0||fr.DeviceAxes[ax]>1)stable20=false;
            double acc0=fr.AxisAcc[0];
            if(i>=15&&Math.Abs(acc0)>2.0&&Math.Abs(prevAcc0)>2.0&&Math.Sign(acc0)!=Math.Sign(prevAcc0))signFlips20++;
            prevAcc0=acc0;
        }
        check(stable20&&signFlips20<10,"SR6MathManifoldEngine substepped C2 filter remains smooth and stable at 20 FPS (dt=0.05s)");
        mEng.Stop("done phase 3 math tests");

        // 31. Regression (P1-2): LineFB drives PhysicalAxes[1] (L1 Surge) and holds PhysicalAxes[2] (L2 Sway) centered at 0.5
        var fbAxisStep=TimelineStep.FromPreset(TimelinePreset.WaistOnly,6.0,.85);
        fbAxisStep.HoldDepth=0.80;fbAxisStep.GrindPattern=GrindPattern.LineFB;fbAxisStep.GrindAmp=0.45;fbAxisStep.TwistAmp=0.0;
        tl.Start("line_fb_axis_check",new[]{fbAxisStep});
        double maxL1Dev=0,maxL2Dev=0;
        for(int i=0;i<100;i++)
        {
            var fr=tl.Step(.02,pmap,null);
            if(i>=20&&fr.PhysicalAxes!=null)
            {
                maxL1Dev=Math.Max(maxL1Dev,Math.Abs(fr.PhysicalAxes[1]-0.5));
                maxL2Dev=Math.Max(maxL2Dev,Math.Abs(fr.PhysicalAxes[2]-0.5));
            }
        }
        check(maxL1Dev>=0.15&&maxL2Dev<=1e-6,"TimelineEngine LineFB maps forward/back sway to L1 (phys[1]) and keeps L2 (phys[2]) centered");

        // 32. Regression (P1-4): Tuning explicit waveform params while Organic is active overrides Organic phrasing immediately
        var orgTuneStep=TimelineStep.FromPreset(TimelinePreset.Organic,20.0,.85);
        tl.Start("org_tune_check",new[]{orgTuneStep});
        for(int i=0;i<25;i++)tl.Step(.02,pmap,null);
        check(tl.OrganicActive,"Organic preset starts in Organic phrasing mode");
        tl.TuneActive(s=>{s.GrindPattern=GrindPattern.LineFB;s.TwistAmp=0.0;s.FreqHz=2.4;});
        check(!tl.OrganicActive,"Explicit waveform TuneActive overrides Organic phrasing immediately");
        tl.Stop("done timeline regression checks");

        // 33. Regression (P2-6): DirectorModulator.Apply honors configurable outerCeiling > 0.80 without deadzone clipping
        var dmCeil=new DirectorModulator();
        dmCeil.SetOverride(new DirectorStep{Mode=DirectorMode.Envelope,L0Min=0.10,L0Max=0.90,IntensityScale=1.0,RampSeconds=0.05},10.0);
        DirectorActionEvent dmEv;
        double[] outAt90=null,outAt80=null;
        for(int i=0;i<30;i++)dmCeil.Apply(new[]{1.0-0.50,.5,.5,.5,.5,.5},.02,20,20,out dmEv,0.92);
        outAt90=dmCeil.Apply(new[]{1.0-0.90,.5,.5,.5,.5,.5},.02,20,20,out dmEv,0.92);
        outAt80=dmCeil.Apply(new[]{1.0-0.80,.5,.5,.5,.5,.5},.02,20,20,out dmEv,0.92);
        check(Math.Abs(outAt80[0]-outAt90[0])>=0.05,"DirectorModulator.Apply normalizes against configurable outerCeiling instead of hardcoded 0.80");
    }

    private static NativeMotionSample Sample(ActionEntry entry,double modelLength,MotionVector[] path)
    {
        return new NativeMotionSample{
            Studio=true,RootDistance=true,HasBodyFrame=true,HasShaftPose=true,HasTargetFrame=true,
            Origin=Identity.Origin,End=path[^1],ReferenceFrame=Identity,BodyFrame=Identity,
            Target=entry.Calibrated.Origin,AnchorTarget=entry.Observed.Origin,TargetReferenceFrame=entry.Calibrated,
            TargetBodyFrame=entry.Calibrated,ShaftRestLength=modelLength,ShaftPath=path,PlaneWidth=.16,PlaneDepth=.11
        };
    }
}
