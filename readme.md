# LNIS AFS RAW Validator

## 1. 개요

이 프로젝트는 GNSS 수신기에서 수집한 `capture.graw`를 LunaNet AFS 프레임으로 부호화하여 UDP로 송신하고, 수신 측에서 다시 GNSS RAW로 복원해 데이터 무결성과 전송 성능을 검증하는 .NET 8 WPF 프로그램이다.

현재 AFS 경로는 다음 범위를 실제로 구현한다.

```text
capture.graw
→ GNSS RAW 레코드 분할
→ SB3/SB4 사용자 정의 메시지
→ CRC24
→ LDPC 부호화
→ 천공
→ 인터리빙
→ 6000심볼 AFS 프레임
→ 750바이트 패킹
→ UDP 브로드캐스트
→ 디인터리빙·LDPC 복호·CRC24 검사
→ GNSS RAW 재조립
→ reconstructed.graw
→ CRC32·SHA-256 비교
```

확산, 반송파 변조, I/Q 생성, RF 송수신, 신호 획득 및 추적은 수행하지 않는다.

## 2. 현재 구현 상태

| 기능 | 상태 |
|---|---|
| UBX 수집 및 `GnssRawEnvelope` 정규화 | 구현 |
| `capture.graw` 직렬화·역직렬화 | 구현 |
| SB3/SB4 RAW Fragment | 구현 |
| SB1 BCH 생성 | 구현, DLL에서 원본 호출 |
| SB2 PRN 8 almanac·시각 데이터 | 구현 |
| CRC24·LDPC·천공·인터리빙 | 구현, DLL에서 원본 호출 |
| AFS 6000심볼 생성·복호 | 구현 |
| UDP 브로드캐스트 3회 중복 송신 | 구현 |
| 중복·손상·누락 검출 | 구현 |
| RAW CRC32·SHA-256 완전 비교 | 구현 |
| JSON·CSV 성능 결과 | 구현 |
| HDTN/BPv7 라우팅 | 미구현, 관련 지표 N/A |
| 공통 PVT Solver | 미구현, 관련 지표 N/A |

## 3. 오픈소스 DLL 통합

`LANS-AFS-SIM-main`과 `PocketSDR-AFS-main` 원본 파일은 수정하지 않는다. 프로젝트가 관리하는 별도 C ABI 래퍼를 Windows x64 DLL로 빌드한다.

```text
WPF / C#
  └─ AfsNativeCodec (P/Invoke)
       └─ LnisAfsCodec.dll
            ├─ LANS-AFS-SIM 인코더
            └─ PocketSDR-AFS 디코더
```

DLL에서 재사용하는 기능:

- `generate_BCH_AFS_SF1`
- `append_CRC24`
- `encode_LDPC_AFS_SF2`
- `encode_LDPC_AFS_SF3`
- `interleave_AFS_SF234`
- `sdr_decode_LDPC_AFS_SF2`
- `sdr_decode_LDPC_AFS_SF3`

LDPC 구현은 전역 상태를 사용하므로 모든 DLL 호출은 프로세스 전체에서 직렬화한다.

### DLL ABI

```c
uint32_t lnis_afs_get_abi_version(void);
const char *lnis_afs_get_last_error(void);
int32_t lnis_afs_encode_frame(...);
int32_t lnis_afs_decode_frame(...);
```

인코더 입력:

- SB2 CRC 입력 데이터 1176비트
- SB3 CRC 입력 데이터 846비트
- SB4 CRC 입력 데이터 846비트
- TOI 0~99

출력은 6000개의 이진 심볼을 MSB-first로 패킹한 750바이트다.

## 4. AFS 프레임 구조

```text
동기 패턴  68심볼
SB1        52심볼
SB2      2400심볼
SB3      1740심볼
SB4      1740심볼
-----------------
합계      6000심볼
```

SB2~SB4의 5880심볼은 하나의 블록으로 인터리빙된다.

### SB1과 시각

- FID는 0을 사용한다.
- TOI는 다음 12초 프레임의 시각을 나타낸다.
- 프레임은 실시간 12초를 기다리지 않고 즉시 생성한다.
- 프레임마다 TOI를 논리적으로 증가시킨다.
- TOI 99 이후 ITOW, ITOW 503 이후 GPS Week를 증가시킨다.

