using System;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    public sealed partial class KoiSyncPanel
    {
        private ConfigEntry<bool> autoConnect;
        private ConfigEntry<int> savedTransport,savedDevice;
        private Toggle autoConnectToggle;
        private bool autoPaused;
        private double nextPortScan,nextAutoAttempt;
        private string[] detectedPorts=new string[0];
        private string scanStatus="";
        internal Func<string[]> PortScanner=SerialTransport.GetPortNames;
        private void InitializeConnections()
        {
            autoConnect=host.Config.Bind("Device","AutoConnect",true,"Scan for the saved endpoint and connect automatically. Manual disconnect suspends retries until rescan or restart.");
            savedTransport=host.Config.Bind("Device","Transport",0,new ConfigDescription("Last selected transport",new AcceptableValueRange<int>(0,2)));
            int legacy=host.Config.Bind("Device","Kind",0,"Legacy device kind; protocol selection now uses Protocol").Value;
            savedDevice=host.Config.Bind("Device","Protocol",legacy==3||savedTransport.Value==2?1:0,new ConfigDescription("0 = TCode; 1 = Intiface Central",new AcceptableValueRange<int>(0,1)));
            device.value=savedDevice.Value;transport.value=device.value==1?2:savedTransport.Value==2?0:savedTransport.Value;previousTransport=transport.value;
            endpoint.text=addresses[previousTransport];argument.text=arguments[previousTransport];
            endpointCaption.text=previousTransport==0?"串口名称 / 波特率":previousTransport==1?"设备 IP / UDP 端口":"本机 WebSocket / 设备索引";
            autoConnectToggle.isOn=autoConnect.Value;
        }
        private void ResumeAutoConnection()
        {autoPaused=false;nextAutoAttempt=nextPortScan=0;connectionError="";}
        internal void UpdateAutoConnection(double now)
        {
            if(autoConnect==null||host.Demo)return;
            if(now>=nextPortScan)
            {
                nextPortScan=now+2;
                try{detectedPorts=PortScanner();scanStatus="串口："+(detectedPorts.Length==0?"未发现":string.Join(", ",detectedPorts));}
                catch(Exception ex){detectedPorts=new string[0];scanStatus=ex.Message;}
            }
            if(!autoConnect.Value||autoPaused||host.Devices.Connected||host.Devices.Connecting||now<nextAutoAttempt||endpoint.isFocused||argument.isFocused)return;
            if(transport.value==0)
            {
                string requested=endpoint.text.Trim();
                if(requested.Length==0&&detectedPorts.Length==1){endpoint.text=detectedPorts[0];requested=endpoint.text;}
                if(Array.FindIndex(detectedPorts,p=>p.Equals(requested,StringComparison.OrdinalIgnoreCase))<0)return;
            }
            nextAutoAttempt=now+10;Connect(true);
        }
    }
}
