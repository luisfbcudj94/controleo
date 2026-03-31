using Controleo.Application.DTOs;
namespace Controleo.Application.Validation;
public static class AuthValidator
{
    public static Dictionary<string, string[]> ValidateRegister(AuthRegisterRequest r)
    {
        var e = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Name)) e["name"] = ["El nombre es requerido."];
        if (string.IsNullOrWhiteSpace(r.Email) || !r.Email.Contains('@')) e["email"] = ["Correo inválido."];
        if (string.IsNullOrWhiteSpace(r.Password) || r.Password.Trim().Length < 8) e["password"] = ["La contraseña debe tener al menos 8 caracteres."];
        return e;
    }
    public static Dictionary<string, string[]> ValidateLogin(AuthLoginRequest r)
    {
        var e = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Email)) e["email"] = ["El correo es requerido."];
        if (string.IsNullOrWhiteSpace(r.Password)) e["password"] = ["La contraseña es requerida."];
        return e;
    }
}
