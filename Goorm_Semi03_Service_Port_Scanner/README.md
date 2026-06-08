# 가시성 확보를 위한 비동기 서비스 포트 스캐너 & 취약점 진단 시스템 (Semi-Project 3차)

> **담당 파트:** 고속 비동기 스캐너 모듈 구현 일체 및 네트워크 인프라 취약점 진단 파이프라인 구축 (`deepguard_portscanner.py`)

네트워크 인프라의 최전방 공격 표면을 식별하기 위해 열린 포트 탐색, 구동 서비스 배너 파싱, Nuclei CVE 매핑, Shodan/VirusTotal OSINT 조회, EPSS 공격 발생 확률 연동, Wappalyzer 웹 기술스택 탐지, Gemini AI 보안 권고안 생성까지 하나의 파이프라인으로 통합한 **인프라 자산 풀스택 진단 시스템**입니다.

---

## 시스템 아키텍처

```
[진단 대상 IP 입력]
          │
          ▼
[DeepguardController.main_controller()]
  자동 포트 탐색 (Nmap --top-ports 65535)
          │
          ▼
[process_target_port() - 열린 포트별 병렬 실행]
          │
    ┌─────┴──────────────────────────────────┐
    │                                        │
    ▼                                        ▼
[PortScanner]                     [EvidenceCollector]
 scan_syn() - Nmap SYN 스캔        start_packet_capture()
 scan_tcp_connect() - 보조 폴백    Scapy AsyncSniffer
          │                                  │
          ▼                                  │
[ServiceIdentifier]                          │
 port_identification()                       │
 ├─ Nmap -sV 배너 수집                       │
 └─ 실패 시 psutil 커널 역추적               │
          │                                  │
          ▼                                  │
[SecurityAnalyzer] (4개 분석 API 병렬)       │
 ├─ port_vulnerability() : Nuclei CVE 매핑  │
 ├─ match_shodan()        : Shodan OSINT    │
 ├─ match_virustotal()    : VT IP 평판      │
 └─ get_epss_score()      : 공격 발생 확률  │
          │                                  │
          ▼                                  ▼
[SolutionGenerator]              stop_packet_capture()
 ai_remediation()                PCAP 자동 저장
 Gemini AI 보안 권고안 생성
          │
          ▼
[ReportSchema.json_result()]
 Risk Score 산출 (CVSS × 0.6 + EPSS × 0.4)
 최종 JSON 리포트 반환
```

---

## 클래스 구조 및 핵심 구현 명세

### 1. PortScanner — 비동기 소켓 기반 고속 포트 탐색

**메인 스캔: Nmap SYN 스캔 (`scan_syn`)**
`python-nmap`의 `-sS -Pn --host-timeout 2s` 옵션으로 SYN 스캔을 실행합니다. 방화벽 정책을 우회하면서 TCP 핸드셰이크를 완성하지 않아 로그 잔존을 최소화하는 정밀 스캔 방식입니다.

**보조 스캔: asyncio TCP Connect (`scan_tcp_connect`)**
SYN 스캔이 실패(`error`)할 경우 `asyncio.open_connection()`으로 즉시 전환합니다. `timeout=1.0`초로 포트별 대기 시간을 바인딩하여 소켓 자원 유실을 방지하고 스캔 효율을 극대화했습니다.

---

### 2. ServiceIdentifier — 하이브리드 서비스 역추적 엔진

**Nmap 서비스 식별 (`port_identification`)**
`-sV` 옵션으로 배너 정보(`product`, `version`, `extrainfo`)를 수집합니다.

**psutil 커널 프로세스 백트래킹 (`get_local_process_info`)**
Nmap이 서비스를 식별하지 못하는 경우(`unknown`) 로컬 환경에 한해 `psutil.net_connections(kind='inet')`으로 시스템 커널의 네트워크 커넥션을 직접 역추적합니다. 해당 포트를 점유 중인 **실제 프로세스 PID, 실행 파일 경로(`exe`), 서비스명**을 파싱해내는 하이브리드 엔진을 완성했습니다.

---

### 3. SecurityAnalyzer — 4개 위협 인텔리전스 분석 API

**분석 API 1. Nuclei CVE 매핑 (`port_vulnerability`)**
`asyncio.create_subprocess_exec()`으로 Nuclei를 비동기 실행하여 `-tags cve -jsonl` 옵션으로 CVE ID, 심각도, 설명, 참조 링크를 구조화된 JSON으로 수집합니다.

**분석 API 2. Shodan OSINT 연동 (`match_shodan`)**
Shodan API로 대상 IP의 외부 노출 이력, 소속 조직(`org`), OS, VPN 여부를 조회합니다. 내부 진단만으로는 파악할 수 없는 외부 공격자 시점의 가시성을 확보합니다.

**분석 API 3. EPSS 공격 발생 확률 조회 (`get_epss_score`)**
`api.first.org`의 EPSS 공식 API를 호출하여 탐지된 CVE별 실제 공격 발생 확률(0.0~1.0)을 조회합니다. 이론적 위험도(CVSS)와 실제 공격 가능성을 분리하여 최종 Risk Score에 반영합니다.

**분석 API 4. VirusTotal IP 평판 조회 (`match_virustotal`)**
VirusTotal API v3으로 대상 IP에 대한 엔진별 악성 판정 수와 전체 검사 엔진 수를 조회하여 IP 평판(`Malicious` / `Clean`)을 반환합니다.

