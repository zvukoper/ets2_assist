# ETS2 Assist — архитектура высокопроизводительного AR HUD

## Цель

Переделать текущую реализацию AR HUD так, чтобы метка максимально плавно следовала за движением головы, имела минимальную задержку и не зависела от производительности JavaScript/WebView2.

Главные требования:

- максимально плавное движение метки;
- минимальная motion-to-photon latency;
- корректная работа на мониторе 180 Hz;
- отсутствие визуального рывка при движении головы;
- минимальное использование CPU;
- использование GPU для отрисовки;
- основное приложение остаётся на C#/.NET;
- WebSocket пока обязательно сохраняется для получения данных, поступающих из ETS2;
- Python для renderer не использовать.

---

# Основное архитектурное решение

Текущий WebView2/HTML/Canvas renderer заменить на **нативный C# renderer на Direct3D 11**.

Рекомендуемый стек:

- C# / .NET 10;
- Vortice.Windows;
- Direct3D 11;
- DXGI;
- DirectComposition либо современный Windows Composition API;
- прозрачный overlay поверх ETS2;
- WebSocket оставить только для получения игровых данных.

Direct3D 11 выбран вместо D3D12 не потому, что D3D12 быстрее в данном случае, а потому что для небольшого HUD D3D11 значительно проще, а необходимых возможностей для данного renderer более чем достаточно.

DXGI flip model следует использовать для swap chain. Microsoft рекомендует flip model для новых Direct3D приложений, а waitable swap chain позволяет уменьшать задержку отображения. 

---

# Новая схема

```text
                    ETS2
                     │
                     │ telemetry
                     ▼
                WebSocket
                     │
                     ▼
              C# Game Data
                     │
                     │
                     ├───────────────┐
                     │               │
                     ▼               │
                HUD State            │
                                     │
Head Tracker ────────► LatestPose ───┘
                            │
                            ▼
                    Render Thread
                            │
                            ▼
                       Direct3D 11
                            │
                            ▼
                         DXGI
                            │
                            ▼
                  Transparent Overlay
                            │
                            ▼
                         Display
```

Критически важно:

**WebSocket НЕ должен управлять частотой рендеринга.**

Получение нового сообщения WebSocket не должно означать "нарисовать кадр".

Renderer должен работать независимо.

---

# Разделение потоков

Не смешивать получение данных, вычисление положения метки и отрисовку.

Предпочтительная схема:

```text
Thread 1:
WebSocket / ETS2 telemetry
        │
        ▼
 GameState


Thread 2:
Head tracking
        │
        ▼
 LatestPoseBuffer


Thread 3:
AR Renderer
        │
        ▼
 Direct3D 11
```

Renderer всегда берёт **самое свежее доступное состояние**.

Не допускается очередь старых pose.

Нельзя делать:

```text
pose1 → render
pose2 → render
pose3 → render
```

если pose2 и pose3 уже устарели.

Нужно:

```text
latest pose → render
```

То есть старые значения могут безболезненно заменяться новыми.

---

# LatestPoseBuffer

Создать отдельное состояние для положения головы.

Минимально оно должно содержать:

```csharp
struct HeadPose
{
    double Timestamp;

    float Yaw;
    float Pitch;
    float Roll;

    float YawVelocity;
    float PitchVelocity;
    float RollVelocity;
}
```

Реальная структура может быть адаптирована под существующий tracker.

Основной принцип:

- writer записывает новую pose;
- renderer читает последнюю pose;
- renderer не блокирует tracker;
- tracker не блокирует renderer;
- mutex/lock на каждом кадре не использовать.

Предпочтительно использовать lock-free/single-writer подход.

Для простой структуры данных допустим double-buffer / sequence-number buffer.

---

# Prediction

Необходимо учитывать задержку всего pipeline.

Renderer не должен просто использовать:

```text
pose(t)
```

а должен иметь возможность использовать прогноз:

```text
pose(t + predictionTime)
```

Например:

```text
predictedYaw =
    yaw + yawVelocity * predictionTime

predictedPitch =
    pitch + pitchVelocity * predictionTime

predictedRoll =
    roll + rollVelocity * predictionTime
```

Prediction time не задавать произвольно большим.

Он должен быть параметром renderer и подбираться экспериментально.

Главная цель prediction — компенсировать задержку между измерением положения головы и фактическим появлением пикселя на экране.

---

# WebSocket

WebSocket сохранить.

Он используется для:

- получения состояния ETS2;
- координат/направлений объектов;
- команд;
- telemetry;
- `ar_target`;
- других данных игры, которые уже используются существующим HUD.

Но WebSocket не должен использоваться как frame clock.

Не делать:

```javascript
onWebSocketMessage()
{
    update();
    render();
}
```

И не переносить такую логику непосредственно в C# renderer.

