# LNIS 네트워크 검증장치

## 1. 프로젝트 개요

본 프로젝트는 GNSS 수신기에서 수집한 동일한 GNSS RAW 데이터를 다음 세 경로로 처리한 후, 각 경로에서 복원된 RAW 데이터와 PVT 결과가 원본과 일치하는지 검증하는 종단 간 시험 시스템이다.

1. **기준 경로**: 네트워크를 거치지 않고 원본 GNSS RAW로 PVT 계산
2. **HDTN 경로**: BPv7 Bundle로 포장하여 Ethernet TCP/IP로 전송
3. **AFS 경로**: AFS 신호로 변환한 뒤 I/Q 샘플을 Wi-Fi UDP 브로드캐스트로 전송

최종적으로 다음 결과를 비교하여 `PASS`, `FAIL`, `Inconclusive`를 판정한다.

```text
원본 GNSS RAW
├─ 기준 경로 ───────────────────────────────→ 기준 PVT
├─ HDTN/BPv7 경로 ─→ 복원 GNSS RAW ────────→ HDTN PVT
└─ AFS I/Q 경로 ───→ 복원 GNSS RAW ────────→ AFS PVT

검증
├─ 원본 RAW ↔ HDTN 복원 RAW ↔ AFS 복원 RAW
└─ 기준 PVT ↔ HDTN PVT ↔ AFS PVT
```

> 이 시스템에서 Wi-Fi는 실제 AFS RF 전파 구간을 완전히 재현하는 것이 아니라, 디지털 AFS I/Q 샘플 전달 구간을 대체한다. 따라서 AFS 신호처리 소프트웨어의 종단 간 기능은 검증할 수 있지만 안테나, RF 출력, 수신 감도, 다중경로와 같은 실제 RF 특성은 검증하지 않는다.

### 1.1 현재 구현 상태

현재 코드는 최종 구조 전체가 아니라 다음 파일 기반 AFS 시험 경로를 먼저 검증하는 프로토타입이다.

```text
LANS-AFS-SIM
→ INT8X2 I/Q 파일 생성
→ 프로젝트 전용 TCP 파일 전송 및 SHA-256 검증
→ PocketSDR-AFS 실행
→ 수신 로그 파싱
→ 신호 획득·SB2 복호·PVT 위치/시간 판정
```

현재 TCP 파일 전송은 HDTN/BPv7 또는 Wi-Fi UDP 브로드캐스트를 구현한 것이 아니다. 공통 `GnssRawEnvelope`, 기준 PVT, HDTN/BPv7, AFS 사용자 정의 메시지, RAW Fragment, UDP I/Q 전송 및 RAW 3자 비교는 이 문서에서 정의하는 목표 설계이며 이후 구현 대상이다.

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
- UDP 패킷의 손실·중복·역순 발생 여부 측정
- AFS I/Q 스트림이 정상적으로 복원되는지 확인
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
         → 채널 코딩·확산·변조
         → 2-bit AFS I/Q 생성
         → UDP 패킷화
         → Wi-Fi 브로드캐스트
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

Wi-Fi UDP 브로드캐스트 수신
→ UDP 패킷 검사·순서 정렬
→ 지터 버퍼
→ 연속 AFS I/Q 스트림 복원
→ PocketSDR-AFS
→ AFS 신호 획득·추적·프레임 복호
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
├─ UDP 패킷 손실률·중복률·역순률
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

## 7. AFS 경로

AFS 경로는 GNSS RAW를 네트워크 바이트로 직접 보내지 않는다. RAW를 AFS 사용자 정의 메시지에 넣고 AFS Frame과 I/Q 신호로 변환한 후 전송한다.

```text
GNSS RAW
→ 직렬화
→ Fragment 분할
→ AFS 사용자 정의 메시지
→ AFS Data Frame
→ 채널 코딩·확산·변조
→ AFS I/Q
→ Wi-Fi UDP 브로드캐스트
```

### 7.1 GNSS RAW Fragment 분할

