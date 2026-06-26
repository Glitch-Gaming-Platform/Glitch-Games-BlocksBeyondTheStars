// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Diagnostics;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// PHASE-0 spike validation for the river-routing plan
/// (<c>docs/developer/RIVER_ROUTING_AND_WATERFALLS_PLAN.md</c>). Confirms the three things the plan
/// said must hold before any worldgen change: (1) every river reaches a sink, (2) the result is
/// deterministic (the basis for the client rebuilding the same network from the seed), and (3) the
/// world-load build cost is acceptable. Nothing here touches <see cref="WorldGenerator.Generate"/>.
/// </summary>
public class RiverNetworkSpikeTests
{
    private readonly ITestOutputHelper _out;
    public RiverNetworkSpikeTests(ITestOutputHelper output) => _out = output;

    private static GameContent Content() => ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>Replicates <c>WorldGenerator.ResolveSeaFluid</c>'s water sea-level formula so the spike
    /// can inject the same sink height the real generator floods to.</summary>
    private static int SeaLevel(PlanetType p)
    {
        bool hasAir = !string.Equals(p.Atmosphere, "none", StringComparison.OrdinalIgnoreCase);
        double waterAb = p.WaterAbundance ?? (hasAir ? 0.55 : 0.0);
        return p.BaseHeight + (int)Math.Round((waterAb - 0.95) * p.Amplitude);
    }

    private static RiverNetwork BuildReal(WorldGenerator gen, PlanetType planet, long seed, int cellSize = 8)
    {
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        return RiverNetwork.Build(seed, circ, period, SeaLevel(planet),
            (x, z) => gen.SurfaceHeight(planet, x, z), cellSize);
    }

    // (1) + (2): a real wet world produces rivers that ALL reach the sea, identically across two builds.
    [Fact]
    public void RealWorld_Rivers_ReachSink_AndBuildIsDeterministic()
    {
        var content = Content();
        var planet = content.GetPlanet("jungle")!;
        const long seed = 992211;
        var gen = new WorldGenerator(seed, content);

        var a = BuildReal(gen, planet, seed);
        var b = BuildReal(gen, planet, seed);

        // Determinism: every per-cell array is byte-identical → the client can rebuild this from the seed
        // (option A in the plan) with no server→client snapshot needed, as long as no float drift creeps in.
        Assert.True(a.Height.SequenceEqual(b.Height), "heights differ");
        Assert.True(a.FilledLevel.SequenceEqual(b.FilledLevel), "filled levels differ");
        Assert.True(a.FlowDir.SequenceEqual(b.FlowDir), "flow dirs differ");
        Assert.True(a.FlowAccum.SequenceEqual(b.FlowAccum), "flow accum differ");
        Assert.Equal(a.ChannelCells.Count, b.ChannelCells.Count);
        Assert.Equal(a.Waterfalls.Count, b.Waterfalls.Count);

        // Rivers must actually exist on a wet world…
        Assert.True(a.ChannelCells.Count > 0, "a wet world produced no river channels");

        // …and the headline invariant: every channel cell drains to the sea (no dead-end on dry land).
        foreach (int c in a.ChannelCells)
        {
            Assert.True(a.ReachesSea(c), $"channel cell {c} does not reach the sea");
        }

        _out.WriteLine($"jungle/{seed}: grid {a.GridW}x{a.GridH}, channels={a.ChannelCells.Count}, " +
                       $"waterfalls={a.Waterfalls.Count}, lakeCells={a.LakeCellCount}, sources={a.SourceCount}");
    }

    // (1): the same must hold on several seeds and a second wet world — connectivity is not seed-luck.
    [Theory]
    [InlineData("jungle", 1)]
    [InlineData("jungle", 7)]
    [InlineData("ocean", 3)]
    [InlineData("varied", 42)]
    public void RealWorld_EveryChannelTerminatesInSeaOrLake(string planetKey, long seed)
    {
        var content = Content();
        var planet = content.GetPlanet(planetKey)!;
        var gen = new WorldGenerator(seed, content);
        var net = BuildReal(gen, planet, seed);

        foreach (int c in net.ChannelCells)
        {
            Assert.True(net.ReachesSea(c), $"{planetKey}/{seed}: channel {c} dead-ends");
        }

        _out.WriteLine($"{planetKey}/{seed}: channels={net.ChannelCells.Count}, " +
                       $"waterfalls={net.Waterfalls.Count}, lakeCells={net.LakeCellCount}");
    }

