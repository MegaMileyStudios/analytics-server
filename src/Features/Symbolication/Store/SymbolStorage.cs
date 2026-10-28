namespace Aptabase.Features.Symbolication.Store;

// Where uploaded symbol files live on disk: <SYMBOLS_PATH>/<upload id>/<path inside the archive>.
// Archives are unpacked into <SYMBOLS_PATH>/staging/<upload id> first and moved into place once indexed.
public class SymbolStorage
{
    private const string StagingFolder = "staging";

    public string Root { get; }

    public SymbolStorage(EnvSettings env)
    {
        Root = Path.GetFullPath(env.SymbolsDirectoryPath);
    }

    public string CreateStagingDirectory(string uploadId)
    {
        var path = Path.Combine(Root, StagingFolder, uploadId);
        Directory.CreateDirectory(path);
        return path;
    }

    public string UploadDirectory(string uploadId) => Path.Combine(Root, uploadId);

    public string FullPath(string relativePath) => Path.Combine(Root, relativePath);

    public void MoveIntoPlace(string stagingDirectory, string uploadId)
    {
        Directory.CreateDirectory(Root);
        Directory.Move(stagingDirectory, UploadDirectory(uploadId));
    }

    public void DeleteUpload(string uploadId) => DeleteDirectory(UploadDirectory(uploadId));

    public void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    // Leftovers of uploads that were interrupted by a restart
    public void CleanStaging() => DeleteDirectory(Path.Combine(Root, StagingFolder));
}
