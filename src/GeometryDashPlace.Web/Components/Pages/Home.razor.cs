using GeometryDashPlace.Web.Auth;
using GeometryDashPlace.Web.Components.Editor;
using GeometryDashPlace.Web.Components.Editor.State;
using GeometryDashPlace.Web.Events;
using GeometryDashPlace.Web.Assets;
using GeometryDashPlace.Web.Persistence;
using GeometryDashPlace.Web.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace GeometryDashPlace.Web.Components.Pages;

public partial class Home : ComponentBase, IDisposable
{
    private static readonly TimeSpan PreviewPublishInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan PreviewHeartbeatInterval = TimeSpan.FromSeconds(1);

    [Inject]
    private ILevelEventRepository EventRepository { get; set; } = default!;

    [Inject]
    private ILevelRepository LevelRepository { get; set; } = default!;

    [Inject]
    private LevelRealtimeService Realtime { get; set; } = default!;

    [Inject]
    private EditorCircuitPresence CircuitPresence { get; set; } = default!;

    [Inject]
    private EventLifecycleNotifier EventLifecycle { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    protected EnvironmentAssetCatalog EnvironmentAssets { get; set; } = default!;

    [Inject]
    private ILogger<Home> Logger { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    protected EditorSession Editor { get; } = new(EditorObjectCatalog.All);
    protected EditorCooldownState Cooldown { get; } = new();
    protected EditorPersistenceActions Actions { get; }
    protected LevelEvent? CurrentEvent { get; private set; }
    protected bool IsAuthenticated { get; private set; }
    protected bool IsLoading { get; private set; } = true;
    protected bool IsSaving { get; private set; }
    protected string? StatusMessage { get; private set; }
    private Guid? _userId;
    private readonly CancellationTokenSource _lifetime = new();
    private IDisposable? _levelSubscription;
    private IDisposable? _previewSubscription;
    private IDisposable? _eventLifecycleSubscription;
    private PlacementPreview? _lastPublishedPreview;
    private DateTimeOffset _lastPreviewPublishedAt;
    private PendingRecentPlacement? _pendingRecentPlacement;
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
            }

            CurrentEvent = await EventRepository.GetCurrentAsync();
            if (CurrentEvent is null)
            {
                Navigation.NavigateTo("/events", replace: true);
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
            _previewSubscription = Realtime.SubscribeToPreviews(
                CurrentEvent.Id, HandlePlacementPreviewAsync);
            _eventLifecycleSubscription = EventLifecycle.Subscribe(
                HandleEventLifecycleChangedAsync);
            await ReloadLevelSafelyAsync();
            if (IsAuthenticated && _userId is { } authenticatedUserId)
            {
                CircuitPresence.Track(CurrentEvent.Id, authenticatedUserId);
                var cooldown = await LevelRepository.GetCooldownAsync(
                    CurrentEvent.Id, authenticatedUserId);
                Cooldown.Synchronize(cooldown.ServerTime, cooldown.NextPlacementAt);
                _ = RunCooldownClockAsync(_lifetime.Token);
                _ = RunPlacementPreviewClockAsync(_lifetime.Token);
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
        if (_lastPublishedPreview?.IsActive is true)
        {
            _ = Realtime.PublishPreviewAsync(
                _lastPublishedPreview with { IsActive = false, Type = null });
        }
        CircuitPresence.StopTracking();
        _levelSubscription?.Dispose();
        _previewSubscription?.Dispose();
        _eventLifecycleSubscription?.Dispose();
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

        EditorCell? sourceCell = null;
        if (Editor.TryGetEditingCell(out var source) &&
            (source.X != placement.X || source.Y != placement.Y))
        {
            sourceCell = source;
        }

        await PersistPlacementAsync(
            placement, sourceCell, Guid.NewGuid(), confirmRecentOverwrite: false);
    }

    private async Task PersistPlacementAsync(
        EditorObjectInstance placement,
        EditorCell? sourceCell,
        Guid requestId,
        bool confirmRecentOverwrite)
    {
        await ExecuteMutationAsync(async () =>
        {
            LevelMutation? result = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    result = await SendPlacementMutationAsync(
                        placement,
                        sourceCell,
                        requestId,
                        confirmRecentOverwrite);
                    break;
                }
                catch (LevelPersistenceException exception) when (
                    exception.Code == "concurrent_update" && attempt == 0)
                {
                    await ReloadLevelSafelyAsync(preserveDraft: true);
                }
                catch (LevelPersistenceException exception) when (
                    exception.Code == "recent_cell_conflict" && !confirmRecentOverwrite)
                {
                    _pendingRecentPlacement = new PendingRecentPlacement(
                        placement.Clone(), sourceCell, requestId);
                    await ReloadLevelSafelyAsync(preserveDraft: true);
                    return;
                }
            }

            if (result is null)
            {
                throw new InvalidOperationException(
                    "The placement did not produce a result.");
            }
            _pendingRecentPlacement = null;
            Editor.ConfirmPlacement();
            await AcceptMutationAsync(
                result, new EditorCell(placement.X, placement.Y), sourceCell);
        });
    }

