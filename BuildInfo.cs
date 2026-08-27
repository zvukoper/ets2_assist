using System.Reflection;

namespace ETS2_Assist_GUI
{
    internal static class BuildInfo
    {
        // Строка версии берётся из AssemblyInformationalVersion (формируется в csproj
        // как A.B.CCCC-DESC-YYYY.MM.DD-HHmm), поэтому не дублируется вручную и всегда
        // синхронизирована с EXE. Запасной литерал — на случай недоступности атрибута.
        public static string Version
        {
            get
            {
                try
                {
                    var attr = System.Reflection.Assembly.GetExecutingAssembly()
                        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>();
                    if (attr != null && !string.IsNullOrWhiteSpace(attr.InformationalVersion))
                        return attr.InformationalVersion;
                }
                catch { }
                return "1.0.38-TARGETS-FILE";
            }
        }
    }
}
