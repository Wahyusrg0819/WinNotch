param([Parameter(Mandatory)][int]$ProcessId, [Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
# Read resident pages only. Never trims memory, collects the GC, or reads page contents.
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
public static class ResidentMemoryMap
{
    [StructLayout(LayoutKind.Sequential)] struct Region
    {
        public ulong Address, AllocationBase;
        public uint AllocationProtect;
        public ulong Size;
        public uint State, Protect, Type;
    }
    public class Group
    {
        public string Kind, File;
        public long PrivateBytes, ShareableBytes;
    }
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(uint access, bool inherit, int id);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError=true)] static extern UIntPtr VirtualQueryEx(IntPtr process, IntPtr address, out Region region, UIntPtr length);
    [DllImport("psapi.dll", SetLastError=true)] static extern bool QueryWorkingSet(IntPtr process, IntPtr buffer, int length);
    [DllImport("psapi.dll", CharSet=CharSet.Unicode)] static extern uint GetMappedFileName(IntPtr process, IntPtr address, StringBuilder name, int size);
    public static Group[] Read(int id)
    {
        if (IntPtr.Size != 8) throw new InvalidOperationException("Run in 64-bit PowerShell.");
        var process = OpenProcess(0x410, false, id);
        if (process == IntPtr.Zero) throw new Win32Exception();
        // ponytail: this buffer covers working sets below 4 GiB; grow it for larger diagnostic targets.
        const int bytes = 8 * 1024 * 1024;
        var buffer = Marshal.AllocHGlobal(bytes);
        try
        {
            if (!QueryWorkingSet(process, buffer, bytes)) throw new Win32Exception();
            var count = checked((int)Marshal.ReadInt64(buffer));
            if (count < 0 || count >= bytes / 8) throw new InvalidOperationException("Unexpected working-set size.");
            var pages = new ulong[count];
            for (int i = 0; i < count; i++) pages[i] = (ulong)Marshal.ReadInt64(buffer, (i + 1) * 8);
            Array.Sort(pages);
            var groups = new Dictionary<string, Group>();
            var files = new Dictionary<ulong, string>();
            var region = new Region();
            Group group = null;
            foreach (var page in pages)
            {
                var address = page & ~4095UL;
                if (group == null || address >= region.Address + region.Size)
                {
                    if (VirtualQueryEx(process, (IntPtr)address, out region, (UIntPtr)Marshal.SizeOf<Region>()) == UIntPtr.Zero) throw new Win32Exception();
                    var kind = region.Type == 0x1000000 ? "Image" : region.Type == 0x40000 ? "Mapped" : "Private";
                    var file = "";
                    if (kind != "Private" && !files.TryGetValue(region.AllocationBase, out file))
                    {
                        var name = new StringBuilder(1024);
                        GetMappedFileName(process, (IntPtr)address, name, name.Capacity);
                        files[region.AllocationBase] = file = Path.GetFileName(name.ToString());
                    }
                    var key = kind + ":" + file;
                    if (!groups.TryGetValue(key, out group)) groups[key] = group = new Group { Kind = kind, File = file };
                }
                if ((page & 0x100) != 0) group.ShareableBytes += 4096;
                else group.PrivateBytes += 4096;
            }
            var result = new Group[groups.Count]; groups.Values.CopyTo(result, 0);
            long total = 0;
            foreach (var item in result) total += item.PrivateBytes + item.ShareableBytes;
            if (total != count * 4096L) throw new InvalidOperationException("Page totals did not reconcile.");
            return result;
        }
        finally { Marshal.FreeHGlobal(buffer); CloseHandle(process); }
    }
}
'@
$rows = @([ResidentMemoryMap]::Read($ProcessId) | ForEach-Object {
    [pscustomobject]@{ Kind=$_.Kind; File=$_.File; PrivateBytes=$_.PrivateBytes; ShareableBytes=$_.ShareableBytes; TotalBytes=($_.PrivateBytes + $_.ShareableBytes) }
} | Sort-Object TotalBytes -Descending)
$report = [pscustomobject]@{
    ProcessId=$ProcessId
    CapturedAt=(Get-Date).ToString('o')
    ResidentMiB=($rows | Measure-Object TotalBytes -Sum).Sum / 1MB
    PrivateResidentMiB=($rows | Measure-Object PrivateBytes -Sum).Sum / 1MB
    ShareableResidentMiB=($rows | Measure-Object ShareableBytes -Sum).Sum / 1MB
    Regions=$rows
}
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $OutputPath
$report | Select-Object ProcessId,ResidentMiB,PrivateResidentMiB,ShareableResidentMiB
