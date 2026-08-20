using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ETS2_Assist_GUI.UI
{
    /// <summary>
    /// Управляет списком треков в GUI:
    /// - Обновление списка из папки saved_tracks
    /// - Открытие плеера по двойному клику
    /// </summary>
    public class TrackListManager
    {
        private readonly Logger _logger;
        private readonly ListBox _listBox;
        private readonly string _tracksDirectory;

        public TrackListManager(Logger logger, ListBox listBox)
        {
            _logger = logger;
            _listBox = listBox;
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _tracksDirectory = Path.Combine(baseDir, "data", "saved_tracks");
        }

        /// <summary>
        /// Обновляет список треков в ListBox.
        /// </summary>
        public void RefreshList()
        {
            if (_listBox.InvokeRequired)
            {
                _listBox.Invoke(new Action(RefreshList));
                return;
            }

            try
            {
                if (!Directory.Exists(_tracksDirectory))
                {
                    Directory.CreateDirectory(_tracksDirectory);
                }

                var files = Directory.GetFiles(_tracksDirectory, "*.html")
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .Where(name => name.StartsWith("track_"))
                    .OrderByDescending(name => name)
                    .ToList();

                _listBox.Items.Clear();
                if (files.Count == 0)
                {
                    _listBox.Items.Add("(нет сохранённых треков)");
                }
                else
                {
                    foreach (var name in files)
                    {
                        _listBox.Items.Add(name);
                    }
                }

                _logger.Log($"Список треков обновлён: {files.Count} треков.");
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка обновления списка треков: {ex.Message}");
            }
        }

        /// <summary>
        /// Открывает выбранный трек в браузере.
        /// </summary>
        public void OpenSelectedTrack()
        {
            if (_listBox.SelectedItem == null) return;

            var selected = _listBox.SelectedItem.ToString();
            if (string.IsNullOrEmpty(selected) || selected.StartsWith("(")) return;

            try
            {
                var filePath = Path.Combine(_tracksDirectory, $"{selected}.html");
                if (!File.Exists(filePath))
                {
                    _logger.Log($"Файл трека не найден: {filePath}");
                    return;
                }

                // Открываем через локальный веб-сервер (порт 8082)
                var url = $"http://localhost:8082/saved_tracks/{selected}.html";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                _logger.Log($"Открыт трек: {selected}");
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка открытия трека: {ex.Message}");
            }
        }

        /// <summary>
        /// Устанавливает обработчик двойного клика.
        /// </summary>
        public void AttachDoubleClickHandler()
        {
            _listBox.DoubleClick += (s, e) => OpenSelectedTrack();
            _listBox.KeyPress += (s, e) => { if (e.KeyChar == (char)Keys.Enter) OpenSelectedTrack(); };
        }

        /// <summary>
        /// Удаляет трек из папки и из списка.
        /// </summary>
        public bool DeleteSelectedTrack()
        {
            if (_listBox.SelectedItem == null) return false;

            var selected = _listBox.SelectedItem.ToString();
            if (string.IsNullOrEmpty(selected) || selected.StartsWith("(")) return false;

            try
            {
                var baseName = selected;
                var filesToDelete = Directory.GetFiles(_tracksDirectory, $"{baseName}.*");
                foreach (var file in filesToDelete)
                {
                    File.Delete(file);
                    _logger.Log($"Удалён файл: {Path.GetFileName(file)}");
                }

                RefreshList();
                return true;
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка удаления трека: {ex.Message}");
                return false;
            }
        }
    }
}