using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ETS2_Assist_GUI.Input
{
    /// <summary>
    /// Менеджер глобальных горячих клавиш.
    /// Регистрирует хоткеи через WinAPI и обрабатывает WM_HOTKEY.
    /// </summary>
    public class HotkeyManager
    {
        private readonly Dictionary<int, Action> _hotkeyActions = new();
        private readonly IntPtr _windowHandle;
        private bool _isRegistered = false;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_WIN = 0x0008;

        // Идентификаторы хоткеев
        public const int HOTKEY_SAVE = 9001;
        public const int HOTKEY_START_RECORD = 9002;
        public const int HOTKEY_STOP_RECORD = 9003;
        public const int HOTKEY_ADD_MARKER = 9004;
        public const int HOTKEY_TEST_WINDOW = 9005;

        /// <summary>
        /// Создаёт экземпляр менеджера хоткеев.
        /// </summary>
        /// <param name="windowHandle">Дескриптор главного окна (this.Handle).</param>
        public HotkeyManager(IntPtr windowHandle)
        {
            _windowHandle = windowHandle;
        }

        /// <summary>
        /// Регистрирует все горячие клавиши по умолчанию.
        /// </summary>
        public void RegisterAll()
        {
            if (_isRegistered) return;

            // Shift+Ctrl+S — сохранить трек
            Register(HOTKEY_SAVE, MOD_CONTROL | MOD_SHIFT, Keys.S);

            // Shift+Ctrl+R — начать запись
            Register(HOTKEY_START_RECORD, MOD_CONTROL | MOD_SHIFT, Keys.R);

            // Shift+Ctrl+X — остановить запись
            Register(HOTKEY_STOP_RECORD, MOD_CONTROL | MOD_SHIFT, Keys.X);

            // Shift+Ctrl+N — добавить заметку
            Register(HOTKEY_ADD_MARKER, MOD_CONTROL | MOD_SHIFT, Keys.N);

            // Shift+Ctrl+T — тестовое окно
            Register(HOTKEY_TEST_WINDOW, MOD_CONTROL | MOD_SHIFT, Keys.T);

            _isRegistered = true;
        }

        /// <summary>
        /// Отменяет регистрацию всех хоткеев.
        /// </summary>
        public void UnregisterAll()
        {
            if (!_isRegistered) return;

            Unregister(HOTKEY_SAVE);
            Unregister(HOTKEY_START_RECORD);
            Unregister(HOTKEY_STOP_RECORD);
            Unregister(HOTKEY_ADD_MARKER);
            Unregister(HOTKEY_TEST_WINDOW);

            _isRegistered = false;
        }

        /// <summary>
        /// Регистрирует один хоткей.
        /// </summary>
        private void Register(int id, uint modifiers, Keys key)
        {
            if (!RegisterHotKey(_windowHandle, id, modifiers, (uint)key))
            {
                throw new InvalidOperationException($"Не удалось зарегистрировать хоткей ID={id}");
            }
        }

        /// <summary>
        /// Отменяет регистрацию одного хоткея.
        /// </summary>
        private void Unregister(int id)
        {
            UnregisterHotKey(_windowHandle, id);
        }

        /// <summary>
        /// Привязывает действие к идентификатору хоткея.
        /// </summary>
        public void SetAction(int id, Action action)
        {
            if (_hotkeyActions.ContainsKey(id))
                _hotkeyActions[id] = action;
            else
                _hotkeyActions.Add(id, action);
        }

        /// <summary>
        /// Обрабатывает сообщение WM_HOTKEY.
        /// Вызывается из WndProc главной формы.
        /// </summary>
        public void ProcessHotkey(int id)
        {
            if (_hotkeyActions.TryGetValue(id, out var action))
            {
                action?.Invoke();
            }
        }

        /// <summary>
        /// Регистрирует хоткей с пользовательской комбинацией.
        /// </summary>
        public void RegisterCustom(int id, bool ctrl, bool shift, bool alt, Keys key)
        {
            uint modifiers = 0;
            if (ctrl) modifiers |= MOD_CONTROL;
            if (shift) modifiers |= MOD_SHIFT;
            if (alt) modifiers |= MOD_ALT;

            if (!RegisterHotKey(_windowHandle, id, modifiers, (uint)key))
            {
                throw new InvalidOperationException($"Не удалось зарегистрировать пользовательский хоткей ID={id}");
            }
        }

        /// <summary>
        /// Отменяет регистрацию пользовательского хоткея.
        /// </summary>
        public void UnregisterCustom(int id)
        {
            UnregisterHotKey(_windowHandle, id);
        }

        public bool IsRegistered => _isRegistered;
    }
}