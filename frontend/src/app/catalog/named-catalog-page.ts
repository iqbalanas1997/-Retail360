import { Component, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom, map } from 'rxjs';

import { ApiError } from '../core/api-error';
import { parseName } from '../core/catalog-rules';
import { type NamedCatalogItem } from '../core/catalog.models';
import { SessionService } from '../core/session.service';
import { ShopApi } from '../core/shop-api.service';

export type NamedCatalogTab = 'categories' | 'brands' | 'units';

interface NamedCatalogKind {
  id: NamedCatalogTab;
  path: string;
  singular: string;
  plural: string;
  label: string;
}

const kinds: Record<NamedCatalogTab, NamedCatalogKind> = {
  categories: {
    id: 'categories',
    path: '/api/categories',
    singular: 'category',
    plural: 'categories',
    label: 'Categories',
  },
  brands: {
    id: 'brands',
    path: '/api/brands',
    singular: 'brand',
    plural: 'brands',
    label: 'Brands',
  },
  units: {
    id: 'units',
    path: '/api/units',
    singular: 'unit',
    plural: 'units',
    label: 'Units',
  },
};

export const NAMED_CATALOG_TABS: NamedCatalogKind[] = [
  kinds.categories,
  kinds.brands,
  kinds.units,
];

@Component({
  selector: 'app-named-catalog-page',
  imports: [RouterLink],
  templateUrl: './named-catalog-page.html',
})
export class NamedCatalogPage {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(ShopApi);
  private readonly session = inject(SessionService);

  protected readonly tabs = NAMED_CATALOG_TABS;
  protected readonly canWrite = this.session.canWriteCatalog;
  protected readonly items = signal<NamedCatalogItem[]>([]);
  protected readonly drafts = signal<Record<string, string>>({});
  protected readonly newName = signal('');
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal('');
  protected readonly pendingDelete = signal<string | null>(null);

  protected readonly activeTab = toSignal(
    this.route.queryParamMap.pipe(map((params) => normalizeTab(params.get('tab')))),
    { initialValue: normalizeTab(this.route.snapshot.queryParamMap.get('tab')) },
  );

  private readonly loadActiveTab = effect(() => {
    const tab = this.activeTab();
    untracked(() => {
      this.resetTabState();
      void this.reload(tab);
    });
  });

  protected kind(): NamedCatalogKind {
    return kinds[this.activeTab()];
  }

  protected singular(): string {
    return this.kind().singular;
  }

  protected plural(): string {
    return this.kind().plural;
  }

  protected async reload(tab = this.activeTab()): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      const items = await firstValueFrom(this.api.listNamed(kinds[tab].path));
      if (tab !== this.activeTab()) {
        return;
      }

      this.items.set(items);
      this.drafts.set(Object.fromEntries(items.map((item) => [item.id, item.name])));
    } catch (error) {
      if (tab !== this.activeTab()) {
        return;
      }

      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      if (tab === this.activeTab()) {
        this.loading.set(false);
      }
    }
  }

  protected setDraft(id: string, value: string): void {
    this.drafts.update((current) => ({ ...current, [id]: value }));
    if (this.pendingDelete() === id) {
      this.pendingDelete.set(null);
    }
  }

  protected isDirty(item: NamedCatalogItem): boolean {
    return (this.drafts()[item.id] ?? '') !== item.name;
  }

  protected saveIfDirty(item: NamedCatalogItem): void {
    if (this.isDirty(item)) {
      void this.save(item);
    }
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

  private resetTabState(): void {
    this.newName.set('');
    this.error.set('');
    this.success.set('');
    this.pendingDelete.set(null);
    this.items.set([]);
    this.drafts.set({});
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

export function normalizeTab(value: string | null | undefined): NamedCatalogTab {
  if (value === 'brands' || value === 'units' || value === 'categories') {
    return value;
  }

  return 'categories';
}

function capitalize(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}