    private Task<LevelMutation> SendPlacementMutationAsync(
        EditorObjectInstance placement,
        EditorCell? sourceCell,
        Guid requestId,
        bool confirmRecentOverwrite)
    {
        var colorTrigger = placement.Type is "bg_color_trigger" or "g1_color_trigger";
        if (sourceCell is { } source)
        {
            return LevelRepository.MoveAsync(
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
                    colorTrigger ? placement.Duration : null,
                    ConfirmRecentOverwrite: confirmRecentOverwrite));
        }

        return LevelRepository.PlaceAsync(
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
                colorTrigger ? placement.Duration : null,
                ConfirmRecentOverwrite: confirmRecentOverwrite));
    }

    private async Task ConfirmRecentOverwriteAsync()
    {
        var pending = _pendingRecentPlacement;
        if (pending is null)
        {
            return;
        }

        _pendingRecentPlacement = null;
        await PersistPlacementAsync(
            pending.Placement,
            pending.Source,
            pending.RequestId,
            confirmRecentOverwrite: true);
    }

    private void CancelRecentOverwrite()
    {
        _pendingRecentPlacement = null;
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

        if (change.Action == "moderation_restore")
        {
            await ReloadLevelSafelyAsync(preserveDraft: true);
            return;
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

    private Task HandlePlacementPreviewAsync(PlacementPreview preview) => InvokeAsync(() =>
    {
        if (CurrentEvent is null ||
            preview.EventId != CurrentEvent.Id ||
            preview.ActorUserId == _userId)
        {
            return;
        }

        Editor.ApplyRemotePreview(
            preview.ActorUserId,
            preview.IsActive && preview.Type is not null
                ? ToEditorObject(preview)
                : null);
    });

    private async Task PublishPlacementPreviewIfNeededAsync()
    {
        if (_lifetime.IsCancellationRequested ||
            !CircuitPresence.IsConnected ||
            !IsAuthenticated ||
            _userId is not { } userId ||
            CurrentEvent is null)
        {
            return;
        }

        var pending = Editor.PendingObject;
        var preview = pending is null
            ? new PlacementPreview(CurrentEvent.Id, userId, IsActive: false)
            : new PlacementPreview(
                CurrentEvent.Id,
                userId,
                IsActive: true,
                pending.Type,
                pending.X,
                pending.Y,
                pending.Rotation,
                pending.ScaleX,
                pending.ScaleY,
                pending.Red,
                pending.Green,
                pending.Blue,
                pending.Duration);
        var now = DateTimeOffset.UtcNow;
        var changed = preview != _lastPublishedPreview;
        var heartbeatDue = preview.IsActive &&
            !changed &&
            now - _lastPreviewPublishedAt >= PreviewHeartbeatInterval;
        if ((!changed && !heartbeatDue) ||
            !preview.IsActive && _lastPublishedPreview is null)
        {
            return;
        }

        try
        {
            await Realtime.PublishPreviewAsync(preview);
            _lastPublishedPreview = preview;
            _lastPreviewPublishedAt = now;
        }
        catch (Exception exception)
        {
            Logger.LogDebug(exception, "Unable to publish the placement preview.");
        }
    }

    private Task HandleEventLifecycleChangedAsync() => InvokeAsync(async () =>
    {
        var current = await EventRepository.GetCurrentAsync();
        if (current?.Id == CurrentEvent?.Id &&
            current?.BackgroundKey == CurrentEvent?.BackgroundKey &&
            current?.GroundKey == CurrentEvent?.GroundKey)
        {
            return;
        }

        Navigation.NavigateTo(
            current is null ? "/events" : "/",
            forceLoad: true,
            replace: true);
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

    private async Task RunPlacementPreviewClockAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PreviewPublishInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(PublishPlacementPreviewIfNeededAsync);
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

    private static EditorObjectInstance ToEditorObject(PlacementPreview preview) => new()
    {
        Type = preview.Type!,
        X = preview.X,
        Y = preview.Y,
        Rotation = preview.Rotation,
        ScaleX = preview.ScaleX,
        ScaleY = preview.ScaleY,
        Red = preview.Red,
        Green = preview.Green,
        Blue = preview.Blue,
        Duration = preview.Duration
    };

    private bool CanPersist() =>
        IsAuthenticated &&
        _userId is not null &&
        CurrentEvent is not null &&
        !IsSaving &&
        Cooldown.IsReady;

    private sealed record PendingRecentPlacement(
        EditorObjectInstance Placement,
        EditorCell? Source,
        Guid RequestId);
}
