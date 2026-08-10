# LNIS 네트워크 검증장치

## 1. 프로젝트 개요

본 프로젝트는 GNSS 수신기에서 수집한 동일한 GNSS RAW 데이터를 다음 세 경로로 처리한 후, 각 경로에서 복원된 RAW 데이터와 PVT 결과가 원본과 일치하는지 검증하는 종단 간 시험 시스템이다.

1. **기준 경로**: 네트워크를 거치지 않고 원본 GNSS RAW로 PVT 계산
2. **HDTN 경로**: BPv7 Bundle로 포장하여 Ethernet TCP/IP로 전송
3. **AFS 경로**: GNSS RAW를 AFS 프레임으로 채널 부호화한 뒤 6000개의 이진 심볼을 네트워크로 전송

최종적으로 다음 결과를 비교하여 `PASS`, `FAIL`, `Inconclusive`를 판정한다.

```text
원본 GNSS RAW
├─ 기준 경로 ───────────────────────────────→ 기준 PVT
├─ HDTN/BPv7 경로 ─→ 복원 GNSS RAW ────────→ HDTN PVT
└─ AFS 프레임 경로 ─→ 복원 GNSS RAW ────────→ AFS PVT

검증
├─ 원본 RAW ↔ HDTN 복원 RAW ↔ AFS 복원 RAW
└─ 기준 PVT ↔ HDTN PVT ↔ AFS PVT
```

> 이 시스템은 AFS의 확산·변조와 I/Q 신호처리를 수행하지 않는다. 검증 범위는 AFS 프레임 생성, CRC24, LDPC, 천공, 인터리빙, 네트워크 전달 및 역처리를 통한 payload 복원이다. 안테나, RF, 신호 획득·추적, 도플러, 잡음 및 수신 감도는 검증 대상이 아니다.

### 1.1 현재 구현 상태

현재 코드는 최종 구조 전체가 아니라 다음 두 개의 독립 프로토타입 경로를 구현한 상태다.

```text
LANS-AFS-SIM / PocketSDR-AFS
→ AFS 프레임 인코딩·복호 기준 출력 생성
→ 단계별 비교 로그 생성
→ SP/SB1, CRC24, LDPC, 천공, 인터리빙 결과 비교
```

```text
ZED-F9P 또는 .ubx 파일
→ 읽기 전용 UBX 스트림 수집
→ RXM-RAWX / RXM-SFRBX checksum 검사·파싱
→ GPS·Galileo GnssRawEnvelope 정규화
→ 결정적 LGRW binary + CRC32 저장
→ 원본 capture.ubx 및 SHA-256 manifest 저장
```

현재 TCP 파일 전송은 HDTN/BPv7 또는 최종 AFS 프레임 전송 프로토콜을 구현한 것이 아니다. `GnssRawEnvelope`와 UBX 수집·직렬화는 구현됐지만 기준 PVT, HDTN/BPv7, AFS 사용자 정의 메시지, RAW Fragment, AFS 심볼 프레임 전송 및 RAW 3자 비교는 이후 구현 대상이다.

---

## 2. 검증 목표

### 2.1 RAW 데이터 무결성 검증

송신 전 GNSS RAW와 각 경로에서 복원한 GNSS RAW가 동일한지 검증한다.

```text
원본 GNSS RAW
= HDTN 경로에서 복원한 GNSS RAW
= AFS 경로에서 복원한 GNSS RAW
```

직렬화된 원본 바이트, 메시지별 CRC 또는 해시값을 사용하여 비교한다.

### 2.2 PVT 결과 검증

동일한 PVT Solver에 세 경로의 GNSS RAW를 입력하고 위치·속도·시간 결과가 허용 오차 안에서 일치하는지 검증한다.

```text
원본 RAW       → PVT Solver → 기준 PVT
HDTN 복원 RAW  → PVT Solver → HDTN PVT
AFS 복원 RAW   → PVT Solver → AFS PVT
```

동일한 Solver를 사용하면 결과 차이의 원인을 PVT 알고리즘 차이가 아니라 전송·복원 과정으로 한정할 수 있다.

### 2.3 네트워크 및 AFS 처리 검증

- BPv7 Bundle이 HDTN을 통해 정상적으로 전달되는지 확인
- 네트워크 패킷의 손실·중복·역순 발생 여부 측정
- AFS 6000심볼 프레임이 정상적으로 복원되는지 확인
- AFS Frame 및 사용자 정의 메시지가 정상적으로 복호되는지 확인
- PVT 계산에 필요한 관측값과 항법 데이터가 모두 복원되는지 확인

