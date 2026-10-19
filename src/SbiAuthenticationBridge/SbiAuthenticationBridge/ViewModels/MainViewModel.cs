using CommunityToolkit.Mvvm.ComponentModel;

namespace SbiAuthenticationBridge.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty] public partial string Greeting { get; set; } = "Welcome to Avalonia!";
}