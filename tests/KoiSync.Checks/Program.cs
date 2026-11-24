using System.Numerics;
using KoiSync.Core;
using KoiSync.Wire;

var checks = new List<string>();
StudioShaftChecks.Run((ok,name)=>{if(!ok)throw new Exception(name);checks.Add(name);});
SR6ReferenceChecks.Run((ok,name)=>{if(!ok)throw new Exception(name);checks.Add(name);});
void Check(bool condition,string name) { if(!condition)throw new Exception(name); checks.Add(name); }
void Near(double actual,double expected,string name,double tolerance=0.00001) { Check(Math.Abs(actual-expected)<tolerance,name); }
Pose PoseOf(Vector3 position,Quaternion rotation) => new(){X=position.X,Y=position.Y,Z=position.Z,Qx=rotation.X,Qy=rotation.Y,Qz=rotation.Z,Qw=rotation.W};
var candidate=new Candidate {Id="pair",Active=true,Target=new Pose{X=.02f,Y=-.03f,Z=.1f}};
var result=RelativeMotion.Solve(candidate,StrokeAxis.Z);
Near(result[0],.1,"L0 uses axial projection");Near(result[1],-.03,"L1 retains negative direction");Near(result[2],.02,"L2 remains independent");
candidate.Target.X=.4f;
Near(RelativeMotion.Solve(candidate,StrokeAxis.Z)[0],.1,"Lateral movement does not inflate stroke");
candidate.Target.X=.02f;
Quaternion rotation=Quaternion.CreateFromYawPitchRoll(.6f,-.4f,.8f);
Vector3 translation=new(5,-3,2);
var moved=new Candidate{Reference=PoseOf(translation,rotation),Target=PoseOf(Vector3.Transform(new(.02f,-.03f,.1f),rotation)+translation,rotation)};
var invariant=RelativeMotion.Solve(moved,StrokeAxis.Z);
for(int i=0;i<6;i++)Near(invariant[i],result[i],"Rigid scene transform invariance "+i,.0001);
Near(RelativeMotion.Solve(candidate,StrokeAxis.X)[0],.02,"X projection selection");
Near(RelativeMotion.Solve(candidate,StrokeAxis.Y)[0],-.03,"Y projection selection");
var pipeline=new MotionPipeline();
var frame=new Snapshot{Session="test",State="play",Scene="scene",Active=true,Sequence=1,SampleTime=.02};
pipeline.Update(frame,candidate,0,StrokeAxis.Z);
Check(!pipeline.Arm(),"Uncalibrated data cannot arm");pipeline.StartCalibration();
for(int i=0;i<90;i++){frame.Sequence++;frame.SampleTime+=.02;candidate.Target.Z=(float)(.1+.05*Math.Sin(i*.2));pipeline.Update(frame,candidate,0,StrokeAxis.Z);}
Check(pipeline.FinishCalibration(),"A moving stroke calibrates");
Check(!pipeline.AxisCalibrated[1],"Static side axis stays uncalibrated");
Check(pipeline.Arm(),"Calibrated active source can arm");
double previous=pipeline.Values[0];
for(int i=0;i<30;i++)
{
    frame.Sequence++;frame.SampleTime+=.02;candidate.Target.Z=i<15?-1:2;
    pipeline.Update(frame,candidate,0,StrokeAxis.Z);
    Check(pipeline.Values[0]>=.1&&pipeline.Values[0]<=.9,"Output range clamps "+i);
    if(i>0)Check(Math.Abs(pipeline.Values[0]-previous)<=.040001,"Slew limit "+i);
    previous=pipeline.Values[0];
}
pipeline.Update(frame,candidate,.3,StrokeAxis.Z);Check(!pipeline.Armed,"Stale input disarms");
frame.Sequence++;frame.SampleTime+=.02;pipeline.Update(frame,candidate,0,StrokeAxis.Z);Check(!pipeline.Armed,"Input recovery requires rearm");
Check(pipeline.Arm(),"Explicit rearm after recovery");
frame.Active=false;pipeline.Update(frame,candidate,0,StrokeAxis.Z);Check(!pipeline.Armed,"Inactive scene disarms");
frame.Sequence++;frame.SampleTime+=.02;candidate.Target.Z=.222f;pipeline.Update(frame,candidate,0,StrokeAxis.Z);
Near(pipeline.Raw[0],.222,"Paused scene still exposes current pose");Check(!pipeline.HasLiveSample,"Paused inspection cannot arm output");
frame.Active=true;candidate.Active=false;frame.Sequence++;frame.SampleTime+=.02;candidate.Target.Z=.233f;pipeline.Update(frame,candidate,0,StrokeAxis.Z);
Near(pipeline.Raw[0],.233,"Inactive animator still exposes pose for Timeline inspection");Check(!pipeline.Armed,"Inspecting an inactive source does not arm output");candidate.Active=true;
frame.Active=true;frame.Sequence++;frame.SampleTime+=.02;candidate.Id="new";pipeline.Update(frame,candidate,0,StrokeAxis.Z);
Check(!pipeline.Calibrated,"Changing pair invalidates calibration");
pipeline.StartCalibration();
for(int i=0;i<40;i++){frame.Sequence++;frame.SampleTime+=.02;pipeline.Update(frame,candidate,0,StrokeAxis.Z);}
Check(!pipeline.FinishCalibration(),"Still pose cannot invent a range");
candidate.Target.Z=.1f;candidate.Target.Qz=(float)Math.Sin(179*Math.PI/360);candidate.Target.Qw=(float)Math.Cos(179*Math.PI/360);
pipeline.Reset("rotation");frame.Sequence++;frame.SampleTime+=.02;pipeline.Update(frame,candidate,0,StrokeAxis.Z);
candidate.Target.Qz=(float)Math.Sin(-179*Math.PI/360);candidate.Target.Qw=(float)Math.Cos(-179*Math.PI/360);
frame.Sequence++;frame.SampleTime+=.02;pipeline.Update(frame,candidate,0,StrokeAxis.Z);Near(pipeline.Raw[3],181,"Rotation unwrap across ±180",.0001);
candidate.Target.X=float.NaN;frame.Sequence++;frame.SampleTime+=.02;pipeline.Update(frame,candidate,0,StrokeAxis.Z);Check(!pipeline.HasLiveSample,"Non-finite geometry rejected");
string command=TCode.Encode(new[]{0.0,.5,.9999,.5,.5,.5},new[]{true,true,true,true,true,true},DeviceKind.SR6,20);
Check(command=="L00000I20 L15000I20 L29999I20 R05000I20 R15000I20 R25000I20\n","TCode interval attaches to every axis");
Check(TCode.Encode(new[]{.5,.5,.5,.5,.5,.5},new[]{true,true,true,true,true,true},DeviceKind.OSR2,20)=="L05000I20 R15000I20 R25000I20\n","Base OSR2 capability mapping");
Check(TCode.Encode(new[]{.5,.5,.5,.5,.5,.5},new[]{true,true,true,true,true,true},DeviceKind.SSR1,20)=="L05000I20\n","SSR1 single axis mapping");
bool handyRejected=false;try{TCode.Encode(new double[6],new bool[6],DeviceKind.Handy,20);}catch(NotSupportedException){handyRejected=true;}
Check(handyRejected,"Handy cannot accidentally receive TCode");
var observation=new InspectionRange();
observation.Observe("a",1,new[]{.02,0,0,0,0,0});observation.Observe("a",2,new[]{.04,0,0,0,0,0});
Near(observation.Minimum[0],.02,"Inspection raw minimum retains world units");Near(observation.Maximum[0]-observation.Minimum[0],.02,"Inspection shows peak-to-peak span");
observation.Observe("a",2,new[]{9.0,0,0,0,0,0});Near(observation.Maximum[0],.04,"Duplicate sample cannot inflate observed range");
observation.Observe("a",3,new[]{double.NaN,0,0,0,0,0});Check(observation.Samples==2,"Invalid samples excluded from observation");
observation.Observe("b",4,new[]{.3,0,0,0,0,0});Near(observation.Minimum[0],.3,"Source change clears previous observed range");Check(observation.Samples==1,"Source change resets observation count");
observation.Reset();Check(observation.Samples==0,"Manual observation reset");
var smallPipeline=new MotionPipeline();var smallCandidate=new Candidate{Id="small",Active=true};
var smallFrame=new Snapshot{Session="small",Scene="scene",State="play",Active=true,SampleTime=.02,Sequence=1};
smallPipeline.Update(smallFrame,smallCandidate,0,StrokeAxis.Z);double inputLow,inputHigh;
Check(!smallPipeline.TryGetCalibrationRange(0,out inputLow,out inputHigh),"No calibration range is invented before calibration");smallPipeline.StartCalibration();
for(int i=0;i<40;i++){smallFrame.Sequence++;smallFrame.SampleTime+=.02;smallCandidate.Target.Z=i%2==0?.02f:.04f;smallPipeline.Update(smallFrame,smallCandidate,0,StrokeAxis.Z);}
Check(smallPipeline.FinishCalibration(),"A 0.02 world-unit span is a valid calibration");
Check(smallPipeline.TryGetCalibrationRange(0,out inputLow,out inputHigh),"Inspection can read a valid calibration input range");Near(inputLow,.02,"Small raw range low");Near(inputHigh,.04,"Small raw range high");
smallFrame.Sequence++;smallFrame.SampleTime+=.02;smallCandidate.Target.Z=.03f;smallPipeline.Update(smallFrame,smallCandidate,0,StrokeAxis.Z);
Near(smallPipeline.Raw[0],.03,"Raw distance is not converted to percent");Near(smallPipeline.Values[0],.5,"Small raw midpoint maps to normalized midpoint");
Check(!smallPipeline.Armed,"Inspecting calibrated values does not arm output");Check(!smallPipeline.TryGetCalibrationRange(1,out inputLow,out inputHigh),"Uncalibrated secondary axis has no claimed range");
double monitored;
Check(smallPipeline.TryGetMappedValue(0,out monitored),"Monitor exposes a computed mapped value");Near(monitored,.5,"Monitor mapped value matches pipeline");
Check(ChannelMonitor.TryNormalized(smallPipeline,0,out monitored),"Monitor normalized input is available after calibration");Near(monitored,.5,"Monitor normalized midpoint");
smallFrame.Active=false;smallFrame.Sequence++;smallFrame.SampleTime+=.02;smallPipeline.Update(smallFrame,smallCandidate,0,StrokeAxis.Z);
Check(!smallPipeline.TryGetMappedValue(0,out monitored),"Paused monitor does not present a stale target as current");
Check(!new MotionPipeline().TryGetMappedValue(0,out monitored),"Initialized 0.5 is not a measured output");
string six="L01234I20 L25678I20 R02345I20 R14567I20 R29999I20";
Check(ChannelMonitor.TryLastSent(six,0,out monitored),"Read serialized TCode L0");Near(monitored,.1234,"Preserve serialized TCode precision");
Check(!ChannelMonitor.TryLastSent(six,1,out monitored),"Omitted axis is not reported as a zero command");
Check(ChannelMonitor.TryLastSent(six,5,out monitored),"Read final rotation token");Near(monitored,.9999,"Maximum serialized TCode value");
Check(ChannelMonitor.TryLastSent("LinearCmd L0=0.3219",0,out monitored),"Read Intiface command telemetry");Near(monitored,.3219,"Intiface serialized precision");
Check(!ChannelMonitor.TryLastSent("LinearCmd L0=0.3219",3,out monitored),"Linear transport cannot claim rotary output");
Check(!ChannelMonitor.TryLastSent("DSTOP",0,out monitored),"Stop command has no invented position");
Check(!ChannelMonitor.TryLastSent("LinearCmd L0=NaN",0,out monitored),"Invalid command telemetry rejected");
NativeMotionSample NativeFixture()=>new(){Origin=new(0,0,0),End=new(.1,0,0),PlanePoint=new(0,0,-.1),Target=new(.05,.02,.03),TargetX=new(1,0,0),TargetZ=new(0,1,0)};
var nativeSample=NativeFixture();var native=NativeMotion.Solve(nativeSample);
double[] expectedNative={.5,.8,.7,.5,.5,.5};
for(int i=0;i<6;i++)Near(native.Normalized[i],expectedNative[i],"Upstream six-axis analytical fixture "+i);
Near(native.Raw[0],.05,"Native raw L0 retains world units");Near(native.Raw[1],.03,"Native cross-product lateral basis");Near(native.Length,.1,"Native measures reference length");
nativeSample.Target=new(.05,.9,.8);Near(NativeMotion.Solve(nativeSample).Normalized[0],.5,"Native lateral movement does not inflate L0");
nativeSample=NativeFixture();
MotionVector MoveNative(MotionVector v,bool point){Vector3 transformed=Vector3.Transform(new Vector3((float)v.X,(float)v.Y,(float)v.Z),rotation)+(point?translation:Vector3.Zero);return new(transformed.X,transformed.Y,transformed.Z);}
var transformedNative=new NativeMotionSample{Origin=MoveNative(nativeSample.Origin,true),End=MoveNative(nativeSample.End,true),PlanePoint=MoveNative(nativeSample.PlanePoint,true),Target=MoveNative(nativeSample.Target,true),TargetX=MoveNative(nativeSample.TargetX,false),TargetZ=MoveNative(nativeSample.TargetZ,false)};
var transformResult=NativeMotion.Solve(transformedNative);for(int i=0;i<6;i++)Near(transformResult.Normalized[i],native.Normalized[i],"Native rigid scene transform invariance "+i,.0001);
nativeSample.TargetZ=new(0,Math.Cos(Math.PI/6),Math.Sin(Math.PI/6));Near(NativeMotion.Solve(nativeSample).Raw[3],30,"Upstream R0 angular reference");
nativeSample.TargetZ=new(0,Math.Cos(Math.PI/6),-Math.Sin(Math.PI/6));Near(NativeMotion.Solve(nativeSample).Raw[3],30,"Upstream R0 unsigned fold explicitly preserved");
nativeSample.TargetX=new(0,1,0);nativeSample.TargetZ=new(-1,-1e-8,0);Near(NativeMotion.Solve(nativeSample).Normalized[4],0,"Perpendicular frame boundary does not flip R1 from roundoff");
nativeSample.End=nativeSample.Origin;bool degenerate=false;try{NativeMotion.Solve(nativeSample);}catch(ArgumentException){degenerate=true;}Check(degenerate,"Zero reference length rejected");
nativeSample=NativeFixture();nativeSample.PlanePoint=new(.2,0,0);degenerate=false;try{NativeMotion.Solve(nativeSample);}catch(ArgumentException){degenerate=true;}Check(degenerate,"Collinear reference plane rejected");
nativeSample=NativeFixture();nativeSample.TargetX=new(double.NaN,0,0);degenerate=false;try{NativeMotion.Solve(nativeSample);}catch(ArgumentException){degenerate=true;}Check(degenerate,"Invalid native direction rejected");
var nativePipeline=new MotionPipeline();var nativeCandidate=new Candidate{Id="native",Active=true,Native=NativeFixture()};
var nativeFrame=new Snapshot{Session="native",Scene="native",State="WLoop",Active=true,Sequence=1,SampleTime=.02};
nativePipeline.Update(nativeFrame,nativeCandidate,0,StrokeAxis.Z);Check(!nativePipeline.Calibrated&&!nativePipeline.Arm(),"Native initialization cannot invent a valid movement range");
for(int i=0;i<30;i++){nativeFrame.Sequence++;nativeFrame.SampleTime+=.02;nativeCandidate.Native.Target=new(i%2==0?.02:.04,0,0);nativePipeline.Update(nativeFrame,nativeCandidate,0,StrokeAxis.Z);}
Check(nativePipeline.Calibrated&&!nativePipeline.Armed,"Native range auto-acquires without arming");Near(nativePipeline.NativeMidpoint,.35,"Native midpoint includes the first valid sample");
Check(!nativePipeline.TryGetCalibrationRange(0,out inputLow,out inputHigh),"Native reference ratio is not misreported as a min-max calibration");
Check(ChannelMonitor.TryNormalized(nativePipeline,0,out monitored),"Monitor reads native reference ratio");Near(monitored,.4,"Native normalized input uses measured length");
nativeCandidate.Native.Gain=2;nativeCandidate.Native.Target=new(.035,0,0);
for(int i=0;i<60;i++){nativeFrame.Sequence++;nativeFrame.SampleTime+=.02;nativePipeline.Update(nativeFrame,nativeCandidate,0,StrokeAxis.Z);}
Near(nativePipeline.Values[0],.5,"Native automatic midpoint maps to center",.0001);
nativeCandidate.Native.Target=new(.04,0,0);
for(int i=0;i<60;i++){nativeFrame.Sequence++;nativeFrame.SampleTime+=.02;nativePipeline.Update(nativeFrame,nativeCandidate,0,StrokeAxis.Z);}
Near(nativePipeline.Values[0],.6,"Native gain scales around observed midpoint",.0001);
string multi=TCode.Encode(nativePipeline.Values,new[]{true,true,true,true,true,true},DeviceKind.SR6,20),single=TCode.Encode(nativePipeline.Values,new[]{true,true,true,true,true,true},DeviceKind.SSR1,20);
Check(multi.Split(' ')[0]+"\n"==single,"Single-axis and multi-axis paths use exactly the same serialized L0");
Check(nativePipeline.Arm(),"Native auto range can be explicitly armed");nativeFrame.Active=false;nativeFrame.Sequence++;nativeFrame.SampleTime+=.02;nativePipeline.Update(nativeFrame,nativeCandidate,0,StrokeAxis.Z);
Check(!nativePipeline.Armed&&!nativePipeline.TryGetMappedValue(0,out monitored),"Native pause stops and hides current mapped output");
nativeFrame.Active=true;nativeFrame.Sequence++;nativeFrame.SampleTime+=.02;nativePipeline.Update(nativeFrame,nativeCandidate,0,StrokeAxis.Z);Check(!nativePipeline.Armed,"Native recovery never arms itself");
nativePipeline.Arm();nativeCandidate.Native.Target=new(.2,0,0);
for(int i=0;i<90;i++){nativeFrame.Sequence++;nativeFrame.SampleTime+=.02;nativePipeline.Update(nativeFrame,nativeCandidate,0,StrokeAxis.Z);}
Check(nativePipeline.Armed&&nativePipeline.TryGetMappedValue(0,out monitored),"Overrange native sample retains synchronization and a current target");Near(nativePipeline.Values[0],.9,"Overrange stroke settles at the configured upper bound",.0001);
nativeCandidate.Native.Target=new(-.2,0,0);
for(int i=0;i<90;i++){nativeFrame.Sequence++;nativeFrame.SampleTime+=.02;nativePipeline.Update(nativeFrame,nativeCandidate,0,StrokeAxis.Z);}
Check(nativePipeline.Armed,"Underrange stroke keeps synchronization armed");Near(nativePipeline.Values[0],.1,"Underrange stroke settles at the configured lower bound",.0001);
nativePipeline.InvertStroke=true;
for(int i=0;i<90;i++){nativeFrame.Sequence++;nativeFrame.SampleTime+=.02;nativePipeline.Update(nativeFrame,nativeCandidate,0,StrokeAxis.Z);}
Near(nativePipeline.Values[0],.9,"Inversion is applied before clamping the stroke",.0001);nativePipeline.InvertStroke=false;
foreach(string phase in new[]{"SLoop","OLoop","M_IN_Start","M_IN_Loop","IN_A","A_M_OUT_Start","A_M_OUT_Loop","A_OUT_A"})
{
    nativeFrame.State=phase;nativeCandidate.Native.Target=new(.04,0,0);nativeFrame.Sequence++;nativeFrame.SampleTime+=.02;nativePipeline.Update(nativeFrame,nativeCandidate,0,StrokeAxis.Z);
    Check(nativePipeline.Calibrated&&nativePipeline.Armed,"Same-animation phase preserves calibration and output: "+phase);
}
nativeCandidate.Native.Target=new(.04,1,1);for(int i=1;i<6;i++){nativeCandidate.Native.Minimum[i]=.2;nativeCandidate.Native.Maximum[i]=.3;}
for(int i=0;i<90;i++){nativeFrame.Sequence++;nativeFrame.SampleTime+=.02;nativePipeline.Update(nativeFrame,nativeCandidate,0,StrokeAxis.Z);}
Check(nativePipeline.Armed,"Secondary axis limits never disarm the remaining axes");for(int i=1;i<6;i++)Check(nativePipeline.Values[i]>=.2&&nativePipeline.Values[i]<=.3,"Secondary axis remains within its own bounds: "+i);
for(int i=0;i<30;i++){nativeFrame.Sequence++;nativeFrame.SampleTime+=.02;nativeCandidate.Native.Target=new(i%2==0?.02:.04,0,0);nativePipeline.Update(nativeFrame,nativeCandidate,0,StrokeAxis.Z);}
nativeCandidate.Native.Minimum[0]=.95;nativeFrame.Sequence++;nativeFrame.SampleTime+=.02;nativePipeline.Update(nativeFrame,nativeCandidate,0,StrokeAxis.Z);Check(nativePipeline.Calibrated&&nativePipeline.Armed,"Disjoint axis and global intervals hold the nearest global bound");Near(nativePipeline.Values[0],.9,"Non-overlapping intervals collapse to the boundary");
nativeCandidate.MotionKey="different-animation";nativeFrame.Sequence++;nativeFrame.SampleTime+=.02;nativePipeline.Update(nativeFrame,nativeCandidate,0,StrokeAxis.Z);Check(!nativePipeline.Armed&&!nativePipeline.Calibrated,"A genuinely different animation still requires fresh calibration");
var nativeProfiles=NativeProfiles.Read(new StringReader("# mapping\nclip,RIGHTHAND\nclip2,ORALSWAP\n"));
Check(nativeProfiles["clip"]==NativeTarget.RIGHTHAND,"Explicit native animation mapping");Check(NativeProfiles.ActorIndex(nativeProfiles["clip2"])==1&&NativeProfiles.Primary(nativeProfiles["clip2"])==NativeTarget.ORAL,"Swap selects the second actor and corresponding profile");
Check(NativeProfiles.Stage("WLoop")==0&&NativeProfiles.Stage("SLoop")==1&&NativeProfiles.Stage("OLoop")==2&&NativeProfiles.Stage("MLoop")==1,"KK loop names map to the matching multiplier stages");
Check(NativeProfiles.Stage("Idle")==-1&&NativeProfiles.Stage("Insert")==-1&&NativeProfiles.Stage("unknown")==-1,"Unsupported native phases do not become active loops");
foreach(string phase in new[]{"IN_Start","IN_Loop","M_IN_Start","M_IN_Loop","M_OUT_Start","M_OUT_Loop","WF_IN_Start","WF_IN_Loop","WS_IN_Start","WS_IN_Loop","WS_IN_A","SF_IN_Start","SF_IN_Loop","SS_IN_Start","SS_IN_Loop","SS_IN_A","IN_A","OUT_A"})
    Check(NativeProfiles.Stage(phase)==2&&NativeProfiles.Stage("A_"+phase)==2&&NativeProfiles.PhaseActive(phase,0),"Confirmed climax phase uses orgasm gain even when manual speed is zero: "+phase);
