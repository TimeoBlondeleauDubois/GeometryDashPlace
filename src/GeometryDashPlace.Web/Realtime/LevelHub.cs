using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GeometryDashPlace.Web.Realtime;

public interface ILevelClient
{
    Task LevelChanged(LevelChange change);
}

[Authorize]
public sealed class LevelHub : Hub<ILevelClient>
{
    public Task JoinEvent(Guid eventId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupName(eventId));

    public Task LeaveEvent(Guid eventId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(eventId));

    internal static string GroupName(Guid eventId) => $"level:{eventId:N}";
}
