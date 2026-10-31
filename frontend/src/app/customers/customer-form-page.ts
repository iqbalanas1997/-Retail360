import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError } from '../core/api-error';
import { parseName } from '../core/catalog-rules';
import { CustomerApi } from '../core/customer-api.service';
import { type Customer, type CustomerSale, type SaveCustomerBody } from '../core/customer.models';
import { formatMoney } from '../core/purchase-rules';
import { formatSoldAt } from '../core/sale-rules';
import { SessionService } from '../core/session.service';
import { optionalAddress, optionalEmail, optionalNotes, optionalPhone } from '../core/supplier-rules';

@Component({
  selector: 'app-customer-form-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './customer-form-page.html',
})
export class CustomerFormPage {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(CustomerApi);
  private readonly session = inject(SessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private historyGeneration = 0;

  protected readonly historyPageSize = 50;
  protected readonly canWrite = this.session.canWriteCustomers;
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal('');
  protected readonly confirmDelete = signal(false);
  protected readonly customer = signal<Customer | null>(null);
  protected readonly sales = signal<CustomerSale[]>([]);
  protected readonly historyLoading = signal(false);
  protected readonly historyLoadingMore = signal(false);
  protected readonly historyError = signal('');
  protected readonly historyHasMore = signal(false);
  protected readonly form = this.formBuilder.nonNullable.group({
    name: ['', Validators.required],
    phone: '',
    email: '',
    address: '',
    notes: '',
  });

  constructor() {
    this.route.paramMap.subscribe(() => {
      void this.load();
    });
  }

  protected money(value: number): string {
    return formatMoney(value);
  }

  protected soldAt(sale: CustomerSale): string {
    return formatSoldAt(sale.soldAt);
  }

  protected reloadHistory(): void {
    void this.loadSales(false);
  }

  protected showMoreSales(): void {
    if (this.historyLoading() || this.historyLoadingMore()) {
      return;
    }

    void this.loadSales(true);
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.error.set('');
    this.success.set('');
    this.confirmDelete.set(false);
    this.clearHistory();
    const message = readSuccess(this.router);
    const id = this.route.snapshot.paramMap.get('id');
    try {
      if (id && id !== 'new') {
        const customer = await firstValueFrom(this.api.get(id));
        this.customer.set(customer);
        this.form.setValue({
          name: customer.name,
          phone: customer.phone ?? '',
          email: customer.email ?? '',
          address: customer.address ?? '',
          notes: customer.notes ?? '',
        });
        void this.loadSales(false);
      } else {
        this.customer.set(null);
      }

      if (this.canWrite()) {
        this.form.enable();
      } else {
        this.form.disable();
      }
    } catch (error) {
      this.loadFailed.set(true);
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      if (!this.loadFailed() && message) {
        this.success.set(message);
      }

      this.loading.set(false);
    }
  }

  protected async save(): Promise<void> {
    if (!this.canWrite() || this.saving()) {
      return;
    }

    this.error.set('');
    this.success.set('');
    let body: SaveCustomerBody;
    try {
      body = this.buildBody();
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'The request failed.');
      return;
    }

    this.saving.set(true);
    const existing = this.customer();
    try {
      const saved = existing
        ? await firstValueFrom(this.api.update(existing.id, body))
        : await firstValueFrom(this.api.create(body));
      this.success.set(existing ? 'Customer saved.' : 'Customer created.');
      if (!existing) {
        await this.router.navigate(['/customers', saved.id], {
          state: { success: 'Customer created.' },
        });
        return;
      }

      this.customer.set(saved);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.saving.set(false);
    }
  }

  protected async setActive(active: boolean): Promise<void> {
    const existing = this.customer();
    if (!existing || !this.canWrite() || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.error.set('');
    this.success.set('');
    try {
      const saved = await firstValueFrom(
        active ? this.api.activate(existing.id) : this.api.deactivate(existing.id),
      );
      this.customer.set(saved);
      this.success.set(active ? 'Customer is active.' : 'Customer is inactive.');
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.saving.set(false);
    }
  }

  protected async remove(): Promise<void> {
    const existing = this.customer();
    if (!existing || !this.canWrite() || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.error.set('');
    try {
      await firstValueFrom(this.api.remove(existing.id));
      await this.router.navigate(['/customers'], { state: { success: 'Customer removed.' } });
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
      this.saving.set(false);
    }
  }

  private async loadSales(append: boolean): Promise<void> {
    const existing = this.customer();
    if (!existing) {
      return;
    }

    const generation = ++this.historyGeneration;
    if (append) {
      this.historyLoadingMore.set(true);
    } else {
      this.historyLoading.set(true);
      this.sales.set([]);
    }

    this.historyError.set('');
    try {
      const sales = await firstValueFrom(
        this.api.sales(existing.id, this.historyPageSize, append ? this.sales().length : 0),
      );
      if (generation !== this.historyGeneration) {
        return;
      }

      this.sales.set(append ? [...this.sales(), ...sales] : sales);
      this.historyHasMore.set(sales.length === this.historyPageSize);
    } catch (error) {
      if (generation !== this.historyGeneration) {
        return;
      }

      this.historyError.set(error instanceof ApiError ? error.message : 'The request failed.');
      this.historyHasMore.set(false);
    } finally {
      if (generation === this.historyGeneration) {
        this.historyLoading.set(false);
        this.historyLoadingMore.set(false);
      }
    }
  }

  private clearHistory(): void {
    this.historyGeneration++;
    this.sales.set([]);
    this.historyError.set('');
    this.historyHasMore.set(false);
    this.historyLoading.set(false);
    this.historyLoadingMore.set(false);
  }

  private buildBody(): SaveCustomerBody {
    const value = this.form.getRawValue();
    return {
      name: parseName(value.name),
      phone: optionalPhone(value.phone),
      email: optionalEmail(value.email),
      address: optionalAddress(value.address),
      notes: optionalNotes(value.notes),
    };
  }
}

function readSuccess(router: Router): string {
  const state = router.currentNavigation()?.extras.state ?? history.state;
  return typeof state?.success === 'string' ? state.success : '';
}
