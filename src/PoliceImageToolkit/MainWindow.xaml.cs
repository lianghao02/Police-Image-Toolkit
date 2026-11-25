using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PoliceImageToolkit.ViewModels;

namespace PoliceImageToolkit;

public partial class MainWindow : Window
{
    private DateTime _lastRepeatStepUtc = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            MinWidth = Math.Min(MinWidth, area.Width);
            MinHeight = Math.Min(MinHeight, area.Height);
            Width = Math.Min(Width, area.Width); Height = Math.Min(Height, area.Height);

            if (ViewModel?.SelectedTabIndex == 1)
            {
                TabVideo.IsChecked = true;
            }
        };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 在輸入框按 Esc 或 Enter 時退出文字輸入焦點，避免接下來按 Space 誤輸入空白。
        if (Keyboard.FocusedElement is TextBox && (e.Key == Key.Escape || e.Key == Key.Return))
        {
            Keyboard.ClearFocus();
            if (TabVideo.IsChecked == true)
            {
                ViewVideoSnapshot.Focus();
            }
            e.Handled = true;
            return;
        }

        // 焦點在文字輸入、下拉選單或滑桿時保留原生按鍵行為（避免方向鍵改選項時同時拖動影片）
        if (Keyboard.FocusedElement is TextBox or ComboBox or ComboBoxItem or Slider)
        {
            return;
        }

        // 焦點在勾選框或按鈕時保留空白鍵 / Enter 原生切換與點擊行為
        if (Keyboard.FocusedElement is CheckBox && e.Key == Key.Space)
        {
            return;
        }

        if (Keyboard.FocusedElement is Button && e.Key == Key.Return)
        {
            return;
        }

        // 僅在影片截圖分頁時啟用快捷鍵
        if (TabVideo.IsChecked == true && ViewModel?.VideoSnapshot != null)
        {
            var vm = ViewModel.VideoSnapshot;

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
            {
                ExecuteShortcut(vm.UndoLastCaptureCommand, e, allowRepeat: false);
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                switch (e.Key)
                {
                    case Key.Left:
                    case Key.A:
                        ExecuteStepShortcut(vm.StepBackwardHalfCommand, e);
                        break;

                    case Key.Right:
                    case Key.D:
                        ExecuteStepShortcut(vm.StepForwardHalfCommand, e);
                        break;
                }
                return;
            }

            if (Keyboard.Modifiers != ModifierKeys.None) return;

            switch (e.Key)
            {
                case Key.Space:
                    ExecuteShortcut(vm.CaptureSnapshotCommand, e, allowRepeat: false);
                    break;

                case Key.Left:
                case Key.A:
                    ExecuteStepShortcut(vm.StepBackwardCommand, e);
                    break;

                case Key.Right:
                case Key.D:
                    ExecuteStepShortcut(vm.StepForwardCommand, e);
                    break;

                case Key.Up:
                case Key.W:
                    ExecuteStepShortcut(vm.StepForward1sCommand, e);
                    break;

                case Key.Down:
                case Key.S:
                    ExecuteStepShortcut(vm.StepBackward1sCommand, e);
                    break;

                case Key.P:
                case Key.Return:
                    ExecuteShortcut(vm.PlayPauseCommand, e, allowRepeat: false);
                    break;
            }
        }
    }

    private void ExecuteStepShortcut(ICommand command, KeyEventArgs e)
    {
        // 一律攔截事件，防止長按方向鍵時焦點在按鈕列左右移動。
        e.Handled = true;
        if (e.IsRepeat)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastRepeatStepUtc).TotalMilliseconds < 75) return;
            _lastRepeatStepUtc = now;
        }
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private static void ExecuteShortcut(ICommand command, KeyEventArgs e, bool allowRepeat)
    {
        // 即使暫時不可執行，也必須攔截按鍵避免 Space 落入目前焦點按鈕引發誤按。
        e.Handled = true;
        if (!allowRepeat && e.IsRepeat) return;
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private void TabImage_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewImageConverter != null && ViewVideoSnapshot != null)
        {
            if (ViewModel != null)
            {
                if (ViewModel.VideoSnapshot.IsPlaying)
                {
                    ViewModel.VideoSnapshot.ExecutePlayPause();
                }
                ViewModel.SelectedTabIndex = 0;
            }
            ViewImageConverter.Visibility = Visibility.Visible;
            ViewVideoSnapshot.Visibility = Visibility.Collapsed;
        }
    }

    private void TabVideo_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewImageConverter != null && ViewVideoSnapshot != null)
        {
            if (ViewModel != null)
            {
                ViewModel.SelectedTabIndex = 1;
            }
            ViewImageConverter.Visibility = Visibility.Collapsed;
            ViewVideoSnapshot.Visibility = Visibility.Visible;
        }
    }
}
