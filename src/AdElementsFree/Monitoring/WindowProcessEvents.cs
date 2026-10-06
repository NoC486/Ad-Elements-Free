using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AdElementsFree.Monitoring;

/// <summary>Out-of-context window events; no timer, process enumeration, or elevated WMI subscription.</summary>
public sealed class WindowProcessEvents : IDisposable
{
    private readonly Thread thread;
    private readonly WinEvent callback;
    private readonly TaskCompletionSource<uint> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string name;
    private readonly Action changed;
    private int disposed;

    public WindowProcessEvents(string executableName, Action changed)
    {
        name = executableName;
        this.changed = changed;
        callback = OnEvent;
        thread = new Thread(Run) { IsBackground = true, Name = "AEF window events" };
        thread.Start();
        ready.Task.GetAwaiter().GetResult();
    }

    private void Run()
    {
        // CREATE and SHOW include hidden startup windows and windows shown after startup.
        var hook = SetWinEventHook(0x8000, 0x8002, IntPtr.Zero, callback, 0, 0, 2);
        if (hook == IntPtr.Zero) { ready.TrySetException(new Win32Exception(Marshal.GetLastWin32Error())); return; }
        try
        {
            PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // Create message queue before publishing thread ID.
            ready.TrySetResult(GetCurrentThreadId());
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            { TranslateMessage(ref message); DispatchMessage(ref message); }
        }
        finally { UnhookWinEvent(hook); }
    }

    private void OnEvent(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint eventThread, uint time)
    {
        if (Volatile.Read(ref disposed) != 0 || kind == 0x8001 || window == IntPtr.Zero || objectId != 0 || childId != 0) return;
        if (GetAncestor(window, 2) != window) return; // Top-level windows only; ignore control events.
        GetWindowThreadProcessId(window, out uint pid);
        try
        {
            using var process = Process.GetProcessById(checked((int)pid));
            if (process.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase)) changed();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or OverflowException) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        if (ready.Task.IsCompletedSuccessfully) PostThreadMessage(ready.Task.Result, 0x0012, UIntPtr.Zero, IntPtr.Zero);
        thread.Join();
    }

    private delegate void WinEvent(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)] private struct Message
    { public IntPtr Window; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, IntPtr window, uint first, uint last, uint remove);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr window, uint first, uint last);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, UIntPtr wParam, IntPtr lParam);
}
