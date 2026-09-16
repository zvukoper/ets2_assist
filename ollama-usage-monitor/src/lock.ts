import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';

/**
 * МЕЖПРОЦЕССНЫЙ ЗАМОК НА ПРОФИЛЬ EDGE.
 *
 * ЗАЧЕМ (корень «иногда работает, иногда пустой DOM»):
 * Edge с одним и тем же --user-data-dir НЕ допускает второй экземпляр: второй
 * запуск «делегирует» первому и завершается с кодом 21 и ПУСТЫМ stdout.
 * Проверено экспериментально:
 *   - одиночный запуск                 → exit 0, DOM получен;
 *   - запуск при живом держателе       → exit 21, вывод ПУСТОЙ;
 *   - два одновременных запуска        → один exit 0, второй exit 21 (пусто).
 * У расширения несколько источников параллельных запусков: активация, таймер,
 * изменение конфигурации, ДРУГОЕ ОКНО VS Code (свой extension host — общий
 * JS-флаг inFlight его не видит). Отсюда «через ВПН и перезапуск иногда
 * срабатывает»: зависит от того, наложились запросы или нет.
 *
 * Замок — файл рядом с профилем. Внутри pid + время; «протухший» замок
 * (процесс мёртв или старше maxAgeMs) удаляется, чтобы сбой не залипал навсегда.
 */
export interface LockHandle {
  release(): void;
}

export function lockPathForProfile(profileDir: string): string {
  return profileDir + '.lock';
}

function tryAcquire(lockPath: string): boolean {
  try {
    // 'wx' — атомарное создание: падает, если файл уже есть.
    const fd = fs.openSync(lockPath, 'wx');
    try {
      fs.writeSync(fd, JSON.stringify({ pid: process.pid, at: Date.now() }));
    } finally {
      fs.closeSync(fd);
    }
    return true;
  } catch {
    return false;
  }
}

function isStale(lockPath: string, maxAgeMs: number): boolean {
  try {
    const raw = fs.readFileSync(lockPath, 'utf8');
    const info = JSON.parse(raw) as { pid?: number; at?: number };
    const age = Date.now() - (info.at ?? 0);
    if (age > maxAgeMs) return true;               // слишком старый
    if (typeof info.pid === 'number' && info.pid > 0) {
      if (info.pid === process.pid) return false;
      try {
        process.kill(info.pid, 0);                 // 0 = только проверка
        return false;                              // процесс жив
      } catch {
        return true;                               // процесса нет → замок мёртв
      }
    }
    return false;
  } catch {
    return true;                                   // битый/нечитаемый → снять
  }
}

/**
 * Ждёт освобождения замка до waitMs. Возвращает handle (замок взят) или null.
 */
export async function acquireProfileLock(
  profileDir: string,
  waitMs = 20000,
  maxAgeMs = 120000
): Promise<LockHandle | null> {
  const lockPath = lockPathForProfile(profileDir);
  const deadline = Date.now() + waitMs;
  // Небольшая случайная задержка — разводит одновременные окна VS Code.
  await sleep(Math.floor(Math.random() * 250));

  for (;;) {
    if (tryAcquire(lockPath)) {
      return { release: () => { try { fs.unlinkSync(lockPath); } catch { /* ignore */ } } };
    }
    if (isStale(lockPath, maxAgeMs)) {
      try { fs.unlinkSync(lockPath); } catch { /* ignore */ }
      continue;                                    // сразу пробуем снова
    }
    if (Date.now() >= deadline) return null;
    await sleep(300);
  }
}

/** Принудительно снять замок (команда «Reset»). */
export function forceRemoveLock(profileDir: string): boolean {
  const lockPath = lockPathForProfile(profileDir);
  try {
    if (fs.existsSync(lockPath)) { fs.unlinkSync(lockPath); return true; }
  } catch { /* ignore */ }
  return false;
}

/** Профиль Edge по умолчанию (тот же, что в ollama_usage.ps1). */
export function defaultProfileDir(): string {
  return path.join(os.homedir(), 'AppData', 'Local', 'ETS2_Assist', 'ollama-edge-profile');
}

/** Singleton-замки Edge (могут остаться после жёсткого kill). */
export function singletonLockFiles(profileDir: string): string[] {
  const out: string[] = [];
  try {
    for (const f of fs.readdirSync(profileDir)) {
      if (f.startsWith('Singleton')) out.push(path.join(profileDir, f));
    }
  } catch { /* ignore */ }
  return out;
}

export function sleep(ms: number): Promise<void> {
  return new Promise((r) => setTimeout(r, ms));
}
