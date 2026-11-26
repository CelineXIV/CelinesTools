using System;
using System.Collections.Generic;

namespace CelinesChat.Services;

internal sealed class AwayTracker
{
    private const int MaxCollected = 200;

    private readonly List<ChatLogEntry> collected = new();
    private bool away;
    private DateTime awayStart;

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
