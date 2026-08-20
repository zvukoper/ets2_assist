using System;
using System.Collections.Generic;

namespace ETS2_Assist_GUI.Core
{
    /// <summary>
    /// Простой Service Locator для доступа к сервисам из любой точки приложения.
    /// Используется как альтернатива DI-контейнеру.
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> _services = new();

        /// <summary>
        /// Регистрирует сервис в контейнере.
        /// </summary>
        /// <typeparam name="T">Тип сервиса (интерфейс или класс).</typeparam>
        /// <param name="instance">Экземпляр сервиса.</param>
        public static void Register<T>(T instance)
        {
            var type = typeof(T);
            if (_services.ContainsKey(type))
                _services[type] = instance;
            else
                _services.Add(type, instance);
        }

        /// <summary>
        /// Получает зарегистрированный сервис.
        /// </summary>
        /// <typeparam name="T">Тип сервиса.</typeparam>
        /// <returns>Экземпляр сервиса.</returns>
        /// <exception cref="InvalidOperationException">Если сервис не зарегистрирован.</exception>
        public static T Get<T>()
        {
            var type = typeof(T);
            if (_services.TryGetValue(type, out var service))
                return (T)service;

            throw new InvalidOperationException($"Сервис типа {type.Name} не зарегистрирован.");
        }

        /// <summary>
        /// Проверяет, зарегистрирован ли сервис.
        /// </summary>
        public static bool IsRegistered<T>()
        {
            return _services.ContainsKey(typeof(T));
        }

        /// <summary>
        /// Удаляет все зарегистрированные сервисы.
        /// </summary>
        public static void Clear()
        {
            _services.Clear();
        }
    }
}