---

## 3. 전체 처리 흐름

### 3.1 송신 PC

```text
GNSS 수신기
→ 수신기 전용 RAW 수집
→ 수신기 전용 Parser
→ 공통 GnssRawEnvelope 생성
   ├─ ObservationEpoch
   ├─ NavigationUpdate
   └─ ReceiverMetadata

   ├─ 기준 경로
   │  └─ PVT Solver
   │     → 기준 PVT
   │
   ├─ HDTN 경로
   │  └─ 직렬화
   │     → BPv7 Payload
   │     → BPv7 Bundle
   │     → HDTN/TCPCLv4
   │     → Ethernet TCP/IP
   │
   └─ AFS 경로
      └─ 직렬화
         → GNSS RAW Fragment 분할
         → AFS 사용자 정의 메시지
         → AFS Data Frame
         → CRC24·LDPC·천공·인터리빙
         → 6000심볼 AFS 프레임
         → 네트워크 패킷화
         → TCP 또는 UDP 전송
```

### 3.2 수신 PC

```text
[HDTN 경로]

Ethernet TCP/IP
→ HDTN/TCPCLv4
→ BPv7 Bundle 해제
→ Payload 추출
→ 역직렬화
→ GnssRawEnvelope 복원
→ PVT Solver
→ HDTN PVT


[AFS 경로]

AFS 프레임 패킷 수신
→ 패킷 검사·순서 정렬
→ 6000심볼 프레임 복원
→ SP·SB1 확인
→ 디인터리빙·LDPC 복호·CRC24 검사
→ AFS 사용자 정의 메시지 추출
→ GNSS RAW Fragment 재조립
→ 역직렬화
→ GnssRawEnvelope 복원
→ PVT Solver
→ AFS PVT
```

### 3.3 최종 검증

```text
원본 RAW ↔ HDTN 복원 RAW ↔ AFS 복원 RAW
기준 PVT ↔ HDTN PVT ↔ AFS PVT

추가 품질 지표
├─ HDTN Bundle 전달 성공률 및 지연
├─ 네트워크 패킷 손실률·중복률·역순률
├─ AFS 프레임 복호율
└─ PVT 계산 성공률

→ PASS / FAIL / Inconclusive
```

---

## 4. 공통 GNSS RAW 모델

GNSS 수신기는 제조사마다 서로 다른 RAW 형식을 출력할 수 있다. 예를 들어 u-blox 수신기에서는 다음 메시지를 사용할 수 있다.

| 수신기 메시지 | 주요 내용 |
|---|---|
| `UBX-RXM-RAWX` | 위성별 의사거리, 반송파 위상, 도플러 등 관측값 |
| `UBX-RXM-SFRBX` | 위성 방송 항법 데이터 |

수신기 전용 Parser는 이를 프로젝트의 공통 모델인 `GnssRawEnvelope`로 변환한다.

```text
GnssRawEnvelope
├─ ObservationEpochMessage
├─ NavigationUpdateMessage
└─ ReceiverMetadataMessage
```

### 4.1 ObservationEpochMessage

매 측정 시각마다 생성되는 핵심 관측값이다.

```text
ObservationEpochMessage
├─ SchemaVersion
├─ MessageId
├─ EpochId
├─ GNSS 수신 시각
├─ 시간계
└─ 위성별 관측값
   ├─ 위성 번호
   ├─ 신호 종류
   ├─ 의사거리
   ├─ 반송파 위상
   ├─ 도플러
   ├─ C/N0
   └─ 추적 상태
```

### 4.2 NavigationUpdateMessage

위성 위치와 위성 시계 오차를 계산하기 위한 항법 데이터다. 매 Epoch마다 반복하지 않고 최초 수신 또는 궤도력 갱신 시 전송한다.

```text
NavigationUpdateMessage
├─ 위성 번호
├─ 방송 궤도력
├─ 위성 시계 보정값
├─ Issue of Data
├─ 시간계 보정값
└─ 전리층 보정 파라미터
```

### 4.3 ReceiverMetadataMessage

시험 환경 및 수신기 설정 정보다. 시험 시작 시 또는 설정 변경 시 전송한다.

