const storageKey = 'retail360.session';

export interface LoginResponse {
  accessToken: string;
  tokenType: string;
  expiresAt: string;
  userId: string;
  userName: string;
}

export interface ShopSession {
  accessToken: string;
  expiresAt: string;
  userId: string;
  userName: string;
  roles: string[];
}

export function readRoleClaims(accessToken: string): string[] {
  const segment = accessToken.split('.')[1];
  if (!segment) {
    return [];
  }

  try {
    const padded = segment.replace(/-/g, '+').replace(/_/g, '/');
    const base64 = padded.padEnd(Math.ceil(padded.length / 4) * 4, '=');
    const bytes = Uint8Array.from(atob(base64), (char) => char.charCodeAt(0));
    const payload = JSON.parse(new TextDecoder().decode(bytes)) as Record<string, unknown>;
    const role =
      payload['role'] ??
      payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'];
    if (Array.isArray(role)) {
      return role.filter((item): item is string => typeof item === 'string');
    }

    return typeof role === 'string' ? [role] : [];
  } catch {
    return [];
  }
}

export function readStoredSession(): ShopSession | null {
  const raw = sessionStorage.getItem(storageKey);
  if (!raw) {
    return null;
  }

  try {
    const parsed = JSON.parse(raw) as Partial<ShopSession>;
    if (
      typeof parsed.accessToken !== 'string' ||
      typeof parsed.expiresAt !== 'string' ||
      typeof parsed.userId !== 'string' ||
      typeof parsed.userName !== 'string' ||
      !Array.isArray(parsed.roles)
    ) {
      sessionStorage.removeItem(storageKey);
      return null;
    }

    const expires = Date.parse(parsed.expiresAt);
    if (!Number.isFinite(expires) || expires <= Date.now()) {
      sessionStorage.removeItem(storageKey);
      return null;
    }

    return {
      accessToken: parsed.accessToken,
      expiresAt: parsed.expiresAt,
      userId: parsed.userId,
      userName: parsed.userName,
      roles: parsed.roles.filter((role): role is string => typeof role === 'string'),
    };
  } catch {
    sessionStorage.removeItem(storageKey);
    return null;
  }
}

export function writeStoredSession(session: ShopSession): void {
  sessionStorage.setItem(storageKey, JSON.stringify(session));
}

export function clearStoredSession(): void {
  sessionStorage.removeItem(storageKey);
}
