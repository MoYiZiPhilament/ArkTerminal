using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using ArkTerminal.Core.Data;

namespace ArkTerminal.Infrastructure;

public sealed class WindowsPackagingContext : IPackagingContext
{
    private const int AppModelErrorNoPackage = 15700;
    private const int ErrorInsufficientBuffer = 122;

    public string? GetPackageFamilyName()
    {
        var length = 0;
        var result = GetCurrentPackageFamilyName(ref length, null);
        if (result == AppModelErrorNoPackage)
        {
            return null;
        }
        if (result != ErrorInsufficientBuffer)
        {
            throw new Win32Exception(result);
        }

        var name = new StringBuilder(length);
        result = GetCurrentPackageFamilyName(ref length, name);
        if (result != 0)
        {
            throw new Win32Exception(result);
        }
        return name.ToString();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref int packageFullNameLength, StringBuilder? packageFullName);
}
