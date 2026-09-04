using GeometryDashPlace.Web.Components.Editor;
using GeometryDashPlace.Web.Components.Editor.State;

namespace GeometryDashPlace.Web.Tests;

public sealed class EditorSessionTests
{
    [Fact]
    public void BuildWithoutCatalogSelection_IgnoresEmptyAndOccupiedCells()
    {
        var editor = CreateEditor();
        editor.LoadConfirmedObjects([Placed("block", 2, 3)]);

        ClickCell(editor, 4, 3);
        ClickCell(editor, 2, 3);

        Assert.Null(editor.PendingObject);
        Assert.False(editor.TryGetEditingCell(out _));
        Assert.Equal(1, editor.ObjectCount);
        Assert.Equal(EditorMode.Build, editor.Mode);
    }

    [Fact]
    public void BuildSelection_ReplacesExistingObjectAndRemainsArmed()
    {
        var editor = CreateEditor();
        editor.LoadConfirmedObjects([Placed("block", 2, 3)]);
        editor.SelectCatalogObject("spike");

        ClickCell(editor, 2, 3);

        Assert.Equal("spike", editor.PendingObject?.Type);
        Assert.True(editor.TryGetEditingCell(out var editedCell));
        Assert.Equal(new EditorCell(2, 3), editedCell);
        Assert.True(editor.CanValidate);

        editor.ConfirmPlacement();

        var rendered = Assert.Single(editor.CreateRenderSnapshot().Objects);
        Assert.Equal("spike", rendered.CatalogType);
        Assert.Equal(EditorMode.Build, editor.Mode);
        Assert.True(editor.BuildObjectArmed);
        Assert.Equal(1, editor.ObjectCount);
    }

    [Fact]
    public void EditAndDelete_RequireAnOccupiedCell()
    {
        var editor = CreateEditor();
        editor.LoadConfirmedObjects([Placed("block", 2, 3)]);
        editor.SetMode(EditorMode.Edit);

        ClickCell(editor, 4, 3);
        Assert.Null(editor.PendingObject);

        ClickCell(editor, 2, 3);
        Assert.NotNull(editor.PendingObject);
        Assert.Equal(EditorMode.Edit, editor.Mode);

        editor.SetMode(EditorMode.Delete);
        ClickCell(editor, 4, 3);
        Assert.False(editor.CanDelete);

        ClickCell(editor, 2, 3);
        Assert.True(editor.CanDelete);

        editor.DeleteSelectedObject();

        Assert.Equal(0, editor.ObjectCount);
        Assert.Equal(EditorMode.Delete, editor.Mode);
    }

    [Fact]
    public void ObjectTransforms_RespectRotationModeAndScaleBounds()
    {
        var editor = CreateEditor();
        editor.LoadConfirmedObjects([Placed("block", 2, 3, rotation: 47)]);
        editor.SetMode(EditorMode.Edit);
        ClickCell(editor, 2, 3);

        Assert.True(editor.CanRotate);
        Assert.False(editor.CanFreeRotate);
        Assert.Equal(90, editor.PendingObject?.Rotation);

        editor.SetPendingRotation("33");
        Assert.Equal(90, editor.PendingObject?.Rotation);

        editor.RotatePendingObject(90);
        editor.SetPendingScaleX("9");
        editor.SetPendingScaleY("-2");

        Assert.Equal(180, editor.PendingObject?.Rotation);
        Assert.Equal(EditorSession.MaximumObjectScale, editor.PendingObject?.ScaleX);
        Assert.Equal(EditorSession.MinimumObjectScale, editor.PendingObject?.ScaleY);
    }

    [Fact]
    public void FreeRotation_NormalizesNumericInput()
    {
        var editor = CreateEditor();
        editor.SelectCatalogObject("spike");
        ClickCell(editor, 5, 2);

        editor.SetPendingRotation("-15");

        Assert.True(editor.CanFreeRotate);
        Assert.Equal(345, editor.PendingObject?.Rotation);
    }

    [Fact]
    public void ColorTrigger_ProducesTheSelectedDatabaseTypeAndSettings()
    {
        var editor = CreateEditor();
        editor.SelectCatalogObject("color_trigger");
        ClickCell(editor, 5, 2);

        editor.SetColorTarget("ground");
        editor.SetColorHex("1E23CD");
        editor.SetColorDuration("-2");
        var placement = editor.CreateConfirmedPlacementSnapshot();

        Assert.NotNull(placement);
        Assert.Equal("g1_color_trigger", placement.Type);
        Assert.Equal(30, placement.Red);
        Assert.Equal(35, placement.Green);
        Assert.Equal(205, placement.Blue);
        Assert.Equal(0, placement.Duration);
        Assert.False(editor.CanRotate);
        Assert.False(editor.CanScale);
    }

    [Fact]
    public void RemoteMove_RemovesSourceAndReplacesTarget()
    {
        var editor = CreateEditor();
        editor.LoadConfirmedObjects(
        [
            Placed("block", 2, 3),
            Placed("spike", 8, 3)
        ]);

        editor.ApplyConfirmedObject(
            new EditorCell(8, 3),
            Placed("yellow_orb", 8, 3),
            new EditorCell(2, 3));

        var rendered = Assert.Single(editor.CreateRenderSnapshot().Objects);
        Assert.Equal("yellow_orb", rendered.CatalogType);
        Assert.Equal(8, rendered.X);
        Assert.Equal(3, rendered.Y);
    }

    [Fact]
    public void RemoteSynchronization_PreservesTheLocalDraft()
    {
        var editor = CreateEditor();
        editor.SelectCatalogObject("spike");
        ClickCell(editor, 5, 2);

        editor.SynchronizeConfirmedObjects([Placed("block", 10, 4)]);

        Assert.Equal("spike", editor.PendingObject?.Type);
        Assert.Equal(new EditorCell(5, 2), editor.SelectedCell);
        Assert.Equal(1, editor.ObjectCount);
        Assert.Equal(2, editor.CreateRenderSnapshot().Objects.Count);
    }

    private static EditorSession CreateEditor()
    {
        var editor = new EditorSession(EditorObjectCatalog.All);
        editor.Resize(1080, 1080);
        return editor;
    }

    private static EditorObjectInstance Placed(
        string type,
        int x,
        int y,
        double rotation = 0) => new()
        {
            Type = type,
            X = x,
            Y = y,
            Rotation = rotation
        };

    private static void ClickCell(EditorSession editor, int x, int y)
    {
        var screenX = (x + 0.5 - editor.OffsetX) * editor.CellSize;
        var screenY = editor.GroundBaseline - (y + 0.5 - editor.OffsetY) * editor.CellSize;
        Assert.True(editor.BeginPointer(1, 0, screenX, screenY));
        editor.EndPointer(1, screenX, screenY);
    }
}
