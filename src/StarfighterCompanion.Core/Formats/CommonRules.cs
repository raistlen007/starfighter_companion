using StarfighterCompanion.Core.Model;

namespace StarfighterCompanion.Core.Formats;

/// <summary>Rule helpers shared by several games.</summary>
internal static class CommonRules
{
    /// <summary>
    /// A rule violation that would break the game. It blocks saving only when the user caused it
    /// (one of the involved fields was edited); a state that was already in the file is reported as a warning
    /// so an untouched pilot can always be saved again.
    /// </summary>
    public static ValidationIssue Broken(string message, params EditableField[] involved) =>
        new(involved.Any(f => f.IsModified) ? IssueSeverity.Error : IssueSeverity.Warning,
            involved.Any(f => f.IsModified) ? message : message + " (this was already the case in the file)",
            involved.FirstOrDefault(f => f.IsModified) ?? involved.FirstOrDefault());

    public static ValidationIssue Odd(string message, EditableField? field = null) =>
        new(IssueSeverity.Warning, message, field);

    /// <summary>Hits can never exceed shots fired.</summary>
    public static IEnumerable<ValidationIssue> HitsVsShots(PilotDocument doc, string shotsId, string hitsId, string? hits2Id, string weapon)
    {
        var shots = doc.Field(shotsId);
        var hits = doc.Field(hitsId);
        var total = hits.GetInteger();
        EditableField? hits2 = null;
        if (hits2Id is not null)
        {
            hits2 = doc.Field(hits2Id);
            total += hits2.GetInteger();
        }

        if (total > shots.GetInteger())
        {
            yield return Odd($"More {weapon} hits ({total:N0}) than {weapon} shots fired ({shots.GetInteger():N0}).", hits.IsModified ? hits : shots);
        }
    }

    /// <summary>A career total should not be smaller than the sum of its breakdown.</summary>
    public static IEnumerable<ValidationIssue> TotalVsBreakdown(PilotDocument doc, string totalId, string arrayId, int count, string what)
    {
        var total = doc.Field(totalId);
        long sum = 0;
        for (var i = 0; i < count; i++)
        {
            sum += doc.Int($"{arrayId}[{i}]");
        }

        if (sum > total.GetInteger())
        {
            yield return Odd($"Total {what} ({total.GetInteger():N0}) is lower than the per-craft {what} added up ({sum:N0}).", total);
        }
    }
}
