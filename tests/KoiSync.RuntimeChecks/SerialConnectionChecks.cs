using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using BepInEx;
using KoiSync.Core;
using KoiSync.Plugin;
using UnityEngine;

[BepInPlugin("local.koisync.serialchecks","KoiSync serial checks","0.1.0")]
[BepInDependency("local.koisync")]
public sealed class SerialConnectionChecks:BaseUnityPlugin
{
    private readonly object gate=new object();
    private string report="";
    private volatile bool finished;
    private string portName;
    private volatile int heartbeat;
    private void Record(string text)
    {
        lock(gate){report+=text+Environment.NewLine;File.WriteAllText(Path.Combine(Paths.GameRootPath,"serial-connection-checks.txt"),report);}
    }
    private void Awake()
    {
        string[] args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--koisync-serial-probe");
        if(index<0||index+1>=args.Length)return;
        portName=args[index+1];StartCoroutine(Run());
    }
    private IEnumerator Run()
    {
        Application.runInBackground=true;yield return new WaitForSecondsRealtime(4);
        Record("Unity "+Application.unityVersion+" / CLR "+Environment.Version+" / "+portName+" at 115200 / no movement offered");
        Thread probe=new Thread(Probe){IsBackground=true,Name="KoiSync.SerialProbe"};probe.Start();
        new Thread(()=>{heartbeat=1;}){IsBackground=true}.Start();
        float deadline=Time.realtimeSinceStartup+12;
        while(!finished&&Time.realtimeSinceStartup<deadline)yield return null;
        if(!finished)Record("FAIL probe did not return in 12 seconds; heartbeat="+heartbeat+" thread="+probe.ThreadState);
        Application.Quit();
    }
    private void Probe()
    {
        Record("Probe thread entered");
        try{ProbeCore();}catch(Exception ex){Record("FAIL outside ProbeCore "+ex);finished=true;}
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProbeCore()
    {
        try
        {
            Stopwatch watch=Stopwatch.StartNew();Record("Native serial constructor begin");
            var settings=new DeviceSettings{Transport=0,Endpoint=portName,Argument=115200,Kind=DeviceKind.SR6};
            using(SerialTransport port=new SerialTransport(settings))
            {
                port.Tick();Record("PASS native serial open and status query elapsed_ms="+watch.ElapsedMilliseconds);
                bool occupied=false;try{using(var duplicate=new SerialTransport(settings)){}}
                catch(IOException ex){occupied=ex.Message.Contains("Win32 5")||ex.Message.Contains("Win32 32");Record("duplicate="+ex.Message);}
                Record((occupied?"PASS":"FAIL")+" occupied port is rejected with an explicit error");
            }
            Record("PASS native serial disposed elapsed_ms="+watch.ElapsedMilliseconds);
            using(DeviceHub hub=new DeviceHub())
            {
                Record("DeviceHub.Connect begin");hub.Connect(new DeviceSettings{Transport=0,Endpoint=portName,Argument=115200,Kind=DeviceKind.SR6});
                watch.Reset();watch.Start();string previous="";
                while(watch.ElapsedMilliseconds<8000&&!hub.Connected)
                {if(previous!=hub.Status){previous=hub.Status;Record("hub="+previous);}Thread.Sleep(20);}
                Record((hub.Connected?"PASS":"FAIL")+" hub connected="+hub.Connected+" status="+hub.Status+" elapsed_ms="+watch.ElapsedMilliseconds);
                Record("movement="+hub.GetTelemetry().LastMovementCommand);hub.Disconnect();
            }
            using(SerialTransport reopened=new SerialTransport(settings))
            {reopened.Tick();Record("PASS COM port released after hub disconnect and disposal");}
        }
        catch(Exception ex){Record("FAIL "+ex);}
        finally{finished=true;}
    }
}
