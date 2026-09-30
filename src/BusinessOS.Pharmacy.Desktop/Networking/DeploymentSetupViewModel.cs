using System.Collections.ObjectModel;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.LocalClient;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.Pharmacy.Desktop.Networking;

public sealed partial class DeploymentSetupViewModel : ObservableObject
{
    private readonly INetworkConfigurationStore _configurationStore;
    private readonly ILocalServerDiscovery _discovery;
    private readonly LanTerminalPairingClient _pairingClient;

    [ObservableProperty] private DeploymentMode selectedMode = DeploymentMode.Standalone;
    [ObservableProperty] private string serverName = "Main Pharmacy Server";
    [ObservableProperty] private string serverHost = string.Empty;
    [ObservableProperty] private int serverPort = NetworkConfiguration.DefaultServerPort;
    [ObservableProperty] private string serverId = string.Empty;
    [ObservableProperty] private string certificateSha256 = string.Empty;
    [ObservableProperty] private string pairingCode = string.Empty;
    [ObservableProperty] private string terminalName = Environment.MachineName;
    [ObservableProperty] private string terminalRole = "POS Terminal";
    [ObservableProperty] private LocalServerDiscoveryAdvertisement? selectedDiscoveredServer;
    [ObservableProperty] private string statusMessage = "Choose how this computer will be used.";
    [ObservableProperty] private bool isBusy;

    public DeploymentSetupViewModel(
        INetworkConfigurationStore configurationStore,
        ILocalServerDiscovery discovery,
        LanTerminalPairingClient pairingClient)
    {
        _configurationStore = configurationStore;
        _discovery = discovery;
        _pairingClient = pairingClient;

        DiscoverCommand = new AsyncRelayCommand(DiscoverAsync, () => !IsBusy);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public event EventHandler? SetupCompleted;

    public ObservableCollection<LocalServerDiscoveryAdvertisement> DiscoveredServers { get; } = new();
    public IReadOnlyList<DeploymentMode> Modes { get; } =
        [DeploymentMode.Standalone, DeploymentMode.Server, DeploymentMode.Client];

    public IAsyncRelayCommand DiscoverCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }

    partial void OnSelectedDiscoveredServerChanged(LocalServerDiscoveryAdvertisement? value)
    {
        if (value is null)
        {
            return;
        }

        ServerHost = value.HostName;
        ServerPort = value.Port;
        ServerId = value.ServerId;
        CertificateSha256 = value.CertificateSha256;
        StatusMessage = $"Found {value.ServerName} on {value.HostName}:{value.Port}.";
    }

    private async Task DiscoverAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            DiscoveredServers.Clear();
            var servers = await _discovery.DiscoverAsync(TimeSpan.FromSeconds(3));

            foreach (var server in servers)
            {
                DiscoveredServers.Add(server);
            }

            StatusMessage = servers.Count == 0
                ? "No Darmaltoon pharmacy server was found automatically. You can enter the server details manually."
                : $"{servers.Count} pharmacy server(s) found.";
        });
    }

    private async Task SaveAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            switch (SelectedMode)
            {
                case DeploymentMode.Standalone:
                    await _configurationStore.SaveAsync(
                        new NetworkConfiguration
                        {
                            Mode = DeploymentMode.Standalone,
                            ServerName = "Standalone Pharmacy",
                            ServerPort = ServerPort,
                            DiscoveryEnabled = false,
                            IsConfigured = true,
                        });
                    break;

                case DeploymentMode.Server:
                    if (string.IsNullOrWhiteSpace(ServerName))
                    {
                        throw new InvalidOperationException("Enter a name for the Main Pharmacy Server.");
                    }

                    await _configurationStore.SaveAsync(
                        new NetworkConfiguration
                        {
                            Mode = DeploymentMode.Server,
                            ServerName = ServerName.Trim(),
                            ServerPort = ServerPort,
                            DiscoveryEnabled = true,
                            IsConfigured = true,
                        });
                    break;

                case DeploymentMode.Client:
                    if (string.IsNullOrWhiteSpace(ServerHost) ||
                        string.IsNullOrWhiteSpace(ServerId) ||
                        string.IsNullOrWhiteSpace(CertificateSha256) ||
                        string.IsNullOrWhiteSpace(PairingCode))
                    {
                        throw new InvalidOperationException(
                            "Client Terminal setup requires the server address, server identity, certificate fingerprint and pairing code.");
                    }

                    await _pairingClient.PairAsync(
                        ServerHost,
                        ServerPort,
                        ServerId,
                        CertificateSha256,
                        PairingCode,
                        TerminalName,
                        TerminalRole);
                    break;
            }

            StatusMessage = "Deployment setup completed.";
            SetupCompleted?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task ExecuteBusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        DiscoverCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();

        try
        {
            await action();
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
            DiscoverCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
        }
    }
}
