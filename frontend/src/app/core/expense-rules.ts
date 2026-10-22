import { MAX_PRICE_TEXT, parseScaledAmount } from './catalog-rules';
import { EXPENSE_CATEGORIES } from './expense.models';

const AMOUNT_MESSAGE = 'Amount must be greater than zero with at most 2 decimal places.';
const CATEGORY_MESSAGE = 'Category must be rent, utilities, wages, transport, supplies, repairs, or other.';
const DATE_MESSAGE = 'Expense date is required.';
const PAYMENT_MESSAGE = 'Payment method must be cash.';

export function expenseCategoryLabel(value: string): string {
  return EXPENSE_CATEGORIES.find((category) => category.value === value)?.label ?? value;
}

export function parseExpenseCategory(raw: string): string {
  const category = raw.trim();
  if (!EXPENSE_CATEGORIES.some((item) => item.value === category)) {
    throw new Error(CATEGORY_MESSAGE);
  }

  return category;
}

export function parseExpenseAmount(raw: string): number {
  const value = parseScaledAmount(raw, 2, MAX_PRICE_TEXT, AMOUNT_MESSAGE);
  if (value <= 0) {
    throw new Error(AMOUNT_MESSAGE);
  }

  return value;
}

export function parseExpenseDate(raw: string): string {
  const value = raw.trim();
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    throw new Error(DATE_MESSAGE);
  }

  return value;
}

export function parseExpenseNotes(raw: string): string | null {
  const notes = raw.trim();
  if (notes.length === 0) {
    return null;
  }

  if (notes.length > 1000) {
    throw new Error('Notes must be empty or at most 1000 characters.');
  }

  return notes;
}

export function parseExpensePaymentMethod(raw: string): string {
  if (raw.trim() !== 'cash') {
    throw new Error(PAYMENT_MESSAGE);
  }

  return 'cash';
}

export function expenseAttemptKey(category: string, amount: number, expenseDate: string, notes: string | null): string {
  return `${category}|${amount.toFixed(2)}|${expenseDate}|${notes ?? ''}|cash`;
}
