# LNIS AFS Validator

## 1. 개요

LNIS AFS Validator는 GNSS RAW 데이터를 LunaNet AFS 프레임으로 부호화하여 UDP로 전송하고, 수신 측에서 다시 RAW로 복원해 데이터 무결성과 전송 성능을 검증하는 Windows용 .NET 8 WPF 프로그램이다.

송신부는 기존 `capture.graw` 파일을 선택하거나 같은 화면의 GNSS COM 탭에서 Windows가 인식한 COM1, COM2 등의 포트를 선택해 원본 직렬 데이터를 받을 수 있다. 장비 프로토콜은 인터페이스로 분리되어 있으며, 실제 프로토콜이 결정되기 전에는 원본 `serial-input.bin`을 보존한다.

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
→ 선택 시험의 오류 주입 또는 데이터그램 Drop
→ UDP 송수신
→ 디인터리빙·LDPC 복호·CRC 검사
→ RAW 재조립
→ reconstructed.graw
→ CRC32·SHA-256 비교
```

## 프로그램 전체 동작 흐름

프로그램을 실행하면 시작 화면에서 `송신부` 또는 `수신부`를 연다. 수신부 창은 열리는 즉시 저장된 포트로 자동 수신 대기하며, 한 시험이 끝나면 마지막 결과를 화면에 유지한 채 다음 세션을 다시 기다린다. 송신부의 `SessionStart`가 시험 종류와 오류·드롭 조건을 전달하면 수신부가 이를 자동 표시하고 해당 복호·복구 경로를 적용한다. `취소` 후에는 `수신 대기` 버튼으로 수동 재시작할 수 있다. GNSS COM 수집은 별도 역할이 아니라 송신부의 입력 방법 중 하나다.

```text
LNIS AFS Validator 실행
        │
        ▼
   시작 대시보드
        │
        ├─ 수신부 ───── UDP 포트 대기 ─────────────────────────────┐
        │                                                        │
        ├─ 송신부 ─┬─ 기존 capture.graw 파일 선택                   │
        │          │                                             │
        │          └─ GNSS COM 입력 탭                            │
        │               ├─ 활성 COM 포트·Baud rate 선택            │
        │               ├─ 프로토콜 미정: serial-input.bin 보존     │
        │               └─ 등록 어댑터: capture.graw 생성·자동 적용  │
        │                  │                                     │
        │                  ▼                                     │
        │       Test A~E 선택·시험 조건 설정                        │
        │                  │                                     │
        │             AFS 프레임 생성                              │
        │        CRC-24Q → LDPC → 인터리빙                         │
        │                  │                                     │
        │                  ▼                                     │
        │       6000심볼을 750바이트로 패킹                         │
        │     B/C 심볼 오류 · D SP 오류 주입                        │
        │                  │                                     │
        │                  ▼                                     │
        │        UDP Broadcast 데이터그램 전송 ─────────────────────┤
        │                                                          ▼
        │                                              중복 제거·AFS 복호
        │                                       디인터리빙 → LDPC → CRC-24Q
        │                                                          │
        │                                                          ▼
        │                                                RAW Fragment 재조립
        │                                                          │
        │                                                          ▼
        │                                               reconstructed.graw 생성
        │                                                          │
        │                                                          ▼
        │                                              길이·레코드·SHA-256 비교
        │                                                          │
        │                                  결과 유니캐스트 ◀────────┘
        │                                          │
        │                                          ▼
        │                              송신부·수신부 결과 화면/파일
