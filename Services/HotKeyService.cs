using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace ZapretMirrlyGUI.Services;

public static class HotKeyService
{
    // Win32 Constants
    public const int WM_HOTKEY = 0x0312;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;

    public const int HOTKEY_ID_DPI = 0x9001; // Ctrl + Alt + Z
    public const int HOTKEY_ID_TG = 0x9002;  // Ctrl + Alt + X
    public const int HOTKEY_ID_ALL = 0x9003; // Ctrl + Alt + C

    public const uint VK_Z = 0x5A;
    public const uint VK_X = 0x58;
    public const uint VK_C = 0x43;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private static IntPtr _registeredHWnd = IntPtr.Zero;

    public static event Action? OnToggleDpiHotKeyPressed;
    public static event Action? OnToggleTgHotKeyPressed;
    public static event Action? OnToggleAllHotKeyPressed;

    public static void RegisterGlobalHotKeys(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        _registeredHWnd = hWnd;

        try
        {
            // Ctrl + Alt + Z
            RegisterHotKey(hWnd, HOTKEY_ID_DPI, MOD_CONTROL | MOD_ALT, VK_Z);

            // Ctrl + Alt + X
            RegisterHotKey(hWnd, HOTKEY_ID_TG, MOD_CONTROL | MOD_ALT, VK_X);

            // Ctrl + Alt + C
            RegisterHotKey(hWnd, HOTKEY_ID_ALL, MOD_CONTROL | MOD_ALT, VK_C);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HotKeyService] Error registering hotkeys: {ex.Message}");
        }
    }

    public static void UnregisterGlobalHotKeys()
    {
        if (_registeredHWnd == IntPtr.Zero) return;

        try
        {
            UnregisterHotKey(_registeredHWnd, HOTKEY_ID_DPI);
            UnregisterHotKey(_registeredHWnd, HOTKEY_ID_TG);
            UnregisterHotKey(_registeredHWnd, HOTKEY_ID_ALL);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HotKeyService] Error unregistering hotkeys: {ex.Message}");
        }
        finally
        {
            _registeredHWnd = IntPtr.Zero;
        }
    }

    public static bool ProcessWndProcMessage(uint uMsg, IntPtr wParam)
    {
        if (uMsg == WM_HOTKEY)
        {
            int hotkeyId = wParam.ToInt32();
            switch (hotkeyId)
            {
                case HOTKEY_ID_DPI:
                    OnToggleDpiHotKeyPressed?.Invoke();
                    return true;
                case HOTKEY_ID_TG:
                    OnToggleTgHotKeyPressed?.Invoke();
                    return true;
                case HOTKEY_ID_ALL:
                    OnToggleAllHotKeyPressed?.Invoke();
                    return true;
            }
        }
        return false;
    }
}