Check(!NativeProfiles.PhaseActive("Idle",2)&&!NativeProfiles.PhaseActive("WLoop",0),"Idle and paused ordinary loops do not qualify as active phases");
bool badMapping=false;try{NativeProfiles.Read(new StringReader("clip,BOTH_FEET"));}catch(FormatException){badMapping=true;}Check(badMapping,"Unsupported upstream placeholder cannot silently become a valid target");
Check(ChannelMonitor.PositionText(0)=="000"&&ChannelMonitor.PositionText(.5)=="500"&&ChannelMonitor.PositionText(1)=="999","Player scale displays both endpoints and midpoint");
Check(ChannelMonitor.PositionText(.5-1e-12)=="500"&&ChannelMonitor.PositionText(.4999)=="499","Display midpoint ignores floating-point tail noise while preserving real command differences");
Check(ChannelMonitor.PositionText(-.1)=="000 ↓"&&ChannelMonitor.PositionText(1.1)=="999 ↑","Player scale marks out-of-range input instead of silently hiding it");
Check(ChannelMonitor.PositionText(double.NaN)=="—"&&ChannelMonitor.PositionText(double.PositiveInfinity)=="—","Missing or invalid measurement is not displayed as zero");
Check(ChannelMonitor.InputText(new MotionPipeline(),0)=="—"&&ChannelMonitor.MappedText(new MotionPipeline(),0)=="—","Player scale does not invent an uncalibrated input or output");
Near(ShapeReference.Normalize(25,0,100),.25,"Shape weight uses explicit endpoints");
Near(ShapeReference.Normalize(25,100,0),.75,"Reversed shape endpoints are supported");
Near(ShapeReference.Normalize(30,10,50),.5,"Nonstandard model shape weight range");
Check(double.IsNaN(ShapeReference.Normalize(0,10,10))&&double.IsNaN(ShapeReference.Normalize(double.NaN,0,100)),"Missing weights and identical endpoints cannot invent a stroke");
Near(ShapeReference.Normalize(150,0,100),1.5,"Raw shape ratio preserves out-of-range evidence");
var shapePipe=new MotionPipeline{Minimum=0,Maximum=1};var shapeSample=NativeFixture();shapeSample.Studio=true;shapeSample.ShapePreview=true;shapeSample.ShapeL0=.25;shapeSample.Gain=5;
var shapeCandidate=new Candidate{Id="shape",Active=true,Native=shapeSample};var shapeFrame=new Snapshot{Session="shape",Scene="fixture",State="test",Active=true,Sequence=1,SampleTime=.02};
void ShapeTick(){shapeFrame.Sequence++;shapeFrame.SampleTime+=.02;shapePipe.Update(shapeFrame,shapeCandidate,0,StrokeAxis.Z);}
ShapeTick();Check(shapePipe.Calibrated&&shapePipe.ShapePreview,"Absolute preview needs no movement-derived calibration");Near(shapePipe.Values[0],.25,"Absolute preview bypasses native midpoint and gain");
Check(!shapePipe.Arm()&&!shapePipe.Armed,"Absolute experiment cannot arm hardware");
Near(shapePipe.Raw[0],.05,"Absolute preview retains skeletal raw world units");
for(int i=1;i<6;i++)Near(shapePipe.Values[i],expectedNative[i],"Absolute preview preserves other axes "+i);
shapeSample.ShapeL0=.75;ShapeTick();Check(shapePipe.Values[0]>.25&&shapePipe.Values[0]<.75,"Absolute preview retains smoothing and rate cap");
for(int i=0;i<60;i++)ShapeTick();Near(shapePipe.Values[0],.75,"Independent shape animation drives the absolute L0 preview",.0001);
shapeSample.ShapeL0=double.NaN;ShapeTick();Check(!shapePipe.Calibrated&&!shapePipe.TryGetMappedValue(0,out monitored),"Removed shape clears preview instead of falling back to bones");
shapeSample.ShapeL0=.4;ShapeTick();Near(shapePipe.Values[0],.4,"Shape recovery discards stale filter");Check(!shapePipe.Armed,"Shape recovery stays disarmed");
shapeFrame.Active=false;shapeSample.ShapeL0=.8;ShapeTick();Check(!shapePipe.TryGetMappedValue(0,out monitored),"Paused preview has no output target");Near(ChannelMonitor.TryNormalized(shapePipe,0,out monitored)?monitored:double.NaN,.8,"Paused inspection still reads current shape weight");
shapeFrame.Active=true;shapeSample.ShapePreview=false;ShapeTick();Check(!shapePipe.ShapePreview&&!shapePipe.Calibrated,"Leaving experiment rebuilds the normal native range");
shapeSample.Studio=false;shapeSample.ShapePreview=true;ShapeTick();Check(!shapePipe.ShapePreview,"Studio shape experiment cannot replace main-game logic");
var direction=DirectionComparison.Measure(NativeFixture(),new(0,0,0),new(1,0,0));Near(direction.Deviation,0,"Aligned BP control direction");
direction=DirectionComparison.Measure(NativeFixture(),new(0,0,0),new(1,1,0));Near(direction.Yaw,45,"BP yaw uses the source's local frame");Near(direction.Pitch,0,"Yaw change is not reported as pitch");
direction=DirectionComparison.Measure(NativeFixture(),new(0,0,0),new(1,0,1));Near(direction.Pitch,45,"BP pitch uses source plane");
var turnedDirection=DirectionComparison.Measure(transformedNative,MoveNative(new(0,0,0),true),MoveNative(new(1,0,1),true));Near(turnedDirection.Pitch,direction.Pitch,"BP direction comparison is invariant under scene rotation",.0001);
bool invalidDirection=false;try{DirectionComparison.Measure(NativeFixture(),new(0,0,0),new(0,0,0));}catch(ArgumentException){invalidDirection=true;}Check(invalidDirection,"Coincident BP control points are rejected");
var hold=new HoldShortcut();Check(!hold.Update(true,10,.7),"Controller shortcut begins with a hold, not an immediate toggle");Check(!hold.Update(true,10.6,.7),"Short controller chord does not toggle");Check(hold.Update(true,10.71,.7),"VR hold uses elapsed time threshold");Check(!hold.Update(true,20,.7),"Holding the chord produces exactly one toggle");Check(!hold.Update(false,20.1,.7)&&!hold.Update(true,21,.7)&&hold.Update(true,21.71,.7),"Controller chord requires release before a second toggle");Check(!hold.Update(false,22,.7),"Tracking loss cancels the active chord");
var focus=new SourceFocus();
var focusOptions=new List<FocusOption>{new(){Id="bp",Priority=2,Distance=2},new(){Id="vagina",Priority=1,Distance=2},new(){Id="other",Distance=2}};
Check(focus.Choose(focusOptions,"",0)=="bp","Equal camera focus prefers BP");focusOptions.RemoveAt(0);
Check(focus.Choose(focusOptions,"",0)=="vagina","Equal focus then prefers vagina");
focusOptions.Add(new(){Id="bp",Priority=2,ScreenRadius=.5,Distance=2});
Check(focus.Choose(focusOptions,"",0)=="vagina","Centered vagina outranks peripheral BP");
focusOptions[2].ScreenRadius=0;focusOptions[2].Distance=20;
Check(focus.Choose(focusOptions,"",0)=="vagina","Camera distance outranks a far BP source");
focusOptions[2].Distance=.5;
Check(focus.Choose(focusOptions,"vagina",1)=="vagina","Challenger must remain stable before switching");
Check(focus.Choose(focusOptions,"vagina",1.5)=="vagina","Brief camera movement does not switch");
Check(focus.Choose(focusOptions,"vagina",1.66)=="bp","Stable camera focus switches after dwell");
focusOptions[2].Distance=2;focusOptions[0].Distance=1.3;
Check(focus.Choose(focusOptions,"bp",2)=="bp"&&focus.Pending=="","Near-equal scores retain selection without jitter");
Check(focus.Choose(new List<FocusOption>(),"bp",3)=="bp","No eligible bindings retains inspection selection");
focusOptions[2].Distance=double.NaN;
Check(focus.Choose(focusOptions,"",4)=="vagina","Non-finite geometry cannot win selection");
var handoff=new SourceHandoff();var handPipe=new MotionPipeline();var handCandidate=new Candidate{Id="a",Active=true,Native=NativeFixture()};handCandidate.Native.Studio=true;
var handFrame=new Snapshot{Session="focus",Scene="focus",Active=true};
void HandTick(){handFrame.Sequence++;handFrame.SampleTime+=.02;handCandidate.Native.Target=new(handFrame.Sequence%2==0?.02:.04,0,0);handPipe.Update(handFrame,handCandidate,0,StrokeAxis.Z);}
for(int i=0;i<20;i++)HandTick();
handoff.Begin(handPipe,true,0);Check(!handoff.Pending,"Auto selection alone never starts synchronization");
Check(handPipe.Arm(),"Focus fixture explicitly enables synchronization");double handPosition=handPipe.Values[0];
handoff.Begin(handPipe,true,0);handCandidate.Id="b";HandTick();
Check(handoff.Pending&&!handoff.Update(handPipe,true,.1)&&!handPipe.Armed,"Source switch waits for its own range");
for(int i=0;i<20;i++)HandTick();
Check(handoff.Update(handPipe,true,1)&&handPipe.Armed,"Previously enabled synchronization resumes after new range is valid");
Near(handPipe.Values[0],handPosition,"Automatic handoff retains previous output position");HandTick();
Check(Math.Abs(handPipe.Values[0]-handPosition)<=.040001,"Handoff respects slew limit from the previous source position");
handoff.Begin(handPipe,true,2);handCandidate.Active=false;HandTick();
Check(!handoff.Update(handPipe,true,2.1)&&!handoff.Pending,"Paused playback cancels pending continuation");handCandidate.Active=true;HandTick();
Check(!handoff.Update(handPipe,true,2.2)&&!handPipe.Armed,"Playback recovery cannot re-enable cancelled continuation");
handPipe.Arm();handoff.Begin(handPipe,true,3);handoff.Cancel();handPipe.Disarm("manual");
Check(!handoff.Update(handPipe,true,3.1)&&!handPipe.Armed,"Manual stop cancels continuation");
handPipe.Arm();handoff.Begin(handPipe,true,4);handPipe.Disarm("switch");
Check(!handoff.Update(handPipe,false,4.1)&&!handoff.Pending,"Device disconnect cancels continuation");
handPipe.Arm();handoff.Begin(handPipe,true,5);handPipe.Disarm("switch");
Check(!handoff.Update(handPipe,true,11.1)&&!handoff.Pending,"Range acquisition timeout cancels continuation");
handPipe.Arm();handoff.Begin(handPipe,true,12);handPipe.Minimum=.7;handPipe.Maximum=.8;handPipe.Disarm("switch");
Check(handoff.Update(handPipe,true,12.1)&&handPipe.Values.All(v=>v>=.7&&v<=.8),"Limits changed during handoff clamp the retained output position immediately");
handPipe.Arm();handoff.Begin(handPipe,true,13);handCandidate.Native.ShapePreview=true;handCandidate.Native.ShapeL0=.5;HandTick();
Check(!handoff.Update(handPipe,true,13.1)&&!handoff.Pending&&!handPipe.Armed,"Shape preview cannot resume device output");
var document=new SceneProfile();var full=new TimelineSegment{Start=0,End=10,Source="stable-binding",Measurement=new MeasurementProfile{LengthScale=1.4,FixedRange=true,RangeStart=.2,RangeEnd=.8}};document.Segments.Add(full);
var second=document.Split(4);Check(document.Segments.Count==2&&full.End==4&&second.Start==4,"Timeline split produces adjacent time ranges");
Check(document.At(3.99)==full&&document.At(4)==second&&document.At(10)==second,"Timeline boundaries choose exactly one segment, including final frame");
Check(document.At(11)==null&&document.At(-1)==null&&document.At(double.NaN)==null,"Time outside configured segments has no assigned segment");
Check(document.At(8)==second&&document.At(1)==full,"Timeline backwards seek and loop use current time rather than previous segment");
string firstMotionKey=full.Measurement.MotionKey;second.Measurement.LengthScale=2;Check(full.Measurement.LengthScale==1.4&&second.Source==full.Source,"Split clones settings independently while retaining the binding");
Check(full.Measurement.MotionKey==firstMotionKey&&second.Measurement.MotionKey!=firstMotionKey,"Editing another segment cannot invalidate the current measurement identity");
bool invalidSegment=false;second.Start=3;try{document.Validate();}catch(ArgumentException){invalidSegment=true;}Check(invalidSegment,"Overlapping segments are rejected");second.Start=4;
var adjusted=new Candidate{Native=NativeFixture()};var adjustment=new MeasurementProfile{LengthScale=.5,RootOffset=new(.01,0,0),TargetOffset=new(.02,0,0),PlaneOffset=new(0,.003,.004)};
adjustment.Apply(adjusted);Near(adjusted.Native.Origin.X,.06,"Root offset follows the configured effective length");Near(adjusted.Native.Target.X,.07,"Target reference offset moves the measurement point");Near((adjusted.Native.End-adjusted.Native.Origin).Length,.04,"Effective length uses the adjusted root");
var unturnedAdjusted=NativeMotion.Solve(adjusted.Native);var rotatedAdjusted=new Candidate{Native=NativeFixture()};
rotatedAdjusted.Native.Origin=MoveNative(rotatedAdjusted.Native.Origin,true);rotatedAdjusted.Native.End=MoveNative(rotatedAdjusted.Native.End,true);rotatedAdjusted.Native.PlanePoint=MoveNative(rotatedAdjusted.Native.PlanePoint,true);rotatedAdjusted.Native.Target=MoveNative(rotatedAdjusted.Native.Target,true);rotatedAdjusted.Native.TargetX=MoveNative(rotatedAdjusted.Native.TargetX,false);rotatedAdjusted.Native.TargetZ=MoveNative(rotatedAdjusted.Native.TargetZ,false);
adjustment.Apply(rotatedAdjusted);var rotatedResult=NativeMotion.Solve(rotatedAdjusted.Native);for(int i=0;i<6;i++)Near(rotatedResult.Normalized[i],unturnedAdjusted.Normalized[i],"Local offset survives whole-scene rotation, axis "+i,.0001);
var sessionPipe=new MotionPipeline();var sessionSync=new SyncSession{Enabled=true};var sessionFrame=new Snapshot{Session="session",Scene="room",Active=true};var sessionCandidate=new Candidate{Id="a",Active=true,Native=NativeFixture()};
var configured=new MeasurementProfile{FixedRange=true,RangeStart=.2,RangeEnd=.8};sessionCandidate.Native.Target=new(.02,0,0);configured.Apply(sessionCandidate);
bool SessionTick(Candidate c,bool connected=true){sessionFrame.Sequence++;sessionFrame.SampleTime+=.02;return sessionSync.Update(sessionPipe,sessionFrame,c,connected,StrokeAxis.Z);}
Check(SessionTick(sessionCandidate)&&sessionPipe.Armed,"Saved range can begin synchronizing immediately without observing a fresh cycle");Near(sessionPipe.Values[0],.1,"Configured insertion start maps to lower output bound");
sessionCandidate.Native.Target=new(.08,0,0);for(int i=0;i<90;i++)SessionTick(sessionCandidate);Near(sessionPipe.Values[0],.9,"Configured insertion end maps to upper output bound",.0001);
sessionFrame.Active=false;Check(!SessionTick(sessionCandidate)&&sessionSync.Enabled&&!sessionPipe.Armed,"Timeline pause holds session enabled without issuing motion");sessionFrame.Active=true;
Check(SessionTick(sessionCandidate)&&sessionSync.Enabled,"Timeline resume restarts configured synchronization automatically");
Check(!SessionTick(null)&&sessionSync.Enabled,"Scene ending retains synchronization intent without a stale source");
sessionFrame.Scene="next scene";sessionCandidate.Id="b";double beforeResume=sessionPipe.Values[0];sessionCandidate.Native.Target=new(.02,0,0);
Check(SessionTick(sessionCandidate),"Next animation or scene resumes without another enable click");Near(sessionPipe.Values[0],beforeResume,"New source starts from the preceding output position");
SessionTick(sessionCandidate);Check(Math.Abs(sessionPipe.Values[0]-beforeResume)<=.040001,"Resumed source respects the existing velocity limit");
Check(!SessionTick(sessionCandidate,false)&&sessionSync.Enabled,"Temporary transport loss keeps synchronization enabled");
Check(SessionTick(sessionCandidate),"Transport recovery uses the current source");
sessionCandidate.Native.Studio=true;sessionCandidate.Native.ShapePreview=true;sessionCandidate.Native.ShapeL0=.5;Check(!SessionTick(sessionCandidate)&&sessionSync.Enabled,"Experimental preview retains intent but never sends movement");sessionCandidate.Native.ShapePreview=false;
sessionSync.Stop();Check(!SessionTick(sessionCandidate)&&!sessionSync.Enabled&&!sessionPipe.Armed,"Explicit stop is authoritative across future valid frames");
var waist=AlignmentFrame.Build(new(0,1,0),new(1,0,0),new(0,1,0));
Near(MotionVector.Dot(waist.X,waist.Y),0,"Waist plane uses torso normal rather than shaft direction");
Near(MotionVector.Cross(waist.X,waist.Y).Z,1,"Waist frame is right handed");
var waistTurn=waist.Adjust(new(.02,.03,.04),new(0,90,0));
Near(waistTurn.World(new(.01,0,0)).Z,-.01,"Point X follows the rotated blue plane");
Near(waistTurn.Origin.Y,1.03,"Reference plane translation follows its original waist anchor");
var localRoundTrip=waistTurn.Local(waistTurn.World(new(.12,.23,.34)));
Near(localRoundTrip.X,.12,"Plane-local XYZ round trips X");Near(localRoundTrip.Y,.23,"Plane-local XYZ round trips Y");Near(localRoundTrip.Z,.34,"Plane-local XYZ round trips Z");
Near(BodyTwist.Measure(waist,waist,new(0,0,1)),0,"Matching pelvis frames produce zero T rotation");
Near(BodyTwist.Measure(waist,waist.Adjust(new(),new(0,0,45)),new(0,0,1)),45,"Target pelvis twist is signed around the main measurement axis");
Near(BodyTwist.Measure(waist,waist.Adjust(new(),new(0,0,-35)),new(0,0,1)),-35,"Opposite target pelvis turn produces negative T");
Near(BodyTwist.Measure(waist,waist.Adjust(new(12,4,7),new()),new(0,0,1)),0,"Pelvis translation alone cannot generate twist");
Near(BodyTwist.Measure(waist,waist.Adjust(new(),new(0,42,0)),new(0,0,1)),0,"Pure pelvis swing does not masquerade as T twist");
Near(BodyTwist.Measure(waist,waist.Adjust(new(),new(50,0,0)),new(1,0,0)),50,"Twist remains measurable when the waist line parallels the shaft");
AlignmentFrame TurnFrame(AlignmentFrame f)=>new(){Origin=MoveNative(f.Origin,true),X=MoveNative(f.X,false),Y=MoveNative(f.Y,false),Z=MoveNative(f.Z,false)};
Near(BodyTwist.Measure(TurnFrame(waist),TurnFrame(waist.Adjust(new(),new(0,0,45))),MoveNative(new(0,0,1),false)),45,"Whole-scene rotation preserves relative pelvis twist",.0001);
var editable=new Candidate{Native=NativeFixture()};editable.Native.Studio=true;editable.Native.HasBodyFrame=true;editable.Native.BodyFrame=waist;editable.Native.ReferenceFrame=waist;
editable.Native.HasTargetFrame=true;editable.Native.TargetBodyFrame=waist;editable.Native.TargetReferenceFrame=waist;
var studioEdit=new MeasurementProfile{AlignmentVersion=1,PlaneRotation=new(0,90,0),RootOffset=new(.01,0,0),EndOffset=new(.02,0,0),TargetOffset=new(.03,0,0)};
studioEdit.Apply(editable);Near(editable.Native.Origin.Z,-.01,"Root calculation point moves in reference-plane X");Near(editable.Native.End.Z,-.02,"Tip is independently editable");Near(editable.Native.Target.Z,0,"Target calculation point moves independently");
Near(editable.Native.ReferenceFrame.Origin.Y,1,"Visible reference plane stays at waist rather than moving to the root");
var uneditedNative=new Candidate{Native=NativeFixture()};var newFieldsNative=new MeasurementProfile{AlignmentVersion=1,EndOffset=new(1,2,3),PlaneRotation=new(70,80,90)};newFieldsNative.Apply(uneditedNative);
Near(uneditedNative.Native.End.X,.1,"Studio frame editing does not alter main-game solver");
var timeView=new TimelineViewport();timeView.Fit(30);timeView.Zoom(.4,3);Near(timeView.At(.4),12,"Zoom preserves time under pointer");
Check(timeView.Span<30&&timeView.Start>0,"Ctrl wheel reveals a shorter time span");timeView.Pan(100);Near(timeView.Start+timeView.Span,30,"Panning is clamped to the clip end");
timeView.Zoom(.5,100);Check(timeView.Span>=.05,"Timeline zoom has finite subsecond minimum");timeView.Fit(30);Near(timeView.Tick(950),5,"Timeline ruler chooses readable major intervals");
var forked=second.Measurement.Clone();forked.AlignmentVersion=1;forked.EndOffset=new(.1,.2,.3);forked.PlaneRotation=new(1,2,3);forked.TargetPlaneRotation=new(4,5,6);second.Measurement=forked;
var third=document.Split(7);third.Measurement.PlaneRotation.X=99;Near(second.Measurement.PlaneRotation.X,1,"Segment split keeps independent reference rotations");Near(third.Measurement.TargetPlaneRotation.Z,6,"Segment split preserves target pelvis reference alignment");
var edits=new SceneProfile();var ea=new TimelineSegment{Start=0,End=3,Source="A",Name="first"};var eb=new TimelineSegment{Start=3,End=5,Source=TimelineSegment.NoSource,Name="无"};var ec=new TimelineSegment{Start=7,End=10,Source="C"};
edits.Segments.AddRange(new[]{ea,eb,ec});edits.Validate();
Check(ea.ColorIndex!=eb.ColorIndex&&eb.ColorIndex!=ec.ColorIndex,"Each new interval receives an independent saved color");
var ed=edits.Split(1);Check(ed.ColorIndex!=ea.ColorIndex&&ed.Name==ea.Name,"Split copies name and processing while assigning a fresh color");
edits.Resize(ed.Id,false,4,10);Near(ed.End,3,"Right edge clamps against the next block");
edits.Resize(eb.Id,true,-1,10);Near(eb.Start,3,"Left edge clamps against the previous block");
edits.Resize(eb.Id,false,20,10);Near(eb.End,7,"Resize can fill the adjacent gap but cannot overlap");
edits.Resize(eb.Id,false,4,10);Near(eb.End,4,"Dragging edge inward leaves a real empty interval");
var gap=edits.CreateGap(6,10);Near(gap.Start,4,"Blank-area creation begins at the neighbouring edge");Near(gap.End,7,"Blank-area creation fills only the containing gap");Check(gap.Source==TimelineSegment.NoSource,"An empty new interval cannot silently fall back to the prior source");
edits.Segments.Remove(gap);var colorBefore=ea.ColorIndex;double aLength=ea.End-ea.Start,cLength=ec.End-ec.Start;
edits.Move(ea.Id,9.7,10);Near(ea.End-ea.Start,aLength,"Reordering preserves the lifted block duration");Near(ec.End-ec.Start,cLength,"Reordering preserves the displaced block duration");Check(edits.Segments[edits.Segments.Count-1]==ea&&ea.ColorIndex==colorBefore&&ea.Source=="A","Reorder carries identity, color and source together");
Near(ea.End,10,"Reordering preserves overall occupied extent");edits.Validate();
var movingGap=new SceneProfile();var mg=new TimelineSegment{Start=0,End=1};movingGap.Segments.Add(mg);movingGap.Move(mg.Id,5,10);Near(mg.Start,4.5,"Dropping in free space moves the segment without ripple");
Check(eb.Title()=="无"&&eb.Source!=string.Empty,"No-source state is distinct from automatic source selection");
NativeMotionSample RootFixture()=>new(){Studio=true,RootDistance=true,HasBodyFrame=true,HasTargetFrame=true,UseReferenceUp=true,Origin=new(0,0,0),End=new(0,.2,0),Target=new(0,0,0),TargetHip=new(0,.1,.1),BodyFrame=AlignmentFrame.Build(new(),new(1,0,0),new(0,1,0)),ReferenceFrame=AlignmentFrame.Build(new(),new(1,0,0),new(0,1,0)),TargetBodyFrame=AlignmentFrame.Build(new(),new(1,0,0),new(0,1,0)),TargetReferenceFrame=AlignmentFrame.Build(new(),new(1,0,0),new(0,1,0))};
var roots=RootFixture();var rootResult=NativeMotion.Solve(roots);
Near(rootResult.Normalized[0],0,"Coincident roots mean L0 000");Near(rootResult.Raw[5],-45,"R2 reads the line's angle to the in-plane pitch axis");Near(rootResult.Raw[4],0,"No lateral target tilt means zero R1");
roots.Target=new(.03,.04,0);rootResult=NativeMotion.Solve(roots);Near(rootResult.Raw[0],.05,"Root stroke is Euclidean distance, not a shaft projection");Near(rootResult.Normalized[0],.25,"Root distance divided by effective length");Near(rootResult.Raw[1],.03,"L1 measures pelvis left-right projection");Near(rootResult.Raw[2],0,"Vertical separation does not inflate L2");
roots.Target=new(0,0,.2);rootResult=NativeMotion.Solve(roots);Near(rootResult.Normalized[0],1,"A full reference length means L0 999");Near(rootResult.Raw[2],.2,"L2 measures pelvis forward-back projection");
roots=RootFixture();roots.TargetHip=new(.1,0,.1);rootResult=NativeMotion.Solve(roots);Near(rootResult.Raw[4],45,"R1 normalizes lateral projection by reference line length");Near(rootResult.Raw[5],-45,"A line 45 degrees off the pitch axis reads -45 R2");
roots.TargetHip=new(-.2,0,.2);Near(NativeMotion.Solve(roots).Raw[4],-45,"R1 retains direction independently of character size");
roots.TargetHip=new(0,-.1,0);Near(NativeMotion.Solve(roots).Raw[5],0,"A line perpendicular to the pitch axis reads zero R2");
roots=RootFixture();roots.Target=new(.01,.02,.03);rootResult=NativeMotion.Solve(roots);
var movedRoots=RootFixture();movedRoots.Origin=MoveNative(roots.Origin,true);movedRoots.End=MoveNative(roots.End,true);movedRoots.Target=MoveNative(roots.Target,true);movedRoots.TargetHip=MoveNative(roots.TargetHip,true);movedRoots.BodyFrame=TurnFrame(roots.BodyFrame);movedRoots.ReferenceFrame=TurnFrame(roots.ReferenceFrame);movedRoots.TargetBodyFrame=TurnFrame(roots.TargetBodyFrame);movedRoots.TargetReferenceFrame=TurnFrame(roots.TargetReferenceFrame);
var rigidRoots=NativeMotion.Solve(movedRoots);for(int i=0;i<6;i++)Near(rigidRoots.Raw[i],rootResult.Raw[i],"Root solver rigid-transform invariance "+i,.0001);
var rootCandidate=new Candidate{Native=RootFixture()};var rootProfile=new MeasurementProfile{AlignmentVersion=1,LengthScale=1.5,RootOffset=new(.01,0,0),TargetOffset=new(.02,0,0)};rootProfile.Apply(rootCandidate);
Near(rootCandidate.Native.Origin.X,.01,"Length scaling keeps the root anchored until its explicit offset");Near(rootCandidate.Native.End.Y,.3,"Root-based length scaling moves the effective tip");Near((rootCandidate.Native.ReferenceFrame.Origin-rootCandidate.Native.Origin).Length,0,"Blue plane follows edited root");Near((rootCandidate.Native.TargetReferenceFrame.Origin-rootCandidate.Native.Target).Length,0,"Pink frame follows edited target root");
foreach(double fraction in new[]{0.0,.2,.8,1.4})
{
    var distancePipe=new MotionPipeline{Minimum=0,Maximum=1};var distanceSample=RootFixture();distanceSample.Target=new(0,.2*fraction,0);
    var distanceCandidate=new Candidate{Id="root-distance",Active=true,Native=distanceSample};var distanceFrame=new Snapshot{Session="root",Scene="studio",State="play",Active=true,Sequence=1,SampleTime=.02};
    distancePipe.Update(distanceFrame,distanceCandidate,0,StrokeAxis.Z);Check(distancePipe.Calibrated,"Root distance is absolute and does not require observed motion "+fraction);
    Near(distancePipe.Values[0],Math.Clamp(fraction,0,1),"Root distance output keeps the defined position without recentering "+fraction);
}
var continuous=new ContinuousAngle();
Near(continuous.Resolve(178),178,"Continuous twist initializes from the measured angle");
Near(continuous.Resolve(-179),181,"Positive seam crossing uses the closest revolution");
Near(continuous.Resolve(double.NaN),181,"Undefined twist holds its last valid orientation");
Near(continuous.Resolve(-179),181,"Repeated rendering of the same sample does not accumulate rotation");
continuous.Reset();Near(continuous.Resolve(-178),-178,"New source clears the previous revolution");
Near(continuous.Resolve(179),-181,"Negative seam crossing retains rotation direction");
foreach(int twistDirection in new[]{-1,1})
{
    var twistPipe=new MotionPipeline{Minimum=0,Maximum=1};
    var twistFrame=new Snapshot{Session="twist",Scene="studio",Active=true,State="play"};
    for(int step=0;step<7;step++)
    {
        double angle=twistDirection*(-2+step);
        var sample=RootFixture();sample.TargetReferenceFrame=sample.TargetReferenceFrame.Adjust(new(),new(0,twistDirection*(178+step),0));
        var twistCandidate=new Candidate{Id="same-pair",Active=true,Native=sample,MotionKey=step<3?"segment-a":"segment-b"};
        twistFrame.Sequence++;twistFrame.SampleTime+=.02;
        twistFrame.Active=step!=2;twistFrame.State=step==2?"paused":"play";
        twistPipe.Update(twistFrame,twistCandidate,0,StrokeAxis.Z);
        Near(twistPipe.Raw[3],angle,"R0 crosses seam continuously across pause and preset changes "+twistDirection+"/"+step);
        Near(NativeMotion.Solve(sample).Raw[3],angle,"3D readout uses the pipeline's continuous R0 "+twistDirection+"/"+step);
        twistPipe.TryGetNativeInput(3,out double twistInput);Near(twistInput,.5+angle/180,"Monitor uses the symmetric 90-degree range "+twistDirection+"/"+step);
        if(twistFrame.Active){Check(twistPipe.Arm(),"R0 boundary does not disable synchronization "+twistDirection+"/"+step);Check(twistPipe.Values[3]>.47&&twistPipe.Values[3]<.53,"Parallel pelvis lines remain near device midpoint across the old seam "+twistDirection+"/"+step);}
    }
    var other=RootFixture();other.TargetReferenceFrame=other.TargetReferenceFrame.Adjust(new(),new(0,-twistDirection*170,0));
    twistFrame.Sequence++;twistFrame.SampleTime+=.02;
    twistPipe.Update(twistFrame,new Candidate{Id="different-pair",Active=true,Native=other},0,StrokeAxis.Z);
    Near(twistPipe.Raw[3],twistDirection*10,"Changing physical source selects its nearest parallel alignment "+twistDirection);
    other.TargetReferenceFrame=other.TargetReferenceFrame.Adjust(new(),new(0,twistDirection*340,0));
    twistFrame.Scene="new scene";twistFrame.Sequence++;twistFrame.SampleTime+=.02;
    twistPipe.Update(twistFrame,new Candidate{Id="different-pair",Active=true,Native=other},0,StrokeAxis.Z);
    Near(twistPipe.Raw[3],-twistDirection*10,"Changing scene clears source rotation history "+twistDirection);
    other.RotationContext=new object();other.TargetReferenceFrame=other.TargetReferenceFrame.Adjust(new(),new(0,-twistDirection*340,0));
    twistFrame.Sequence++;twistFrame.SampleTime+=.02;twistPipe.Update(twistFrame,new Candidate{Id="different-pair",Active=true,Native=other},0,StrokeAxis.Z);
    Near(twistPipe.Raw[3],twistDirection*10,"Reloading a card in the same Unity scene clears rotation history "+twistDirection);
}
foreach(double degrees in new[]{-90d,-45,0,45,90,180,-180})
{
    var pelvis=RootFixture();pelvis.TargetReferenceFrame=pelvis.TargetReferenceFrame.Adjust(new(),new(0,degrees,0));
    var measurement=NativeMotion.Solve(pelvis);double expected=Math.Abs(degrees)==180?0:degrees;
    Near(measurement.Raw[3],expected,"Pelvis line signed angle "+degrees);
    Near(measurement.Normalized[3],.5+expected/180,"Pelvis line midpoint and symmetric endpoints "+degrees);
}
var pitchedPelvis=RootFixture();pitchedPelvis.TargetReferenceFrame=pitchedPelvis.TargetReferenceFrame.Adjust(new(),new(75,0,0));
Near(NativeMotion.Solve(pitchedPelvis).Raw[3],0,"Target torso pitch with an unchanged pelvis line does not create R0");
pitchedPelvis.TargetReferenceFrame=RootFixture().TargetReferenceFrame.Adjust(new(),new(0,25,0));pitchedPelvis.R0Continuity=new ContinuousAngle(180);
Near(NativeMotion.Solve(pitchedPelvis).Raw[3],25,"Pelvis projection tracks the signed in-plane angle");
pitchedPelvis.TargetReferenceFrame=RootFixture().TargetReferenceFrame.Adjust(new(),new(0,0,90));
Near(NativeMotion.Solve(pitchedPelvis).Raw[3],25,"Near-zero projected pelvis line holds the last valid R0");
foreach(int turnSign in new[]{-1,1})
{
    var reserve=new ContinuousAngle(180);
    foreach(double degrees in new[]{85d,89,91,95,100,95,91,89,85})
    {
        double wrapped=PelvisTwist.Measure(waist,waist.Adjust(new(),new(0,turnSign*degrees,0)));
        double reserveAngle=reserve.Resolve(wrapped);Near(reserveAngle,turnSign*degrees,"R0 reserve beyond 90 degrees stays continuous and returns from the endpoint "+turnSign+"/"+degrees);
    }
}
NativeMotionSample ContactFixture()
{
    var s=RootFixture();s.Target=s.AnchorTarget=new(0,0,.1);s.AnchorRoot=s.Origin;
    s.Contact=new(){SourceLeft=new(-.1,0,-.1),SourceRight=new(.1,0,-.1),TwistStart=s.Target,TwistEnd=s.Target+new MotionVector(.1,0,0),TiltStart=s.Target,TiltEnd=s.Target+new MotionVector(.1,.1,0),SourceTorso=new(0,1,0),TargetTorso=new(0,1,0),Name="手部参考"};return s;
}
var contact=ContactFixture();var contactResult=NativeMotion.Solve(contact);
foreach(var expected in new[]{(0,.1),(1,.1),(2,0d),(3,0d),(4,45d),(5,-45d)})Near(contactResult.Raw[expected.Item1],expected.Item2,"Legacy contact geometry known reference channel "+expected.Item1);
Near(contactResult.Normalized[0],.5,"Contact L0 uses universal source root and length");
contact.TargetHip=new(55,-27,12);for(int i=0;i<6;i++)Near(NativeMotion.Solve(contact).Raw[i],contactResult.Raw[i],"Contact uses selected part rather than the target pelvis "+i);
var changedContact=ContactFixture();changedContact.Contact.TiltEnd=changedContact.Target+new MotionVector(0,.1,.1);
Check(Math.Abs(NativeMotion.Solve(changedContact).Raw[4]-contactResult.Raw[4])>1,"Moving the contact direction changes roll without moving the torso");
var movedContact=ContactFixture();movedContact.Origin=MoveNative(contact.Origin,true);movedContact.End=MoveNative(contact.End,true);movedContact.Target=MoveNative(contact.Target,true);movedContact.AnchorRoot=movedContact.Origin;movedContact.AnchorTarget=movedContact.Target;
movedContact.BodyFrame=TurnFrame(contact.BodyFrame);movedContact.ReferenceFrame=TurnFrame(contact.ReferenceFrame);movedContact.TargetBodyFrame=TurnFrame(contact.TargetBodyFrame);movedContact.TargetReferenceFrame=TurnFrame(contact.TargetReferenceFrame);
movedContact.Contact.SourceLeft=MoveNative(contact.Contact.SourceLeft,true);movedContact.Contact.SourceRight=MoveNative(contact.Contact.SourceRight,true);movedContact.Contact.TwistStart=MoveNative(contact.Contact.TwistStart,true);movedContact.Contact.TwistEnd=MoveNative(contact.Contact.TwistEnd,true);movedContact.Contact.TiltStart=MoveNative(contact.Contact.TiltStart,true);movedContact.Contact.TiltEnd=MoveNative(contact.Contact.TiltEnd,true);movedContact.Contact.SourceTorso=MoveNative(contact.Contact.SourceTorso,false);movedContact.Contact.TargetTorso=MoveNative(contact.Contact.TargetTorso,false);
// MoveNative intentionally uses Unity-like single precision, including subtraction of translated points.
for(int i=0;i<6;i++)Near(NativeMotion.Solve(movedContact).Raw[i],contactResult.Raw[i],"Contact whole-scene transform invariance "+i+" actual="+NativeMotion.Solve(movedContact).Raw[i],i<3?.00001:.001);
contact=ContactFixture();contact.R0Continuity=new ContinuousAngle(180);
foreach(double degrees in new[]{85d,89,91,95,91,89}){double radians=degrees*Math.PI/180;contact.Contact.TwistEnd=contact.Contact.TwistStart+new MotionVector(Math.Cos(radians),0,-Math.Sin(radians));Near(NativeMotion.Solve(contact).Raw[3],degrees,"Contact R0 continuous line angle "+degrees);}
var contactPipe=new MotionPipeline();var contactCandidate=new Candidate{Id="same-partner",Active=true,Native=ContactFixture()};contactCandidate.Native.RotationBasis="hand";
var contactSnapshot=new Snapshot{Session="parts",Scene="parts",Active=true,Sequence=1,SampleTime=.02};contactPipe.Update(contactSnapshot,contactCandidate,0,StrokeAxis.Z);var handTracker=contactCandidate.Native.R0Continuity;
contactCandidate.Native.RotationBasis="foot";contactSnapshot.Sequence++;contactSnapshot.SampleTime+=.02;contactPipe.Update(contactSnapshot,contactCandidate,0,StrokeAxis.Z);Check(!ReferenceEquals(handTracker,contactCandidate.Native.R0Continuity),"Switching body-part references resets their independent R0 continuity");
var sized=RootFixture();sized.Studio=false;sized.Target=new(.02,.01,.03);sized.TargetHip=sized.Target+new MotionVector(.1,.1,0);
var unsized=NativeMotion.Solve(sized);sized.PlaneWidth=.4;sized.PlaneDepth=.6;var resized=NativeMotion.Solve(sized);
Near(resized.Normalized[1]-.5,(unsized.Normalized[1]-.5)/2,"Doubling plane width halves L1 response in main game");
Near(resized.Normalized[2]-.5,(unsized.Normalized[2]-.5)/3,"Independent plane depth scales L2");
Near(resized.Normalized[4]-.5,(unsized.Normalized[4]-.5)/2,"Plane width scales R1 response around its center");
foreach(int channel in new[]{0,3,5})Near(resized.Normalized[channel],unsized.Normalized[channel],"Plane dimensions leave unrelated channel unchanged "+channel);
for(int i=0;i<6;i++)Near(resized.Raw[i],unsized.Raw[i],"Plane dimensions retain physical measurement "+i);
sized.HiddenObjects=31;for(int i=0;i<6;i++)Near(NativeMotion.Solve(sized).Normalized[i],resized.Normalized[i],"Hidden editable objects continue calculating "+i);
var deep=new MeasurementProfile{AlignmentVersion=1,PlaneWidth=.3,HiddenObjects=6};deep.Multipliers[0]=2;var copy=deep.Clone();copy.Multipliers[0]=3;copy.ChannelMaximum[1]=.7;Near(deep.Multipliers[0],2,"Cloned animation/segment gain is independent");Near(deep.ChannelMaximum[1],1,"Cloned segment limits are independent");
var limitedCandidate=new Candidate{Native=RootFixture()};limitedCandidate.Native.Minimum[0]=.1;limitedCandidate.Native.Maximum[0]=.4;var limitedProfile=new MeasurementProfile{AlignmentVersion=1};limitedProfile.ChannelMinimum[0]=.7;limitedProfile.ChannelMaximum[0]=.9;limitedProfile.Apply(limitedCandidate);Near(limitedCandidate.Native.Minimum[0],.4,"Profile lower bound cannot exceed the configured channel ceiling");Near(limitedCandidate.Native.Maximum[0],.4,"Nonoverlapping animation bounds hold at the channel ceiling");
var gainSample=RootFixture();gainSample.Studio=false;gainSample.Target=new(0,.2*200/999,0);gainSample.Multipliers[0]=2;
var gainPipe=new MotionPipeline{Minimum=0,Maximum=1};var gainFrame=new Snapshot{Session="gain",Scene="main",Sequence=1,SampleTime=.02,Active=true};var gainCandidate=new Candidate{Id="native-gain",Native=gainSample,Active=true};gainPipe.Update(gainFrame,gainCandidate,0,StrokeAxis.Z);
Near(gainPipe.Desired[0],400d/999,"Protocol position 200 multiplied by two gives 400");Check(gainPipe.Calibrated&&gainPipe.Arm(),"Main game root distance works from first valid sample");
gainSample.Maximum[0]=.35;gainFrame.Sequence++;gainFrame.SampleTime+=.02;gainPipe.Update(gainFrame,gainCandidate,0,StrokeAxis.Z);Near(gainPipe.Desired[0],.35,"Animation limit clamps multiplied position");Check(gainPipe.Armed,"Per-animation clamp does not disable synchronization");
gainFrame.Active=false;gainFrame.Sequence++;gainFrame.SampleTime+=.02;gainPipe.Update(gainFrame,gainCandidate,0,StrokeAxis.Z);Check(gainPipe.HasDesired&&!gainPipe.Armed,"Paused preview retains exact target value without output");
var normalMain=RootFixture();normalMain.Studio=false;var normalStudio=RootFixture();for(int i=0;i<6;i++)Near(NativeMotion.Solve(normalMain).Normalized[i],NativeMotion.Solve(normalStudio).Normalized[i],"Main and Studio use the same normal solver "+i);
var contactMain=ContactFixture();contactMain.Studio=false;for(int i=0;i<6;i++)Near(NativeMotion.Solve(contactMain).Normalized[i],NativeMotion.Solve(ContactFixture()).Normalized[i],"Main and Studio use the same body-part solver "+i);
NativeStrokeChecks.Run(Check,RootFixture,args);
FunscriptChecks.Run(Check);
Console.WriteLine("PASS: "+checks.Count+" assertions (geometry, animation profiles, plane size, channel mapping, continuous angles, timeline editing and protocols)");