    // (user decision 1): fill-and-spill must turn an enclosed depression into a lake that still drains.
    [Fact]
    public void Synthetic_EnclosedDepression_BecomesLake_AndStillDrains()
    {
        const int size = 64, seaLevel = 5, cell = 1;
        // The builder samples grid row gz at worldZ = gz - size/2, so the latitude domain is [-32, 32).
        // Define the terrain directly in those world coords: a high plateau (100) everywhere, a sea column
        // at worldX 0, and a 3x3 pit (10) around (worldX 32, worldZ 0) fully enclosed by the plateau.
        int H(int x, int z)
        {
            int wx = ((x % size) + size) % size;
            if (wx == 0) return 0;                                   // sea sink
            if (Math.Abs(wx - 32) <= 1 && Math.Abs(z) <= 1) return 10; // the pit floor (z already in [-32,32))
            return 100;                                              // enclosing plateau
        }

        var net = RiverNetwork.Build(seed: 5, circumference: size, latitudePeriod: size,
            seaLevel: seaLevel, height: H, cellSize: cell);

        Assert.True(net.LakeCellCount >= 9, $"expected the pit to fill into a lake, got {net.LakeCellCount} lake cells");

        // Locate a pit cell by its terrain height (independent of the grid↔world mapping): it must be
        // filled above its terrain (a lake) yet still drain to the sea.
        int pit = -1;
        for (int i = 0; i < net.Height.Length; i++) if (net.Height[i] == 10) { pit = i; break; }
        Assert.True(pit >= 0, "the pit was not sampled into the grid");
        Assert.True(net.FilledLevel[pit] > net.Height[pit], "pit was not filled into a lake");
        Assert.True(net.ReachesSea(pit), "the filled lake does not drain to the sea");
    }

    // (1): a clean monotonic slope to a sea strip — a high source must descend the whole way to the sea.
    [Fact]
    public void Synthetic_TiltedSlope_SourceDescendsToSea()
    {
        const int w = 200, h = 64, seaLevel = 2, cell = 1;
        // Height falls from east to west to a sea strip at x<3. (The wrap seam at x=0 is a cliff, but the
        // flood routes everything to the sea regardless — we only assert the descent reaches the sea.)
        int Height(int x, int z)
        {
            int wx = ((x % w) + w) % w;
            return wx < 3 ? 0 : wx; // 0..199 ramp, low end is sea
        }

        var net = RiverNetwork.Build(seed: 9, circumference: w, latitudePeriod: h,
            seaLevel: seaLevel, height: Height, cellSize: cell, sourceCount: 8);

        Assert.True(net.ChannelCells.Count > 0, "no channel formed on a slope");

        // Walk the steepest channel down: filled level must be non-increasing to the sea (water never climbs).
        int start = net.ChannelCells.OrderByDescending(c => net.Height[c]).First();
        int c2 = start, prev = int.MaxValue, guard = w * h;
        while (guard-- > 0)
        {
            Assert.True(net.FilledLevel[c2] <= prev, "water surface climbed uphill along the channel");
            prev = net.FilledLevel[c2];
            if (net.IsSea[c2]) break;
            int d = net.FlowDir[c2];
            if (d < 0) break;
            c2 = d;
        }

        Assert.True(net.IsSea[c2] || net.FlowDir[c2] < 0, "channel did not terminate at the sea");
    }

    // (3): the whole-world build at world-load must be cheap enough. Logs the cost; asserts a generous
    // ceiling only to catch a pathological blow-up (timing is machine-dependent, so the bound is loose).
    [Fact]
    public void BuildCost_FullTorus_IsAcceptable()
    {
        var content = Content();
        var planet = content.GetPlanet("jungle")!;
        const long seed = 314159;
        var gen = new WorldGenerator(seed, content);

        var sw = Stopwatch.StartNew();
        var net = BuildReal(gen, planet, seed, cellSize: 8);
        sw.Stop();

        _out.WriteLine($"Full-torus RiverNetwork build (cellSize 8): {sw.ElapsedMilliseconds} ms, " +
                       $"grid {net.GridW}x{net.GridH} = {net.GridW * net.GridH} cells, " +
                       $"channels={net.ChannelCells.Count}, waterfalls={net.Waterfalls.Count}, lakeCells={net.LakeCellCount}");

        Assert.True(sw.ElapsedMilliseconds < 10000, $"build took {sw.ElapsedMilliseconds} ms — investigate cost");
    }
}
