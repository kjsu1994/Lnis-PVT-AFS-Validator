using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

public sealed record AfsAlmanacEntry(int Prn, double Eccentricity, double ToeSeconds, double Inclination, double SqrtA, double RightAscension, double ArgumentOfPerigee, double MeanAnomaly, double Af0, double Af1, int Week);

public static class AfsSb2Builder
{
    private const double Pow2M32 = 2.3283064365386963e-10;
    private const double Pow2M19 = 1.9073486328125e-6;
    private const double Pow2M31 = 4.656612873077393e-10;
    private const double Pow2M43 = 1.1368683772161603e-13;

    public static AfsAlmanacEntry ReadAlmanac(string path, int targetPrn)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Almanac file was not found.", path);
        using var reader = new StreamReader(path); string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!line.StartsWith('*')) continue;
            var id = ReadValue(reader); var prn = ParseInt(id);
            _ = ReadValue(reader); var ecc = ParseDouble(ReadValue(reader)); var toe = ParseDouble(ReadValue(reader));
            var inc = ParseDouble(ReadValue(reader)); _ = ReadValue(reader); var sqrtA = ParseDouble(ReadValue(reader));
            var omega0 = ParseDouble(ReadValue(reader)); var aop = ParseDouble(ReadValue(reader)); var m0 = ParseDouble(ReadValue(reader));
            var af0 = ParseDouble(ReadValue(reader)); var af1 = ParseDouble(ReadValue(reader)); var week = ParseInt(ReadValue(reader)) + 2048;
            if (prn == targetPrn) return new(prn, ecc, toe, inc, sqrtA, omega0, aop, m0, af0, af1, week);
        }
        throw new InvalidDataException($"PRN {targetPrn} was not found in the almanac.");
    }

    public static byte[] Build(ushort week, ushort intervalOfWeek, AfsAlmanacEntry entry)
    {
        if (intervalOfWeek >= 504) throw new ArgumentOutOfRangeException(nameof(intervalOfWeek));
        var bits = Enumerable.Range(0, 1176).Select(i => (byte)(i & 1)).ToArray();
        Write(bits, 0, 13, week); Write(bits, 13, 9, intervalOfWeek);
        var o = 22; Write(bits, o, 16, ToUnsigned(entry.ToeSeconds / 16));
        Write(bits, o + 16, 32, ToUnsigned(entry.Eccentricity / Pow2M32));
        Write(bits, o + 48, 32, ToUnsigned(entry.SqrtA / Pow2M19));
        Write(bits, o + 80, 32, ToSigned(entry.Inclination / Pow2M31 / Math.PI));
        Write(bits, o + 112, 32, ToSigned(entry.RightAscension / Pow2M31 / Math.PI));
        Write(bits, o + 144, 32, ToSigned(entry.ArgumentOfPerigee / Pow2M31 / Math.PI));
        Write(bits, o + 176, 32, ToSigned(entry.MeanAnomaly / Pow2M31 / Math.PI));
        Write(bits, o + 208, 16, ToUnsigned(entry.ToeSeconds / 16));
        Write(bits, o + 224, 22, ToSigned(entry.Af0 / Pow2M31));
        Write(bits, o + 246, 16, ToSigned(entry.Af1 / Pow2M43)); return bits;
    }

    private static string ReadValue(StreamReader reader) => reader.ReadLine() ?? throw new InvalidDataException("Truncated almanac entry.");
    private static string Value(string line) => line.Length > 26 ? line[26..].Trim() : throw new InvalidDataException("Malformed almanac line.");
    private static int ParseInt(string line) => int.Parse(Value(line), System.Globalization.CultureInfo.InvariantCulture);
    private static double ParseDouble(string line) => double.Parse(Value(line), System.Globalization.CultureInfo.InvariantCulture);
    private static ulong ToUnsigned(double value) => checked((ulong)Math.Truncate(value));
    private static ulong ToSigned(double value) => unchecked((ulong)checked((long)Math.Truncate(value)));
    private static void Write(Span<byte> bits, int offset, int length, ulong value) { for (var i = 0; i < length; i++) bits[offset + i] = (byte)((value >> (length - i - 1)) & 1); }
}

