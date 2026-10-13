import { Routes } from '@angular/router';

import { NamedCatalogPage } from './catalog/named-catalog-page';
import { ProductFormPage } from './catalog/product-form-page';
import { ProductListPage } from './catalog/product-list-page';
import { authGuard, guestGuard } from './core/auth.guard';
import { InventoryPage } from './inventory/inventory-page';
import { LoginPage } from './login/login-page';
import { Shell } from './shell/shell';

export const routes: Routes = [
  { path: 'login', component: LoginPage, canActivate: [guestGuard] },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'catalog/products' },
      { path: 'catalog/products', component: ProductListPage, data: { title: 'Products' } },
      { path: 'catalog/products/new', component: ProductFormPage, data: { title: 'New product' } },
      { path: 'catalog/products/:id', component: ProductFormPage, data: { title: 'Product' } },
      {
        path: 'catalog/categories',
        component: NamedCatalogPage,
        data: { title: 'Categories', kind: 'categories' },
      },
      { path: 'catalog/brands', component: NamedCatalogPage, data: { title: 'Brands', kind: 'brands' } },
      { path: 'catalog/units', component: NamedCatalogPage, data: { title: 'Units', kind: 'units' } },
      { path: 'inventory', component: InventoryPage, data: { title: 'Inventory' } },
      { path: '**', redirectTo: 'catalog/products' },
    ],
  },
];
