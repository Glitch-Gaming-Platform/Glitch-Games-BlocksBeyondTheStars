using System.Linq;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.Geometry;
using Spacecraft.WorldGeneration;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

/// <summary>
/// Procedural creatures (World systems / §12): a seed-derived species roster per world (sized by
/// biodiversity), live spawning of mostly-non-hostile fauna, species-specific drops, and the
/// consume system (food heals, poison harms).
/// </summary>
public sealed class CreatureTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CreatureTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_creature_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(string planet, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "creature"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "creature",
            Seed = 4242,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    // ---------------- Roster generation ----------------

    [Fact]
    public void Roster_IsDeterministic_ForSameSeedAndPlanet()
    {
        var planet = _content.GetPlanet("jungle")!;
        var a = CreatureGenerator.GenerateRoster(planet, 12345);
        var b = CreatureGenerator.GenerateRoster(planet, 12345);

        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Id, b[i].Id);
            Assert.Equal(a[i].Habitat, b[i].Habitat);
            Assert.Equal(a[i].Temperament, b[i].Temperament);
            Assert.Equal(a[i].Activity, b[i].Activity);
            Assert.Equal(a[i].MaxHealth, b[i].MaxHealth);
            Assert.Equal(a[i].DropItem, b[i].DropItem);
        }
    }

    [Fact]
    public void Abundance_ControlsSpeciesCount()
    {
        var planet = ContentLoader.LoadFromDirectory(TestPaths.DataDir()).GetPlanet("jungle")!;

        planet.CreatureAbundance = "none";
        Assert.Empty(CreatureGenerator.GenerateRoster(planet, 7));

        planet.CreatureAbundance = "few";
        Assert.Equal(3, CreatureGenerator.GenerateRoster(planet, 7).Count);

        planet.CreatureAbundance = "many";
        Assert.Equal(6, CreatureGenerator.GenerateRoster(planet, 7).Count);
    }

    [Fact]
    public void Roster_IsNotAllHostile()
    {
        var planet = _content.GetPlanet("jungle")!; // "many"
        var roster = CreatureGenerator.GenerateRoster(planet, 4242);

        int hostile = roster.Count(s => s.Hostile);
        int peaceful = roster.Count(s => !s.Hostile);
        Assert.True(peaceful > 0, "A world must have some non-hostile creatures.");
        Assert.True(peaceful >= hostile, "Most creatures should be non-hostile.");
    }

    [Fact]
    public void Roster_DropsAreValid_AndCoverFoodOrPoison()
    {
        var planet = _content.GetPlanet("jungle")!;
        var roster = CreatureGenerator.GenerateRoster(planet, 4242);

        foreach (var s in roster)
        {
            Assert.False(string.IsNullOrEmpty(s.DropItem));
            Assert.True(s.DropCount >= 1);
            Assert.NotNull(_content.GetItem(s.DropItem)); // every drop is a real item
        }

        // Food/poison are the common kinds; a material substitute is the rare one.
        Assert.Contains(roster, s => s.DropKind is CreatureDropKind.Food or CreatureDropKind.Poison);
    }

    // ---------------- Live spawning & combat ----------------

    [Fact]
    public void Creatures_SpawnOnALushWorld()
    {
        var server = Started("jungle", out var repo); // "many"
        using (repo)
        {
            var p = server.AddLocalPlayer("Ranger");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);

            server.Tick(6.0); // spawn interval elapses
            Assert.NotEmpty(server.Creatures);
        }
    }

    [Fact]
    public void AttackCreature_KillsIt_AndYieldsItsSpeciesDrop()
    {
        var server = Started("jungle", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Ranger");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);

            server.Tick(6.0);
            var creature = server.Creatures.First();
            var species = server.SpeciesRoster.First(s => s.Id == creature.SpeciesId);
            int before = p.State.Inventory.CountOf(species.DropItem);

            for (int i = 0; i < 6 && server.Creatures.Any(c => c.Id == creature.Id); i++)
            {
                server.AttackEntity("Ranger", creature.Id);
            }

            Assert.DoesNotContain(server.Creatures, c => c.Id == creature.Id);
            Assert.True(p.State.Inventory.CountOf(species.DropItem) >= before + species.DropCount);
        }
    }

    // ---------------- Eating (food heals, poison harms) ----------------

    [Fact]
    public void EatingFood_RestoresHealth_AndConsumesIt()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Eater");
            p.State.Health = 50f;
            p.State.Inventory.Add("creature_meat", 2, 20);

            server.ConsumeItem("Eater", "creature_meat"); // +25 health
            Assert.Equal(75f, p.State.Health);
            Assert.Equal(1, p.State.Inventory.CountOf("creature_meat"));
        }
    }

    [Fact]
    public void EatingFood_IsCappedAtFullHealth()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Eater");
            p.State.Health = 90f;
            p.State.Inventory.Add("creature_meat", 1, 20);

            server.ConsumeItem("Eater", "creature_meat");
            Assert.Equal(100f, p.State.Health); // clamped
        }
    }

    [Fact]
    public void EatingPoison_HarmsThePlayer()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Eater");
            p.State.Health = 100f;
            p.State.Inventory.Add("toxic_gland", 1, 20);

            server.ConsumeItem("Eater", "toxic_gland"); // -20 health
            Assert.Equal(80f, p.State.Health);
            Assert.Equal(0, p.State.Inventory.CountOf("toxic_gland"));
        }
    }

    [Fact]
    public void Consume_RejectsNonConsumable_AndMissingItem()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Eater");
            p.State.Health = 70f;

            server.ConsumeItem("Eater", "stone");        // not a consumable
            server.ConsumeItem("Eater", "creature_meat"); // none in inventory
            Assert.Equal(70f, p.State.Health);            // nothing happened
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
