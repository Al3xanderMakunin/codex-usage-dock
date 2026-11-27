using System;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CodexUsage;

/// <summary>
/// Flyout shown when a Dock item is clicked: both windows with bars, exact reset times,
/// and data freshness.
/// </summary>
internal sealed partial class UsageDetailsPage : ContentPage
{
    private readonly UsageService _service;
    private readonly DetailsForm _form;

    public UsageDetailsPage(UsageService service, string id)
    {
        _service = service;
        Id = id;
        Name = "Details";
        Title = "Codex usage";
        Icon = IconHelpers.FromRelativePath(@"Assets\StoreLogo.png");
        _form = new DetailsForm(service);
        Commands =
        [
            new CommandContextItem(new OpenUrlCommand(AppInfo.UsageUrl)) { Title = "Open Codex usage" },
        ];
        _service.Changed += _form.Refresh;
        _form.Refresh();
    }

    /// <summary>Stops live updates. Called when Command Palette has been handed a newer copy.</summary>
    public void Detach() => _service.Changed -= _form.Refresh;

    public override IContent[] GetContent() => [_form];

    private sealed partial class DetailsForm : FormContent
    {
        private readonly UsageService _service;

        public DetailsForm(UsageService service)
        {
            _service = service;
            TemplateJson = Template;
        }

        public void Refresh() => DataJson = BuildData(_service.Latest).ToJsonString();

        public override ICommandResult SubmitForm(string inputs, string data) => CommandResult.KeepOpen();
    }

    private static JsonObject BuildData(UsageSnapshot? snap)
    {
        var hasData = snap is not null && (snap.FiveHour is not null || snap.Weekly is not null);
        var data = new JsonObject
        {
            ["waiting"] = !hasData,
            ["usageUrl"] = AppInfo.UsageUrl,
            ["fiveHour"] = Window(snap?.FiveHour),
            ["weekly"] = Window(snap?.Weekly),
        };

        if (snap is not null)
        {
            var age = DateTimeOffset.Now - snap.UpdatedAt;
            data["updated"] = $"Updated {Ago(age)} from Codex";
            data["stale"] = UsageText.IsStale(snap);
        }

        return data;
    }

    private static JsonObject? Window(UsageWindow? w)
    {
        if (w is null)
        {
            return null;
        }

        var pct = Math.Round(w.Percent);
        var expired = UsageText.HasReset(w);
        return new JsonObject
        {
            ["percent"] = expired ? "—" : $"{pct.ToString(CultureInfo.CurrentCulture)}%",
            ["bar"] = expired ? null : Bar(pct),
            ["resets"] = UsageText.ResetLine(w),
        };
    }

    private static string Bar(double percent)
    {
        const int width = 1000;
        var filled = Math.Clamp(percent, 0, 100) / 100 * width;
        var color = GaugeIcons.AccentColor(GaugeIcons.SeverityOf(percent));
        var inv = CultureInfo.InvariantCulture;
        var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='8' viewBox='0 0 {width} 8' preserveAspectRatio='none'>" +
                  $"<rect width='{width}' height='8' rx='4' fill='rgb(128,128,128)' fill-opacity='0.25'/>" +
                  (filled > 0 ? $"<rect width='{Math.Max(8, filled).ToString("F1", inv)}' height='8' rx='4' fill='{color}'/>" : string.Empty) +
                  "</svg>";
        return "data:image/svg+xml;utf8," + svg;
    }

    private static string Ago(TimeSpan t) =>
        t.TotalMinutes < 1 ? "just now" :
        t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min ago" :
        t.TotalDays < 1 ? $"{(int)t.TotalHours}h {t.Minutes}m ago" :
        $"{(int)t.TotalDays}d ago";

    private const string Template = """
    {
      "type": "AdaptiveCard",
      "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
      "version": "1.5",
      "body": [
        {
          "type": "Container",
          "$when": "${waiting}",
          "items": [
            { "type": "TextBlock", "text": "Waiting for Codex", "weight": "bolder", "size": "medium" },
            { "type": "TextBlock", "wrap": true, "isSubtle": true,
              "text": "Send a message in Codex on this PC. Usage appears when Codex records subscription limits in its local session logs." }
          ]
        },
        {
          "type": "Container",
          "$when": "${fiveHour != null}",
          "spacing": "medium",
          "items": [
            { "type": "ColumnSet", "columns": [
              { "type": "Column", "width": "stretch", "verticalContentAlignment": "bottom", "items": [
                { "type": "TextBlock", "text": "5-hour limit", "weight": "bolder" },
                { "type": "TextBlock", "text": "5-hour window", "isSubtle": true, "size": "small", "spacing": "none" } ] },
              { "type": "Column", "width": "auto", "items": [
                { "type": "TextBlock", "text": "${fiveHour.percent}", "size": "extraLarge", "weight": "bolder" } ] } ] },
            { "type": "Image", "$when": "${fiveHour.bar != null}", "url": "${fiveHour.bar}", "size": "stretch", "height": "8px", "spacing": "small" },
            { "type": "TextBlock", "text": "${fiveHour.resets}", "isSubtle": true, "size": "small", "spacing": "small" }
          ]
        },
        {
          "type": "Container",
          "$when": "${weekly != null}",
          "spacing": "large",
          "items": [
            { "type": "ColumnSet", "columns": [
              { "type": "Column", "width": "stretch", "verticalContentAlignment": "bottom", "items": [
                { "type": "TextBlock", "text": "Weekly", "weight": "bolder" },
                { "type": "TextBlock", "text": "7-day window", "isSubtle": true, "size": "small", "spacing": "none" } ] },
              { "type": "Column", "width": "auto", "items": [
                { "type": "TextBlock", "text": "${weekly.percent}", "size": "extraLarge", "weight": "bolder" } ] } ] },
            { "type": "Image", "$when": "${weekly.bar != null}", "url": "${weekly.bar}", "size": "stretch", "height": "8px", "spacing": "small" },
            { "type": "TextBlock", "text": "${weekly.resets}", "isSubtle": true, "size": "small", "spacing": "small" }
          ]
        },
        {
          "type": "Container",
          "$when": "${stale}",
          "style": "warning",
          "spacing": "medium",
          "items": [
            { "type": "TextBlock", "wrap": true, "size": "small",
              "text": "The latest local reading is over an hour old. Use Codex to refresh the numbers." }
          ]
        },
        { "type": "TextBlock", "$when": "${updated != null}", "text": "${updated}", "isSubtle": true, "size": "small", "spacing": "medium" }
      ],
      "actions": [
        { "type": "Action.OpenUrl", "title": "Open Codex usage", "url": "${usageUrl}" }
      ]
    }
    """;
}
