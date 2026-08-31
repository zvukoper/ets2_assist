using System.Threading;

namespace ETS2_Assist_GUI.AR
{
    /// <summary>
    /// Latest-value буфер (single writer / single reader).
    /// Writer клалает новый снимок — старый молча заменяется (очередь устаревших
    /// НЕ накапливается — требование архитектуры AR HUD v2.0: «latest pose only»).
    /// Renderer читает последнее значение без блокировок (Volatile.Read).
    /// </summary>
    public sealed class LatestBuffer<T> where T : class
    {
        private T? _value;
        private long _sequence;     // монотонный номер публикации
        private long _skipped;      // сколько публикаций «промелькнули» мимо рендера (диагностика)

        public void Publish(T value)
        {
            // Считаем, сколько снимков не успел прочитать reader c прошлого publish.
            // (Точная диагностика не критична: это метрика перегрузки.)
            if (Volatile.Read(ref _value) != null) Interlocked.Increment(ref _skipped);
            Volatile.Write(ref _value, value);
            Interlocked.Increment(ref _sequence);
        }

        /// <summary>Последний опубликованный снимок (может быть null до первой публикации).</summary>
        public T? Latest => Volatile.Read(ref _value);

        /// <summary>Сколько публикаций reader не увидел (смягчается: renderer берёт latest).</summary>
        public long Skipped => Interlocked.Read(ref _skipped);

        /// <summary>Номер последней публикации (для отладки «GameState age»).</summary>
        public long Version => Interlocked.Read(ref _sequence);

        public void Reset()
        {
            Volatile.Write(ref _value, null);
            Interlocked.Exchange(ref _sequence, 0);
            Interlocked.Exchange(ref _skipped, 0);
        }
    }
}