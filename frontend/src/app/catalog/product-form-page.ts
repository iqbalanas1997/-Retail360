import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError } from '../core/api-error';
import { type NamedCatalogItem, type Product, type SaveProductBody } from '../core/catalog.models';
import { formatScaled, parseBarcodes, parseMinimumStock, parseName, parsePrice } from '../core/catalog-rules';
import { SessionService } from '../core/session.service';
import { ShopApi } from '../core/shop-api.service';

@Component({
  selector: 'app-product-form-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './product-form-page.html',
})
export class ProductFormPage {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(ShopApi);
  private readonly session = inject(SessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly canWrite = this.session.canWriteCatalog;
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal('');
  protected readonly confirmDelete = signal(false);
  protected readonly categories = signal<NamedCatalogItem[]>([]);
  protected readonly brands = signal<NamedCatalogItem[]>([]);
  protected readonly units = signal<NamedCatalogItem[]>([]);
  protected readonly barcodes = signal<string[]>([]);
  protected readonly barcodeDraft = signal('');
  protected readonly product = signal<Product | null>(null);
  protected readonly form = this.formBuilder.nonNullable.group({
    name: ['', Validators.required],
    categoryId: '',
    brandId: '',
    unitId: ['', Validators.required],
    purchasePrice: ['0.00', Validators.required],
    sellingPrice: ['0.00', Validators.required],
    minimumStock: ['0.000', Validators.required],
  });

  constructor() {
    this.route.paramMap.subscribe(() => {
      void this.load();
    });
  }

  protected inputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.error.set('');
    this.success.set('');
    this.confirmDelete.set(false);
    const id = this.route.snapshot.paramMap.get('id');
    try {
      const [categories, brands, units] = await Promise.all([
        firstValueFrom(this.api.listNamed('/api/categories')),
        firstValueFrom(this.api.listNamed('/api/brands')),
        firstValueFrom(this.api.listNamed('/api/units')),
      ]);
      this.categories.set(categories);
      this.brands.set(brands);
      this.units.set(units);
      if (id) {
        const product = await firstValueFrom(this.api.getProduct(id));
        this.product.set(product);
        this.form.setValue({
          name: product.name,
          categoryId: product.categoryId ?? '',
          brandId: product.brandId ?? '',
          unitId: product.unitId,
          purchasePrice: formatScaled(product.purchasePrice, 2),
          sellingPrice: formatScaled(product.sellingPrice, 2),
          minimumStock: formatScaled(product.minimumStock, 3),
        });
        this.barcodes.set([...product.barcodes]);
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
      const created = history.state as { success?: unknown } | null;
      if (created && typeof created.success === 'string') {
        this.success.set(created.success);
      }
      this.loading.set(false);
    }
  }

  protected addBarcode(): void {
    this.error.set('');
    const next = [...this.barcodes(), this.barcodeDraft()];
    try {
      this.barcodes.set(parseBarcodes(next));
      this.barcodeDraft.set('');
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'The request failed.');
    }
  }

  protected removeBarcode(barcode: string): void {
    this.barcodes.update((current) => current.filter((item) => item !== barcode));
  }

  protected async save(): Promise<void> {
    this.error.set('');
    this.success.set('');
    let body: SaveProductBody;
    try {
      body = this.buildBody();
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'The request failed.');
      return;
    }

    this.saving.set(true);
    const existing = this.product();
    try {
      const saved = existing
        ? await firstValueFrom(this.api.updateProduct(existing.id, body))
        : await firstValueFrom(this.api.createProduct(body));
      this.success.set(existing ? 'Product saved.' : 'Product created.');
      if (!existing) {
        await this.router.navigate(['/catalog/products', saved.id], {
          state: { success: 'Product created.' },
        });
        return;
      }

      this.product.set(saved);
      this.barcodes.set([...saved.barcodes]);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.saving.set(false);
    }
  }

  protected async setOffered(active: boolean): Promise<void> {
    const existing = this.product();
    if (!existing) {
      return;
    }

    this.error.set('');
    this.success.set('');
    this.saving.set(true);
    try {
      const saved = await firstValueFrom(
        active ? this.api.activateProduct(existing.id) : this.api.deactivateProduct(existing.id),
      );
      this.product.set(saved);
      this.success.set(active ? 'Product is offered.' : 'Product is not offered.');
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.saving.set(false);
    }
  }

  protected async remove(): Promise<void> {
    const existing = this.product();
    if (!existing) {
      return;
    }

    this.error.set('');
    this.saving.set(true);
    try {
      await firstValueFrom(this.api.deleteProduct(existing.id));
      await this.router.navigateByUrl('/catalog/products');
    } catch (error) {
      this.confirmDelete.set(false);
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.saving.set(false);
    }
  }

  private buildBody(): SaveProductBody {
    const value = this.form.getRawValue();
    const unitId = value.unitId.trim();
    if (!unitId) {
      throw new Error('Unit is required.');
    }

    return {
      name: parseName(value.name),
      categoryId: value.categoryId || null,
      brandId: value.brandId || null,
      unitId,
      purchasePrice: parsePrice(value.purchasePrice),
      sellingPrice: parsePrice(value.sellingPrice),
      minimumStock: parseMinimumStock(value.minimumStock),
      barcodes: parseBarcodes(this.barcodes()),
    };
  }
}
