using System.Globalization;

namespace GeometryDashPlace.Web.Components.Editor.State;

public sealed class EditorSession
{
    public const int ColumnCount = 1024;
    public const int RowCount = 32;
    public const int GroundTileCells = 4;
    public const int ObjectTextureUnit = 120;
    public const int PalettePageSize = 12;
    public const double MinimumZoom = 0.5;
    public const double MaximumZoom = 3;
    public const string BackgroundTexturePath = "/assets/environment/backgrounds/classic-square.png";
    public const string GroundTexturePath = "/assets/environment/grounds/classic-square.png";

    private const string ColorTriggerType = "color_trigger";
    private const string BackgroundColorTriggerType = "bg_color_trigger";
    private const string GroundColorTriggerType = "g1_color_trigger";

    private readonly IReadOnlyList<EditorObjectDefinition> _definitions;
    private readonly IReadOnlyDictionary<string, EditorObjectDefinition> _definitionByType;
    private readonly Dictionary<string, EditorObjectInstance> _objects = [];
    private long? _pointerId;
    private double _pointerX;
    private double _pointerY;
    private double _dragDistance;

    public EditorSession(IReadOnlyList<EditorObjectDefinition> definitions)
    {
        _definitions = definitions;
        _definitionByType = definitions.ToDictionary(definition => definition.Type);
    }

    public event Action? Changed;

