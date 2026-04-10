namespace Controleo.Mobile.Core.Interfaces;

public interface IConnectivityService
{
    bool IsOnline { get; }
    event EventHandler<bool>? ConnectivityChanged;
}
