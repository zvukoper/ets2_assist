import { spawn } from 'child_process';
import * as os from 'os';
import * as path from 'path';
import * as fs from 'fs';
import { UsageInfo } from './types';
import { parseUsageHtml } from './usageParser';
import { acquireProfileLock, defaultProfileDir, sleep } from './lock';

export type AttemptFailure =
  | 'NO_BROWSER'      // Edge не найден
  | 'PROFILE_BUSY'    // профиль занят (exit 21 + пустой вывод)
  | 'EMPTY_DOM'       // код 0, но DOM пуст
  | 'TIMEOUT'         // не дождались
  | 'SPAWN_ERROR';    // Edge не запустился

/**
 * Клиент получения usage БЕЗ API — парсит HTML https://ollama.com/settings
 * через headless Edge с авторизованным профилем (как ollama_usage.ps1).
 * Профиль: %LOCALAPPDATA%\ETS2_Assist\ollama-edge-profile.
 *
 * v0.5: ГЛАВНЫЙ ФИКС «пустого DOM».
 *   • МЕЖПРОЦЕССНЫЙ ЗАМОК на профиль (lock.ts): Edge НЕ допускает второй
 *     экземпляр на одном --user-data-dir — второй запуск делегирует первому и
 *     отдаёт exit 21 + ПУСТОЙ stdout. Проверено: одиночный запуск → exit 0 и DOM;
 *     запуск при живом держателе/двумя одновременно → exit 21 и ПУСТО.
 *     Замок сериализует запросы даже между РАЗНЫМИ ОКНАМИ VS Code (у каждого
 *     свой extension host, поэтому JS-флаг inFlight их не разводит).
 *   • ПОВТОРНЫЕ ПОПЫТКИ: PROFILE_BUSY/EMPTY_DOM транзиентны (остаточный Edge,
 *     медленный старт) — до 3 попыток с растущей паузой.
 *   • ЖДЁМ ПОЯВЛЕНИЯ ДАННЫХ: --virtual-time-budget не гарантирует, что
 *     серверный HTML уже записан; после закрытия Edge перечитываем файл,
 *     коротко ожидая разметку.
 *   • ЧЕСТНЫЙ СТАТУС: не отдали DOM — не выдаём это за «данных нет».
 */
export class OllamaUsageClient {
  private readonly timeoutMs = 30000;
  private readonly maxAttempts = 3;

  /** Профиль Edge (используется также командами обслуживания). */
  readonly profileDir: string = defaultProfileDir();

  /** Выполняет headless Edge → dump-dom → парсит HTML → UsageInfo. */
  async fetchUsage(): Promise<UsageInfo> {
    const exe = this.findEdge();
    if (!exe) throw new Error('NO_BROWSER');

    // Замок: один запрос на профиль во всей системе (все окна VS Code).
    const lock = await acquireProfileLock(this.profileDir);
    if (!lock) return this.lockedUsage();

    try {
      let last: AttemptFailure = 'EMPTY_DOM';
      for (let attempt = 1; attempt <= this.maxAttempts; attempt++) {
        const r = await this.runEdgeOnce(exe, attempt);
        if (r.kind === 'ok') return r.usage;
        last = r.reason;
        if (attempt < this.maxAttempts) await sleep(500 * attempt);
      }
      if (last === 'PROFILE_BUSY' || last === 'TIMEOUT' || last === 'EMPTY_DOM') {
        return this.lockedUsage();
      }
      throw new Error(last);
    } finally {
      lock.release();
    }
  }

