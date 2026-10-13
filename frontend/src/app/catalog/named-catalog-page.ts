import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError } from '../core/api-error';
import { parseName } from '../core/catalog-rules';
import { type NamedCatalogItem } from '../core/catalog.models';
import { SessionService } from '../core/session.service';
import { ShopApi } from '../core/shop-api.service';

interface NamedCatalogKind {
  path: string;
  singular: string;
  plural: string;
}

const kinds: Record<string, NamedCatalogKind> = {
  categories: { path: '/api/categories', singular: 'category', plural: 'categories' },
  brands: { path: '/api/brands', singular: 'brand', plural: 'brands' },
  units: { path: '/api/units', singular: 'unit', plural: 'units' },
};

@Component({
  selector: 'app-named-catalog-page',
  templateUrl: './named-catalog-page.html',
})
export class NamedCatalogPage {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(ShopApi);
  private readonly session = inject(SessionService);

  protected readonly canWrite = this.session.canWriteCatalog;
  protected readonly items = signal<NamedCatalogItem[]>([]);
  protected readonly drafts = signal<Record<string, string>>({});
  protected readonly newName = signal('');
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal('');
  protected readonly pendingDelete = signal<string | null>(null);

  constructor() {
    void this.reload();
  }

  protected singular(): string {
    return this.kind().singular;
  }

  protected plural(): string {
    return this.kind().plural;
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      const items = await firstValueFrom(this.api.listNamed(this.kind().path));
      this.items.set(items);
      this.drafts.set(Object.fromEntries(items.map((item) => [item.id, item.name])));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.loading.set(false);
    }
  }

  protected setDraft(id: string, value: string): void {
    this.drafts.update((current) => ({ ...current, [id]: value }));
  }

  protected async create(): Promise<void> {
    await this.run(async () => {
      const name = parseName(this.newName());
      await firstValueFrom(this.api.createNamed(this.kind().path, name));
      this.newName.set('');
      this.success.set(`${capitalize(this.singular())} saved.`);
      await this.reload();
    });
  }

  protected async save(item: NamedCatalogItem): Promise<void> {
    await this.run(async () => {
      const name = parseName(this.drafts()[item.id] ?? '');
      await firstValueFrom(this.api.updateNamed(this.kind().path, item.id, name));
      this.success.set(`${capitalize(this.singular())} saved.`);
      await this.reload();
    });
  }

  protected async remove(item: NamedCatalogItem): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(this.api.deleteNamed(this.kind().path, item.id));
      this.pendingDelete.set(null);
      this.success.set(`${capitalize(this.singular())} removed.`);
      await this.reload();
    });
  }

  protected inputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  private kind(): NamedCatalogKind {
    const key = this.route.snapshot.data['kind'];
    return kinds[typeof key === 'string' ? key : ''] ?? kinds['categories'];
  }

  private async run(work: () => Promise<void>): Promise<void> {
    if (this.saving()) {
      return;
    }

    this.error.set('');
    this.success.set('');
    this.saving.set(true);
    try {
      await work();
    } catch (error) {
      this.pendingDelete.set(null);
      this.error.set(
        error instanceof ApiError || error instanceof Error ? error.message : 'The request failed.',
      );
    } finally {
      this.saving.set(false);
    }
  }
}

function capitalize(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}
