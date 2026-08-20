using System;
using System.Windows.Forms;

namespace ETS2_Assist_GUI.UI
{
    /// <summary>
    /// Отвечает за отображение уведомлений пользователю:
    /// - Всплывающие подсказки (tray)
    /// - Тосты (можно расширить)
    /// - Диалоги (сообщения, подтверждения)
    /// </summary>
    public class NotificationManager
    {
        private readonly Logger _logger;
        private readonly NotifyIcon _trayIcon;

        public NotificationManager(Logger logger = null, NotifyIcon trayIcon = null)
        {
            _logger = logger;
            _trayIcon = trayIcon;
        }

        /// <summary>
        /// Показывает сообщение в трее (balloon tip).
        /// </summary>
        public void ShowTrayNotification(string title, string message, int timeoutMs = 2000)
        {
            try
            {
                if (_trayIcon != null)
                {
                    _trayIcon.ShowBalloonTip(timeoutMs, title, message, ToolTipIcon.Info);
                }
                else
                {
                    // Запасной вариант – просто лог
                    _logger?.Log($"[Notification] {title}: {message}");
                }
            }
            catch (Exception ex)
            {
                _logger?.Log($"Ошибка показа уведомления: {ex.Message}");
            }
        }

        /// <summary>
        /// Показывает уведомление о сохранении трека.
        /// </summary>
        public void ShowTrailSaved()
        {
            ShowTrayNotification("ETS2 Assist", "Трек сохранён!", 3000);
        }

        /// <summary>
        /// Показывает уведомление о создании триггера сохранения.
        /// </summary>
        public void ShowTrailSaveTriggered()
        {
            ShowTrayNotification("ETS2 Assist", "Сохранение трека инициировано (триггер создан).", 2000);
        }

        /// <summary>
        /// Показывает уведомление о статусе подключения.
        /// </summary>
        public void ShowConnectionStatus(bool connected)
        {
            var status = connected ? "подключено" : "отключено";
            ShowTrayNotification("ETS2 Assist", $"Соединение с телеметрией {status}.", 1500);
        }

        /// <summary>
        /// Показывает сообщение об ошибке с диалогом.
        /// </summary>
        public void ShowErrorDialog(string title, string message)
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            _logger?.Log($"Ошибка: {title} - {message}");
        }

        /// <summary>
        /// Показывает информационное сообщение.
        /// </summary>
        public void ShowInfoDialog(string title, string message)
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            _logger?.Log($"Информация: {title} - {message}");
        }

        /// <summary>
        /// Показывает диалог подтверждения.
        /// </summary>
        public bool ShowConfirmDialog(string title, string message)
        {
            var result = MessageBox.Show(message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            return result == DialogResult.Yes;
        }
    }
}