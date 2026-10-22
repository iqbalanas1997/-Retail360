import { parseExpenseAmount, parseExpenseCategory, parseExpenseDate, parseExpenseNotes, parseExpensePaymentMethod } from './expense-rules';

describe('expense rules', () => {
  it('keeps a positive two-decimal amount and rejects zero, negative, and extra digits', () => {
    expect(parseExpenseAmount('12.50')).toBe(12.5);
    expect(parseExpenseAmount('0.01')).toBe(0.01);
    expect(() => parseExpenseAmount('0')).toThrow('Amount must be greater than zero');
    expect(() => parseExpenseAmount('0.00')).toThrow('Amount must be greater than zero');
    expect(() => parseExpenseAmount('-1.00')).toThrow('Amount must be greater than zero');
    expect(() => parseExpenseAmount('1.235')).toThrow('Amount must be greater than zero');
  });

  it('accepts a known category and a cash payment, and trims notes', () => {
    expect(parseExpenseCategory('rent')).toBe('rent');
    expect(() => parseExpenseCategory('food')).toThrow('Category must be');
    expect(parseExpenseDate('2026-10-22')).toBe('2026-10-22');
    expect(() => parseExpenseDate('')).toThrow('Expense date is required.');
    expect(parseExpenseNotes('  Rent  ')).toBe('Rent');
    expect(parseExpenseNotes('   ')).toBeNull();
    expect(parseExpensePaymentMethod('cash')).toBe('cash');
    expect(() => parseExpensePaymentMethod('card')).toThrow('Payment method must be cash.');
  });
});
