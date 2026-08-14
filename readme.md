# LNIS AFS Validator

## 1. 개요

LNIS AFS Validator는 GNSS RAW 데이터를 LunaNet AFS 프레임으로 부호화하여 UDP로 전송하고, 수신 측에서 다시 RAW로 복원해 데이터 무결성과 전송 성능을 검증하는 Windows용 .NET 8 WPF 프로그램이다.

정상 송수신뿐 아니라 AFS 오류정정 실험설계의 Test A~E를 실행할 수 있다.

| 시험 | 내용 | 상태 |
|---|---|---|
| Test A | 정상 AFS/UDP 종단간 송수신과 RAW 복원 | 구현 |
| Test B | Random 심볼 오류 반복시험 | 구현 |
| Test C | Burst 연속 심볼 오류 반복시험 | 구현 |
| Test D | SP 훼손 후 다음 정상 프레임 재동기·복호 복귀 | 구현 |
| Test E | Seed 기반 의도적 UDP Frame 데이터그램 Drop | 구현 |
| 다중 PRN·PVT | PVT 성공률, 위치오차, PVT 복귀시간 | 미구현 |
| HDTN/BPv7 | DTN 라우팅과 재라우팅 | 미구현 |

현재 프로그램은 AFS 디지털 프레임과 UDP 전달 경로를 시험한다. 확산, 반송파 변조, I/Q 생성, RF 송수신, 신호 획득·추적 및 실제 PVT 계산은 수행하지 않는다.

```text
capture.graw
→ RAW 레코드 분할
→ SB3/SB4 사용자 정의 Fragment
→ CRC-24Q
→ LDPC 부호화·천공
→ 인터리빙
→ 6000심볼 AFS 프레임
→ 750바이트 MSB-first 패킹
→ UDP 송수신 또는 오류 주입
→ 디인터리빙·LDPC 복호·CRC 검사
→ RAW 재조립
→ reconstructed.graw
→ CRC32·SHA-256 비교
```

## 2. 화면 구성

### 기본 AFS 송수신 화면

프로그램 시작 시 `AfsDashboardWindow`가 열린다.

실행 역할:

- `Sender`: 다른 PC로 AFS 프레임 송신
- `Receiver`: AFS 프레임 수신과 RAW 복원
- `Local`: 한 프로세스에서 `127.0.0.1`로 전체 경로 시험

기본 입력:

- `capture.graw`
- PRN 8 almanac
- Broadcast 주소
- 데이터·결과 포트
- 가용률, 지연, 처리량, 손실률, 전달률 임계값

최초 기능 확인은 `Local` 역할을 권장한다.

### 오류 실험 전용 화면

기본 화면의 `오류 실험 창 열기` 버튼으로 별도 창을 연다. 오류 실험 창은 두 탭으로 구성된다.

1. `Test B/C/D · AFS 오류정정·재동기`
2. `Test E · UDP Packet Drop`

정상 송수신 설정과 오류 실험 설정을 분리하여 사용자가 시험 목적을 혼동하지 않도록 했다.

## 3. Test A 정상 종단간 시험

### 송신기

1. `capture.graw` 레코드를 읽는다.
2. GNSS 시각과 PRN 8 almanac으로 SB2를 만든다.
3. RAW 레코드를 최대 86바이트 단위 Fragment로 나눈다.
4. SB3과 SB4에 Fragment를 배치한다.
5. 네이티브 DLL로 6000심볼 AFS 프레임을 생성한다.
6. 각 논리 프레임을 기본 3회 UDP 전송한다.
7. 수신기의 결과를 기다린다.

### 수신기

1. UDP 패킷 CRC32와 중복 여부를 검사한다.
2. 750바이트 AFS 프레임을 네이티브 DLL로 복호한다.
3. CRC-24Q가 통과한 SB3/SB4 Fragment를 재조립한다.
4. `reconstructed.graw`를 생성한다.
5. 파일 길이, 레코드 수, CRC32, SHA-256을 비교한다.
6. 결과를 송신기로 유니캐스트한다.

필수 무결성 조건:

- 원본·복원 파일 길이 일치
- RAW 레코드 수 일치
- 모든 Fragment 조립 완료
- 레코드 CRC32 일치
- 전체 파일 SHA-256 일치

## 4. Test B Random 오류

CRC·LDPC·인터리빙이 모두 끝난 정상 AFS 프레임에서 임의 심볼을 반전한다.

```text
AFS Encode
→ 인터리빙 완료
→ Random 심볼 반전
→ LDPC Decode
→ CRC-24Q 검사
→ 원본 데이터 비교
```

