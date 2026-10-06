using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;

namespace AdElementsFree.Monitoring;

public static class LoopbackPort
{
    public static int? GetOwner(int port)
    {
        int? owner = null;
        // Inspect OS TCP metadata only; do not open the owning process.
        foreach (int family in new[] { 2, 23 })
        {
            int size = 0;
            uint result = GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, 3, 0);
            if (result != 122 && result != 0) throw new Win32Exception((int)result);
            var memory = Marshal.AllocHGlobal(size);
            try
            {
                result = GetExtendedTcpTable(memory, ref size, false, family, 3, 0);
                if (result != 0) throw new Win32Exception((int)result);
                int count = Marshal.ReadInt32(memory);
                int rowSize = family == 2 ? 24 : 56;
                for (int i = 0; i < count; i++)
                {
                    var row = IntPtr.Add(memory, 4 + i * rowSize);
                    int portOffset = family == 2 ? 8 : 20;
                    int localPort = (Marshal.ReadByte(row, portOffset) << 8) | Marshal.ReadByte(row, portOffset + 1);
                    if (localPort != port) continue;
                    var address = new byte[family == 2 ? 4 : 16];
                    Marshal.Copy(IntPtr.Add(row, family == 2 ? 4 : 0), address, 0, address.Length);
                    if (!IPAddress.IsLoopback(new IPAddress(address)))
                        throw new InvalidOperationException("调试端口绑定到非本机地址，已拒绝连接；请检查 KOOK 启动配置。");
                    if (family == 2)
                    {
                        int pid = Marshal.ReadInt32(row, 20);
                        if (owner != null && owner != pid) throw new InvalidOperationException("调试端口存在多个所有者。");
                        owner = pid;
                    }
                }
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        return owner;
    }

    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedTcpTable(IntPtr table, ref int size,
        bool order, int family, int tableClass, uint reserved);
}
