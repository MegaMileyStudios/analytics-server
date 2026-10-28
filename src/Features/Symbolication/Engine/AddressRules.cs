namespace Aptabase.Features.Symbolication.Engine;

// Turns the offsets in a report into the addresses llvm-symbolizer has to look up
public static class AddressRules
{
    // The faulting instruction is looked up as-is: [0] on Windows, [2] on Linux and macOS (after the signal trampoline).
    // Every frame after it is a return address, which points just past the call, so one byte is subtracted to land on the call itself.
    public static bool IsExactAddress(ReportPlatform platform, int index) => platform == ReportPlatform.Windows ? index == 0 : index <= 2;

    // The frames up to the signal belong to the crash handler: Linux [1] is libc's signal trampoline,
    // macOS [0] is the crash handler itself and [1] is _sigtramp
    public static bool IsCrashHandlerFrame(ReportPlatform platform, int index) => platform switch
    {
        ReportPlatform.Linux => index == 1,
        ReportPlatform.MacOS => index <= 1,
        _ => false,
    };

    // The image base is 0 for Linux (modules load their first segment at 0), the stand-in PE's base for Windows,
    // and the __TEXT segment's vmaddr for macOS
    public static ulong NativeAddress(ReportPlatform platform, NativeFrame frame, ulong imageBase)
    {
        var address = imageBase + frame.Offset;
        return IsExactAddress(platform, frame.Index) ? address : ReturnAddressToCall(address);
    }

    // Managed frames are all return addresses, also the first one (the call that threw).
    // The base is the image base for "<BaseAddress>+0x..." frames, and the function's address for "Method() + 0x..." frames.
    public static ulong ManagedAddress(ulong baseAddress, ulong offset) => offset == 0 ? baseAddress : baseAddress + offset - 1;

    private static ulong ReturnAddressToCall(ulong address) => address == 0 ? 0 : address - 1;
}
