import * as vscode from 'vscode';
import { StatusState } from './types';
import { formatStatusBar, formatTooltip } from './usageFormatter';
import { styleDecisionFor, applyBold } from './barStyle';

/**
 * Управление StatusBarItem: текст, tooltip, цвет, команда по клику.
 * Цвет текста — hex-строки из barStyle.ts (item.color принимает CSS-строку,
 * она идёт напрямую в style.color). Диапазоны и ограничения API — см. barStyle.ts.
 * Фон 0–1%: официальный API допускает только error/warning ThemeColor —
 * эмулируем через 'statusBarItem.warningBackground'.
 */
export class StatusBar {
  private readonly item: vscode.StatusBarItem;
  private readonly showEmoji: () => boolean;

  constructor(showEmoji: () => boolean) {
    this.showEmoji = showEmoji;
    this.item = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
    this.item.command = 'ollamaUsage.openSettings';
    this.item.tooltip = 'Ollama Cloud Usage';
    this.item.show();
  }

  update(state: StatusState): void {
    if (state.kind === 'ok') {
      const usage = state.usage;
      const d = styleDecisionFor(usage.sessionPercent);
      const text = formatStatusBar(usage, this.showEmoji());
      this.item.text = d.bold ? applyBold(text) : text;
      this.item.tooltip = formatTooltip(usage);
      this.item.color = d.foreground;
      this.item.backgroundColor =
        d.backgroundKind === 'warning'
          ? new vscode.ThemeColor('statusBarItem.warningBackground')
          : undefined;
    } else if (state.kind === 'no-key') {
      this.item.text = this.showEmoji() ? '🦙 —' : '—';
      this.item.tooltip = 'Ollama Cloud Usage\n\nNo data.';
      this.item.color = new vscode.ThemeColor('statusBarItem.warningForeground');
      this.item.backgroundColor = undefined;
    } else if (state.kind === 'login') {
      this.item.text = this.showEmoji() ? '🦙 🔑' : '🔑';
      this.item.tooltip = 'Ollama Cloud Usage\n\nТребуется вход. Выполните "Ollama Usage: Switch Account" (logout), войдите в аккаунт и нажмите Refresh.';
      this.item.color = new vscode.ThemeColor('statusBarItem.warningForeground');
      this.item.backgroundColor = undefined;
    } else if (state.kind === 'locked') {
      this.item.text = this.showEmoji() ? '🦙 🔒' : '🔒';
      this.item.tooltip =
        'Ollama Cloud Usage\n\nПрофиль Edge занят — headless-запрос не получил страницу.\n\n' +
        'Причины: открыто окно Edge с профилем расширения (кнопка Switch Account)\n' +
        'или остался «висячий» процесс Edge после закрытия.\n\n' +
        'Что делать: выполните "Ollama Usage: Reset" — закроет только процессы Edge\n' +
        'с профилем расширения и повторит запрос.';
      this.item.color = new vscode.ThemeColor('statusBarItem.warningForeground');
      this.item.backgroundColor = undefined;
    } else if (state.kind === 'resetting') {
      this.item.text = this.showEmoji() ? '🦙 ⏳' : '⏳';
      this.item.tooltip = 'Ollama Cloud Usage\n\nСброс внутренних механизмов и повторный запрос…';
      this.item.color = undefined;
      this.item.backgroundColor = undefined;
    } else {
      this.item.text = this.showEmoji() ? '🦙 ?' : '?';
      const hint = state.hintReset
        ? '\n\nПохоже на проблему с профилем Edge (пустой DOM).\n' +
          'Выполните "Ollama Usage: Reset" — очистка и повторный запрос.'
        : '';
      this.item.tooltip = `Ollama Cloud Usage\n\nError: ${state.message}${hint}`;
      this.item.color = new vscode.ThemeColor('statusBarItem.errorForeground');
      this.item.backgroundColor = undefined;
    }
  }

  dispose(): void {
    this.item.dispose();
  }
}
