namespace GeometryDashPlace.Web.Events;

public sealed record EventActionTotals(
    long Placements,
    long Replacements,
    long Deletions,
    long Moves,
    long MoveReplacements)
{
    public long Total => Placements + Replacements + Deletions + Moves + MoveReplacements;
}

public sealed record EventContributorResult(
    Guid UserId,
    string DisplayName,
    EventActionTotals Actions,
    DateTimeOffset FirstContributionAt,
    DateTimeOffset LastContributionAt);

public sealed record EventResultSummary(
    LevelEvent Event,
    int FinalObjectCount,
    EventActionTotals Actions,
    DateTimeOffset? FirstContributionAt,
    DateTimeOffset? LastContributionAt,
    IReadOnlyList<EventContributorResult> Contributors);

public interface IEventResultsRepository
{
    Task<EventResultSummary?> GetBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default);
}