기본적으로 SP와 SB1을 제외한 심볼 `120~5999`에서 오류 위치를 선택한다. 오류 위치는 Seed와 반복 번호로 결정되어 같은 설정으로 재현할 수 있다.

권장 조건:

```text
오류 개수: 1, 2, 5, 10, 20, 50
조건별 반복: 100회 이상
```

측정 항목:

- SB2/SB3/SB4 LDPC 성공 여부
- CRC-24Q 성공률
- 전체 프레임 복원률
- LDPC가 변경한 비트 수

## 5. Test C Burst 오류

인터리빙 완료 프레임의 연속 심볼 구간을 반전한다. 특정 시간 구간에 집중된 잡음·간섭을 모사하고 인터리빙 효과를 확인하는 시험이다.

권장 Burst 길이:

```text
5, 10, 20, 50, 100 symbols
```

시작 위치는 Seed와 반복 번호로 재현 가능하게 결정된다.

## 6. Test D Sync Loss와 재동기

Test D는 다음 3개 프레임을 하나의 스트림으로 만든다.

```text
Frame 0: 정상
Frame 1: 68심볼 SP 일부 훼손
Frame 2: 정상
```

동기 탐색기는 수신 시작 위치를 모른다고 가정하고 한 심볼씩 이동하며 68심볼 SP를 찾는다. 프레임 경계가 바이트 중간에 있어도 탐색할 수 있다.

측정 항목:

- 손상 프레임 거부율
- 다음 SP 재탐색 성공률
- 다음 정상 프레임 Decode 복구율
- 복구 프레임 수
- AFS 논리 복구시간
- 재탐색된 스트림 비트 위치

복구시간 계산:

```text
(다음 정상 SP 위치 - 손상 프레임 시작 위치) × 2 ms
```

이 값은 AFS 심볼 기준 논리시간이다. 실제 RF Tracking 복귀시간이나 PVT 복귀시간은 아니다.

## 7. Test E UDP Packet Drop

Test E는 실제 네트워크 손실과 구분되는 의도적 Drop 시험이다.

설정 항목:

- Sender / Receiver / Local 역할
- `capture.graw`와 PRN 8 almanac
- Broadcast 주소와 UDP 포트
- 중복 송신 횟수
- Drop Rate 0~100%
- Drop Seed

Frame 데이터그램만 설정 비율로 제거한다. 시험 세션 자체가 중단되지 않도록 다음 제어 패킷은 항상 전송한다.

- TimeSyncRequest / TimeSyncResponse
- SessionStart
- Probe / ProbeResponse
- SessionEnd
- Result

동일한 Seed에서는 같은 `Frame Sequence + Copy Index` 조합이 제거된다.

기록 항목:

- 설정 Drop Rate
- 실제 제거한 데이터그램 수와 비율
- 수신·중복·손상 데이터그램 수
- 논리 AFS 프레임 손실률과 전달률
- 최종 RAW 복원과 SHA-256 결과

중복 송신이 3회이면 복제본 중 하나 이상 정상 도착한 논리 프레임은 전달 성공이다.

## 8. AFS 프레임 구조

```text
동기 패턴     68심볼
SB1           52심볼
SB2         2400심볼
SB3         1740심볼
SB4         1740심볼
-------------------
합계         6000심볼
```

6000개 이진 심볼은 MSB-first로 패킹하여 750바이트가 된다. SB2~SB4의 5880심볼은 하나의 블록으로 인터리빙된다.

### SB1

- FID 0
- TOI 0~99
- `generate_BCH_AFS_SF1()` 재사용

### SB2

- CRC 입력 1176비트
- CRC-24Q 추가 후 1200비트
- LDPC·천공 후 2400심볼
- GPS Week, ITOW, PRN 8 almanac 포함

### SB3/SB4

- 사용자 정의 타입 63
- CRC 입력 각 846비트
- CRC-24Q 추가 후 각 870비트
- LDPC·천공 후 각 1740심볼
- 한 subframe의 실제 RAW payload 최대 86바이트

## 9. 네이티브 오픈소스 통합

`LANS-AFS-SIM-main`과 `PocketSDR-AFS-main` 원본은 직접 수정하지 않는다. 프로젝트 소유 C ABI 래퍼를 `LnisAfsCodec.dll`로 빌드한다.

```text
WPF / C#
  └─ AfsNativeCodec (P/Invoke)
       └─ LnisAfsCodec.dll
            ├─ LANS-AFS-SIM 인코더
            └─ PocketSDR-AFS 디코더
```

재사용 함수:

