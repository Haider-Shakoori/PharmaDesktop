using BusinessOS.Pharmacy.Application.Abstractions.Time;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BusinessOS.Pharmacy.Desktop;

public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string applicationName = "BusinessOS Pharmacy";

    [ObservableProperty]
    private string statusText;

    public MainWindowViewModel(IClock clock)
    {
        statusText = $"Desktop foundation initialized • {clock.UtcNow:yyyy-MM-dd HH:mm} UTC";
    }
}
