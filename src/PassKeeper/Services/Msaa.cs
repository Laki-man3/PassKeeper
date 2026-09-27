using System.Runtime.InteropServices;
using System.Windows;
using Accessibility;

namespace PassKeeper.Services;

/// <summary>
/// MSAA fallback for browsers whose UI Automation tree does not expose the page address (e.g. Firefox):
/// the accessible object of role "document" returns the URL as its value.
/// </summary>
internal static class Msaa
{
    private const int RoleSystemDocument = 0x0F;
    private const int ChildIdSelf = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromPoint(POINT pt, [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible, out object childId);

    /// <param name="screenBounds">Bounds of an element inside the page (physical screen pixels).</param>
    public static string? DocumentUrlAt(Rect screenBounds)
    {
        if (screenBounds.IsEmpty) return null;
        try
        {
            var pt = new POINT { X = (int)(screenBounds.Left + screenBounds.Width / 2), Y = (int)(screenBounds.Top + screenBounds.Height / 2) };
            if (AccessibleObjectFromPoint(pt, out var acc, out _) != 0) return null;
            for (var depth = 0; acc != null && depth < 60; depth++)
            {
                if (acc.get_accRole(ChildIdSelf) is int role && role == RoleSystemDocument)
                    return acc.get_accValue(ChildIdSelf);
                acc = acc.accParent as IAccessible;
            }
        }
        catch (COMException) { }
        catch (InvalidCastException) { }
        catch (ArgumentException) { }
        return null;
    }
}