직렬화한 GNSS RAW가 AFS 사용자 정의 메시지 한 개에 모두 들어가지 않을 수 있으므로 여러 Fragment로 분할한다.

```text
원본 GNSS RAW 메시지
├─ Fragment 0
├─ Fragment 1
├─ Fragment 2
└─ Fragment 3
```

각 Fragment에는 재조립과 무결성 검증을 위한 정보가 필요하다.

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

### 7.2 AFS Data Frame

GNSS RAW Fragment는 AFS Frame 전체가 아니라 프레임 내부의 사용자 정의 메시지 영역에 넣을 프로젝트 응용 데이터다. 이 사용자 정의 메시지 구조는 현재 구현된 AFS 표준 메시지라고 단정하지 않으며, 적용할 AFS 규격에서 허용하는 확장 영역과 메시지 식별 방법을 확인한 뒤 프로젝트 전용 확장 규격으로 확정해야 한다.

```text
AFS Data Frame
├─ 필수 프레임 정보
│  ├─ 동기·식별 정보
│  ├─ 프레임 번호·시간 정보
│  └─ 오류 검출·정정 관련 정보
├─ 표준 항법정보
│  ├─ 시험에 필요한 규격상 유효값 사용
│  └─ 선택 메시지라면 미전송 가능
└─ 사용자 정의 메시지
   └─ GNSS RAW Fragment
```

표준 항법정보에는 임의의 값을 넣어서는 안 된다. 프레임 동기, 복호 또는 검증에 사용되는 필드는 규격에 맞는 유효한 시험값을 사용해야 한다.

LANS-AFS-SIM에서는 다음 기존 기능을 최대한 재사용하고 GNSS RAW 사용자 정의 메시지를 삽입하는 부분을 확장한다.

- 필수 AFS 프레임 생성
- 채널 코딩
- 확산 코드 적용
- 변조
- 복소 I/Q 샘플 생성

### 7.3 AFS I/Q 생성

```text
AFS Frame 비트
→ 오류 정정 부호화
→ 심볼 생성
→ 확산 코드 적용
→ 반송파·코드 위상 적용
→ 복소 I/Q 샘플 생성
```

복소 샘플은 다음 두 성분으로 구성된다.

```text
ComplexSample
├─ I: In-phase
└─ Q: Quadrature
```

현재 LANS-AFS-SIM의 `-b 2` 옵션은 I와 Q를 2비트 수준의 네 값으로 양자화하지만, 각 값을 `signed char` 한 바이트에 저장한다. PocketSDR-AFS의 `INT8X2`도 interleaved int8 I/Q 형식이므로 현재 파일에서는 복소 샘플 하나가 2바이트다.

| 샘플 형식 | 복소 샘플당 비트 | 12 Msps 기준 순수 전송률 |
|---|---:|---:|
| I 16비트 + Q 16비트 | 32비트 | 384 Mbps |
| 현재 `INT8X2` 파일·스트림 | 16비트 | 192 Mbps |
| 향후 I 2비트 + Q 2비트 packed 형식 | 4비트 | 48 Mbps |

표의 값은 UDP/IP, Ethernet 및 Wi-Fi 계층의 헤더와 전송 오버헤드를 제외한 순수 샘플 데이터율이다. 향후 4비트 packed 형식을 도입하려면 프로젝트 전용 패커·언패커와 PocketSDR-AFS 입력 변환을 구현하고, 기존 `INT8X2` 결과와 동등한 복호 성능을 회귀 시험으로 검증해야 한다.

---

## 8. Wi-Fi UDP 브로드캐스트

### 8.1 송신 처리

```text
연속 AFS I/Q
→ 2비트 I/Q 패킹
→ 일정 크기의 UDP Payload로 분할
→ 프로젝트 전용 I/Q 헤더 추가
→ 서브넷 브로드캐스트 주소로 전송
```

예시 네트워크 설정은 다음과 같다.

