namespace Controleo.Mobile.Interfaces;

public interface IPaymentIconService
{
    (string Emoji, string Label)[] AvailableIcons { get; }
    string IconForPaymentMethod(string? paymentMethod);
    void SetIconForPaymentMethod(string paymentMethod, string icon);
    void RemovePaymentMethod(string paymentMethod);
    void RenamePaymentMethod(string oldName, string newName);
}
