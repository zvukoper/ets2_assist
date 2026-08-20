using System;
using System.Threading.Tasks;
using ETS2_Assist_GUI.Audio;
using ETS2_Assist_GUI.Services;
using ETS2_Assist_GUI.Recording;
using ETS2_Assist_GUI.Storage;
using ETS2_Assist_GUI.UI;
using ETS2_Assist_GUI.Input;
using ETS2_Assist_GUI.Core;

namespace ETS2_Assist_GUI.Core
{
    public class ApplicationController
    {
        private bool _isRunning = false;
        private bool _isStarting = false;
        private bool _hasErrors = false;
        private readonly Logger _logger;
        private readonly IntPtr _windowHandle;

        // Сервисы
        public SoundManager SoundManager { get; private set; }
        public WebSocketServer WebSocketServer { get; private set; }
        public HttpServer HttpServer { get; private set; }
        public TelemetryClient TelemetryClient { get; private set; }
        public PythonServerStarter PythonServer { get; private set; }
        public TrailRecorder TrailRecorder { get; private set; }
        public TrailSaver TrailSaver { get; private set; }
        public DataManager DataManager { get; private set; }
        public SettingsManager SettingsManager { get; private set; }
        public TrackListManager TrackListManager { get; private set; }
        public NotificationManager NotificationManager { get; private set; }
        public HotkeyManager HotkeyManager { get; private set; }

        public event Action OnStarted;
        public event Action OnStopped;

        public bool IsRunning => _isRunning;
        public bool IsStarting => _isStarting;
        public bool HasErrors => _hasErrors;

        public ApplicationController(IntPtr windowHandle, Logger logger)
        {
            _windowHandle = windowHandle;
            _logger = logger;

            // Инициализация базовых сервисов
            SoundManager = new SoundManager();
            NotificationManager = new NotificationManager();
            SettingsManager = new SettingsManager(_logger);
            DataManager = new DataManager(_logger);

            // Регистрация в ServiceLocator
            ServiceLocator.Register(this);
            ServiceLocator.Register(_logger);
            ServiceLocator.Register(SettingsManager);
            ServiceLocator.Register(DataManager);
            ServiceLocator.Register(SoundManager);
            ServiceLocator.Register(NotificationManager);

            // Создаём остальные сервисы
            PythonServer = new PythonServerStarter();
            HttpServer = new HttpServer(_logger);
            WebSocketServer = new WebSocketServer(_logger);
            TelemetryClient = new TelemetryClient(_logger);
            TrailRecorder = new TrailRecorder(_logger);
            TrailSaver = new TrailSaver(_logger, DataManager, SettingsManager);
            TrackListManager = new TrackListManager(_logger);
            HotkeyManager = new HotkeyManager(_windowHandle);

            // Подписка на события
            WebSocketServer.OnTrailReceived += OnTrailReceived;
            WebSocketServer.OnSoundRequest += OnSoundRequest;
            WebSocketServer.OnMarkerRequest += OnMarkerRequest;
            TelemetryClient.OnTelemetryUpdate += OnTelemetryUpdate;
        }

        public async Task StartAsync()
        {
            if (_isRunning || _isStarting) return;
            _isStarting = true;
            _hasErrors = false;

            try
            {
                _logger.Log("Запуск системы...");

                // 1. Загружаем настройки
                SettingsManager.Load();

                // 2. Запускаем Python HTTP-сервер (статику)
                PythonServer.Start();

                // 3. Запускаем HTTP-сервер управления
                HttpServer.Start();

                // 4. Запускаем WebSocket-сервер управления
                WebSocketServer.Start();

                // 5. Запускаем клиент телеметрии
                await TelemetryClient.ConnectAsync();

                // 6. Регистрируем хоткеи
                HotkeyManager.RegisterAll();

                _isRunning = true;
                _isStarting = false;
                OnStarted?.Invoke();
                _logger.Log("Система успешно запущена.");
            }
            catch (Exception ex)
            {
                _hasErrors = true;
                _isStarting = false;
                _logger.Error($"Ошибка при запуске системы: {ex.Message}");
                throw;
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;

            try
            {
                _logger.Log("Остановка системы...");

                // Отписываемся от событий
                WebSocketServer.OnTrailReceived -= OnTrailReceived;
                WebSocketServer.OnSoundRequest -= OnSoundRequest;
                WebSocketServer.OnMarkerRequest -= OnMarkerRequest;
                TelemetryClient.OnTelemetryUpdate -= OnTelemetryUpdate;

                // Останавливаем сервисы
                TelemetryClient.Disconnect();
                WebSocketServer.Stop();
                HttpServer.Stop();
                PythonServer.Stop();
                HotkeyManager.UnregisterAll();

                _isRunning = false;
                OnStopped?.Invoke();
                _logger.Log("Система остановлена.");
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка при остановке системы: {ex.Message}");
            }
        }

        // ===== Обработчики событий =====

        private void OnTelemetryUpdate(object sender, TelemetryData data)
        {
            TrailRecorder.Update(data);
        }

        private void OnTrailReceived(object sender, string jsonData)
        {
            TrailSaver.SaveFromJson(jsonData);
            TrackListManager.RefreshList(); // обновляем список в UI (через событие)
            NotificationManager.ShowTrailSaved();
            SoundManager.Play("success");
        }

        private void OnSoundRequest(object sender, string soundType)
        {
            SoundManager.Play(soundType);
        }

        private void OnMarkerRequest(object sender, MarkerData data)
        {
            // Здесь будет диалог добавления заметки
            // Пока просто логируем
            _logger.Log($"Запрос на добавление заметки: {data.Name} - {data.Description}");
            // В будущем: открыть MarkerDialog и отправить ответ через WebSocketServer
        }

        // ===== Публичные методы =====

        public void TriggerTrailSave()
        {
            var triggerPath = System.IO.Path.Combine(Constants.DataFolder, Constants.TriggerFile);
            try
            {
                System.IO.File.WriteAllText(triggerPath, "trigger");
                _logger.Log("Триггер сохранения трека создан.");
                NotificationManager.ShowTrailSaveTriggered();
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка создания триггера: {ex.Message}");
            }
        }

        public void StartRecording()
        {
            TrailRecorder.Start();
            _logger.Log("Запись трека начата вручную.");
        }

        public void StopRecording()
        {
            var trailData = TrailRecorder.Stop();
            if (trailData != null)
            {
                TrailSaver.Save(trailData);
                TrackListManager.RefreshList();
                NotificationManager.ShowTrailSaved();
                SoundManager.Play("success");
            }
            _logger.Log("Запись трека остановлена вручную.");
        }

        public void AddMarkerFromHotkey()
        {
            // Отправляем команду на карту через WebSocket
            WebSocketServer.SendMarkerCommand("user_marker", "Заметка", "Добавлено по хоткею");
            _logger.Log("Команда добавления заметки отправлена.");
        }

        public void ShowTestWindow()
        {
            // Открываем тестовое окно (можно реализовать позже)
            MessageBox.Show("Тестовое окно (в разработке)", "Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void RestartOverlay()
        {
            // Завершаем процессы WebOverlay и запускаем заново
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName("WebOverlay"))
                try { proc.Kill(); } catch { }
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName("pano"))
                try { proc.Kill(); } catch { }
            // Запускаем оверлей через Python-сервер
            var urlPda = "http://localhost:8082/web_pda_map.html";
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(urlPda) { UseShellExecute = true });
                _logger.Log("Overlay перезапущен.");
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка перезапуска overlay: {ex.Message}");
            }
        }

        public async void CheckUpdates()
        {
            await Updater.CheckForUpdates();
        }
    }
}