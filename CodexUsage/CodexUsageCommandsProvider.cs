using System;
using System.Globalization;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CodexUsage;

/// <summary>Formatting shared by the Dock labels and the details flyout.</summary>
internal static class UsageText
{
    /// <summary>After this long without an update, values are shown as approximate (~).</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);

    public static bool HasReset(UsageWindow w) => w.ResetsAt is { } r && r <= DateTimeOffset.Now;

    public static bool IsStale(UsageSnapshot snap) => DateTimeOffset.Now - snap.UpdatedAt >= StaleAfter;

    public static string ResetLine(UsageWindow w)
    {
        if (w.ResetsAt is not { } at)
        {
            return string.Empty;
        }

        if (HasReset(w))
        {
            return "Window reset · waiting for a new reading";
        }

        var local = at.ToLocalTime();
        var left = local - DateTimeOffset.Now;
        var when = local.Date == DateTime.Today
            ? local.ToString("t", CultureInfo.CurrentCulture)
            : local.ToString("ddd ", CultureInfo.CurrentCulture) + local.ToString("t", CultureInfo.CurrentCulture);
        return $"Resets {when} · in {Duration(left)}";
    }

    public static string Duration(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h" :
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" :
        $"{Math.Max(1, t.Minutes)}m";
}

/// <summary>One Dock button: ring gauge + "5h 82%". Clicking opens the details flyout.</summary>
internal sealed partial class UsageItem : ListItem
{
    private readonly UsageService _service;
    private readonly UsageDetailsPage _page;
    private readonly bool _weekly;

    public UsageItem(UsageService service, bool weekly)
        : this(service, weekly, new UsageDetailsPage(service, weekly ? AppInfo.WeeklyItemId : AppInfo.FiveHourItemId))
    {
    }

    private UsageItem(UsageService service, bool weekly, UsageDetailsPage page)
        : base(page)
    {
        _service = service;
        _page = page;
        _weekly = weekly;
        Update();
        service.Changed += Update;
    }

    /// <summary>Stops live updates. Called when Command Palette has been handed a newer copy.</summary>
    public void Detach()
    {
        _service.Changed -= Update;
        _page.Detach();
    }

    private void Update()
    {
        var label = _weekly ? "7d" : "5h";
        var snap = _service.Latest;
        var w = _weekly ? snap?.Weekly : snap?.FiveHour;

        if (w is null)
        {
            Title = $"{label} --";
            Subtitle = "Waiting for Codex usage";
            Icon = new IconInfo(AppInfo.Glyph(0xE9D9));
            return;
        }

        Subtitle = UsageText.ResetLine(w);
        if (UsageText.HasReset(w))
        {
            Title = $"{label} --";
            Icon = new IconInfo(AppInfo.Glyph(0xE9D9));
            return;
        }

        var pct = Math.Round(w.Percent);
        var approx = UsageText.IsStale(snap!) ? "~" : string.Empty;
        Title = $"{label} {approx}{pct.ToString(CultureInfo.CurrentCulture)}%";
        Icon = GaugeIcons.For(pct);
    }
}

public sealed partial class CodexUsageCommandsProvider : CommandProvider
{
    private readonly UsageService _service = new();
    private readonly System.Threading.Lock _lock = new();
    private UsageItem[] _bandItems = [];
    private UsageDetailsPage? _detailsPage;

    public CodexUsageCommandsProvider()
    {
        Id = AppInfo.ProviderId;
        DisplayName = AppInfo.DisplayName;
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
    }

    // Command Palette asks for commands and bands each time it connects, and this process
    // outlives Command Palette restarts. Each request gets fresh objects. With SDK
    // 0.12.260812002, a property change goes to every subscriber in turn and stops at the
    // first one that throws. A Command Palette instance that has exited would otherwise
    // block updates to the running one. Fixed upstream in microsoft/PowerToys#50485.
    public override ICommandItem[] TopLevelCommands()
    {
        var page = new UsageDetailsPage(_service, AppInfo.DetailsId);
        lock (_lock)
        {
            _detailsPage?.Detach();
            _detailsPage = page;
        }

        return [
            new CommandItem(page)
            {
                Title = "Codex usage",
                Subtitle = "5-hour and weekly limits",
            },
        ];
    }

    public override ICommandItem[]? GetDockBands()
    {
        UsageItem[] items = [new UsageItem(_service, weekly: false), new UsageItem(_service, weekly: true)];
        lock (_lock)
        {
            foreach (var old in _bandItems)
            {
                old.Detach();
            }

            _bandItems = items;
        }

        return [new WrappedDockItem([.. items], AppInfo.BandId, AppInfo.DisplayName)];
    }
}
