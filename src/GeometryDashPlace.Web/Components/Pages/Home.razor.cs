using GeometryDashPlace.Web.Auth;
using GeometryDashPlace.Web.Components.Editor;
using GeometryDashPlace.Web.Components.Editor.State;
using GeometryDashPlace.Web.Events;
using GeometryDashPlace.Web.Persistence;
using GeometryDashPlace.Web.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace GeometryDashPlace.Web.Components.Pages;

public partial class Home : ComponentBase, IDisposable
{
    [Inject]
    private ILevelEventRepository EventRepository { get; set; } = default!;

    [Inject]
    private ILevelRepository LevelRepository { get; set; } = default!;

    [Inject]
    private LevelRealtimeService Realtime { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private ILogger<Home> Logger { get; set; } = default!;

    protected EditorSession Editor { get; } = new(EditorObjectCatalog.All);
    protected EditorCooldownState Cooldown { get; } = new();
    protected EditorPersistenceActions Actions { get; }
    protected LevelEvent? CurrentEvent { get; private set; }
    protected bool IsAuthenticated { get; private set; }
    protected bool IsLoading { get; private set; } = true;
    protected bool IsSaving { get; private set; }
    protected string? UserDisplayName { get; private set; }
    protected string? StatusMessage { get; private set; }
    private Guid? _userId;
    private readonly CancellationTokenSource _lifetime = new();
    private IDisposable? _levelSubscription;
    private long _levelRevision;

    public Home()
    {
        Actions = new EditorPersistenceActions(
            ConfirmPlacementAsync, DeleteSelectedObjectAsync, CanPersist);
    }

    protected override async Task OnInitializedAsync()
    {
        Editor.Changed += HandleEditorChanged;
        try
        {
            var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            if (authenticationState.User.Identity?.IsAuthenticated is true &&
                AuthenticatedUser.TryGetUserId(authenticationState.User, out var userId))
            {
                IsAuthenticated = true;
                _userId = userId;
                UserDisplayName = authenticationState.User.Identity?.Name;
            }

            CurrentEvent = await EventRepository.GetCurrentAsync();
            if (CurrentEvent is null)
            {
                return;
            }

            if (CurrentEvent.Width != EditorSession.ColumnCount ||
                CurrentEvent.Height != EditorSession.RowCount)
            {
                StatusMessage = "The active event dimensions are not supported by this editor.";
                CurrentEvent = null;
                return;
            }

            _levelSubscription = Realtime.Subscribe(
                CurrentEvent.Id, HandleLevelChangedAsync);
            await ReloadLevelSafelyAsync();
            if (IsAuthenticated && _userId is { } authenticatedUserId)
            {
                var cooldown = await LevelRepository.GetCooldownAsync(
                    CurrentEvent.Id, authenticatedUserId);
                Cooldown.Synchronize(cooldown.ServerTime, cooldown.NextPlacementAt);
                _ = RunCooldownClockAsync(_lifetime.Token);
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Unable to load the active level.");
            StatusMessage = "Unable to load the level.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _levelSubscription?.Dispose();
        Editor.Changed -= HandleEditorChanged;
    }

    private void HandleEditorChanged()
    {
        _ = InvokeAsync(StateHasChanged);
    }

    private async Task ConfirmPlacementAsync()
    {
        var placement = Editor.CreateConfirmedPlacementSnapshot();
        if (placement is null || !CanPersist())
        {
            return;
        }

        await ExecuteMutationAsync(async () =>
        {
            var requestId = Guid.NewGuid();
            var colorTrigger = placement.Type is "bg_color_trigger" or "g1_color_trigger";
            LevelMutation result;
            EditorCell? sourceCell = null;
            if (Editor.TryGetEditingCell(out var source) &&
                (source.X != placement.X || source.Y != placement.Y))
            {
                sourceCell = source;
                result = await LevelRepository.MoveAsync(
                    CurrentEvent!.Id,
                    _userId!.Value,
                    source.X,
                    source.Y,
                    new MoveLevelCellRequest(
                        requestId, placement.X, placement.Y, placement.Type,
                        placement.Rotation, placement.ScaleX, placement.ScaleY,
                        colorTrigger ? placement.Red : null,
                        colorTrigger ? placement.Green : null,
                        colorTrigger ? placement.Blue : null,
                        colorTrigger ? placement.Duration : null));
            }
            else
            {
                result = await LevelRepository.PlaceAsync(
                    CurrentEvent!.Id,
                    _userId!.Value,
                    placement.X,
                    placement.Y,
                    new PlaceLevelCellRequest(
                        requestId, placement.Type, placement.Rotation,
                        placement.ScaleX, placement.ScaleY,
                        colorTrigger ? placement.Red : null,
                        colorTrigger ? placement.Green : null,
                        colorTrigger ? placement.Blue : null,
                        colorTrigger ? placement.Duration : null));
            }

            Editor.ConfirmPlacement();
            await AcceptMutationAsync(
                result, new EditorCell(placement.X, placement.Y), sourceCell);
        });
    }

    private async Task DeleteSelectedObjectAsync()
    {
        if (!CanPersist() || !Editor.TryGetEditingCell(out var cell))
        {
            return;
        }

        await ExecuteMutationAsync(async () =>
        {
            var result = await LevelRepository.DeleteAsync(
                CurrentEvent!.Id,
                _userId!.Value,
                cell.X,
                cell.Y,
                new DeleteLevelCellRequest(Guid.NewGuid()));
            Editor.DeleteSelectedObject();
            await AcceptMutationAsync(result, cell);
        });
    }

    private async Task AcceptMutationAsync(
        LevelMutation result,
        EditorCell target,
        EditorCell? source = null)
    {
        _levelRevision = Math.Max(_levelRevision, result.Revision);
        Cooldown.SetNextActionAt(result.NextPlacementAt);
        if (result.IsReplay)
        {
            return;
        }

        await Realtime.PublishAsync(new LevelChange(
            CurrentEvent!.Id,
            _userId!.Value,
            result.Action,
            result.Revision,
            target.X,
            target.Y,
            source?.X,
            source?.Y,
            result.NextPlacementAt,
            result.Cell));
    }

    private Task HandleLevelChangedAsync(LevelChange change) => InvokeAsync(async () =>
    {
        if (CurrentEvent is null || change.EventId != CurrentEvent.Id)
        {
            return;
        }

        if (change.ActorUserId == _userId)
        {
            Cooldown.SetNextActionAt(change.NextPlacementAt);
        }

        if (change.Revision <= _levelRevision)
        {
            return;
        }

        if (change.Revision != _levelRevision + 1)
        {
            await ReloadLevelSafelyAsync(preserveDraft: true);
            return;
        }

        var source = change.SourceX is { } sourceX && change.SourceY is { } sourceY
            ? new EditorCell(sourceX, sourceY)
            : (EditorCell?)null;
        Editor.ApplyConfirmedObject(
            new EditorCell(change.X, change.Y),
            change.Cell is null ? null : ToEditorObject(change.Cell),
            source);
        _levelRevision = change.Revision;
        StateHasChanged();
    });

    private async Task RunCooldownClockAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ExecuteMutationAsync(Func<Task> mutation)
    {
        if (IsSaving)
        {
            return;
        }

        IsSaving = true;
        StatusMessage = null;
        await InvokeAsync(StateHasChanged);
        try
        {
            await mutation();
        }
        catch (LevelPersistenceException exception)
        {
            if (exception.RetryAt is { } nextActionAt)
            {
                Cooldown.SetNextActionAt(nextActionAt);
            }
            StatusMessage = exception.RetryAt is { } retryAt
                ? $"{exception.Message} Try again at {retryAt.LocalDateTime:T}."
                : exception.Message;
            await ReloadLevelSafelyAsync();
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Unable to persist an editor mutation.");
            StatusMessage = "Unable to save the change.";
            await ReloadLevelSafelyAsync();
        }
        finally
        {
            IsSaving = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task ReloadLevelAsync(bool preserveDraft = false)
    {
        if (CurrentEvent is null)
        {
            return;
        }

        var state = await LevelRepository.LoadAsync(CurrentEvent.Id);
        _levelRevision = state.Revision;
        var objects = state.Cells.Select(ToEditorObject);
        if (preserveDraft)
        {
            Editor.SynchronizeConfirmedObjects(objects);
        }
        else
        {
            Editor.LoadConfirmedObjects(objects);
        }
    }

    private async Task ReloadLevelSafelyAsync(bool preserveDraft = false)
    {
        try
        {
            await ReloadLevelAsync(preserveDraft);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Unable to reload the level after a failed mutation.");
        }
    }

    private static EditorObjectInstance ToEditorObject(LevelCell cell) => new()
    {
        Type = cell.Type,
        X = cell.X,
        Y = cell.Y,
        Rotation = cell.Rotation,
        ScaleX = cell.ScaleX,
        ScaleY = cell.ScaleY,
        Red = cell.Red ?? 255,
        Green = cell.Green ?? 255,
        Blue = cell.Blue ?? 255,
        Duration = cell.Duration ?? 0.2
    };

    private bool CanPersist() =>
        IsAuthenticated &&
        _userId is not null &&
        CurrentEvent is not null &&
        !IsSaving &&
        Cooldown.IsReady;
}
