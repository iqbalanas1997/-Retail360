import { inject } from '@angular/core';
import { Router, Routes, type RedirectFunction } from '@angular/router';

import { PasswordPage } from './account/password-page';
import { AuditPage } from './audit/audit-page';
import { NamedCatalogPage, type NamedCatalogTab } from './catalog/named-catalog-page';
import { ProductFormPage } from './catalog/product-form-page';
import { ProductListPage } from './catalog/product-list-page';
import { authGuard, guestGuard } from './core/auth.guard';
import { CustomerFormPage } from './customers/customer-form-page';
import { CustomerListPage } from './customers/customer-list-page';
import { DashboardPage } from './dashboard/dashboard-page';
import { ExpenseFormPage } from './expenses/expense-form-page';
import { ExpenseListPage } from './expenses/expense-list-page';
import { InventoryHubPage, type InventoryTab } from './inventory/inventory-hub-page';
import { LoginPage } from './login/login-page';
import { Shell } from './shell/shell';
import { PosPage } from './pos/pos-page';
import { PurchaseFormPage } from './purchases/purchase-form-page';
import { PurchaseListPage } from './purchases/purchase-list-page';
import { SaleDetailPage } from './sales/sale-detail-page';
import { SaleListPage } from './sales/sale-list-page';
import { SupplierFormPage } from './suppliers/supplier-form-page';
import { SupplierListPage } from './suppliers/supplier-list-page';
import { UserFormPage } from './users/user-form-page';
import { UserListPage } from './users/user-list-page';

export const routes: Routes = [
  { path: 'login', component: LoginPage, canActivate: [guestGuard] },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'catalog/products' },
      { path: 'dashboard', component: DashboardPage, data: { title: 'Dashboard' } },
      { path: 'catalog/products', component: ProductListPage, data: { title: 'Products' } },
      { path: 'catalog/products/new', component: ProductFormPage, data: { title: 'New product' } },
      { path: 'catalog/products/:id', component: ProductFormPage, data: { title: 'Product' } },
      { path: 'catalog/names', component: NamedCatalogPage, data: { title: 'Catalog names' } },
      { path: 'catalog/categories', redirectTo: catalogNamesRedirect('categories'), pathMatch: 'full' },
      { path: 'catalog/brands', redirectTo: catalogNamesRedirect('brands'), pathMatch: 'full' },
      { path: 'catalog/units', redirectTo: catalogNamesRedirect('units'), pathMatch: 'full' },
      { path: 'inventory', component: InventoryHubPage, data: { title: 'Inventory' } },
      { path: 'inventory/stock', redirectTo: inventoryRedirect('stock'), pathMatch: 'full' },
      { path: 'inventory/locations', redirectTo: inventoryRedirect('locations'), pathMatch: 'full' },
      { path: 'inventory/opening', redirectTo: inventoryRedirect('opening'), pathMatch: 'full' },
      { path: 'inventory/adjustments', redirectTo: inventoryRedirect('adjust'), pathMatch: 'full' },
      { path: 'inventory/transfers', redirectTo: inventoryRedirect('transfer'), pathMatch: 'full' },
      { path: 'suppliers', component: SupplierListPage, data: { title: 'Suppliers' } },
      { path: 'suppliers/new', component: SupplierFormPage, data: { title: 'New supplier' } },
      { path: 'suppliers/:id', component: SupplierFormPage, data: { title: 'Supplier' } },
      { path: 'customers', component: CustomerListPage, data: { title: 'Customers' } },
      { path: 'customers/new', component: CustomerFormPage, data: { title: 'New customer' } },
      { path: 'customers/:id', component: CustomerFormPage, data: { title: 'Customer' } },
      { path: 'purchases', component: PurchaseListPage, data: { title: 'Purchases' } },
      { path: 'purchases/new', component: PurchaseFormPage, data: { title: 'New purchase' } },
      { path: 'purchases/:id', component: PurchaseFormPage, data: { title: 'Purchase' } },
      { path: 'expenses', component: ExpenseListPage, data: { title: 'Expenses' } },
      { path: 'expenses/new', component: ExpenseFormPage, data: { title: 'New expense' } },
      { path: 'expenses/:id', component: ExpenseFormPage, data: { title: 'Expense' } },
      { path: 'pos', component: PosPage, data: { title: 'POS' } },
      { path: 'sales', component: SaleListPage, data: { title: 'Sales' } },
      { path: 'account', component: PasswordPage, data: { title: 'Password' } },
      { path: 'users', component: UserListPage, data: { title: 'Users' } },
      { path: 'audit', component: AuditPage, data: { title: 'Audit' } },
      { path: 'users/new', component: UserFormPage, data: { title: 'New user' } },
      { path: 'users/:id', component: UserFormPage, data: { title: 'User' } },
      { path: 'sales/:id', component: SaleDetailPage, data: { title: 'Receipt' } },
      { path: '**', redirectTo: 'catalog/products' },
    ],
  },
];

function catalogNamesRedirect(tab: NamedCatalogTab): RedirectFunction {
  return () => inject(Router).createUrlTree(['/catalog/names'], { queryParams: { tab } });
}

function inventoryRedirect(tab: InventoryTab): RedirectFunction {
  return () => inject(Router).createUrlTree(['/inventory'], { queryParams: { tab } });
}
