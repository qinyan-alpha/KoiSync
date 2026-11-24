using System;
using System.Collections;
using System.Collections.Generic;
using KoiSync.Core;
using UnityEngine;
using GameStudio = global::Studio.Studio;
using OCI = global::Studio.ObjectCtrlInfo;
using OCIItem = global::Studio.OCIItem;

namespace KoiSync.Studio
{
    // Alternate Studio props (either attached inside a character hierarchy OR placed as
    // Studio workspace OCIItems such as penis7 / Human01 under a folder) can supply the
    // visible moving shaft while the native BP mesh is hidden or static.
    internal sealed class StudioShaftModel
    {
        internal readonly SkinnedMeshRenderer Renderer;
        internal readonly Transform[] Chain;
        internal readonly bool ExternalItem;
        internal readonly int ItemKey;
        internal readonly string ItemName;
        private readonly object itemTreeNode;
        private readonly Vector3[] restSegments;

        internal Transform Root { get { return Chain[0]; } }
        internal string DisplayName { get { return !string.IsNullOrEmpty(ItemName) ? ItemName : (Renderer != null ? Renderer.name : "Shaft"); } }

        internal bool Alive
        {
            get
            {
                if (Renderer == null || Chain == null || Chain.Length < 2) return false;
                for (int i = 0; i < Chain.Length; i++) if (Chain[i] == null) return false;
                return true;
            }
        }

        internal bool Visible
        {
            get
            {
                if (!Alive || !Renderer.enabled || !Renderer.gameObject.activeInHierarchy) return false;
                object node = itemTreeNode;
                while (node != null)
                {
                    object vis = RuntimeAccess.Read(node, "visible");
                    if (vis is bool && !(bool)vis) return false;
                    node = RuntimeAccess.Read(node, "parent");
                }
                return true;
            }
        }

        private StudioShaftModel(SkinnedMeshRenderer renderer, Transform[] chain, Vector3[] segments, bool externalItem, int itemKey, string itemName, object treeNode)
        {
            Renderer = renderer;
            Chain = chain;
            restSegments = segments;
            ExternalItem = externalItem;
            ItemKey = itemKey;
            ItemName = itemName ?? "";
            itemTreeNode = treeNode;
        }

        internal bool Contains(Transform node)
        {
            return node != null && Array.IndexOf(Chain, node) >= 0;
        }

        internal double BaseLength
        {
            get
            {
                double result = 0;
                if (Renderer == null) return 0;
                foreach (var segment in restSegments)
                    result += Renderer.transform.TransformVector(segment).magnitude;
                return result;
            }
        }

        internal static List<StudioShaftModel> Discover(ChaControl actor)
        {
            return DiscoverForActor(actor, -1, null);
        }

        internal static List<StudioShaftModel> DiscoverForActor(ChaControl actor, int actorKey, List<ChaControl> allActors)
        {
            var result = new List<StudioShaftModel>();
            if (actor == null) return result;
            var seen = new HashSet<SkinnedMeshRenderer>();

            // 1. Direct children of the actor's Unity transform
            foreach (var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer == null || !seen.Add(renderer)) continue;
                var model = Read(renderer, false, -1, renderer.name, null);
                if (model != null) result.Add(model);
            }

            // 2. Scene-level Studio OCIItem props (e.g. penis7 / Human01_reference under PENIS EDIT folder)
            var studio = GameStudio.Instance;
            if (studio == null || studio.dicObjectCtrl == null) return result;

            object actorNode = null;
            OCI actorOci;
            if (actorKey >= 0 && studio.dicObjectCtrl.TryGetValue(actorKey, out actorOci) && actorOci != null)
                actorNode = actorOci.treeNodeObject;

            int visibleMales = 0;
            if (allActors != null)
            {
                for (int i = 0; i < allActors.Count; i++)
                    if (allActors[i] != null && allActors[i].sex == 0 && allActors[i].gameObject.activeInHierarchy) visibleMales++;
            }
            bool canClaimStandalone = actor.sex == 0 && (visibleMales <= 1);

