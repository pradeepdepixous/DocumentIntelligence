using Microsoft.EntityFrameworkCore;
using PixousDocIntelligence.Persistence.Entities;

namespace PixousDocIntelligence.Persistence;

public class DocumentIntelligenceDbContext : DbContext
{
    public DocumentIntelligenceDbContext(DbContextOptions<DocumentIntelligenceDbContext> options)
        : base(options)
    {
    }

    public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();
    public DbSet<ExtractionJobEntity> ExtractionJobs => Set<ExtractionJobEntity>();
    public DbSet<BatchManifestEntity> BatchManifests => Set<BatchManifestEntity>();
    public DbSet<ExtractionBatchEntity> ExtractionBatches => Set<ExtractionBatchEntity>();
    public DbSet<StitchedRecordEntity> StitchedRecords => Set<StitchedRecordEntity>();
    public DbSet<ReconciliationLedgerEntity> ReconciliationLedgers => Set<ReconciliationLedgerEntity>();
    public DbSet<ReviewTaskEntity> ReviewTasks => Set<ReviewTaskEntity>();
    public DbSet<CustomerAccountEntity> CustomerAccounts => Set<CustomerAccountEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Documents
        modelBuilder.Entity<DocumentEntity>(entity =>
        {
            entity.ToTable("documents");
            entity.HasKey(e => e.DocumentId);
            entity.HasIndex(e => new { e.TenantId, e.Sha256Checksum });
        });

        // Extraction Jobs
        modelBuilder.Entity<ExtractionJobEntity>(entity =>
        {
            entity.ToTable("extraction_jobs");
            entity.HasKey(e => e.JobId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.DocumentId);
            entity.Property(e => e.ExtractionPlanJson).HasColumnType("jsonb");
        });

        // Batch Manifests
        modelBuilder.Entity<BatchManifestEntity>(entity =>
        {
            entity.ToTable("batch_manifests");
            entity.HasKey(e => e.JobId);
            entity.Property(e => e.ManifestPayloadJson).HasColumnType("jsonb");
        });

        // Extraction Batches (composite key on job_id, batch_id)
        modelBuilder.Entity<ExtractionBatchEntity>(entity =>
        {
            entity.ToTable("extraction_batches");
            entity.HasKey(e => new { e.JobId, e.BatchId });
            entity.HasIndex(e => e.JobId);
        });

        // Stitched Records
        modelBuilder.Entity<StitchedRecordEntity>(entity =>
        {
            entity.ToTable("stitched_records");
            entity.HasKey(e => e.RecordId);
            entity.HasIndex(e => e.JobId).IsUnique();
            entity.Property(e => e.ConsolidatedPayloadJson).HasColumnType("jsonb");
        });

        // Reconciliation Ledgers
        modelBuilder.Entity<ReconciliationLedgerEntity>(entity =>
        {
            entity.ToTable("reconciliation_ledgers");
            entity.HasKey(e => e.JobId);
            entity.Property(e => e.LedgerPayloadJson).HasColumnType("jsonb");
        });

        // Review Tasks
        modelBuilder.Entity<ReviewTaskEntity>(entity =>
        {
            entity.ToTable("review_tasks");
            entity.HasKey(e => e.TaskId);
            entity.HasIndex(e => e.JobId);
            entity.HasIndex(e => e.Status);
            entity.Property(e => e.ReportedValueJson).HasColumnType("jsonb");
            entity.Property(e => e.ResolvedValueJson).HasColumnType("jsonb");
        });

        // Customer Accounts (portal sign-in identities)
        modelBuilder.Entity<CustomerAccountEntity>(entity =>
        {
            entity.ToTable("customer_accounts");
            entity.HasKey(e => e.AccountId);
            entity.HasIndex(e => e.GoogleSubject).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
        });
    }
}
