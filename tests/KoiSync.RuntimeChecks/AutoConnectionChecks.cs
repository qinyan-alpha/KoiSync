using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using KoiSync.Plugin;
using UnityEngine;
using UnityEngine.UI;

[BepInPlugin("local.koisync.autochecks","KoiSync automatic connection checks","0.1.0")]
[BepInDependency("local.koisync")]
public sealed class AutoConnectionChecks:BaseUnityPlugin
{
    private const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private readonly StringBuilder report=new StringBuilder();
    private object Field(object o,string name){return o.GetType().GetField(name,F).GetValue(o);}
    private void Call(object o,string name,params object[] args){o.GetType().GetMethod(name,F).Invoke(o,args);}
    private void Check(bool ok,string name){report.AppendLine((ok?"PASS ":"FAIL ")+name);if(!ok)throw new Exception(name);}
    private void Awake(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"--koisync-auto-checks")>=0)StartCoroutine(Run());}
    private IEnumerator Wait(Func<bool> predicate)
    {float end=Time.realtimeSinceStartup+2;while(!predicate()&&Time.realtimeSinceStartup<end)yield return null;Check(predicate(),"Connection attempt finishes within the runtime test deadline");}
    private IEnumerator Run()
    {
        Application.runInBackground=true;yield return new WaitForSecondsRealtime(4);
        var host=UnityEngine.Object.FindObjectOfType<Plugin>();host.enabled=false;host.StopAllCoroutines();
        var panel=(KoiSyncPanel)Field(host,"panel");var config=(ConfigEntry<bool>)Field(panel,"autoConnect");
        IEnumerator checks=Checks(host,panel,config);
        while(true)
        {
            bool more;object next;
            try{more=checks.MoveNext();next=more?checks.Current:null;}catch(Exception ex){report.AppendLine("FAIL "+ex);break;}
            if(!more)break;yield return next;
        }
        (checks as IDisposable)?.Dispose();config.Value=false;host.Devices.Disconnect();host.Devices.Dispose();
        File.WriteAllText(Path.Combine(Paths.GameRootPath,"auto-connection-checks.txt"),report.ToString());Application.Quit();
    }
    private IEnumerator Checks(Plugin host,KoiSyncPanel panel,ConfigEntry<bool> config)
    {
        string[] ports=SerialTransport.GetPortNames();report.AppendLine("Native scan: "+string.Join(", ",ports));
        Check(Array.TrueForAll(ports,p=>p.StartsWith("COM")),"Port inventory works under the actual Unity CLR without opening devices");
        Check(config.DefaultValue.Equals(true),"Automatic scanning and connection are enabled by default");
        config.Value=true;host.Devices.Dispose();int attempts=0;DeviceSettings opened=null;IdleTransport transport=null;
        host.Devices=new DeviceHub(s=>{attempts++;opened=s;transport=new IdleTransport();return transport;},null,2);
        var endpoint=(InputField)Field(panel,"endpoint");var argument=(InputField)Field(panel,"argument");
        ((Dropdown)Field(panel,"transport")).value=0;endpoint.text="COM4";argument.text="115200";
        panel.PortScanner=()=>new[]{"COM4","COM5"};Call(panel,"ResumeAutoConnection");panel.UpdateAutoConnection(0);
        yield return Wait(()=>host.Devices.Connected);
        Check(opened.Endpoint=="COM4"&&opened.Argument==115200&&attempts==1,"Scanner chooses the saved COM4 endpoint among multiple ports");
        Check(!host.Pipeline.Armed&&transport.Commands==0,"Automatic connection does not enable movement output");
        ((Button)Field(panel,"connect")).onClick.Invoke();panel.UpdateAutoConnection(30);
        Check(!host.Devices.Connected&&attempts==1,"Manual disconnect suspends automatic reconnection");
        Call(panel,"ResumeAutoConnection");panel.UpdateAutoConnection(31);yield return Wait(()=>host.Devices.Connected);
        Check(attempts==2,"An explicit rescan resumes connection attempts");
        ((Button)Field(panel,"connect")).onClick.Invoke();config.Value=false;Call(panel,"ResumeAutoConnection");panel.UpdateAutoConnection(50);
        Check(attempts==2&&!host.Devices.Connecting,"Disabling automatic connection prevents retries");
        config.Value=true;endpoint.text="COM9";Call(panel,"ResumeAutoConnection");panel.UpdateAutoConnection(60);
        Check(attempts==2,"A missing saved device is not replaced with an unrelated port");
        panel.PortScanner=()=>new[]{"COM4","COM9"};panel.UpdateAutoConnection(63);yield return Wait(()=>host.Devices.Connected);
        Check(attempts==3&&opened.Endpoint=="COM9","A saved port appearing in a later scan connects automatically");
        host.Devices.Dispose();attempts=0;host.Devices=new DeviceHub(s=>{attempts++;throw new IOException("fixture occupied");},null,2);
        endpoint.text="COM4";Call(panel,"ResumeAutoConnection");panel.UpdateAutoConnection(100);yield return Wait(()=>!host.Devices.Connecting&&host.Devices.Status.Contains("fixture occupied"));
        panel.UpdateAutoConnection(101);report.AppendLine("Backoff attempts="+attempts+" status="+host.Devices.Status+" error="+Field(panel,"connectionError")+" endpoint="+endpoint.text+" argument="+argument.text);Check(attempts==1&&host.Devices.Status.Contains("fixture occupied"),"Failed automatic connection keeps its error visible during backoff");
        panel.UpdateAutoConnection(111);yield return Wait(()=>!host.Devices.Connecting);Check(attempts==2,"Automatic retry is paced instead of restarting every frame");
        host.Demo=true;Call(panel,"ResumeAutoConnection");panel.UpdateAutoConnection(200);Check(attempts==2,"Demo mode suppresses automatic connections");host.Demo=false;
    }
    private sealed class IdleTransport:ITransport
    {
        public int Commands;
        public void Send(string text,double value){Commands++;}
        public void Stop(){Commands++;}
        public void Tick(){}
        public void Dispose(){}
    }
}
