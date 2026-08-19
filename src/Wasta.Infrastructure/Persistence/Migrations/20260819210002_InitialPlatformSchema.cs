using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wasta.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlatformSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "application_status",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_terminal = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_status", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employment_type",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employment_type", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "industry",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_industry", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "location",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    city = table.Column<string>(type: "text", nullable: false),
                    country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_location", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_method",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_method", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "skill",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_skill", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "track",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_account",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    email = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    email_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_account", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "work_type",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_type", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assessment_form",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    track_id = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    question_count = table.Column<short>(type: "smallint", nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assessment_form", x => x.id);
                    table.ForeignKey(
                        name: "fk_assessment_form_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "track",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scoring_rule_version",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    track_id = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scoring_rule_version", x => x.id);
                    table.ForeignKey(
                        name: "fk_scoring_rule_version_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "track",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "section",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    track_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_section", x => x.id);
                    table.ForeignKey(
                        name: "fk_section_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "track",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    actor_user_id = table.Column<long>(type: "bigint", nullable: true),
                    action = table.Column<string>(type: "text", nullable: false),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<string>(type: "text", nullable: false),
                    detail = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_log_user_accounts_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "user_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "company",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    normalized_name = table.Column<string>(type: "text", nullable: false),
                    website = table.Column<string>(type: "text", nullable: true),
                    company_size = table.Column<string>(type: "text", nullable: true),
                    industry_id = table.Column<int>(type: "integer", nullable: true),
                    verification_state = table.Column<int>(type: "integer", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verified_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    rejection_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company", x => x.id);
                    table.ForeignKey(
                        name: "fk_company_industries_industry_id",
                        column: x => x.industry_id,
                        principalTable: "industry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_company_user_accounts_user_id",
                        column: x => x.user_id,
                        principalTable: "user_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_seeker",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    full_name = table.Column<string>(type: "text", nullable: false),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    track_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_seeker", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_seeker_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "track",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_job_seeker_user_accounts_user_id",
                        column: x => x.user_id,
                        principalTable: "user_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification", x => x.id);
                    table.ForeignKey(
                        name: "fk_notification_user_accounts_user_id",
                        column: x => x.user_id,
                        principalTable: "user_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_token",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    family_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_token", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_token_user_accounts_user_id",
                        column: x => x.user_id,
                        principalTable: "user_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "score_band",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    rule_version_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    min_percent = table.Column<short>(type: "smallint", nullable: false),
                    max_percent = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_score_band", x => x.id);
                    table.ForeignKey(
                        name: "fk_score_band_scoring_rule_versions_rule_version_id",
                        column: x => x.rule_version_id,
                        principalTable: "scoring_rule_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "question",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    track_id = table.Column<int>(type: "integer", nullable: false),
                    section_id = table.Column<int>(type: "integer", nullable: false),
                    body = table.Column<string>(type: "jsonb", nullable: false),
                    difficulty = table.Column<short>(type: "smallint", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_question", x => x.id);
                    table.ForeignKey(
                        name: "fk_question_sections_section_id",
                        column: x => x.section_id,
                        principalTable: "section",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_question_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "track",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "section_weight",
                columns: table => new
                {
                    rule_version_id = table.Column<int>(type: "integer", nullable: false),
                    section_id = table.Column<int>(type: "integer", nullable: false),
                    weight = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_section_weight", x => new { x.rule_version_id, x.section_id });
                    table.ForeignKey(
                        name: "fk_section_weight_scoring_rule_version_rule_version_id",
                        column: x => x.rule_version_id,
                        principalTable: "scoring_rule_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_section_weight_section_section_id",
                        column: x => x.section_id,
                        principalTable: "section",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_document",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    document_type = table.Column<int>(type: "integer", nullable: false),
                    file_url = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_document", x => x.id);
                    table.ForeignKey(
                        name: "fk_company_document_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "credit_ledger_entry",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    delta = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<int>(type: "integer", nullable: false),
                    balance_after = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<long>(type: "bigint", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_ledger_entry", x => x.id);
                    table.ForeignKey(
                        name: "fk_credit_ledger_entry_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_credit_ledger_entry_user_accounts_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "user_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "job_post",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    track_id = table.Column<int>(type: "integer", nullable: false),
                    work_type_id = table.Column<int>(type: "integer", nullable: true),
                    location_id = table.Column<int>(type: "integer", nullable: true),
                    employment_type_id = table.Column<int>(type: "integer", nullable: true),
                    salary_min = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    salary_max = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    salary_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    salary_period = table.Column<string>(type: "text", nullable: true),
                    job_description = table.Column<string>(type: "text", nullable: false),
                    project_brief = table.Column<string>(type: "text", nullable: true),
                    project_deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closes_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_post", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_post_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_job_post_employment_type_employment_type_id",
                        column: x => x.employment_type_id,
                        principalTable: "employment_type",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_job_post_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "location",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_job_post_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "track",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_job_post_work_types_work_type_id",
                        column: x => x.work_type_id,
                        principalTable: "work_type",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "attempt",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_seeker_id = table.Column<long>(type: "bigint", nullable: false),
                    form_id = table.Column<int>(type: "integer", nullable: false),
                    track_id = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attempt", x => x.id);
                    table.ForeignKey(
                        name: "fk_attempt_assessment_form_form_id",
                        column: x => x.form_id,
                        principalTable: "assessment_form",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attempt_job_seekers_job_seeker_id",
                        column: x => x.job_seeker_id,
                        principalTable: "job_seeker",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_attempt_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "track",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_seeker_profile",
                columns: table => new
                {
                    job_seeker_id = table.Column<long>(type: "bigint", nullable: false),
                    bio = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    university = table.Column<string>(type: "text", nullable: true),
                    graduation_year = table.Column<short>(type: "smallint", nullable: true),
                    availability = table.Column<string>(type: "text", nullable: true),
                    preferred_work_type_id = table.Column<int>(type: "integer", nullable: true),
                    cv_url = table.Column<string>(type: "text", nullable: true),
                    cv_uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    visible_to_companies = table.Column<bool>(type: "boolean", nullable: false),
                    profile_strength = table.Column<short>(type: "smallint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_seeker_profile", x => x.job_seeker_id);
                    table.ForeignKey(
                        name: "fk_job_seeker_profile_job_seeker_job_seeker_id",
                        column: x => x.job_seeker_id,
                        principalTable: "job_seeker",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_job_seeker_profile_work_types_preferred_work_type_id",
                        column: x => x.preferred_work_type_id,
                        principalTable: "work_type",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "job_seeker_skill",
                columns: table => new
                {
                    job_seeker_id = table.Column<long>(type: "bigint", nullable: false),
                    skill_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_seeker_skill", x => new { x.job_seeker_id, x.skill_id });
                    table.ForeignKey(
                        name: "fk_job_seeker_skill_job_seeker_job_seeker_id",
                        column: x => x.job_seeker_id,
                        principalTable: "job_seeker",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_job_seeker_skill_skills_skill_id",
                        column: x => x.skill_id,
                        principalTable: "skill",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "section_band_feedback",
                columns: table => new
                {
                    section_id = table.Column<int>(type: "integer", nullable: false),
                    band_id = table.Column<int>(type: "integer", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_section_band_feedback", x => new { x.section_id, x.band_id });
                    table.ForeignKey(
                        name: "fk_section_band_feedback_score_band_band_id",
                        column: x => x.band_id,
                        principalTable: "score_band",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_section_band_feedback_sections_section_id",
                        column: x => x.section_id,
                        principalTable: "section",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "assessment_form_question",
                columns: table => new
                {
                    form_id = table.Column<int>(type: "integer", nullable: false),
                    question_id = table.Column<long>(type: "bigint", nullable: false),
                    display_order = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assessment_form_question", x => new { x.form_id, x.question_id });
                    table.ForeignKey(
                        name: "fk_assessment_form_question_assessment_form_form_id",
                        column: x => x.form_id,
                        principalTable: "assessment_form",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_assessment_form_question_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "question",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "question_option",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    question_id = table.Column<long>(type: "bigint", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    is_correct = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_question_option", x => x.id);
                    table.ForeignKey(
                        name: "fk_question_option_question_question_id",
                        column: x => x.question_id,
                        principalTable: "question",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "credit_topup_request",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    credits_requested = table.Column<int>(type: "integer", nullable: false),
                    payment_method_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    state = table.Column<int>(type: "integer", nullable: false),
                    reviewed_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ledger_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_topup_request", x => x.id);
                    table.ForeignKey(
                        name: "fk_credit_topup_request_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_credit_topup_request_credit_ledger_entry_ledger_entry_id",
                        column: x => x.ledger_entry_id,
                        principalTable: "credit_ledger_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_credit_topup_request_payment_methods_payment_method_id",
                        column: x => x.payment_method_id,
                        principalTable: "payment_method",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_credit_topup_request_user_accounts_reviewed_by_user_id",
                        column: x => x.reviewed_by_user_id,
                        principalTable: "user_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "profile_unlock",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    job_seeker_id = table.Column<long>(type: "bigint", nullable: false),
                    ledger_entry_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profile_unlock", x => x.id);
                    table.ForeignKey(
                        name: "fk_profile_unlock_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_profile_unlock_credit_ledger_entry_ledger_entry_id",
                        column: x => x.ledger_entry_id,
                        principalTable: "credit_ledger_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_profile_unlock_job_seeker_job_seeker_id",
                        column: x => x.job_seeker_id,
                        principalTable: "job_seeker",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_application",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_seeker_id = table.Column<long>(type: "bigint", nullable: false),
                    job_post_id = table.Column<long>(type: "bigint", nullable: false),
                    status_id = table.Column<int>(type: "integer", nullable: false),
                    project_title = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    repo_url = table.Column<string>(type: "text", nullable: true),
                    live_demo_url = table.Column<string>(type: "text", nullable: true),
                    feedback = table.Column<string>(type: "text", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_application", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_application_application_status_status_id",
                        column: x => x.status_id,
                        principalTable: "application_status",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_job_application_job_posts_job_post_id",
                        column: x => x.job_post_id,
                        principalTable: "job_post",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_job_application_job_seekers_job_seeker_id",
                        column: x => x.job_seeker_id,
                        principalTable: "job_seeker",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_post_file",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_post_id = table.Column<long>(type: "bigint", nullable: false),
                    file_url = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_post_file", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_post_file_job_post_job_post_id",
                        column: x => x.job_post_id,
                        principalTable: "job_post",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_post_skill",
                columns: table => new
                {
                    job_post_id = table.Column<long>(type: "bigint", nullable: false),
                    skill_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_post_skill", x => new { x.job_post_id, x.skill_id });
                    table.ForeignKey(
                        name: "fk_job_post_skill_job_post_job_post_id",
                        column: x => x.job_post_id,
                        principalTable: "job_post",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_job_post_skill_skills_skill_id",
                        column: x => x.skill_id,
                        principalTable: "skill",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attempt_score",
                columns: table => new
                {
                    attempt_id = table.Column<long>(type: "bigint", nullable: false),
                    rule_version_id = table.Column<int>(type: "integer", nullable: false),
                    overall_percent = table.Column<short>(type: "smallint", nullable: false),
                    percentile = table.Column<short>(type: "smallint", nullable: true),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attempt_score", x => x.attempt_id);
                    table.ForeignKey(
                        name: "fk_attempt_score_attempt_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "attempt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_attempt_score_scoring_rule_versions_rule_version_id",
                        column: x => x.rule_version_id,
                        principalTable: "scoring_rule_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attempt_section_score",
                columns: table => new
                {
                    attempt_id = table.Column<long>(type: "bigint", nullable: false),
                    section_id = table.Column<int>(type: "integer", nullable: false),
                    percent = table.Column<short>(type: "smallint", nullable: false),
                    band_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attempt_section_score", x => new { x.attempt_id, x.section_id });
                    table.ForeignKey(
                        name: "fk_attempt_section_score_attempt_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "attempt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_attempt_section_score_score_bands_band_id",
                        column: x => x.band_id,
                        principalTable: "score_band",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_attempt_section_score_sections_section_id",
                        column: x => x.section_id,
                        principalTable: "section",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attempt_answer",
                columns: table => new
                {
                    attempt_id = table.Column<long>(type: "bigint", nullable: false),
                    question_id = table.Column<long>(type: "bigint", nullable: false),
                    selected_option_id = table.Column<long>(type: "bigint", nullable: true),
                    flagged_for_review = table.Column<bool>(type: "boolean", nullable: false),
                    answered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attempt_answer", x => new { x.attempt_id, x.question_id });
                    table.ForeignKey(
                        name: "fk_attempt_answer_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "attempt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_attempt_answer_question_options_selected_option_id",
                        column: x => x.selected_option_id,
                        principalTable: "question_option",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_attempt_answer_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "question",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "application_file",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    application_id = table.Column<long>(type: "bigint", nullable: false),
                    file_url = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_file", x => x.id);
                    table.ForeignKey(
                        name: "fk_application_file_job_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "job_application",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_application_file_application_id",
                table: "application_file",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_assessment_form_track_id_version",
                table: "assessment_form",
                columns: new[] { "track_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assessment_form_question_question_id",
                table: "assessment_form_question",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ix_attempt_form_id",
                table: "attempt",
                column: "form_id");

            migrationBuilder.CreateIndex(
                name: "ix_attempt_job_seeker_id_track_id_started_at",
                table: "attempt",
                columns: new[] { "job_seeker_id", "track_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_attempt_track_id",
                table: "attempt",
                column: "track_id");

            migrationBuilder.CreateIndex(
                name: "ix_attempt_answer_question_id",
                table: "attempt_answer",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ix_attempt_answer_selected_option_id",
                table: "attempt_answer",
                column: "selected_option_id");

            migrationBuilder.CreateIndex(
                name: "ix_attempt_score_rule_version_id",
                table: "attempt_score",
                column: "rule_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_attempt_section_score_band_id",
                table: "attempt_section_score",
                column: "band_id");

            migrationBuilder.CreateIndex(
                name: "ix_attempt_section_score_section_id",
                table: "attempt_section_score",
                column: "section_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_actor_user_id",
                table: "audit_log",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entity_type_entity_id_created_at",
                table: "audit_log",
                columns: new[] { "entity_type", "entity_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_company_industry_id",
                table: "company",
                column: "industry_id");

            migrationBuilder.CreateIndex(
                name: "ix_company_normalized_name",
                table: "company",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_company_user_id",
                table: "company",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_company_document_company_id",
                table: "company_document",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_ledger_entry_actor_user_id",
                table: "credit_ledger_entry",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_ledger_entry_company_id_created_at",
                table: "credit_ledger_entry",
                columns: new[] { "company_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_credit_topup_request_company_id",
                table: "credit_topup_request",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_topup_request_ledger_entry_id",
                table: "credit_topup_request",
                column: "ledger_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_topup_request_payment_method_id",
                table: "credit_topup_request",
                column: "payment_method_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_topup_request_reviewed_by_user_id",
                table: "credit_topup_request",
                column: "reviewed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_topup_request_state_created_at",
                table: "credit_topup_request",
                columns: new[] { "state", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_job_application_job_post_id",
                table: "job_application",
                column: "job_post_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_application_job_seeker_id_job_post_id",
                table: "job_application",
                columns: new[] { "job_seeker_id", "job_post_id" });

            migrationBuilder.CreateIndex(
                name: "ix_job_application_status_id",
                table: "job_application",
                column: "status_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_post_company_id",
                table: "job_post",
                column: "company_id",
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_job_post_employment_type_id",
                table: "job_post",
                column: "employment_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_post_location_id",
                table: "job_post",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_post_track_id",
                table: "job_post",
                column: "track_id",
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_job_post_work_type_id",
                table: "job_post",
                column: "work_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_post_file_job_post_id",
                table: "job_post_file",
                column: "job_post_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_post_skill_skill_id",
                table: "job_post_skill",
                column: "skill_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_seeker_track_id",
                table: "job_seeker",
                column: "track_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_seeker_user_id",
                table: "job_seeker",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_seeker_profile_preferred_work_type_id",
                table: "job_seeker_profile",
                column: "preferred_work_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_seeker_skill_skill_id",
                table: "job_seeker_skill",
                column: "skill_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_user_id_created_at",
                table: "notification",
                columns: new[] { "user_id", "created_at" },
                filter: "read_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_profile_unlock_company_id_job_seeker_id",
                table: "profile_unlock",
                columns: new[] { "company_id", "job_seeker_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_profile_unlock_job_seeker_id_created_at",
                table: "profile_unlock",
                columns: new[] { "job_seeker_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_profile_unlock_ledger_entry_id",
                table: "profile_unlock",
                column: "ledger_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_question_section_id",
                table: "question",
                column: "section_id");

            migrationBuilder.CreateIndex(
                name: "ix_question_track_id_section_id",
                table: "question",
                columns: new[] { "track_id", "section_id" });

            migrationBuilder.CreateIndex(
                name: "ix_question_option_question_id",
                table: "question_option",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_family_id",
                table: "refresh_token",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_token_hash",
                table: "refresh_token",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_user_id",
                table: "refresh_token",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_score_band_rule_version_id",
                table: "score_band",
                column: "rule_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_scoring_rule_version_track_id_version",
                table: "scoring_rule_version",
                columns: new[] { "track_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_section_track_id_name",
                table: "section",
                columns: new[] { "track_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_section_band_feedback_band_id",
                table: "section_band_feedback",
                column: "band_id");

            migrationBuilder.CreateIndex(
                name: "ix_section_weight_section_id",
                table: "section_weight",
                column: "section_id");

            migrationBuilder.CreateIndex(
                name: "ix_skill_name",
                table: "skill",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_track_slug",
                table: "track",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_account_email",
                table: "user_account",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_file");

            migrationBuilder.DropTable(
                name: "assessment_form_question");

            migrationBuilder.DropTable(
                name: "attempt_answer");

            migrationBuilder.DropTable(
                name: "attempt_score");

            migrationBuilder.DropTable(
                name: "attempt_section_score");

            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "company_document");

            migrationBuilder.DropTable(
                name: "credit_topup_request");

            migrationBuilder.DropTable(
                name: "job_post_file");

            migrationBuilder.DropTable(
                name: "job_post_skill");

            migrationBuilder.DropTable(
                name: "job_seeker_profile");

            migrationBuilder.DropTable(
                name: "job_seeker_skill");

            migrationBuilder.DropTable(
                name: "notification");

            migrationBuilder.DropTable(
                name: "profile_unlock");

            migrationBuilder.DropTable(
                name: "refresh_token");

            migrationBuilder.DropTable(
                name: "section_band_feedback");

            migrationBuilder.DropTable(
                name: "section_weight");

            migrationBuilder.DropTable(
                name: "job_application");

            migrationBuilder.DropTable(
                name: "question_option");

            migrationBuilder.DropTable(
                name: "attempt");

            migrationBuilder.DropTable(
                name: "payment_method");

            migrationBuilder.DropTable(
                name: "skill");

            migrationBuilder.DropTable(
                name: "credit_ledger_entry");

            migrationBuilder.DropTable(
                name: "score_band");

            migrationBuilder.DropTable(
                name: "application_status");

            migrationBuilder.DropTable(
                name: "job_post");

            migrationBuilder.DropTable(
                name: "question");

            migrationBuilder.DropTable(
                name: "assessment_form");

            migrationBuilder.DropTable(
                name: "job_seeker");

            migrationBuilder.DropTable(
                name: "scoring_rule_version");

            migrationBuilder.DropTable(
                name: "company");

            migrationBuilder.DropTable(
                name: "employment_type");

            migrationBuilder.DropTable(
                name: "location");

            migrationBuilder.DropTable(
                name: "work_type");

            migrationBuilder.DropTable(
                name: "section");

            migrationBuilder.DropTable(
                name: "industry");

            migrationBuilder.DropTable(
                name: "user_account");

            migrationBuilder.DropTable(
                name: "track");
        }
    }
}
