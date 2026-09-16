import { spawnSync } from 'child_process';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { defaultProfileDir, forceRemoveLock, singletonLockFiles } from './lock';

/**
 * ОБСЛУЖИВАНИЕ ВНУТРЕННИХ МЕХАНИЗМОВ (запрос пользователя:
 * «Может добавить какую-то команду очистки и перезагрузки внутренних
 * механизмов?»).
 *
 * ЧТО ЛЕЧИТ (воспроизведённые причины «пустого DOM»):
 *  1. остаточные процессы Edge, держащие профиль → следующий запуск получает
 *     exit 21 и пустой вывод (Edge делегирует запрос живому процессу);
 *  2. зависший межпроцессный замок расширения (lock.ts);
 *  3. остаточные Singleton-замки профиля (переживают жёсткий kill);
 *  4. мусорные temp-файлы dump-dom, включая пустые.
 *
 * ВАЖНО: закрываются ТОЛЬКО процессы Edge, запущенные с профилем расширения
 * (ollama-edge-profile). Ваш обычный Edge не тронет — это критично.
 */

export interface ResetReport {
  killedProcesses: number;
  lockRemoved: boolean;
  singletonLocksRemoved: number;
  tempFilesRemoved: number;
}

/** Находит PID процессов Edge, использующих профиль расширения. */
function edgePidsForProfile(profileDir: string): number[] {
  const pids: number[] = [];
  if (process.platform !== 'win32') return pids;
  try {
    // CommandLine надёжнее, чем имя: закрываем ТОЛЬКО наш профиль.
    const ps =
      'Get-CimInstance Win32_Process -Filter "Name=\'msedge.exe\'" | ' +
      'Select-Object ProcessId,CommandLine | ConvertTo-Json -Compress';
    const r = spawnSync('powershell', ['-NoProfile', '-Command', ps], {
      encoding: 'utf8', windowsHide: true, timeout: 20000,
    });
    const raw = (r.stdout ?? '').trim();
    if (!raw) return pids;
    const parsed = JSON.parse(raw) as unknown;
    const arr = Array.isArray(parsed) ? parsed : [parsed];
    const needle = profileDir.toLowerCase();
    for (const item of arr as Array<{ ProcessId?: number; CommandLine?: string }>) {
      const cmd = (item.CommandLine ?? '').toLowerCase();
      if (item.ProcessId && cmd.includes(needle)) pids.push(item.ProcessId);
    }
  } catch { /* нет прав/нет Edge — не критично */ }
  return pids;
}

/** Снимает остаточные Singleton-замки профиля Edge. */
function cleanSingletonLocks(profileDir: string): number {
  let n = 0;
  for (const f of singletonLockFiles(profileDir)) {
    try { fs.unlinkSync(f); n++; } catch { /* ignore */ }
  }
  return n;
}

/** Удаляет накопленные temp-файлы dump-dom (в т.ч. пустые). */
function cleanTempFiles(): number {
  let n = 0;
  try {
    const dir = os.tmpdir();
    for (const f of fs.readdirSync(dir)) {
      if (f.startsWith('ollama_dom_') && f.endsWith('.html')) {
        try { fs.unlinkSync(path.join(dir, f)); n++; } catch { /* ignore */ }
      }
    }
  } catch { /* ignore */ }
  return n;
}

/**
 * Полный сброс: гасит наши Edge, снимает замки и чистит temp.
 * Возвращает отчёт для показа пользователю.
 */
export function resetInternals(profileDir: string = defaultProfileDir()): ResetReport {
  const pids = edgePidsForProfile(profileDir);
  let killed = 0;
  for (const pid of pids) {
    try {
      process.kill(pid, 'SIGKILL');
      killed++;
    } catch { /* уже умер */ }
  }

  const lockRemoved = forceRemoveLock(profileDir);
  const singletonLocksRemoved = cleanSingletonLocks(profileDir);
  const tempFilesRemoved = cleanTempFiles();

  return { killedProcesses: killed, lockRemoved, singletonLocksRemoved, tempFilesRemoved };
}
