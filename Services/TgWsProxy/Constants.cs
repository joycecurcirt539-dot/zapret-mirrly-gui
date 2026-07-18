using System;
using System.Collections.Generic;

namespace ZapretMirrlyGUI.Services.TgWsProxy;

public static class Constants
{
    public const int HANDSHAKE_LEN = 64;
    public const int SKIP_LEN = 8;
    public const int PREKEY_LEN = 32;
    public const int KEY_LEN = 32;
    public const int IV_LEN = 16;
    public const int PROTO_TAG_POS = 56;
    public const int DC_IDX_POS = 60;

    public static readonly byte[] PROTO_TAG_ABRIDGED = { 0xef, 0xef, 0xef, 0xef };
    public static readonly byte[] PROTO_TAG_INTERMEDIATE = { 0xee, 0xee, 0xee, 0xee };
    public static readonly byte[] PROTO_TAG_SECURE = { 0xdd, 0xdd, 0xdd, 0xdd };

    public const uint PROTO_ABRIDGED_INT = 0xEFEFEFEF;
    public const uint PROTO_INTERMEDIATE_INT = 0xEEEEEEEE;
    public const uint PROTO_PADDED_INTERMEDIATE_INT = 0xDDDDDDDD;

    public static readonly byte[] ZERO_64 = new byte[64];

    public static readonly HashSet<byte> RESERVED_FIRST_BYTES = new() { 0xEF };
    
    public static readonly List<byte[]> RESERVED_STARTS = new()
    {
        new byte[] { 0x48, 0x45, 0x41, 0x44 }, // HEAD
        new byte[] { 0x50, 0x4F, 0x53, 0x54 }, // POST
        new byte[] { 0x47, 0x45, 0x54, 0x20 }, // GET 
        new byte[] { 0xee, 0xee, 0xee, 0xee },
        new byte[] { 0xdd, 0xdd, 0xdd, 0xdd },
        new byte[] { 0x16, 0x03, 0x01, 0x02 }
    };
    public static readonly byte[] RESERVED_CONTINUE = { 0x00, 0x00, 0x00, 0x00 };

    public static readonly Dictionary<int, string> DC_DEFAULT_IPS = new()
    {
        { 1, "149.154.175.50" },
        { 2, "149.154.167.51" },
        { 3, "149.154.175.100" },
        { 4, "149.154.167.91" },
        { 5, "149.154.171.5" },
        { 203, "91.105.192.100" }
    };

    public static readonly Dictionary<int, string> DC_TEST_IPS = new()
    {
        { 1, "149.154.175.10" },
        { 2, "149.154.167.40" },
        { 3, "149.154.175.117" }
    };

    public const string WS_PATH = "/apiws";
    public const string WS_PATH_TEST = "/apiws_test";
}
