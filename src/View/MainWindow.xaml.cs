using IcoConverter.Models;
using IcoConverter.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace IcoConverter;

public partial class MainWindow
{
    private MainViewModel? _boundViewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.ServiceProvider.GetRequiredService<MainViewModel>();

        PreviewKeyDown += OnWindowPreviewKeyDown;

        Loaded += OnWindowLoaded;

        // 从主窗口的 ViewModel 同步弹出菜单的勾选状态。
        AttachViewModel(DataContext as MainViewModel);
        DataContextChanged += (_, e) => AttachViewModel(e.NewValue as MainViewModel);

        // 窗口关闭时解除挂接，否则单例 ViewModel 会持续持有已释放窗口的引用
        Closed += (_, _) => DetachViewModel();
    }

    private void AttachViewModel(MainViewModel? viewModel)
    {
        if (viewModel == null || ReferenceEquals(_boundViewModel, viewModel))
        {
            return;
        }

        if (_boundViewModel != null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _boundViewModel = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void DetachViewModel()
    {
        if (_boundViewModel == null)
        {
            return;
        }

        _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _boundViewModel = null;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as MainViewModel);

        SyncMaterialMenu();
        SyncPreviewBackgroundMenu();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsMicaMaterial)
            or nameof(MainViewModel.IsAcrylicMaterial)
            or nameof(MainViewModel.IsSolidMaterial))
        {
            SyncMaterialMenu();
        }

        if (e.PropertyName is nameof(MainViewModel.PreviewBackground))
        {
            SyncPreviewBackgroundMenu();
        }
    }

    private void SyncPreviewBackgroundMenu()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        NoneBackgroundItem.IsChecked = viewModel.PreviewBackground == PreviewBackgroundMode.None;
        CheckerBackgroundItem.IsChecked = viewModel.PreviewBackground == PreviewBackgroundMode.Checker;
        WhiteBackgroundItem.IsChecked = viewModel.PreviewBackground == PreviewBackgroundMode.White;
        BlackBackgroundItem.IsChecked = viewModel.PreviewBackground == PreviewBackgroundMode.Black;
    }

    private void OnPreviewBackgroundNoneClick(object sender, RoutedEventArgs e) => SetPreviewBackground(PreviewBackgroundMode.None);

    private void OnPreviewBackgroundCheckerClick(object sender, RoutedEventArgs e) => SetPreviewBackground(PreviewBackgroundMode.Checker);

    private void OnPreviewBackgroundWhiteClick(object sender, RoutedEventArgs e) => SetPreviewBackground(PreviewBackgroundMode.White);

    private void OnPreviewBackgroundBlackClick(object sender, RoutedEventArgs e) => SetPreviewBackground(PreviewBackgroundMode.Black);

    private void SetPreviewBackground(PreviewBackgroundMode mode)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.PreviewBackground = mode;
        }
    }

    private void SyncMaterialMenu()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        MicaMaterialItem.IsChecked = viewModel.IsMicaMaterial;
        AcrylicMaterialItem.IsChecked = viewModel.IsAcrylicMaterial;
        SolidMaterialItem.IsChecked = viewModel.IsSolidMaterial;
    }

    private void OnMaterialMicaClick(object sender, RoutedEventArgs e) => SetMaterial(WindowMaterial.Mica);

    private void OnMaterialAcrylicClick(object sender, RoutedEventArgs e) => SetMaterial(WindowMaterial.Acrylic);

    private void OnMaterialSolidClick(object sender, RoutedEventArgs e) => SetMaterial(WindowMaterial.Solid);

    private void SetMaterial(WindowMaterial material)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SetMaterial(material);
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximizeOrRestoreClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void OnToolbarMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is ButtonBase { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        // 焦点位于输入框时交由控件处理，否则输入时按 Ctrl+Z 会撤销图像操作
        if (Keyboard.FocusedElement is TextBoxBase)
        {
            return;
        }

        if (viewModel.IsCropModeEnabled)
        {
            if (e.Key == Key.Enter && viewModel.ApplyCropCommand.CanExecute(null))
            {
                viewModel.ApplyCropCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && viewModel.CancelCropCommand.CanExecute(null))
            {
                viewModel.CancelCropCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        var isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (e.Key == Key.Z && isShift)
        {
            ExecuteRedo(viewModel, e);
        }
        else if (e.Key == Key.Z)
        {
            if (viewModel.UndoCommand.CanExecute(null))
            {
                viewModel.UndoCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Y)
        {
            ExecuteRedo(viewModel, e);
        }
    }

    private void OnExportIcoMenuClick(object sender, RoutedEventArgs e)
    {
        ExecuteExportCommand(vm => vm.ConvertToIcoCommand);
    }

    private void OnExportIcnsMenuClick(object sender, RoutedEventArgs e)
    {
        ExecuteExportCommand(vm => vm.ConvertToIcnsCommand);
    }

    private void OnExportPngMenuClick(object sender, RoutedEventArgs e)
    {
        ExecuteExportCommand(vm => vm.ExportPreviewPngCommand);
    }

    private void OnExportPngSetMenuClick(object sender, RoutedEventArgs e)
    {
        ExecuteExportCommand(vm => vm.ExportPngSetCommand);
    }

    private void OnExportWebpMenuClick(object sender, RoutedEventArgs e)
    {
        ExecuteExportCommand(vm => vm.ExportPreviewWebpCommand);
    }

    private void OnExportWebsiteIconsMenuClick(object sender, RoutedEventArgs e)
    {
        ExecuteExportCommand(vm => vm.ExportWebsiteIconsCommand);
    }

    // 菜单项位于弹出层，DataContext 继承不可靠，统一从窗口 DataContext 获取命令
    private void ExecuteExportCommand(Func<MainViewModel, System.Windows.Input.ICommand> selector)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var command = selector(viewModel);
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private static void ExecuteRedo(MainViewModel viewModel, KeyEventArgs e)
    {
        if (viewModel.RedoCommand.CanExecute(null))
        {
            viewModel.RedoCommand.Execute(null);
            e.Handled = true;
        }
    }
}