- `generate_BCH_AFS_SF1`
- `append_CRC24`
- `encode_LDPC_AFS_SF2`
- `encode_LDPC_AFS_SF3`
- `interleave_AFS_SF234`
- `sdr_decode_LDPC_AFS_SF2`
- `sdr_decode_LDPC_AFS_SF3`

PocketSDR-AFS LDPC 반환값:

- `0 이상`: LDPC parity 검증 성공이며 값은 복호 중 변경된 비트 수
- `-1`: LDPC parity 검증 실패
- LDPC 성공 후 CRC-24Q를 별도로 검사

LDPC 구현은 전역 상태를 사용하므로 DLL 호출은 프로세스 전체에서 직렬화한다.

## 10. 결과 상태

| 상태 | 의미 |
|---|---|
| Pass | 필수 RAW 무결성과 활성 임계값을 모두 만족 |
| Fail | RAW 불일치 또는 활성 임계값 초과 |
| Measured | 값은 측정했지만 합격 임계값이 없음 |
| NotApplicable | 현재 구성에서 측정할 수 없음 |
| Inconclusive | 취소, DLL 누락 또는 시험 미완료 |

Test B/C/D는 기본적으로 성공률을 측정하는 `Measured` 시험이다.

## 11. 결과 파일

기본 결과 위치:

```text
%LocalAppData%\LnisAfsValidator\Runs
```

### Test A / Test E 수신 결과

```text
reconstructed.graw
result.json
metrics-summary.csv
metrics-timeseries.csv
```

### Test A / Test E 송신 결과

```text
result.json
metrics-summary.csv
metrics-timeseries.csv
```

### Test B / Test C

```text
fec-result.json
fec-summary.csv
fec-trials.csv
reference-sb2.bits
reference-sb3.bits
reference-sb4.bits
실험데이터_파일설명.txt
frames/
  Random-0001/ 또는 Burst-0005/
    trial-0001-reference.afs
    trial-0001-injected.afs
    trial-0001-flipped-symbols.txt
    trial-0001-decoded-sb2.bits
    trial-0001-decoded-sb3.bits
    trial-0001-decoded-sb4.bits
```

- `reference.afs`: 오류 주입 전 정상 750바이트 프레임
- `injected.afs`: 실제 복호기에 입력한 오류 포함 프레임
- `flipped-symbols.txt`: 반전한 0 기준 심볼 인덱스
- `decoded-sb*.bits`: 복호기가 출력한 unpacked 0/1 배열

### Test D

```text
sync-result.json
sync-summary.csv
sync-trials.csv
실험데이터_파일설명.txt
SyncLoss-20/
  trial-0001-3frames.afsstream
  trial-0001-damaged-reference.afs
  trial-0001-damaged.afs
  trial-0001-flipped-sync-symbols.txt
  trial-0001-recovered.afs
```

`3frames.afsstream`은 정상·손상·정상 프레임을 연결한 2250바이트 파일이다.

## 12. 성능지표

네트워크:

- 링크 가용률
- 평균·최대 단방향 지연
- RAW goodput
- 의도적 UDP Drop Rate

전달:

- 논리 AFS 프레임 손실률
- 논리 AFS 프레임 전달률
- 중복·손상 데이터그램

오류정정:

- 오류 유형과 심볼 수
- LDPC 성공률
- CRC-24Q 성공률
- 전체 프레임 복원률
- LDPC 평균 변경 비트 수

재동기:

- 손상 프레임 거부율
- Sync 복구율
- Decode 복구율
- 평균 논리 복구시간

시스템:

- 평균·최대 CPU 사용률
- 평균·최대 메모리 사용량
- 로그 저장률

HDTN 재라우팅과 PVT 지표는 현재 `NotApplicable`이다.

## 13. UDP 프로토콜

기본값:

| 항목 | 기본값 |
|---|---:|
| 데이터 포트 | 45821 |
| 결과 포트 | 45822 |
| Frame 중복 송신 | 3회 |
| 결과 대기 | 30초 |
| SessionEnd 유예 | 1000ms |
| Probe 간격 | 1000ms |

지원 패킷:

- `TimeSyncRequest`, `TimeSyncResponse`
- `SessionStart`
- `Frame`
- `Probe`, `ProbeResponse`
- `SessionEnd`
- `Result`

모든 패킷은 `LAFS` magic, 버전, TestId, 종류, Sequence, CopyIndex, PRN, WN/ITOW/TOI, 송신시각, payload 길이와 CRC32를 포함한다.

동일 논리 패킷은 `TestId + PacketKind + Sequence`로 중복 제거한다. NACK 기반 재전송은 사용하지 않는다.