  /** Одна попытка. Никогда не бросает — возвращает результат/причину. */
  private runEdgeOnce(
    exe: string,
    attempt: number
  ): Promise<{ kind: 'ok'; usage: UsageInfo } | { kind: 'fail'; reason: AttemptFailure }> {
    return new Promise((resolve) => {
      // v0.4: Edge НЕ пишет в файл через --dump-dom <path>. Как в ps1: --dump-dom
      // (без файла) + перенаправление stdout в temp-файл.
      const dst = path.join(os.tmpdir(), `ollama_dom_${Date.now()}_${attempt}.html`);
      let outFd: number;
      try {
        outFd = fs.openSync(dst, 'w');
      } catch {
        resolve({ kind: 'fail', reason: 'SPAWN_ERROR' });
        return;
      }

      const args = [
        '--headless=new',
        '--disable-gpu',
        '--no-first-run',
        '--user-data-dir=' + this.profileDir,  // БЕЗ кавычек: spawn сам экранирует
        // v0.5: уменьшено с 15000 — страница отдаёт разметку быстро, а долгий
        // budget задерживал освобождение профиля и провоцировал exit 21.
        '--virtual-time-budget=8000',
        '--dump-dom',
        'https://ollama.com/settings',
      ];

      const child = spawn(exe, args, { windowsHide: true, stdio: ['ignore', outFd, 'ignore'] });
      let settled = false;
      const finish = (
        v: { kind: 'ok'; usage: UsageInfo } | { kind: 'fail'; reason: AttemptFailure }
      ): void => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);
        try { fs.closeSync(outFd); } catch { /* ignore */ }
        try { fs.unlinkSync(dst); } catch { /* ignore */ }
        resolve(v);
      };

      const timer = setTimeout(() => {
        try { child.kill(); } catch { /* ignore */ }
        finish({ kind: 'fail', reason: 'TIMEOUT' });
      }, this.timeoutMs);

      child.on('error', () => finish({ kind: 'fail', reason: 'SPAWN_ERROR' }));

      child.on('close', (code) => {
        void (async () => {
          let out = '';
          try { out = fs.existsSync(dst) ? fs.readFileSync(dst, 'utf8') : ''; } catch { out = ''; }

          // v0.5: даже при exit 0 разметка может не успеть записаться —
          // коротко ждём появления страницы (до 1.5 с), затем перечитываем.
          for (let i = 0; i < 6 && (!out || out.length < 512); i++) {
            await sleep(250);
            try { out = fs.existsSync(dst) ? fs.readFileSync(dst, 'utf8') : ''; } catch { out = ''; }
          }

          if (!out || out.length === 0) {
            // exit 21 = профиль занят (Edge делегировал запрос живому процессу).
            finish({ kind: 'fail', reason: code === 21 ? 'PROFILE_BUSY' : 'EMPTY_DOM' });
            return;
          }

          try {
            finish({ kind: 'ok', usage: parseUsageHtml(out) });
          } catch {
            finish({ kind: 'fail', reason: 'EMPTY_DOM' });
          }
        })();
      });
    });
  }

  private lockedUsage(): UsageInfo {
    return {
      sessionPercent: null, sessionResetAt: null,
      weeklyPercent: null, weeklyResetAt: null,
      account: null, status: 'locked',
    };
  }

  private findEdge(): string | undefined {
    const candidates = [
      'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
      'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
    ];
    for (const c of candidates) {
      try {
        if (fs.existsSync(c)) return c;
      } catch { /* ignore */ }
    }
    return undefined;
  }

  /**
   * Смена аккаунта: открывает ОБЫЧНЫЙ (видимый) Edge с тем же выделенным
   * профилем. Cookie/сессия общие с headless-профилем, поэтому после
   * logout/login headless-запросы используют новый аккаунт.
   *
   * ВАЖНО: пока это окно открыто, профиль ЗАНЯТ и headless-запросы получают
   * exit 21 — это ожидаемо, статус в UI будет «🔒 профиль занят».
   */
  openAccountSwitchPage(): boolean {
    const exe = this.findEdge();
    if (!exe) return false;
    const args = [
      `--user-data-dir=${this.profileDir}`,
      '--no-first-run',
      'https://ollama.com/settings',
    ];
    try {
      const child = spawn(exe, args, { windowsHide: false, detached: true, stdio: 'ignore' });
      child.unref();
      return true;
    } catch {
      return false;
    }
  }
}

