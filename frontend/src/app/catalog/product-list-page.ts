import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError } from '../core/api-error';
import { formatScaled } from '../core/catalog-rules';
import { type Product } from '../core/catalog.models';
import { SessionService } from '../core/session.service';
import { ShopApi } from '../core/shop-api.service';

@Component({
  selector: 'app-product-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './product-list-page.html',
})
export class ProductListPage {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(ShopApi);
  private readonly session = inject(SessionService);

  protected readonly pageSize = 50;
  protected readonly canWrite = this.session.canWriteCatalog;
  protected readonly products = signal<Product[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly error = signal('');
  protected readonly hasMore = signal(false);
  protected readonly form = this.formBuilder.nonNullable.group({
    name: '',
    activeOnly: false,
  });

  constructor() {
    void this.reload();
  }

  protected price(value: number): string {
    return formatScaled(value, 2);
  }

  protected stock(value: number): string {
    return formatScaled(value, 3);
  }

  protected barcodes(product: Product): string {
    return product.barcodes.join(', ');
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    await this.load(0, false);
    this.loading.set(false);
  }

  protected async showMore(): Promise<void> {
    this.loadingMore.set(true);
    this.error.set('');
    await this.load(this.products().length, true);
    this.loadingMore.set(false);
  }

  private async load(offset: number, append: boolean): Promise<void> {
    const { name, activeOnly } = this.form.getRawValue();
    try {
      const page = await firstValueFrom(
        this.api.listProducts({ name, activeOnly, limit: this.pageSize, offset }),
      );
      this.products.set(append ? [...this.products(), ...page] : page);
      this.hasMore.set(page.length === this.pageSize);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
      if (!append) {
        this.products.set([]);
        this.hasMore.set(false);
      }
    }
  }
}
