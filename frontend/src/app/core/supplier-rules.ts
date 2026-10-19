import { parseName } from './catalog-rules';

export const CONTACT_PERSON_MAX_LENGTH = 200;
export const PHONE_MAX_LENGTH = 40;
export const EMAIL_MAX_LENGTH = 200;
export const ADDRESS_MAX_LENGTH = 400;
export const NOTES_MAX_LENGTH = 1000;

const EMAIL_MESSAGE = `Email must be empty or a single address of at most ${EMAIL_MAX_LENGTH} characters.`;

export function supplierName(raw: string): string {
  return parseName(raw);
}

export function optionalContactPerson(raw: string): string | null {
  return optionalText(raw, 'Contact person', CONTACT_PERSON_MAX_LENGTH);
}

export function optionalPhone(raw: string): string | null {
  return optionalText(raw, 'Phone', PHONE_MAX_LENGTH);
}

export function optionalAddress(raw: string): string | null {
  return optionalText(raw, 'Address', ADDRESS_MAX_LENGTH);
}

export function optionalNotes(raw: string): string | null {
  return optionalText(raw, 'Notes', NOTES_MAX_LENGTH);
}

export function optionalEmail(raw: string): string | null {
  const email = raw.trim();
  if (email.length === 0) {
    return null;
  }

  if (email.length > EMAIL_MAX_LENGTH || !isSingleAddress(email)) {
    throw new Error(EMAIL_MESSAGE);
  }

  return email;
}

function optionalText(raw: string, field: string, maxLength: number): string | null {
  const text = raw.trim();
  if (text.length === 0) {
    return null;
  }

  if (text.length > maxLength) {
    throw new Error(`${field} must be between 1 and ${maxLength} characters.`);
  }

  return text;
}

function isSingleAddress(value: string): boolean {
  const at = value.indexOf('@');
  if (at <= 0 || at !== value.lastIndexOf('@') || at === value.length - 1) {
    return false;
  }

  if (/\s/.test(value)) {
    return false;
  }

  const domain = value.slice(at + 1);
  const dot = domain.indexOf('.');
  return domain.length >= 3 && dot > 0 && !domain.endsWith('.');
}
