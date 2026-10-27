using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DocViewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "search_audit",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    trace_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    vin_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    cache_hit = table.Column<bool>(type: "boolean", nullable: false),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    source_status = table.Column<string>(type: "jsonb", nullable: false),
                    doc_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_audit", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "source_snapshot",
                columns: table => new
                {
                    vin = table.Column<string>(type: "character(17)", fixedLength: true, maxLength: 17, nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    documents = table.Column<string>(type: "jsonb", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_snapshot", x => new { x.vin, x.source });
                });

            migrationBuilder.CreateIndex(
                name: "ix_search_audit_created_at",
                table: "search_audit",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "search_audit");

            migrationBuilder.DropTable(
                name: "source_snapshot");
        }
    }
}
