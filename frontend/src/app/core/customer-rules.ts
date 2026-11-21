import { MAX_PRICE_TEXT, parseScaledAmount } from './catalog-rules';

const AMOUNT_MESSAGE = 'Payment amount must be greater than zero with at most 2 decimal places.';

export function parseCustomerPayment(raw: string): number {
  const value = parseScaledAmount(raw, 2, MAX_PRICE_TEXT, AMOUNT_MESSAGE);
  if (value <= 0) {
    throw new Error(AMOUNT_MESSAGE);
  }

  return value;
}
