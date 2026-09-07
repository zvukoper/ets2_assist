using System.Threading.Tasks;

namespace ETS2_Assist_GUI
{
    internal sealed partial class MapEditor2Form
    {
        // Map Editor 2 owns the diagnostic grid inside its actual map viewport.
        // Keep this hook for compatibility with the existing initialization flow.
        private static Task InstallDebugGridAsync() => Task.CompletedTask;
    }
}
