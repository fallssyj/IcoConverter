using IcoConverter.ViewModels;

namespace IcoConverter;

public partial class AboutWindow
{
    public AboutWindow(AboutViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