```text
ReceiverMetadataMessage
├─ 수신기 종류
├─ 안테나 정보
├─ 펌웨어 버전
├─ 측정 주기
├─ 기준 좌표
├─ TestId
└─ 설정 버전
```

---

## 5. 기준 경로

기준 경로는 네트워크와 AFS 변환을 거치지 않은 공통 GNSS RAW를 직접 PVT Solver에 입력한다.

```text
GnssRawEnvelope
→ PVT Solver
→ ReferencePvt
```

```text
ReferencePvt
├─ EpochId
├─ 위도·경도·고도
├─ ECEF X/Y/Z
├─ 속도
├─ 수신기 시계 오차
├─ 사용 위성 수
└─ Solution 상태
```

가능하면 GNSS 수신기가 자체 계산한 NMEA 위치가 아니라 공통 GNSS RAW를 동일한 PVT Solver에 넣어 계산한 결과를 기준값으로 사용한다.

---

## 6. HDTN/BPv7 경로

HDTN 경로는 GNSS RAW 메시지를 BPv7 Payload로 넣고 HDTN을 통해 Ethernet으로 전달하는 경로다.

### 6.1 송신 처리

```text
GnssRawEnvelope
→ 직렬화
→ BPv7 Payload
→ BPv7 Bundle 생성
→ HDTN Outduct
→ TCPCLv4
→ TCP/IP
→ Ethernet
```

### 6.2 수신 처리

```text
Ethernet
→ TCP/IP
→ TCPCLv4
→ HDTN Induct
→ BPv7 Bundle 해제
→ Payload 추출
→ 역직렬화
→ GnssRawEnvelope 복원
→ PVT Solver
→ HDTN PVT
```

### 6.3 계층별 역할

| 계층 | 역할 |
|---|---|
| `GnssRawEnvelope` | 실제 전송할 GNSS 데이터 |
| 직렬화 | 객체를 전송 가능한 `byte[]`로 변환 |
| BPv7 | 데이터를 DTN Bundle로 포장 |
| HDTN | Bundle의 저장·전달·라우팅 처리 |
| TCPCLv4 | BPv7 Bundle을 TCP 연결로 운반 |
| TCP/IP | PC 사이의 네트워크 통신 |
| Ethernet | 실제 유선 전송 매체 |

### 6.4 HDTN 경로 검증 항목

- 원본 RAW와 HDTN 복원 RAW의 바이트·CRC·해시 비교
- 기준 PVT와 HDTN PVT 비교
- BPv7 Bundle ID 기록
- Bundle 전달 성공·실패·지연 측정
- 중복 Bundle 수신 여부 확인

---

## 7. AFS 프레임 경로

AFS 경로는 GNSS RAW를 AFS 사용자 정의 메시지에 넣고 AFS 프레임으로 채널 부호화한 뒤, 최종 6000개의 이진 심볼을 네트워크로 전송한다. 확산, 반송파 변조 및 I/Q 생성은 수행하지 않는다.

```text
GNSS RAW
→ 직렬화
→ Fragment 분할
→ AFS 사용자 정의 메시지
→ AFS Data Frame payload
→ CRC24
→ LDPC 부호화·천공
→ 인터리빙
→ SP + SB1 + SB2~SB4 조립
→ 6000심볼 프레임
→ 네트워크 전송
```

### 7.1 GNSS RAW Fragment 분할

직렬화한 GNSS RAW가 사용자 정의 메시지 한 개에 모두 들어가지 않으면 여러 Fragment로 분할한다.

```text
AfsCustomGnssFragment
├─ SchemaVersion
├─ MessageType
├─ TestId
├─ OriginalMessageId
├─ SequenceNumber
├─ FragmentIndex
├─ FragmentCount
├─ OriginalPayloadLength
├─ Payload
└─ CRC32
```

### 7.2 AFS 프레임 구성

```text
AFS Frame 6000심볼
├─ SP                 68심볼
├─ SB1                52심볼
└─ 인터리빙된 SB2~SB4 5880심볼
   ├─ SB2             2400심볼
   ├─ SB3             1740심볼
   └─ SB4             1740심볼
```

- SP는 고정 동기 패턴이다.
- SB1은 FID와 TOI를 52심볼로 부호화한다.
- SB2는 시간·궤도·시계정보 및 프로젝트에서 정의한 payload를 포함한다.
- SB3·SB4의 사용자 정의 영역에는 GNSS RAW Fragment를 배치한다.
- SB2~SB4에는 CRC24, LDPC 부호화, 천공 및 인터리빙을 적용한다.

