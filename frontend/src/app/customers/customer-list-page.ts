import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError } from '../core/api-error';
import { CustomerApi } from '../core/customer-api.service';
import { type Customer } from '../core/customer.models';
import { SessionService } from '../core/session.service';

@Component({
  selector: 'app-customer-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './customer-list-page.html',
})
export class CustomerListPage {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(CustomerApi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);

  protected readonly pageSize = 50;
  protected readonly canWrite = this.session.canWriteCustomers;
  protected readonly customers = signal<Customer[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal('');
  protected readonly hasMore = signal(false);
  private loadGeneration = 0;
  protected readonly form = this.formBuilder.nonNullable.group({
    search: '',
    activeOnly: false,
  });

  constructor() {
    const message = readSuccess(this.router);
    if (message) {
      this.success.set(message);
    }

    void this.reload();
  }

  protected status(customer: Customer): string {
    return customer.isActive ? 'Active' : 'Inactive';
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
    await this.load(this.customers().length, true, generation);
    if (generation === this.loadGeneration) {
      this.loadingMore.set(false);
    }
  }

  private async load(offset: number, append: boolean, generation: number): Promise<void> {
    const value = this.form.getRawValue();
    try {
      const customers = await firstValueFrom(
        this.api.list({
          search: value.search,
          activeOnly: value.activeOnly,
          limit: this.pageSize,
          offset,
        }),
      );
      if (generation !== this.loadGeneration) {
        return;
      }

      this.customers.set(append ? [...this.customers(), ...customers] : customers);
      this.hasMore.set(customers.length === this.pageSize);
    } catch (error) {
      if (generation !== this.loadGeneration) {
        return;
      }

      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
      if (!append) {
        this.customers.set([]);
        this.hasMore.set(false);
      }
    }
  }
}

function readSuccess(router: Router): string {
  const state = router.currentNavigation()?.extras.state ?? history.state;
  return typeof state?.success === 'string' ? state.success : '';
}
