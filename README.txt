ETS2 Assist — vector map renderer v8

Заменяет ТОЛЬКО:
  MapEditorVectorRenderer.cs
  MapEditorForm.VectorRender.cs

MapEditorForm.cs заменять НЕ нужно.
Directory.Build.props должен содержать <UseWPF>true</UseWPF>; текущий файл проекта в репозитории это делает через добавленный Directory.Build.props.

Архитектура:
- _mapPanel.Paint старого GDI+ renderer отключается после создания WPF surface;
- существующий InvalidateMap()/RequestRender() подключён к renderer через _mapPanel.Invalidated;
- pan/zoom меняют только camera transform retained-mode world visuals;
- roads geometry строится один раз; при zoom меняется только stroke command (геометрия не разбирается заново);
- обычные точки/подписи записываются один раз;
- selection — отдельный screen-space visual, обновляется только при смене selection/режима only-selected и при camera change для нового положения;
- truck/cone/create marker — отдельный screen-space visual и не затрагивает static scene;
- нет bitmap всей карты;
- глобальный Application.Idle hook снимается после подключения и восстанавливается после закрытия редактора.

Проверка на Windows:
  dotnet clean
  dotnet build

Ожидаем: 0 ошибок. Предупреждения существующего проекта могут остаться.
