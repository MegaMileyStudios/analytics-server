using Xunit;
using AwesomeAssertions;
using Aptabase.Features.Symbolication.Engine;
using Aptabase.Features.Symbolication.Store;

namespace Aptabase.UnitTests.Features.Symbolication;

// Parsing of what the LLVM tools print, with output captured from BallsyDrop's real symbols
public class ToolOutputTests
{
    [Fact]
    public void Elf_Build_Id_And_Debug_Info()
    {
        const string notes = """

            Displaying notes found in: .note.gnu.build-id
              Owner                Data size 	Description
              GNU                  0x00000014	NT_GNU_BUILD_ID (unique build ID bitstring)
                Build ID: c4a9dc3c93f34990366feedf5dd3265549f37070
            """;
        const string sections = """
            Section Headers:
              [34] .debug_info       PROGBITS        0000000000000000 0003c8 2a58a7 00   C  0   0  8
              [46] .symtab           SYMTAB          0000000000000000 5239f0 581b20 18     47 240344  8
            """;

        SymbolFileInspector.ParseElfBuildId(notes).Should().Be("c4a9dc3c93f34990366feedf5dd3265549f37070");
        SymbolFileInspector.HasElfDebugInfo(sections).Should().BeTrue();
        SymbolFileInspector.HasElfDebugInfo("  [46] .symtab SYMTAB 0000").Should().BeFalse();
    }

    [Fact]
    public void Pdb_Summary()
    {
        const string summary = """
                                      Summary
            ============================================================
              Block Size: 4096
              Signature: 1793184808
              Age: 1
              GUID: {5D208BED-4385-4D76-B914-23A570D498C7}
              Features: 0x1
            """;

        var identity = SymbolFileInspector.ParsePdbSummary(summary)!.Value;

        SymbolFileInspector.PdbDebugId(identity.Guid, identity.Age).Should().Be("5D208BED43854D76B91423A570D498C71");
    }

    [Fact]
    public void MachO_Uuids_Per_Architecture_And_Text_Address()
    {
        const string uuids = """
            UUID: D5AFCC57-C739-3F1E-A482-E9F5B2BF9887 (arm64) BallsyDrop.dylib
            UUID: 0F0E0D0C-0B0A-0908-0706-050403020100 (x86_64) BallsyDrop.dylib
            """;
        const string headers = """
            Load command 1
                  cmd LC_SEGMENT_64
              cmdsize 1192
              segname __TEXT
               vmaddr 0x0000000100000000
               vmsize 0x0000000001bb8000
            Section
              sectname __text
               segname __TEXT
                  addr 0x0000000100004000
            """;

        SymbolFileInspector.ParseDwarfUuids(uuids).Should().BeEquivalentTo([
            ("D5AFCC57-C739-3F1E-A482-E9F5B2BF9887", "arm64"),
            ("0F0E0D0C-0B0A-0908-0706-050403020100", "x86_64"),
        ]);
        SymbolFileInspector.ParseMachOTextVmAddr(headers).Should().Be(0x100000000);
    }

    [Theory]
    [InlineData("osx-arm64/BallsyDrop.dylib.dSYM/Contents/Resources/DWARF/BallsyDrop.dylib", true)]
    [InlineData("godot.macos.template_release.arm64.mono.dSYM/Contents/Resources/DWARF/godot.macos.template_release.arm64.mono", true)]
    [InlineData("osx-arm64/BallsyDrop.dylib", false)]
    [InlineData("osx-arm64/BallsyDrop.dylib.dSYM/Contents/Resources/Relocations/aarch64/BallsyDrop.dylib.yml", false)]
    public void Dsym_Dwarf_Paths(string path, bool expected)
    {
        SymbolFileInspector.IsDsymDwarfPath(path).Should().Be(expected);
    }

    [Fact]
    public void Elf_Architecture_From_The_Header()
    {
        var header = new byte[64];
        header[18] = 0x3E;
        SymbolFileInspector.ElfArch(header).Should().Be("x86_64");
        header[18] = 0xB7;
        SymbolFileInspector.ElfArch(header).Should().Be("aarch64");
    }

