import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from "react";
import {
  ApiError,
  apiFetch,
  clearSession,
  getPortalToken,
  setPortalToken,
  SESSION_EXPIRED_EVENT,
} from "../lib/apiClient";
import type { AuthSessionResponse, PortalAccount } from "../lib/types";

interface AuthState {
  isLoading: boolean;
  isAuthenticated: boolean;
  isAdmin: boolean;
  account: PortalAccount | null;
  error: string | null;
  loginWithGoogleIdToken: (idToken: string) => Promise<void>;
  loginWithPassword: (email: string, password: string) => Promise<void>;
  registerWithPassword: (email: string, password: string, name: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthState | undefined>(undefined);

const ACCOUNT_STORAGE_KEY = "pixous_portal_account";

/**
 * Three ways to sign in — Google ID token, or email+password login/registration — all return the
 * same shape: a Pixous portal session JWT. That JWT authenticates /api/my/* (this account's own
 * keys and usage) — it does NOT authenticate document-processing calls, which still need one of
 * the account's own generated API keys (see Playground's "Test API Key" field).
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [isLoading, setIsLoading] = useState(true);
  const [account, setAccount] = useState<PortalAccount | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const token = getPortalToken();
    const storedAccount = localStorage.getItem(ACCOUNT_STORAGE_KEY);
    if (token && storedAccount) {
      try {
        setAccount(JSON.parse(storedAccount) as PortalAccount);
      } catch {
        clearSession();
      }
    }
    setIsLoading(false);
  }, []);

  const applySession = useCallback((result: AuthSessionResponse) => {
    setPortalToken(result.token);
    localStorage.setItem(ACCOUNT_STORAGE_KEY, JSON.stringify(result.account));
    setAccount(result.account);
  }, []);

  const runAuthRequest = useCallback(
    async (path: string, body: Record<string, string>, fallbackError: string) => {
      setError(null);
      setIsLoading(true);
      try {
        const result = await apiFetch<AuthSessionResponse>(path, {
          method: "POST",
          body: JSON.stringify(body),
        });
        applySession(result);
      } catch (err) {
        clearSession();
        setAccount(null);
        setError(err instanceof ApiError ? err.message : fallbackError);
      } finally {
        setIsLoading(false);
      }
    },
    [applySession],
  );

  const loginWithGoogleIdToken = useCallback(
    (idToken: string) => runAuthRequest("/auth/google", { idToken }, "Google sign-in failed."),
    [runAuthRequest],
  );

  const loginWithPassword = useCallback(
    (email: string, password: string) => runAuthRequest("/auth/login", { email, password }, "Sign-in failed."),
    [runAuthRequest],
  );

  const registerWithPassword = useCallback(
    (email: string, password: string, name: string) =>
      runAuthRequest("/auth/register", { email, password, name }, "Registration failed."),
    [runAuthRequest],
  );

  const logout = useCallback(() => {
    clearSession();
    localStorage.removeItem(ACCOUNT_STORAGE_KEY);
    setAccount(null);
  }, []);

  // A 401 from any portal-token-authenticated route (expired/revoked session) clears state here
  // so ProtectedRoute's isAuthenticated check flips and redirects to /login, instead of every
  // subsequent call just failing silently with no visible recovery.
  useEffect(() => {
    function handleSessionExpired() {
      clearSession();
      localStorage.removeItem(ACCOUNT_STORAGE_KEY);
      setAccount(null);
      setError("Your session expired. Please sign in again.");
    }
    window.addEventListener(SESSION_EXPIRED_EVENT, handleSessionExpired);
    return () => window.removeEventListener(SESSION_EXPIRED_EVENT, handleSessionExpired);
  }, []);

  return (
    <AuthContext.Provider
      value={{
        isLoading,
        isAuthenticated: account !== null,
        isAdmin: account?.is_admin ?? false,
        account,
        error,
        loginWithGoogleIdToken,
        loginWithPassword,
        registerWithPassword,
        logout,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error("useAuth must be used within AuthProvider");
  }
  return ctx;
}
