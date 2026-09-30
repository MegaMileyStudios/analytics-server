import { api } from "@fns/api";
import { trackEvent } from "@aptabase/web";

export type UserAccount = {
  id: string;
  name: string;
  email: string;
  avatarUrl: string;
};

export type OAuthProviders = {
  github: boolean;
  google: boolean;
  signupsDisabled: boolean;
};

export async function getOAuthProviders(): Promise<OAuthProviders> {
  return await api.get<OAuthProviders>("/_auth/providers");
}

export async function requestSignInLink(email: string): Promise<boolean> {
  const [status, response] = await api.fetch("POST", "/_auth/signin", {
    email,
  });

  if (status === 404) return false;
  if (status === 200) return true;

  await api.handleError(status, response);
  return false;
}

export async function requestRegisterLink(name: string, email: string): Promise<void> {
  await api.post("/_auth/register", { name, email });
  trackEvent("register");
}

export async function me(): Promise<UserAccount | null> {
  const [status, account] = await api.fetch("GET", "/_auth/me");

  // Any 4xx (e.g. a session whose user no longer exists) means logged out. 5xx is a real
  // failure and should show the error state instead of bouncing the user to the login page.
  if (status >= 400 && status < 500) return null;
  if (status >= 500) throw new Error(`Failed to load account (${status})`);

  return account.json() as Promise<UserAccount>;
}

export async function signOut(): Promise<void> {
  await api.fetch("POST", "/_auth/signout");
  location.href = "/auth";
}
