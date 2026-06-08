# OSINT 기반 위협 인텔리전스 수집 시스템 (Semi-Project 1차)

> **담당 파트:** 비동기 B2B 크롤러 모듈 일체, 검색 로직 및 데이터 표준화 아키텍처 구축 (`deepguard_crawl_b2b.py`)

기업 이메일을 입력받아 텔레그램 해커 채널 17개, 다크웹 미러링 사이트 4개, Tor 기반 실제 다크웹 3개를 4개 레이어로 병렬 탐색하여 임직원 자격증명 유출, 기밀 자산 노출, 랜섬웨어 피해, 프로젝트 소스코드 유출 여부를 실시간으로 진단하는 인텔리전스 모듈입니다.

---

## 시스템 아키텍처

```
[입력: 이메일 리스트 / 프로젝트 키워드]
              │
              ▼
     [데이터 1차 가공]
  이메일 파싱 → 도메인/회사명 추출 → 중복 제거
              │
    ┌─────────┴──────────────────┐
    │                            │
    ▼                            ▼
[비동기 수집]                [동기 수집]
Telethon (텔레그램)          requests + Tor Proxy
aiohttp  (미러링 사이트)     (socks5h://127.0.0.1:9050)
              │
              ▼
   [스팸 필터 / 유효성 검증]
   spamfilter() → valid_check()
              │
              ▼
  [데이터 표준화 (format_database)]
  UUID4 기반 고유 ID 생성 → JSON 포맷 정규화
              │
              ▼
   [keyword_type 태그 부착]
   credential / asset / company / project
              │
              ▼
  [백엔드 전달 (MongoDB / ElasticSearch)]
```

---

## 4대 검색 레이어

### Layer 1. 이메일 주소 기반 — 자격증명 유출 탐지 (`credential`)
- **검색 대상:** 텔레그램 해커 채널 17개, 미러링 사이트 4개 (Pastebin, Darkforums, Ahmia, GitHub), Tor 다크웹 3개 (Ahmia .onion, Darkforums .onion, Torch .onion)
- **검색 방식:** 입력 이메일 + 위협 키워드 (`combo`, `password`, `stealer`, `leak`, `auth`, `dump` 등 12개)
- **목적:** 임직원 ID/PW의 해커 채널 유출 여부 실시간 감지

### Layer 2. 도메인 주소 기반 — 치명적 자산 노출 식별 (`asset`)
- **검색 대상:** Layer 1 동일 채널
- **검색 방식:** 이메일에서 파싱한 도메인 + 보안 키워드 (`admin`, `root`, `confidential`, `backup`, `vpn`, `intranet` 등 8개)
- **목적:** 관리자 권한, 대외비 문서, 백업 파일 등 치명적 자산 노출 여부 식별

### Layer 3. 회사이름 기반 — 랜섬웨어 피해 현황 모니터링 (`company`)
- **검색 대상:** ransomwatch GitHub 공개 피해 DB (`raw.githubusercontent.com/joshhighet/ransomwatch/main/posts.json`)
- **검색 방식:** Gemini API(`gemini-2.5-flash`)가 기업명 분석 → 경쟁사 3개 자동 산출 → 자사 + 경쟁사명으로 피해 리스트 정규식 대조
- **목적:** 경쟁사 랜섬웨어 피해 현황을 거시적으로 파악하여 사전 대응

### Layer 4. 프로젝트 키워드 기반 — 미공개 소스코드·설계도 유출 탐지 (`project`)
- **검색 대상:** 텔레그램 해커 채널, 미러링 사이트(GitHub 포함), Tor 다크웹
- **검색 방식:** 사용자 입력 키워드 + 프로젝트 키워드 (`source code`, `blueprint`, `schema`, `api key`, `dump` 등 8개)
- **목적:** 개발 중인 신제품·미공개 프로젝트 소스코드·설계도 유출 여부 정밀 검색

---

## 핵심 구현 명세

