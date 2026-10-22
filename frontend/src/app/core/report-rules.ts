export type ReportPeriod = 'today' | 'yesterday' | 'last7' | 'month';

const DATE_MESSAGE = 'Start date and end date are required.';
const RANGE_MESSAGE = 'The start date must be on or before the end date.';

export function utcToday(now = new Date()): string {
  return formatUtc(now);
}

export function reportRange(period: ReportPeriod, today: string): { from: string; to: string } {
  const date = parseUtcDate(today);
  if (period === 'today') {
    return { from: today, to: today };
  }

  if (period === 'yesterday') {
    const previous = addUtcDays(date, -1);
    return { from: previous, to: previous };
  }

  if (period === 'last7') {
    return { from: addUtcDays(date, -6), to: today };
  }

  return { from: formatUtc(new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1))), to: today };
}

export function requireReportRange(from: string, to: string): { from: string; to: string } {
  const start = parseUtcDate(from.trim());
  const end = parseUtcDate(to.trim());
  if (start.getTime() > end.getTime()) {
    throw new Error(RANGE_MESSAGE);
  }

  return { from: formatUtc(start), to: formatUtc(end) };
}

function parseUtcDate(value: string): Date {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value.trim());
  if (!match) {
    throw new Error(DATE_MESSAGE);
  }

  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const date = new Date(Date.UTC(year, month - 1, day));
  if (date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day) {
    throw new Error(DATE_MESSAGE);
  }

  return date;
}

function addUtcDays(date: Date, days: number): string {
  return formatUtc(new Date(date.getTime() + days * 24 * 60 * 60 * 1000));
}

function formatUtc(date: Date): string {
  const month = String(date.getUTCMonth() + 1).padStart(2, '0');
  const day = String(date.getUTCDate()).padStart(2, '0');
  return `${date.getUTCFullYear()}-${month}-${day}`;
}
