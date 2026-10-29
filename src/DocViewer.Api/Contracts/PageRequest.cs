using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using DocViewer.Domain;

namespace DocViewer.Api.Contracts;

/// <summary>
/// Optional keyset paging (D-031): <c>limit</c> documents after the <c>cursor</c> position. Without a limit the whole
/// (filtered) list is returned, exactly as before pagination existed. The cursor is opaque to clients: base64url of
/// <c>v1|date|source|sourceId</c>, i.e. the <see cref="DocumentPosition"/> of the last document on the previous page.
/// </summary>
internal sealed record PageRequest(int? Limit, DocumentPosition? After)
{
    public const int MaxLimit = 100;

    private const string Version = "v1";

    public static PageRequest All { get; } = new(null, null);

    public static bool TryCreate(int? limit, string? cursor, [NotNullWhen(true)] out PageRequest? page, [NotNullWhen(false)] out string? error)
    {
        page = null;
        if (limit is < 1 or > MaxLimit)
        {
            error = $"limit must be between 1 and {MaxLimit}. Got {limit}.";
            return false;
        }

        DocumentPosition? after = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            if (!TryDecode(cursor, out var position))
            {
                error = "cursor is not valid. Use the nextCursor value of a previous response, unchanged.";
                return false;
            }

            after = position;
        }

        page = new PageRequest(limit, after);
        error = null;
        return true;
    }

    public static string Encode(DocumentPosition position) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(string.Join('|',
            Version, position.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), position.Source.ToString(), position.SourceId)));

    private static bool TryDecode(string cursor, out DocumentPosition position)
    {
        position = default;
        string text;
        try
        {
            text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
        }
        catch (FormatException)
        {
            return false;
        }

        // sourceId is last and may itself contain '|', so split into at most four parts.
        var parts = text.Split('|', 4);
        if (parts.Length != 4 || parts[0] != Version
            || !DateOnly.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || !Enum.TryParse<SourceSystem>(parts[2], ignoreCase: false, out var source) || !Enum.IsDefined(source)
            || parts[3].Length == 0)
        {
            return false;
        }

        position = new DocumentPosition(date, source, parts[3]);
        return true;
    }
}