Правильно:

```text
WebSocket message
       ↓
update GameState
       ↓
renderer в следующем кадре
       ↓
берёт latest GameState
```

При этом renderer может отрисовать несколько кадров между двумя WebSocket сообщениями.

---

# Частота рендеринга

Renderer должен быть независим от частоты WebSocket и желательно работать с максимальной доступной частотой дисплея.

Для монитора 180 Hz ориентир:

```text
180 Hz
≈ 5.56 ms/frame
```

Но не следует искусственно делать:

```text
Task.Delay(5)
Thread.Sleep(5)
Timer(5 ms)
```

Это приведёт к дополнительному jitter.

Синхронизацию следует строить вокруг графического presentation pipeline.

---

# Swap Chain

Использовать DXGI flip model.

Предпочтительно:

```text
DXGI_SWAP_EFFECT_FLIP_DISCARD
```

либо совместимый flip-вариант, если выбранная схема composition требует другой конфигурации.

Использовать минимально необходимое количество back buffers.

Для уменьшения очереди кадров включить:

```text
DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT
```

и использовать:

```text
IDXGISwapChain2::SetMaximumFrameLatency(1)
```

В renderer необходимо ждать сигнал waitable swap chain перед началом формирования следующего кадра.

Это позволяет не накапливать несколько кадров в очереди и уменьшает input-to-display latency.

Microsoft прямо описывает waitable swap chain как механизм снижения effective frame latency.

Flip model также уменьшает лишние копирования и является рекомендуемой моделью presentation для современных Direct3D приложений.

---

# Прозрачный overlay

Создать отдельное прозрачное окно поверх ETS2.

Требования к окну:

```text
borderless
transparent
click-through
no activation
no cursor
```

Overlay не должен перехватывать управление мышью/клавиатурой ETS2.

Размер overlay должен соответствовать нужному display/output.

При необходимости overlay должен автоматически отслеживать:

- изменение разрешения;
- смену монитора;
- изменение положения окна ETS2;
- fullscreen/windowed/borderless режим.

Не использовать HTML/CSS для самого AR HUD.

---

# Direct3D renderer

Renderer должен непосредственно рисовать GPU geometry.

Не использовать Canvas.

Не создавать bitmap изображения для каждого кадра.

Не перерисовывать текст через браузер.

Основные primitives:

```text
triangle
quad
line
circle
ring
sprite
text glyph
```

Все динамические позиции менять через GPU buffers / constant buffers.

Для нескольких AR-элементов использовать batching.

Например:

```text
1 draw call:
all simple markers

1 draw call:
all lines

1 draw call:
all sprites
```

а не отдельный draw call на каждый объект, насколько это практически возможно.

---

# Текст

Если HUD содержит текст, не использовать системный текстовый renderer каждый кадр.

Использовать заранее созданный texture atlas:

```text
glyph atlas
     ↓
UV coordinates
     ↓
quad
```

Текст должен быть GPU-rendered.

Для динамического текста обновлять только необходимые данные.

---

# Анимация

Не использовать browser animation:

```text
requestAnimationFrame()
```

Не использовать:

```text
CSS transition
CSS transform
setInterval
setTimeout
```

Положение AR-объектов рассчитывается непосредственно перед каждым GPU frame.

---

# Главное правило обновления AR-позиции

На каждом frame:

```text
1. Получить текущее время.
2. Получить последнюю HeadPose.
3. Выполнить prediction.
4. Получить актуальный GameState.
5. Рассчитать положение AR marker.
6. Записать transforms в GPU buffers.
7. Render.
8. Present.
```

Не делать тяжёлых операций между пунктами 2 и 8.

---

# Интерполяция

Для данных ETS2, которые обновляются реже renderer, разрешается интерполировать состояние.

Но положение головы интерполировать назад нельзя, поскольку это увеличивает задержку.

Для head pose приоритет:

```text
latest pose
+
velocity prediction
```

Для игровых объектов, поступающих через WebSocket:

```text
latest state
или
interpolation между двумя известными state
```

Выбор зависит от конкретной логики объекта.

---

# Существующий JS

Текущий HTML содержит:

```html
<canvas id="arCanvas"></canvas>
```

и загружает:

```html
<script src="js/ar_hud.js"></script>
```

HTML сам по себе не должен использоваться в новой системе как renderer. 
Существующий JS следует сохранить временно как reference implementation.

Не удалять его до полного сравнения поведения новой реализации.

Сначала добиться визуального соответствия:

```text
target coordinates
marker position
marker size
marker visibility
colors
labels
```

После этого старый WebView/Canvas renderer можно вывести из production path.

---

# Не использовать Python

Python в renderer не нужен.

Не создавать:

```text
Python renderer
OpenCV renderer
pygame
Qt canvas
Tkinter
```

