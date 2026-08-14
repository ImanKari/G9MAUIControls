using G9MAUIControls.Icons;

namespace G9MAUIControls.Controls;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using G9MAUIControls.Helpers;
/// <summary>
///     Data type for a single G9PlusButton menu item: icon (G9IconSource) and text.
/// </summary>
public partial class G9PlusButtonItem : ObservableObject
{
    public required G9IconSource Icon { get; init; }
    public required string Text { get; init; }

    /// <summary>
    ///     Indicates whether selecting this item should close the plus menu.
    /// </summary>
    public bool CloseMenuOnSelect { get; init; } = true;

    public required Func<G9PlusButtonItem, Task> OnSelected { get; init; }

    internal Func<Task>? CloseMenuAsync { get; set; }

    [RelayCommand]
    private async Task Selected(G9PlusButtonItem item)
    {
        await G9SafeCommand.RunAsync(async () =>
        {
            await OnSelected.Invoke(item);

            if (item is { CloseMenuOnSelect: true, CloseMenuAsync: not null }) await item.CloseMenuAsync.Invoke();
        }, new G9SafeCommandOptions
        {
            Source = nameof(G9PlusButtonItem),
            ShowErrorG9Popup = true
        });
    }
}