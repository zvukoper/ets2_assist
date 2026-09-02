/**
 * Внутренняя нормализованная модель usage (не привязана к сырому JSON API).
 */
export interface UsageInfo {
  /** Процент session usage БЕЗ округления (как на странице: 35.4 → 35.4). */
  sessionPercent: number | null;
  /** Точное время ресета: из блока local-time (data-time), fallback — относительное "Resets in ...". */
  sessionResetAt: Date | null;
  weeklyPercent: number | null;
  weeklyResetAt: Date | null;
  /** Имя/логин залогиненного пользователя со страницы settings (null если не найдено). */
  account: string | null;
}

/** Состояние статус-бара. */
export type StatusState =
  | { kind: 'ok'; usage: UsageInfo }
  | { kind: 'no-key' }
  | { kind: 'error'; message: string };
