using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.IO;

namespace QSightClient.Services
{
    public class ApiService
    {
        private readonly HttpClient _http;
        private const string BaseUrl = "http://43.202.10.230:8000";

        public ApiService()
        {
            _http = new HttpClient
            {
                BaseAddress = new Uri(BaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        // 서버 연결 확인
        public async Task<bool> HealthCheckAsync()
        {
            try
            {
                var response = await _http.GetAsync("/health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        // 스캔 생성
        public async Task<string?> CreateScanAsync(string employeeId, string fileName, string sha256)
        {
            try
            {
                var body = new
                {
                    employee_id = employeeId,
                    file_name = fileName,
                    sha256 = sha256,
                    source_type = "watcher",
                    static_result = "unknown",
                    severity = "low",
                    status = "queued"
                };

                var response = await _http.PostAsJsonAsync("/scans", body);
                if (!response.IsSuccessStatusCode) return null;

                var result = await response.Content.ReadFromJsonAsync<ScanCreateResponse>();
                return result?.scan?.scan_id;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> CompleteScanAsync(string scanId, string fileName)
        {
            try
            {
                var payload = new
                {
                    file_name = fileName,
                    object_key = $"uploads/{scanId}/{fileName}"
                };
                var response = await _http.PostAsJsonAsync($"/scans/{scanId}/uploads/complete", payload);

                var status = response.StatusCode.ToString();
                var body = await response.Content.ReadAsStringAsync();
                System.IO.File.WriteAllText(@"C:\Users\Kongs\Desktop\complete_result.txt",
                    $"Status: {status}\nBody: {body}\nScanId: {scanId}");

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText(@"C:\Users\Kongs\Desktop\complete_error.txt", ex.ToString());
                return false;
            }
        }

        // 대시보드 데이터 조회
        public async Task<DashboardSummary?> GetDashboardSummaryAsync(int days = 30)
        {
            try
            {
                var response = await _http.GetAsync($"/dashboard/summary?days={days}");
                if (!response.IsSuccessStatusCode) return null;
                return await response.Content.ReadFromJsonAsync<DashboardSummary>();
            }
            catch
            {
                return null;
            }
        }

        // 스캔 결과 조회
        public async Task<ScanResultResponse?> GetScanResultAsync(string scanId)
        {
            try
            {
                var response = await _http.GetAsync($"/scans/{scanId}");
                if (!response.IsSuccessStatusCode) return null;
                return await response.Content.ReadFromJsonAsync<ScanResultResponse>();
            }
            catch
            {
                return null;
            }
        }
    }

    // 응답 모델
    public class ScanCreateResponse
    {
        public bool ok { get; set; }
        public ScanData? scan { get; set; }
    }

    public class ScanData
    {
        public string? scan_id { get; set; }
        public string? employee_id { get; set; }
        public string? status { get; set; }
        public string? severity { get; set; }
        public string? static_result { get; set; }
    }

    public class DashboardSummary
    {
        public bool ok { get; set; }
        public int days { get; set; }
        public SummaryData? summary { get; set; }
    }

    public class SummaryData
    {
        public int total_scans { get; set; }
        public int severity_high_count { get; set; }
        public int severity_critical_count { get; set; }
        public double avg_dynamic_score { get; set; }
    }

    public class ScanResultResponse
    {
        public bool ok { get; set; }
        public ScanData? scan { get; set; }
    }
}