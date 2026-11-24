using System.Collections.Generic;
using KoiSync.KK;
using UnityEngine;

namespace KoiSync.MainGame
{
    internal sealed class GameContactPoint : KkContactPoint
    {
        internal GameContactPoint(Dictionary<string,Transform> bones,OpeningKind kind,Transform node)
            : base(bones,(ContactLandmarkKind)kind,node) {}
    }
}
