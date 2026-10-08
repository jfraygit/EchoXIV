using System;
using System.Collections.Generic;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI;

/// The fixed vocabulary for "when am I usually on".
internal static class AvailabilitySlots
{
    /// What the start dropdown shows for "on this day, but no particular time" - a real answer, and the one a
    /// brand-new day starts on.
    public const string AnyTime = "Any time";

    public const string DefaultZone = "ET";

    /// 12AM through 11PM.
    public static readonly string[] Hours = BuildHours();

    /// The start dropdown's options - "Any time" at index 0, then the hours.
    public static readonly string[] StartOptions = BuildStartOptions();

    /// Abbreviations rather than IANA ids: these are read at a glance beside a time range, and
    /// "America/New_York" in a 90px cell is not.
    public static readonly string[] Zones = { "PT", "MT", "CT", "ET", "GMT", "CET", "JST", "AEST" };

    private static readonly Dictionary<string, int> HourIndex = BuildHourIndex();

    /// How long a range runs by default, the first time a start hour is picked.
    private const int DefaultRunHours = 3;

    /// Builds the stored note.
    public static string Compose(int startHour, int endHour, string zone)
    {
        if (startHour < 0 || startHour >= Hours.Length)
            return string.Empty;

        var end = endHour < 0 || endHour >= Hours.Length ? DefaultEndFor(startHour) : endHour;
        return $"{Hours[startHour]}-{Hours[end]} {NormalizeZone(zone)}";
    }

    public static int DefaultEndFor(int startHour) => (startHour + DefaultRunHours) % Hours.Length;

    /// Splits a stored note into its three parts.
    public static (int Start, int End, string? Zone) Parse(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return (-1, -1, null);

        var value = note!.Trim();

        var space = value.LastIndexOf(' ');
        var zone = space < 0 ? null : value[(space + 1)..].Trim();
        var range = space < 0 ? value : value[..space].Trim();

        var dash = range.IndexOf('-');
        if (dash <= 0)
            return (-1, -1, null);

        if (!HourIndex.TryGetValue(range[..dash].Trim(), out var start)
            || !HourIndex.TryGetValue(range[(dash + 1)..].Trim(), out var end))
        {
            return (-1, -1, null);
        }

        return (start, end, string.IsNullOrEmpty(zone) ? null : zone);
    }

    /// The range on its own, for a cell that shows the zone elsewhere.
    public static string WindowOf(string? note)
    {
        var (start, end, _) = Parse(note);
        if (start < 0)
            return string.Empty;

        return SameMeridiem(start, end)
            ? $"{HourLabel(start, withMeridiem: false)}-{Hours[end]}"
            : $"{Hours[start]}-{Hours[end]}";
    }

    /// The zone the given days are quoted in - whichever the first day carrying one says.
    public static string? ZoneOf(IEnumerable<DjAvailabilityDayDto> days)
    {
        foreach (var day in days)
        {
            if (!day.IsAvailable)
                continue;

            if (Parse(day.TimeNote).Zone is { Length: > 0 } zone)
                return zone;
        }

        return null;
    }

    /// Index of a zone in Zones, or the default's index when it isn't one of them.
    public static int ZoneIndex(string? zone)
    {
        if (zone != null)
        {
            for (var i = 0; i < Zones.Length; i++)
            {
                if (string.Equals(Zones[i], zone, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        return Array.IndexOf(Zones, DefaultZone);
    }

    private static bool SameMeridiem(int a, int b) => (a < 12) == (b < 12);

    private static string HourLabel(int hour24, bool withMeridiem)
    {
        var hour12 = hour24 % 12 == 0 ? 12 : hour24 % 12;
        return withMeridiem ? $"{hour12}{(hour24 < 12 ? "AM" : "PM")}" : hour12.ToString();
    }

    private static string[] BuildHours()
    {
        var hours = new string[24];
        for (var i = 0; i < 24; i++)
            hours[i] = HourLabel(i, withMeridiem: true);

        return hours;
    }

    private static string[] BuildStartOptions()
    {
        var options = new string[Hours.Length + 1];
        options[0] = AnyTime;
        Array.Copy(Hours, 0, options, 1, Hours.Length);
        return options;
    }

    private static Dictionary<string, int> BuildHourIndex()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Hours.Length; i++)
            map[Hours[i]] = i;

        return map;
    }

    private static string NormalizeZone(string zone) => Zones[ZoneIndex(zone)];
}
