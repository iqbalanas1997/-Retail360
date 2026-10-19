import { Injectable, NgZone, computed, inject, signal } from '@angular/core';

import {
  type LoginResponse,
  type ShopSession,
  clearStoredSession,
  readRoleClaims,
  readStoredSession,
  storedAccessToken,
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
  readonly canWriteSuppliers = computed(() =>
    this.roles().some((role) => role === 'Admin' || role === 'Manager'),
  );
  readonly canManageInventory = computed(() =>
    this.roles().some((role) => role === 'Admin' || role === 'Manager' || role === 'Inventory Staff'),
  );
  readonly hasSession = computed(() => this.session() !== null);

  private readonly zone = inject(NgZone);
  private expiryTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    this.scheduleExpiry();
    document.addEventListener('visibilitychange', this.onVisible);
  }

  isSignedIn(): boolean {
    return this.accessToken() !== null;
  }

  accessToken(): string | null {
    const current = this.currentSession();
    if (!current) {
      if (this.session() !== null) {
        this.clear();
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
    this.scheduleExpiry();
  }

  clear(): void {
    this.cancelExpiry();
    clearStoredSession();
    this.session.set(null);
  }

  private readonly onVisible = (): void => {
    if (document.visibilityState === 'visible') {
      this.accessToken();
    }
  };

  private scheduleExpiry(): void {
    this.cancelExpiry();
    const current = this.session();
    if (!current) {
      return;
    }

    const token = current.accessToken;
    const delay = Date.parse(current.expiresAt) - Date.now();
    if (delay <= 0) {
      this.expireIfCurrent(token);
      return;
    }

    this.expiryTimer = this.zone.runOutsideAngular(() =>
      setTimeout(() => this.zone.run(() => this.expireIfCurrent(token)), delay),
    );
  }

  private expireIfCurrent(token: string): void {
    if (this.session()?.accessToken !== token) {
      return;
    }

    const stored = storedAccessToken();
    if (stored && stored !== token) {
      return;
    }

    this.clear();
  }

  private cancelExpiry(): void {
    if (this.expiryTimer !== null) {
      clearTimeout(this.expiryTimer);
      this.expiryTimer = null;
    }
  }

  private currentSession(): ShopSession | null {
    return readStoredSession();
  }
}
