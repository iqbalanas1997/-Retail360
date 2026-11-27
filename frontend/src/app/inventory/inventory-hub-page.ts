import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';

import { SessionService } from '../core/session.service';
import { LocationsPage } from './locations-page';
import { StockPage } from './stock-page';
import { StockPostPage } from './stock-post-page';

export type InventoryTab = 'stock' | 'locations' | 'opening' | 'adjust' | 'transfer';

interface InventoryTabItem {
  id: InventoryTab;
  label: string;
  manageOnly: boolean;
}

const ALL_TABS: InventoryTabItem[] = [
  { id: 'stock', label: 'Stock', manageOnly: false },
  { id: 'locations', label: 'Locations', manageOnly: false },
  { id: 'opening', label: 'Opening', manageOnly: true },
  { id: 'adjust', label: 'Adjust', manageOnly: true },
  { id: 'transfer', label: 'Transfer', manageOnly: true },
];

@Component({
  selector: 'app-inventory-hub-page',
  imports: [RouterLink, StockPage, LocationsPage, StockPostPage],
  templateUrl: './inventory-hub-page.html',
})
export class InventoryHubPage {
  private readonly route = inject(ActivatedRoute);
  private readonly session = inject(SessionService);

  protected readonly canManage = this.session.canManageInventory;
  protected readonly tabs = computed(() =>
    ALL_TABS.filter((tab) => !tab.manageOnly || this.canManage()),
  );

  protected readonly activeTab = toSignal(
    this.route.queryParamMap.pipe(map((params) => normalizeInventoryTab(params.get('tab'), this.canManage()))),
    { initialValue: normalizeInventoryTab(this.route.snapshot.queryParamMap.get('tab'), this.canManage()) },
  );
}

export function normalizeInventoryTab(
  value: string | null | undefined,
  canManage: boolean,
): InventoryTab {
  if (value === 'locations') {
    return 'locations';
  }

  if (!canManage) {
    return 'stock';
  }

  if (value === 'opening') {
    return 'opening';
  }

  if (value === 'adjust' || value === 'adjustments') {
    return 'adjust';
  }

  if (value === 'transfer' || value === 'transfers') {
    return 'transfer';
  }

  return 'stock';
}