### SB2

- 단일 논리 PRN은 8이다.
- 선택한 almanac 파일에서 PRN 8 항목을 읽는다.
- 첫 Observation epoch의 GPS Week와 TOW를 우선 사용한다.
- Observation이 없으면 첫 레코드의 UTC 수집 시각을 GPST로 변환한다.
- WN 13비트와 ITOW 9비트 뒤에 PRN 8 항법 필드를 배치한다.
- 나머지 예약 영역은 기존 시험 패턴을 유지한다.

## 5. SB3/SB4 사용자 정의 메시지

프로젝트 SISICD v1은 6비트 사용자 정의 타입 `63`을 사용한다.

```text
메시지 타입     6비트 = 63
사용자 데이터 840비트 = 105바이트
CRC24          24비트
-----------------------
합계          870비트
```

105바이트 사용자 데이터 구조:

| Offset | 길이 | 필드 |
|---:|---:|---|
| 0 | 1 | 프로토콜 버전, 현재 1 |
| 1 | 1 | Start/End 플래그 |
| 2 | 4 | RAW 레코드 순번 |
| 6 | 2 | Fragment index |
| 8 | 2 | Fragment count |
| 10 | 4 | 원본 레코드 길이 |
| 14 | 1 | 현재 payload 길이 |
| 15 | 4 | 원본 레코드 CRC32 |
| 19 | 최대 86 | 실제 RAW 데이터 |

SB3과 SB4는 각각 독립적인 Fragment 하나를 운반한다. AFS 프레임 하나의 최대 RAW payload는 172바이트다. Fragment가 홀수이면 마지막 Fragment를 두 SB에 중복 배치하고 수신 측에서 제거한다.

## 6. UDP 프로토콜

기본 설정:

| 항목 | 기본값 |
|---|---:|
| 데이터 포트 | 45821 |
| 결과 포트 | 45822 |
| 중복 송신 | 3회 |
| 결과 대기시간 | 30초 |
| End 유예시간 | 1000ms |
| Link Probe 간격 | 1000ms |

지원 패킷:

- `TimeSyncRequest`, `TimeSyncResponse`
- `SessionStart`
- `Frame`
- `Probe`, `ProbeResponse`
- `SessionEnd`
- `Result`

모든 패킷은 `LAFS` magic, 프로토콜 버전, TestId, 종류, 순번, PRN, WN/ITOW/TOI, 송신시각, payload 길이와 CRC32를 포함한다.

AFS Frame 패킷은 750바이트 frame payload를 사용해 일반적인 Ethernet MTU 이내에 유지한다. 동일 논리 패킷은 3회 송신하며 수신기는 `TestId + PacketKind + Sequence`로 중복 제거한다. NACK 기반 재전송은 사용하지 않는다.

시험 시작 전 4-timestamp 교환을 8회 수행하고 중앙값으로 송·수신 PC의 시계 오프셋을 추정한다. 단방향 지연은 이 오프셋을 보정해 계산한다.

## 7. 실행 역할

### 송신기

1. `capture.graw`와 PRN 8 almanac을 선택한다.
2. directed broadcast 주소와 포트를 입력한다.
3. RAW를 AFS 프레임으로 생성한다.
4. Session Start, Probe, Frame, Session End를 방송한다.
5. 수신기의 Result 유니캐스트를 기다린다.
6. 송신 측 결과 폴더에 JSON과 CSV를 저장한다.

### 수신기

1. 데이터 포트에서 UDP를 수신 대기한다.
2. AFS 프레임을 DLL로 복호한다.
3. SB3/SB4 Fragment를 레코드 순서대로 재조립한다.
4. `reconstructed.graw`를 생성한다.
5. 원본 manifest의 길이·레코드 수·SHA-256과 비교한다.
6. 결과를 송신 endpoint로 유니캐스트한다.

### 로컬시험

한 WPF 프로세스에서 수신기를 먼저 시작한 후 `127.0.0.1`로 전체 송수신 경로를 검증한다. 최초 확인에는 로컬시험 사용을 권장한다.

## 8. 성능지표