    public EditorMode Mode { get; private set; } = EditorMode.Build;
    public double Width { get; private set; }
    public double Height { get; private set; }
    public double BaseCellSize { get; private set; } = 30;
    public double Zoom { get; private set; } = 1;
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }
    public EditorCell? HoverCell { get; private set; }
    public EditorCell? SelectedCell { get; private set; }
    public EditorObjectInstance? PendingObject { get; private set; }
    public string? EditingObjectKey { get; private set; }
    public string? SelectedObjectType { get; private set; }
    public bool BuildObjectArmed { get; private set; }
    public int SelectedRotation { get; private set; }
    public int PalettePage { get; private set; }
    public bool HasPendingObject => PendingObject is not null;
    public bool CanDelete => EditingObjectKey is not null && _objects.ContainsKey(EditingObjectKey);
    public bool CanRotate => PendingObject is not null && SelectedDefinition?.CanRotate is not false;
    public bool IsColorTriggerSelected => PendingObject?.Type == ColorTriggerType;
    public int ObjectCount => _objects.Count;
    public int PalettePageCount => Math.Max(1, (int)Math.Ceiling((double)_definitions.Count / PalettePageSize));
    public string PalettePageText => $"{PalettePage + 1} / {PalettePageCount}";
    public double CellSize => BaseCellSize * Zoom;
    public double GroundBaseline => Height - GroundTileCells * BaseCellSize;
    public double TimelineProgress => Math.Clamp(HorizontalTravelCells() > 0 ? OffsetX / HorizontalTravelCells() : 0, 0, 1);
    public string CoordinateText => HoverCell is { } hover
        ? $"Cell {hover.X}, {hover.Y}"
        : SelectedCell is { } selected ? $"Cell {selected.X}, {selected.Y}" : "Cell —";
    public string ZoomText => $"Zoom {Math.Round(Zoom * 100)} %";
    public string ObjectCountText => $"{ObjectCount} object{(ObjectCount == 1 ? string.Empty : "s")}";
    public string SelectedObjectName => ActiveDefinition?.Name ?? "Select an object";
    public string RotationText => ActiveDefinition is null
        ? "Rotation —"
        : ActiveDefinition.CanRotate ? $"Rotation {SelectedRotation}°" : "Fixed rotation";
    public string ColorTarget => PendingObject?.ColorTarget ?? "background";
    public string ColorHex => PendingObject is null
        ? "#FFFFFF"
        : $"#{PendingObject.Red:X2}{PendingObject.Green:X2}{PendingObject.Blue:X2}";
    public double ColorDuration => PendingObject?.Duration ?? 0.2;
    public bool IsColorHexInvalid { get; private set; }
    public IReadOnlyList<EditorObjectDefinition> Definitions => _definitions;
    public IEnumerable<EditorObjectDefinition> VisibleDefinitions => _definitions
        .Skip(PalettePage * PalettePageSize)
        .Take(PalettePageSize);

    private EditorObjectDefinition? SelectedDefinition => SelectedObjectType is not null &&
        _definitionByType.TryGetValue(SelectedObjectType, out var definition) ? definition : null;

    private EditorObjectDefinition? ActiveDefinition
    {
        get
        {
            var hasActiveObject = Mode == EditorMode.Build ? BuildObjectArmed : PendingObject is not null;
            return hasActiveObject ? SelectedDefinition : null;
        }
    }

    public bool IsObjectSelected(string type)
    {
        var hasActiveObject = Mode == EditorMode.Build ? BuildObjectArmed : PendingObject is not null;
        return hasActiveObject && SelectedObjectType == type;
    }

    public void Resize(double width, double height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        BaseCellSize = Height / (RowCount + GroundTileCells);
        ClampCamera();
        NotifyChanged();
    }

    public void SetMode(EditorMode mode)
    {
        if (mode == EditorMode.Build && Mode != EditorMode.Build)
        {
            ClearPendingSelection(false);
        }

        Mode = mode;
        NotifyChanged();
    }

    public void SelectCatalogObject(string type)
    {
        if (!_definitionByType.ContainsKey(type))
        {
            return;
        }

        SelectedObjectType = type;
        BuildObjectArmed = true;
        SelectedRotation = 0;

        if (PendingObject is not null)
        {
            PendingObject = CreatePendingObject(type, PendingObject.X, PendingObject.Y, 0);
        }

        Mode = EditorMode.Build;
        NotifyChanged();
    }

    public void PreviousPalettePage()
    {
        PalettePage = (PalettePage - 1 + PalettePageCount) % PalettePageCount;
        NotifyChanged();
    }

    public void NextPalettePage()
    {
        PalettePage = (PalettePage + 1) % PalettePageCount;
        NotifyChanged();
    }

    public void MovePendingObject(int deltaX, int deltaY)
    {
        if (PendingObject is null)
        {
            return;
        }

        PendingObject.X = Math.Clamp(PendingObject.X + deltaX, 0, ColumnCount - 1);
        PendingObject.Y = Math.Clamp(PendingObject.Y + deltaY, 0, RowCount - 1);
        SelectedCell = new EditorCell(PendingObject.X, PendingObject.Y);
        NotifyChanged();
    }

    public bool CanMovePendingObject(int deltaX, int deltaY)
    {
        if (PendingObject is null)
        {
            return false;
        }

        var nextX = PendingObject.X + deltaX;
        var nextY = PendingObject.Y + deltaY;
        return nextX >= 0 && nextX < ColumnCount && nextY >= 0 && nextY < RowCount;
    }

    public void RotatePendingObject(int step)
    {
        if (!CanRotate || PendingObject is null)
        {
            return;
        }

        SelectedRotation = (SelectedRotation + step + 360) % 360;
        PendingObject.Rotation = SelectedRotation;
        NotifyChanged();
    }

    public void ConfirmPlacement()
    {
        if (PendingObject is null)
        {
            return;
        }

        var confirmedObject = CreateConfirmedObject(PendingObject);
        if (EditingObjectKey is not null)
        {
            _objects.Remove(EditingObjectKey);
        }

        _objects[CellKey(confirmedObject.X, confirmedObject.Y)] = confirmedObject;
        PendingObject = null;
        EditingObjectKey = null;
        SelectedCell = null;
        Mode = EditorMode.Build;
        NotifyChanged();
    }

    public void DeleteSelectedObject()
    {
        if (EditingObjectKey is null || !_objects.Remove(EditingObjectKey))
        {
            return;
        }

        ClearPendingSelection(false);
        Mode = EditorMode.Edit;
        NotifyChanged();
    }

    public void SetColorTarget(string target)
    {
        if (!IsColorTriggerSelected || PendingObject is null || target is not ("background" or "ground"))
        {
            return;
        }

        PendingObject.ColorTarget = target;
        NotifyChanged();
    }

    public void SetColorHex(string? value)
    {
        var normalized = value?.Trim().TrimStart('#');
        var color = 0;
        var isValid = normalized is not null && normalized.Length == 6 &&
            int.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color);
        IsColorHexInvalid = !isValid;

        if (isValid && PendingObject is not null && IsColorTriggerSelected)
        {
            PendingObject.Red = (color >> 16) & 0xFF;
            PendingObject.Green = (color >> 8) & 0xFF;
            PendingObject.Blue = color & 0xFF;
        }

        NotifyChanged();
    }

    public void SetColorDuration(string? value)
    {
        if (PendingObject is null || !IsColorTriggerSelected ||
            !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
        {
            return;
        }

        PendingObject.Duration = Math.Max(0, duration);
        NotifyChanged();
    }

    public bool BeginPointer(long pointerId, long button, double x, double y)
    {
        if (button is not (0 or 1))
        {
            return false;
        }

        _pointerId = pointerId;
        _pointerX = x;
        _pointerY = y;
        _dragDistance = 0;
        return true;
    }

    public void MovePointer(long pointerId, double x, double y)
    {
        if (_pointerId == pointerId)
        {
            var deltaX = x - _pointerX;
            var deltaY = y - _pointerY;
            _pointerX = x;
            _pointerY = y;
            _dragDistance += Math.Abs(deltaX) + Math.Abs(deltaY);
            OffsetX -= deltaX / CellSize;
            OffsetY += deltaY / CellSize;
            ClampCamera();
        }

        HoverCell = ScreenToCell(x, y);
        NotifyChanged();
    }

    public void EndPointer(long pointerId, double x, double y)
    {
        if (_pointerId != pointerId)
        {
            return;
        }

        if (_dragDistance < 5 && ScreenToCell(x, y) is { } cell)
        {
            HandleCellClick(cell);
        }

        _pointerId = null;
        NotifyChanged();
    }

    public void LeavePointer()
    {
        if (_pointerId is not null)
        {
            return;
        }

        HoverCell = null;
        NotifyChanged();
    }

    public void ZoomAt(double factor, double anchorX) => SetZoom(Zoom * factor, anchorX);
    public void ZoomIn() => SetZoom(Zoom * 1.2, Width / 2);
    public void ZoomOut() => SetZoom(Zoom / 1.2, Width / 2);

    public void SetTimelineProgress(double progress)
    {
        OffsetX = Math.Clamp(progress, 0, 1) * HorizontalTravelCells();
        ClampCamera();
        NotifyChanged();
    }

    public void MoveTimelineByCells(double cellCount)
    {
        var travel = Math.Max(1, HorizontalTravelCells());
        SetTimelineProgress(TimelineProgress + cellCount / travel);
    }

    public EditorRenderSnapshot CreateRenderSnapshot()
    {
        var renderObjects = _objects
            .Where(pair => pair.Key != EditingObjectKey)
            .Select(pair => CreateRenderObject(pair.Value, 1))
            .ToList();

        if (PendingObject is not null)
        {
            renderObjects.Add(CreateRenderObject(PendingObject, 0.62));
        }

        return new EditorRenderSnapshot(
            Width, Height, BaseCellSize, CellSize, GroundBaseline, OffsetX, OffsetY,
            ColumnCount, RowCount, GroundTileCells, ObjectTextureUnit,
            renderObjects, HoverCell, SelectedCell);
    }

    private void HandleCellClick(EditorCell cell)
    {
        var key = CellKey(cell.X, cell.Y);
        var containsPlacedObject = _objects.ContainsKey(key);

        if (Mode == EditorMode.Build && BuildObjectArmed)
        {
            PreparePlacement(cell, containsPlacedObject ? key : null);
        }
        else if (containsPlacedObject)
        {
            Mode = EditorMode.Edit;
            SelectPlacedObject(cell, key);
        }
        else if (Mode == EditorMode.Edit)
        {
            PendingObject = null;
            EditingObjectKey = null;
            SelectedCell = cell;
        }
        else
        {
            SelectedCell = null;
        }
    }

    private void PreparePlacement(EditorCell cell, string? replacedObjectKey)
    {
        if (!BuildObjectArmed || SelectedObjectType is null)
        {
            return;
        }

        EditingObjectKey = replacedObjectKey;
        PendingObject = CreatePendingObject(SelectedObjectType, cell.X, cell.Y, SelectedRotation);
        SelectedCell = cell;
        Mode = EditorMode.Edit;
    }

    private void SelectPlacedObject(EditorCell cell, string key)
    {
        var placedObject = _objects[key];
        EditingObjectKey = key;
        PendingObject = CreateEditableObject(placedObject);
        SelectedObjectType = PendingObject.Type;
        BuildObjectArmed = false;
        SelectedRotation = PendingObject.Rotation;
        SelectedCell = cell;
    }

    private void ClearPendingSelection(bool notify = true)
    {
        PendingObject = null;
        EditingObjectKey = null;
        SelectedCell = null;
        if (notify)
        {
            NotifyChanged();
        }
    }

    private void SetZoom(double zoom, double anchorX)
    {
        var previousSize = CellSize;
        var worldX = OffsetX + anchorX / previousSize;
        var groundScreenY = GridToScreenY(0);
        Zoom = Math.Clamp(zoom, MinimumZoom, MaximumZoom);
        OffsetX = worldX - anchorX / CellSize;
        OffsetY = (groundScreenY - GroundBaseline) / CellSize;
        ClampCamera();
        NotifyChanged();
    }

    private EditorCell? ScreenToCell(double x, double y)
    {
        var column = (int)Math.Floor(OffsetX + x / CellSize);
        var row = (int)Math.Floor(OffsetY + (GroundBaseline - y) / CellSize);
        return column < 0 || column >= ColumnCount || row < 0 || row >= RowCount
            ? null : new EditorCell(column, row);
    }

    private double GridToScreenY(double y) => GroundBaseline - (y - OffsetY) * CellSize;
    private double HorizontalTravelCells() => Math.Max(0, ColumnCount - Width / CellSize);

    private void ClampCamera()
    {
        OffsetX = AxisOffset(OffsetX, ColumnCount, Width / CellSize);
        var visibleRows = GroundBaseline / CellSize;
        OffsetY = visibleRows >= RowCount ? 0 : Math.Clamp(OffsetY, 0, RowCount - visibleRows);
    }

    private static double AxisOffset(double value, double totalCells, double visibleCells) => visibleCells >= totalCells
        ? (totalCells - visibleCells) / 2
        : Math.Clamp(value, 0, totalCells - visibleCells);

    private static EditorRenderObject CreateRenderObject(EditorObjectInstance instance, double opacity) => new(
        CatalogTypeFor(instance.Type), instance.X, instance.Y, instance.Rotation, opacity);

    private static EditorObjectInstance CreatePendingObject(string type, int x, int y, int rotation)
    {
        var pendingObject = new EditorObjectInstance { Type = type, X = x, Y = y, Rotation = rotation };
        if (type == ColorTriggerType)
        {
            pendingObject.ColorTarget = "background";
            pendingObject.Rotation = 0;
        }
        return pendingObject;
    }

    private static EditorObjectInstance CreateEditableObject(EditorObjectInstance confirmedObject)
    {
        var editableObject = confirmedObject.Clone();
        editableObject.Type = CatalogTypeFor(confirmedObject.Type);
        if (editableObject.Type == ColorTriggerType)
        {
            editableObject.ColorTarget = confirmedObject.Type == GroundColorTriggerType ? "ground" : "background";
        }
        return editableObject;
    }

    private static EditorObjectInstance CreateConfirmedObject(EditorObjectInstance pendingObject)
    {
        var confirmedObject = pendingObject.Clone();
        if (confirmedObject.Type == ColorTriggerType)
        {
            confirmedObject.Type = confirmedObject.ColorTarget == "ground"
                ? GroundColorTriggerType : BackgroundColorTriggerType;
            confirmedObject.Red = Math.Clamp(confirmedObject.Red, 0, 255);
            confirmedObject.Green = Math.Clamp(confirmedObject.Green, 0, 255);
            confirmedObject.Blue = Math.Clamp(confirmedObject.Blue, 0, 255);
            confirmedObject.Duration = Math.Max(0, confirmedObject.Duration);
            confirmedObject.ColorTarget = null;
        }
        return confirmedObject;
    }

    private static string CatalogTypeFor(string type) => type is BackgroundColorTriggerType or GroundColorTriggerType
        ? ColorTriggerType : type;
    private static string CellKey(int x, int y) => $"{x}:{y}";
    private void NotifyChanged() => Changed?.Invoke();
}
