using System.Security.Cryptography;
using System.Text;
using BreakReminder.Core;

namespace BreakReminder.Platform.Windows;

/// <summary>
/// Секреты в settings.ini — через DPAPI Windows: расшифровать может только эта учётная запись на этом компьютере.
/// Перенесённый на другой компьютер файл даст пустые ключи — их нужно будет ввести заново.
/// </summary>
sealed class DpapiProtector : ISecretProtector
{
    // «соль» приложения: чужая программа той же учётной записи не расшифрует случайно попавшееся значение
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("BreakReminder.settings.v1");

    public string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));

    public string? Unprotect(string stored)
    {
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