            foreach (var kv in studio.dicObjectCtrl)
            {
                var item = kv.Value as OCIItem;
                if (item == null || item.objectItem == null) continue;

                // Determine whether this Studio item belongs to `actor`
                int ownerKey = ResolveOwnerActorKey(item, studio.dicObjectCtrl);
                bool matchesActor = (actorKey >= 0 && ownerKey == actorKey)
                    || (actorNode != null && IsTreeDescendantOf(item.treeNodeObject, actorNode))
                    || (ownerKey < 0 && canClaimStandalone);
                if (!matchesActor) continue;

                string itemLabel = item.treeNodeObject != null && !string.IsNullOrEmpty(item.treeNodeObject.textName)
                    ? item.treeNodeObject.textName
                    : item.objectItem.name;

                foreach (var renderer in item.objectItem.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer == null || !seen.Add(renderer)) continue;
                    var model = Read(renderer, true, kv.Key, itemLabel, item.treeNodeObject);
                    if (model != null) result.Add(model);
                }
            }

            return result;
        }

        // Diagnostic helper: list every recognized shaft model across the whole Studio scene.
        internal static List<StudioShaftModel> DiscoverAllSceneItems()
        {
            var result = new List<StudioShaftModel>();
            var studio = GameStudio.Instance;
            if (studio == null || studio.dicObjectCtrl == null) return result;
            var seen = new HashSet<SkinnedMeshRenderer>();
            foreach (var kv in studio.dicObjectCtrl)
            {
                var item = kv.Value as OCIItem;
                if (item == null || item.objectItem == null) continue;
                string itemLabel = item.treeNodeObject != null && !string.IsNullOrEmpty(item.treeNodeObject.textName)
                    ? item.treeNodeObject.textName
                    : item.objectItem.name;
                foreach (var renderer in item.objectItem.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer == null || !seen.Add(renderer)) continue;
                    var model = Read(renderer, true, kv.Key, itemLabel, item.treeNodeObject);
                    if (model != null) result.Add(model);
                }
            }
            return result;
        }

        private static bool IsTreeDescendantOf(object node, object ancestor)
        {
            if (node == null || ancestor == null) return false;
            object cur = RuntimeAccess.Read(node, "parent");
            int guard = 0;
            while (cur != null && guard++ < 32)
            {
                if (ReferenceEquals(cur, ancestor)) return true;
                cur = RuntimeAccess.Read(cur, "parent");
            }
            return false;
        }

        private static int ResolveOwnerActorKey(OCIItem item, Dictionary<int, OCI> dic)
        {
            if (item == null || item.treeNodeObject == null || dic == null) return -1;
            // 1. Walk Studio TreeNode parent chain to see if nested under a character (directly or via Folder)
            object cur = RuntimeAccess.Read(item.treeNodeObject, "parent");
            int guard = 0;
            while (cur != null && guard++ < 32)
            {
                foreach (var kv in dic)
                {
                    var ch = kv.Value as global::Studio.OCIChar;
                    if (ch != null && ReferenceEquals(ch.treeNodeObject, cur)) return kv.Key;
                }
                cur = RuntimeAccess.Read(cur, "parent");
            }

            // 2. Check NodesConstraints: if this item (or its parent object) is constrained as child of an OCIChar
            try
            {
                var ncType = RuntimeAccess.Find("NodesConstraints.NodesConstraints");
                var list = RuntimeAccess.Read(RuntimeAccess.Read(ncType, "_self"), "_constraints") as IEnumerable;
                if (list != null && item.objectItem != null)
                {
                    var itemRoot = item.objectItem.transform;
                    foreach (var handle in list)
                    {
                        if (handle == null) continue;
                        var child = RuntimeAccess.Read(handle, "childTransform") as Transform;
                        var parent = RuntimeAccess.Read(handle, "parentTransform") as Transform;
                        if (child == null || parent == null) continue;
                        if (!child.IsChildOf(itemRoot)) continue;
                        var ownerChar = parent.GetComponentInParent<ChaControl>();
                        if (ownerChar == null) continue;
                        foreach (var kv in dic)
                        {
                            var ch = kv.Value as global::Studio.OCIChar;
                            if (ch != null && ch.charInfo == ownerChar) return kv.Key;
                        }
                    }
                }
            }
            catch { }

            return -1;
        }

        private static StudioShaftModel Read(SkinnedMeshRenderer renderer, bool externalItem, int itemKey, string itemName, object treeNode)
        {
            if (renderer == null) return null;
            var mesh = renderer.sharedMesh;
            if (mesh == null) return null;
            var bones = renderer.bones;
            if (bones == null || bones.Length < 3) return null;
            var poses = mesh.bindposes;
            if (poses == null || poses.Length != bones.Length) return null;

            List<int> indices = TryReadHuman01Chain(bones) ?? TryReadPenisChain(bones);
            if (indices == null || indices.Count < 3) return null;

            var chain = new Transform[indices.Count];
            var segments = new Vector3[indices.Count - 1];
            for (int i = 0; i < indices.Count; i++)
            {
                chain[i] = bones[indices[i]];
                if (i > 0)
                    segments[i - 1] = poses[indices[i]].inverse.MultiplyPoint3x4(Vector3.zero) - poses[indices[i - 1]].inverse.MultiplyPoint3x4(Vector3.zero);
            }
            var model = new StudioShaftModel(renderer, chain, segments, externalItem, itemKey, itemName, treeNode);
            return Numbers.Finite(model.BaseLength) && model.BaseLength > 0.001 ? model : null;
        }

        private static List<int> TryReadHuman01Chain(Transform[] bones)
        {
            int root = Index(bones, "dick_base"), tip = Index(bones, "penor_e");
            if (root < 0 || tip < 0) return null;
            var indices = new List<int> { root };
            for (int i = 0; i < 10; i++)
            {
                int index = Index(bones, "shaft_" + i.ToString("00"));
                if (index < 0) return null;
                if (i > 0 && bones[index].parent != bones[indices[indices.Count - 1]]) return null;
                indices.Add(index);
            }
            int glans = Index(bones, "peno_glan");
            if (glans < 0 || bones[glans].parent != bones[indices[indices.Count - 1]] || bones[tip].parent != bones[glans]) return null;
            indices.Add(glans);
            indices.Add(tip);
            return indices;
        }

        // Supports both full 9-bone chains (Penis00..05 + PenisTip00/01/01_end) AND
        // shorter/variant Studio penis7 / penis10 / Human01_reference chains (e.g. Penis00..Penis05 + optional tip).
        private static List<int> TryReadPenisChain(Transform[] bones)
        {
            int p0 = IndexIgnoreCase(bones, "Penis00");
            if (p0 < 0) return null;

            // First try the classic 9-bone exact chain
            string[] classic = { "Penis00", "Penis01", "Penis02", "Penis03", "Penis04", "Penis05", "PenisTip00", "PenisTip01", "PenisTip01_end" };
            var exact = new List<int>();
            bool allExact = true;
            for (int i = 0; i < classic.Length; i++)
            {
                int idx = IndexIgnoreCase(bones, classic[i]);
                if (idx < 0 || (exact.Count > 0 && bones[idx].parent != bones[exact[exact.Count - 1]])) { allExact = false; break; }
                exact.Add(idx);
            }
            if (allExact) return exact;

            // Flexible sequential walk from Penis00 along direct child bones whose name contains "penis" or "tip" or "glan"
            var chain = new List<int> { p0 };
            var boneToIdx = new Dictionary<Transform, int>();
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null && !boneToIdx.ContainsKey(bones[i])) boneToIdx[bones[i]] = i;

            Transform cur = bones[p0];
            for (int step = 0; step < 20; step++)
            {
                int nextIdx = -1;
                int bestPriority = -1;
                for (int i = 0; i < bones.Length; i++)
                {
                    var b = bones[i];
                    if (b == null || b.parent != cur) continue;
                    string low = b.name.ToLowerInvariant();
                    if (low.Contains("ball") || low.Contains("scrot") || low.Contains("tama")) continue;
                    int prio = low.StartsWith("penis") ? 3 : (low.Contains("tip") || low.Contains("glan") || low.Contains("end") ? 2 : 0);
                    if (prio > bestPriority)
                    {
                        bestPriority = prio;
                        nextIdx = i;
                    }
                }
                if (nextIdx < 0) break;
                chain.Add(nextIdx);
                cur = bones[nextIdx];
            }
            return chain.Count >= 4 ? chain : null;
        }

        private static int Index(Transform[] bones, string name)
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null && bones[i].name == name) return i;
            return -1;
        }

        private static int IndexIgnoreCase(Transform[] bones, string name)
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null && string.Equals(bones[i].name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }
}