| 항목 | 예시 값 |
|---|---|
| 송신 PC IP | `192.168.0.10` |
| 서브넷 마스크 | `255.255.255.0` |
| 브로드캐스트 주소 | `192.168.0.255` |
| UDP 포트 | `50000` |
| 송신 목적지 | `192.168.0.255:50000` |

### 8.2 수신 처리

```text
UDP 50000 포트에 Bind
→ 브로드캐스트 패킷 수신
→ 패킷 CRC 검사
→ 순서번호 확인
→ 중복 제거
→ 지터 버퍼 처리
→ 누락 패킷 처리
→ 연속 AFS I/Q 스트림 복원
```

송신 측에서 수신 PC의 개별 IP를 지정할 필요는 없지만 다음 조건을 만족해야 한다.

- 송신·수신 PC가 같은 IP 서브넷 또는 허용된 동일 브로드캐스트 도메인에 있어야 함
- 수신 프로그램이 지정된 UDP 포트에 Bind되어 있어야 함
- Windows 방화벽에서 해당 UDP 포트 인바운드가 허용되어야 함
- 공유기의 AP Isolation 또는 클라이언트 격리가 꺼져 있어야 함
- 공유기 또는 무선 AP가 브로드캐스트 전송을 차단하지 않아야 함

### 8.3 I/Q UDP 패킷

```text
IqUdpPacket
├─ Magic
├─ ProtocolVersion
├─ TestId
├─ StreamId
├─ SequenceNumber
├─ FirstSampleIndex
├─ SampleCount
├─ SampleFormat
├─ SampleRate
├─ PayloadLength
├─ I/Q Payload
└─ CRC32
```

`FirstSampleIndex`는 패킷 손실 시 누락된 샘플 수를 계산하고 이후 샘플의 시간 위치를 유지하는 데 사용한다. 필요한 경우 누락 구간에 0 또는 정의된 무효 샘플을 삽입할 수 있지만, 연속 손실이 크면 AFS 추적과 프레임 복호는 실패할 수 있다.

### 8.4 브로드캐스트의 특성

Wi-Fi 브로드캐스트는 여러 수신기에 한 번에 전달할 수 있지만 일반적인 유니캐스트와 달리 개별 MAC ACK와 자동 재전송을 기대하기 어렵다.

- 패킷 손실 가능성이 상대적으로 큼
- 송신자가 각 수신기의 수신 성공 여부를 직접 알 수 없음
- 공유기가 브로드캐스트를 낮은 기본 전송률로 보낼 수 있음
- 대량 I/Q 트래픽이 무선 채널 사용 시간을 많이 차지할 수 있음

최종 요구사항은 브로드캐스트로 구현하되, 개발 초기에는 다음 순서로 문제를 분리해 검증하는 것이 좋다.

```text
1단계: 파일 기반
LANS-AFS-SIM → I/Q 파일 → PocketSDR-AFS

2단계: UDP 유니캐스트
송신 PC → 특정 수신 PC

3단계: UDP 브로드캐스트
송신 PC → 같은 서브넷의 여러 수신 PC

4단계: 손실·지터 조건 시험
패킷 손실률, 프레임 복호율, PVT 성공률 측정
```

---

## 9. 패킷과 프레임 계층 구분

AFS Frame과 UDP I/Q 패킷은 서로 다른 계층이다.

```text
UDP Payload
├─ 프로젝트 전용 I/Q 전송 헤더
└─ AFS I/Q 샘플
   └─ 신호 안에 AFS Data Frame이 변조되어 있음
      └─ AFS 사용자 정의 메시지
         └─ GNSS RAW Fragment
```

| 구성 요소 | 계층 및 목적 |
|---|---|
| GNSS RAW Fragment | 실제 전달하려는 GNSS 응용 데이터 조각 |
| AFS 사용자 정의 메시지 | RAW Fragment를 AFS 규격 내부에 수용 |
| AFS Data Frame | AFS 신호로 변조할 프레임 데이터 |
| AFS I/Q | AFS Frame을 디지털 신호 샘플로 표현 |
| I/Q UDP 헤더 | I/Q 샘플의 순서·형식·무결성 관리 |
| UDP/IP | I/Q 패킷을 Wi-Fi 네트워크로 운반 |

