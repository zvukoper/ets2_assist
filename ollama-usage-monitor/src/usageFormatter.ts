import { UsageInfo } from './types';

/**
 * Форматирование countdown до reset, РУССКИЙ формат с минутами:
 *   <1м → «59с»; 1–59 мин → «20м»; 1ч+ → «1ч 20м»; 24ч+ → «1д 3ч».
 * Вычисляется относительно текущей системной даты (перед показом в виджете).
 */
export function formatCountdown(resetAt: Date | null, now: number = Date.now()): string {
  if (!resetAt) return '—';
  const diffMs = resetAt.getTime() - now;
  if (diffMs <= 0) return '0м';
  const totalMin = Math.floor(diffMs / 60000);
  if (totalMin < 1) {
    const sec = Math.floor(diffMs / 1000);
    return `${sec}с`;
  }
  if (totalMin < 60) return `${totalMin}м`;
  const hours = Math.floor(totalMin / 60);
  const mins = totalMin % 60;
  if (hours < 24) return mins > 0 ? `${hours}ч ${mins}м` : `${hours}ч`;
  const days = Math.floor(hours / 24);
  const restH = hours % 24;
  return restH > 0 ? `${days}д ${restH}ч` : `${days}д`;
}

/** Процент с запятой, БЕЗ округления: 35.4 → «35,4». */
export function formatPercent(pct: number | null): string {
  if (pct === null) return '—';
  return pct.toString().replace('.', ',');
}

/** Основной текст статус-бара: «🦙 35,4% 1ч 20м» / «🦙 —» / «🦙 ?». */
export function formatStatusBar(usage: UsageInfo | null, showEmoji: boolean): string {
  const prefix = showEmoji ? '🦙 ' : '';
  if (!usage) return `${prefix}—`;
  const pct = usage.sessionPercent;
  if (pct === null) return `${prefix}—`;
  const cd = formatCountdown(usage.sessionResetAt);
  return `${prefix}${formatPercent(pct)}% ${cd}`;
}

/** Tooltip: Session + Weekly + аккаунт. */
export function formatTooltip(usage: UsageInfo | null): string {
  if (!usage) return 'Ollama Cloud Usage\n\nNo data.';
  const sPct = formatPercent(usage.sessionPercent);
  const sCd = formatCountdown(usage.sessionResetAt);
  const wPct = formatPercent(usage.weeklyPercent);
  const wCd = formatCountdown(usage.weeklyResetAt);
  let t = `Ollama Cloud Usage\n\nSession: ${sPct}%\nReset: ${sCd}\n\nWeekly: ${wPct}%\nReset: ${wCd}`;
  if (usage.account) t += `\n\nAccount: ${usage.account}`;
  return t;
}
