namespace GeometryDashPlace.Web.Components.Editor.State;

public sealed record EditorPersistenceActions(
    Func<Task> ConfirmPlacementAsync,
    Func<Task> DeleteSelectedObjectAsync);
