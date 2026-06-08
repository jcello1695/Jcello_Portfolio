using SharpShell.Attributes;
using SharpShell.SharpContextMenu;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Text.Json;

[ComVisible(true)]
[COMServerAssociation(AssociationType.AllFiles)]
public class QSightContextMenu : SharpContextMenu
{
    protected override bool CanShowMenu()
    {
        return true;
    }

    protected override ContextMenuStrip CreateMenu()
    {
        var menu = new ContextMenuStrip();

        var item = new ToolStripMenuItem("Q-Sight 분석");

        item.Click += (sender, args) =>
        {
            SendScanCommand();
        };

        menu.Items.Add(item);

        return menu;
    }

    private void SendScanCommand()
    {
        using var pipe = new NamedPipeClientStream(".", "QSightPipe", PipeDirection.Out);
        pipe.Connect(2000);

        using var writer = new StreamWriter(pipe, Encoding.UTF8);

        var msg = new
        {
            Command = "SCAN",
            Path = SelectedItemPaths.First()
        };

        var json = JsonSerializer.Serialize(msg);

        writer.WriteLine(json);
        writer.Flush();
    }
}