```

### 송신부 GNSS COM 입력의 실제 순서

1. 송신부의 `GNSS COM에서 수집` 탭을 열고 Windows가 현재 인식한 COM 포트 목록에서 장비 포트를 선택한다.
2. 장비를 새로 연결했다면 `새로고침`으로 COM1, COM2 등의 활성 포트 목록을 다시 조회한다. 필요하면 포트 이름을 직접 입력할 수도 있다.
3. Baud rate, DTR/RTS와 저장 폴더를 설정하고, 장비 프로토콜이 아직 정해지지 않았다면 `원본 바이트 저장(프로토콜 미정)`을 선택한다.
4. 수집 서비스가 선택한 `SerialPort`를 열고 들어오는 모든 바이트를 변경 없이 `serial-input.bin`에 기록한다.
5. 선택된 `IGnssDeviceProtocolAdapter`에도 같은 바이트 조각을 전달한다. COM 읽기와 장비별 해석은 서로 의존하지 않는다.
6. 어댑터가 `ObservationEpochMessage`, `NavigationUpdateMessage` 등을 반환할 수 있을 때만 정규화 `capture.graw`를 생성한다.
7. 수집이 끝나면 생성된 `capture.graw` 경로가 같은 송신부의 AFS 입력 경로에 자동 적용된다.

현재 등록된 어댑터:

| ID | 목적 | `capture.graw` 생성 |
|---|---|---|
| `raw-only` | 아직 모르는 장비 프로토콜의 원본 직렬 바이트 보존 | 아니요 |
| `lnis-canonical-v1` | 시험 및 외부 변환기 연동용 4바이트 길이 + LGRW v1 스트림 | 예 |

특정 제조사의 UBX나 다른 바이너리 프로토콜은 아직 선택하지 않았다. 장비가 결정되면 `IGnssDeviceProtocolAdapter`를 구현하고 `GnssProtocolAdapterCatalog`에 등록한다. 기존 COM 수집 패널, 송신부 ViewModel과 AFS 송신 코드는 변경할 필요가 없다.

### 송수신 시험의 실제 순서

1. 수신 PC에서 수신부 창을 연다. 저장된 데이터 포트와 결과 포트로 자동 수신 대기가 시작된다.
2. 송신 PC에서 송신부 창을 열고 `capture.graw`, Broadcast 주소, 포트와 중복 송신 횟수를 설정한다.
3. Test A~E 중 하나를 선택하고 오류 심볼 수, Seed, SP 손상 간격 또는 UDP Drop 조건을 입력한다. 선택한 시험에 해당하는 값만 적용되며, 각 항목의 라벨이나 입력칸에 마우스를 올리면 적용 시험·범위·동작 설명이 표시된다.
4. 송신부는 RAW 레코드를 SB3/SB4 Fragment로 나누고, 내부 검증 패턴으로 SB2를 구성한다.
5. 각 SB에 CRC-24Q와 LDPC를 적용하고 SB2~SB4를 인터리빙하여 정확히 6000심볼의 AFS 프레임을 만든다.
6. B/C는 payload 심볼, D는 지정 간격 프레임의 SP를 송신 직전에 반전한다. A/E의 AFS 프레임은 변경하지 않는다.
7. 6000개 이진 심볼을 MSB-first 750바이트로 패킹하고, E만 Seed에 따라 Frame 데이터그램을 제거한 뒤 UDP Broadcast한다.
8. 수신부는 SessionStart의 시험 조건을 자동 표시하고 동일 논리 프레임의 복제본을 제거한다. D는 수신 프레임을 연속 심볼로 이어 SP를 다시 검색하고, 나머지는 수신 프레임을 직접 복호한다.
9. 디인터리빙·LDPC 복호·CRC 검사를 통과한 SB3/SB4 Fragment로 `reconstructed.graw`를 만든다.
10. 원본과 복원 파일의 길이, 레코드 수, CRC32와 SHA-256을 비교하고 시험별 기준으로 Pass/Fail을 결정한다.
11. 수신 결과를 송신부의 결과 포트로 유니캐스트하고 양쪽 화면과 JSON·CSV 파일에 기록한다.

Test A는 정상 프레임을 그대로 전송한다. Test B/C는 실제 `capture.graw`에서 만든 모든 AFS 프레임에 Random/Burst 오류를 주입한다. Test D는 마지막 프레임을 제외한 지정 간격 프레임의 SP를 훼손하여 다음 정상 SP 재탐색을 검증한다. Test E는 Frame 데이터그램 일부만 Seed 기반으로 제거한다.

### Test B/C/D 종단 간 복구의 실제 순서

1. 송신부가 실제 `capture.graw`로 정상 AFS 프레임을 생성한다.
2. Test B는 SP/SB1을 제외한 영역의 서로 다른 심볼을, Test C는 연속 심볼을 지정 개수만큼 반전한 뒤 UDP로 전송한다.
3. 수신부가 SB2·SB3·SB4를 LDPC 복호하고 CRC-24Q 통과 수, 정정 심볼 수와 RAW 복원 결과를 집계한다.
4. Test D는 지정 간격 프레임의 SP를 훼손하여 보내고, 수신부는 UDP 경계를 프레임 경계로 쓰지 않고 수신 payload들을 이어 정확한 SP를 다시 찾는다.
5. D는 모든 데이터그램 수신과 손상되지 않은 프레임 수만큼의 SP 재탐색·복호 성공을 Pass 기준으로 사용한다. 의도적으로 제외된 손상 프레임 때문에 전체 RAW 해시는 일치하지 않을 수 있다.

이 실험의 오류는 RF 샘플 잡음이 아니라 AFS 디지털 심볼의 강제 반전이다. 따라서 결과는 CRC·LDPC·인터리빙과 프레임 동기 기능의 성능을 뜻하며 RF 수신 감도, Tracking 또는 PVT 성능을 뜻하지 않는다.

## 2. 화면 구성

### 역할별 화면

프로그램 시작 시 `AfsDashboardWindow`에서 다음 독립 창을 선택한다.

- `송신부`: 기존 `capture.graw` 파일을 선택하거나 GNSS COM 탭에서 RAW를 수집하고 Test A~E와 조건을 선택한 뒤 AFS 프레임을 UDP Broadcast한다.
- `수신부`: 별도 입력 파일이나 시험 선택 없이 UDP를 대기한다. SessionStart에서 시험 종류와 조건을 자동 인식하여 AFS 복호, SP 재탐색, RAW 복원과 판정을 수행한다.

AFS 6000심볼은 MSB-first 750바이트로 패킹되며 한 AFS 프레임 전체가 한 UDP 데이터그램 payload에 들어간다. 운영 UI에는 Local 역할이 없고 루프백 종단간 경로는 자동 통합시험으로 검증한다.

외부 알마낙 입력은 없다. 화면의 PRN 8은 UDP 패킷과 시험 세션에서 프레임을 구분하는 기본 논리 식별값이며, I/Q 생성이나 RF 위성 신호·PVT 계산을 뜻하지 않는다.

## 3. Test A 정상 종단간 시험

### 송신기

1. `capture.graw` 레코드를 읽는다.
2. GNSS 주차·ITOW를 넣은 결정론적 1176비트 검증 패턴으로 SB2를 만든다.
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
송신부 AFS Encode
→ 인터리빙 완료
→ Random 심볼 반전
→ UDP 송신
→ 수신부 LDPC Decode
→ CRC-24Q 검사
→ RAW 복원·원본 비교
```

