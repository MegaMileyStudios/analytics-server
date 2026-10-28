using System.Security.Cryptography;
using System.Text;
using Aptabase.Data;
using Dapper;

namespace Aptabase.Features.Symbolication.Store;

public class SymbolUploadKey
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string KeyPrefix { get; set; } = "";
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public class StoredSymbolFile
{
    public string Id { get; set; } = "";
    public string UploadId { get; set; } = "";
    public string DebugId { get; set; } = "";
    public string Format { get; set; } = "";
    public string? Arch { get; set; }
    public string? Rid { get; set; }
    public string? BuildId { get; set; }
    public string ModuleName { get; set; } = "";
    public string Path { get; set; } = "";
    public long ImageBase { get; set; }
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SymbolUpload
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? BuildId { get; set; }
    public string? KeyName { get; set; }
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public StoredSymbolFile[] Files { get; set; } = [];
}

public record NewSymbolFile(string DebugId, string Format, string? Arch, string? Rid, string ModuleName, string Path, ulong ImageBase, long SizeBytes);

// What changed in the store since a point in time, used to re-symbolicate reports that were missing these symbols
public record SymbolStoreChanges(string[] Keys, DateTime? LastModifiedAt);

public interface ISymbolStore
{
    Task<bool> CanManageSymbols(string userId, CancellationToken cancellationToken);

    Task<SymbolUploadKey[]> ListKeys(CancellationToken cancellationToken);
    Task<(SymbolUploadKey Key, string Secret)> CreateKey(string name, string userId, CancellationToken cancellationToken);
    Task<bool> DeleteKey(string id, CancellationToken cancellationToken);
    Task<string?> UseKey(string secret, CancellationToken cancellationToken);

    Task<SymbolUpload[]> ListUploads(CancellationToken cancellationToken);
    Task SaveUpload(string uploadId, string name, string? buildId, string keyId, long sizeBytes, IReadOnlyList<NewSymbolFile> files, CancellationToken cancellationToken);
    Task<bool> DeleteUpload(string uploadId, CancellationToken cancellationToken);

    Task<StoredSymbolFile[]> FindByDebugIds(string[] debugIds, CancellationToken cancellationToken);
    Task<StoredSymbolFile?> FindByBuild(string buildId, string rid, CancellationToken cancellationToken);
    Task<string[]> AssignBuildId(string[] debugIds, string buildId, CancellationToken cancellationToken);
    Task<SymbolStoreChanges> ChangesSince(DateTime? since, CancellationToken cancellationToken);
}

public class SymbolStore : ISymbolStore
{
    private const string KeyPrefix = "SYM-";
    private const string KeyAlphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private const string FileColumns = @"f.id, f.upload_id, f.debug_id, f.format, f.arch, f.rid, f.build_id,
                                         f.module_name, f.path, f.image_base, f.size_bytes, f.created_at";

    private readonly IDbContext _db;

