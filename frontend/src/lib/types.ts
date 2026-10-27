// API DTOs. Casing matches what the backend actually serializes per endpoint —
// snake_case for types with [JsonPropertyName] attributes (credentials, usage, envelope),
// PascalCase for types without them (agent responses, document processing result).

export interface ClientCredential {
  client_id: string;
  owner_account_id?: string | null;
  name: string;
  key_prefix: string;
  permissions: string[];
  is_active: boolean;
  created_at: string;
  expires_at: string | null;
  last_used_at: string | null;
  plan_tier: string;
  monthly_page_quota: number;
  pages_used_this_month: number;
  total_requests: number;
  billing_cycle: string;
}

// Self-serve permissions available to a customer-created key. "admin" is deliberately excluded —
// only the allowlisted portal admin identity ever gets that role, and only on their own session.
export const SELF_SERVE_PERMISSIONS = ["process", "summarize", "ask", "evaluate"] as const;

export interface PortalAccount {
  account_id: string;
  email: string;
  name: string;
  picture_url: string | null;
  is_admin: boolean;
}

export interface AuthSessionResponse {
  token: string;
  account: PortalAccount;
}

export interface MyUsageResponse {
  credit: {
    credit_limit: number;
    credits_used: number;
    credits_remaining: number;
    billing_cycle: string;
  };
  total_keys: number;
  active_keys: number;
  total_requests: number;
  keys: Array<{
    client_id: string;
    name: string;
    key_prefix: string;
    permissions: string[];
    is_active: boolean;
    pages_used_this_month: number;
    total_requests: number;
    last_used_at: string | null;
  }>;
}

export interface CreateCredentialResponse {
  message: string;
  client_id: string;
  project_name: string;
  access_key: string;
  key_prefix: string;
  permissions: string[];
  created_at: string;
  expires_at: string | null;
}

export interface RotateCredentialResponse {
  message: string;
  client_id: string;
  project_name: string;
  new_access_key: string;
  key_prefix: string;
  rotated_at: string;
}

export interface AdminAccountSummary {
  account_id: string;
  email: string;
  name: string;
  picture_url: string | null;
  is_admin: boolean;
  created_at: string;
  last_login_at: string | null;
  credit_limit: number;
  credits_used: number;
  credits_remaining: number;
  total_keys: number;
  active_keys: number;
  total_requests: number;
}

export interface SetAccountCreditResponse {
  message: string;
  account_id: string;
  credit_limit: number;
  credits_used: number;
  credits_remaining: number;
}

export interface AdminUsageResponse {
  summary: {
    total_accounts: number;
    total_registered_keys: number;
    active_keys: number;
    total_pages_processed_this_cycle: number;
    total_api_requests: number;
  };
  keys: Array<{
    client_id: string;
    owner_account_id: string | null;
    name: string;
    key_prefix: string;
    plan_tier: string;
    monthly_quota: number;
    pages_used: number;
    total_requests: number;
    is_active: boolean;
    last_used_at: string | null;
  }>;
}
