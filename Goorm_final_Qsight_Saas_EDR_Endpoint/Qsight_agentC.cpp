#include <windows.h>
#include <tlhelp32.h>
#include <iphlpapi.h>
#include <iostream>
#include <vector>
#include <string>
#include <sstream>
#include <chrono>
#include <iomanip>
#include <nlohmann/json.hpp>

#pragma comment(lib, "iphlpapi.lib")
#pragma comment(lib, "ws2_32.lib")

using json = nlohmann::json;

//1. behavior 스키마에 맞는 구조체
struct process_events {
    std::string timestamp;
    DWORD pid;
    DWORD ppid;
    std::string action;
    std::string path;
    std::string severity;
    };

struct file_events {
    std::string timestamp;
    std::string action;
    std::string path;
    DWORD pid;
    std::string severity;
    };

struct registry_events {
    std::string timestamp;
    std::string action;
    std::string key;
    DWORD pid;
    std::string severity;
    };

struct network_events {
    std::string timestamp;
    std::string protocol;
    std::string dst_ip;
    int dst_port;
    DWORD pid;
    std::string severity;
    };


//2. Q매니저 - 데이터를 쌓고 관리하는 클래스
class QManager {
private:
    std::string GetISOTimestamp() {
        auto now = std::chrono::system_clock::now();
        auto in_time_t = std::chrono::system_clock::to_time_t(now);
        std::tm gmt;
        gmtime_s(&gmt, &in_time_t); // thread-safe version
        std::stringstream ss;
        ss << std::put_time(&gmt, "%Y-%m-%dT%H:%M:%SZ");
        return ss.str();
    }

public:
    std::vector<process_events> proc_list;
    std::vector<file_events> file_list;
    std::vector<registry_events> reg_list;
    std::vector<network_events> net_list;

    void AddProcess(DWORD pid, DWORD ppid, std::string path, std::string action, std::string sev) {
        proc_list.push_back({ GetISOTimestamp(), pid, ppid, action, path, sev });
    }
    void AddFile(DWORD pid, std::string action, std::string path, std::string sev) {
        file_list.push_back({ GetISOTimestamp(), action, path, pid, sev });
    }
    void AddRegistry(DWORD pid, std::string action, std::string key, std::string sev) {
        reg_list.push_back({ GetISOTimestamp(), action, key, pid, sev });
    }
    void AddNetwork(DWORD pid, std::string proto, std::string ip, int port, std::string sev) {
        net_list.push_back({ GetISOTimestamp(), proto, ip, port, pid, sev });
    }
};


//3. Q에이전트 - 실제로 위협행위를 추적하는 클래스
class QAgent {
private:
    QManager& manager;

public:
    QAgent(QManager& m) : manager(m), is_running(false) {}

    //비동기 추적 시작
    void Start(std::wstring watch_path) {
        is_running = true;
        workers.push_back(std::thread(&QAgent::TraceProcessLoop, this));
        workers.push_back(std::thread(&QAgent::TraceFileLoop, this, watch_path));
        workers.push_back(std::thread(&QAgent::TraceRegistryLoop, this));
        workers.push_back(std::thread(&QAgent::TraceNetworkLoop, this));
    }

    //비동기 추적 정지
    void Stop() {
        is_running = false;
        for (auto& t : workers) {
            if (t.joinable()) t.detach(); // 실제 구현 시에는 적절한 Signal로 종료 유도 권장
        }
    }

    //3-ㄱ. 프로세스 추적. 자녀프로세스를 생성하는지? 쉘을 실행하는지?
    void TraceProcess() {
        while (is_running) {
            HANDLE hSnap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            PROCESSENTRY32 pe = { sizeof(pe) };
            if (Process32First(hSnap, &pe)) {
                do {
                    manager.AddProcess(pe.th32ProcessID, pe.th32ParentProcessID, std::string(pe.szExeFile), "execute", "의심");
                } while (Process32Next(hSnap, &pe));
            }
            CloseHandle(hSnap);
            std::this_thread::sleep_for(std::chrono::seconds(5));
        }
    }