    public SymbolStore(IDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    // The store is shared by the whole server, any app owner can manage it
    public async Task<bool> CanManageSymbols(string userId, CancellationToken cancellationToken)
    {
        var cmd = new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM apps WHERE owner_id = @userId AND deleted_at IS NULL)",
            new { userId }, cancellationToken: cancellationToken);
        return await _db.Connection.ExecuteScalarAsync<bool>(cmd);
    }

    public async Task<SymbolUploadKey[]> ListKeys(CancellationToken cancellationToken)
    {
        var cmd = new CommandDefinition(@"
            SELECT k.id, k.name, k.key_prefix, u.name AS created_by_name, k.created_at, k.last_used_at
            FROM symbol_upload_keys k
            INNER JOIN users u ON u.id = k.created_by
            ORDER BY k.created_at DESC", cancellationToken: cancellationToken);
        return (await _db.Connection.QueryAsync<SymbolUploadKey>(cmd)).ToArray();
    }

    public async Task<(SymbolUploadKey Key, string Secret)> CreateKey(string name, string userId, CancellationToken cancellationToken)
    {
        var secret = KeyPrefix + RandomNumberGenerator.GetString(KeyAlphabet, 40);
        var id = NanoId.New();
        var cmd = new CommandDefinition(@"
            INSERT INTO symbol_upload_keys (id, name, key_hash, key_prefix, created_by)
            VALUES (@id, @name, @hash, @prefix, @userId)",
            new { id, name, hash = HashKey(secret), prefix = secret[..12], userId }, cancellationToken: cancellationToken);
        await _db.Connection.ExecuteAsync(cmd);

        var key = (await ListKeys(cancellationToken)).Single(k => k.Id == id);
        return (key, secret);
    }

    public async Task<bool> DeleteKey(string id, CancellationToken cancellationToken)
    {
        var cmd = new CommandDefinition("DELETE FROM symbol_upload_keys WHERE id = @id", new { id }, cancellationToken: cancellationToken);
        return await _db.Connection.ExecuteAsync(cmd) > 0;
    }

    // Returns the key's id, or null when the key doesn't exist (anymore)
    public async Task<string?> UseKey(string secret, CancellationToken cancellationToken)
    {
        var cmd = new CommandDefinition(@"
            UPDATE symbol_upload_keys SET last_used_at = now()
            WHERE key_hash = @hash
            RETURNING id", new { hash = HashKey(secret) }, cancellationToken: cancellationToken);
        return await _db.Connection.ExecuteScalarAsync<string?>(cmd);
    }

    public async Task<SymbolUpload[]> ListUploads(CancellationToken cancellationToken)
    {
        var uploadsCmd = new CommandDefinition(@"
            SELECT s.id, s.name, s.build_id, k.name AS key_name, s.size_bytes, s.created_at
            FROM symbol_uploads s
            LEFT JOIN symbol_upload_keys k ON k.id = s.key_id
            ORDER BY s.created_at DESC", cancellationToken: cancellationToken);
        var filesCmd = new CommandDefinition($"SELECT {FileColumns} FROM symbol_files f ORDER BY f.path, f.arch", cancellationToken: cancellationToken);

        using var conn = _db.Connection;
        var uploads = (await conn.QueryAsync<SymbolUpload>(uploadsCmd)).ToArray();
        var files = (await conn.QueryAsync<StoredSymbolFile>(filesCmd)).ToLookup(f => f.UploadId);
        foreach (var upload in uploads)
            upload.Files = files[upload.Id].ToArray();
        return uploads;
    }

    public async Task SaveUpload(string uploadId, string name, string? buildId, string keyId, long sizeBytes, IReadOnlyList<NewSymbolFile> files, CancellationToken cancellationToken)
    {
        using var conn = _db.Connection;
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        await conn.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO symbol_uploads (id, name, build_id, key_id, size_bytes)
            VALUES (@uploadId, @name, @buildId, @keyId, @sizeBytes)",
            new { uploadId, name, buildId, keyId, sizeBytes }, tx, cancellationToken: cancellationToken));

        foreach (var file in files)
        {
            await conn.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO symbol_files (id, upload_id, debug_id, format, arch, rid, build_id, module_name, path, image_base, size_bytes)
                VALUES (@id, @uploadId, @DebugId, @Format, @Arch, @Rid, @buildId, @ModuleName, @Path, @imageBase, @SizeBytes)",
                new { id = NanoId.New(), uploadId, file.DebugId, file.Format, file.Arch, file.Rid, buildId, file.ModuleName, file.Path, imageBase = (long)file.ImageBase, file.SizeBytes },
                tx, cancellationToken: cancellationToken));
        }

        await tx.CommitAsync(cancellationToken);
    }

    public async Task<bool> DeleteUpload(string uploadId, CancellationToken cancellationToken)
    {
        var cmd = new CommandDefinition("DELETE FROM symbol_uploads WHERE id = @uploadId", new { uploadId }, cancellationToken: cancellationToken);
        return await _db.Connection.ExecuteAsync(cmd) > 0;
    }

    public async Task<StoredSymbolFile[]> FindByDebugIds(string[] debugIds, CancellationToken cancellationToken)
    {
        if (debugIds.Length == 0)
            return [];

        var cmd = new CommandDefinition($"SELECT {FileColumns} FROM symbol_files f WHERE f.debug_id = ANY(@debugIds)",
            new { debugIds }, cancellationToken: cancellationToken);
        return (await _db.Connection.QueryAsync<StoredSymbolFile>(cmd)).ToArray();
    }

    public async Task<StoredSymbolFile?> FindByBuild(string buildId, string rid, CancellationToken cancellationToken)
    {
        var cmd = new CommandDefinition($@"
            SELECT {FileColumns} FROM symbol_files f
            WHERE f.build_id = @buildId AND f.rid = @rid
            ORDER BY f.created_at DESC
            LIMIT 1", new { buildId, rid }, cancellationToken: cancellationToken);
        return await _db.Connection.QueryFirstOrDefaultAsync<StoredSymbolFile>(cmd);
    }

    // A file uploaded again with a game build ID (e.g. the same symbols, first uploaded without one) gets that build ID.
    // Returns the debug IDs that now have it.
    public async Task<string[]> AssignBuildId(string[] debugIds, string buildId, CancellationToken cancellationToken)
    {
        if (debugIds.Length == 0)
            return [];

        var cmd = new CommandDefinition(@"
            UPDATE symbol_files SET build_id = @buildId, modified_at = now()
            WHERE debug_id = ANY(@debugIds) AND build_id IS NULL
            RETURNING debug_id", new { debugIds, buildId }, cancellationToken: cancellationToken);
        return (await _db.Connection.QueryAsync<string>(cmd)).ToArray();
    }

    // The keys are the ones symbolication results list as missing: a debug ID, or build:<build id>/<rid>
    public async Task<SymbolStoreChanges> ChangesSince(DateTime? since, CancellationToken cancellationToken)
    {
        const string columns = "SELECT debug_id, build_id, rid, modified_at FROM symbol_files";
        var cmd = since is null
            ? new CommandDefinition(columns, cancellationToken: cancellationToken)
            : new CommandDefinition($"{columns} WHERE modified_at > @since", new { since }, cancellationToken: cancellationToken);
        var rows = (await _db.Connection.QueryAsync<SymbolFileChange>(cmd)).ToArray();

        var keys = rows.Select(r => r.DebugId)
            .Concat(rows.Where(r => r.BuildId != null && r.Rid != null).Select(r => BuildKey(r.BuildId!, r.Rid!)))
            .Distinct()
            .ToArray();
        return new SymbolStoreChanges(keys, rows.Length == 0 ? since : rows.Max(r => r.ModifiedAt));
    }

    private class SymbolFileChange
    {
        public string DebugId { get; set; } = "";
        public string? BuildId { get; set; }
        public string? Rid { get; set; }
        public DateTime ModifiedAt { get; set; }
    }

    public static string BuildKey(string buildId, string rid) => $"build:{buildId}/{rid}";

    public static string HashKey(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
