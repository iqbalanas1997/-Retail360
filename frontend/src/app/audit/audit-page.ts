import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

import { auditActionLabel, AUDIT_ACTIONS, AUDIT_ENTITY_TYPES, type AuditEntry } from '../core/audit.models';
import { AuditApi } from '../core/audit-api.service';
import { ApiError } from '../core/api-error';
import { type ShopUser } from '../core/user.models';
import { UserApi } from '../core/user-api.service';

@Component({
  selector: 'app-audit-page',
  imports: [ReactiveFormsModule],
  templateUrl: './audit-page.html',
})
export class AuditPage {
  private readonly formBuilder = inject(FormBuilder);
  private readonly auditApi = inject(AuditApi);
  private readonly userApi = inject(UserApi);
  private loadGeneration = 0;

  protected readonly actions = AUDIT_ACTIONS;
  protected readonly entityTypes = AUDIT_ENTITY_TYPES;
  protected readonly pageSize = 50;
  protected readonly users = signal<ShopUser[]>([]);
  protected readonly entries = signal<AuditEntry[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly error = signal('');
  protected readonly hasMore = signal(false);
  protected readonly form = this.formBuilder.nonNullable.group({
    from: '',
    to: '',
    actorUserId: '',
    action: '',
    entityType: '',
  });

  constructor() {
    void this.loadUsers();
    void this.reload();
  }

  protected actionLabel(action: string): string {
    return auditActionLabel(action);
  }

  protected actor(entry: AuditEntry): string {
    return entry.actorUserName || entry.actorUserId || 'Unknown';
  }

  protected summary(entry: AuditEntry): string {
    const details = entry.details ?? {};
    const parts = DETAIL_LABELS.flatMap(([key, label]) => {
      const value = details[key];
      return typeof value === 'string' && value.length > 0 ? [`${label} ${value}`] : [];
    });
    return parts.length > 0 ? parts.join(', ') : '—';
  }

  protected async apply(): Promise<void> {
    await this.reload();
  }

  protected async reload(): Promise<void> {
    const generation = ++this.loadGeneration;
    this.loading.set(true);
    this.loadingMore.set(false);
    this.error.set('');
    await this.load(0, false, generation);
    if (generation === this.loadGeneration) {
      this.loading.set(false);
    }
  }

  protected async showMore(): Promise<void> {
    if (this.loading() || this.loadingMore()) {
      return;
    }

    const generation = this.loadGeneration;
    this.loadingMore.set(true);
    this.error.set('');
    await this.load(this.entries().length, true, generation);
    if (generation === this.loadGeneration) {
      this.loadingMore.set(false);
    }
  }

  private async loadUsers(): Promise<void> {
    try {
      const users = await firstValueFrom(
        this.userApi.list({ name: '', activeOnly: false, limit: 200, offset: 0 }),
      );
      this.users.set(users);
    } catch {
      this.users.set([]);
    }
  }

  private async load(offset: number, append: boolean, generation: number): Promise<void> {
    const value = this.form.getRawValue();
    try {
      const entries = await firstValueFrom(
        this.auditApi.list({
          from: value.from,
          to: value.to,
          actorUserId: value.actorUserId,
          action: value.action,
          entityType: value.entityType,
          limit: this.pageSize,
          offset,
        }),
      );
      if (generation !== this.loadGeneration) {
        return;
      }

      this.entries.set(append ? [...this.entries(), ...entries] : entries);
      this.hasMore.set(entries.length === this.pageSize);
    } catch (error) {
      if (generation !== this.loadGeneration) {
        return;
      }

      this.error.set(toMessage(error));
      if (!append) {
        this.entries.set([]);
        this.hasMore.set(false);
      }
    }
  }
}

const DETAIL_LABELS: ReadonlyArray<readonly [string, string]> = [
  ['userName', 'User'],
  ['previousRole', 'Previous role'],
  ['role', 'Role'],
  ['name', 'Name'],
  ['category', 'Category'],
  ['amount', 'Amount'],
  ['expenseDate', 'Date'],
  ['receiptNumber', 'Receipt'],
  ['total', 'Total'],
  ['quantity', 'Quantity'],
  ['direction', 'Direction'],
  ['customerId', 'Customer id'],
];

function toMessage(error: unknown): string {
  return error instanceof ApiError ? error.message : 'The shop API could not be reached.';
}
