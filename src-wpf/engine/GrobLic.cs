namespace Grobyq {
using System;
using System.Globalization;
using System.Management;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

// C# 5 on purpose: Windows PowerShell 5.1 compiles Add-Type with the
// .NET Framework csc. No ?. no $"" no => members no out var.
public static class GrobLic
{
    // ------------------------------------------------------------------
    //  HWID (unchanged since v2, so issued licenses stay valid):
    //  ProcessorId | BaseBoard serial | first disk serial | MachineGuid,
    //  SHA-256, first 16 bytes, XXXXXXXX-XXXXXXXX-XXXXXXXX-XXXXXXXX.
    // ------------------------------------------------------------------
    private static string _hwid;

    // The HWID once computed (WMI is slow on the first call); null until then. The UI reads this, a worker computes.
    public static string HwidIfReady { get { return _hwid; } }

    public static string Hwid()
    {
        if (_hwid != null) return _hwid;
        string raw = string.Join("|", new string[] {
            Wmi("SELECT ProcessorId FROM Win32_Processor", "ProcessorId", "cpu-unknown", false),
            Wmi("SELECT SerialNumber FROM Win32_BaseBoard", "SerialNumber", "mb-unknown", false),
            Wmi("SELECT SerialNumber FROM Win32_PhysicalMedia", "SerialNumber", "disk-unknown", true),
            MachineGuid()
        });
        _hwid = Hash(raw);
        return _hwid;
    }

    // firstNonEmpty=false: first object decides (null -> fallback, "" stays "").
    // firstNonEmpty=true : first non-empty value wins, else fallback.
    private static string Wmi(string query, string prop, string fallback, bool firstNonEmpty)
    {
        try
        {
            using (ManagementObjectSearcher s = new ManagementObjectSearcher(query))
            {
                foreach (ManagementObject mo in s.Get())
                {
                    object v = mo[prop];
                    string t = v == null ? null : v.ToString().Trim();
                    if (!firstNonEmpty) return t ?? fallback;
                    if (!string.IsNullOrEmpty(t)) return t;
                }
            }
        }
        catch { }
        return fallback;
    }

    // Always the 64-bit registry view, whatever the bitness of the caller.
    private static string MachineGuid()
    {
        try
        {
            using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", false))
            {
                if (key == null) return "guid-unknown";
                object v = key.GetValue("MachineGuid");
                return v == null ? "guid-unknown" : v.ToString().Trim();
            }
        }
        catch { return "guid-unknown"; }
    }

    public static string Hash(string input)
    {
        byte[] b = Sha256(Encoding.UTF8.GetBytes(input));
        StringBuilder sb = new StringBuilder(32);
        for (int i = 0; i < 16; i++) sb.Append(b[i].ToString("X2"));
        string s = sb.ToString();
        return s.Substring(0, 8) + "-" + s.Substring(8, 8) + "-" + s.Substring(16, 8) + "-" + s.Substring(24, 8);
    }

    private static byte[] Sha256(byte[] data)
    {
        using (SHA256 sha = SHA256.Create()) return sha.ComputeHash(data);
    }

    // ------------------------------------------------------------------
    //  JSON string escaping used for the signed payload text
    //  (grobyq Admin signs this exact text; one different byte = bad signature).
    // ------------------------------------------------------------------
    public static string Str(string s)
    {
        if (s == null) return "null";
        StringBuilder sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20 || c > 0x7E || c == '"' || c == '&' || c == '\'' ||
                        c == '+' || c == '<' || c == '>' || c == '`')
                        sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    // Canonical payload: sorted keys, same text grobyq Admin signs.
    public static string Canonical(string appVersion, string expires, string hwid, long issued, string purchase, int ttlHours)
    {
        return "{\"app_version\":" + Str(appVersion) +
               ",\"expires\":" + Str(expires) +
               ",\"hwid\":" + Str(hwid) +
               ",\"issued\":" + issued.ToString(CultureInfo.InvariantCulture) +
               ",\"purchase\":" + Str(purchase) +
               ",\"ttl_hours\":" + ttlHours.ToString(CultureInfo.InvariantCulture) + "}";
    }

    // ------------------------------------------------------------------
    //  ECDSA P-256 / SHA-256 verification, IEEE P1363 signature (r||s),
    //  as produced by .NET ECDsa.SignData in grobyq Admin. Plain
    //  BigInteger math: no CNG blob tricks, works on every .NET 4.x.
    // ------------------------------------------------------------------
    private static readonly BigInteger P  = H("FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF");
    private static readonly BigInteger A  = P - 3;
    private static readonly BigInteger B  = H("5AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B");
    private static readonly BigInteger N  = H("FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551");
    private static readonly BigInteger GX = H("6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296");
    private static readonly BigInteger GY = H("4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5");

    // SubjectPublicKeyInfo prefix: id-ecPublicKey, prime256v1, uncompressed point.
    private static readonly byte[] SpkiPrefix = {
        0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01,
        0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07, 0x03, 0x42, 0x00, 0x04 };

    private static BigInteger H(string hex)
    {
        return BigInteger.Parse("0" + hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    }

    private static BigInteger U(byte[] be, int off, int len)
    {
        byte[] le = new byte[len + 1];
        for (int i = 0; i < len; i++) le[i] = be[off + len - 1 - i];
        return new BigInteger(le);
    }

    private static BigInteger Mod(BigInteger v, BigInteger m)
    {
        BigInteger r = BigInteger.Remainder(v, m);
        return r.Sign < 0 ? r + m : r;
    }

    private static BigInteger Inv(BigInteger v, BigInteger m)
    {
        return BigInteger.ModPow(Mod(v, m), m - 2, m);
    }

    // Affine points as {x, y}; null is the point at infinity.
    private static BigInteger[] Add(BigInteger[] p, BigInteger[] q)
    {
        if (p == null) return q;
        if (q == null) return p;
        BigInteger l;
        if (p[0] == q[0])
        {
            if (Mod(p[1] + q[1], P).IsZero) return null;
            l = Mod(3 * p[0] * p[0] + A, P) * Inv(2 * p[1], P);
        }
        else
        {
            l = Mod(q[1] - p[1], P) * Inv(q[0] - p[0], P);
        }
        l = Mod(l, P);
        BigInteger x = Mod(l * l - p[0] - q[0], P);
        BigInteger y = Mod(l * (p[0] - x) - p[1], P);
        return new BigInteger[] { x, y };
    }

    private static BigInteger[] Mul(BigInteger k, BigInteger[] pt)
    {
        BigInteger[] r = null;
        BigInteger[] a = pt;
        while (k.Sign > 0)
        {
            if (!k.IsEven) r = Add(r, a);
            a = Add(a, a);
            k = k >> 1;
        }
        return r;
    }

    public static bool Verify(string data, string signatureB64, string publicKeyB64)
    {
        try
        {
            if (data == null || signatureB64 == null || publicKeyB64 == null) return false;
            byte[] spki = Convert.FromBase64String(publicKeyB64);
            byte[] sig = Convert.FromBase64String(signatureB64);
            if (spki.Length != 91 || sig.Length != 64) return false;
            for (int i = 0; i < SpkiPrefix.Length; i++)
                if (spki[i] != SpkiPrefix[i]) return false;

            BigInteger qx = U(spki, 27, 32), qy = U(spki, 59, 32);
            if (qx >= P || qy >= P) return false;
            if (!Mod(qy * qy - (qx * qx * qx + A * qx + B), P).IsZero) return false;

            BigInteger r = U(sig, 0, 32), s = U(sig, 32, 32);
            if (r.Sign <= 0 || r >= N || s.Sign <= 0 || s >= N) return false;

            BigInteger e = U(Sha256(Encoding.UTF8.GetBytes(data)), 0, 32);
            BigInteger w = BigInteger.ModPow(s, N - 2, N);
            BigInteger[] X = Add(Mul(Mod(e * w, N), new BigInteger[] { GX, GY }),
                                 Mul(Mod(r * w, N), new BigInteger[] { qx, qy }));
            if (X == null) return false;
            return Mod(X[0], N) == r;
        }
        catch { return false; }
    }
}

}
