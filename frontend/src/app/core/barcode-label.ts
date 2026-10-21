const CODE128_PATTERNS = [
  '212222', '222122', '222221', '121223', '121322', '131222', '122213', '122312', '132212', '221213',
  '221312', '231212', '112232', '122132', '122231', '113222', '123122', '123221', '223211', '221132',
  '221231', '213212', '223112', '312131', '311222', '321122', '321221', '312212', '322112', '322211',
  '212123', '212321', '232121', '111323', '131123', '131321', '112313', '132113', '132311', '211313',
  '231113', '231311', '112133', '112331', '132131', '113123', '113321', '133121', '313121', '211331',
  '231131', '213113', '213311', '213131', '311123', '311321', '331121', '312113', '312311', '332111',
  '314111', '221411', '431111', '111224', '111422', '121124', '121421', '141122', '141221', '112214',
  '112412', '122114', '122411', '142112', '142211', '241211', '221114', '413111', '241112', '134111',
  '111242', '121142', '121241', '114212', '124112', '124211', '411212', '421112', '421211', '212141',
  '214121', '412121', '111143', '111341', '131141', '114113', '114311', '411113', '411311', '113141',
  '114131', '311141', '411131', '211412', '211214', '211232', '2331112',
];

const START_B = 104;
const STOP = 106;
const QUIET_ZONE = 10;

export const SHOP_BARCODE_PREFIX = 'R360-';

export interface Code128Bar {
  x: number;
  width: number;
}

export interface Code128Label {
  bars: Code128Bar[];
  width: number;
}

export function createInternalBarcode(bytes?: Uint8Array): string {
  const source = bytes ?? randomBytes();
  const suffix = [...source].map((value) => value.toString(16).padStart(2, '0')).join('').toUpperCase();
  return `${SHOP_BARCODE_PREFIX}${suffix}`;
}

export function nextInternalBarcode(existing: readonly string[], create: () => string = createInternalBarcode): string {
  for (let attempt = 0; attempt < 5; attempt++) {
    const value = create();
    if (!existing.includes(value)) {
      return value;
    }
  }

  throw new Error('A shop barcode could not be generated. Try again.');
}

export function code128Bars(value: string): Code128Label {
  if (value.length === 0 || [...value].some((character) => {
    const code = character.charCodeAt(0);
    return code < 32 || code > 126;
  })) {
    throw new Error('This barcode cannot be printed as Code 128.');
  }

  const symbols = [START_B];
  for (const character of value) {
    symbols.push(character.charCodeAt(0) - 32);
  }

  let checksum = symbols[0];
  for (let index = 1; index < symbols.length; index++) {
    checksum += symbols[index] * index;
  }
  symbols.push(checksum % 103);
  symbols.push(STOP);

  const bars: Code128Bar[] = [];
  let x = QUIET_ZONE;
  for (const symbol of symbols) {
    const pattern = CODE128_PATTERNS[symbol];
    let drawBar = true;
    for (const module of pattern) {
      const width = Number(module);
      if (drawBar) {
        bars.push({ x, width });
      }
      x += width;
      drawBar = !drawBar;
    }
  }

  return { bars, width: x + QUIET_ZONE };
}

function randomBytes(): Uint8Array {
  const bytes = new Uint8Array(4);
  crypto.getRandomValues(bytes);
  return bytes;
}
