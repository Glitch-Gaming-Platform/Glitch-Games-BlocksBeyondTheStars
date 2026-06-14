namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Server → client: the active story's shared, server-wide progress for this save — the meter shown in the
/// Story Log tab + Map tab, plus the raw counters. Sent on join and whenever the story advances. The
/// narrator beats themselves arrive separately on the existing VEGA <c>ShipAiLine</c> channel; this message
/// carries only the aggregate state. <see cref="Active"/> is false when the story is disabled ("none"
/// sandbox).
/// </summary>
public sealed class StoryStateMessage
{
    /// <summary>The active story pack id (e.g. "vega_protocol"), or "none" when disabled.</summary>
    public string StoryId { get; set; } = string.Empty;

    /// <summary>False when no story is active (the "none" sandbox) — the UI hides the meter/tab.</summary>
    public bool Active { get; set; }

    /// <summary>The current weighted progress score.</summary>
    public int Progress { get; set; }

    /// <summary>The score that opens the finale (the last beat's threshold) — for the "NN %" meter.</summary>
    public int ProgressTarget { get; set; }

    public int FragmentsFound { get; set; }
    public int MachineKills { get; set; }
    public int Milestones { get; set; }

    /// <summary>How many narrator beats of the arc have been revealed so far.</summary>
    public int BeatsRevealed { get; set; }

    public bool GuardianSystemRevealed { get; set; }
    public bool GuardianDefeated { get; set; }
}

/// <summary>
/// Client → server (admin only): choose the save's active story pack, or "none" to disable the story
/// (sandbox). Switching resets the per-save story progress to a fresh state for the chosen pack.
/// </summary>
public sealed class StorySelectIntent
{
    public string StoryId { get; set; } = string.Empty;
}

/// <summary>One net fragment scattered on a body's surface for the client to render + let the player pick up
/// (walk up, press E). Text-only story finds — distinct from the knowledge mini-game dataqubes. The archive
/// text is not sent until pickup (see <see cref="NetFragmentRevealed"/>); this carries only position + the
/// lore <see cref="Category"/> for the icon/tint. Server-authoritative placement (deterministic from seed).</summary>
public sealed class NetStoryFragment
{
    public int Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>Lore category (vega | sps | guardian | network | settler | netnode) for the client icon/tint.</summary>
    public string Category { get; set; } = string.Empty;
}

/// <summary>Full set of net fragments the client should render for its current world (server → client).</summary>
public sealed class NetFragmentList
{
    public NetStoryFragment[] Fragments { get; set; } = System.Array.Empty<NetStoryFragment>();
}

/// <summary>The player picks up a net fragment they're standing at — press E (client → server). The server
/// validates the fragment exists and the player is within reach, then reveals it and advances the story.</summary>
public sealed class NetFragmentFoundIntent
{
    public int FragmentId { get; set; }
}

/// <summary>Server → client: the picked-up fragment's archive text to show in the reader panel. The client
/// localizes <see cref="TextKey"/> (bilingual DE+EN); <see cref="Category"/> tints/labels the entry.</summary>
public sealed class NetFragmentRevealed
{
    public string Category { get; set; } = string.Empty;
    public string TextKey { get; set; } = string.Empty;
}

/// <summary>Server → client: a personal player memory just unlocked (by defeating a Guardian machine). The
/// client localizes <see cref="TextKey"/> (DE+EN) and shows it in the reader. Per-player — each player is a
/// different neural-imprint clone, so memories never contradict across a multiplayer crew.</summary>
public sealed class PlayerMemoryRevealed
{
    public string TextKey { get; set; } = string.Empty;
}
