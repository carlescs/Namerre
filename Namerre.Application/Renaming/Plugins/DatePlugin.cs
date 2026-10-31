using System.Globalization;

namespace Namerre.Application.Renaming.Plugins;

/// <summary>
/// Inserts the file's modified or created date, formatted with a .NET custom date format in the invariant
/// culture and converted to a chosen time zone (the server's local zone when none is given).
/// </summary>
public sealed class DatePlugin() : RenamePlugin<DatePlugin.Options>("date", 1, "Date",
    "Insert the file's modified or created date",
    [
        new([
            new("source", "Date", OptionFieldKind.Select, [new("modified", "Last modified"), new("created", "Created")]),
            new("format", "Format", Hint: ".NET date format, e.g. yyyy-MM-dd or yyyyMMdd_HHmmss",
                Suggestions: ["yyyy-MM-dd", "yyyyMMdd", "yyyyMMdd_HHmmss", "yyyy-MM-dd HH.mm.ss", "yyyy-MM"]),
            new("timeZone", "Time zone", Hint: "IANA or Windows ID such as Europe/Madrid; empty uses the server's",
                Placeholder: "Server local time", Suggestions: ["UTC"])
        ]),
        new([
            NameInsertion.PositionField(Placeholder),
            NameInsertion.SeparatorField(),
            NameInsertion.TemplateField(Placeholder, "{date} {name}")
        ])
    ])
{
    public const string Placeholder = "{date}";

    public sealed class Options
    {
        public string Source { get; init; } = "modified";
        public string Format { get; init; } = "yyyy-MM-dd";
        public string TimeZone { get; init; } = "";
        public string Position { get; init; } = "prefix";
        public string Separator { get; init; } = " ";
        public string Template { get; init; } = "{date} {name}";
    }

    protected override PathParts Apply(PluginContext context, Options options)
    {
        var date = options.Source == "created"
            ? context.Facts.CreatedUtc
                ?? throw new RenameException("DateUnavailable", "The filesystem does not record this file's creation date.")
            : context.Facts.ModifiedUtc;
        var value = Format(TimeZoneInfo.ConvertTime(date, Zone(options.TimeZone)), options.Format);
        return context.Path with
        {
            Stem = NameInsertion.Insert(context.Path.Stem, value, options.Position, options.Separator, options.Template,
                Placeholder)
        };
    }

    internal static string Format(DateTimeOffset date, string format)
    {
        if (string.IsNullOrWhiteSpace(format))
            throw new RenameException("InvalidConfiguration", "Enter a date format.");
        // A single character is a standard format (e.g. "d"), whose culture-specific output is rarely file-safe.
        if (format.Length == 1)
            throw new RenameException("InvalidConfiguration",
                $"Date format \"{format}\" is a standard format; use a custom one such as yyyy-MM-dd.");
        try
        {
            return date.ToString(format, CultureInfo.InvariantCulture);
        }
        catch (FormatException exception)
        {
            throw new RenameException("InvalidConfiguration", $"Date format \"{format}\" is not a valid .NET date format.",
                innerException: exception);
        }
    }

    private static TimeZoneInfo Zone(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Local;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new RenameException("InvalidConfiguration", $"Unknown time zone \"{id}\".", innerException: exception);
        }
    }
}