Всё, что связано с визуальным выводом, должно быть C# + Direct3D.

Python допустим только для отдельных offline/dev tools, но не для realtime AR pipeline.

---

# Архитектура проекта

Предпочтительно выделить отдельный модуль:

```text
ETS2_Assist_GUI
│
├── AR
│   ├── ArRenderer.cs
│   ├── ArOverlayWindow.cs
│   ├── ArRenderLoop.cs
│   ├── ArGameState.cs
│   ├── HeadPose.cs
│   ├── LatestPoseBuffer.cs
│   ├── PosePredictor.cs
│   ├── MarkerRenderer.cs
│   ├── TextRenderer.cs
│   └── Shaders
│       ├── marker.hlsl
│       └── text.hlsl
│
├── Networking
│   └── существующая WebSocket логика
│
└── ...
```

При необходимости названия адаптировать под существующую структуру проекта.

---

# Поток данных

Итоговый pipeline должен выглядеть так:

```text
                         ETS2
                          │
                          ▼
                    WebSocket 8084
                          │
                          ▼
                   GameState cache
                          │
                          │
                          ▼
Head Tracker ─────► LatestPoseBuffer
                          │
                          ▼
                  AR Render Thread
                          │
                 latest pose + prediction
                          │
                          ▼
                    marker math
                          │
                          ▼
                     D3D11 GPU
                          │
                          ▼
                       DXGI
                          │
                          ▼
                  Transparent Overlay
                          │
                          ▼
                        180 Hz
```

---

# Что запрещено

Не делать:

```text
WebSocket → render directly
```

Не делать:

```text
WebSocket message → UI thread → canvas
```

Не делать:

```text
head tracking → WebSocket → renderer
```

если head tracker уже находится в том же процессе.

Не делать:

```text
Thread.Sleep()
Task.Delay()
Timer
```

для имитации частоты кадров.

Не делать:

```text
Canvas 2D
DOM transforms
CSS animation
requestAnimationFrame
WebView2
```

для production renderer.

Не держать очередь устаревших HeadPose.

Не блокировать render thread ожиданием WebSocket.

Не блокировать head-tracking thread ожиданием render thread.

---

# Приоритеты оптимизации

Оптимизировать в следующем порядке:

1. Минимальная задержка получения HeadPose.
2. Latest-pose architecture.
3. Prediction.
4. Render непосредственно перед Present.
5. Waitable swap chain / минимальная очередь кадров.
6. GPU rendering.
7. Минимум CPU работы на frame.
8. Batching draw calls.
9. Оптимизация текстового renderer.
10. Остальные микрооптимизации.

Не тратить время на микрооптимизацию шейдеров, пока не устранены архитектурные задержки.

---

# Диагностика

В новой реализации добавить debug telemetry:

```text
Render FPS
Render frame time
HeadPose update rate
WebSocket update rate
GameState age
HeadPose age
Prediction time
Estimated render latency
Dropped/overwritten pose count
```

Особенно важны:

```text
HeadPose age
```

и

```text
GameState age
```

Например:

```text
HeadPose age: 1.8 ms
GameState age: 8.4 ms
Render: 180 FPS
Frame: 5.3 ms
Prediction: 6.0 ms
```

Это позволит объективно искать задержку, а не оценивать её только визуально.

---

# Критерий готовности

Новая реализация считается успешной, если:

1. AR marker визуально следует за движением головы без заметного рывка.
2. Задержка значительно меньше текущей JS/Canvas реализации.
3. WebSocket остаётся источником игровых данных.
4. WebSocket не задаёт cadence renderer.
5. Renderer работает независимо от частоты WebSocket.
6. При увеличении FPS монитора движение становится плавнее.
7. При отсутствии новых WebSocket сообщений HUD продолжает плавно обновляться.
8. При быстром движении головы метка не начинает заметно "догонять" голову.
9. CPU нагрузка renderer минимальна.
10. Никаких Python/WebView2/Canvas компонентов в realtime render path.

---

# Итоговое решение

**Production renderer:**

```text
C#
+
Vortice.Windows
+
Direct3D 11
+
DXGI Flip Model
+
Waitable Swap Chain
+
Transparent Native Overlay
+
GPU rendering
```

**WebSocket:**

```text
оставить
```

но только как источник данных ETS2.

**Head tracking:**

```text
LatestPoseBuffer
+
prediction
```

**Renderer:**

```text
отдельный render thread
+
latest state
+
GPU
+
minimal frame queue
```

**Python:**

```text
не использовать
```

Основная цель реализации — не просто получить высокий FPS, а минимизировать весь путь:

```text
Head movement
    ↓
HeadPose
    ↓
Prediction
    ↓
GPU frame
    ↓
Present
    ↓
Pixel on display
```

Именно этот pipeline должен определять архитектуру.