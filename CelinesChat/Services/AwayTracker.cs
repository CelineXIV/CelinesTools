using System;
using System.Collections.Generic;

namespace CelinesChat.Services;

/// <summary>
/// "While you were away": collects incoming whispers and mentions while the game is in the
/// background or the character is /afk, and offers them as a summary once the player is back -
/// but only after a real absence (Configuration.AwaySummaryMinutes), not a quick Alt+Tab.
/// </summary>
internal sealed class AwayTracker
{
    private const int MaxCollected = 200;

    private readonly List<ChatLogEntry> collected = new();
    private bool away;
    private DateTime awayStart;

    /// <summary>The summary to show, or null. Stays until dismissed; a later absence adds to it.</summary>
    public AwaySummary? Summary { get; private set; }

    public bool IsAway => away;

    public void Update(bool awayNow, int minMinutes)
    {
        var now = DateTime.Now;
        if (awayNow && !away)
        {
            away = true;
            awayStart = now;
            collected.Clear();
            return;
        }

        if (awayNow || !away)
        {
            return;
        }

        away = false;
        if (collected.Count == 0 || now - awayStart < TimeSpan.FromMinutes(Math.Max(0, minMinutes)))
        {
            collected.Clear();
            return;
        }

        if (Summary == null)
        {
            Summary = new AwaySummary(awayStart, now, new List<ChatLogEntry>(collected));
        }
        else
        {
            Summary.Entries.AddRange(collected);
            Summary.End = now;
        }

        collected.Clear();
    }

    /// <summary>Called for every new message; only whispers/mentions (ChatLogEntry.NotifyKind) are kept.</summary>
    public void OnEntry(ChatLogEntry entry)
    {
        if (away && entry.NotifyKind != 0 && collected.Count < MaxCollected)
        {
            collected.Add(entry);
        }
    }

    public void Dismiss() => Summary = null;
}

internal sealed class AwaySummary
{
    public AwaySummary(DateTime start, DateTime end, List<ChatLogEntry> entries)
    {
        Start = start;
        End = end;
        Entries = entries;
    }

    public DateTime Start { get; }

    public DateTime End { get; set; }

    public List<ChatLogEntry> Entries { get; }
}
