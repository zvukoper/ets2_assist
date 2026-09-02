/**
 * Чистая логика стиля статус-бара (БЕЗ vscode — тестируется в plain node).
 * Диапазоны цвета по проценту:
 *   0–1%   → белый жирный + зелёный фон (#014700, эмуляция через warning);
 *   1–70%  → lime (#00ff00), обычный;
 *   70–90% → cyan (#00ffff);
 *   ≥90%   → оранжевый (#ffa500) жирный;
 *   ≥95%   → красный (#ff0000) жирный.
 */
export interface BarStyleDecision {
  foreground: string | undefined;
  /** 'warning' → ThemeColor('statusBarItem.warningBackground'); undefined → без фона. */
  backgroundKind: 'warning' | undefined;
  bold: boolean;
}

export function styleDecisionFor(pct: number | null): BarStyleDecision {
  if (pct !== null && pct >= 0 && pct <= 1) {
    return { foreground: '#ffffff', backgroundKind: 'warning', bold: true };
  }
  if (pct !== null && pct >= 95) {
    return { foreground: '#ff0000', backgroundKind: undefined, bold: true };
  }
  if (pct !== null && pct >= 90) {
    return { foreground: '#ffa500', backgroundKind: undefined, bold: true };
  }
  if (pct !== null && pct >= 70) {
    return { foreground: '#00ffff', backgroundKind: undefined, bold: false };
  }
  if (pct !== null) {
    return { foreground: '#00ff00', backgroundKind: undefined, bold: false };
  }
  return { foreground: undefined, backgroundKind: undefined, bold: false };
}

/**
 * Unicode Mathematical Bold: цифры 0-9 (U+1D7CE..), латиница A-Z (U+1D400..),
 * a-z (U+1D41A..); %, 🦙, пробелы остаются как есть. Визуально жирный текст
 * (настоящий font-weight у StatusBarItem API отсутствует).
 */
export function applyBold(text: string): string {
  let out = '';
  for (const ch of text) {
    const c = ch.codePointAt(0)!;
    if (c >= 0x30 && c <= 0x39) out += String.fromCodePoint(0x1d7ce + (c - 0x30));
    else if (c >= 0x41 && c <= 0x5a) out += String.fromCodePoint(0x1d400 + (c - 0x41));
    else if (c >= 0x61 && c <= 0x7a) out += String.fromCodePoint(0x1d41a + (c - 0x61));
    else out += ch;
  }
  return out;
}