---

### 4. EvidenceCollector — 패킷 캡처 및 증거 수집 체계

**비동기 패킷 스니핑 (`start_packet_capture` / `stop_packet_capture`)**
포트 활성화가 확인되는 즉시 `Scapy AsyncSniffer`로 백그라운드에서 해당 포트의 인/아웃바운드 트래픽을 실시간 덤프합니다. 분석 종료 후 타임스탬프 기반 **PCAP 파일(`traffic_*.pcap`)**로 자동 저장하여 침해사고 대응 시 증거 보존 요건을 충족합니다.

**Wappalyzer 웹 기술스택 탐지 (`collect_web_metadata`)**
HTTP/HTTPS 포트(80, 443) 또는 배너에 `http`가 포함된 경우 `Wappalyzer.latest()`로 웹 서비스의 프레임워크, CMS, CDN, 서버 소프트웨어 등 기술스택을 탐지하고 `tech_stack.json`으로 저장합니다.

**배너 직접 수집 (`fetch_banner_advanced`)**
웹 서비스가 아닌 경우 raw 소켓으로 HTTP 프로브를 직접 전송하여 `Server:` 헤더에서 배너 정보를 추출하고 `banner_info.txt`로 저장합니다.

---

### 5. SolutionGenerator — Gemini AI 보안 권고안 생성

**AI 보안 권고안 생성 (`ai_remediation`)**
Nuclei에서 탐지된 CVE가 존재할 경우 포트, 서비스 정보, 취약점 목록을 Gemini API(`gemini-2.0-flash`)에 전달합니다. CERT 전문가 역할로 프롬프트를 구성하여 버전 업데이트 방법, 즉시 적용 가능한 방화벽 설정 변경, 실무자가 바로 사용할 수 있는 CLI 명령어를 한국어로 반환합니다. AI 호출은 `asyncio.Semaphore(1)`로 동시 호출을 제어합니다.

---

### 6. ReportSchema — Risk Score 산출 및 최종 리포트

**위험도 스코어링**
CVSS 기본 점수(취약점 존재 시 7.5)와 EPSS 최대값을 가중 평균하여 최종 위험도 점수를 산출합니다.

```
Risk Score = (CVSS_base × 0.6) + (EPSS_max × 10 × 0.4)
Risk Level = "위험" (score > 6.0) | "안전"
```

**최종 출력 JSON 구조**

```json
{
  "scan_metadata": {
    "target_ip": "127.0.0.1",
    "scan_mode": "SYN_SCAN",
    "timestamp": "2025-01-01 00:00:00"
  },
  "summary": {
    "port": 443,
    "service_name": "nginx",
    "risk_score": 7.2,
    "risk_level": "위험",
    "enterprise_target": false
  },
  "details": {
    "cve_list": [],
    "shodan_data": {},
    "reputation_data": {},
    "remediation": "AI 생성 보안 권고안"
  },
  "evidence": {
    "is_web": true,
    "tech_stack": {},
    "pcap_path": "evidence/443/traffic_20250101_000000.pcap",
    "raw_log": "배너 정보"
  }
}
```

---

## 기술 스택

| 영역 | 기술 |
|------|------|
| 개발 언어 | Python 3.10+ |
| 비동기 처리 | asyncio, ThreadPoolExecutor |
| 포트 스캔 | asyncio TCP Connect, python-nmap (SYN Scan) |
| 서비스 역추적 | psutil |
| CVE 매핑 | Nuclei |
| OSINT | Shodan API |
| IP 평판 | VirusTotal API v3 |
| 공격 확률 | EPSS API (api.first.org) |
| 웹 기술스택 탐지 | Wappalyzer |
| 패킷 캡처 | Scapy AsyncSniffer |
| AI 권고안 생성 | Google Gemini API (gemini-2.0-flash) |
| 로깅 | logging (deepguard.log) |

---

## 환경 구성

### 라이브러리 설치

```
pip install python-nmap shodan aiohttp psutil scapy wappalyzer-python google-genai webdriver-manager
```

### Nuclei 설치 (CVE 매핑에 필수)

Nuclei 공식 사이트(projectdiscovery.io)에서 바이너리를 다운로드한 후 시스템 PATH에 등록합니다.

### API 키 설정 (`deepguard_portscanner.py` 내 상수)

| 상수 | 발급처 |
|------|--------|
| `SHODAN_API_KEY` | shodan.io |
| `VT_API_KEY` | virustotal.com |
| `my_gemini_key` | aistudio.google.com |

---

## 실행 방법

```python
# __main__ 블록에서 진단 대상 IP 설정 후 실행
controller = DeepguardController(gemini_key="your_gemini_key")
final_output = asyncio.run(controller.main_controller("진단할 IP 주소"))
```

포트 범위를 직접 지정할 수도 있습니다.

```python
# 특정 포트만 진단
final_output = asyncio.run(controller.main_controller("127.0.0.1", port_range=[80, 443, 3306, 3389]))
```

---

## 담당 소스 코드

- **`deepguard_portscanner.py`**: PortScanner, ServiceIdentifier, SecurityAnalyzer, EvidenceCollector, SolutionGenerator, ReportSchema, DeepguardController 7개 클래스 및 전체 파이프라인이 구현된 메인 엔진