기본적으로 SP와 SB1을 제외한 심볼 `120~5999`에서 오류 위치를 선택한다. 오류 위치는 Seed와 반복 번호로 결정되어 같은 설정으로 재현할 수 있다.

오류 개수와 Seed를 바꾸려면 조건별로 송수신 시험을 다시 실행한다.

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

Test D는 실제 송신 프레임 중 지정 간격의 프레임 SP를 훼손한다. 마지막 프레임은 다음 정상 SP가 없으므로 훼손하지 않는다.

```text
Frame 0: SP 훼손
Frame 1: 정상
...
Frame N: 지정 간격에 따라 SP 훼손
Frame Last: 정상
```

수신부는 UDP payload를 sequence 순으로 이어 붙이고 데이터그램 경계를 프레임 경계로 사용하지 않는다. 동기 탐색기는 한 심볼씩 이동하며 정확한 68심볼 SP를 찾는다.

측정 항목:

- 수신 논리 프레임 수
- 정상 SP 재탐색 프레임 수
- 다음 정상 프레임 Decode 복구 수
- SB2/SB3/SB4 CRC 통과 수

복구시간 계산:

```text
(다음 정상 SP 위치 - 손상 프레임 시작 위치) × 2 ms
```

이 값은 AFS 심볼 기준 논리시간이다. 실제 RF Tracking 복귀시간이나 PVT 복귀시간은 아니다.

