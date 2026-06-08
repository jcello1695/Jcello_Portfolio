using QSightClient.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace QSightClient.Services
{
    public class IPCService
    {
        public event Action<IPCMessage>? OnMessageReceived;

        public async Task StartListening()
        {
            while (true)
            {
                var server = new NamedPipeServerStream(
                            "QSightPipe",
                            PipeDirection.In,
                            NamedPipeServerStream.MaxAllowedServerInstances,
                            PipeTransmissionMode.Message,
                            PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync();

                _ = HandleClient(server);
            }
        }

        private async Task HandleClient(NamedPipeServerStream server)
        {
            using (server)
            {
                try
                {
                    using var reader = new StreamReader(server, Encoding.UTF8); 

                    var json = await reader.ReadLineAsync();

                    Debug.WriteLine($"RAW JSON: {json}");

                    if (string.IsNullOrEmpty(json))
                        return;

                    var msg = JsonSerializer.Deserialize<IPCMessage>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                    if (msg != null)
                    {
                        Debug.WriteLine($"MESSAGE RECEIVED: {msg.Command}");
                        OnMessageReceived?.Invoke(msg);
                        Debug.WriteLine("[IPC] Event Invoked!");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("IPC ERROR:");
                    Debug.WriteLine(ex.ToString());
                }
            }
        }
    }
}