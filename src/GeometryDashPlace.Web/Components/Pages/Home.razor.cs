using GeometryDashPlace.Web.Auth;
using GeometryDashPlace.Web.Components.Editor;
using GeometryDashPlace.Web.Components.Editor.State;
using GeometryDashPlace.Web.Events;
using GeometryDashPlace.Web.Persistence;
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
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private ILogger<Home> Logger { get; set; } = default!;

    protected EditorSession Editor { get; } = new(EditorObjectCatalog.All);
    protected EditorPersistenceActions Actions { get; }
    protected LevelEvent? CurrentEvent { get; private set; }
    protected bool IsAuthenticated { get; private set; }
    protected bool IsLoading { get; private set; } = true;
    protected bool IsSaving { get; private set; }
    protected string? UserDisplayName { get; private set; }
    protected string? StatusMessage { get; private set; }
    private Guid? _userId;

    public Home()
    {
        Actions = new EditorPersistenceActions(ConfirmPlacementAsync, DeleteSelectedObjectAsync);
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

            await ReloadLevelSafelyAsync();
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
            if (Editor.TryGetEditingCell(out var source) &&
                (source.X != placement.X || source.Y != placement.Y))
            {
                await LevelRepository.MoveAsync(
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
                await LevelRepository.PlaceAsync(
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
            await LevelRepository.DeleteAsync(
                CurrentEvent!.Id,
                _userId!.Value,
                cell.X,
                cell.Y,
                new DeleteLevelCellRequest(Guid.NewGuid()));
            Editor.DeleteSelectedObject();
        });
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

    private async Task ReloadLevelAsync()
    {
        if (CurrentEvent is null)
        {
            return;
        }

        var state = await LevelRepository.LoadAsync(CurrentEvent.Id);
        Editor.LoadConfirmedObjects(state.Cells.Select(cell => new EditorObjectInstance
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
        }));
    }

    private async Task ReloadLevelSafelyAsync()
    {
        try
        {
            await ReloadLevelAsync();
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Unable to reload the level after a failed mutation.");
        }
    }

    private bool CanPersist() =>
        IsAuthenticated && _userId is not null && CurrentEvent is not null && !IsSaving;
}
