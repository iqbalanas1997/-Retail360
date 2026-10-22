import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError } from '../core/api-error';
import { OperationAttempt } from '../core/inventory-rules';
import { ExpenseApi } from '../core/expense-api.service';
import { EXPENSE_CATEGORIES, type Expense } from '../core/expense.models';
import {
  expenseAttemptKey,
  parseExpenseAmount,
  parseExpenseCategory,
  parseExpenseDate,
  parseExpenseNotes,
  parseExpensePaymentMethod,
} from '../core/expense-rules';
import { SessionService } from '../core/session.service';

@Component({
  selector: 'app-expense-form-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './expense-form-page.html',
})
export class ExpenseFormPage {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(ExpenseApi);
  private readonly session = inject(SessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly attempt = new OperationAttempt();

  protected readonly categories = EXPENSE_CATEGORIES;
  protected readonly canWrite = this.session.canWriteExpenses;
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal('');
  protected readonly confirmDelete = signal(false);
  protected readonly expense = signal<Expense | null>(null);
  protected readonly form = this.formBuilder.nonNullable.group({
    category: ['', Validators.required],
    amount: ['', Validators.required],
    expenseDate: [today(), Validators.required],
    notes: '',
    paymentMethod: 'cash',
  });

  constructor() {
    this.route.paramMap.subscribe(() => {
      void this.load();
    });
  }

  protected get creating(): boolean {
    return this.expense() === null && !this.route.snapshot.paramMap.get('id');
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.error.set('');
    this.success.set('');
    this.confirmDelete.set(false);
    const message = readSuccess(this.router);
    const id = this.route.snapshot.paramMap.get('id');
    try {
      if (id) {
        const expense = await firstValueFrom(this.api.get(id));
        this.expense.set(expense);
        this.form.setValue({
          category: expense.category,
          amount: expense.amount.toFixed(2),
          expenseDate: expense.expenseDate,
          notes: expense.notes ?? '',
          paymentMethod: expense.paymentMethod,
        });
      } else {
        this.expense.set(null);
        this.form.reset({
          category: '',
          amount: '',
          expenseDate: today(),
          notes: '',
          paymentMethod: 'cash',
        });
      }

      if (this.canWrite()) {
        this.form.enable();
      } else {
        this.form.disable();
      }

      if (message) {
        this.success.set(message);
      }
    } catch (error) {
      this.expense.set(null);
      this.loadFailed.set(true);
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async save(): Promise<void> {
    if (!this.canWrite() || this.saving()) {
      return;
    }

    this.error.set('');
    this.success.set('');
    const raw = this.form.getRawValue();
    let category: string;
    let amount: number;
    let expenseDate: string;
    let notes: string | null;
    let paymentMethod: string;
    try {
      category = parseExpenseCategory(raw.category);
      amount = parseExpenseAmount(raw.amount);
      expenseDate = parseExpenseDate(raw.expenseDate);
      notes = parseExpenseNotes(raw.notes);
      paymentMethod = parseExpensePaymentMethod(raw.paymentMethod);
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'The request failed.');
      return;
    }

    const current = this.expense();
    this.saving.set(true);
    try {
      if (current) {
        const saved = await firstValueFrom(
          this.api.update(current.id, { category, amount, expenseDate, notes, paymentMethod }),
        );
        this.expense.set(saved);
        this.success.set('Expense saved.');
      } else {
        const operationId = this.attempt.idFor(expenseAttemptKey(category, amount, expenseDate, notes));
        const saved = await firstValueFrom(
          this.api.create({ operationId, category, amount, expenseDate, notes, paymentMethod }),
        );
        this.attempt.complete();
        await this.router.navigate(['/expenses', saved.id], { state: { success: 'Expense recorded.' } });
      }
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.saving.set(false);
    }
  }

  protected async remove(): Promise<void> {
    const current = this.expense();
    if (!current || !this.canWrite() || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.error.set('');
    try {
      await firstValueFrom(this.api.delete(current.id));
      await this.router.navigate(['/expenses'], { state: { success: 'Expense removed.' } });
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
      this.saving.set(false);
    }
  }
}

function today(): string {
  const now = new Date();
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');
  return `${now.getFullYear()}-${month}-${day}`;
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
