using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PortFlow.Shared.Id;

/// <summary>UUIDv7 (RFC 9562) tăng dần nghiêm ngặt trong một tiến trình: 48 bit millis + 12 bit bộ đếm + 62 bit ngẫu nhiên.</summary>
public static class UuidV7
{
    private static readonly Lock Gate = new();

    private static long _lastMillis = -1;
    private static int _counter;

    public static Guid Next()
    {
        long millis;
        int seq;
        lock (Gate)
        {
            millis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (millis <= _lastMillis)
            {
                millis = _lastMillis;
                _counter++;
                if (_counter > 0xFFF)
                {
                    // Hết 12 bit bộ đếm trong cùng millisecond: mượn millisecond kế tiếp để vẫn tăng dần.
                    millis = ++_lastMillis;
                    _counter = 0;
                }
            }
            else
            {
                _lastMillis = millis;
                _counter = RandomNumberGenerator.GetInt32(0x800);
            }
            seq = _counter;
        }

        long msb = (millis << 16) | (0x7L << 12) | (long)seq;
        long lsb = (RandomLong() & 0x3FFFFFFFFFFFFFFFL) | unchecked((long)0x8000000000000000UL);

        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteInt64BigEndian(bytes, msb);
        BinaryPrimitives.WriteInt64BigEndian(bytes[8..], lsb);
        return new Guid(bytes, bigEndian: true);
    }

    private static long RandomLong()
    {
        Span<byte> buffer = stackalloc byte[8];
        RandomNumberGenerator.Fill(buffer);
        return BinaryPrimitives.ReadInt64BigEndian(buffer);
    }
}
