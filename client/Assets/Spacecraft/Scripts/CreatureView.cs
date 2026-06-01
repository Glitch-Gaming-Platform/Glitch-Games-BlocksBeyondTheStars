using System.Collections.Generic;
using Spacecraft.Networking.Messages;
using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Renders the server's live creatures (<c>GameBootstrap.Creatures</c>) as parametric blocky
    /// bodies via <see cref="CreatureBuilder"/>, syncing the set each frame. The server is
    /// authoritative over spawns/positions/deaths; positions are interpolated for smoothness and
    /// the player attacks the nearest one with F (PlayerController). Render-only.
    /// </summary>
    public sealed class CreatureView : MonoBehaviour
    {
        public GameBootstrap Game;

        private sealed class Entry
        {
            public GameObject Root;
            public Vector3 Target;
            public string Bank;   // creature_{size}_{disposition} voice bank
            public float Pitch;   // shifted by body size for per-creature variety
            public float NextCall; // Time.time of the next idle vocalisation
        }

        private readonly Dictionary<string, Entry> _creatures = new Dictionary<string, Entry>();

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            var seen = new HashSet<string>();
            foreach (var c in Game.Creatures)
            {
                seen.Add(c.Id);
                var pos = new Vector3(c.X, c.Y, c.Z);
                if (!_creatures.TryGetValue(c.Id, out var entry))
                {
                    var root = new GameObject("Creature_" + c.SpeciesId);
                    root.transform.position = pos;
                    new CreatureBuilder().Build(root, c);
                    entry = new Entry
                    {
                        Root = root,
                        Target = pos,
                        Bank = Bank(c),
                        Pitch = Mathf.Clamp(1.5f - 0.35f * c.Size, 0.7f, 1.6f),
                        NextCall = Time.time + Random.Range(2f, 6f),
                    };
                    _creatures[c.Id] = entry;
                }

                entry.Target = pos;
                // Smoothly chase the authoritative position (creatures may wander later).
                entry.Root.transform.position = Vector3.Lerp(entry.Root.transform.position, entry.Target, Time.deltaTime * 8f);

                // Periodic idle vocalisation, spatialised at the creature, pitched by its size.
                if (Time.time >= entry.NextCall)
                {
                    entry.NextCall = Time.time + Random.Range(5f, 12f);
                    ClientAudio.Instance?.At(entry.Bank + "_idle", entry.Root.transform.position, entry.Pitch, 0.8f);
                }
            }

            if (_creatures.Count > seen.Count)
            {
                var stale = new List<string>();
                foreach (var id in _creatures.Keys)
                {
                    if (!seen.Contains(id))
                    {
                        stale.Add(id);
                    }
                }

                foreach (var id in stale)
                {
                    var e = _creatures[id];
                    ClientAudio.Instance?.At(e.Bank + "_die", e.Root.transform.position, e.Pitch, 0.9f);
                    Destroy(e.Root);
                    _creatures.Remove(id);
                }
            }
        }

        /// <summary>Voice bank for a creature: size tier (small/medium/large) x disposition (calm/hostile).</summary>
        private static string Bank(NetCreature c)
        {
            string size = c.Size < 0.8f ? "small" : c.Size < 1.6f ? "medium" : "large";
            return $"creature_{size}_{(c.Hostile ? "hostile" : "calm")}";
        }
    }
}
