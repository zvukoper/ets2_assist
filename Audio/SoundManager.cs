 
using System;
using System.Media;
using System.IO;

namespace ETS2_Assist_GUI.Audio
{
    /// <summary>
    /// Менеджер звуковых эффектов.
    /// Отвечает за воспроизведение системных звуков и пользовательских WAV-файлов.
    /// </summary>
    public class SoundManager
    {
        private readonly string _soundsDirectory;

        /// <summary>
        /// Создаёт экземпляр SoundManager.
        /// </summary>
        /// <param name="soundsDirectory">Путь к папке с пользовательскими звуковыми файлами (опционально).</param>
        public SoundManager(string soundsDirectory = null)
        {
            _soundsDirectory = soundsDirectory;
        }

        /// <summary>
        /// Воспроизводит звук по его типу.
        /// </summary>
        /// <param name="soundType">Тип звука: "success", "icon", "error", "beep", или путь к WAV-файлу.</param>
        public void Play(string soundType)
        {
            if (string.IsNullOrEmpty(soundType))
                return;

            try
            {
                switch (soundType.ToLower())
                {
                    case "success":
                        SystemSounds.Asterisk.Play();
                        break;

                    case "icon":
                        SystemSounds.Beep.Play();
                        break;

                    case "error":
                        SystemSounds.Hand.Play();
                        break;

                    case "beep":
                        SystemSounds.Exclamation.Play();
                        break;

                    default:
                        // Если передан путь к WAV-файлу (или имя файла в папке sounds)
                        PlayWav(soundType);
                        break;
                }
            }
            catch (Exception ex)
            {
                // Подавляем ошибки звука, чтобы не нарушать работу приложения
                System.Diagnostics.Debug.WriteLine($"[SoundManager] Ошибка воспроизведения: {ex.Message}");
            }
        }

        /// <summary>
        /// Воспроизводит WAV-файл по указанному пути.
        /// </summary>
        /// <param name="filePath">Полный путь к файлу или имя файла в папке звуков.</param>
        private void PlayWav(string filePath)
        {
            string fullPath = filePath;

            // Если путь не абсолютный, ищем в папке звуков
            if (!Path.IsPathRooted(filePath) && !string.IsNullOrEmpty(_soundsDirectory))
            {
                fullPath = Path.Combine(_soundsDirectory, filePath);
                if (!File.Exists(fullPath))
                    fullPath = filePath; // возвращаем как есть, может быть полный путь
            }

            if (File.Exists(fullPath))
            {
                using (var player = new SoundPlayer(fullPath))
                {
                    player.Play();
                }
            }
            else
            {
                // Если файл не найден, проигрываем системный звук по умолчанию
                SystemSounds.Beep.Play();
            }
        }

        /// <summary>
        /// Асинхронное воспроизведение WAV-файла (без блокировки потока).
        /// </summary>
        public void PlayAsync(string filePath)
        {
            System.Threading.Tasks.Task.Run(() => Play(filePath));
        }
    }
}