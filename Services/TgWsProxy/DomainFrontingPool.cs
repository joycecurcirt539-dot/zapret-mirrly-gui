using System;
using System.Collections.Generic;

namespace ZapretMirrlyGUI.Services.TgWsProxy;

public class DomainFrontingPool
{
    private static readonly string[] FRONTING_SNI_CANDIDATES = new[]
    {
        "sprinthost.ru",
        "vk.com",
        "yandex.ru",
        "sberbank.ru",
        "tbank.ru",
        "mail.ru",
        "ok.ru"
    };

    private static int _currentIndex = 0;

    public static string GetNextFrontingSni()
    {
        int idx = Math.Abs(Interlocked.Increment(ref _currentIndex)) % FRONTING_SNI_CANDIDATES.Length;
        return FRONTING_SNI_CANDIDATES[idx];
    }
}