## 7. Test E UDP Packet Drop

Test E는 실제 네트워크 손실과 구분되는 의도적 Drop 시험이다.

설정 항목:

- 독립된 Sender / Receiver 역할
- 송신부 `capture.graw` 입력
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
- GPS Week, ITOW와 재현 가능한 내부 검증 패턴 포함

외부 알마낙은 읽지 않는다. 현재 PRN 8은 UDP 헤더와 AFS 시험 세션의 논리 스트림 식별값일 뿐이며, 특정 위성 RF 신호 생성·궤도 계산·PVT에는 사용되지 않는다.

### SB3/SB4

- 사용자 정의 타입 63
- CRC 입력 각 846비트
- CRC-24Q 추가 후 각 870비트
- LDPC·천공 후 각 1740심볼
- 한 subframe의 실제 RAW payload 최대 86바이트

### AFS 구현 출처와 현재 규격 적용 범위

아래 표는 현재 프로그램에서 사용하는 AFS 처리 요소가 어디에서 왔으며, 실제 앱에서 어느 수준까지 구현되어 있는지를 구분한 것이다. 여기서 `오픈소스 기반`은 해당 오픈소스의 알고리즘이나 함수를 네이티브 코덱을 통해 사용한다는 뜻이며, LunaNet 표준 메시지의 실제 운용 데이터 내용까지 모두 구현되었다는 뜻은 아니다.