사용자 정의 메시지의 정확한 배치는 적용할 AFS 규격의 확장 가능 영역을 확인한 뒤 프로젝트 규격으로 확정한다. 표준 필드에는 임의 값을 넣지 않고 규격상 유효한 시험값을 사용한다.

### 7.3 재사용할 오픈소스 기능

송신 측은 LANS-AFS-SIM에서 다음 기능을 분리해 사용한다.

- SP 및 SB1 생성
- SB2~SB4 payload 조립
- CRC-24Q 추가
- LDPC 부호화
- 천공
- SB2~SB4 인터리빙
- 최종 6000심볼 프레임 조립

PRN 확산, 코드·반송파 위상, 도플러, 잡음, ADC 양자화 및 I/Q 파일 생성은 사용하지 않는다.

---

## 8. AFS 프레임 네트워크 전송

초기 구현은 심볼 하나를 한 바이트의 `0x00` 또는 `0x01`로 표현한다. 따라서 프레임 payload는 6000바이트다. 동작 검증 후 필요하면 8심볼을 한 바이트로 패킹하여 750바이트로 줄일 수 있다.

```text
AfsFramePacket
├─ Magic
├─ ProtocolVersion
├─ TestId
├─ SequenceNumber
├─ PRN
├─ WN / ITOW / TOI
├─ SymbolFormat
├─ SymbolCount = 6000
├─ PayloadLength
├─ Frame Symbols
└─ Packet CRC32
```

AFS CRC24는 각 서브프레임의 오류 검출에 사용하고, Packet CRC32는 네트워크 헤더와 심볼 payload의 손상 검출에 사용한다.

초기 개발은 프레임 경계와 전달 신뢰성을 단순하게 관리할 수 있는 TCP를 사용한다. UDP가 필요하면 MTU를 넘지 않도록 프레임을 여러 패킷으로 분할하고 FragmentIndex, FragmentCount 및 순서번호를 추가한다.

```text
1단계: 메모리 내부 encode → decode
2단계: 파일 기반 6000심볼 저장 → 읽기 → decode
3단계: TCP 프레임 송수신
4단계: 필요 시 UDP 분할 전송과 손실·중복·역순 시험
```

---

## 9. 패킷과 프레임 계층 구분

```text
TCP/UDP Payload
└─ 프로젝트 전용 AFS 프레임 전송 헤더
   └─ 6000개의 AFS 이진 심볼
      └─ SP + SB1 + 채널 부호화된 SB2~SB4
         └─ AFS 사용자 정의 메시지
            └─ GNSS RAW Fragment
```

| 구성 요소 | 목적 |
|---|---|
| GNSS RAW Fragment | 실제 전달할 GNSS 응용 데이터 조각 |
| AFS 사용자 정의 메시지 | RAW Fragment를 AFS 프레임에 수용 |
| AFS Data Frame | CRC24·LDPC·천공·인터리빙이 적용된 6000심볼 |
| 프레임 전송 헤더 | 프레임 순서·형식·길이·무결성 관리 |
| TCP/UDP | 프레임 심볼을 네트워크로 운반 |

네트워크 전송 헤더는 AFS 표준의 일부가 아니라 시험장치가 6000심볼 프레임을 전달하기 위한 프로젝트 전용 규격이다.

---

## 10. AFS 수신 처리

수신 측에서는 송신 과정의 역순으로 처리한다.

```text
네트워크 패킷 수신
→ Packet CRC32 및 순서 검사
→ 6000심볼 프레임 복원
→ SP 동기 패턴 검사
→ SB1 FID·TOI 확인
→ SB2~SB4 디인터리빙
→ SB2/SB3/SB4 분리
→ 천공 위치에 erasure 삽입
→ LDPC 복호
→ CRC24 검사
→ 사용자 정의 메시지 추출
→ GNSS RAW Fragment 재조립
→ GnssRawEnvelope 역직렬화
→ PVT Solver
→ AFS PVT
```

PocketSDR-AFS에서는 디인터리빙, AFS LDPC 복호, CRC 검사 및 payload 추출 기능만 분리해 재사용한다. I/Q 입력, 신호 획득, PRN 상관, FLL/PLL/DLL 추적, C/N0 및 PVT용 AFS 신호 관측 처리는 사용하지 않는다.

