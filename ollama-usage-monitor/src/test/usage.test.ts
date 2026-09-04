import { test } from 'node:test';
import assert from 'node:assert';
import { parseUsageHtml } from '../usageParser';
import { formatCountdown, formatStatusBar, formatTooltip, formatPercent } from '../usageFormatter';
import { applyBold, styleDecisionFor } from '../barStyle';

test('parser: valid HTML (session + weekly + reset)', () => {
  const html = `
    <div>Session usage</div><div>35.0%</div>
    <div>Resets in 3 hours</div>
    <div>Weekly usage</div><div>18.0%</div>
    <div>Resets in 2 days</div>
  `;
  const u = parseUsageHtml(html);
  assert.strictEqual(u.status, 'ok');
  assert.strictEqual(u.sessionPercent, 35);
  assert.ok(u.sessionResetAt instanceof Date);
  assert.strictEqual(u.weeklyPercent, 18);
  assert.ok(u.weeklyResetAt instanceof Date);
});

test('parser: login page (Вход) → status login, no data', () => {
  const html = `<html><head><title>Вход</title></head><body>hosted-authkit sign-in form</body></html>`;
  const u = parseUsageHtml(html);
  assert.strictEqual(u.status, 'login');
  assert.strictEqual(u.sessionPercent, null);
  assert.strictEqual(u.sessionResetAt, null);
});

test('parser: login page (Sign in) → status login', () => {
  const html = `<html><head><title>Sign in</title></head><body>hosted-authkit</body></html>`;
  const u = parseUsageHtml(html);
  assert.strictEqual(u.status, 'login');
});

test('parser: normal page with hosted-authkit in unrelated script → NOT login', () => {
  // hosted-authkit встречается в скриптах даже на залогиненной странице;
  // решающий признак — <title>Вход/Sign in</title>.
  const html = `<html><head><title>Settings</title></head><body><script src="/apps/hosted-authkit/production/x.js"></script><div>Session usage</div><div>35.0%</div></body></html>`;
  const u = parseUsageHtml(html);
  assert.strictEqual(u.status, 'ok');
  assert.strictEqual(u.sessionPercent, 35);
});

test('parser: session only, no weekly', () => {
  const html = `<div>Session usage</div><div>95.0%</div><div>Resets in 25 minutes</div>`;
  const u = parseUsageHtml(html);
  assert.strictEqual(u.sessionPercent, 95);
  assert.ok(u.sessionResetAt instanceof Date);
  assert.strictEqual(u.weeklyPercent, null);
});

test('parser: missing fields → null', () => {
  const u = parseUsageHtml('<html>no usage here</html>');
  assert.strictEqual(u.sessionPercent, null);
  assert.strictEqual(u.sessionResetAt, null);
  assert.strictEqual(u.weeklyPercent, null);
});

test('parser: malformed (empty) → nulls, no throw', () => {
  assert.doesNotThrow(() => parseUsageHtml(''));
  const u = parseUsageHtml('');
  assert.strictEqual(u.sessionPercent, null);
});

test('parser: fallback "NN.N % used"', () => {
  const u = parseUsageHtml('<div>42.5 % used</div>');
  assert.strictEqual(u.sessionPercent, 42.5); // БЕЗ округления
});

test('parser: comma decimal', () => {
  const u = parseUsageHtml('<div>Session usage</div><div>12,5%</div>');
  assert.strictEqual(u.sessionPercent, 12.5); // БЕЗ округления
});

test('parser: data-time block → точное время ресета', () => {
  const iso = '2026-09-02T08:00:00Z';
  const html = `
    <div>Session usage</div><div>35,4%</div>
    <div class="text-xs text-neutral-500 mt-1 local-time" data-time="${iso}" title="ср, 2 сент., 13:00">
      Resets in 4 hours.
    </div>
  `;
  const u = parseUsageHtml(html);
  assert.strictEqual(u.sessionPercent, 35.4);
  assert.ok(u.sessionResetAt instanceof Date);
  assert.strictEqual(u.sessionResetAt!.toISOString(), '2026-09-02T08:00:00.000Z'); // data-time, а не «+4ч»
});

test('countdown: 0s', () => {
  const now = 1000000;
  assert.strictEqual(formatCountdown(new Date(now), now), '0м');
});

