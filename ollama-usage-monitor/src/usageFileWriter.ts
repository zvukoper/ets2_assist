import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import { UsageInfo } from './types';
import { formatPercent, formatCountdown } from './usageFormatter';

/**
 * Запись результатов парсинга в файл MemoryAI/ollama_parsed_usage.txt
 * текущего открытого проекта (если папка MemoryAI и файл существуют):
 *   строка 1: процент (с запятой, без округления, напр. «35,4»);
 *   строка 2: время до ресета («1ч 20м»), вычисленное относительно текущей
 *             системной даты на момент записи.
 */
export function writeParsedUsageFile(usage: UsageInfo): string | null {
  const file = findParsedUsageFile();
  if (!file) return null;
  const pct = usage.sessionPercent === null ? 'UNKNOWN' : formatPercent(usage.sessionPercent);
  const reset = usage.sessionResetAt === null ? 'UNKNOWN' : formatCountdown(usage.sessionResetAt);
  const content = `${pct}\n${reset}\n`;
  try {
    // Файл может отсутствовать (например, удалён или ещё не создавался) —
    // создаём его (и папку MemoryAI, если её нет) при записи.
    const dir = path.dirname(file);
    if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(file, content, 'utf8');
    return file;
  } catch (e) {
    console.log('usageFile:write-error ' + (e instanceof Error ? e.message : String(e)));
    return null;
  }
}

/**
 * Ищет путь <workspaceRoot>/MemoryAI/ollama_parsed_usage.txt (первая папка workspace).
 * Возвращает путь, если папка MemoryAI существует (сам файл может отсутствовать —
 * он будет создан при записи). Если папки MemoryAI нет — null.
 */
export function findParsedUsageFile(): string | null {
  const folders = vscode.workspace.workspaceFolders;
  if (!folders || folders.length === 0) return null;
  for (const f of folders) {
    const candidate = path.join(f.uri.fsPath, 'MemoryAI', 'ollama_parsed_usage.txt');
    try {
      if (fs.existsSync(path.dirname(candidate))) return candidate;
    } catch { /* ignore */ }
  }
  return null;
}