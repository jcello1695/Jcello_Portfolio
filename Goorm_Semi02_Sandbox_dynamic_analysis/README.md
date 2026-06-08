# MobSF 연동 안드로이드 동적 분석 및 위험 식별 엔진 (Semi-Project 2차)

> **담당 파트:** 동적 분석 파이프라인 아키텍처 설계 및 위험 식별/정규식 필터링 엔진 리드 개발 (`deepguard_dynamic_analyzer.py`)

정적 분석 데이터(CVE, MITRE ATT&CK 기술)를 기반으로 Genymotion 가상 격리 환경을 구동하고, Frida로 메모리를 계측하며, Android logcat 로그를 실시간으로 추출·추적하여 악성 행위를 자동 판별하는 보안 시스템입니다. 독자적으로 구조화한 **8개의 핵심 API 파이프라인**을 기반으로 작동합니다.

---

## 시스템 아키텍처

```
[정적 분석 결과 (out_runs_b/{run_id}/)]
  ├─ evidence.json
  └─ interpretation.json
              │
              ▼
  [API 2. 정적 데이터 수신 및 바인딩]
  receive_static_result()
              │
              ▼
  [API 3. 분석 모드 결정 엔진]
  parse_static_result()
  ├─ speedy 모드: tags 배열 존재 시 → 선별 추적
  └─ exact  모드: 항상 실행 → 제로 트러스트 전수 조사
              │
              ▼
  [API 5. 샌드박스 구동 및 Frida 바인딩]
  dynamic_environment()
  ├─ Genymotion 에뮬레이터 부팅 검증 (sys.boot_completed)
  ├─ APK 설치 (adb install -r, 최대 3회 재시도)
  ├─ Frida device.spawn() → session attach → agent.js 로드
  └─ 안티분석 탐지 시 (Terminated / Detach 등) → T1622 강제 부여
              │
              ▼
  [API 6. Logcat 실시간 수집]
  extract_logcat()
  adb logcat -d -v time -t 5000 → raw_logs
              │
              ▼
  [API 7. 정규식 기반 위험 식별 엔진]
  regex_filtering()
  malicious_behavior 딕셔너리 (threat_signature.py) 와 실시간 매핑
              │
              ▼
  [API 8. 위협 스코어링 및 JSON 리포트 반환]
  result_json() → out_b/dynamic_report_{run_id}.json
```

---

## 핵심 모듈 및 API 명세

### Part 1. 인프라 환경 구성 및 데이터 수집

**API 1. 크로스 플랫폼 ADB 경로 자동 탐색 (`get_adb_path`)**

`shutil.which("adb")`로 시스템 PATH를 1차 검증하고, macOS(`~/Library/Android/sdk/platform-tools/adb`)와 Windows(`~\AppData\Local\Android\Sdk\platform-tools\adb.exe`) 표준 SDK 경로를 순서대로 탐색합니다. 어느 OS에서도 분석 환경이 정상 구동되도록 OS 독립성을 보장합니다.

**API 2. 정적 분석 데이터 수신 및 바인딩 (`receive_static_result`)**

`out_runs_b/{run_id}/` 경로에서 `evidence.json`과 `interpretation.json`을 로드하여 엔진 내부 딕셔너리로 매핑합니다. 파일 손상 및 경로 오류에 대한 예외 처리를 포함합니다.

**API 3. 위협 식별 기반 분석 모드 설정 (`parse_static_result`)**

`interpretation.json`의 `tags` 배열 유무를 판별하여 두 가지 분석 아키텍처로 동적 분기합니다.

| 모드 | 조건 | 동작 |
|------|------|------|
| `speedy` | tags 배열 존재 시 | 태그 기반 힌트 전달, 선별 행위만 추적 |
| `exact` | 항상 실행 | 제로 트러스트, 전체 전수 조사 |

---

### Part 2. 가상화 샌드박스 제어 및 메모리 분석

**API 4. APK 패키지명 실시간 추출 (`get_package_name`)**

`aapt dump badging {apk_path}` 명령을 `subprocess.run()`으로 실행하고 정규식 `package: name='([^']+)'`으로 패키지명과 메인 액티비티를 동적으로 파싱합니다.

**API 5. 샌드박스 구동 및 Frida 에이전트 바인딩 (`dynamic_environment`)**

총 12단계로 실행됩니다.