test('countdown: 59s', () => {
  const now = 1000000;
  assert.strictEqual(formatCountdown(new Date(now + 59 * 1000), now), '59с');
});

test('countdown: 1m', () => {
  const now = 1000000;
  assert.strictEqual(formatCountdown(new Date(now + 60 * 1000), now), '1м');
});

test('countdown: 59m', () => {
  const now = 1000000;
  assert.strictEqual(formatCountdown(new Date(now + 59 * 60 * 1000), now), '59м');
});

test('countdown: 1h', () => {
  const now = 1000000;
  assert.strictEqual(formatCountdown(new Date(now + 60 * 60 * 1000), now), '1ч');
});

test('countdown: 1ч 20м', () => {
  const now = 1000000;
  assert.strictEqual(formatCountdown(new Date(now + 80 * 60 * 1000), now), '1ч 20м');
});

test('countdown: 2ч 5м', () => {
  const now = 1000000;
  assert.strictEqual(formatCountdown(new Date(now + 125 * 60 * 1000), now), '2ч 5м');
});

test('countdown: 23h', () => {
  const now = 1000000;
  assert.strictEqual(formatCountdown(new Date(now + 23 * 60 * 60 * 1000), now), '23ч');
});

test('countdown: 24h → 1д 0ч', () => {
  const now = 1000000;
  assert.strictEqual(formatCountdown(new Date(now + 24 * 60 * 60 * 1000), now), '1д');
});

test('countdown: multi-day', () => {
  const now = 1000000;
  assert.strictEqual(formatCountdown(new Date(now + (2 * 24 + 5) * 60 * 60 * 1000), now), '2д 5ч');
});

test('countdown: null → —', () => {
  assert.strictEqual(formatCountdown(null), '—');
});

test('percent: запятая, без округления', () => {
  assert.strictEqual(formatPercent(35.4), '35,4');
  assert.strictEqual(formatPercent(0), '0');
  assert.strictEqual(formatPercent(12.5), '12,5');
  assert.strictEqual(formatPercent(null), '—');
});

test('statusBar: ok format с запятой', () => {
  const u = { sessionPercent: 35.4, sessionResetAt: new Date(Date.now() + 3 * 3600 * 1000), weeklyPercent: null, weeklyResetAt: null, account: null, status: 'ok' as const };
  assert.match(formatStatusBar(u, true), /^🦙 35,4% 3ч$/);
});

test('statusBar: no usage → 🦙 —', () => {
  assert.strictEqual(formatStatusBar(null, true), '🦙 —');
});

test('statusBar: no emoji', () => {
  const u = { sessionPercent: 35, sessionResetAt: null, weeklyPercent: null, weeklyResetAt: null, account: null, status: 'ok' as const };
  assert.strictEqual(formatStatusBar(u, false), '35% —');
});

test('tooltip: contains session and weekly', () => {
  const u = { sessionPercent: 35, sessionResetAt: new Date(Date.now() + 3600 * 1000), weeklyPercent: 20, weeklyResetAt: new Date(Date.now() + 24 * 3600 * 1000), account: null, status: 'ok' as const };
  const t = formatTooltip(u);
  assert.match(t, /Session: 35%/);
  assert.match(t, /Weekly: 20%/);
});

test('applyBold: цифры → Mathematical Bold', () => {
  const b = applyBold('35'); // '3'=U+1D7D1, '5'=U+1D7D3
  assert.strictEqual(b, '\u{1D7D1}\u{1D7D3}');
  assert.notStrictEqual(b, '35');
});

test('styleFor: диапазоны цветов', () => {
  assert.strictEqual(styleDecisionFor(0.5).bold, true);
  assert.strictEqual(styleDecisionFor(0.5).backgroundKind, 'warning'); // фон 0-1%
  assert.strictEqual(styleDecisionFor(50).foreground, '#00ff00'); // lime
  assert.strictEqual(styleDecisionFor(75).foreground, '#00ffff'); // cyan
  assert.strictEqual(styleDecisionFor(92).foreground, '#ffa500'); // orange bold
  assert.strictEqual(styleDecisionFor(96).foreground, '#ff0000'); // red bold
});
