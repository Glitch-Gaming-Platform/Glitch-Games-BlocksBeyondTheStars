using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Story;

/// <summary>
/// One narrator beat in a story pack's ordered arc. Beats reveal strictly in <see cref="Index"/> order, each
/// gated by a <see cref="Threshold"/> on the story-progress score (see <see cref="StoryEngine"/>). The
/// <see cref="TextKey"/> is a locale key the narrator (e.g. VEGA) speaks — bilingual DE+EN like all in-game
/// text. The pack is story-agnostic data: nothing here is VEGA-specific.
/// </summary>
public sealed class StoryBeat
{
    /// <summary>Position in the arc (0-based). Beats are revealed in ascending index order.</summary>
    public int Index { get; set; }

    /// <summary>Short dev-facing working title (not shown to players).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Locale key spoken by the narrator when this beat reveals (must exist in DE+EN once wired to UI).</summary>
    public string TextKey { get; set; } = string.Empty;

    /// <summary>Progress score at/above which this beat reveals (monotonic across the arc).</summary>
    public int Threshold { get; set; }

    /// <summary>Optional knowledge points granted to the revealing player when this beat first fires.</summary>
    public int KnowledgeReward { get; set; }
}

/// <summary>
/// One findable net fragment of a story pack — a text-only story find (distinct from the knowledge
/// mini-game dataqubes). Placed in the world (structures + scattered on planet surfaces); picking one up
/// reveals its archive <see cref="TextKey"/> and advances the shared story. Deduped by <see cref="Key"/>.
/// </summary>
public sealed class StoryFragment
{
    /// <summary>Unique fragment id (the dedupe key tracked in <c>StoryState.FoundFragmentKeys</c>).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Lore category: vega | sps | guardian | network | settler | netnode (for the reader/icon).</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Locale key of the archive text shown on pickup (bilingual DE+EN).</summary>
    public string TextKey { get; set; } = string.Empty;

    /// <summary>Relative draw weight (rarity) when picking which fragment a spot holds.</summary>
    public int Weight { get; set; } = 1;
}

/// <summary>
/// One personal player memory — a fragment of the cloned SPS member's life, unlocked (in order) when the
/// player defeats the Guardian's machines. Per-player + non-contradictory in multiplayer (each player is a
/// different neural imprint). Tracked per-player in <c>PlayerState.Milestones</c> as <c>story:mem:&lt;key&gt;</c>.
/// </summary>
public sealed class StoryMemory
{
    /// <summary>Unique memory id (the per-player unlock key).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Locale key of the memory text (bilingual DE+EN), shown in the reader on unlock.</summary>
    public string TextKey { get; set; } = string.Empty;
}

/// <summary>
/// A story pack: identity + pacing config + the ordered beat arc. The story engine consumes this and is
/// completely story-agnostic, so further storylines are added as more packs (see the implementation plan
/// D2–D4). For P0 a pack is code-defined in <see cref="StoryRegistry"/>; a later phase loads it from
/// <c>data/stories/&lt;id&gt;/</c>.
/// </summary>
public sealed class StoryDefinition
{
    /// <summary>Stable pack id (e.g. "vega_protocol"); keys the per-save story state + the selection UI.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Locale key for the pack's display name (shown in the story-selection world option).</summary>
    public string NameKey { get; set; } = string.Empty;

    // Pacing weights: progress = FragmentsFound*FragmentWeight
    //                          + min(MachineKills, KillContributionCap)*KillWeight
    //                          + Milestones*MilestoneWeight   (see StoryEngine).

    /// <summary>Score contribution per net fragment found (the primary story driver).</summary>
    public int FragmentWeight { get; set; } = 3;

    /// <summary>Score contribution per Guardian-machine kill, up to <see cref="KillContributionCap"/>.</summary>
    public int KillWeight { get; set; } = 1;

    /// <summary>Score contribution per milestone (system mapped / settlement helped / first base or station).</summary>
    public int MilestoneWeight { get; set; } = 2;

    /// <summary>Diminishing-returns cap: machine kills beyond this stop adding to progress (anti-grind).</summary>
    public int KillContributionCap { get; set; } = 40;

    /// <summary>The ordered beat arc (ascending <see cref="StoryBeat.Index"/> and <see cref="StoryBeat.Threshold"/>).
    /// A concrete list so it deserializes cleanly from a pack's <c>story.json</c>.</summary>
    public List<StoryBeat> Beats { get; set; } = new();

    /// <summary>The pack's findable net fragments (text-only story finds placed in the world). Empty packs
    /// still work — combat then drives the story alone.</summary>
    public List<StoryFragment> Fragments { get; set; } = new();

    /// <summary>The pack's personal player memories, unlocked in order by defeating machines (per player).</summary>
    public List<StoryMemory> Memories { get; set; } = new();
}
