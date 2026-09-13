using System.Net;
using System.Net.Http.Json;
using GeometryDashPlace.Web.Data.Entities;
using GeometryDashPlace.Web.Events;

namespace GeometryDashPlace.Web.Tests.Integration;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class EventArchiveTests(PostgreSqlIntegrationFixture database)
{
    [PostgreSqlFact]
    public async Task Archive_ListsPastEventsAndHidesActiveAndDraftEvents()
    {
        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("N");
        var endedOpen = Event("ended-open", "open", now.AddMinutes(-5), 12);
        var closed = Event("closed", "closed", now.AddMinutes(-10), 8);
        var archived = Event("archived", "archived", now.AddHours(-1), 5);
        var active = Event("active", "open", now.AddHours(1), 3);
        var draft = Event("draft", "draft", now.AddMinutes(-1), 1);

        await using (var context = database.CreateDbContext())
        {
            context.Events.AddRange(endedOpen, closed, archived, active, draft);
            await context.SaveChangesAsync();
        }

        using var client = database.Application.CreateClient(userId: null);
        var events = await client.GetFromJsonAsync<List<LevelEvent>>("/api/events");

        Assert.NotNull(events);
        var seededPastEvents = events
            .Where(levelEvent =>
                levelEvent.Id == endedOpen.Id ||
                levelEvent.Id == closed.Id ||
                levelEvent.Id == archived.Id)
            .ToList();
        Assert.Equal(
            [endedOpen.Id, closed.Id, archived.Id],
            seededPastEvents.Select(levelEvent => levelEvent.Id));
        Assert.DoesNotContain(events, levelEvent => levelEvent.Id == active.Id);
        Assert.DoesNotContain(events, levelEvent => levelEvent.Id == draft.Id);

        var detail = await client.GetFromJsonAsync<LevelEvent>($"/api/events/{closed.Slug}");
        Assert.NotNull(detail);
        Assert.Equal(closed.Id, detail.Id);
        Assert.Equal(closed.CurrentRevision, detail.Revision);

        using var draftResponse = await client.GetAsync($"/api/events/{draft.Slug}");
        Assert.Equal(HttpStatusCode.NotFound, draftResponse.StatusCode);

        LevelEventEntity Event(
            string label,
            string status,
            DateTimeOffset endsAt,
            long revision) => new()
            {
                Id = Guid.NewGuid(),
                Slug = $"archive-{label}-{suffix}",
                Name = $"Archive {label}",
                Width = 16,
                Height = 8,
                CooldownSeconds = 60,
                Status = status,
                CurrentRevision = revision,
                StartsAt = now.AddDays(-1),
                EndsAt = endsAt,
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now
            };
    }

    [PostgreSqlFact]
    public async Task UpcomingEvents_AreReturnedInStartOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("N");
        var later = Upcoming("later", now.AddDays(2));
        var next = Upcoming("next", now.AddHours(2));
        var draft = Upcoming("draft", now.AddHours(1), "draft");
        var alreadyStarted = Upcoming("started", now.AddMinutes(-5));

        await using (var context = database.CreateDbContext())
        {
            context.Events.AddRange(later, next, draft, alreadyStarted);
            await context.SaveChangesAsync();
        }

        using var client = database.Application.CreateClient(userId: null);
        var events = await client.GetFromJsonAsync<List<LevelEvent>>("/api/events/upcoming");

        Assert.NotNull(events);
        var seededEvents = events
            .Where(levelEvent => levelEvent.Id == next.Id || levelEvent.Id == later.Id)
            .ToList();
        Assert.Equal([next.Id, later.Id], seededEvents.Select(levelEvent => levelEvent.Id));
        Assert.DoesNotContain(events, levelEvent => levelEvent.Id == draft.Id);
        Assert.DoesNotContain(events, levelEvent => levelEvent.Id == alreadyStarted.Id);

        LevelEventEntity Upcoming(string label, DateTimeOffset startsAt, string status = "open") => new()
        {
            Id = Guid.NewGuid(),
            Slug = $"upcoming-{label}-{suffix}",
            Name = $"Upcoming {label}",
            Width = 32,
            Height = 16,
            CooldownSeconds = 60,
            Status = status,
            StartsAt = startsAt,
            EndsAt = startsAt.AddDays(1),
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
