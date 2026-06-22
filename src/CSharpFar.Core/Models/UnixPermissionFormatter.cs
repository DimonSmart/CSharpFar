namespace CSharpFar.Core.Models;

public static class UnixPermissionFormatter
{
    public static string ToOctalString(UnixPermissionBits permissions)
    {
        int value = (int)permissions & 0xfff;
        return Convert.ToString(value, 8).PadLeft(4, '0');
    }

    public static string ToSymbolicString(UnixPermissionBits permissions) =>
        new string(
        [
            Read(permissions, UnixPermissionBits.OwnerRead),
            Write(permissions, UnixPermissionBits.OwnerWrite),
            Execute(permissions, UnixPermissionBits.OwnerExecute, UnixPermissionBits.SetUid, 's', 'S'),
            Read(permissions, UnixPermissionBits.GroupRead),
            Write(permissions, UnixPermissionBits.GroupWrite),
            Execute(permissions, UnixPermissionBits.GroupExecute, UnixPermissionBits.SetGid, 's', 'S'),
            Read(permissions, UnixPermissionBits.OthersRead),
            Write(permissions, UnixPermissionBits.OthersWrite),
            Execute(permissions, UnixPermissionBits.OthersExecute, UnixPermissionBits.Sticky, 't', 'T'),
        ]);

    public static string ToDisplayString(UnixPermissionBits permissions) =>
        $"{ToOctalString(permissions)}  {ToSymbolicString(permissions)}";

    private static char Read(UnixPermissionBits permissions, UnixPermissionBits bit) =>
        Has(permissions, bit) ? 'r' : '-';

    private static char Write(UnixPermissionBits permissions, UnixPermissionBits bit) =>
        Has(permissions, bit) ? 'w' : '-';

    private static char Execute(
        UnixPermissionBits permissions,
        UnixPermissionBits executeBit,
        UnixPermissionBits specialBit,
        char specialWithExecute,
        char specialWithoutExecute)
    {
        bool hasExecute = Has(permissions, executeBit);
        bool hasSpecial = Has(permissions, specialBit);

        return (hasExecute, hasSpecial) switch
        {
            (true, true) => specialWithExecute,
            (false, true) => specialWithoutExecute,
            (true, false) => 'x',
            _ => '-',
        };
    }

    private static bool Has(UnixPermissionBits permissions, UnixPermissionBits bit) =>
        (permissions & bit) != 0;
}
