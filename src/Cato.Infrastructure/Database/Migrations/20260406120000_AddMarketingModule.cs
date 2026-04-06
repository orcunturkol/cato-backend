using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Users ──
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true);

            // ── UserProfile ──
            migrationBuilder.CreateTable(
                name: "user_profile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Bio = table.Column<string>(type: "text", nullable: true),
                    AvatarUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_profile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_profile_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_profile_UserId",
                table: "user_profile",
                column: "UserId");

            // ── MarketingTarget ──
            migrationBuilder.CreateTable(
                name: "marketing_target",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ContactTwitter = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ContactDiscord = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    PreferredGenres = table.Column<string>(type: "jsonb", nullable: true),
                    PreferredTags = table.Column<string>(type: "jsonb", nullable: true),
                    AudienceSize = table.Column<int>(type: "integer", nullable: true),
                    AudienceRegion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Platform = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EngagementRate = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    CostEstimateUsd = table.Column<decimal>(type: "numeric(15,2)", nullable: true),
                    LastContacted = table.Column<DateOnly>(type: "date", nullable: true),
                    ResponseRate = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_target", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_marketing_target_name",
                table: "marketing_target",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "idx_marketing_target_platform",
                table: "marketing_target",
                column: "Platform");

            migrationBuilder.CreateIndex(
                name: "idx_marketing_target_type",
                table: "marketing_target",
                column: "TargetType");

            // ── Action ──
            migrationBuilder.CreateTable(
                name: "action",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DecisionSource = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Manual"),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Planned"),
                    PlannedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ActionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CompletionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: false),
                    BudgetUsd = table.Column<decimal>(type: "numeric(15,2)", nullable: true),
                    ActualCostUsd = table.Column<decimal>(type: "numeric(15,2)", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_action_date",
                table: "action",
                column: "ActionDate");

            migrationBuilder.CreateIndex(
                name: "idx_action_planned_date",
                table: "action",
                column: "PlannedDate");

            migrationBuilder.CreateIndex(
                name: "idx_action_status",
                table: "action",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "idx_action_type",
                table: "action",
                column: "ActionType");

            // ── GameAction ──
            migrationBuilder.CreateTable(
                name: "game_action",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameRole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Primary"),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_action", x => x.Id);
                    table.ForeignKey(
                        name: "FK_game_action_action_ActionId",
                        column: x => x.ActionId,
                        principalTable: "action",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_game_action_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_game_action_action",
                table: "game_action",
                column: "ActionId");

            migrationBuilder.CreateIndex(
                name: "idx_game_action_game",
                table: "game_action",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "unique_game_action",
                table: "game_action",
                columns: new[] { "ActionId", "GameId" },
                unique: true);

            // ── ActionTarget ──
            migrationBuilder.CreateTable(
                name: "action_target",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    OutreachDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ResponseDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Planned"),
                    DeliverableUrl = table.Column<string>(type: "text", nullable: true),
                    Views = table.Column<int>(type: "integer", nullable: true),
                    Engagement = table.Column<int>(type: "integer", nullable: true),
                    CostUsd = table.Column<decimal>(type: "numeric(15,2)", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_target", x => x.Id);
                    table.ForeignKey(
                        name: "FK_action_target_action_ActionId",
                        column: x => x.ActionId,
                        principalTable: "action",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_action_target_marketing_target_TargetId",
                        column: x => x.TargetId,
                        principalTable: "marketing_target",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_action_target_action",
                table: "action_target",
                column: "ActionId");

            migrationBuilder.CreateIndex(
                name: "idx_action_target_status",
                table: "action_target",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "idx_action_target_target",
                table: "action_target",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "unique_action_target",
                table: "action_target",
                columns: new[] { "ActionId", "TargetId" },
                unique: true);

            // ── ActionImpact ──
            migrationBuilder.CreateTable(
                name: "action_impact",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeasurementStart = table.Column<DateOnly>(type: "date", nullable: true),
                    MeasurementEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    BaselineStart = table.Column<DateOnly>(type: "date", nullable: true),
                    BaselineEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    BaselineWishlistAdds = table.Column<int>(type: "integer", nullable: true),
                    ResultWishlistAdds = table.Column<int>(type: "integer", nullable: true),
                    WishlistChange = table.Column<int>(type: "integer", nullable: true),
                    WishlistChangePercent = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    BaselineSalesUnits = table.Column<int>(type: "integer", nullable: true),
                    ResultSalesUnits = table.Column<int>(type: "integer", nullable: true),
                    SalesUnitsChange = table.Column<int>(type: "integer", nullable: true),
                    SalesChangePercent = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    BaselineRevenueUsd = table.Column<decimal>(type: "numeric(15,2)", nullable: true),
                    ResultRevenueUsd = table.Column<decimal>(type: "numeric(15,2)", nullable: true),
                    RevenueChangeUsd = table.Column<decimal>(type: "numeric(15,2)", nullable: true),
                    RevenueChangePercent = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    BaselineTraffic = table.Column<int>(type: "integer", nullable: true),
                    ResultTraffic = table.Column<int>(type: "integer", nullable: true),
                    TrafficChange = table.Column<int>(type: "integer", nullable: true),
                    TrafficChangePercent = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    BaselineConversionRate = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    ResultConversionRate = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    ConversionRateChange = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    TotalCostUsd = table.Column<decimal>(type: "numeric(15,2)", nullable: true),
                    Roi = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CalculatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CalculatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_impact", x => x.Id);
                    table.ForeignKey(
                        name: "FK_action_impact_action_ActionId",
                        column: x => x.ActionId,
                        principalTable: "action",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_action_impact_roi",
                table: "action_impact",
                column: "Roi");

            migrationBuilder.CreateIndex(
                name: "unique_action_impact",
                table: "action_impact",
                column: "ActionId",
                unique: true);

            // ── TargetMatch ──
            migrationBuilder.CreateTable(
                name: "target_match",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    LifecycleStage = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RelevanceScore = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    GenreMatchScore = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    TagMatchScore = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    HistoricalPerformanceScore = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    SampleSize = table.Column<int>(type: "integer", nullable: false),
                    MatchingGenres = table.Column<string>(type: "jsonb", nullable: true),
                    MatchingTags = table.Column<string>(type: "jsonb", nullable: true),
                    CalculatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_target_match", x => x.Id);
                    table.ForeignKey(
                        name: "FK_target_match_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_target_match_marketing_target_TargetId",
                        column: x => x.TargetId,
                        principalTable: "marketing_target",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_target_match_game",
                table: "target_match",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "idx_target_match_score",
                table: "target_match",
                column: "RelevanceScore");

            migrationBuilder.CreateIndex(
                name: "idx_target_match_target",
                table: "target_match",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "unique_target_match",
                table: "target_match",
                columns: new[] { "GameId", "TargetId", "LifecycleStage" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "target_match");
            migrationBuilder.DropTable(name: "action_impact");
            migrationBuilder.DropTable(name: "action_target");
            migrationBuilder.DropTable(name: "game_action");
            migrationBuilder.DropTable(name: "action");
            migrationBuilder.DropTable(name: "marketing_target");
            migrationBuilder.DropTable(name: "user_profile");
            migrationBuilder.DropTable(name: "users");
        }
    }
}
