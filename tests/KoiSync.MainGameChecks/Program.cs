using System.Text.Json;
using KoiSync.Core;
using KoiSync.MainGame;

int checks=0;
void Check(bool yes,string label){if(!yes)throw new Exception(label);checks++;}
using var table=File.OpenText(Path.Combine(AppContext.BaseDirectory,"Actions.tsv"));
using var names=File.OpenText(Path.Combine(AppContext.BaseDirectory,"NativeAnimations.csv"));
var catalogue=new ActionCatalogue(table,names);
Check(catalogue.Count==96,"Every native action has one explicit definition");
foreach(var d in catalogue.Definitions)
{
    var split=d.Key.LastIndexOf('_');var mode=d.Key[..split];var id=int.Parse(d.Key[(split+1)..]);
    var resolved=catalogue.Resolve(mode,id,d.Name,false);
    Check(resolved.Key==d.Key&&resolved.Effector!=ActionSource.Unsupported,"Native definition resolves: "+d.Key);
}
var oral=catalogue.Resolve("houshi",7,"片手フェラ",false);
Check(oral.Contact==NativeTarget.ORAL&&oral.Source==ActorSlot.Male&&oral.Target==ActorSlot.Female0,"Fixed oral pair");
var swap=catalogue.Resolve("houshi3P",1,"Wフェラ入れ替え",false);
Check(swap.Target==ActorSlot.Female1,"Native swap selects the second woman");
Check(catalogue.Resolve("houshi3P",923,"Wパイズリ入れ替え",false).Effector==ActionSource.Unsupported,"Unknown extension never silently defaults");
var anal=catalogue.Resolve("sonyu",2,"後背位",true);
Check(anal.Contact==NativeTarget.ANAL&&anal.Key=="sonyu_2_anal","Anal variant has independent calibration");
Check(catalogue.Resolve("sonyu",0,"正常位",true).Contact==NativeTarget.VAGINAL,"Stale anal flags cannot change an action without that native branch");
var solo=catalogue.Resolve("masturbation",1,"椅子オナニー",false);
Check(solo.Source==solo.Target&&solo.Effector==ActionSource.RightFinger,"Solo finger source is the same actor");
var caress=catalogue.Resolve("aibu",0,"立ち愛撫",false);
Check(caress.Effector==ActionSource.InteractionItem,"Caress reads explicit native interaction item");
Check(ActionStages.IsTravel("peeping_0","In")&&ActionStages.IsTravel("peeping_1","Out"),"Native peeping travel never drives the device");
Check(!ActionStages.IsTravel("peeping_0","Loop"),"The actual peeping loop remains recordable");
Check(!ActionStages.IsTravel("sonyu_0","In"),"Peeping travel rules cannot suppress another action type");
Check(ActionStages.ItemLayer(-1,12,1)==-1,"An attached idle item is not an active interaction");
Check(ActionStages.ItemLayer(7,12,1)==7,"Use the native action layer instead of the static prefab base layer");
Check(ActionStages.ItemLayer(7,12,.4)==-1&&ActionStages.ItemLayer(12,12,1)==-1,"Wait for active layer blending and valid layer bounds");
using var nativeResources=File.OpenText(Path.Combine(AppContext.BaseDirectory,"NativeResources.tsv"));
using var extraActions=File.OpenText(Path.Combine(AppContext.BaseDirectory,"KPlugActions.tsv"));
using var allTable=File.OpenText(Path.Combine(AppContext.BaseDirectory,"Actions.tsv"));
using var allNames=File.OpenText(Path.Combine(AppContext.BaseDirectory,"NativeAnimations.csv"));
var all=new ActionCatalogue(allTable,allNames,nativeResources,extraActions);
Check(all.Count==267,"96 native and all 171 installed kPlug resources have explicit definitions");
Check(all.Definitions.Count(d=>d.AnalAllowed)==85,"Only the actual 8 native and 77 extension anal branches have independent profiles");
foreach(var d in all.Definitions)
{
    string head=d.Resource.Split('|')[0];int split=head.LastIndexOf('_');
    var resolved=all.Resolve(head[..split],int.Parse(head[(split+1)..]),"translated or duplicated display name",false,d.Resource);
    Check(resolved.Key==d.Key&&resolved.Effector!=ActionSource.Unsupported,"Resource identity ignores cloned ID and translated display name: "+d.Key);
}
var foot=all.Definitions.Single(d=>d.Key=="kplug_khh_f_60");
var foot2=all.Definitions.Single(d=>d.Key=="kplug_khh_f_61");
Check(foot.Resource.Split('|')[0]==foot2.Resource.Split('|')[0]&&foot.Key!=foot2.Key&&foot.Key!="houshi_0","Cloned kPlug actions and native template have distinct profile directories");
Check(foot.Contact==NativeTarget.BOTH_FEET,"A foot extension cannot inherit the template's hand binding");
Check(all.Definitions.Single(d=>d.Key=="kplug_khh_f_86").Landmark==ActionLandmark.LeftArmpit,"Armpit contact is an explicit surface landmark");
Check(all.Definitions.Single(d=>d.Key=="kplug_khe_f_06").Contact==NativeTarget.ANAL,"Extra anal finger action retains its actual target");
Check(all.Definitions.Single(d=>d.Key=="kplug_kht_f_05").Effector==ActionSource.LeftFinger,"The authored solo wall action uses its actual left finger chain");
Check(all.Definitions.Where(d=>d.Key=="kplug_kht_f_08"||d.Key=="kplug_kht_f_09").All(d=>d.Effector==ActionSource.AnimatedProp&&d.Source==d.Target),"Both independently animated solo props are scoped to their owning actor");
var unknown=all.Resolve("sonyu",2,"後背位",false,"another bundle or controller");
Check(unknown.Effector==ActionSource.Unsupported&&unknown.Key!="sonyu_2","An unlisted asset cannot silently reuse a native configuration");
Check(AnimationResources.UnknownKey("a")!=AnimationResources.UnknownKey("b"),"Unlisted resources also have separate manual profile identities");
Check(AnimationResources.Identity("houshi",0,"H\\ANIM\\TEST","Clip")=="houshi_0|h/anim/test|clip","Asset path normalization is stable");
var surfaceRuler=new ActionCalibration{Length=.13,L1=.08,L2=.08,Origin=new MotionVector(.65,.1,.05)};surfaceRuler.Validate();
Check(surfaceRuler.Length==.13,"A service surface can be more than 500 mm from the source pivot without changing its fixed 130 mm span");
Check(all.Definitions.Where(d=>d.Key.StartsWith("kplug_")&&d.Effector==ActionSource.Shaft&&d.Contact!=NativeTarget.ORAL&&d.Resource.StartsWith("houshi_")).All(d=>d.CenteredMotion),"External service surfaces use a fixed motion ruler rather than an insertion-plane contact gate");
var multi=catalogue.Resolve("houshi3PMMF",2,"フェラ＆手コキ",false);
Check(ActionStages.SourceFor(multi.Source,multi.WeakSource,"WLoop",false)==ActorSlot.Male1,"The authored weak MMF oral stage uses the second male");
Check(ActionStages.SourceFor(multi.Source,multi.WeakSource,"SLoop",false)==ActorSlot.Male,"The authored strong MMF oral stage returns to the first male");
Check(ActionStages.SourceFor(ActorSlot.Female1,multi.WeakSource,"WLoop",true)==ActorSlot.Female1,"Manual participant choice overrides the fixed stage default");
var pinned=new NativeMotionResult{Length=.18,TargetDepth=.09,Engagement=1,MaterialInsertion=0,ContactDistance=.009};
Check(StrokeMapping.Depth(pinned,.14,.13,.12,AxisMappingMode.Soft)==0,"Unbound visible chain outside the entry does not request insertion");
Check(StrokeMapping.Depth(pinned,.14,.13,.12,AxisMappingMode.Soft,true)>.5,"A native pinned tip preserves root-to-entry motion using fixed length");
pinned.ContactDistance=.1;Check(StrokeMapping.Depth(pinned,.14,.13,.12,AxisMappingMode.Soft,true)==0,"Explicit native pinning cannot bypass finite contact distance");
var cycle=new CycleCapture();cycle.Arm();
Check(CycleCapture.ResolveDuration(double.PositiveInfinity,1.333333)>1.33,"A zero-speed native BlendTree uses its finite clip duration");
Check(CycleCapture.ResolveDuration(double.NaN,double.NaN)==0,"An unavailable clock waits instead of producing invalid timestamps");
Check(CycleCapture.ResolveDuration(.533333,1.333333)<.54,"A valid native state duration remains authoritative");
Check(cycle.Observe(.43,true,true,false)==CaptureStep.Wait,"Partial loop is not called complete");
Check(cycle.Observe(.9,true,true,false)==CaptureStep.Wait,"Wait until observed wrap");
Check(cycle.Observe(1.01,true,true,false)==CaptureStep.Begin,"Begin on the next observed cycle");
Check(cycle.Observe(1.02,true,false,false)==CaptureStep.Wait,"Pause does not advance capture");
Check(cycle.Observe(1.03,true,true,true)==CaptureStep.Wait,"Animator transition cannot enter a take");
Check(cycle.Observe(1.5,true,true,false)==CaptureStep.Sample,"Ordinary playback records");
Check(cycle.Observe(2.01,true,true,false)==CaptureStep.Complete,"A real loop boundary completes capture");
cycle.Arm();cycle.Observe(2.01,true,true,false);
Check(cycle.Observe(.5,true,true,false)==CaptureStep.Discontinuity,"Seek or restart cannot join backwards timestamps");
cycle.Arm();Check(cycle.Observe(.3,false,true,false)==CaptureStep.Begin,"One-shot phases record their actual observed portion");
Check(cycle.Observe(1,false,true,false)==CaptureStep.Complete,"One-shot terminal sample closes capture");
string root=Path.Combine(Path.GetTempPath(),"koisync-main-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
try
{
    AlignmentChecks.Run(Check,root);
    LocaleChecks.Run(Check);
    var p=ActionProfile.From(oral);p.Mode=AxisMappingMode.Manual;p.ManualBinding=true;p.Source=ActorSlot.Male1;
    p.Calibration=new ActionCalibration{Length=.1,L1=.03,L2=.05,Origin=new MotionVector(.01,.02,.03),ManualEnd=new MotionVector(.11,.02,.03)};p.Save(root);
    var read=ActionProfile.Load(root,oral);
    Check(read.Source==ActorSlot.Male1&&read.Mode==AxisMappingMode.Manual&&read.ManualBinding,"User source and mapping persist without an automatic override");
    Check((read.Calibration.ManualEnd.Value-p.Calibration.ManualEnd.Value).Length<1e-10,"Manual endpoints round trip");
    read.ManualBinding=false;read.Source=oral.Source;read.Contact=NativeTarget.RIGHTHAND;read.Save(root);
    var refreshed=ActionProfile.Load(root,oral);
    Check(refreshed.Contact==NativeTarget.ORAL&&refreshed.Calibration!=null,"Corrected fixed defaults update unedited bindings while preserving source calibration");
    p.Save(root);
    var other=ActionProfile.From(swap);other.Save(root);
    Check(ActionProfile.Load(root,oral).Source==ActorSlot.Male1,"Saving another action cannot replace current calibration");
    var custom=all.Definitions.Single(d=>d.Key=="kplug_khh_f_86");var customProfile=ActionProfile.From(custom);customProfile.ManualBinding=true;customProfile.Landmark=ActionLandmark.RightArmpit;customProfile.Calibration=surfaceRuler;customProfile.Save(root);
    var customRead=ActionProfile.Load(root,custom);
    Check(customRead.Landmark==ActionLandmark.RightArmpit&&customRead.Calibration.Origin.X==.65,"Manual surface choice and long source offset round trip");
    Check(customRead.Resource==custom.Resource,"Profile resource provenance survives save and load");
    var duplicate=ActionProfile.From(foot);duplicate.Save(root);var duplicate2=ActionProfile.From(foot2);duplicate2.Mode=AxisMappingMode.Manual;duplicate2.Save(root);
    Check(ActionProfile.Load(root,foot).Mode!=AxisMappingMode.Manual,"Cloned mode and ID cannot share extension calibration");
    var multiProfile=ActionProfile.From(multi);multiProfile.Calibration=new ActionCalibration{Length=.15,L1=.08,L2=.08};multiProfile.SelectCalibration(ActorSlot.Male1);multiProfile.Calibration=new ActionCalibration{Length=.2,L1=.08,L2=.08};multiProfile.Save(root);
    var multiRead=ActionProfile.Load(root,multi);Check(multiRead.Calibration.Length==.15,"Primary source length remains independent of the weak-stage source");multiRead.SelectCalibration(ActorSlot.Male1);
    Check(multiRead.Calibration.Length==.2&&multiRead.WeakSource==ActorSlot.Male1,"Alternate source calibration round trips within one action file");
    multiRead.ClearCalibrations();multiRead.SelectCalibration(ActorSlot.Male);Check(multiRead.Calibration==null,"Reset action calibration clears both fixed source slots");
    bool rejected=false;try{ActionProfile.Folder(root,"../escape");}catch(ArgumentException){rejected=true;}Check(rejected,"Action keys stay inside the action directory");
    var drive=new GameDrive();drive.Gain[0]=2;drive.Update(new[]{.3,.5,.5,.5,.5,.5},.02,AxisMappingMode.Absolute);
    double final=drive.Values[0];var recorder=new ActionRecorder();recorder.Begin(.017);recorder.Add(.017,drive.Values);
    drive.Update(new[]{.5,.6,.4,.5,.5,.5},.03,AxisMappingMode.Soft);recorder.Add(.047,drive.Values);
    string stem=Path.Combine(root,"recording");recorder.Save(stem);
    foreach(string suffix in new[]{"",".surge",".sway",".twist",".roll",".pitch"})
    {
        using var json=JsonDocument.Parse(File.ReadAllText(stem+suffix+".funscript"));var frames=json.RootElement.GetProperty("actions");
        Check(frames.GetArrayLength()==2&&frames[0].GetProperty("at").GetInt64()==17&&frames[1].GetProperty("at").GetInt64()==47,"All axes retain the same observed timestamps");
    }
    using var l0=JsonDocument.Parse(File.ReadAllText(stem+".funscript"));double saved=l0.RootElement.GetProperty("actions")[0].GetProperty("pos").GetDouble()/100;
    drive.UpdateScript(new[]{saved,.5,.5,.5,.5,.5});Check(Math.Abs(drive.Values[0]-final)<.0051,"Final script playback bypasses gain and L0 inversion");

    // v0.4.1 SR6 Math Lab regression checks: seamless family hot-switch + zero-mean chaos centering + workspace feasibility
    var mathEng=new SR6MathManifoldEngine();
    mathEng.Start(new SR6MathParams{Family=SR6MathFamily.VirtualPivotWave,FreqHz=1.4,OrbitAmp=0.85,HelixTwist=0.8});
    SR6MathFrame lastF=default;
    for(int s=0;s<60;s++)lastF=mathEng.Step(1.0/60.0,null,null);
    Check(lastF.Active&&lastF.BlendEnvelope>0.99,"MathEngine ramps BlendEnvelope to 1.0 within 1s");
    double[] prevAxes=(double[])lastF.DeviceAxes.Clone();
    mathEng.Start(new SR6MathParams{Family=SR6MathFamily.VariablePitchHelix,FreqHz=1.5,OrbitAmp=0.85,HelixTwist=0.9});
    var switchedF=mathEng.Step(1.0/60.0,null,null);
    Check(switchedF.BlendEnvelope>=0.999,"Hot-switching math family keeps BlendEnvelope continuous without dropping to 0");
    double maxJump=0;for(int a=0;a<6;a++)maxJump=Math.Max(maxJump,Math.Abs(switchedF.DeviceAxes[a]-prevAxes[a]));
    Check(maxJump<0.09,"Hot-switching math family glides smoothly via C2 filter (maxJump="+maxJump.ToString("0.000")+")");

    mathEng.Stop();
    mathEng.Start(new SR6MathParams{Family=SR6MathFamily.BoundedSmoothChaos,FreqHz=1.35,OrbitAmp=0.85,HelixTwist=0.85});
    double l2Sum=0,l2Min=1,l2Max=0;int chaosSteps=240;
    for(int s=0;s<chaosSteps;s++)
    {
        var cf=mathEng.Step(1.0/60.0,null,null);
        Check(SR6MathManifoldEngine.IsWithinSR6Workspace(cf.DeviceAxes[0],cf.DeviceAxes[1],cf.DeviceAxes[2],cf.DeviceAxes[4]),"BoundedSmoothChaos frame stays inside SR6 workspace");
        l2Sum+=cf.DeviceAxes[2];l2Min=Math.Min(l2Min,cf.DeviceAxes[2]);l2Max=Math.Max(l2Max,cf.DeviceAxes[2]);
    }
    double l2Mean=l2Sum/chaosSteps;
    Check(l2Mean>=0.38&&l2Mean<=0.62,"BoundedSmoothChaos L2 stays zero-mean centered (mean="+l2Mean.ToString("0.000")+")");
    Check(l2Min<0.46&&l2Max>0.54,"BoundedSmoothChaos L2 genuinely crosses center symmetrically ("+l2Min.ToString("0.00")+".."+l2Max.ToString("0.00")+")");
}
finally{Directory.Delete(root,true);}
Console.WriteLine("PASS "+checks+" main-game policy, capture, profile and six-axis recording checks");
if(args.Length==2&&args[0]=="--export-defaults")
{
    string destination=Path.GetFullPath(args[1]);int written=0;
    foreach(var definition in all.Definitions)
    {
        var variants=new List<ActionDefinition>{definition};
        if(definition.AnalAllowed){var branch=definition.Copy();branch.Key+="_anal";branch.Contact=NativeTarget.ANAL;variants.Add(branch);}
        foreach(var variant in variants)
        {
            string path=Path.Combine(ActionProfile.Folder(destination,variant.Key),"action.xml");
            if(!File.Exists(path)){ActionProfile.From(variant).Save(destination);written++;}
        }
    }
    Console.WriteLine("Wrote "+written+" missing default profiles; existing files were preserved: "+destination);
}
