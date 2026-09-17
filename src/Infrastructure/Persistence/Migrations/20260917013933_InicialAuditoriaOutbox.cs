using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CoreFid.Auditoria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InicialAuditoriaOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auditoria",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    codigo_empresa = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    codigo_modulo = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    codigo_transaccion = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    usuario = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    entidad = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    clave_entidad = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    accion = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    valor_anterior = table.Column<string>(type: "text", nullable: true),
                    valor_nuevo = table.Column<string>(type: "text", nullable: true),
                    direccion_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    canal = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    fecha_registro = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auditoria", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_empresa_fecha",
                table: "auditoria",
                columns: new[] { "codigo_empresa", "fecha_registro" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_empresa_usuario",
                table: "auditoria",
                columns: new[] { "codigo_empresa", "usuario" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pendientes",
                table: "outbox_messages",
                columns: new[] { "next_attempt_at", "occurred_at" },
                filter: "processed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auditoria");

            migrationBuilder.DropTable(
                name: "outbox_messages");
        }
    }
}
