import { PublicClientApplication, type AccountInfo } from "@azure/msal-browser";

/**
 * Two sign-in modes:
 *  - "entra": Microsoft Entra ID (ministry accounts). Production.
 *  - "dev":   choose a demo user on the sign-in page; the API must run with Auth:Mode=Development.
 *             Signing out lets you sign in as someone else, to see each role's view.
 */
export const authMode: "dev" | "entra" = import.meta.env.VITE_AUTH_MODE === "entra" ? "entra" : "dev";

const DEV_KEY = "mkash.devUser";
const scope = import.meta.env.VITE_API_SCOPE as string | undefined;

let msal: PublicClientApplication | null = null;
let account: AccountInfo | null = null;

export async function initAuth(): Promise<void> {
  if (authMode !== "entra") return;
  msal = new PublicClientApplication({
    auth: {
      clientId: import.meta.env.VITE_ENTRA_CLIENT_ID as string,
      authority: `https://login.microsoftonline.com/${import.meta.env.VITE_ENTRA_TENANT_ID as string}`,
      redirectUri: window.location.origin,
    },
    cache: { cacheLocation: "sessionStorage" },
  });
  await msal.initialize();
  const res = await msal.handleRedirectPromise();
  account = res?.account ?? msal.getAllAccounts()[0] ?? null;
}

export function isSignedIn(): boolean {
  return authMode === "entra" ? account !== null : !!devUser();
}

export function devUser(): string | null {
  try {
    return sessionStorage.getItem(DEV_KEY);
  } catch {
    return null;
  }
}

export function signInDev(email: string): void {
  sessionStorage.setItem(DEV_KEY, email);
}

export async function signIn(): Promise<void> {
  if (msal) await msal.loginRedirect({ scopes: scope ? [scope] : [] });
}

export async function signOut(): Promise<void> {
  if (authMode === "dev") {
    sessionStorage.removeItem(DEV_KEY);
    return;
  }
  if (msal) await msal.logoutRedirect({ account: account ?? undefined });
}

/** Headers that identify the caller to the API. */
export async function authHeaders(): Promise<Record<string, string>> {
  if (authMode === "dev") {
    const u = devUser();
    return u ? { "X-Dev-User": u } : {};
  }
  if (!msal || !account) return {};
  try {
    const t = await msal.acquireTokenSilent({ account, scopes: scope ? [scope] : [] });
    return { Authorization: `Bearer ${t.accessToken}` };
  } catch {
    await msal.acquireTokenRedirect({ account, scopes: scope ? [scope] : [] });
    return {};
  }
}
