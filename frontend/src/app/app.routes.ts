import { Routes } from '@angular/router';

import { NamedCatalogPage } from './catalog/named-catalog-page';
import { ProductFormPage } from './catalog/product-form-page';
import { ProductListPage } from './catalog/product-list-page';
import { authGuard, guestGuard } from './core/auth.guard';
import { LocationsPage } from './inventory/locations-page';
import { StockPage } from './inventory/stock-page';
import { StockPostPage } from './inventory/stock-post-page';
import { LoginPage } from './login/login-page';
import { Shell } from './shell/shell';
import { SupplierFormPage } from './suppliers/supplier-form-page';
import { SupplierListPage } from './suppliers/supplier-list-page';

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
      { path: 'inventory', pathMatch: 'full', redirectTo: 'inventory/stock' },
      { path: 'inventory/stock', component: StockPage, data: { title: 'Stock' } },
      { path: 'inventory/locations', component: LocationsPage, data: { title: 'Locations' } },
      { path: 'inventory/opening', component: StockPostPage, data: { title: 'Opening stock', mode: 'opening' } },
      {
        path: 'inventory/adjustments',
        component: StockPostPage,
        data: { title: 'Stock adjustment', mode: 'adjustment' },
      },
      { path: 'inventory/transfers', component: StockPostPage, data: { title: 'Shelf transfer', mode: 'transfer' } },
      { path: 'suppliers', component: SupplierListPage, data: { title: 'Suppliers' } },
      { path: 'suppliers/new', component: SupplierFormPage, data: { title: 'New supplier' } },
      { path: 'suppliers/:id', component: SupplierFormPage, data: { title: 'Supplier' } },
      { path: '**', redirectTo: 'catalog/products' },
    ],
  },
];