    [Fact]
    public void Elf_Function_Table_Keeps_Code_Symbols()
    {
        const string nm = """
            0000000000435ec0 0000000000000028 t BallsyDrop_BallsyDrop_Analytics_ErrorReportingTest___c___Trigger_b__0_0
            0000000000c106a0 00000000000001bc T S_P_CoreLib_System_Threading_Thread__StartThread
            0000000001000000 0000000000000010 d SomeData
            0000000000c10af0 t NoSize
            """;

        var table = FunctionTable.ParseElfNm(nm);

        table.Functions.Select(f => f.Name).Should().Equal(
            "BallsyDrop_BallsyDrop_Analytics_ErrorReportingTest___c___Trigger_b__0_0",
            "S_P_CoreLib_System_Threading_Thread__StartThread",
            "NoSize");
        table.Functions[1].Size.Should().Be(0x1bc);
    }

    [Fact]
    public void MachO_Function_Table_Keeps_Text_Symbols_And_Derives_Sizes()
    {
        const string nm = """
            0000000000364140 (__TEXT,__managedcode) non-external (was a private external) _BallsyDrop_BallsyDrop_Analytics_ErrorReportingTest___c___Trigger_b__0_0
            0000000000364168 (__TEXT,__managedcode) non-external (was a private external) _BallsyDrop_BallsyDrop_Analytics_ErrorReportingTest___c___Trigger_b__0_1
            0000000001c029da (__DATA,.dotnet_eh_table) non-external (was a private external) _lsda0_BallsyDrop_BallsyDrop_Analytics_ErrorReportingTest___c___Trigger_b__0_0
            """;

        var table = FunctionTable.ParseMachONm(nm);

        table.Functions.Should().HaveCount(2);
        table.Functions[0].Should().Be(new FunctionSymbol(0x364140, 0x28, "BallsyDrop_BallsyDrop_Analytics_ErrorReportingTest___c___Trigger_b__0_0"));
    }

    [Fact]
    public void Pdb_Function_Table_Uses_Code_Sections_And_The_Image_Base()
    {
        const string dump = """
                                   Public Symbols
            ============================================================
              Records
              31932152 | S_PUB32 [size = 88] `BallsyDrop_BallsyDrop_Analytics_ErrorReportingTest___c___Trigger_b__0_0`
                       flags = none, addr = 0001:3213392
              17414180 | S_PUB32 [size = 96] `_unwind0BallsyDrop_BallsyDrop_Analytics_ErrorReportingTest___c___Trigger_b__0_0`
                       flags = none, addr = 0002:8947832

                                  Section Headers
            ============================================================

              SECTION HEADER #1
                 .text name
                F3E168 virtual size
                  1000 virtual address
              60000020 flags
                       IMAGE_SCN_CNT_CODE
                       IMAGE_SCN_MEM_EXECUTE

              SECTION HEADER #2
                .rdata name
                995CE0 virtual size
                F40000 virtual address
              40000040 flags
                       IMAGE_SCN_CNT_INITIALIZED_DATA
            """;

        var table = FunctionTable.ParsePdbDump(dump, PdbStub.ImageBase);

        table.Functions.Should().ContainSingle().Which.Address.Should().Be(PdbStub.ImageBase + 0x1000 + 3213392);
    }

    [Fact]
    public void Function_Table_Survives_A_Save_And_Load()
    {
        var table = new FunctionTable([new FunctionSymbol(0x10, 0x8, "a"), new FunctionSymbol(0x18, 0x4, "b_c")]);
        var path = Path.GetTempFileName();
        try
        {
            table.Save(path);
            FunctionTable.Load(path).Functions.Should().Equal(table.Functions);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Symbolizer_Json_Keeps_Inlined_Frames_And_Errors()
    {
        const string json = """
            [{"Address":"0x24871a2","ModuleName":"x","Symbol":[
                {"Column":5,"FileName":"/workspace/a/b/core/templates/list.h","FunctionName":"List<String>::List","Line":253},
                {"Column":0,"FileName":"/workspace/a/b/core/core_bind.cpp","FunctionName":"CoreBind::OS::crash(String const&)","Line":349}]},
             {"Address":"0x1","ModuleName":"x","Symbol":[{"FileName":"","FunctionName":"??","Line":0}]},
             {"Address":"0x2","Error":{"Message":"No such file or directory"},"ModuleName":"x"}]
            """;

        var results = LlvmSymbolizer.ParseJson(json);

        results.Should().HaveCount(3);
        results[0].Locations.Select(l => l.FunctionName).Should().Equal("List<String>::List", "CoreBind::OS::crash(String const&)");
        results[0].Locations[1].Line.Should().Be(349);
        results[1].Locations.Should().BeEmpty();
        results[2].Error.Should().Be("No such file or directory");
    }
}
