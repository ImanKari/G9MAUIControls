namespace G9MAUIControls.Popup;

public sealed record G9PopupResult(
    G9PopupResultAction Action,
    G9PopupDescriptor? NextG9Popup = null,
    Func<Task>? AfterCloseAsync = null)
{
    public static G9PopupResult Close(Func<Task>? afterCloseAsync = null)
    {
        return new G9PopupResult(G9PopupResultAction.CloseG9Popup, null, afterCloseAsync);
    }

    public static G9PopupResult NoAction()
    {
        return new G9PopupResult(G9PopupResultAction.DoNothing);
    }

    public static G9PopupResult ShowNext(G9PopupDescriptor descriptor)
    {
        return new G9PopupResult(G9PopupResultAction.ShowNext, descriptor);
    }
}