| 분류 | 성능지표 | 설명 | 구현 상태 |
|---|---|---|---|
| 네트워크 성능 | 링크 가용률 | 링크가 사용 가능한 시간 비율 | Probe로 측정 |
| 네트워크 성능 | 평균 지연 | 송신부터 수신까지 걸리는 평균 시간 | 측정 |
| 네트워크 성능 | 최대 지연 | 시험 중 발생한 최대 전달 지연 | 측정 |
| 네트워크 성능 | 처리량 | 단위 시간당 전달된 데이터량 | RAW goodput 측정 |
| 라우팅 성능 | 패킷 손실률 | 수신되지 않은 데이터 비율 | 논리 프레임 기준 측정 |
| 라우팅 성능 | 패킷 전달률 | 최종 수신 성공 비율 | 논리 프레임 기준 측정 |
| 라우팅 성능 | 재라우팅 시간 | 새로운 경로가 적용되는 시간 | N/A |
| 라우팅 성능 | 라우팅 오버헤드 | 전체 전송량 중 제어 메시지가 차지하는 비율 | N/A |
| 라우팅 성능 | 경로 안정성 | 일정 시간 유지되는 경로의 지속성 | N/A |
| PVT 성능 | 위치 오차 | 기준 위치 산출 값과 PVT 산출 값의 차이 | N/A |
| PVT 성능 | 시간 오차 | 기준 시간 대비 산출 시간 차이 | N/A |
| PVT 성능 | PVT 전달 지연 | PVT 정보가 수신되는 데 걸리는 시간 | N/A |
| 시스템 성능 | CPU 사용률 | 라우팅 및 PVT 처리 시 프로세서 사용량 | 송·수신 앱 측정 |
| 시스템 성능 | 메모리 사용량 | 번들 저장 및 처리 시 메모리 사용량 | 송·수신 앱 측정 |
| 시스템 성능 | 로그 저장률 | 시험데이터 기록 성공률 | 측정 |

패킷 손실률과 전달률은 중복 UDP 데이터그램이 아니라 논리 AFS 프레임을 기준으로 판정한다. 세 복제본 중 하나라도 정상 도착하면 해당 논리 프레임은 전달 성공이다.

성능 임계값은 기본적으로 비활성화되어 있다. 사용자가 WPF에서 활성화한 지표만 최종 PASS/FAIL에 반영한다. `Measured`와 `NotApplicable`은 최종 판정에 영향을 주지 않는다.

## 9. 최종 판정

필수 RAW 무결성 조건:

- 원본·복원 파일 길이 일치
- RAW 레코드 수 일치
- 모든 Fragment 완전 조립
- 레코드 CRC32 일치
- 전체 파일 SHA-256 일치

상태:

| 상태 | 의미 |
|---|---|
| Pass | 필수 무결성과 활성 임계값을 모두 만족 |
| Fail | RAW 불일치 또는 활성 임계값 초과 |
| Measured | 값은 측정했지만 임계값 미설정 |
| NotApplicable | 현재 구성에서 측정할 수 없음 |
| Inconclusive | 취소, DLL 누락 또는 시험 자체를 완료하지 못함 |

## 10. 결과 파일

수신 결과 폴더:

```text
reconstructed.graw
result.json
metrics-summary.csv
metrics-timeseries.csv
```

송신 결과 폴더:

```text
result.json
metrics-summary.csv
metrics-timeseries.csv
```

`result.json`에는 RAW 무결성, 네트워크 카운터, 성능지표와 최종 판정이 저장된다. CSV는 Excel 등의 후처리 도구에서 바로 사용할 수 있다.

## 11. 빌드와 실행

### 요구사항

- Windows x64
- .NET 8 SDK
- DLL 재빌드 시 WSL Ubuntu와 `x86_64-w64-mingw32-gcc`

### WPF 빌드

명령은 솔루션 파일이 있는 `LnisAfsValidator` 폴더에서 실행해야 한다. 현재 위치가 `LnisAfsValidator\오픈소스`라면 먼저 한 단계 위로 이동한다.

```powershell
Set-Location ..
dotnet build LnisAfsValidator.sln
```

Visual Studio에서는 솔루션 구성을 `Debug | x64` 또는 `Release | x64`로 선택한다. 명령줄에서 x64 구성을 명시하려면 다음과 같이 실행한다.

```powershell
dotnet build LnisAfsValidator.sln -c Debug -p:Platform=x64
```

빌드된 DLL은 자동으로 WPF 출력 폴더에 복사된다.

