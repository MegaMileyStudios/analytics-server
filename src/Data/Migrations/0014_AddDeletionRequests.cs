using FluentMigrator;

namespace Aptabase.Data.Migrations;

[Migration(0014)]
public class AddDeletionRequests : Migration
{
    public override void Up()
    {
        // Rows are kept after processing, a hash can be requested again once its previous request is completed
        Create.Table("deletion_requests")
            .WithNanoIdColumn("id").PrimaryKey()
            .WithColumn("hash").AsString(64).NotNullable()
            .WithColumn("requested_at").AsDateTime().NotNullable()
            .WithColumn("completed_at").AsDateTime().Nullable()
            .WithColumn("status").AsString(20).NotNullable().WithDefaultValue("pending"); // 'pending' or 'completed'

        Create.Index("idx_deletion_requests_hash")
            .OnTable("deletion_requests")
            .OnColumn("hash");

        Create.Index("idx_deletion_requests_status")
            .OnTable("deletion_requests")
            .OnColumn("status");

        // At most one pending request per hash, so repeated clicks don't pile up
        Execute.Sql("CREATE UNIQUE INDEX idx_deletion_requests_pending_hash ON deletion_requests (hash) WHERE status = 'pending'");
    }

    public override void Down()
    {
        Delete.Table("deletion_requests");
    }
}
