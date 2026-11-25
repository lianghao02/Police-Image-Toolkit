using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PoliceImageToolkit.Services;

namespace PoliceImageToolkit.Views;

internal static class ImagePreviewWindowHelper
{
    public static Window? ShowOrUpdate(
        Window? existingWindow,
        DependencyObject ownerControl,
        string imagePath,
        string windowTitle,
        string rightHintSuffix,
        double defaultWidth,
        double defaultHeight,
        Action onClosed)
    {
        BitmapSource bitmap = ImageService.LoadOrientedBitmap(imagePath);

        var image = new Image { Source = bitmap, Stretch = Stretch.Uniform };
        var scroll = new ScrollViewer
        {
            Content = image,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = GetBrush("PhoneScreenColor", Color.FromRgb(15, 23, 42))
        };

        string groupName = $"Zoom_{Guid.NewGuid():N}";
        var radioStyle = Application.Current?.TryFindResource("SegmentedRadioStyle") as Style;
        var fitBtn = new RadioButton { Content = "符合視窗", IsChecked = true, GroupName = groupName, Margin = new Thickness(0, 0, 6, 0), Style = radioStyle };
        var actualBtn = new RadioButton { Content = "100% 原尺寸", GroupName = groupName, Margin = new Thickness(0, 0, 12, 0), Style = radioStyle };
        fitBtn.Checked += (_, _) =>
        {
            image.Stretch = Stretch.Uniform;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        };
        actualBtn.Checked += (_, _) =>
        {
            image.Stretch = Stretch.None;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        };

        var topBar = new Border
        {
            Background = GetBrush("BgInset", Color.FromRgb(238, 245, 252)),
            Padding = new Thickness(12, 8, 12, 8),
            BorderBrush = GetBrush("InkSoft", Color.FromRgb(36, 54, 86)),
            BorderThickness = new Thickness(0, 0, 0, 2)
        };
        var dock = new DockPanel();
        var hint = new TextBlock
        {
            Text = $"{bitmap.PixelWidth} × {bitmap.PixelHeight}{rightHintSuffix}",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = GetBrush("TextSecondary", Color.FromRgb(74, 95, 127))
        };
        DockPanel.SetDock(hint, Dock.Right);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(fitBtn);
        controls.Children.Add(actualBtn);
        dock.Children.Add(hint);
        dock.Children.Add(controls);
        topBar.Child = dock;

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(topBar, 0);
        Grid.SetRow(scroll, 1);
        layout.Children.Add(topBar);
        layout.Children.Add(scroll);

        if (existingWindow != null)
        {
            existingWindow.Title = windowTitle;
            existingWindow.Content = layout;
            existingWindow.Activate();
            return existingWindow;
        }

        var window = new Window
        {
            Title = windowTitle,
            Width = defaultWidth,
            Height = defaultHeight,
            Content = layout,
            Owner = Window.GetWindow(ownerControl),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        window.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                window.Close();
                args.Handled = true;
            }
        };
        window.Closed += (_, _) => onClosed();
        window.Show();
        return window;
    }

    private static Brush GetBrush(string resourceKey, Color fallback)
    {
        if (Application.Current?.TryFindResource(resourceKey) is Brush brush)
        {
            return brush;
        }
        var solid = new SolidColorBrush(fallback);
        solid.Freeze();
        return solid;
    }
}
