using System;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Geometry;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

/// <summary>
/// Suit oxygen on a toxic (non-breathable) world: it drains while the player is outside on the surface, but
/// the ship's life support keeps the air breathable — standing inside the ship refills the reserve and never
/// drains it. (Default rules: Survival + Normal oxygen consumption.)
/// </summary>
public sealed class OxygenTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public OxygenTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_oxy_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Start(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "oxy"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "oxy", Seed = 7, StartPlanet = "rocky", // rocky = toxic atmosphere → oxygen matters
            AutoSaveIntervalMinutes = 9999, PlaceStarterShip = true,
            PlaceSettlements = false, PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void AboardShip_RefillsOxygen_OutsideDrainsIt()
    {
        var server = Start(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot"); // spawns inside the ship at the heal-tank
            var insidePos = p.State.Position;

            // Inside the ship: oxygen recovers (life support), never drains.
            p.State.Oxygen = 40f;
            for (int i = 0; i < 10; i++)
            {
                server.TickForTest(0.5);
            }

            Assert.True(p.State.AboardShip, "Standing in the ship should read as aboard.");
            Assert.True(p.State.Oxygen > 40f, $"Oxygen should refill aboard the ship (was {p.State.Oxygen}).");

            // Step outside onto the toxic surface: oxygen now drains.
            p.State.Position = new Vector3f(insidePos.X + 60f, insidePos.Y, insidePos.Z + 60f);
            server.TickForTest(0.1); // let UpdateAboard see the move
            Assert.False(p.State.AboardShip, "Standing well away from the ship should not read as aboard.");

            float before = p.State.Oxygen = 80f;
            for (int i = 0; i < 6; i++)
            {
                server.TickForTest(0.5);
            }

            Assert.True(p.State.Oxygen < before, $"Oxygen should drain on the toxic surface (was {p.State.Oxygen}).");
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
