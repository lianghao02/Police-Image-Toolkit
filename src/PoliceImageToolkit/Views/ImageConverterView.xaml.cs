using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PoliceImageToolkit.Models;
using PoliceImageToolkit.ViewModels;

namespace PoliceImageToolkit.Views;

public partial class ImageConverterView : UserControl
{
    private Window? _previewWindow;

    public ImageConverterView()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
            _previewWindow?.Close();
            _previewWindow = null;
        };
    }

    private void Thumbnail_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ImageTaskItem item }) return;
        string path = !string.IsNullOrWhiteSpace(item.OutputPath) && File.Exists(item.OutputPath)
            ? item.OutputPath
            : item.FilePath;
        if (!File.Exists(path)) return;

        try
        {
            _previewWindow = ImagePreviewWindowHelper.ShowOrUpdate(
                _previewWindow,
                this,
                path,
                $"{Path.GetFileName(path)}｜圖片預覽",
                " · 按 Esc 關閉",
                760,
                680,
                () => _previewWindow = null);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"無法預覽圖片：{ex.Message}", "圖片預覽");
        }
    }

    private void UserControl_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void UserControl_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                if (DataContext is ImageConverterViewModel vm)
                {
                    vm.AddFiles(files);
                }
            }
        }
    }
}
