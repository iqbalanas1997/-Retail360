export const EXPENSE_CATEGORIES = [
  { value: 'rent', label: 'Rent' },
  { value: 'utilities', label: 'Electricity and utilities' },
  { value: 'wages', label: 'Staff wages' },
  { value: 'transport', label: 'Transportation' },
  { value: 'supplies', label: 'Packaging and supplies' },
  { value: 'repairs', label: 'Repairs and maintenance' },
  { value: 'other', label: 'Other' },
] as const;

export interface Expense {
  id: string;
  category: string;
  amount: number;
  expenseDate: string;
  notes: string | null;
  paymentMethod: string;
  createdAt: string;
  updatedAt: string;
}

export interface ExpenseList {
  expenses: Expense[];
  total: number;
}

export interface ExpenseQuery {
  category: string;
  from: string;
  to: string;
  limit: number;
  offset: number;
}

export interface SaveExpenseBody {
  category: string;
  amount: number;
  expenseDate: string;
  notes: string | null;
  paymentMethod: string;
}

export interface CreateExpenseBody extends SaveExpenseBody {
  operationId: string;
}
