using System.Windows;
using System.Windows.Controls;
using DriftDeck.Services;

namespace DriftDeck;

internal sealed class LayoutNameWindow : Window
{
    public string LayoutName { get; private set; } = string.Empty;

    public LayoutNameWindow(string currentName, LayoutStore store)
    {
        Title = "Save layout as";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "SurfaceBaseBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SetResourceReference(FontFamilyProperty, "BodyFont");
        var content = new StackPanel { Margin = new Thickness(20) };
        var name = new TextBox { Text = store.SuggestCopyName(currentName), Margin = new Thickness(0, 8, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(name, "New layout name");
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 12, 8, 0) };
        var save = new Button { Content = "Create layout", IsDefault = true, Margin = new Thickness(0, 12, 0, 0) };
        save.Click += (_, _) =>
        {
            var normalized = LayoutStore.NormalizeName(name.Text);
            if (string.IsNullOrWhiteSpace(name.Text) || normalized != name.Text.Trim())
            {
                error.Text = "Use a name of up to 40 characters without filename symbols.";
                return;
            }
            if (store.Exists(normalized))
            {
                error.Text = "That name is already in use. Choose another name.";
                return;
            }
            LayoutName = normalized;
            DialogResult = true;
        };
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        content.Children.Add(new TextBlock { Text = "New layout name" });
        content.Children.Add(name);
        content.Children.Add(error);
        content.Children.Add(actions);
        Content = content;
        Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
    }
}
