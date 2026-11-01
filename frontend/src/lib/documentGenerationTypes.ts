export interface TemplateSectionDefinition {
  id: string;
  title?: string;
  type: string; // text, paragraphs, table, address_block, date, signature_block, list, bullet_list, object
  required: boolean;
  description?: string;
  fields?: string[];
}

export interface TemplateContentSchema {
  sections: TemplateSectionDefinition[];
}

export interface TemplatePresentation {
  primary_color?: string;
  accent_color?: string;
  font_family?: string;
  page_size?: string;
  margins?: {
    top_inches?: number;
    bottom_inches?: number;
    left_inches?: number;
    right_inches?: number;
  };
}

export interface DocumentTemplateDefinition {
  template_id: string;
  version: number;
  document_type: string;
  title?: string;
  description?: string;
  content_schema: TemplateContentSchema;
  presentation?: TemplatePresentation;
}

export interface TemplateInspectionResult {
  is_valid: boolean;
  template_id: string;
  template: DocumentTemplateDefinition;
  errors: string[];
  warnings: string[];
}

export interface DraftRequest {
  template_id: string;
  template_version?: number;
  instructions: string;
  output_format?: "docx" | "pdf";
  provider?: string;
  api_key?: string;
  model?: string;
}

export interface DraftResponse {
  document_id: string;
  status: "needs_information" | "ready" | "generated";
  template_id: string;
  template_version: number;
  missing_fields: string[];
  content: Record<string, any>;
  markdown_preview?: string;
}

export interface ValidateDraftRequest {
  content?: Record<string, any>;
}

export interface ValidateDraftResponse {
  document_id: string;
  is_valid: boolean;
  status: "needs_information" | "ready" | "generated";
  missing_fields: string[];
  errors: string[];
}

export interface GenerateRequest {
  formats: string[];
}

export interface GeneratedFileInfo {
  format: string;
  download_url: string;
}

export interface GenerateResponse {
  document_id: string;
  status: string;
  files: GeneratedFileInfo[];
}
