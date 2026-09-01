using GeometryDashPlace.Web.Components.Editor;
using GeometryDashPlace.Web.Components.Editor.State;
using Microsoft.AspNetCore.Components;

namespace GeometryDashPlace.Web.Components.Pages;

public partial class Home : ComponentBase, IDisposable
{
    protected EditorSession Editor { get; } = new(EditorObjectCatalog.All);

    protected override void OnInitialized()
    {
        Editor.Changed += HandleEditorChanged;
    }

    public void Dispose()
    {
        Editor.Changed -= HandleEditorChanged;
    }

    private void HandleEditorChanged()
    {
        _ = InvokeAsync(StateHasChanged);
    }
}
