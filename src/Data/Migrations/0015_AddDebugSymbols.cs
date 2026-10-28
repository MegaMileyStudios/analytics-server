using FluentMigrator;

namespace Aptabase.Data.Migrations;

[Migration(0015)]
public class AddDebugSymbols : Migration
{
    public override void Up()
    {
        // Keys CI uses to upload symbols. Only the SHA-256 hash of a key is stored, the key itself is shown once.
        Create.Table("symbol_upload_keys")
            .WithNanoIdColumn("id").PrimaryKey()
            .WithColumn("name").AsString(100).NotNullable()
            .WithColumn("key_hash").AsString(64).NotNullable().Unique()
            .WithColumn("key_prefix").AsString(20).NotNullable()
            .WithNanoIdColumn("created_by")
                .ForeignKey("fk_symbol_upload_keys_user", "users", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("last_used_at").AsDateTimeOffset().Nullable()
            .WithTimestamps();

        Create.Table("symbol_uploads")
            .WithNanoIdColumn("id").PrimaryKey()
            .WithColumn("name").AsString(200).NotNullable()
            .WithColumn("build_id").AsString(100).Nullable()
            .WithColumn("key_id").AsString(22).Nullable()
                .ForeignKey("fk_symbol_uploads_key", "symbol_upload_keys", "id").OnDelete(System.Data.Rule.SetNull)
            .WithColumn("size_bytes").AsInt64().NotNullable()
            .WithTimestamps();

        // One row per debug ID, a universal Mach-O file has one per architecture.
        // build_id + rid find the game's symbols for C# frames, which carry no debug ID of their own.
        Create.Table("symbol_files")
            .WithNanoIdColumn("id").PrimaryKey()
            .WithNanoIdColumn("upload_id")
                .ForeignKey("fk_symbol_files_upload", "symbol_uploads", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("debug_id").AsString(64).NotNullable().Unique()
            .WithColumn("format").AsString(10).NotNullable()
            .WithColumn("arch").AsString(20).Nullable()
            .WithColumn("rid").AsString(30).Nullable()
            .WithColumn("build_id").AsString(100).Nullable()
            .WithColumn("module_name").AsString(200).NotNullable()
            .WithColumn("path").AsString(1000).NotNullable()
            .WithColumn("image_base").AsInt64().NotNullable()
            .WithColumn("size_bytes").AsInt64().NotNullable()
            .WithTimestamps();

        Create.Index("idx_symbol_files_build")
            .OnTable("symbol_files")
            .OnColumn("build_id").Ascending()
            .OnColumn("rid").Ascending();

        Create.Index("idx_symbol_files_upload")
            .OnTable("symbol_files")
            .OnColumn("upload_id");

        Create.Index("idx_symbol_files_modified")
            .OnTable("symbol_files")
            .OnColumn("modified_at");
    }

    public override void Down()
    {
        Delete.Table("symbol_files");
        Delete.Table("symbol_uploads");
        Delete.Table("symbol_upload_keys");
    }
}
