using System.Buffers.Binary;
using System.Globalization;
using System.Net;
namespace CSharpFar.NetworkTraffic;

internal sealed record SocketSourceEvent(long Sequence, string Provider, int Id,
    DateTimeOffset OccurredUtc, Dictionary<string, string?> Fields);
internal sealed record NetworkByte(DateTimeOffset Time, string Protocol, string Family, string Direction,
    string LocalAddress, int LocalPort, string RemoteAddress, int RemotePort, long Bytes);
internal static class NetworkByteDecoder
{
    public static NetworkByte? Decode(SocketSourceEvent observation)
    {
        var kind = observation.Id switch
        {
            10 => ("TCP", "IPv4", "Send"),
            11 => ("TCP", "IPv4", "Receive"),
            26 => ("TCP", "IPv6", "Send"),
            27 => ("TCP", "IPv6", "Receive"),
            42 => ("UDP", "IPv4", "Send"),
            43 => ("UDP", "IPv4", "Receive"),
            58 => ("UDP", "IPv6", "Send"),
            59 => ("UDP", "IPv6", "Receive"),
            _ => ("", "", "")
        };
        if (kind.Item1.Length == 0) return null;
        var f = observation.Fields;
        int sp = BinaryPrimitives.ReverseEndianness((ushort)int.Parse(f["sport"]!, CultureInfo.InvariantCulture));
        int dp = BinaryPrimitives.ReverseEndianness((ushort)int.Parse(f["dport"]!, CultureInfo.InvariantCulture));
        var sa = Address(f["saddr"]!, kind.Item2); var da = Address(f["daddr"]!, kind.Item2);
        bool send = kind.Item3 == "Send";
        return new(observation.OccurredUtc, kind.Item1, kind.Item2, kind.Item3, send ? sa : da, send ? sp : dp,
            send ? da : sa, send ? dp : sp, long.Parse(f["size"]!, CultureInfo.InvariantCulture));
    }
    private static string Address(string raw, string family)
    {
        if (IPAddress.TryParse(raw, out var parsed) && (raw.Contains('.') || raw.Contains(':'))) return parsed.ToString();
        if (family == "IPv6") return new IPAddress(Convert.FromHexString(raw)).ToString();
        return new IPAddress(BitConverter.GetBytes(unchecked((uint)long.Parse(raw, CultureInfo.InvariantCulture)))).ToString();
    }
}