    //3-ㄴ. 파일추적. 별도의 파일을 생성하는지? 혹은 기존 파일을 변조하는지?
    void TraceFile(std::wstring watch_path) {
        HANDLE hDir = CreateFileW(watch_path.c_str(), FILE_LIST_DIRECTORY,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, NULL);

        char buf[1024];
        DWORD returned;

        while (is_running) {
            if (ReadDirectoryChangesW(hDir, buf, sizeof(buf), TRUE, FILE_NOTIFY_CHANGE_FILE_NAME | FILE_NOTIFY_CHANGE_LAST_WRITE, &returned, NULL, NULL)) {
                manager.AddFile(GetCurrentProcessId(), "modify", "C:\\Detected\\Target.exe", "위험");
            }
        }
        CloseHandle(hDir);
    }

    //3-ㄷ. 레지스트리 추적. 레지스트리의 경로나 레지스트리의 값을 변경하는지?
    void TraceRegistry() {
        HKEY hKey;
        if (RegOpenKeyEx(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Run", 0, KEY_NOTIFY, &hKey) == ERROR_SUCCESS) {
            while (is_running) {
                if (RegNotifyChangeKeyValue(hKey, TRUE, REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET, NULL, FALSE) == ERROR_SUCCESS) {
                    manager.AddRegistry(GetCurrentProcessId(), "set", "HKCU\\..\\Run", "위험");
                }
            }
            RegCloseKey(hKey);
        }
    }

    //3-ㄹ. 네트워크추적. 외부 서버와 별도의 통신을 하지는 않는지??
    void TraceNetwork() {
        while (is_running) {
            DWORD size = 0;
            GetExtendedTcpTable(NULL, &size, TRUE, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
            PMIB_TCPTABLE_OWNER_PID pTable = (MIB_TCPTABLE_OWNER_PID*)malloc(size);
            if (GetExtendedTcpTable(pTable, &size, TRUE, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) == NO_ERROR) {
                for (DWORD i = 0; i < pTable->dwNumEntries; i++) {
                    if (pTable->table[i].dwRemoteAddr != 0 && pTable->table[i].dwRemoteAddr != 0x0100007f) {
                        struct in_addr addr;
                        addr.S_un.S_addr = pTable->table[i].dwRemoteAddr;
                        manager.AddNetwork(pTable->table[i].dwOwningPid, "TCP", inet_ntoa(addr), ntohs((u_short)pTable->table[i].dwRemotePort), "위험");
                    }
                }
            }
            free(pTable);
            std::this_thread::sleep_for(std::chrono::seconds(3));
        }
    }
};


//4. Q리포트 - 스키마에 맞게 리포트 작성
class QReport {
public:
    json Create(QManager& mgr, std::string f_name, std::string f_hash) {
        json r;
        r["analysis_id"] = "uuid-" + std::to_string(std::time(0));
        r["meta"] = { {"file_name", f_name}, {"sha256", f_hash}, {"size", 204800}, {"download_time", "2026-02-28T07:15:00Z"}, {"analysis_time_sec", 120} };
        r["environment"] = { {"vm_os", "Windows 10 x64"}, {"vm_id", "vm-01"} };

        r["behavior"]["process_events"] = json::array();
        for (const auto& e : mgr.proc_list) r["behavior"]["process_events"].push_back({ {"timestamp", e.timestamp}, {"pid", e.pid}, {"ppid", e.ppid}, {"action", e.action}, {"path", e.path}, {"severity", e.severity} });

        r["behavior"]["file_events"] = json::array();
        for (const auto& e : mgr.file_list) r["behavior"]["file_events"].push_back({ {"timestamp", e.timestamp}, {"action", e.action}, {"path", e.path}, {"pid", e.pid}, {"severity", e.severity} });

        r["behavior"]["registry_events"] = json::array();
        for (const auto& e : mgr.reg_list) r["behavior"]["registry_events"].push_back({ {"timestamp", e.timestamp}, {"action", e.action}, {"key", e.key}, {"pid", e.pid}, {"severity", e.severity} });

        r["behavior"]["network_events"] = json::array();
        for (const auto& e : mgr.net_list) r["behavior"]["network_events"].push_back({ {"timestamp", e.timestamp}, {"protocol", e.protocol}, {"dst_ip", e.dst_ip}, {"dst_port", e.dst_port}, {"pid", e.pid}, {"severity", e.severity} });

        r["detection"] = { {"verdict", "malicious"}, {"score", 82}, {"attack_type", {"ransomware"}}, {"matched_rules", {"RUN_KEY_PERSISTENCE", "C2_CONNECTION"}} };
        r["user_security_profile"] = { {"total_downloaded", 42}, {"scan_count", 40}, {"policy_violation_score", 12} };
        return r;
    }
};