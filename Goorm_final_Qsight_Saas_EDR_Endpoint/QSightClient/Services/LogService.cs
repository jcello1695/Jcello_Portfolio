using QSightClient.Models;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace QSightClient.Services
{
    public class LogService
    {
        private readonly string _logDir = Path.Combine
        (
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Desktop", "QSightLogs"
        );

        public ObservableCollection<ScanLog> Logs { get; } = new();

        public LogService()
        {
            Directory.CreateDirectory(_logDir);
        }

        public void SaveLog(ScanLog log)
        {
            // 메모리에 추가
            Logs.Insert(0, log);

            // 파일에도 저장
            var file = Path.Combine(_logDir, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}.json");
            var json = JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(file, json);
        }

        public void LoadFromDisk()
        {
            if (!Directory.Exists(_logDir)) return;
            Logs.Clear();
            foreach (var file in Directory.GetFiles(_logDir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var log = JsonSerializer.Deserialize<ScanLog>(json);
                    if (log != null) Logs.Add(log);
                }
                catch { }
            }
        }
    }
}