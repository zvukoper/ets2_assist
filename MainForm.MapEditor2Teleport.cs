using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    public partial class MainForm
    {
        internal static async Task MapEditor2TeleportGameAsync(double x, double y, double z)
        {
            var main = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
            if (main == null) return;

            try
            {
                await Task.Run(() => main.RunTeleportGameSync(
                    (x, y, z, false),
                    0.0,
                    0.0,
                    0.0,
                    0.0));
            }
            catch (Exception ex)
            {
                main.BeginInvoke((Action)(() => main.AppendLog("[TELEPORT] Ошибка: " + ex.Message)));
            }
        }

        internal static void MapEditor2TeleportEditor(double x, double y, double z)
        {
            var main = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
            if (main == null) return;

            try
            {
                Task.Run(() => main.RunTeleportEditorSync(
                    x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    y.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
            }
            catch (Exception ex)
            {
                main.BeginInvoke((Action)(() => main.AppendLog("[TELEPORT-EDITOR] Ошибка: " + ex.Message)));
            }
        }
    }
}
