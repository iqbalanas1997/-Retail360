import { MAX_PRICE_TEXT, MAX_QUANTITY_TEXT, formatScaled, parseScaledAmount } from './catalog-rules';

export const MAX_PURCHASE_LINES = 100;
const QUANTITY_MESSAGE = 'Quantity must be greater than zero with at most 3 decimal places.';
const UNIT_COST_MESSAGE = 'Unit cost must be zero or greater with at most 2 decimal places.';
const LINE_TOTAL_MESSAGE = 'Line total exceeds the maximum.';
const PURCHASE_TOTAL_MESSAGE = 'Purchase total exceeds the maximum.';
const MAX_MONEY_DIGITS = 999999999999n;

export function parsePurchaseQuantity(raw: string): number {
  const value = parseScaledAmount(raw, 3, MAX_QUANTITY_TEXT, QUANTITY_MESSAGE);
  if (value <= 0) {
    throw new Error(QUANTITY_MESSAGE);
  }

  return value;
}

export function parseUnitCost(raw: string): number {
  return parseScaledAmount(raw, 2, MAX_PRICE_TEXT, UNIT_COST_MESSAGE);
}

export function parsePurchaseReference(raw: string): string | null {
  return optionalText(raw, 'Reference', 40);
}

export function parsePurchaseNotes(raw: string): string | null {
  return optionalText(raw, 'Notes', 1000);
}

export function parsePurchaseDate(raw: string): string {
  const value = raw.trim();
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) {
    throw new Error('Purchase date is required.');
  }

  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const date = new Date(Date.UTC(year, month - 1, day));
  if (date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day) {
    throw new Error('Purchase date is required.');
  }

  return value;
}

export function purchaseLineTotal(quantity: number, unitCost: number): number {
  const product = scaledInteger(quantity, 3) * scaledInteger(unitCost, 2);
  const rounded = roundHalfAway(product, 1000n);
  if (rounded > MAX_MONEY_DIGITS) {
    throw new Error(LINE_TOTAL_MESSAGE);
  }

  return Number(rounded) / 100;
}

export function purchaseTotal(lineTotals: readonly number[]): number {
  if (lineTotals.length === 0) {
    throw new Error('A purchase needs at least one line.');
  }

  if (lineTotals.length > MAX_PURCHASE_LINES) {
    throw new Error(`A purchase can have at most ${MAX_PURCHASE_LINES} lines.`);
  }

  let sum = 0n;
  for (const total of lineTotals) {
    sum += scaledInteger(total, 2);
  }

  if (sum > MAX_MONEY_DIGITS) {
    throw new Error(PURCHASE_TOTAL_MESSAGE);
  }

  return Number(sum) / 100;
}

export function formatMoney(value: number): string {
  return formatScaled(value, 2);
}

function optionalText(raw: string, field: string, maxLength: number): string | null {
  const value = raw.trim();
  if (value.length === 0) {
    return null;
  }

  if (value.length > maxLength) {
    throw new Error(`${field} must be empty or at most ${maxLength} characters.`);
  }

  return value;
}

function scaledInteger(value: number, scale: number): bigint {
  const text = formatScaled(value, scale);
  if (!text) {
    throw new Error(scale === 2 ? UNIT_COST_MESSAGE : QUANTITY_MESSAGE);
  }

  return BigInt(text.replace('.', ''));
}

function roundHalfAway(value: bigint, divisor: bigint): bigint {
  const half = divisor / 2n;
  return (value + half) / divisor;
}
