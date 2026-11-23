using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduMonitor.Analytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class Sprint3_AgenteRecomendacionesReportes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsultasAgente",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FechaUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DocenteEmail = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    MateriaId = table.Column<int>(type: "int", nullable: false),
                    Pregunta = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    Intencion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Modelo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModeloDisponible = table.Column<bool>(type: "bit", nullable: false),
                    DuracionMs = table.Column<int>(type: "int", nullable: false),
                    Resultado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsultasAgente", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvocacionesHerramienta",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConsultaId = table.Column<long>(type: "bigint", nullable: false),
                    FechaUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Herramienta = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Parametros = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Exito = table.Column<bool>(type: "bit", nullable: false),
                    Resumen = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DuracionMs = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvocacionesHerramienta", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Recomendaciones",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MateriaId = table.Column<int>(type: "int", nullable: false),
                    EstudianteId = table.Column<int>(type: "int", nullable: false),
                    Nivel = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    NotaPonderada = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    Evidencia = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Contenido = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Fuente = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    GeneradaUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeneradaPor = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    EditadaUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EditadaPor = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    AprobadaUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AprobadaPor = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    EnviadaUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EnviadaA = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recomendaciones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReportesCurso",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MateriaId = table.Column<int>(type: "int", nullable: false),
                    GeneradoUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeneradoPor = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Contenido = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportesCurso", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsultasAgente_MateriaId_FechaUtc",
                table: "ConsultasAgente",
                columns: new[] { "MateriaId", "FechaUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InvocacionesHerramienta_ConsultaId",
                table: "InvocacionesHerramienta",
                column: "ConsultaId");

            migrationBuilder.CreateIndex(
                name: "IX_Recomendaciones_MateriaId_EstudianteId",
                table: "Recomendaciones",
                columns: new[] { "MateriaId", "EstudianteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportesCurso_MateriaId_GeneradoUtc",
                table: "ReportesCurso",
                columns: new[] { "MateriaId", "GeneradoUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsultasAgente");

            migrationBuilder.DropTable(
                name: "InvocacionesHerramienta");

            migrationBuilder.DropTable(
                name: "Recomendaciones");

            migrationBuilder.DropTable(
                name: "ReportesCurso");
        }
    }
}
