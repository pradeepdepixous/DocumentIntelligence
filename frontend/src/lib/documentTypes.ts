// Shapes returned by the document-processing and agent endpoints. The outer DocumentProcessingResult
// (and AgentSummarizeResponse/QnAResult) have no [JsonPropertyName] attributes on the backend, so
// ASP.NET Core's default camelCase policy applies to them; nested types like DocumentEnvelope/
// ExtractedItem DO carry explicit snake_case attributes, which always win over the policy. Hence
// the mixed casing below is intentional, not a typo.

export interface SourceEvidence {
  page_number: number;
  source_text: string;
}

export interface ConfidenceSignal {
  value: number;
  source: string;
  calibration_adjustment?: number;
}

export interface ExtractedItem {
  item_type: string;
  field_key: string;
  field_label: string;
  value: unknown;
  normalized_value?: unknown;
  value_type: string;
  sources: SourceEvidence[];
  confidence: ConfidenceSignal;
  validation_status: string;
  is_dynamically_discovered?: boolean;
}

export interface ExtractedCell {
  value: unknown;
  confidence: number;
  source_text?: string | null;
}

export interface ExtractedTable {
  table_key: string;
  table_title?: string | null;
  page_number: number;
  headers: string[];
  rows: Array<Record<string, ExtractedCell>>;
}

export interface PageMarkdownContent {
  page_number: number;
  has_tables: boolean;
  markdown: string;
  word_count: number;
}

export interface DocumentEnvelope {
  total_pages: number;
  document_summary: string;
  document_keywords: string[];
  pages: PageMarkdownContent[];
  full_markdown: string;
  items: ExtractedItem[];
  tables: ExtractedTable[];
}

export interface ReviewTask {
  task_id: string;
  reason: string;
  field_key: string;
  page_number: number;
  reported_value?: unknown;
  reported_confidence?: number;
  source_quote?: string;
  reviewer_notes?: string | null;
}

export interface PageCoverage {
  page_number: number;
  is_scanned: boolean;
  extracted_item_count: number;
  table_count: number;
  has_markdown_content: boolean;
}

export interface VerificationReport {
  total_items_checked: number;
  verified_count: number;
  vision_verified_count?: number;
  scanned_page_count?: number;
  unverified_evidence_count?: number;
  low_confidence_count?: number;
  review_tasks: ReviewTask[];
  total_pages?: number;
  page_coverage?: PageCoverage[];
  pages_with_no_extracted_content?: number[];
}

export interface ExtractionPlan {
  target_document_type: string;
  extraction_mode: string;
}

export interface BatchManifest {
  total_pages: number;
  batch_size: number;
}

export interface ReconciliationLedger {
  total_decisions: number;
  merged_count: number;
  conflict_count: number;
}

export interface DocumentProcessingResult {
  jobId: string;
  documentId: string;
  status: string;
  plan: ExtractionPlan;
  manifest: BatchManifest;
  stitchedEnvelope: DocumentEnvelope;
  reconciliationLedger: ReconciliationLedger;
  verificationReport: VerificationReport;
  executionDurationMs: number;
}

export interface SummarizeResponse {
  jobId: string;
  focusArea?: string;
  summary: string;
  generatedAt: string;
}

export interface AskCitation {
  fieldName: string;
  pageNumber: number;
  confidence: number;
  evidence: string;
}

export interface AskResponse {
  answer: string;
  citations: AskCitation[];
  warnings: string[];
  isFoundInDocument: boolean;
}

export interface RuleDefinition {
  rule_id: string;
  category: string;
  name?: string;
  rule_description: string;
  is_knockout: boolean;
  max_marks: number;
}

export interface RuleValidationRequest {
  rules: RuleDefinition[];
  total_marks_cap?: number;
  passing_threshold?: number;
}

export type RuleStatus = "Pass" | "Fail" | "Partial" | "NotApplicable";

export interface RuleEvaluationResult {
  rule_id: string;
  category: string;
  status: RuleStatus;
  is_knockout: boolean;
  marks_awarded: number;
  max_marks: number;
  finding: string;
  decision_reason: string;
  marks_reason: string;
  page_citations: number[];
  evidence_quote: string;
  confidence: number;
  data_quality_warning?: string;
}

export interface RuleValidationResponse {
  job_id: string;
  document_id: string;
  overall_verdict: "PASS" | "FAIL" | "NEEDS_REVIEW";
  total_marks_cap?: number;
  total_score_achieved: number;
  total_max_score: number;
  percentage: number;
  passing_threshold: number;
  knockout_passed: boolean;
  summary: string;
  evaluated_at: string;
  rule_results: RuleEvaluationResult[];
}

/** Mirrors ExtractedItem.GetDisplayValue() on the backend: joins arrays, stringifies everything else. */
export function displayValue(value: unknown): string {
  if (value === null || value === undefined) return "";
  if (Array.isArray(value)) return value.map((v) => displayValue(v)).join(", ");
  if (typeof value === "object") return JSON.stringify(value);
  return String(value);
}

export function confidenceTone(value: number): "good" | "ok" | "low" {
  if (value >= 0.85) return "good";
  if (value >= 0.6) return "ok";
  return "low";
}
