import { UsageInfo } from './types';

/**
 * Парсер HTML страницы https://ollama.com/settings (dump-dom).
 *   - "Session usage ... NN.N" → sessionPercent (БЕЗ округления);
 *   - блок local-time с data-time (точное время ресета, ISO UTC) → sessionResetAt;
 *     fallback: "Resets in ..." (относительное) — только если data-time не найден;
 *   - "Weekly usage ... NN.N" → weeklyPercent;
 *   - имя аккаунта (кнопка-меню профиля).
 * Не бросает; при отсутствии данных → null.
 */
export function parseUsageHtml(html: string): UsageInfo {
  const out: UsageInfo = {
    sessionPercent: null,
    sessionResetAt: null,
    weeklyPercent: null,
    weeklyResetAt: null,
    account: null,
    status: 'ok',
  };

  // Если страница — форма входа (профиль разлогинен), данных нет.
  // Надёжный признак — <title>Вход</title> / <title>Sign in</title>.
  // (hosted-authkit / sign-in встречаются в скриптах и на залогиненной странице,
  // поэтому по ним одним определять НЕЛЬЗЯ.)
  const isLoginPage = /<title>\s*(Вход|Sign in|Log in|Login)\s*<\/title>/i.test(html);
  if (isLoginPage) {
    out.status = 'login';
    return out;
  }

  // Session usage: "Session usage ... NN.N"
  const m = /Session usage[^0-9%]*([0-9]+(?:[.,][0-9]+)?)/.exec(html);
  if (m) {
    out.sessionPercent = parseNum(m[1]);
    // Блок ресета внутри блока Session usage (до блока Weekly usage).
    const wi = html.indexOf('Weekly usage', m.index);
    const end = wi > m.index ? wi : Math.min(m.index + 3000, html.length);
    const seg = html.substring(m.index, end);
    // Приоритет 1: data-time="2026-09-02T08:00:00Z" (точное UTC-время ресета).
    const dt = /data-time="([^"]+)"/.exec(seg);
    if (dt) out.sessionResetAt = parseIsoDate(dt[1]);
    // Приоритет 2 (fallback): "Resets in 4 hours" (относительное).
    if (!out.sessionResetAt) {
      const rm = /Resets in ([^<]+)/.exec(seg);
      if (rm) out.sessionResetAt = parseRelativeReset(rm[1].trim());
    }
  } else {
    // Fallback: "NN.N % used"
    const m2 = /([0-9]+(?:[.,][0-9]+)?)\s*%\s*used/.exec(html);
    if (m2) out.sessionPercent = parseNum(m2[1]);
  }

  // Weekly usage: "Weekly usage ... NN.N"
  const wm = /Weekly usage[^0-9%]*([0-9]+(?:[.,][0-9]+)?)/.exec(html);
  if (wm) {
    out.weeklyPercent = parseNum(wm[1]);
    const wseg = html.substring(wm.index, Math.min(wm.index + 3000, html.length));
    // Приоритет 1: data-time; приоритет 2: "Resets in ...".
    const wdt = /data-time="([^"]+)"/.exec(wseg);
    if (wdt) out.weeklyResetAt = parseIsoDate(wdt[1]);
    if (!out.weeklyResetAt) {
      const wrm = /Resets in ([^<]+)/.exec(wseg);
      if (wrm) out.weeklyResetAt = parseRelativeReset(wrm[1].trim());
    }
  }

  // Имя аккаунта: класс кнопки меню профиля на странице settings.
  const am = /class="[^"]*account-button[^"]*"[^>]*>([\s\S]*?)<\/button>/.exec(html)
    ?? /class="[^"]*profile[^"]*"[^>]*>([\s\S]*?)<\/button>/.exec(html);
  if (am) {
    const name = stripTags(am[1]).trim();
    if (name) out.account = name;
  }
  if (!out.account) {
    const em = /[\w.+-]+@[\w-]+\.[\w.]{2,}/.exec(html);
    if (em) out.account = em[0];
  }

  return out;
}

function parseIsoDate(s: string): Date | null {
  const d = new Date(s);
  return isNaN(d.getTime()) ? null : d;
}

function stripTags(s: string): string {
  return s.replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ').trim();
}

function parseNum(s: string): number | null {
  const n = parseFloat(s.replace(',', '.'));
  return Number.isFinite(n) ? n : null; // БЕЗ округления
}

/** "48 minutes" / "2 hours" / "3 days" → Date (относительно now). */
function parseRelativeReset(s: string): Date | null {
  const mm = /(\d+)\s*(second|minute|hour|day|week)s?/.exec(s);
  if (!mm) return null;
  const n = parseInt(mm[1], 10);
  const unit = mm[2];
  const ms = n * (unit === 'second' ? 1000 : unit === 'minute' ? 60000 : unit === 'hour' ? 3600000 : unit === 'day' ? 86400000 : 604800000);
  return new Date(Date.now() + ms);
}