### 송수신 단계별 비교

```text
송신: C001/C101 → C201~C404 → C501 → C502 → C601
전송: 송신 C601 ↔ 수신 C601
수신: C601 → C502 → C501 → C204/C304/C404
      → LDPC 복호 → C202/C302/C402 → C201/C301/C401
```

정상 전달 시 6000심볼, 디인터리빙 결과, LDPC 복호 결과, CRC24 및 최종 payload가 모두 일치해야 한다.

---
## 11. PC별 소프트웨어 구성

### 11.1 송신 PC

```text
GNSS Receiver Adapter
├─ GNSS RAW Collector
├─ Receiver-specific Parser
└─ Common GNSS RAW Mapper

Reference Processing
└─ PVT Solver → 기준 PVT

HDTN Transmitter
└─ Serializer
   → BPv7 Payload
   → HDTN
   → TCPCLv4
   → Ethernet

AFS Transmitter
└─ Serializer
   → RAW Fragmenter
   → AFS Custom Message Encoder
   → AFS Frame Encoder
   → CRC24·LDPC·천공·인터리빙
   → 6000-Symbol Packetizer
   → TCP/UDP Transport
```

### 11.2 수신 PC

```text
HDTN Receiver
└─ Ethernet
   → HDTN
   → BPv7 Payload
   → Deserializer
   → HDTN 복원 RAW
   → PVT Solver
   → HDTN PVT

AFS Receiver
└─ AFS Frame Network Receiver
   → Packet Validator / Reassembler
   → 6000-Symbol Frame Decoder
   → Deinterleaver / LDPC / CRC24
   → Custom Message Decoder
   → RAW Fragment Reassembler
   → Deserializer
   → AFS 복원 RAW
   → PVT Solver
   → AFS PVT

Test Validator
├─ RAW 무결성 비교
├─ PVT 오차 비교
├─ HDTN 전달 상태 확인
├─ 네트워크 패킷 손실·중복·역순 측정
├─ AFS 프레임 복호율 측정
└─ PASS / FAIL / Inconclusive 판정
```

---

## 12. 소프트웨어 계층 설계

```text
Presentation
└─ WPF 시험 제어·모니터링 화면

Application
├─ TestOrchestrator
├─ TransmitTestService
├─ ReceiveTestService
└─ ValidationService

Domain
├─ GnssRawEnvelope
├─ ObservationEpoch
├─ NavigationUpdate
├─ ReceiverMetadata
├─ PvtResult
└─ TestResult

Infrastructure
├─ UbxGnssAdapter
├─ HdtnAdapter
├─ AfsEncoderAdapter
├─ AfsDecoderAdapter
├─ AfsFrameTransportAdapter
└─ ResultRepository
```

Spring Boot 구조로 비유하면 다음과 같다.

| LNIS 검증 시스템 | Spring Boot 비유 |
|---|---|
| GNSS 수신기 Adapter | 외부 API Client |
| GNSS RAW Parser | 외부 DTO 변환기 |
| `GnssRawEnvelope` | 공통 Domain/DTO |
| PVT Solver | Domain Service |
| HDTN Adapter | 메시지 브로커 Producer/Consumer |
| BPv7 Bundle | 메시지 Envelope |
| AFS Frame Encoder | 특수 Serializer |
| AFS Frame Codec | 채널 부호화·복호 Service |
| AFS Frame Transport | 네트워크 Producer/Consumer |
| AFS Frame Decoder | LDPC·CRC 복호 Consumer |
| Validator | 검증 Service |
| 시험 결과 저장 | Repository |

---

## 13. 시험 판정 기준

| 검증 단계 | 비교 대상 | 목적 |
|---|---|---|
| HDTN RAW 무결성 | 원본 RAW ↔ HDTN RAW | BPv7 경로의 데이터 보존 확인 |
| AFS RAW 무결성 | 원본 RAW ↔ AFS RAW | AFS 전체 경로의 데이터 보존 확인 |
| HDTN PVT | 기준 PVT ↔ HDTN PVT | HDTN 복원 데이터의 계산 가능성 확인 |
| AFS PVT | 기준 PVT ↔ AFS PVT | AFS 복원 데이터의 계산 가능성 확인 |
| 네트워크 품질 | 송신·수신 패킷 수와 순서 | 손실·중복·역순 확인 |
| AFS 품질 | 송신·복호 프레임 수 및 CRC/LDPC 결과 | AFS 프레임 코덱 성공률 확인 |

