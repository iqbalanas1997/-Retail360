export interface AuditEntry {
  id: string;
  actorUserId: string | null;
  actorUserName: string | null;
  action: string;
  entityType: string;
  entityId: string;
  occurredAt: string;
  details: Record<string, string>;
}

export interface AuditQuery {
  from: string;
  to: string;
  actorUserId: string;
  action: string;
  entityType: string;
  limit: number;
  offset: number;
}

export const AUDIT_ACTIONS: ReadonlyArray<{ value: string; label: string }> = [
  { value: 'user.created', label: 'User created' },
  { value: 'user.role_changed', label: 'User role changed' },
  { value: 'user.activated', label: 'User activated' },
  { value: 'user.deactivated', label: 'User deactivated' },
  { value: 'user.deleted', label: 'User removed' },
  { value: 'product.created', label: 'Product created' },
  { value: 'product.updated', label: 'Product edited' },
  { value: 'product.deleted', label: 'Product removed' },
  { value: 'stock.opening', label: 'Opening stock' },
  { value: 'stock.adjustment', label: 'Stock adjustment' },
  { value: 'stock.transfer', label: 'Shelf transfer' },
  { value: 'purchase.created', label: 'Purchase recorded' },
  { value: 'purchase.received', label: 'Purchase received' },
  { value: 'sale.completed', label: 'Sale completed' },
  { value: 'expense.created', label: 'Expense recorded' },
  { value: 'expense.updated', label: 'Expense edited' },
  { value: 'expense.deleted', label: 'Expense removed' },
  { value: 'supplier.created', label: 'Supplier created' },
  { value: 'supplier.updated', label: 'Supplier edited' },
  { value: 'supplier.activated', label: 'Supplier activated' },
  { value: 'supplier.deactivated', label: 'Supplier deactivated' },
  { value: 'supplier.deleted', label: 'Supplier removed' },
  { value: 'customer.created', label: 'Customer created' },
  { value: 'customer.updated', label: 'Customer edited' },
  { value: 'customer.activated', label: 'Customer activated' },
  { value: 'customer.deactivated', label: 'Customer deactivated' },
  { value: 'customer.deleted', label: 'Customer removed' },
];

export const AUDIT_ENTITY_TYPES: ReadonlyArray<{ value: string; label: string }> = [
  { value: 'user', label: 'User' },
  { value: 'product', label: 'Product' },
  { value: 'stock', label: 'Stock' },
  { value: 'purchase', label: 'Purchase' },
  { value: 'sale', label: 'Sale' },
  { value: 'expense', label: 'Expense' },
  { value: 'supplier', label: 'Supplier' },
  { value: 'customer', label: 'Customer' },
];

const ACTION_LABELS = new Map(AUDIT_ACTIONS.map((action) => [action.value, action.label]));

export function auditActionLabel(action: string): string {
  return ACTION_LABELS.get(action) ?? action;
}