| 구분 | 세부 항목 | 원형·출처 | 현재 프로그램의 실제 구현 | LunaNet AFS 규격 관점 |
|---|---|---|---|---|
| 프레임 구조 | 6000 symbol 프레임과 SP/SB1/SB2/SB3/SB4 배치 | LunaNet AFS 구조 및 AFS 오픈소스 | 68 + 52 + 2400 + 1740 + 1740 symbol로 프레임을 구성하고 MSB-first 750바이트로 패킹 | 물리적인 프레임 길이와 서브프레임 경계는 규격 구조를 따름 |
| 동기 | 68-bit SP | AFS 오픈소스 기반 | 고정 SP를 프레임 앞에 삽입하고 수신 시 동일 패턴을 검색하여 Test D 재동기를 수행 | SP 구조 시험에 사용하며 RF 신호 추적 시험은 아님 |
| SB1 | FID·TOI와 BCH 생성 | `LANS-AFS-SIM` | FID 0과 TOI를 넣고 `generate_BCH_AFS_SF1()`로 52 symbol SB1 생성 | SB1 부호화 구조는 오픈소스 구현을 사용 |
| CRC | CRC-24Q 생성·검사 | `LANS-AFS-SIM` | SB2/SB3/SB4 데이터 뒤에 `append_CRC24()`로 CRC를 추가하고 수신 시 다시 검사 | CRC 알고리즘과 길이는 규격 구조에 맞춤 |
| SB2 채널 부호화 | LDPC 행렬·부호화와 천공 | `LANS-AFS-SIM` | 1176 data bit와 24 CRC bit를 LDPC 부호화·천공하여 2400 symbol 생성 | 채널 부호화 구조는 오픈소스 기반 |
| SB3/SB4 채널 부호화 | LDPC 행렬·부호화와 천공 | `LANS-AFS-SIM` | 각 846 data bit와 24 CRC bit를 LDPC 부호화·천공하여 각 1740 symbol 생성 | 채널 부호화 구조는 오픈소스 기반 |
| LDPC 복호 | SB2/SB3/SB4 LDPC 복호 | `PocketSDR-AFS` | `sdr_decode_LDPC_AFS_SF2()`와 `sdr_decode_LDPC_AFS_SF3()`를 호출하고 복호 결과 및 변경 bit 수를 기록 | 오픈소스 복호기를 직접 연동 |
| 천공 복원 | puncture 위치의 erasure 복원 | AFS 오픈소스 기반 | 송신 시 생략된 위치를 수신 네이티브 코덱이 erasure로 복원한 뒤 LDPC 복호 | 채널 복호 과정의 일부로 구현 |
| 인터리빙 | SB2~SB4 60×98 인터리빙 | `LANS-AFS-SIM` | `interleave_AFS_SF234()`를 사용하고 수신 측에서 역인터리빙 후 각 SB를 분리 | 인터리빙 배열 구조는 오픈소스 기반 |
| SB2 데이터 내용 | CED, ToT, Health/Safety, Time Conversion 영역 | 현재 앱 자체 시험 패턴 | `AfsSb2Builder`가 GPS Week·ITOW를 포함한 결정론적 검증 패턴 1176 bit를 생성 | 실제 MSG-G4/G8/G2/G30 비트 레이아웃은 아직 구현하지 않았으므로 운용 메시지 준수 상태가 아님 |
| SB3/SB4 데이터 내용 | LunaNet Navigation Service Protocol 메시지 영역 | 현재 앱 자체 형식 | `.graw` 레코드를 조각내 SB3/SB4에 번갈아 적재하고 수신 측에서 재조립 | 표준 MSG-G* 메시지가 아니며, Custom Message로 사용하려면 제공자 SISICD 정의가 필요 |
| 사용자 메시지 형식 | Type 63과 19바이트 fragment header | 현재 앱 자체 정의 | 세션·레코드·fragment 식별 정보, 길이 및 CRC32를 담아 최대 86바이트 RAW payload를 운반 | 프로젝트 내부 시험 형식으로서 타 LunaNet 구현과의 상호운용은 보장하지 않음 |
| GNSS 입력 파일 | `capture.graw` / `reconstructed.graw` | 현재 앱 자체 형식 | `LGRW` 레코드를 길이-prefix 형식으로 저장하고 송수신 전후 CRC32·SHA-256을 비교 | LunaNet 표준 파일 형식이 아니라 앱 내부 검증용 컨테이너 |
| 오류 시험 | Test A~E 오류 주입·Drop·재동기 | 현재 앱 자체 구현 | 정상 전송, Random/Burst symbol 오류, SP 손상, UDP datagram Drop을 송신부에서 선택해 수행 | 규격 적합성 인증 벡터가 아니라 현재 코덱과 전송 경로의 기능 시험 |
| 전송 제어 | UDP 세션·결과 반환 프로토콜 | 현재 앱 자체 구현 | `SessionStart`, AFS frame, `SessionEnd`, 결과 packet을 별도 UDP 프로토콜로 교환 | AFS 무선 규격 자체가 아니며 두 PC 간 검증을 위한 시험 제어 계층 |
| 인터페이스 문서 | Type 63·fragment header·`.graw`의 SISICD | 없음 | 코드와 본 README에 내부 형식만 설명되어 있음 | 외부 상호운용 또는 NASA 규격 준수를 주장하려면 별도 SISICD 작성과 통제된 시험 벡터 대조가 필요 |

따라서 현재 구현은 **AFS 프레임 구조, CRC, LDPC, 천공 및 인터리빙 코덱을 오픈소스 기반으로 통합한 `.graw` 종단 간 전송·복구 시험기**로 보는 것이 정확하다. SB2의 실제 표준 메시지와 SB3/SB4의 표준 MSG-G* 내용을 완성한 운용용 LunaNet AFS 송수신기 또는 NASA 규격 적합성 인증 구현으로 보아서는 안 된다.

완전한 규격 준수를 목표로 할 경우에는 SB2의 CED·ToT·Health/Safety·Time Conversion 비트 배치, SB3/SB4의 표준 메시지 레이아웃, Custom Message용 SISICD 및 규격 시험 벡터 대조를 별도 작업으로 완료해야 한다.

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

Test B/C는 최종 RAW 무결성과 활성 임계값을 만족해야 Pass다. Test D는 모든 논리 프레임을 수신하고, 손상되지 않은 예상 프레임 수만큼 SP 재탐색과 Decode에 성공해야 Pass다.

