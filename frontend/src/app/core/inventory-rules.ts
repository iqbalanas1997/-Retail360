import { MAX_QUANTITY_TEXT, parseScaledAmount } from './catalog-rules';

const POSITIVE_QUANTITY = 'Quantity must be greater than zero with at most 3 decimal places.';
const ADJUSTMENT_QUANTITY = 'Adjustment quantity must be non-zero with at most 3 decimal places.';

export function parsePositiveQuantity(raw: string): number {
  const value = parseScaledAmount(raw, 3, MAX_QUANTITY_TEXT, POSITIVE_QUANTITY);
  if (value <= 0) {
    throw new Error(POSITIVE_QUANTITY);
  }

  return value;
}

export function parseAdjustmentQuantity(raw: string): number {
  const text = raw.trim();
  const negative = text.startsWith('-');
  const magnitude = negative ? text.slice(1) : text;
  const value = parseScaledAmount(magnitude, 3, MAX_QUANTITY_TEXT, ADJUSTMENT_QUANTITY);
  if (value === 0) {
    throw new Error(ADJUSTMENT_QUANTITY);
  }

  return negative ? -value : value;
}

export function parseReason(raw: string, required: boolean): string {
  const reason = raw.trim();
  if (!required && reason.length === 0) {
    return '';
  }

  if (reason.length < 1 || reason.length > 200) {
    throw new Error('Reason must be between 1 and 200 characters.');
  }

  return reason;
}

export class OperationAttempt {
  private id: string | null = null;
  private key = '';

  constructor(private readonly nextId: () => string = () => crypto.randomUUID()) {}

  idFor(key: string): string {
    if (this.id === null || this.key !== key) {
      this.id = this.nextId();
      this.key = key;
    }

    return this.id;
  }

  complete(): void {
    this.id = null;
    this.key = '';
  }
}
