using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using KoiSync.Core;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        private sealed class PairActor
        {
            internal ChaControl Actor;
            internal Component Bp;
            internal Transform Root;
            internal Transform[] Chain;
            internal MotionVector[] Path;
            internal Transform NativeRoot;
            internal Transform[] NativeChain;
            internal bool HasBpChain;
            internal StudioShaftModel Model;
            internal List<StudioShaftModel> Models;
            internal readonly List<PairOpening> Openings=new List<PairOpening>();
            internal readonly List<StudioHandSource> Hands=new List<StudioHandSource>();
            internal readonly Dictionary<string,Transform> Bones=new Dictionary<string,Transform>();
        }
        private sealed class PairOpening {internal Transform Node;internal OpeningKind Kind;internal StudioContactPoint Contact;internal Vector3 Position{get{return Contact.Position;}}}
        private sealed class PairConstraint {internal object Handle;internal PairActor Source,Target;internal Transform Entry,Child;}
        private readonly List<PairActor> pairActors=new List<PairActor>();
        private readonly List<PairConstraint> pairConstraints=new List<PairConstraint>();
        private readonly Dictionary<Transform,PairActor> pairSourceNodes=new Dictionary<Transform,PairActor>();
        private Type pairConstraintType;
        private bool autoCameraPairs;
        private float nextPairCheck;
        private double lastPairTime=-1;
        private Chapter cameraChoice;
        private readonly ChapterSelection chapterSelection=new ChapterSelection();
        private bool ActorVisible(ChaControl actor)
        {
            if(actor==null||!actor.gameObject.activeInHierarchy)return false;
            object visible=RuntimeAccess.Read(actor,"visibleAll");if(visible is bool&&!(bool)visible)return false;
            int key=Key(actor);global::Studio.ObjectCtrlInfo info;
            if(key>=0&&global::Studio.Studio.Instance.dicObjectCtrl.TryGetValue(key,out info))
            {
                object node=info.treeNodeObject;
                while(node!=null){visible=RuntimeAccess.Read(node,"visible");if(visible is bool&&!(bool)visible)return false;node=RuntimeAccess.Read(node,"parent");}
            }
            return true;
        }
        private void ResetPairCandidates(){chapterSelection.Reset();pairActors.Clear();pairConstraints.Clear();pairSourceNodes.Clear();pairConstraintType=null;KoiSync.KK.KkMouthAnchor.Clear();cameraChoice=null;nextPairCheck=0;lastPairTime=-1;}
        private void CachePairCandidates()
        {
            ResetPairCandidates();
            foreach(var actor in actors)
            {
                var entry=new PairActor{Actor=actor,Bp=StudioBinding.FindBp(actor)};
                foreach(var bone in actor.GetComponentsInChildren<Transform>(true))if(!entry.Bones.ContainsKey(bone.name))entry.Bones[bone.name]=bone;
                entry.Bones.TryGetValue("cm_J_dan100_00",out entry.NativeRoot);
                if(entry.NativeRoot==null)entry.Bones.TryGetValue("cm_J_dan101_00",out entry.NativeRoot);
                entry.NativeChain=StudioBinding.ReadBpChain(entry.Bp);
                entry.HasBpChain=entry.NativeChain!=null&&entry.NativeChain.Length>=2;
                if(!entry.HasBpChain)entry.NativeChain=ReadNativeBoneChain(entry);
                entry.Models=StudioShaftModel.DiscoverForActor(actor,Key(actor),actors);RefreshShaft(entry);
                if(actor.sex==0)foreach(var kind in new[]{OpeningKind.LeftHandVaginal,OpeningKind.RightHandVaginal})
                {var hand=new StudioHandSource(entry.Bones,kind);if(hand.Alive)entry.Hands.Add(hand);}
                if(actor.sex==1)AddPairOpening(entry,OpeningKind.Vaginal,"cf_J_Vagina_root","cf_j_vagina_root","cf_j_kokan");
                AddPairOpening(entry,OpeningKind.Anal,"cf_j_ana","cf_J_Ana_Root");
                AddPairOpening(entry,OpeningKind.Oral,"cf_J_Mouthup","a_n_mouth","cf_J_MouthLow","cf_J_MouthCavity");
                AddPairOpening(entry,OpeningKind.LeftHand,"a_n_hand_L","cf_j_hand_L");
                AddPairOpening(entry,OpeningKind.RightHand,"a_n_hand_R","cf_j_hand_R");pairActors.Add(entry);
                if(actor.sex==1)AddPairOpening(entry,OpeningKind.Breast,"cf_j_bust03_L","cf_j_bust02_L");
            }
            foreach(var actor in pairActors)
            {
                Transform native;if(actor.Bones.TryGetValue("k_f_dan_entry",out native))pairSourceNodes[native]=actor;
                foreach(var model in actor.Models)foreach(var bone in model.Chain)pairSourceNodes[bone]=actor;
            }
            pairConstraintType=RuntimeAccess.Find("NodesConstraints.NodesConstraints");
            RefreshPairConstraints();
        }
        private void RefreshPairConstraints()
        {
            // NodesConstraints restores its table after the actor-load callback.
            // Read the small live table at the existing 10 Hz grouping cadence;
            // never rescan skeletons or Timeline keys. Also handles edits whose
            // list count is unchanged (retarget/replacement/enable switches).
            pairConstraints.Clear();
            var list=RuntimeAccess.Read(RuntimeAccess.Read(pairConstraintType,"_self"),"_constraints") as IEnumerable;
            if(list==null)return;
            foreach(var handle in list)
            {
                var child=RuntimeAccess.Read(handle,"childTransform") as Transform;PairActor source;
                if(child==null||!pairSourceNodes.TryGetValue(child,out source))continue;
                var parent=RuntimeAccess.Read(handle,"parentTransform") as Transform;if(parent==null)continue;
                var owner=parent.GetComponentInParent<ChaControl>();var target=pairActors.Find(p=>p.Actor==owner);
                if(target!=null&&source!=target)pairConstraints.Add(new PairConstraint{Handle=handle,Source=source,Target=target,Entry=parent,Child=child});
            }
        }
        private static Transform[] ReadNativeBoneChain(PairActor actor)
        {
            if(actor.Bones==null||actor.Bones.Count==0)return null;
            var native=new List<Transform>();
            Transform root=actor.NativeRoot;
            if(root==null)actor.Bones.TryGetValue("cm_J_dan100_00",out root);
            if(root==null)actor.Bones.TryGetValue("cm_J_dan101_00",out root);
            if(root!=null)native.Add(root);
            for(int i=101;i<=109;i++)
            {
                Transform node;
                if(actor.Bones.TryGetValue("cm_J_dan"+i+"_00",out node)&&node!=null&&!native.Contains(node))native.Add(node);
            }
            return native.Count>=2?native.ToArray():null;
        }
        private static void RefreshShaft(PairActor actor)
        {
            actor.Model=actor.Models.Find(m=>m.Visible);
            if(!actor.HasBpChain)
            {
                var bpChain=StudioBinding.ReadBpChain(actor.Bp);
                if(bpChain!=null&&bpChain.Length>=2){actor.NativeChain=bpChain;actor.HasBpChain=true;}
                else if(actor.NativeChain==null||actor.NativeChain.Length<2)actor.NativeChain=ReadNativeBoneChain(actor);
            }
            if(actor.NativeRoot==null&&actor.NativeChain!=null&&actor.NativeChain.Length>0)actor.NativeRoot=actor.NativeChain[0];
            actor.Root=actor.Model==null?actor.NativeRoot:actor.Model.Root;
            actor.Chain=actor.Model==null?actor.NativeChain:actor.Model.Chain;
        }
        private StudioShaftModel VisibleShaft(ChaControl source)
        {
            var actor=pairActors.Find(a=>a.Actor==source);
            if(actor==null&&source!=null)
            {
                // Manual/saved bindings need only their selected source. Do not
                // discover every actor's contacts while analysis is disabled.
                actor=new PairActor{Actor=source,Bp=StudioBinding.FindBp(source),Models=StudioShaftModel.DiscoverForActor(source,Key(source),actors)};
                pairActors.Add(actor);
            }
            if(actor==null)return null;RefreshShaft(actor);return actor.Model;
        }
        private static bool ActiveConstraint(PairActor actor,Transform child)
        {return child!=null&&(actor.Model==null?child.name=="k_f_dan_entry":actor.Model.Contains(child));}
        private static void UpdatePairPath(PairActor actor)
        {
            if(actor.Chain==null||actor.Chain.Length<2){actor.Path=null;return;}
            if(actor.Path==null||actor.Path.Length!=actor.Chain.Length)actor.Path=new MotionVector[actor.Chain.Length];
            for(int i=0;i<actor.Path.Length;i++)
            {if(actor.Chain[i]==null){actor.Path=null;return;}actor.Path[i]=StudioBinding.V(actor.Chain[i].position);}
        }
        private static void AddPairOpening(PairActor actor,OpeningKind kind,params string[] names)
        {foreach(var name in names){Transform node;if(actor.Bones.TryGetValue(name,out node)){actor.Openings.Add(new PairOpening{Node=node,Kind=kind,Contact=new StudioContactPoint(actor.Bones,kind,node)});return;}}}
        private Chapter CameraChapter()
        {
            if(!autoCameraPairs)return null;
            bool seek=lastPairTime>=0&&(Clock.Time<lastPairTime-.01||Clock.Time-lastPairTime>1);
            lastPairTime=Clock.Time;
            bool lost=cameraChoice!=null&&(!ActorVisible(Actor(cameraChoice.Source))||!ActorVisible(Actor(cameraChoice.Target)));
            if(!seek&&!lost&&Time.realtimeSinceStartup<nextPairCheck)return cameraChoice;
            nextPairCheck=Time.realtimeSinceStartup+.1f;
            var choices=new Dictionary<string,Chapter>();var points=new Dictionary<string,Vector3>();var evidence=new Dictionary<string,string>();
            var distances=new Dictionary<string,double>();var attachedSources=new HashSet<int>();var authoritative=new HashSet<string>();
            // BP can create its chain after the scene-load callback. Resolve that
            // small per-actor reference lazily instead of keeping an empty cache.
            foreach(var actor in pairActors)
            {
                if(actor.Actor==null)continue;
                if(actor.Bp==null)actor.Bp=StudioBinding.FindBp(actor.Actor);
                RefreshShaft(actor);UpdatePairPath(actor);
            }
            foreach(var source in pairActors)
            {
                if(source.Root==null||source.Model!=null||!ActorVisible(source.Actor))continue;
                var actual=RuntimeAccess.Read(RuntimeAccess.Read(source.Bp,"collisionAgent"),"m_collisionCharacter") as ChaControl;
                if(actual==null||actual==source.Actor||!RuntimeAccess.Flag(source.Bp,"danTargetsValid"))continue;
                var target=pairActors.Find(p=>p.Actor==actual);if(target==null||!ActorVisible(actual))continue;
                string node=Convert.ToString(RuntimeAccess.Read(source.Bp,"danEntryParentName"));Transform entry;
                var kind=StudioBinding.Infer(node,OpeningKind.Vaginal);
                var contact=target.Openings.Find(o=>o.Kind==kind&&o.Contact.Alive);
                // BP retains its last parent and validity flag after a Timeline
                // attachment is disabled. A distant cached target must not hide
                // the current geometric contact. Enabled constraints below still
                // take priority, including deliberately offset entry proxies.
                if(source.Path==null||contact==null||!CameraPairRanking.AttachmentMatchesContact(
                    StudioShaftMotion.ClosestDistance(source.Path,StudioBinding.V(contact.Position)),StudioShaftMotion.PathLength(source.Path)))continue;
                if(!target.Bones.TryGetValue(node,out entry)&&!target.Bones.TryGetValue("cf_j_hips",out entry))entry=actual.transform;
                var chapter=MakeChapter(source.Actor,actual,kind);
                choices[chapter.Identity]=chapter;points[chapter.Identity]=(source.Root.position+entry.position)*.5f;
                attachedSources.Add(chapter.Source);authoritative.Add(chapter.Identity);
                evidence[chapter.Identity]=L("可见角色 · BP 吸附","Visible · BP attachment");
            }
            RefreshPairConstraints();
            var explicitPairs=new HashSet<string>();
            foreach(var c in pairConstraints)
            {
                if(c.Entry==null||c.Source.Root==null||!ActiveConstraint(c.Source,c.Child)||!ActorVisible(c.Source.Actor)||!ActorVisible(c.Target.Actor)||!RuntimeAccess.Flag(c.Handle,"enabled")||!RuntimeAccess.Flag(c.Handle,"position"))continue;
                var chapter=MakeChapter(c.Source.Actor,c.Target.Actor,StudioBinding.Infer(StudioBinding.AnatomicalName(c.Entry,c.Target.Actor.transform),OpeningKind.Vaginal));
                // Match StudioBinding.FindExplicitEntry: the first enabled
                // constraint for this pair wins, overriding its BP fallback.
                // Multiple constrained shaft bones must not create rival kinds.
                if(!explicitPairs.Add(chapter.Source+":"+chapter.Target))continue;
                string previous=null;foreach(var choice in choices.Values)if(choice.Source==chapter.Source&&choice.Target==chapter.Target){previous=choice.Identity;break;}
                if(previous!=null)choices.Remove(previous);
                choices[chapter.Identity]=chapter;points[chapter.Identity]=(c.Source.Root.position+c.Entry.position)*.5f;
                attachedSources.Add(chapter.Source);authoritative.Add(chapter.Identity);
                evidence[chapter.Identity]=L("可见角色 · 显式吸附","Visible · explicit attachment");
            }
            // Older cards may animate bones directly without BP collision targets
            // or attachment tracks. Qualify a pair by the visible finite chain;
            // camera ranking alone must never pair unrelated distant characters.
            foreach(var source in pairActors)
            {
                if(source.Root==null||source.Path==null||!ActorVisible(source.Actor))continue;
                bool known=false;foreach(var choice in choices.Values)if(choice.Source==Key(source.Actor)){known=true;break;}if(known)continue;
                var path=source.Path;
                double length=StudioShaftMotion.PathLength(path),limit=CameraPairRanking.ContactLimit(length),nearest=limit;PairActor target=null;PairOpening opening=null;bool selectedCavity=false;
                foreach(var candidate in pairActors)
                {
                    if(candidate==source||!ActorVisible(candidate.Actor))continue;
                    foreach(var entry in candidate.Openings)
                    {
                        if(!entry.Contact.Alive)continue;double distance=StudioShaftMotion.ClosestDistance(path,StudioBinding.V(entry.Position));
                        bool currentEntry=Binding!=null&&!Binding.IsHandSource&&Binding.Source==source.Actor&&Binding.Target==candidate.Actor&&Binding.Kind==entry.Kind;
                        bool cavity=false;
                        if((int)entry.Kind<=2)
                        {
                            MotionVector point,right,inward;entry.Contact.Pose(path,out point,out right,out inward);
                            double insertion=StudioShaftMotion.InsertionFraction(path,point,inward);
                            if(!currentEntry&&!CameraPairRanking.AcquireCavity(distance,length,insertion))continue;
                            cavity=CameraPairRanking.PreferCavityToGrip(distance,length,insertion);
                        }
                        // A supporting grip may be closer to the centre line
                        // throughout an oral stroke. Confirmed internal contact
                        // owns the interaction; proximity alone must not steal it.
                        if(distance<limit&&CameraPairRanking.PreferContact((int)entry.Kind,cavity,distance,opening==null?-1:(int)opening.Kind,selectedCavity,nearest))
                        {nearest=distance;target=candidate;opening=entry;selectedCavity=cavity;}
                    }
                }
                if(target!=null)
                {
                    if(Binding!=null&&!Binding.IsHandSource&&Binding.Source==source.Actor&&Binding.Target==target.Actor)
                    {
                        var currentOpening=target.Openings.Find(o=>o.Kind==Binding.Kind&&o.Node!=null);
                        if(currentOpening!=null)
                        {
                            double currentDistance=StudioShaftMotion.ClosestDistance(path,StudioBinding.V(currentOpening.Position));
                            bool inside=false;
                            if((int)currentOpening.Kind<=1&&(int)opening.Kind<=1)
                            {
                                MotionVector point,right,inward;currentOpening.Contact.Pose(path,out point,out right,out inward);
                                inside=CameraPairRanking.KeepCavity(currentDistance,nearest,StudioShaftMotion.PathLength(path),StudioShaftMotion.InsertionFraction(path,point,inward));
                            }
                            if((int)currentOpening.Kind<=2&&(int)opening.Kind>=3)
                            {
                                MotionVector point,right,inward;currentOpening.Contact.Pose(path,out point,out right,out inward);
                                inside=CameraPairRanking.KeepCavityOverGrip(currentDistance,length,StudioShaftMotion.InsertionFraction(path,point,inward));
                            }
                            if(inside||!(selectedCavity&&(int)currentOpening.Kind>=3)&&CameraPairRanking.KeepContact(currentDistance,nearest,limit))opening=currentOpening;
                        }
                    }
                    var chapter=MakeChapter(source.Actor,target.Actor,opening.Kind);choices[chapter.Identity]=chapter;
                    distances[chapter.Identity]=StudioShaftMotion.ClosestDistance(path,StudioBinding.V(opening.Position));
                    points[chapter.Identity]=(source.Root.position+opening.Position)*.5f;
                    evidence[chapter.Identity]=L("可见角色 · 接触位置","Visible · contact position");
                }

            }
            foreach(var source in pairActors)
            {
                if(source.Hands.Count==0||!ActorVisible(source.Actor)||attachedSources.Contains(Key(source.Actor)))continue;
                foreach(var target in pairActors)
                {
                    if(target==source||!ActorVisible(target.Actor))continue;
                    var entry=target.Openings.Find(o=>o.Kind==OpeningKind.Vaginal&&o.Contact.Alive);if(entry==null)continue;
                    StudioHandSource bestHand=null,currentHand=null;double nearest=.018,currentDistance=double.MaxValue;
                    foreach(var hand in source.Hands)
                    {
                        if(!hand.Alive)continue;double distance;hand.Closest(entry.Position,out distance);
                        if(Binding!=null&&Binding.IsHandSource&&Binding.Source==source.Actor&&Binding.Target==target.Actor&&Binding.Kind==hand.Kind){currentHand=hand;currentDistance=distance;}
                        if(distance<nearest){nearest=distance;bestHand=hand;}
                    }
                    if(currentHand!=null&&CameraPairRanking.KeepContact(currentDistance,nearest,.024)){bestHand=currentHand;nearest=currentDistance;}
                    if(bestHand==null)continue;
                    string competing=null;
                    foreach(var choice in choices.Values)if(choice.Source==Key(source.Actor)&&choice.Target==Key(target.Actor)){competing=choice.Identity;break;}
                    if(competing!=null)
                    {
                        double other;if(!distances.TryGetValue(competing,out other))continue;
                        if(choices[competing].Opening<=2&&other<=.020)continue;
                        // A close finger contact may replace a loose incidental
                        // shaft/hand proximity, not an authoritative attachment.
                        double margin=currentHand==bestHand?-.005:.005;
                        if(nearest+margin>=other)continue;
                        choices.Remove(competing);
                    }
                    var chapter=MakeChapter(source.Actor,target.Actor,bestHand.Kind);choices[chapter.Identity]=chapter;
                    points[chapter.Identity]=entry.Position;distances[chapter.Identity]=nearest;
                    evidence[chapter.Identity]=L("可见角色 · 来源手指接触","Visible · source finger contact");
                }
            }
            Chapter best=null;double bestScore=-2,currentScore=-2;
            string current=Binding==null?"":MakeChapter(Binding.Source,Binding.Target,Binding.Kind).Identity;
            var camera=Camera.main;
            foreach(var choice in choices.Values)
            {
                var v=camera==null?new Vector3(.5f,.5f,1):camera.WorldToViewportPoint(points[choice.Identity]);
                double score=CameraPairRanking.Score(v.x,v.y,v.z,camera==null?1:camera.aspect);
                if(choice.Identity==current)currentScore=score;
                if(score>bestScore){best=choice;bestScore=score;}
            }
            if(best==null)
            {
                cameraEvidence=L("尚未找到有效接触组","No contact group found");
                // Contact can leave the finite chain during a stroke. Retain
                // the visible pair; its solver already returns neutral motion.
                if(!lost&&cameraChoice!=null)best=cameraChoice;
                else{cameraChoice=null;chapterSelection.Reset();return null;}
            }
            else cameraEvidence=choices.Count>1?L("多组互动 · 画面中心优先","Multiple groups · view centre"):evidence[best.Identity];
            if(choices.Count>1&&currentScore>=0&&bestScore<currentScore+.15&&choices.ContainsKey(current))best=choices[current];
            bool immediate=lost||authoritative.Contains(best.Identity)&&(cameraChoice==null||best.Source==cameraChoice.Source&&best.Target==cameraChoice.Target);
            string accepted=chapterSelection.Choose(best.Identity,Clock.Time,Time.realtimeSinceStartup,Clock.Playing,Clock.Speed*Time.timeScale,immediate);
            if(accepted==best.Identity)cameraChoice=best;
            return cameraChoice;
        }
        private void RestoreChapterActors()
        {
            foreach(var chapter in Book.Chapters)
            {
                chapter.Source=RestoreActorKey(chapter.Source,chapter.SourceName);
                chapter.Target=RestoreActorKey(chapter.Target,chapter.TargetName);
            }
        }
        private int RestoreActorKey(int key,string name)
        {
            if(key<0)return key;
            var actor=Actor(key);if(actor!=null&&(string.IsNullOrEmpty(name)||StudioBinding.Name(actor)==name))return key;
            var matches=actors.FindAll(a=>StudioBinding.Name(a)==name);
            return matches.Count==1?Key(matches[0]):-1;
        }
    }
}