## 11. 결과 파일

기본 결과 위치:

```text
%LocalAppData%\LnisAfsValidator\Runs
```

### Test A~E 수신 결과

```text
reconstructed.graw
result.json
metrics-summary.csv
metrics-timeseries.csv
```

### Test A~E 송신 결과

```text
result.json
metrics-summary.csv
metrics-timeseries.csv
```

모든 시험은 같은 결과 파일 형식을 사용한다. `metrics-summary.csv`에는 `DecodedFrames`, SB2/SB3/SB4 CRC 통과 프레임 수, `CorrectedSymbols`가 기록되며 Test D에는 `RecoveredSyncFrames`가 추가된다.

개발 회귀용 로컬 실험 서비스의 `fec-*`, `sync-*` 파일은 공식 송수신 UI에서 생성하지 않는다.

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
- 분할된 COM 바이트 스트림의 원본 무손실 저장
- 프로토콜 미정 모드와 Canonical 어댑터의 `capture.graw` 생성 조건
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

현재 전체 자동 테스트는 34개이며 모두 통과한다. 네이티브 DLL이 없으면 코덱 통합시험은 건너뛰지 않고 실패한다.

로컬 UDP 통합시험에서 Test A의 RAW 복원, Test B/C의 실제 프레임 오류정정, Test D의 다음 정상 SP 재탐색과 Test E의 의도적 데이터그램 Drop 판정을 검증한다.

### 반복 가능한 화면 실행 인수

```powershell
# 수신부를 먼저 실행하면 자동으로 수신 대기
LnisAfsValidator.exe --receiver --data-port=45821 --result-port=45822

# 송신부 정상 전송(Test A)
LnisAfsValidator.exe --sender --auto-start --capture=C:\data\capture.graw --broadcast=255.255.255.255

# 송신부 의도적 Drop(Test E)
LnisAfsValidator.exe --sender --auto-start --capture=C:\data\capture.graw --test=TestE_UdpDrop --drop-rate=10 --drop-seed=2

# 송수신 Random 오류정정 시험(Test B 예시)
LnisAfsValidator.exe --sender --auto-start --capture=C:\data\capture.graw --test=TestB_RandomErrors --errors=5 --seed=2025

# 송수신 SP 재동기 시험(Test D 예시)
LnisAfsValidator.exe --sender --auto-start --capture=C:\data\capture.graw --test=TestD_SyncRecovery --errors=2 --sync-interval=10 --seed=2025
```

## 16. 주요 코드

`App.xaml.cs`가 Composition Root로서 AFS·GNSS Infrastructure 구현을 생성하고 Core 인터페이스를 통해 ViewModel에 주입한다. Presentation의 ViewModel과 Window는 Infrastructure 구현을 직접 생성하지 않는다.

