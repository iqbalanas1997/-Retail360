import { formatScaled } from './catalog-rules';
import { parsePurchaseQuantity, purchaseLineTotal } from './purchase-rules';

export const MAX_SALE_LINES = 100;
const SALE_TOTAL_MESSAGE = 'Sale total exceeds the maximum.';
const MAX_MONEY_DIGITS = 999999999999n;

export function parseSaleQuantity(raw: string): number {
  return parsePurchaseQuantity(raw);
}

export function saleLineTotal(quantity: number, unitPrice: number): number {
  return purchaseLineTotal(quantity, unitPrice);
}

export function saleTotal(lineTotals: readonly number[]): number {
  if (lineTotals.length === 0) {
    throw new Error('A sale needs at least one line.');
  }

  if (lineTotals.length > MAX_SALE_LINES) {
    throw new Error(`A sale can have at most ${MAX_SALE_LINES} lines.`);
  }

  let sum = 0n;
  for (const total of lineTotals) {
    const text = formatScaled(total, 2);
    if (!text) {
      throw new Error(SALE_TOTAL_MESSAGE);
    }

    sum += BigInt(text.replace('.', ''));
  }

  if (sum > MAX_MONEY_DIGITS) {
    throw new Error(SALE_TOTAL_MESSAGE);
  }

  return Number(sum) / 100;
}

export function saleAttemptKey(
  lines: readonly { productId: string; shelfId: string; quantity: number }[],
): string {
  return lines
    .map((line) => `${line.productId}|${line.shelfId}|${formatScaled(line.quantity, 3)}`)
    .join('||');
}

export function formatSoldAt(value: string): string {
  const match = /^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2}:\d{2})/.exec(value);
  return match ? `${match[1]} ${match[2]} UTC` : value;
}

export function parseReceiptNumber(raw: string): number | null {
  const value = raw.trim();
  if (value.length === 0) {
    return null;
  }

  if (!/^[1-9]\d*$/.test(value)) {
    throw new Error('Receipt number must be greater than zero.');
  }

  const number = Number(value);
  if (!Number.isSafeInteger(number)) {
    throw new Error('Receipt number must be greater than zero.');
  }

  return number;
}
