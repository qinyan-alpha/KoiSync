using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KoiSync.Core;
using KoiSync.Plugin;
using LitJson;

internal static class Program
{
    private static int count;
    private static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;Console.WriteLine("PASS "+label);}
    private static void Until(Func<bool> predicate){Stopwatch w=Stopwatch.StartNew();while(!predicate()){if(w.ElapsedMilliseconds>5000)throw new Exception("Timed out");Thread.Sleep(10);}}
    public static int Main()
    {
        try
        {
            CheckConnectionLifecycle();
            CheckOscillationTransport();
            using(UdpClient receiver=new UdpClient(new IPEndPoint(IPAddress.Loopback,0)))
            using(DeviceHub hub=new DeviceHub())
            {
                receiver.Client.ReceiveTimeout=2000;
                Check(!hub.Connected,"No automatic device connection");
                Check(hub.GetTelemetry().LastMovementCommand=="","No fabricated command before sending");
                hub.Connect(new DeviceSettings{Transport=1,Endpoint="127.0.0.1",Argument=((IPEndPoint)receiver.Client.LocalEndPoint).Port,Kind=DeviceKind.SR6});
                Until(()=>hub.Connected);
                hub.Offer(new[]{.5,.5,.5,.5,.5,.5},new[]{true,false,false,false,false,false},DeviceKind.SR6);
                IPEndPoint sender=null;
                Check(Encoding.ASCII.GetString(receiver.Receive(ref sender))=="L05000I20\n","Loopback UDP receives correctly framed TCode");
                Until(()=>hub.GetTelemetry().LastMovementCommand!="");
                Check(hub.GetTelemetry().LastMovementCommand=="L05000I20","Telemetry records the serialized command after sending");
                bool stopped=false;Stopwatch watch=Stopwatch.StartNew();
                while(watch.ElapsedMilliseconds<1500){string line=Encoding.ASCII.GetString(receiver.Receive(ref sender));if(line=="DSTOP\n"){stopped=true;break;}}
                Check(stopped,"Output worker stops after input expires, without Unity Update");
                Check(!hub.GetTelemetry().OutputRequested,"Expired input is not reported as an active send request");
                Check(hub.GetTelemetry().LastMovementCommand=="L05000I20","Stopped output retains clearly historical last command");
                hub.Offer(new[]{.1,.2,.3,.4,.5,.6},new[]{true,true,true,true,true,true},DeviceKind.SR6);
                Check(Encoding.ASCII.GetString(receiver.Receive(ref sender))=="L01000I20 L12000I20 L23000I20 R04000I20 R15000I20 R26000I20\n","Selected six axes reach the TCode transport, not just the monitor");
                hub.Disconnect();Check(!hub.Connected,"Disconnect clears connection state");
            }
            using(FakeIntiface server=new FakeIntiface(true))
            using(IntifaceTransport client=new IntifaceTransport(new DeviceSettings{Endpoint=server.Url,Argument=7}))
            {
                client.Send("",.25);Thread.Sleep(150);client.Tick();client.Stop();
                Check(server.Messages.Contains("RequestServerInfo"),"Intiface v3 handshake");
                Check(server.Messages.Contains("RequestDeviceList"),"Device capability discovery");
                Check(server.Messages.Contains("LinearCmd"),"LinearCmd request and acknowledgement");
                Check(server.Position==.25,"LinearCmd position preserved");
                Check(server.Messages.Contains("Ping"),"MaxPingTime heartbeat");
                Check(server.Messages.Contains("StopDeviceCmd"),"Intiface explicit stop");
            }
            using(FakeIntiface server=new FakeIntiface(false))
            {
                bool rejected=false;try{using(var client=new IntifaceTransport(new DeviceSettings{Endpoint=server.Url,Argument=7})){} }
                catch(System.IO.IOException){rejected=true;}
                Check(rejected,"Rejects device without a linear position capability");
                Check(!server.Messages.Contains("LinearCmd"),"No movement sent to unsupported device");
            }
            Console.WriteLine("PASS: "+count+" transport checks; all endpoints are local emulators, no hardware opened.");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    private static DeviceSettings FixtureSettings(){return new DeviceSettings{Transport=0,Endpoint="COM4",Argument=115200,Kind=DeviceKind.SR6};}
    private static void CheckOscillationTransport()
    {
        var capture=new PulseTransport();
        using(var hub=new DeviceHub(s=>capture,null,2))
        {
            hub.DelaySeconds=.06;
            hub.Connect(FixtureSettings());Until(()=>hub.Connected);
            var wave=new AxisOscillation(0,5,350d/999,750d/999,10);var axes=new[]{.5,.5,.5,.5,.5,.5};var enabled=new[]{true,true,true,false,true,true};
            var clock=Stopwatch.StartNew();
            while(clock.Elapsed.TotalSeconds<1.1)
            {hub.Offer(axes,enabled,DeviceKind.SR6,wave,clock.Elapsed.TotalSeconds,1);Thread.Sleep(80);}
            hub.Stop();Thread.Sleep(35);
            List<double> values;List<string> texts;
            lock(capture.Values){values=new List<double>(capture.Values);texts=new List<string>(capture.Texts);}
            int rising=0;for(int i=1;i<values.Count;i++)if(values[i-1]<.55&&values[i]>=.55)rising++;
            Check(values.Count>=55&&rising>=9&&rising<=13,"Delayed 10 Hz transport continues between sparse scene updates");
            Check(values.TrueForAll(v=>v>=350d/999&&v<=750d/999),"Oscillation respects final endpoint limits");
            Check(texts.TrueForAll(t=>t.Contains("I10")&&!t.Contains("R0")&&t.Contains("R15000")),"Waveform keeps disabled R0 absent and other axes unchanged");
            lock(capture.Values){capture.Values.Clear();capture.Texts.Clear();}
            hub.Offer(axes,enabled,DeviceKind.SR6,wave,.025,0);Thread.Sleep(120);hub.Stop();Thread.Sleep(35);
            lock(capture.Values)Check(capture.Values.Count>=3&&capture.Values.TrueForAll(v=>Math.Abs(v-wave.Value(.025))<1e-10),"Paused Timeline holds its phase in the output worker");
            Check(!hub.GetTelemetry().OutputRequested,"Stopping synchronization clears the oscillation request");
        }
    }
    private sealed class PulseTransport:ITransport
    {
        public readonly List<double> Values=new List<double>();public readonly List<string> Texts=new List<string>();
        public void Send(string text,double position){lock(Values){Values.Add(position);Texts.Add(text);}}
        public void Stop(){}public void Tick(){}public void Dispose(){}
    }
    private static void CheckConnectionLifecycle()
    {
        Check(SerialTransport.PortPath(" com4 ")==@"\\.\COM4"&&SerialTransport.PortPath("COM123")==@"\\.\COM123","Native serial supports normalized and high COM numbers");
        foreach(string value in new[]{"COM0","COM-1","COM4/file","C:\\file",""})
        {
            bool rejected=false;try{SerialTransport.PortPath(value);}catch(ArgumentException){rejected=true;}
            Check(rejected,"Rejects invalid serial endpoint: "+value);
        }
        Check(Marshal.SizeOf(typeof(SerialTransport.Dcb))==28&&Marshal.SizeOf(typeof(SerialTransport.CommTimeouts))==20,"Native serial structures match Win32 ABI");
        int attempts=0;FakeTransport recovered=new FakeTransport();
        using(DeviceHub hub=new DeviceHub(s=>{if(Interlocked.Increment(ref attempts)==1)throw new TypeLoadException("fixture runtime type missing");return recovered;},null,2))
        {
            hub.Connect(FixtureSettings());Until(()=>!hub.Connecting);
            Check(!hub.Connected&&hub.Status.Contains("fixture runtime type missing"),"Type-loading failure leaves connecting state and preserves the error");
            hub.Connect(FixtureSettings());Until(()=>hub.Connected);
            Check(!hub.Connecting,"Worker accepts a new attempt after connection failure");
        }
        Check(recovered.Disposed&&recovered.Sends==0&&recovered.Stops==0,"An unarmed connection opens and closes without device commands");
        using(ManualResetEvent entered=new ManualResetEvent(false))
        using(ManualResetEvent release=new ManualResetEvent(false))
        {
            FakeTransport stale=new FakeTransport(),next=new FakeTransport();int calls=0;
            using(DeviceHub hub=new DeviceHub(s=>{if(Interlocked.Increment(ref calls)>1)return next;entered.Set();release.WaitOne();return stale;},null,2))
            {
                try
                {
                    hub.Connect(FixtureSettings());Until(()=>entered.WaitOne(0));
                    Check(hub.Connecting&&!hub.Connected,"Pending open is reported separately from connected");
                    bool duplicate=false;try{hub.Connect(FixtureSettings());}catch(InvalidOperationException){duplicate=true;}
                    Check(duplicate&&calls==1,"Repeated connect cannot silently replace a pending attempt");
                    hub.Disconnect();Check(!hub.Connecting&&!hub.Connected&&hub.Status=="已取消连接","Cancel immediately clears the pending state");
                    hub.Connect(FixtureSettings());release.Set();Until(()=>hub.Connected&&stale.Disposed);
                    Check(stale.Sends==0&&stale.Stops==0&&stale.Ticks==0,"A cancelled late connection is disposed without commands or reuse");
                    Check(!next.Disposed&&calls==2,"Only the replacement attempt becomes connected");
                }
                finally{release.Set();}
            }
        }
        using(ManualResetEvent entered=new ManualResetEvent(false))
        using(ManualResetEvent release=new ManualResetEvent(false))
        {
            FakeTransport late=new FakeTransport();
            using(DeviceHub hub=new DeviceHub(s=>{entered.Set();release.WaitOne();return late;},null,.2))
            {
                try
                {
                    hub.Connect(FixtureSettings());Until(()=>entered.WaitOne(0));Until(()=>!hub.Connecting);
                    Check(!hub.Connected&&hub.Status.Contains("连接超时"),"Watchdog reports timeout even when the opening call is blocked");
                    release.Set();Until(()=>late.Disposed);
                    Check(!hub.Connected&&hub.Status.Contains("连接超时")&&late.Sends==0&&late.Stops==0,"Late success cannot resurrect a timed-out connection");
                }
                finally{release.Set();}
            }
        }
        using(ManualResetEvent entered=new ManualResetEvent(false))
        using(ManualResetEvent release=new ManualResetEvent(false))
        {
            FakeTransport late=new FakeTransport();DeviceHub hub=new DeviceHub(s=>{entered.Set();release.WaitOne();return late;},null,2);
            try
            {
                hub.Connect(FixtureSettings());Until(()=>entered.WaitOne(0));hub.Dispose();
                release.Set();Until(()=>late.Disposed);
                Check(!hub.Connected&&!hub.Connecting&&late.Sends==0&&late.Stops==0,"Disposal invalidates an in-flight open before it can connect");
                bool disposed=false;try{hub.Connect(FixtureSettings());}catch(ObjectDisposedException){disposed=true;}
                Check(disposed,"Disposed hub cannot queue a new connection");
            }
            finally{release.Set();hub.Dispose();}
        }
    }
    private sealed class FakeTransport:ITransport
    {
        public volatile bool Disposed;
        public int Sends,Stops,Ticks;
        public void Send(string text,double position){Interlocked.Increment(ref Sends);}
        public void Stop(){Interlocked.Increment(ref Stops);}
        public void Tick(){Interlocked.Increment(ref Ticks);}
        public void Dispose(){Disposed=true;}
    }
    private sealed class FakeIntiface:IDisposable
    {
        private readonly TcpListener listener=new TcpListener(IPAddress.Loopback,0);
        private readonly Task worker;
        private volatile bool closing;
        public readonly List<string> Messages=new List<string>();
        public double Position;
        public string Url;
        public FakeIntiface(bool linear)
        {
            listener.Start();Url="ws://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/";
            worker=Task.Run(()=>
            {
                try
                {
                    using(var client=listener.AcceptTcpClient())
                    using(var stream=client.GetStream())
                    {
                        client.ReceiveTimeout=4000;client.SendTimeout=2000;
                        Upgrade(stream);
                        while(!closing)
                        {
                            string text=ReadText(stream);
                            if(text==null)break;
                            foreach(JsonData item in JsonMapper.ToObject(text))foreach(DictionaryEntry property in (IDictionary)item)
                            {
                                string name=(string)property.Key;JsonData body=(JsonData)property.Value;int id=(int)body["Id"];Messages.Add(name);
                                string response;
                                if(name=="RequestServerInfo")response="[{\"ServerInfo\":{\"Id\":"+id+",\"ServerName\":\"Local Test\",\"MessageVersion\":3,\"MaxPingTime\":200}}]";
                                else if(name=="RequestDeviceList")response="[{\"DeviceList\":{\"Id\":"+id+",\"Devices\":[{\"DeviceIndex\":7,\"DeviceName\":\"Fake Linear\",\"DeviceMessages\":{"+(linear?"\"LinearCmd\":{\"FeatureCount\":1}":"\"ScalarCmd\":[{\"ActuatorType\":\"Vibrate\"}]")+"}}]}}]";
                                else{if(name=="LinearCmd")Position=(double)body["Vectors"][0]["Position"];response="[{\"Ok\":{\"Id\":"+id+"}}]";}
                                WriteText(stream,response);
                            }
                        }
                    }
                }
                catch(SocketException){if(!closing)throw;}
            });
        }

        // The fixture only needs the WebSocket upgrade and single-frame JSON
        // messages exchanged by the production client on this loopback port.
        private static void Upgrade(Stream stream)
        {
            var header=new StringBuilder();
            while(header.Length<4096)
            {
                int value=stream.ReadByte();
                if(value<0)throw new EndOfStreamException();
                header.Append((char)value);
                int n=header.Length;
                if(n>=4&&header[n-4]=='\r'&&header[n-3]=='\n'&&header[n-2]=='\r'&&header[n-1]=='\n')break;
            }
            const string keyHeader="Sec-WebSocket-Key:";
            foreach(string line in header.ToString().Split('\n'))
            {
                if(!line.StartsWith(keyHeader,StringComparison.OrdinalIgnoreCase))continue;
                string key=line.Substring(keyHeader.Length).Trim()+"258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
                using(var sha=SHA1.Create())
                {
                    string accept=Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key)));
                    byte[] response=Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: "+accept+"\r\n\r\n");
                    stream.Write(response,0,response.Length);
                }
                return;
            }
            throw new IOException("Missing WebSocket key");
        }
        private static string ReadText(Stream stream)
        {
            int opcode=stream.ReadByte();
            if(opcode<0)return null;
            var size=ReadBytes(stream,1)[0];
            if((opcode&0x0f)==8)return null;
            if(opcode!=0x81||(size&0x80)==0)throw new IOException("Expected a masked WebSocket text frame");
            int length=size&0x7f;
            if(length==126){byte[] extended=ReadBytes(stream,2);length=extended[0]*256+extended[1];}
            else if(length==127)throw new IOException("Unexpected extended WebSocket message");
            byte[] mask=ReadBytes(stream,4),payload=ReadBytes(stream,length);
            for(int i=0;i<payload.Length;i++)payload[i]^=mask[i%4];
            return Encoding.UTF8.GetString(payload);
        }
        private static byte[] ReadBytes(Stream stream,int size)
        {
            byte[] bytes=new byte[size];
            for(int offset=0;offset<size;)
            {
                int count=stream.Read(bytes,offset,size-offset);
                if(count==0)throw new EndOfStreamException();
                offset+=count;
            }
            return bytes;
        }
        private static void WriteText(Stream stream,string text)
        {
            byte[] payload=Encoding.UTF8.GetBytes(text);
            stream.WriteByte(0x81);
            if(payload.Length<126)stream.WriteByte((byte)payload.Length);
            else{stream.WriteByte(126);stream.WriteByte((byte)(payload.Length>>8));stream.WriteByte((byte)payload.Length);}
            stream.Write(payload,0,payload.Length);
        }
        public void Dispose()
        {
            closing=true;listener.Stop();
            if(!worker.Wait(4500))throw new TimeoutException("Loopback Intiface server did not close");
        }
    }
}
