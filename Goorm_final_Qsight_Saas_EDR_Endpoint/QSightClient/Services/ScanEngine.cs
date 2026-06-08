using QSightClient.Models;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using QSightClient.Services;

namespace QSightClient.Services
{
    public class ScanEngine
    {
        public event Action<string>? OnStatusChanged;
        public event Action<int>? OnProgressChanged;

        private CancellationTokenSource? _cts;

        public async Task<ScanLog?> StartScan(string path)
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                // SHA256 계산
                OnStatusChanged?.Invoke("Calculating SHA256...");
                OnProgressChanged?.Invoke(10);

                var sha256 = ComputeSha256(path);
                var fileName = Path.GetFileName(path);

                if (App.WhiteList.IsWhitelisted(sha256))
                {
                    OnStatusChanged?.Invoke("Whitelisted file (skipped)");
                    OnProgressChanged?.Invoke(100);

                    var whiteLog = new ScanLog
                    {
                        FilePath = path,
                        FileName = fileName,
                        Result = "clean",
                        StaticResult = "whitelisted",
                        Timestamp = DateTime.Now,
                        Sha256 =sha256
                    };

                    App.Logs.SaveLog(whiteLog);

                    return whiteLog;
                }

                // Scan 생성
                OnStatusChanged?.Invoke("Creating scan...");
                OnProgressChanged?.Invoke(25);

                var scanId = await App.Api.CreateScanAsync("EMP001", fileName, sha256);
                if (string.IsNullOrEmpty(scanId))
                    throw new Exception("Failed to create scan");

                // VT 분석 트리거
                OnStatusChanged?.Invoke("Triggering analysis...");
                OnProgressChanged?.Invoke(40);

                var completeOk = await App.Api.CompleteScanAsync(scanId, fileName);
                if (!completeOk)
                    throw new Exception("Failed to trigger analysis");

                // 결과 polling
                OnStatusChanged?.Invoke("Waiting for result...");
                OnProgressChanged?.Invoke(50);

                var result = await PollScanResult(scanId, token);

                var staticResult = result?.scan?.static_result ?? "unknown";

                // 완료
                OnStatusChanged?.Invoke($"Scan Complete: {staticResult}");
                OnProgressChanged?.Invoke(100);

                var resultLog = new ScanLog
                {
                    FilePath = path,
                    FileName = fileName,
                    ScanId = scanId,
                    StaticResult = staticResult,
                    Timestamp = DateTime.Now,
                    ScanTime = DateTime.Now,
                    Result = staticResult,
                    Sha256 = sha256
                };

                // 로그 저장
                App.Logs.SaveLog(resultLog);

                // 파일로 직접 확인
                File.WriteAllText(
                    @"C:\Users\Kongs\Desktop\qsight_log_test.txt",
                    $"파일:{fileName}, 결과:{staticResult}, 로그수:{App.Logs.Logs.Count}, 시간:{DateTime.Now}"
                );

                return resultLog;
            }
            catch (TaskCanceledException)
            {
                OnStatusChanged?.Invoke("Scan Cancelled");
                OnProgressChanged?.Invoke(0);

                return null;
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke($"Error: {ex.Message}");
                OnProgressChanged?.Invoke(0);

                File.WriteAllText(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop", "qsight_error.txt"),
                ex.ToString()
                );

                return null;
            }
        }

        public void CancelScan()
        {
            _cts?.Cancel();
        }

        // SHA256 계산
        private string ComputeSha256(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        // 결과 polling
        private async Task<ScanResultResponse?> PollScanResult(string scanId, CancellationToken token)
        {
            int retry = 0;
            int maxRetry = 10; // 최대 20초

            while (retry < maxRetry)
            {
                token.ThrowIfCancellationRequested();

                await Task.Delay(2000, token);
                retry++;

                var result = await App.Api.GetScanResultAsync(scanId);

                if (result?.scan == null)
                    continue;

                var status = result.scan.status;

                OnStatusChanged?.Invoke($"Analyzing... ({status})");
                OnProgressChanged?.Invoke(50 + (retry * 5)); // 50~100 점진 증가

                if (status == "completed")
                    return result;
            }

            return null; // 타임아웃
        }
    }
}