```text
bin/Debug/net8.0-windows/LnisAfsCodec.dll
```

### 네이티브 DLL 재빌드

```powershell
./Native/LnisAfsCodec/build-wsl.ps1
```

O: 보안 디스크는 WSL에 직접 마운트되지 않으므로 스크립트가 필요한 원본 파일을 Windows 임시 폴더에 복사해 빌드한다. 임시 복사본에서 손상된 선택적 로그 함수만 비활성화하며 원본 파일은 변경하지 않는다. 결과는 다음 위치에 저장된다.

```text
Native/LnisAfsCodec/bin/win-x64/LnisAfsCodec.dll
```

### 빌드 문제 확인

`MSB1009: 프로젝트 파일이 없습니다`가 나오면 대부분 실행 폴더가 `오픈소스`이기 때문이다. `LnisAfsValidator.sln`이 있는 상위 폴더로 이동한 뒤 다시 실행한다.

```powershell
Set-Location O:\3.ing\LNIS\LnisAfsValidator
dotnet clean LnisAfsValidator.sln
dotnet build LnisAfsValidator.sln -c Debug -p:Platform=x64
dotnet test Tests/LnisAfsValidator.Tests.csproj
```

빌드는 성공하지만 실행 시 `LnisAfsCodec.dll`을 찾지 못한다면 다음 파일이 존재하는지 확인한다.

```text
Native/LnisAfsCodec/bin/win-x64/LnisAfsCodec.dll
bin/Debug/net8.0-windows/LnisAfsCodec.dll
```

첫 번째 파일이 없으면 위의 `build-wsl.ps1`을 실행한 후 WPF를 다시 빌드한다.

## 12. 테스트

```powershell
dotnet test Tests/LnisAfsValidator.Tests.csproj
```

현재 검증 항목:

- GNSS RAW 직렬화 왕복
- RAW Fragment 크기·순서·CRC32 왕복
- UDP 패킷 encode/decode와 CRC 손상 검출
- 논리 프레임 손실률·전달률·지연 계산
- 실제 네이티브 DLL의 SB2/SB3/SB4 encode/decode
- 합성 `capture.graw`의 로컬 UDP 종단간 송수신
- `reconstructed.graw` 바이트 완전 일치

현재 전체 테스트는 23개다.

## 13. 주요 코드 위치

```text
AfsDashboardWindow.xaml              WPF 실행 화면
AfsMainViewModel.cs                  실행 제어와 설정
Core/AfsProtocolModels.cs            성능지표 모델
Core/AfsSessionModels.cs             세션·결과 모델
Infrastructure/AfsRawFragmentCodec.cs RAW Fragment와 재조립
Infrastructure/AfsSb2Builder.cs      PRN 8 almanac·SB2 생성
Infrastructure/AfsPacketCodec.cs     UDP wire format
Infrastructure/AfsNativeCodec.cs     DLL P/Invoke
Infrastructure/AfsUdpSessionService.cs 송신·수신 orchestration
Infrastructure/AfsPerformance.cs     성능 계산과 자원 계측
Native/LnisAfsCodec/                 C ABI·DLL·빌드 스크립트
Tests/AfsProtocolTests.cs            AFS 단위·통합 테스트
```

## 14. 제한사항과 향후 작업

- 현재 수신기 한 대만 지원한다.
- PRN 8 단일 논리 AFS 스트림을 사용한다.
- Custom Type 63은 프로젝트 SISICD이므로 다른 LNSP와 사용하려면 타입 할당 합의가 필요하다.
- UDP NACK·선택 재전송은 구현하지 않았다.
- HDTN/BPv7 경로와 재라우팅 지표는 후속 구현 대상이다.
- 공통 PVT Solver와 위치·시간 오차 판정은 후속 구현 대상이다.
- 외부 NTP/PTP 대신 UDP timestamp 교환으로 시계 오프셋을 추정하므로 비대칭 경로에서는 단방향 지연 오차가 커질 수 있다.

## 15. 라이선스

배포 출력의 `licenses` 폴더에 LANS-AFS-SIM, PocketSDR-AFS/PocketSDR 라이선스와 제3자 고지를 포함한다. 원본 오픈소스 디렉터리는 빌드 입력으로만 사용하며 수정하거나 그 안에 새 산출물을 생성하지 않는다.