시험 시작 전 4-timestamp 교환을 8회 수행하고 중앙값으로 송·수신 PC의 시계 오프셋을 추정한다.

## 14. 빌드

요구사항:

- Windows x64
- .NET 8 SDK
- DLL 재빌드 시 WSL Ubuntu
- WSL의 `x86_64-w64-mingw32-gcc`

솔루션 루트에서 실행한다.

```powershell
dotnet build LnisAfsValidator.sln -c Debug -p:Platform=x64
```

출력:

```text
bin/Debug/net8.0-windows/LnisAfsValidator.exe
bin/Debug/net8.0-windows/LnisAfsCodec.dll
```

네이티브 DLL 재빌드:

```powershell
./Native/LnisAfsCodec/build-wsl.ps1
```

출력:

```text
Native/LnisAfsCodec/bin/win-x64/LnisAfsCodec.dll
```

O: 보안 디스크를 WSL에서 직접 마운트할 수 없는 환경을 위해 스크립트가 필요한 파일을 Windows 임시 폴더에 staging한다. 오픈소스 원본은 변경하지 않는다.

## 15. 테스트

```powershell
dotnet test Tests/LnisAfsValidator.Tests.csproj
```

자동검증 항목:

- GNSS RAW 직렬화 왕복
- RAW Fragment 분할·재조립·CRC32
- UDP packet encode/decode와 손상 검출
- 논리 프레임 손실률·전달률·지연
- 실제 네이티브 DLL AFS encode/decode
- 로컬 UDP 종단간 RAW 복원
- Random/Burst/SyncLoss 오류 위치
- 오류 개수 입력 파서
- 오류시험 바이너리·CSV·JSON 생성
- 비트 단위 SP 재탐색
- 다음 정상 프레임 Decode 복구
- Seed 기반 UDP Drop 재현성

현재 전체 테스트는 36개이며 모두 통과한다.

## 16. 주요 코드

```text
AfsDashboardWindow.xaml
  정상 AFS/UDP 화면과 오류 실험 창 진입

AfsDashboardViewModel.cs
  정상 송신·수신·로컬시험 제어

AfsErrorExperimentWindow.xaml
  Test B/C/D와 Test E 전용 탭 화면

AfsErrorExperimentViewModel.cs
  오류정정·재동기·UDP Drop 시험 제어

Core/AfsErrorInjectionModels.cs
  오류 주입 설정과 결과

Core/AfsErrorCorrectionExperimentModels.cs
  Test B/C 결과 모델

Core/AfsSyncRecoveryModels.cs
  Test D 결과 모델

Infrastructure/AfsErrorInjector.cs
  Random·Burst·SyncLoss 심볼 반전

Infrastructure/AfsErrorCorrectionExperimentService.cs
  Test B/C 반복 실행·집계·파일 저장

Infrastructure/AfsFrameSynchronizer.cs
  68심볼 SP 탐색과 프레임 추출

Infrastructure/AfsSyncRecoveryExperimentService.cs
  Test D 3프레임 생성·재탐색·복구시간

Infrastructure/AfsPacketDropSimulator.cs
  Test E Seed 기반 데이터그램 제거 결정

Infrastructure/AfsUdpSessionService.cs
  정상 UDP 송수신과 Test E Frame Drop

Infrastructure/AfsNativeCodec.cs
  DLL P/Invoke와 LDPC 상태 전달

Native/LnisAfsCodec/
  프로젝트 소유 C ABI 래퍼와 빌드 스크립트
```

## 17. 제한사항

- PRN 8 단일 논리 AFS 스트림만 지원한다.
- 수신기는 한 대만 지원한다.
- Test D 복구시간은 2ms/symbol을 적용한 논리시간이다.
- RF 획득·Tracking 복귀시간은 측정하지 않는다.
- PVT Solver가 없어 PVT 성공률과 위치·시간 오차를 측정하지 않는다.
- HDTN/BPv7 라우팅과 재라우팅은 구현하지 않았다.
- UDP NACK와 선택 재전송은 구현하지 않았다.
- UDP timestamp 기반 시계 보정은 비대칭 네트워크에서 오차가 발생할 수 있다.

## 18. 라이선스

`오픈소스` 아래 LANS-AFS-SIM, PocketSDR-AFS, PocketSDR, HDTN 트리는 읽기 전용 빌드 입력과 분석 자료로 취급한다. 프로젝트 기능을 위해 원본 파일을 직접 수정하지 않는다.

배포 출력의 `licenses` 폴더에는 사용한 오픈소스 라이선스와 제3자 고지를 포함한다.
