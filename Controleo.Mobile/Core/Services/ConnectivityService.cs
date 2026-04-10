using Controleo.Mobile.Core.Interfaces;
using Microsoft.Maui.Networking;

namespace Controleo.Mobile.Core.Services;

public sealed class ConnectivityService : IConnectivityService, IDisposable
{
    private readonly IConnectivity _connectivity;

    public event EventHandler<bool>? ConnectivityChanged;

    public bool IsOnline => _connectivity.NetworkAccess == NetworkAccess.Internet;

    public ConnectivityService()
        : this(Connectivity.Current)
    {
    }

    internal ConnectivityService(IConnectivity connectivity)
    {
        _connectivity = connectivity;
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        ConnectivityChanged?.Invoke(this, e.NetworkAccess == NetworkAccess.Internet);
    }

    public void Dispose()
    {
        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
    }
}