프로젝트 전용 I/Q 헤더는 AFS 표준의 일부가 아니며, AFS I/Q를 Wi-Fi로 전달하기 위해 추가하는 네트워크 계층이다.

---

## 10. PocketSDR-AFS 수신 처리

수신 측에서는 송신 과정의 역순으로 처리한다.

```text
Wi-Fi UDP 수신
→ I/Q 패킷 검사·순서 정렬
→ 연속 I/Q 스트림 복원
→ PocketSDR-AFS 입력
→ AFS 신호 획득
→ 코드·반송파 추적
→ 프레임 동기
→ 채널 복호
→ AFS Data Frame 추출
→ 사용자 정의 메시지 추출
→ GNSS RAW Fragment 재조립
→ GnssRawEnvelope 역직렬화
→ PVT Solver
→ AFS PVT
```

### 재사용 대상

- AFS 신호 획득
- 코드 추적
- 반송파 추적
- 프레임 동기
- LDPC 등 오류 정정 복호
- 항법 프레임 비트 추출

### 추가 구현 대상

- UDP I/Q 실시간 입력
- I/Q 패킷 순서 정렬 및 지터 버퍼
- 누락·중복 패킷 처리
- AFS 사용자 정의 메시지 식별 및 추출
- GNSS RAW Fragment 재조립
- 공통 `GnssRawEnvelope` 역직렬화
- RAW 및 PVT 비교
- 시험 결과 판정·저장·표시

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
   → LANS-AFS-SIM 기반 I/Q Generator
   → UDP I/Q Packetizer
   → Wi-Fi Broadcast
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
└─ Wi-Fi UDP Receiver
   → Jitter Buffer
   → I/Q Stream Reassembler
   → PocketSDR-AFS
   → AFS Frame Decoder
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
├─ UDP 패킷 손실률 측정
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
├─ PocketSdrAdapter
├─ UdpBroadcastAdapter
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
| AFS I/Q Generator | 물리계층 변환 Service |
| UDP Broadcaster | 네트워크 Producer |
| PocketSDR-AFS | 신호 Decoder/Consumer |
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
| UDP 품질 | 송신·수신 패킷 수와 순서 | Wi-Fi 손실·중복·역순 확인 |
| AFS 품질 | 송신·복호 프레임 수 | AFS 신호처리 성공률 확인 |

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
- 대용량 I/Q 파일을 디스크에 생성하고 다시 읽어야 함
- 진행 상태, 오류 원인, 복호 결과를 구조화된 데이터로 직접 받기 어려움
- GNSS RAW 사용자 정의 메시지와 AFS Frame 확장이 외부 실행 파일 수정에 종속됨

따라서 최종 구현에서는 필요한 신호처리 코드를 프로젝트가 관리하는 네이티브 모듈로 분리하고, C# 애플리케이션에서 명확한 API로 호출하는 방식을 사용합니다.

권장 구조는 다음과 같습니다.

```text
WPF / C# 애플리케이션
→ 프로젝트 전용 C ABI→ LnisAfsNative.dll 또는 liblnis_afs.so
├─ LANS-AFS-SIM 기반 AFS Frame·I/Q 생성
└─ PocketSDR-AFS 기반 신호 획득·추적·복호
```

C/C++ 신호처리 코드를 C#으로 전면 재작성하지는 않습니다. FFT, LDPC, 신호 획득, 코드·반송파 추적 및 변조 코드는 성능과 수치 재현성에 민감하기 때문에 원본 언어와 알고리즘을 최대한 유지합니다. 대신 프로젝트에 필요한 진입점만 얇은 C API로 노출하고 C#에서는 P/Invoke로 호출합니다.

기능별 책임은 다음과 같이 구분합니다.

