using Microsoft.EntityFrameworkCore;
using PixousDocIntelligence.Application.Options;
using PixousDocIntelligence.Persistence;

namespace PixousDocIntelligence.Startup;

public static class DatabaseInitializer
{
    public static void EnsureSchema(WebApplication app, string storageProvider, PortalAuthOptions portalAuthOptions)
    {
        if (!storageProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DocumentIntelligenceDbContext>();

            db.Database.EnsureCreated();

            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS customer_accounts (
                    account_id TEXT PRIMARY KEY,
                    google_subject TEXT NULL,
                    email TEXT NOT NULL,
                    name TEXT NOT NULL,
                    picture_url TEXT NULL,
                    is_admin BOOLEAN NOT NULL DEFAULT FALSE,
                    created_at TIMESTAMPTZ NOT NULL,
                    last_login_at TIMESTAMPTZ NULL,
                    password_hash TEXT NULL,
                    failed_login_attempts INT NOT NULL DEFAULT 0,
                    locked_out_until TIMESTAMPTZ NULL
                );
                ALTER TABLE customer_accounts ALTER COLUMN google_subject DROP NOT NULL;
                ALTER TABLE customer_accounts ADD COLUMN IF NOT EXISTS password_hash TEXT NULL;
                ALTER TABLE customer_accounts ADD COLUMN IF NOT EXISTS failed_login_attempts INT NOT NULL DEFAULT 0;
                ALTER TABLE customer_accounts ADD COLUMN IF NOT EXISTS locked_out_until TIMESTAMPTZ NULL;
                CREATE UNIQUE INDEX IF NOT EXISTS ix_customer_accounts_google_subject ON customer_accounts (google_subject);
                CREATE UNIQUE INDEX IF NOT EXISTS ix_customer_accounts_email ON customer_accounts (email);
            ");

            // Credit columns in a separate statement so the default (sourced from config,
            // not a literal) backfills existing accounts created before this feature existed.
#pragma warning disable EF1002
            db.Database.ExecuteSqlRaw($@"
                ALTER TABLE customer_accounts ADD COLUMN IF NOT EXISTS credit_limit INT NOT NULL DEFAULT {portalAuthOptions.DefaultAccountCreditLimit};
                ALTER TABLE customer_accounts ADD COLUMN IF NOT EXISTS credits_used INT NOT NULL DEFAULT 0;
                ALTER TABLE customer_accounts ADD COLUMN IF NOT EXISTS billing_cycle TEXT NOT NULL DEFAULT to_char(now(), 'YYYY-MM');
            ");
#pragma warning restore EF1002

            db.Database.ExecuteSqlRaw(@"
                ALTER TABLE extraction_jobs ADD COLUMN IF NOT EXISTS attempt_count INT NOT NULL DEFAULT 0;
                ALTER TABLE extraction_jobs ADD COLUMN IF NOT EXISTS next_attempt_at TIMESTAMPTZ NULL;
            ");

            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS validation_profiles (
                    profile_id TEXT PRIMARY KEY,
                    tenant_id TEXT NOT NULL DEFAULT 'default-tenant',
                    name TEXT NOT NULL,
                    description TEXT NOT NULL DEFAULT '',
                    document_type TEXT NOT NULL,
                    version INT NOT NULL DEFAULT 1,
                    requirements_json JSONB NOT NULL DEFAULT '[]',
                    decision_policy_json JSONB NOT NULL DEFAULT '{}',
                    is_active BOOLEAN NOT NULL DEFAULT TRUE,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
                );
                CREATE INDEX IF NOT EXISTS ix_validation_profiles_tenant_profile ON validation_profiles (tenant_id, profile_id);
                CREATE INDEX IF NOT EXISTS ix_validation_profiles_document_type ON validation_profiles (document_type);

                CREATE TABLE IF NOT EXISTS validation_results (
                    validation_id TEXT PRIMARY KEY,
                    tenant_id TEXT NOT NULL DEFAULT 'default-tenant',
                    profile_id TEXT NULL,
                    profile_version INT NULL,
                    document_type TEXT NOT NULL,
                    status TEXT NOT NULL,
                    is_valid BOOLEAN NULL,
                    checks_json JSONB NOT NULL DEFAULT '[]',
                    message TEXT NOT NULL DEFAULT '',
                    validated_at TIMESTAMPTZ NOT NULL DEFAULT now()
                );
                CREATE INDEX IF NOT EXISTS ix_validation_results_tenant_val ON validation_results (tenant_id, validation_id);
                CREATE INDEX IF NOT EXISTS ix_validation_results_profile ON validation_results (profile_id);
            ");

            Console.WriteLine("📦 PostgreSQL schema verified and active.");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠️ PostgreSQL connection failed: {ex.Message}. Pipeline will retry on demand.");
            Console.ResetColor();
        }
    }
}
