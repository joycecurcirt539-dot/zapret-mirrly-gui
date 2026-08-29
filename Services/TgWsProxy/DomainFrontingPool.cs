using System;
using System.Collections.Generic;

namespace ZapretMirrlyGUI.Services.TgWsProxy;

public class DomainFrontingPool
{
    private static readonly string[] FRONTING_SNI_CANDIDATES = new[]
    {
        "sprinthost.ru",
        "habr.com",
        "vc.ru",
        "dtf.ru",
        "4pda.to",
        "pikabu.ru",
        "cdnjs.cloudflare.com",
        "speed.cloudflare.com",
        "challenges.cloudflare.com",
        "pages.dev",
        "workers.dev"
    };

    private static int _currentIndex = 0;

    public static string GetNextFrontingSni()
    {
        int idx = Math.Abs(Interlocked.Increment(ref _currentIndex)) % FRONTING_SNI_CANDIDATES.Length;
        return FRONTING_SNI_CANDIDATES[idx];
    }
}
