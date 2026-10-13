import { Injectable, computed, signal } from '@angular/core';

import {
  type LoginResponse,
  type ShopSession,
  clearStoredSession,
  readRoleClaims,
  readStoredSession,
  writeStoredSession,
} from './shop-session';

@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly session = signal<ShopSession | null>(this.currentSession());

  readonly userName = computed(() => this.session()?.userName ?? '');
  readonly roles = computed(() => this.session()?.roles ?? []);
  readonly canWriteCatalog = computed(() =>
    this.roles().some((role) => role === 'Admin' || role === 'Manager'),
  );

  isSignedIn(): boolean {
    return this.accessToken() !== null;
  }

  accessToken(): string | null {
    const current = this.currentSession();
    if (!current) {
      if (this.session() !== null) {
        this.session.set(null);
      }

      return null;
    }

    if (Date.parse(current.expiresAt) <= Date.now()) {
      this.clear();
      return null;
    }

    return current.accessToken;
  }

  remember(response: LoginResponse): void {
    const next: ShopSession = {
      accessToken: response.accessToken,
      expiresAt: response.expiresAt,
      userId: response.userId,
      userName: response.userName,
      roles: readRoleClaims(response.accessToken),
    };
    writeStoredSession(next);
    this.session.set(next);
  }

  clear(): void {
    clearStoredSession();
    this.session.set(null);
  }

  private currentSession(): ShopSession | null {
    return readStoredSession();
  }
}
