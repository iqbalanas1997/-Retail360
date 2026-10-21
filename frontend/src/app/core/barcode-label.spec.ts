import { code128Bars, type Code128Label, createInternalBarcode, nextInternalBarcode, SHOP_BARCODE_PREFIX } from './barcode-label';

describe('barcode labels', () => {
  it('draws Code 128 set B with the readable value left unchanged', () => {
    const label = code128Bars('00123');

    expect(label.bars[0]).toEqual({ x: 10, width: 2 });
    expect(label.bars[1]).toEqual({ x: 13, width: 1 });
    expect(runs(label)).toBe('2112141231221231221232212232112211322214112331112');
    expect(code128Bars('R360-00010FFF').bars.length).toBeGreaterThan(0);
    expect(() => code128Bars('')).toThrow('This barcode cannot be printed as Code 128.');
    expect(() => code128Bars('bad\ncode')).toThrow('This barcode cannot be printed as Code 128.');
  });

  it('builds a shop barcode that is not a numeric manufacturer code', () => {
    expect(createInternalBarcode(Uint8Array.from([0, 1, 15, 255]))).toBe('R360-00010FFF');
    expect(createInternalBarcode(Uint8Array.from([0, 1, 15, 255])).startsWith(SHOP_BARCODE_PREFIX)).toBe(true);
    expect(nextInternalBarcode(['R360-00010FFF'], () => 'R360-AAAAAAAA')).toBe('R360-AAAAAAAA');
    expect(() => nextInternalBarcode(['R360-00010FFF'], () => 'R360-00010FFF')).toThrow(
      'A shop barcode could not be generated. Try again.',
    );
    expect(nextInternalBarcode([], () => 'R360-00010FFF')).toBe('R360-00010FFF');
  });
});

function runs(label: Code128Label): string {
  let cursor = 10;
  let pattern = '';
  for (const bar of label.bars) {
    if (bar.x > cursor) {
      pattern += String(bar.x - cursor);
    }
    pattern += String(bar.width);
    cursor = bar.x + bar.width;
  }

  return pattern;
}
