using System.Security.Cryptography;
using System.Text;
using Invy.Client.Core.Connection;

namespace Invy.Desktop.Hosting;

/// <summary>Токен устройства шифруется DPAPI: расшифровать может только этот пользователь Windows на этом ПК.</summary>
internal sealed class DpapiTokenProtector : ITokenProtector
{
    private static readonly byte[] Entropy = "Invy.Desktop.DeviceToken"u8.ToArray();

    public static ITokenProtector ForCurrentPlatform() => OperatingSystem.IsWindows() ? new DpapiTokenProtector() : new PlainTokenProtector();

    public string Protect(string token)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }

        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser));
    }

    public string? Unprotect(string protectedToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedToken), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;   // настройки скопированы с другого ПК или пользователя — подключимся заново
        }
    }
}
