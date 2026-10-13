export const NAME_MAX_LENGTH = 200;
export const BARCODE_MAX_LENGTH = 128;
export const MAX_PRICE_TEXT = '9999999999.99';
export const MAX_QUANTITY_TEXT = '99999999999.999';

const PRICE_MESSAGE = 'Price must be zero or greater with at most 2 decimal places.';
const QUANTITY_MESSAGE = 'Minimum stock must be zero or greater with at most 3 decimal places.';

export function parseName(raw: string): string {
  const name = raw.trim();
  if (name.length < 1 || name.length > NAME_MAX_LENGTH) {
    throw new Error(`Name must be between 1 and ${NAME_MAX_LENGTH} characters.`);
  }

  return name;
}

export function parsePrice(raw: string): number {
  return parseScaled(raw, 2, MAX_PRICE_TEXT, PRICE_MESSAGE);
}

export function parseMinimumStock(raw: string): number {
  return parseScaled(raw, 3, MAX_QUANTITY_TEXT, QUANTITY_MESSAGE);
}

export function parseBarcodes(raw: readonly string[]): string[] {
  const values: string[] = [];
  const seen = new Set<string>();
  for (const entry of raw) {
    const barcode = entry.trim();
    if (barcode.length === 0) {
      continue;
    }

    if (barcode.length > BARCODE_MAX_LENGTH) {
      throw new Error(`Barcode must be between 1 and ${BARCODE_MAX_LENGTH} characters.`);
    }

    if (seen.has(barcode)) {
      throw new Error('Barcode is duplicated on the product.');
    }

    seen.add(barcode);
    values.push(barcode);
  }

  return values;
}

export function formatScaled(value: number, scale: number): string {
  if (!Number.isFinite(value)) {
    return '';
  }

  const digits = scaledDigits(JSON.stringify(value), scale);
  if (digits === null) {
    return '';
  }

  const whole = digits.slice(0, -scale) || '0';
  const fraction = digits.slice(-scale);
  return `${whole}.${fraction}`;
}

function parseScaled(raw: string, scale: number, maximum: string, message: string): number {
  const digits = scaledDigits(raw.trim(), scale);
  const limit = scaledDigits(maximum, scale);
  if (digits === null || limit === null || compareDigits(digits, limit) > 0) {
    throw new Error(message);
  }

  const value = Number(raw.trim());
  const roundTrip = scaledDigits(JSON.stringify(value), scale);
  if (!Number.isFinite(value) || roundTrip !== digits) {
    throw new Error(message);
  }

  return value;
}

function scaledDigits(text: string, scale: number): string | null {
  const match = /^(\d+)(?:\.(\d+))?$/.exec(text);
  if (!match || (match[2]?.length ?? 0) > scale) {
    return null;
  }

  const fraction = (match[2] ?? '').padEnd(scale, '0');
  return `${match[1]}${fraction}`.replace(/^0+(?=\d)/, '');
}

function compareDigits(left: string, right: string): number {
  const width = Math.max(left.length, right.length);
  const a = left.padStart(width, '0');
  const b = right.padStart(width, '0');
  if (a === b) {
    return 0;
  }

  return a > b ? 1 : -1;
}
