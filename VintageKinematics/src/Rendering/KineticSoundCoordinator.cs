using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Rendering
{
    /// <summary>
    /// Client-only coordinator for kinetic looping sounds. Picks one winner
    /// per (networkId, dedupKey) pair (closest to the listener) and applies
    /// a global cap per dedupKey across all networks.
    /// </summary>
    public class KineticSoundCoordinator : ModSystem
    {
        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

        public class Entry
        {
            public long NetworkId;
            public string DedupKey;
            public Vec3d Pos;
            public int MaxSimultaneous = 3;
            public object Tag;
        }

        private readonly List<Entry> registered = new List<Entry>();
        private readonly object lockObj = new object();

        public void Register(Entry e)
        {
            lock (lockObj) { registered.Add(e); }
        }

        public void Unregister(object tag)
        {
            lock (lockObj) { registered.RemoveAll(x => x.Tag == tag); }
        }

        public override void StartClientSide(ICoreClientAPI capi)
        {
            base.StartClientSide(capi);
            capi.Event.RegisterGameTickListener(_ => Recompute(capi.World.Player.Entity.Pos.XYZ), 250);
        }

        private void Recompute(Vec3d listener)
        {
            List<Entry> snap;
            lock (lockObj) { snap = new List<Entry>(registered); }

            int cap = 3;
            foreach (var e in snap) cap = e.MaxSimultaneous;

            // TODO: v1.1 — apply PickWinners to mute losers
            var winners = PickWinners(snap, listener, cap);
        }

        public static List<Entry> PickWinners(List<Entry> sources, Vec3d listener, int maxPerKey)
        {
            var perPair = new Dictionary<(long, string), Entry>();
            foreach (var s in sources)
            {
                var key = (s.NetworkId, s.DedupKey);
                if (!perPair.TryGetValue(key, out var existing) ||
                    s.Pos.SquareDistanceTo(listener) < existing.Pos.SquareDistanceTo(listener))
                {
                    perPair[key] = s;
                }
            }

            var byKey = new Dictionary<string, List<Entry>>();
            foreach (var e in perPair.Values)
            {
                if (!byKey.TryGetValue(e.DedupKey, out var list))
                {
                    list = new List<Entry>();
                    byKey[e.DedupKey] = list;
                }
                list.Add(e);
            }

            var winners = new List<Entry>();
            foreach (var list in byKey.Values)
            {
                list.Sort((a, b) => a.Pos.SquareDistanceTo(listener).CompareTo(b.Pos.SquareDistanceTo(listener)));
                int take = list.Count < maxPerKey ? list.Count : maxPerKey;
                for (int i = 0; i < take; i++) winners.Add(list[i]);
            }
            return winners;
        }
    }
}
