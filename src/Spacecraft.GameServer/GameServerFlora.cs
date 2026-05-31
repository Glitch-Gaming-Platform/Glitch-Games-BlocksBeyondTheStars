using System.Collections.Generic;
using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Primitives;

namespace Spacecraft.GameServer;

/// <summary>
/// Surface flora (World systems). Worldgen places one plant per eligible column (bounded — no
/// spreading). When a plant is harvested it <b>regrows on the same cell after a delay, as long
/// as its host block underneath is still intact</b> (mine the ground and it won't return).
/// Seeds let the player replant flora on a valid host block (validated here). Growth is capped:
/// one plant per host cell, never spreading.
///
/// Planned extension: per-species procedural appearance/effects and a maturity/"produces seeds"
/// state (normal harvest yields the species material — wood/berries/fibre; seeds only from a
/// matured, producing plant).
/// </summary>
public sealed partial class GameServer
{
    private const double FloraRegrowSeconds = 30.0;

    private ushort _floraPlantId, _floraCrystalId;
    private HashSet<ushort> _plantHostIds = new();
    private HashSet<ushort> _crystalHostIds = new();
    private readonly Dictionary<Vector3i, (ushort FloraId, double Timer)> _floraRegrow = new();

    private void InitFlora()
    {
        _floraPlantId = _content.GetBlock("flora_plant")?.NumericId.Value ?? 0;
        _floraCrystalId = _content.GetBlock("flora_crystal")?.NumericId.Value ?? 0;
        _plantHostIds = HostIds("grass", "dirt", "mud");
        _crystalHostIds = HostIds("crystal", "stone", "basalt");
    }

    private HashSet<ushort> HostIds(params string[] keys)
    {
        var set = new HashSet<ushort>();
        foreach (var k in keys)
        {
            if (_content.GetBlock(k) is { } d)
            {
                set.Add(d.NumericId.Value);
            }
        }

        return set;
    }

    private bool IsFlora(ushort id) => id != 0 && (id == _floraPlantId || id == _floraCrystalId);

    /// <summary>True if the flora may be planted at the cell — the block below must be a valid host.</summary>
    private bool IsValidFloraHost(ushort floraId, Vector3i pos)
    {
        ushort below = _world.GetBlock(new Vector3i(pos.X, pos.Y - 1, pos.Z)).Value;
        if (floraId == _floraPlantId)
        {
            return _plantHostIds.Contains(below);
        }

        if (floraId == _floraCrystalId)
        {
            return _crystalHostIds.Contains(below);
        }

        return false;
    }

    /// <summary>Test/diagnostic: whether a flora block could be planted at a cell.</summary>
    public bool CanPlantFlora(string floraKey, int x, int y, int z)
    {
        var def = _content.GetBlock(floraKey);
        return def != null && IsFlora(def.NumericId.Value) && IsValidFloraHost(def.NumericId.Value, new Vector3i(x, y, z));
    }

    /// <summary>Schedules a harvested plant to regrow on its cell (if the host stays intact).</summary>
    private void ScheduleFloraRegrow(Vector3i pos, ushort floraId)
        => _floraRegrow[pos] = (floraId, FloraRegrowSeconds);

    private void TickFlora(double dt)
    {
        if (_floraRegrow.Count == 0)
        {
            return;
        }

        List<Vector3i>? done = null;
        // Iterate over a copy of the keys so we can update/remove entries safely.
        foreach (var pos in new List<Vector3i>(_floraRegrow.Keys))
        {
            var (floraId, timer) = _floraRegrow[pos];
            timer -= dt;
            if (timer > 0)
            {
                _floraRegrow[pos] = (floraId, timer);
                continue;
            }

            (done ??= new List<Vector3i>()).Add(pos);

            // Regrow only if the cell is still air and the host below is a valid ground for it.
            if (_world.GetBlock(pos).IsAir && IsValidFloraHost(floraId, pos))
            {
                _world.SetBlock(pos, new BlockId(floraId));
                Broadcast(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = floraId });
            }
        }

        if (done != null)
        {
            foreach (var pos in done)
            {
                _floraRegrow.Remove(pos);
            }
        }
    }
}
