using System.Text;
using Xunit;
using AwesomeAssertions;
using Aptabase.Features.Symbolication;
using Aptabase.Features.Symbolication.Store;

namespace Aptabase.UnitTests.Features.Symbolication;

public class PdbStubTests
{
    private static readonly Guid Guid = Guid.Parse("5D208BED-4385-4D76-B914-23A570D498C7");

    [Fact]
    public void Writes_A_PE_With_The_CodeView_Record()
    {
        var stub = PdbStub.Build(Guid, 1, "BallsyDrop.pdb");

        Encoding.ASCII.GetString(stub, 0, 2).Should().Be("MZ");
        var peOffset = BitConverter.ToInt32(stub, 0x3C);
        Encoding.ASCII.GetString(stub, peOffset, 4).Should().Be("PE\0\0");
        BitConverter.ToUInt64(stub, peOffset + 24 + 24).Should().Be(PdbStub.ImageBase);

        // The CodeView record follows the 28-byte debug directory at the start of the only section
        var codeView = 0x200 + 28;
        Encoding.ASCII.GetString(stub, codeView, 4).Should().Be("RSDS");
        new Guid(stub.AsSpan(codeView + 4, 16)).Should().Be(Guid);
        BitConverter.ToUInt32(stub, codeView + 20).Should().Be(1);
        Encoding.UTF8.GetString(stub, codeView + 24, "BallsyDrop.pdb".Length).Should().Be("BallsyDrop.pdb");
        (stub.Length % 0x200).Should().Be(0);
    }

    [RequiresLlvmFact]
    public async Task Llvm_Reads_The_Pdb_Id_And_Image_Base_Back()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.stub.exe");
        await File.WriteAllBytesAsync(path, PdbStub.Build(Guid, 3, "BallsyDrop.pdb"));
        try
        {
            var result = await ExternalProcess.RunAsync("llvm-readobj-18", ["--file-headers", "--coff-debug-directory", path], TimeSpan.FromSeconds(30), CancellationToken.None);

            result.ExitCode.Should().Be(0);
            result.StandardOutput.Should().Contain("ImageBase: 0x140000000");
            result.StandardOutput.Should().Contain("PDBGUID: (ED 8B 20 5D 85 43 76 4D B9 14 23 A5 70 D4 98 C7)");
            result.StandardOutput.Should().Contain("PDBAge: 3");
            result.StandardOutput.Should().Contain("PDBFileName: BallsyDrop.pdb");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
