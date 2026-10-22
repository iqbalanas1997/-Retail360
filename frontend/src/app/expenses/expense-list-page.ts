import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError } from '../core/api-error';
import { formatScaled } from '../core/catalog-rules';
import { ExpenseApi } from '../core/expense-api.service';
import { EXPENSE_CATEGORIES, type Expense } from '../core/expense.models';
import { expenseCategoryLabel } from '../core/expense-rules';
import { SessionService } from '../core/session.service';

@Component({
  selector: 'app-expense-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './expense-list-page.html',
})
export class ExpenseListPage {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(ExpenseApi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);

  protected readonly categories = EXPENSE_CATEGORIES;
  protected readonly pageSize = 50;
  protected readonly canWrite = this.session.canWriteExpenses;
  protected readonly expenses = signal<Expense[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal('');
  protected readonly hasMore = signal(false);
  private loadGeneration = 0;
  protected readonly form = this.formBuilder.nonNullable.group({
    from: '',
    to: '',
    category: '',
  });

  constructor() {
    const message = readSuccess(this.router);
    if (message) {
      this.success.set(message);
    }

    void this.reload();
  }

  protected categoryLabel(value: string): string {
    return expenseCategoryLabel(value);
  }

  protected money(value: number): string {
    return formatScaled(value, 2);
  }

  protected async search(): Promise<void> {
    this.success.set('');
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
    await this.load(this.expenses().length, true, generation);
    if (generation === this.loadGeneration) {
      this.loadingMore.set(false);
    }
  }

  private async load(offset: number, append: boolean, generation: number): Promise<void> {
    const { from, to, category } = this.form.getRawValue();
    try {
      const page = await firstValueFrom(
        this.api.list({ from, to, category, limit: this.pageSize, offset }),
      );
      if (generation !== this.loadGeneration) {
        return;
      }

      this.expenses.set(append ? [...this.expenses(), ...page.expenses] : page.expenses);
      this.total.set(page.total);
      this.hasMore.set(page.expenses.length === this.pageSize);
    } catch (error) {
      if (generation !== this.loadGeneration) {
        return;
      }

      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
      if (!append) {
        this.expenses.set([]);
        this.total.set(0);
        this.hasMore.set(false);
      }
    }
  }
}

function readSuccess(router: Router): string {
  const candidates = [
    router.getCurrentNavigation()?.extras?.state,
    router.lastSuccessfulNavigation()?.extras?.state,
    history.state,
  ];
  for (const state of candidates) {
    if (state && typeof state === 'object' && 'success' in state && typeof state.success === 'string') {
      return state.success;
    }
  }

  return '';
}