| 기능 | 구현 위치 |
|---|---|
| GNSS 공통 RAW 모델 | C# Domain |
| 직렬화·역직렬화 | C# |
| RAW Fragment 분할·재조립 및 CRC | C# |
| AFS 사용자 정의 메시지 모델 | C# |
| AFS Frame 내 사용자 메시지 삽입·추출 | 네이티브 모듈과 C# 경계 |
| AFS Frame 생성·채널 코딩 | LANS-AFS-SIM 기반 네이티브 모듈 |
| 확산·변조·I/Q 생성 | LANS-AFS-SIM 기반 네이티브 모듈 |
| UDP 패킷화·브로드캐스트 | C# Infrastructure |
| 지터 버퍼·순서 정렬·중복 제거 | C# Infrastructure |
| AFS 신호 획득·코드/반송파 추적 | PocketSDR-AFS 기반 네이티브 모듈 |
| LDPC 및 AFS Frame 복호 | PocketSDR-AFS 기반 네이티브 모듈 |
| PVT 계산·RAW/PVT 비교·판정 | C# Domain/Application |

HDTN은 신호처리 라이브러리와 성격이 다릅니다. Bundle 저장·전달·라우팅과 TCPCLv4를 담당하는 독립 서비스이므로 프로세스 경계를 유지하고, BPv7 Payload 입력과 복원 Payload 출력에 대한 Adapter를 C# Infrastructure 계층에 구현합니다.

단계적 전환 순서는 다음과 같습니다.
```text
1. 현재 외부 실행 파일 방식으로 기준 출력과 시험 벡터를 확보합니다.
2. LANS-AFS-SIM에서 Frame·I/Q 생성에 필요한 부분을 네이티브 라이브러리로 분리합니다.
3. PocketSDR-AFS에서 획득·추적·Frame 복호에 필요한 부분을 네이티브 라이브러리로 분리합니다.
4. 안정된 프로젝트 전용 C ABI와 C# P/Invoke Adapter를 정의합니다.
5. 파일 기반 I/Q 교환을 메모리 버퍼 또는 스트림 기반 처리로 전환합니다.
6. 기존 실행 파일 결과와 네이티브 API 결과를 동일 입력으로 비교하여 수치 및 복호 결과가 일치하는지 검증합니다.
7. 검증이 끝난 기능부터 외부 실행 파일 의존성을 제거합니다.
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
5. LANS-AFS-SIM과 PocketSDR-AFS의 파일 기반 연동 검증
6. GNSS RAW Fragment 및 AFS 사용자 정의 메시지 규격 정의
7. AFS Frame 내부 사용자 정의 메시지 삽입·추출 구현
8. 2비트 I/Q 패킹과 UDP 패킷 규격 구현
9. UDP 유니캐스트로 I/Q 전송 경로 검증
10. Wi-Fi UDP 브로드캐스트로 확장
11. 지터 버퍼와 손실·중복·역순 처리 구현
12. RAW/PVT 자동 비교 및 최종 시험 판정 구현
13. WPF 시험 제어·모니터링 화면과 결과 저장 기능 구현

---

## 16. 핵심 요약

본 시스템은 GNSS 수신기에서 추출한 관측값과 항법 데이터를 공통 RAW 모델로 정규화하고, 이를 기준 경로, HDTN/BPv7 Ethernet 경로, AFS I/Q Wi-Fi 브로드캐스트 경로로 각각 처리한다.

수신 측에서는 HDTN Bundle과 AFS I/Q 신호로부터 동일한 GNSS RAW를 복원하고, 동일한 PVT Solver를 사용해 PVT를 다시 계산한다. 이후 원본·복원 RAW의 무결성과 기준·복원 PVT의 오차를 비교하여 전체 전송 및 신호처리 경로의 성공 여부를 판정한다.

```text
GNSS RAW 정규화
→ 기준/HDTN/AFS 세 경로 처리
→ HDTN 및 AFS 수신·복원
→ RAW 무결성 비교
→ PVT 결과 비교
→ PASS / FAIL / Inconclusive
```