```
5-1  에뮬레이터 구동 스크립트 실행 (deepguard_emulator.bat)
5-2  adb devices 응답 대기 (최대 15회 재시도)
5-3  sys.boot_completed == 1 확인 (최대 20회 재시도)
5-4  루트 권한 재부여 (adb root)
5-5  Frida 서버 포트 포워딩 및 실행 (tcp:27042)
5-6  패키지 매니저 응답 확인 (pm path android)
5-7  APK 설치 (adb install -r, 최대 3회 재시도)
5-8  패키지 존재 여부 확인 (pm list packages)
5-9  Frida device.spawn() → session attach
5-10 agent.js 로드 및 DEX 덤프 메시지 핸들러 등록
5-11 20초 동적 실행 후 세션 정상 해제
5-12 에뮬레이터 종료 (finally 블록 보장)
```

**안티분석 탐지 로직:** 앱이 분석 환경을 탐지하여 `Terminated`, `Detach`, `Closed`, `Transport`, `Gadget`, `Jailed` 키워드를 포함한 예외를 발생시키면 이를 캐치하여 MITRE ATT&CK `T1622` 태그를 강제 부여하고 `detected` 상태로 반환합니다.

---

### Part 3. 실시간 로그 파싱 및 위협 탐지

**API 6. Logcat 기반 커널 로그 추출 (`extract_logcat`)**

`adb -s {device} logcat -d -v time -t 5000` 명령으로 최근 5000줄 타임스탬프 로그를 추출합니다. `CalledProcessError`, `FileNotFoundError`, `TimeoutExpired` 등 ADB 레벨 예외를 각각 분기 처리하여 어떤 환경에서도 에러 원인을 명확히 반환합니다.

**API 7. 표준 정규식 기반 위험 식별 엔진 (`regex_filtering`)**

`threat_signature.py`의 `malicious_behavior` 딕셔너리를 참조하여 카테고리별 정규식 패턴을 raw 로그 전체 라인과 `re.IGNORECASE`로 매핑합니다. `speedy` 모드에서는 정적 분석이 지정한 `behavior` 항목만 대조하고, `exact` 모드에서는 전체 카테고리를 전수 조사합니다.

**API 8. 위협 스코어링 및 표준 가시성 리포트 반환 (`result_json`)**

탐지된 위협 시그니처를 집계하여 최종 JSON 리포트를 생성하고 `out_b/dynamic_report_{run_id}.json`으로 저장합니다.

```json
{
  "metadata": {
    "analyzer": "DeepGuard_Dynamic_Engine",
    "target_app": "앱 이름",
    "mode": "speedy | exact"
  },
  "analysis_summary": {
    "status": "success | detected",
    "anti_analysis_detected": [],
    "dex_extraction": {
      "dumped_count": 0,
      "analyzed_count": 0,
      "efficiency": "0.0%"
    },
    "evidence_summary": {
      "total_evidence_found": 0,
      "reliability_score": "High | Low"
    }
  },
  "threat_details": {
    "match_count": 0,
    "matches": []
  },
  "artifacts": {
    "dump_path": "dumped_dex/",
    "log_file": "analyzed_{apk}_{mode}.txt"
  }
}
```

---

## 기술 스택

| 영역 | 기술 |
|------|------|
| 개발 언어 | Python 3.10+ |
| 가상화 환경 | Genymotion 에뮬레이터 |
| 정적 분석 연동 | MobSF (evidence.json, interpretation.json) |
| 동적 계측 | Frida (frida, agent.js) |
| 로그 수집 | Android logcat (adb) |
| 프로세스 제어 | subprocess, adb |
| 위협 시그니처 | threat_signature.py 정규식 딕셔너리 |
| 위협 매핑 | MITRE ATT&CK |

---

## 외부 의존 파일

| 파일 | 설명 | 담당 |
|------|------|------|
| `agent.js` | Frida 메모리 계측 스크립트 (DEX 덤프 트리거) | 팀원 담당 |
| `threat_signature.py` | 카테고리별 악성 행위 정규식 딕셔너리 | 본인 담당 |
| `deepguard_emulator.bat` | Genymotion 에뮬레이터 구동 배치 스크립트 | 팀원 담당 |

---

## 담당 소스 코드

- **`deepguard_dynamic_analyzer.py`**: API 1~8 파이프라인 전체 로직이 집약된 메인 엔진
- **`threat_signature.py`**: 위험 식별 엔진(API 7)에서 참조하는 보안 시그니처 정규식 딕셔너리