### PASS

다음 조건을 모두 만족할 때 합격으로 판정한다.

- 필수 GNSS RAW 메시지가 모두 복원됨
- RAW CRC 또는 해시가 일치함
- PVT 계산에 성공함
- 위치·속도·시간 오차가 정의된 허용 범위 이내임
- 시험별 필수 네트워크 및 AFS 품질 기준을 만족함

### FAIL

다음 중 하나 이상이 발생하면 불합격으로 판정한다.

- CRC 또는 해시 불일치
- RAW 필드 값 불일치
- 필수 Bundle 또는 AFS Frame 복원 실패
- 원본 RAW에는 존재한 필수 관측값·항법 데이터가 HDTN 또는 AFS 전송·복원 과정에서 누락됨
- 기준 PVT는 성공했지만 HDTN 또는 AFS 복원 RAW의 PVT 계산이 실패함
- PVT 오차가 허용 범위를 초과함

### Inconclusive

다음과 같이 전송 경로의 합격·불합격 자체를 판단할 시험 조건이 충족되지 않은 경우다.

- 시험 시작 전 원본 RAW의 항법 데이터가 부족함
- 시험 시작 전 원본 RAW의 수신 위성 수가 부족함
- 외부 GNSS 환경으로 인해 기준 PVT 계산 조건 자체를 만족하지 못함
- 시험 설정이 유효하지 않음
- 기준 PVT 자체를 생성할 수 없음

`Inconclusive`는 단순한 전송 실패가 아니라 전송 경로를 평가할 유효한 기준 데이터가 없어 정상적인 판정을 내릴 수 없는 상태를 의미한다. 원본에는 필요한 데이터가 있었지만 특정 전송 경로에서만 누락되거나 손상된 경우는 `FAIL`이다.

---

## 14. 오픈소스 통합 전략

현재 프로토타입은 LANS-AFS-SIM과 PocketSDR-AFS를 별도의 실행 파일로 실행하고, 생성 파일과 로그를 통해 결과를 교환합니다. 이 방식은 초기 기능 검증에는 적합하지만 최종 시스템에서는 다음과 같은 한계가 있습니다.

- 외부 실행 파일의 설치 경로와 실행 환경에 의존
- 로그 문자열 형식이 변경되면 Parser가 동작하지 않을 수 있음
- 프레임 생성·복호의 단계별 결과를 구조화된 API로 받기 어려움
- 진행 상태, 오류 원인, 복호 결과를 구조화된 데이터로 직접 받기 어려움
- GNSS RAW 사용자 정의 메시지와 AFS Frame 확장이 외부 실행 파일 수정에 종속됨

따라서 최종 구현에서는 필요한 신호처리 코드를 프로젝트가 관리하는 네이티브 모듈로 분리하고, C# 애플리케이션에서 명확한 API로 호출하는 방식을 사용합니다.

권장 구조는 다음과 같습니다.

```text
WPF / C# 애플리케이션
→ 프로젝트 전용 C ABI → LnisAfsNative.dll 또는 liblnis_afs.so
├─ LANS-AFS-SIM 기반 AFS Frame 인코딩
└─ PocketSDR-AFS 기반 디인터리빙·LDPC·CRC 복호
```

CRC24, LDPC, 천공 및 인터리빙 코드는 원본과의 비트 단위 재현성이 중요하므로 C#으로 전면 재작성하지 않는다. 필요한 프레임 인코딩·복호 진입점만 얇은 C API로 노출하고 C#에서는 P/Invoke로 호출한다.

기능별 책임은 다음과 같이 구분합니다.

| 기능 | 구현 위치 |
|---|---|
| GNSS 공통 RAW 모델 | C# Domain |
| 직렬화·역직렬화 | C# |
| RAW Fragment 분할·재조립 및 CRC | C# |
| AFS 사용자 정의 메시지 모델 | C# |
| AFS Frame 내 사용자 메시지 삽입·추출 | 네이티브 모듈과 C# 경계 |
| AFS Frame 생성·채널 코딩 | LANS-AFS-SIM 기반 네이티브 모듈 |
| SP/SB1 생성·CRC24·LDPC·천공·인터리빙 | LANS-AFS-SIM 기반 네이티브 모듈 |
| AFS 프레임 패킷화·TCP/UDP 전송 | C# Infrastructure |
| 패킷 재조립·순서 정렬·중복 제거 | C# Infrastructure |
| 디인터리빙·천공 복원 | PocketSDR-AFS 기반 네이티브 모듈 |
| LDPC 및 AFS Frame 복호 | PocketSDR-AFS 기반 네이티브 모듈 |
| PVT 계산·RAW/PVT 비교·판정 | C# Domain/Application |

