import { reportRange, requireReportRange } from './report-rules';

describe('report dates', () => {
  const today = '2026-10-22';

  it('builds inclusive periods from an explicit UTC date', () => {
    expect(reportRange('today', today)).toEqual({ from: '2026-10-22', to: '2026-10-22' });
    expect(reportRange('yesterday', today)).toEqual({ from: '2026-10-21', to: '2026-10-21' });
    expect(reportRange('last7', today)).toEqual({ from: '2026-10-16', to: '2026-10-22' });
    expect(reportRange('month', today)).toEqual({ from: '2026-10-01', to: '2026-10-22' });
    expect(reportRange('month', '2026-10-01')).toEqual({ from: '2026-10-01', to: '2026-10-01' });
    expect(reportRange('yesterday', '2024-03-01')).toEqual({ from: '2024-02-29', to: '2024-02-29' });
  });

  it('rejects a start date after the end date', () => {
    expect(requireReportRange('2026-10-01', '2026-10-22')).toEqual({
      from: '2026-10-01',
      to: '2026-10-22',
    });
    expect(() => requireReportRange('2026-10-22', '2026-10-21')).toThrow(
      'The start date must be on or before the end date.',
    );
    expect(() => requireReportRange('', '2026-10-22')).toThrow('Start date and end date are required.');
  });
});
