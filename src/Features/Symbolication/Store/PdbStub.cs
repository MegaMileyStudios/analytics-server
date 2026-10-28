using System.Text;

namespace Aptabase.Features.Symbolication.Store;

// llvm-symbolizer won't read a .pdb on its own, it needs the .exe/.dll the PDB belongs to.
// From that file it only uses the CodeView record (PDB name, GUID and age) and the image base, the rest comes from the PDB.
// So instead of storing the real module, a ~1 KB stand-in PE with that record is written next to the PDB.
public static class PdbStub
{
    public const ulong ImageBase = 0x140000000;
    public const string FileSuffix = ".stub.exe";

    public static byte[] Build(Guid guid, int age, string pdbFileName)
    {
        var codeView = new MemoryStream();
        var cv = new BinaryWriter(codeView);
        cv.Write("RSDS"u8);
        cv.Write(guid.ToByteArray());
        cv.Write((uint)age);
        cv.Write(Encoding.UTF8.GetBytes(pdbFileName));
        cv.Write((byte)0);
        var codeViewBytes = codeView.ToArray();

        const uint fileAlignment = 0x200;
        const uint sectionRva = 0x1000;
        const uint sectionOffset = 0x200;
        const uint debugDirectorySize = 28;
        var sectionSize = debugDirectorySize + (uint)codeViewBytes.Length;
        var rawSize = (sectionSize + fileAlignment - 1) / fileAlignment * fileAlignment;

        var file = new MemoryStream();
        var w = new BinaryWriter(file);

        // DOS header, only e_lfanew matters
        w.Write("MZ"u8);
        w.Write(new byte[58]);
        w.Write((uint)0x40);

        // PE signature and COFF header: AMD64, 1 section, 240-byte optional header, executable + large address aware
        w.Write("PE\0\0"u8);
        w.Write((ushort)0x8664);
        w.Write((ushort)1);
        w.Write((uint)0);
        w.Write((uint)0);
        w.Write((uint)0);
        w.Write((ushort)240);
        w.Write((ushort)0x22);

        // PE32+ optional header
        w.Write((ushort)0x20B);
        w.Write((byte)14);
        w.Write((byte)0);
        w.Write((uint)0);              // SizeOfCode
        w.Write(rawSize);              // SizeOfInitializedData
        w.Write((uint)0);              // SizeOfUninitializedData
        w.Write((uint)0);              // AddressOfEntryPoint
        w.Write(sectionRva);           // BaseOfCode
        w.Write(ImageBase);
        w.Write((uint)0x1000);         // SectionAlignment
        w.Write(fileAlignment);
        w.Write((ushort)6);            // OS version
        w.Write((ushort)0);
        w.Write((ushort)0);            // Image version
        w.Write((ushort)0);
        w.Write((ushort)6);            // Subsystem version
        w.Write((ushort)0);
        w.Write((uint)0);              // Win32VersionValue
        w.Write(sectionRva + 0x1000);  // SizeOfImage
        w.Write((uint)0x200);          // SizeOfHeaders
        w.Write((uint)0);              // CheckSum
        w.Write((ushort)3);            // Subsystem: console
        w.Write((ushort)0x8160);       // DllCharacteristics
        w.Write((ulong)0x100000);      // Stack reserve/commit, heap reserve/commit
        w.Write((ulong)0x1000);
        w.Write((ulong)0x100000);
        w.Write((ulong)0x1000);
        w.Write((uint)0);              // LoaderFlags
        w.Write((uint)16);             // NumberOfRvaAndSizes

        // Data directories, only entry 6 (debug) is set
        for (var i = 0; i < 16; i++)
        {
            w.Write(i == 6 ? sectionRva : 0u);
            w.Write(i == 6 ? debugDirectorySize : 0u);
        }

        // A single .rdata section holding the debug directory and the CodeView record
        var name = new byte[8];
        ".rdata"u8.CopyTo(name);
        w.Write(name);
        w.Write(sectionSize);
        w.Write(sectionRva);
        w.Write(rawSize);
        w.Write(sectionOffset);
        w.Write((uint)0);
        w.Write((uint)0);
        w.Write((ushort)0);
        w.Write((ushort)0);
        w.Write((uint)0x40000040);     // initialized data, readable

        w.Write(new byte[sectionOffset - file.Length]);

        // IMAGE_DEBUG_DIRECTORY of type CODEVIEW (2)
        w.Write((uint)0);
        w.Write((uint)0);
        w.Write((ushort)0);
        w.Write((ushort)0);
        w.Write((uint)2);
        w.Write((uint)codeViewBytes.Length);
        w.Write(sectionRva + debugDirectorySize);
        w.Write(sectionOffset + debugDirectorySize);
        w.Write(codeViewBytes);

        w.Write(new byte[sectionOffset + rawSize - file.Length]);
        return file.ToArray();
    }
}