```text
App.xaml.cs
  AFS·GNSS 구현 생성과 ViewModel·Window 의존성 주입

AfsDashboardWindow.xaml
  송신부·수신부 시작 화면

AfsDashboardViewModel.cs
  역할별 독립 창 실행

Presentation/Sender/
  Test A~E 선택·조건 설정과 송신 XAML·ViewModel

Presentation/Receiver/
  AFS 수신·RAW 복원 XAML과 ViewModel

Presentation/Gnss/
  송신부에 삽입되는 COM 포트 설정·수집 현황 패널과 하위 ViewModel

Core/Gnss/GnssCaptureAbstractions.cs
  바이트 소스·프로토콜 어댑터·캡처 서비스 인터페이스

Infrastructure/Gnss/Sources/SerialPortGnssByteSource.cs
  제조사 중립 SerialPort 입력

Infrastructure/Gnss/Protocols/GnssProtocolAdapters.cs
  Raw-only·Canonical 어댑터와 확장 카탈로그

Infrastructure/Gnss/Capture/GnssComCaptureService.cs
  원본 Serial 보존과 선택적 capture.graw 기록

Core/Afs/ErrorCorrection/AfsErrorInjectionModels.cs
  오류 주입 설정과 결과

Core/Afs/ErrorCorrection/AfsErrorCorrectionExperimentModels.cs
  Test B/C 결과 모델

Core/Afs/Recovery/AfsSyncRecoveryModels.cs
  Test D 결과 모델

Core/Afs/Protocol/AfsProtocolLimits.cs
  Presentation과 Infrastructure가 공유하는 AFS 고정 한계값

Infrastructure/Afs/Experiments/AfsErrorInjector.cs
  Random·Burst·SyncLoss 심볼 반전

Infrastructure/Afs/Experiments/AfsErrorCorrectionExperimentService.cs
  Test B/C 반복 실행·집계·파일 저장

Infrastructure/Afs/Synchronization/AfsFrameSynchronizer.cs
  68심볼 SP 탐색과 프레임 추출

Infrastructure/Afs/Synchronization/AfsSyncRecoveryExperimentService.cs
  Test D 3프레임 생성·재탐색·복구시간

Infrastructure/Afs/Experiments/AfsPacketDropSimulator.cs
  Test E Seed 기반 데이터그램 제거 결정

Infrastructure/Afs/Sessions/AfsSessionOrchestrator.cs
  설정 검증과 송신·수신 세션 Handler 위임

Infrastructure/Afs/Sessions/AfsSendSessionHandler.cs
  SessionStart·Frame·SessionEnd 송신과 Result 수신 순서

Infrastructure/Afs/Sessions/AfsReceiveSessionHandler.cs
  패킷 수신·프레임 복호·RAW 복원과 Result 반환 순서

Infrastructure/Afs/Frames/AfsFrameService.cs
  RAW Fragment 구성, AFS 부호화·복호와 Test B/C/D 오류 주입

Infrastructure/Afs/Transport/AfsUdpTransport.cs
  UDP 패킷 송수신·복제·Drop과 중복 판정

Infrastructure/Afs/Synchronization/AfsTimeSynchronizer.cs
  송수신 PC 시각 교환과 시계 오프셋 계산

Infrastructure/Afs/Evaluation/AfsTestEvaluator.cs
  Test A~E 합격 조건과 성능지표 계산

Infrastructure/Afs/Results/AfsResultWriter.cs
  reconstructed.graw·result.json·CSV 저장

Infrastructure/Afs/Codecs/AfsNativeCodec.cs
  DLL P/Invoke와 LDPC 상태 전달

Native/LnisAfsCodec/
  프로젝트 소유 C ABI 래퍼와 빌드 스크립트
```

## 17. 제한사항

- 실제 GNSS 장비 프로토콜은 아직 결정되지 않아 제조사별 어댑터가 없다. 현재 실제 COM 장비는 원본 바이트 보존까지 보장한다.
- `capture.graw` 생성에는 해당 장비 프로토콜을 해석하는 `IGnssDeviceProtocolAdapter` 구현이 필요하다.
- PRN 8 단일 논리 AFS 스트림만 지원한다.
- 수신기는 한 대만 지원한다.
- Test D는 손상 프레임 자체의 RAW를 복원하지 않고 다음 정상 SP와 프레임 복호 복귀를 판정한다.
- RF 획득·Tracking 복귀시간은 측정하지 않는다.
- PVT Solver가 없어 PVT 성공률과 위치·시간 오차를 측정하지 않는다.
- HDTN/BPv7 라우팅과 재라우팅은 구현하지 않았다.
- UDP NACK와 선택 재전송은 구현하지 않았다.
- UDP timestamp 기반 시계 보정은 비대칭 네트워크에서 오차가 발생할 수 있다.

## 18. 라이선스

`오픈소스` 아래 LANS-AFS-SIM, PocketSDR-AFS, PocketSDR, HDTN 트리는 읽기 전용 빌드 입력과 분석 자료로 취급한다. 프로젝트 기능을 위해 원본 파일을 직접 수정하지 않는다.

배포 출력의 `licenses` 폴더에는 사용한 오픈소스 라이선스와 제3자 고지를 포함한다.
