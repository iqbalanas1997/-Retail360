import { Component, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { ApiError } from '../core/api-error';
import { parseName } from '../core/catalog-rules';
import { type Area, type Shelf } from '../core/inventory.models';
import { InventoryApi } from '../core/inventory-api.service';
import { SessionService } from '../core/session.service';

@Component({
  selector: 'app-locations-page',
  templateUrl: './locations-page.html',
})
export class LocationsPage {
  private readonly api = inject(InventoryApi);
  private readonly session = inject(SessionService);

  protected readonly canManage = this.session.canManageInventory;
  protected readonly areas = signal<Area[]>([]);
  protected readonly shelves = signal<Shelf[]>([]);
  protected readonly areaDrafts = signal<Record<string, string>>({});
  protected readonly shelfDrafts = signal<Record<string, string>>({});
  protected readonly selectedAreaId = signal('');
  protected readonly newArea = signal('');
  protected readonly newShelf = signal('');
  protected readonly loading = signal(true);
  protected readonly loadingShelves = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal('');
  protected readonly pendingDelete = signal('');

  constructor() {
    void this.reload();
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      const areas = await firstValueFrom(this.api.listAreas());
      this.areas.set(areas);
      this.areaDrafts.set(Object.fromEntries(areas.map((area) => [area.id, area.name])));
      const selected = areas.some((area) => area.id === this.selectedAreaId())
        ? this.selectedAreaId()
        : (areas[0]?.id ?? '');
      this.selectedAreaId.set(selected);
      if (selected) {
        await this.loadShelves(selected);
      } else {
        this.shelves.set([]);
      }
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async chooseArea(areaId: string): Promise<void> {
    this.selectedAreaId.set(areaId);
    this.pendingDelete.set('');
    await this.loadShelves(areaId);
  }

  protected async createArea(): Promise<void> {
    await this.run(async () => {
      const created = await firstValueFrom(this.api.createArea(parseName(this.newArea())));
      this.newArea.set('');
      this.selectedAreaId.set(created.id);
      this.success.set('Area saved.');
      await this.reload();
    });
  }

  protected async saveArea(area: Area): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(this.api.renameArea(area.id, parseName(this.areaDrafts()[area.id] ?? '')));
      this.success.set('Area saved.');
      await this.reload();
    });
  }

  protected async setAreaActive(area: Area, active: boolean): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(active ? this.api.activateArea(area.id) : this.api.deactivateArea(area.id));
      this.success.set(active ? 'Area is active.' : 'Area is inactive.');
      await this.reload();
    });
  }

  protected async removeArea(area: Area): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(this.api.deleteArea(area.id));
      this.pendingDelete.set('');
      this.success.set('Area removed.');
      await this.reload();
    });
  }

  protected async createShelf(): Promise<void> {
    const areaId = this.selectedAreaId();
    await this.run(async () => {
      if (!areaId) {
        throw new Error('Area is required.');
      }

      await firstValueFrom(this.api.createShelf(areaId, parseName(this.newShelf())));
      this.newShelf.set('');
      this.success.set('Shelf saved.');
      await this.loadShelves(areaId);
    });
  }

  protected async saveShelf(shelf: Shelf): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(this.api.renameShelf(shelf.id, parseName(this.shelfDrafts()[shelf.id] ?? '')));
      this.success.set('Shelf saved.');
      await this.loadShelves(shelf.areaId);
    });
  }

  protected async setShelfActive(shelf: Shelf, active: boolean): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(active ? this.api.activateShelf(shelf.id) : this.api.deactivateShelf(shelf.id));
      this.success.set(active ? 'Shelf is active.' : 'Shelf is inactive.');
      await this.loadShelves(shelf.areaId);
    });
  }

  protected async removeShelf(shelf: Shelf): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(this.api.deleteShelf(shelf.id));
      this.pendingDelete.set('');
      this.success.set('Shelf removed.');
      await this.loadShelves(shelf.areaId);
    });
  }

  protected setAreaDraft(id: string, value: string): void {
    this.areaDrafts.update((current) => ({ ...current, [id]: value }));
  }

  protected setShelfDraft(id: string, value: string): void {
    this.shelfDrafts.update((current) => ({ ...current, [id]: value }));
  }

  protected inputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  private async loadShelves(areaId: string): Promise<void> {
    this.loadingShelves.set(true);
    try {
      const shelves = await firstValueFrom(this.api.listShelves(areaId));
      this.shelves.set(shelves);
      this.shelfDrafts.set(Object.fromEntries(shelves.map((shelf) => [shelf.id, shelf.name])));
    } catch (error) {
      this.shelves.set([]);
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.loadingShelves.set(false);
    }
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
      this.pendingDelete.set('');
      this.error.set(error instanceof ApiError || error instanceof Error ? error.message : 'The request failed.');
    } finally {
      this.saving.set(false);
    }
  }
}
