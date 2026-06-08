using System.IO.Pipes;
using System.Text;
using System.Text.Json;

using var client = new NamedPipeClientStream(".", "QSightPipe", PipeDirection.Out);

client.Connect();

using var writer = new StreamWriter(client, Encoding.UTF8)
{
    AutoFlush = true
};

var msg = new
{
    Command = "SCAN",
    Path = "C:\\hello.exe"
};

var json = JsonSerializer.Serialize(msg);

writer.WriteLine(json);