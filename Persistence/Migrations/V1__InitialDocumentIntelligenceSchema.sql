-- =============================================================================
-- Migration V1: Pixous Document Intelligence Enterprise PostgreSQL Schema
-- Generic, Instruction-Driven, Batch-Based Document Processing Architecture
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- 1. DOCUMENTS TABLE
CREATE TABLE IF NOT EXISTS documents (
    document_id VARCHAR(64) PRIMARY KEY,
    tenant_id VARCHAR(64) NOT NULL DEFAULT 'default-tenant',
    file_name VARCHAR(255) NOT NULL,
    file_path TEXT NOT NULL,
    file_size_bytes BIGINT NOT NULL,
    sha256_checksum CHAR(64) NOT NULL,
    page_count INT NOT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'ingested',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_documents_tenant_checksum ON documents (tenant_id, sha256_checksum);

-- 2. EXTRACTION JOBS TABLE
CREATE TABLE IF NOT EXISTS extraction_jobs (
    job_id VARCHAR(64) PRIMARY KEY,
    document_id VARCHAR(64) NOT NULL REFERENCES documents(document_id) ON DELETE CASCADE,
    tenant_id VARCHAR(64) NOT NULL DEFAULT 'default-tenant',
    status VARCHAR(32) NOT NULL DEFAULT 'created',
    user_instruction TEXT,
    schema_version INT NOT NULL DEFAULT 1,
    extraction_plan JSONB NOT NULL,
    error_message TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_extraction_jobs_status ON extraction_jobs (status);
CREATE INDEX IF NOT EXISTS idx_extraction_jobs_document ON extraction_jobs (document_id);

-- 3. EXTRACTION BATCHES TABLE
CREATE TABLE IF NOT EXISTS extraction_batches (
    batch_id VARCHAR(64) NOT NULL,
    job_id VARCHAR(64) NOT NULL REFERENCES extraction_jobs(job_id) ON DELETE CASCADE,
    start_page INT NOT NULL,
    end_page INT NOT NULL,
    overlap_start_page INT,
    overlap_end_page INT,
    status VARCHAR(32) NOT NULL DEFAULT 'pending',
    retry_count INT NOT NULL DEFAULT 0,
    error_message TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (job_id, batch_id)
);

CREATE INDEX IF NOT EXISTS idx_extraction_batches_job ON extraction_batches (job_id);

-- 4. STITCHED RECORDS TABLE
CREATE TABLE IF NOT EXISTS stitched_records (
    record_id VARCHAR(64) PRIMARY KEY DEFAULT uuid_generate_v4()::text,
    job_id VARCHAR(64) NOT NULL UNIQUE REFERENCES extraction_jobs(job_id) ON DELETE CASCADE,
    document_id VARCHAR(64) NOT NULL REFERENCES documents(document_id),
    consolidated_payload JSONB NOT NULL,
    unresolved_conflicts_count INT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_stitched_records_gin ON stitched_records USING GIN (consolidated_payload);

-- 5. REVIEW TASKS TABLE (HUMAN-IN-THE-LOOP AUDIT)
CREATE TABLE IF NOT EXISTS review_tasks (
    task_id VARCHAR(64) PRIMARY KEY,
    job_id VARCHAR(64) NOT NULL REFERENCES extraction_jobs(job_id) ON DELETE CASCADE,
    field_key VARCHAR(128) NOT NULL,
    page_number INT NOT NULL,
    reason VARCHAR(64) NOT NULL, -- 'low_confidence', 'unverified_evidence', 'unresolved_conflict'
    reported_value JSONB NOT NULL,
    reported_confidence NUMERIC(4, 3) NOT NULL,
    source_quote TEXT,
    status VARCHAR(32) NOT NULL DEFAULT 'open', -- 'open', 'approved', 'rejected', 'overridden'
    reviewer_notes TEXT,
    resolved_value JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    resolved_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_review_tasks_status ON review_tasks (status);
CREATE INDEX IF NOT EXISTS idx_review_tasks_job ON review_tasks (job_id);
