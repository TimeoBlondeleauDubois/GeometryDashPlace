namespace GeometryDashPlace.Web.Components.Editor.State;

public sealed record EditorRenderObject(
    string CatalogType,
    int X,
    int Y,
    double Rotation,
    double ScaleX,
    double ScaleY,
    double Opacity);

public sealed record FreeRotationGuide(int X, int Y, double Rotation, double RadiusCells);

public sealed record EditorRenderSnapshot(
    double Width,
    double Height,
    double BaseCellSize,
    double CellSize,
    double GroundBaseline,
    double OffsetX,
    double OffsetY,
    int ColumnCount,
    int RowCount,
    int GroundTileCells,
    int ObjectTextureUnit,
    IReadOnlyList<EditorRenderObject> Objects,
    EditorCell? HoverCell,
    EditorCell? SelectedCell,
    FreeRotationGuide? RotationGuide);