HDTN은 신호처리 라이브러리와 성격이 다릅니다. Bundle 저장·전달·라우팅과 TCPCLv4를 담당하는 독립 서비스이므로 프로세스 경계를 유지하고, BPv7 Payload 입력과 복원 Payload 출력에 대한 Adapter를 C# Infrastructure 계층에 구현합니다.

단계적 전환 순서는 다음과 같습니다.
```text
1. 현재 단계별 비교 로그로 기준 시험 벡터를 확보합니다.
2. LANS-AFS-SIM에서 프레임 생성·CRC24·LDPC·천공·인터리빙을 네이티브 라이브러리로 분리합니다.
3. PocketSDR-AFS에서 디인터리빙·LDPC 복호·CRC 검사를 네이티브 라이브러리로 분리합니다.
4. 프로젝트 전용 C ABI와 C# P/Invoke Adapter를 정의합니다.
5. 메모리 내부 encode → decode 회귀 시험을 구성합니다.
6. 6000심볼 프레임의 파일 및 네트워크 송수신을 검증합니다.
7. 기준 로그와 네이티브 API 결과가 비트 단위로 일치하는지 확인합니다.
```
오픈소스 코드를 프로젝트에 포함하거나 수정할 때는 다음 항목을 반드시 관리합니다.
```text
- 원본 프로젝트 이름, 출처 및 기준 Commit ID
- 원본 라이선스와 저작권 고지
- 프로젝트에서 사용한 파일과 제외한 파일 목록
- 수정한 내용과 수정 이유
- 원본 대비 시험 결과 및 회귀 테스트
- 배포 대상별 라이선스 의무사항
```
즉, 오픈소스 전체를 무분별하게 복사하거나 C#으로 다시 작성하지 않고, 필요한 신호처리 기능은 원본 언어로 유지한 채 프로젝트가 관리하는 네이티브 라이브러리로 통합합니다. 시험 제어, 네트워크 전송, 공통 RAW 모델, PVT 비교 및 판정은 C# 계층이 담당합니다.

## 15. 권장 구현 순서

1. GNSS 수신기 RAW 수집 및 Parser 구현
2. 공통 `GnssRawEnvelope`와 직렬화 형식 정의
3. 동일한 RAW를 사용하는 기준 PVT 경로 구현
4. HDTN/BPv7 Ethernet 송·수신 및 RAW 무결성 검증
5. AFS 송신 인코더를 네이티브 DLL로 분리
6. AFS 수신 디코더를 네이티브 DLL로 분리
7. 메모리 내부에서 6000심볼 encode → decode 검증
8. GNSS RAW Fragment 및 AFS 사용자 정의 메시지 규격 정의
9. AFS Frame 내부 사용자 정의 메시지 삽입·추출 구현
10. TCP 기반 6000심볼 프레임 송수신 구현
11. 필요 시 UDP 분할 전송과 손실·중복·역순 처리 구현
12. RAW/PVT 자동 비교 및 최종 시험 판정 구현
13. WPF 시험 제어·모니터링 화면과 결과 저장 기능 구현
---

## 16. 핵심 요약

본 시스템은 GNSS 수신기에서 추출한 관측값과 항법 데이터를 공통 RAW 모델로 정규화하고, 이를 기준 경로, HDTN/BPv7 Ethernet 경로, AFS 6000심볼 프레임 네트워크 경로로 각각 처리한다.

수신 측에서는 HDTN Bundle과 AFS 채널 부호화 프레임으로부터 동일한 GNSS RAW를 복원하고, 동일한 PVT Solver를 사용해 PVT를 다시 계산한다. 이후 원본·복원 RAW의 무결성과 기준·복원 PVT의 오차를 비교하여 전체 전송 및 프레임 코덱 경로의 성공 여부를 판정한다.

```text
GNSS RAW 정규화
→ 기준/HDTN/AFS 세 경로 처리
→ HDTN 및 AFS 수신·복원
→ RAW 무결성 비교
→ PVT 결과 비교
→ PASS / FAIL / Inconclusive
```