### 비동기 병렬 수집 엔진
텔레그램 채널 17개와 미러링 사이트 4개를 동시에 탐색하기 위해 `aiohttp.ClientSession`과 `asyncio.gather()`로 병렬 I/O를 구성했습니다. 멀티스레딩 대비 시스템 자원 소비를 최소화하면서 전체 수집 시간을 단축했습니다.

### 스팸 필터 및 유효성 검증 (`spamfilter`, `valid_check`)
수집된 데이터 중 20자 미만 텍스트와 `join my channel`, `bitcoin`, `casino` 등 7개 스팸 키워드 포함 메시지를 1차 차단합니다. 이후 실제 위협 키워드가 함께 포함된 경우에만 유출로 인식하는 2단계 검증 로직을 구현했습니다.

### 컨텍스트 추출 (`extract_context`)
유출이 확인된 페이지에서 키워드 전후 100자를 추출하여 `raw_text`에 저장합니다. 전문을 통째로 저장하는 대신 관련 컨텍스트만 정밀하게 잘라내어 분석 효율을 높였습니다.

### 데이터 표준화 (`format_database`)
텔레그램, 미러링 사이트, Tor 다크웹 등 이종 소스에서 수집된 데이터를 소스 종류에 무관하게 단일 JSON 스키마로 정규화합니다. 백엔드가 별도 파싱 없이 MongoDB / ElasticSearch로 직접 적재할 수 있도록 인터페이스를 통일했습니다.

```json
{
  "id": "uuid4로 생성된 고유 식별자",
  "keyword_type": "credential | asset | company | project",
  "source_id": "telegram(채널명) | surface(사이트명) | darkweb(엔진명) | ransomware(그룹명)",
  "original_link": "유출 확인 페이지 주소",
  "raw_text": "키워드 전후 100자 컨텍스트",
  "leak_date": "유출 확인 날짜"
}
```

---

## 기술 스택

| 영역 | 기술 |
|------|------|
| 개발 언어 | Python 3.10+ |
| 텔레그램 수집 | Telethon |
| 비동기 HTTP | aiohttp, asyncio |
| 미러링/서피스웹 | requests, BeautifulSoup4 |
| 다크웹 접속 | requests + Tor Proxy (socks5h://127.0.0.1:9050) |
| Tor 네트워크 | Tor Expert Bundle (Port 9050) |
| AI 경쟁사 분석 | Google Gemini API (gemini-2.5-flash) |
| 랜섬웨어 피드 | ransomwatch GitHub 공개 DB |
| ID 생성 | UUID4 |
| 환경 변수 | python-dotenv (auth.env) |

---

## 환경 구성

### 1. Tor 네트워크 구성 (필수)

Tor Project에서 Windows Expert Bundle 다운로드 후 `tor.exe`를 관리자 권한으로 실행합니다. 실행 창을 닫지 않고 백그라운드에 유지해야 합니다. (기본 포트: 9050)

### 2. 라이브러리 설치

```
pip install telethon aiohttp requests beautifulsoup4 python-dotenv google-generativeai
```

### 3. 환경 변수 설정 (`auth.env`)

```
telegram_api_id=11111111
telegram_api_hash=api해시값
telegram_session=deepguard_b2b_session
google_api_key=구글api키
```

텔레그램 API는 my.telegram.org, Gemini API는 aistudio.google.com에서 발급합니다.
`=` 앞뒤로 공백 없이 작성하세요.

---

## 실행 방법

코드 하단 `__main__` 블록에서 진단 대상을 설정합니다.

```python
input_email = ["target@company.com"]
input_keyword = "프로젝트명"  # Layer 4 선택 사항
```

최초 실행 시 텔레그램 인증이 필요합니다. 터미널에 `+821012345678` 형식으로 입력 후 수신된 인증코드를 입력하면 `deepguard_b2b_session.session` 파일이 생성되며 이후 자동 로그인됩니다.

---

## 담당 소스 코드

- **`deepguard_crawl_b2b.py`**: 4대 레이어 비동기 수집 엔진, 스팸 필터, 데이터 표준화, 랜섬웨어 피드 연동, 컨트롤러 전체가 집약된 메인 모듈
