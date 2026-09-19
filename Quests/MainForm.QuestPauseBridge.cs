namespace ETS2_Assist_GUI
{
    public partial class MainForm
    {
        // v1.0.40.82:
        // Старый параллельный WinForms timer, который каждые 300 мс сам менял
        // QuestRuntime._paused через reflection, удалён.
        // Авторитетный pause snapshot принадлежит WebUIManager: ESC — единственный
        // триггер интерактивной паузы, REST paused — только валидатор.
    }
}
