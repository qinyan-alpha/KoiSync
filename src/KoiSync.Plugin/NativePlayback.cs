using System;
using System.Collections.Generic;
using UnityEngine;

namespace KoiSync.Plugin
{
    internal sealed class NativePlayback
    {
        private sealed class Held{internal Animator Animator;internal float Speed;internal RuntimeAnimatorController Controller;}
        private readonly List<Held> actors=new List<Held>();
        private Animator clock;
        private double duration;
        internal string Key="",Label="";
        internal int SeekRevision{get;private set;}
        internal bool Paused{get;private set;}
        internal bool Available{get{return clock!=null&&clock.runtimeAnimatorController!=null&&Duration>.0001;}}
        internal double Duration
        {
            get
            {
                if(clock==null)return 0;
                if(!Paused)
                {
                    double length=clock.GetCurrentAnimatorStateInfo(0).length;
                    if(KoiSync.Core.Numbers.Finite(length)&&length>.0001)duration=length;
                    else
                    {
                        double total=0,weight=0;foreach(var clip in clock.GetCurrentAnimatorClipInfo(0))if(clip.clip!=null){total+=clip.clip.length*clip.weight;weight+=clip.weight;}
                        if(weight>0)duration=total/weight;
                    }
                }
                return duration;
            }
        }
        internal double Time{get{if(clock==null)return 0;var s=clock.GetCurrentAnimatorStateInfo(0);double n=s.normalizedTime;return (s.loop?n-Math.Floor(n):Math.Max(0,Math.Min(1,n)))*Duration;}}
        internal void Bind(string key,string label,Animator target,IEnumerable<Animator> participants)
        {
            if(Key==key&&clock==target){Hold();return;}
            Release();Key=key;Label=label;clock=target;
            foreach(var a in participants)if(a!=null&&a.runtimeAnimatorController!=null&&!actors.Exists(h=>h.Animator==a))actors.Add(new Held{Animator=a,Speed=a.speed,Controller=a.runtimeAnimatorController});
        }
        internal void Toggle(){SetPlaying(Paused);}
        internal void SetPlaying(bool play)
        {
            if(!Available)throw new InvalidOperationException("当前本体动画尚不可控制");
            if(play){foreach(var h in actors)if(h.Animator!=null&&h.Animator.runtimeAnimatorController==h.Controller)h.Animator.speed=h.Speed;Paused=false;}
            else if(!Paused){duration=Duration;foreach(var h in actors)if(h.Animator!=null){h.Speed=h.Animator.speed;h.Animator.speed=0;}Paused=true;}
        }
        internal void Hold(){if(Paused)foreach(var h in actors)if(h.Animator!=null&&h.Animator.runtimeAnimatorController==h.Controller)h.Animator.speed=0;}
        internal void Seek(double seconds)
        {
            if(!Available)throw new InvalidOperationException("当前本体动画尚不可定位");
            float fraction=(float)Math.Max(0,Math.Min(.999999,seconds/Duration));
            SeekRevision++;
            foreach(var h in actors)
            {
                var a=h.Animator;if(a==null||a.runtimeAnimatorController!=h.Controller)continue;
                for(int layer=0;layer<a.layerCount;layer++){var state=a.GetCurrentAnimatorStateInfo(layer);if(state.fullPathHash!=0)a.Play(state.fullPathHash,layer,fraction);}
                a.Update(0);
            }
            Hold();
        }
        internal void Release()
        {
            if(Paused)foreach(var h in actors)if(h.Animator!=null&&h.Animator.runtimeAnimatorController==h.Controller)h.Animator.speed=h.Speed;
            Paused=false;actors.Clear();clock=null;duration=0;Key=Label="";
        }
    }
}
