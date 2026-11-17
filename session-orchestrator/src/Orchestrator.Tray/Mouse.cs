using System.Runtime.InteropServices;

namespace Orchestrator.Tray;

/// <summary>Real mouse input for the drag check of the self-test (SendInput).</summary>
static partial class Mouse
{
    const uint LeftDown = 0x0002;
    const uint LeftUp = 0x0004;

    public static void Drag(Point from, Point to)
    {
        SetCursorPos(from.X, from.Y);
        Thread.Sleep(150);
        Send(LeftDown);
        Thread.Sleep(400);
        for (var i = 1; i <= 10; i++)
        {
            SetCursorPos(from.X + (to.X - from.X) * i / 10, from.Y + (to.Y - from.Y) * i / 10);
            Thread.Sleep(40);
        }
        Thread.Sleep(150);
        Send(LeftUp);
    }

    static void Send(uint flags)
    {
        var input = new Input { Type = 0, Mi = new MouseInput { Flags = flags } };
        _ = SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Input
    {
        public uint Type;
        public MouseInput Mi;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint count, [In] Input[] inputs, int size);
}
