1. 전체 시스템 목적

최종적으로 다음 두 가지를 검증합니다.

데이터 무결성 검증
송신 전 GNSS RAW
=
HDTN 경로를 거쳐 복원된 GNSS RAW
=
AFS 경로를 거쳐 복원된 GNSS RAW

직렬화된 원본 바이트나 해시값을 비교합니다.

PVT 결과 검증
기준 RAW → 기준 PVT
HDTN 복원 RAW → HDTN PVT
AFS 복원 RAW → AFS PVT

세 결과의 위치·속도·시간이 허용 오차 안에서 같은지 확인합니다.

즉, 단순히 “데이터가 수신됐다”만 보는 것이 아니라:

RAW 데이터가 손상되지 않았는가
PVT 계산에 필요한 관측값과 항법 데이터가 모두 복원됐는가
복원된 RAW로 동일한 PVT가 나오는가
AFS I/Q 신호가 정상적으로 생성·전송·복호됐는가

까지 확인합니다.

2. 송신 PC 전체 구조
   GNSS 수신기
   → 수신기 전용 RAW 수집
   → 수신기 전용 Parser
   → 공통 GNSS RAW 모델로 변환

GNSS 수신기는 제조사마다 출력 형식이 다를 수 있습니다.

예를 들어 u-blox를 사용한다면:

UBX-RXM-RAWX
→ 위성별 관측값

UBX-RXM-SFRBX
→ 방송 항법 데이터

Parser가 이를 프로젝트의 공통 데이터 구조로 바꿉니다.

공통 GNSS RAW 모델

논리적으로는 다음 세 종류로 분리하는 것이 적합합니다.

GnssRawEnvelope
├─ ObservationEpochMessage
├─ NavigationUpdateMessage
└─ ReceiverMetadataMessage
ObservationEpochMessage

매 측정 시각마다 발생하는 관측값입니다.

ObservationEpochMessage
├─ Epoch ID
├─ GNSS 수신 시각
├─ GPS/Galileo 등 시간계
└─ 위성별 관측값
├─ 위성 번호
├─ 신호 종류
├─ 의사거리
├─ 반송파 위상
├─ 도플러
├─ C/N0
└─ 추적 상태

PVT Solver가 실제 위치를 계산할 때 사용하는 핵심 측정값입니다.

NavigationUpdateMessage

위성 위치와 시계 오차 계산에 필요한 항법 데이터입니다.

NavigationUpdateMessage
├─ 위성 번호
├─ 방송 궤도력
├─ 위성 시계 보정값
├─ Issue of Data
├─ 시간계 보정
└─ 전리층 보정 파라미터

관측값처럼 매 Epoch마다 반복할 필요는 없습니다.

관측값
→ 1 Hz, 5 Hz, 10 Hz 등 매 Epoch 생성

항법 데이터
→ 최초 수신 시 생성
→ 새로운 궤도력 수신 시 갱신
ReceiverMetadataMessage

시험 환경 및 수신기 설정입니다.

ReceiverMetadataMessage
├─ 수신기 종류
├─ 안테나 정보
├─ 펌웨어 버전
├─ 측정 주기
├─ 기준 좌표
├─ 시험 ID
└─ 설정 버전

시험 시작이나 수신기 설정이 바뀌었을 때 전송하면 됩니다.

3. 기준 경로

기준 경로는 네트워크를 전혀 거치지 않은 원본 RAW로 PVT를 계산합니다.

공통 GNSS RAW
→ PVT Solver
→ 기준 PVT

기준 PVT에는 다음과 같은 값이 포함됩니다.

ReferencePvt
├─ Epoch ID
├─ 위도
├─ 경도
├─ 고도
├─ X/Y/Z 좌표
├─ 속도
├─ 수신기 시계 오차
├─ 사용 위성 수
└─ Solution 상태

이 결과가 HDTN과 AFS 경로의 비교 기준입니다.

중요한 점은 GNSS 수신기가 이미 계산한 NMEA 위치를 기준으로 삼는 것이 아니라, 가능하면 공통 RAW를 동일한 PVT Solver에 넣어 계산해야 한다는 것입니다.

그래야 차이가 발생했을 때 원인을 다음으로 좁힐 수 있습니다.

PVT 알고리즘 차이 ❌
네트워크 전송·복원 차이 ✅
4. HDTN/BPv7 경로

HDTN 경로는 GNSS RAW를 데이터 메시지 그대로 Ethernet을 통해 전송하는 경로입니다.

송신 PC
공통 GNSS RAW
→ 직렬화
→ BPv7 Payload
→ BPv7 Bundle 생성
→ HDTN Outduct
→ TCPCLv4
→ TCP/IP
→ Ethernet

각 계층의 역할은 다음과 같습니다.

계층	역할
GnssRawEnvelope	실제 전송할 GNSS 데이터
직렬화	객체를 byte[]로 변환
BPv7	데이터를 DTN Bundle로 포장
HDTN	Bundle 저장·전달·라우팅
TCPCLv4	Bundle을 TCP로 운반
TCP/IP	PC 사이 네트워크 통신
Ethernet	실제 유선망

즉:

GNSS RAW가 BPv7 Payload가 됨
→ Payload를 포함한 Bundle이 만들어짐
→ HDTN이 Ethernet TCP/IP로 Bundle을 전달
수신 PC
Ethernet
→ TCP/IP
→ TCPCLv4
→ HDTN Induct
→ BPv7 Bundle 해제
→ Payload 추출
→ 역직렬화
→ 공통 GNSS RAW 복원
→ PVT Solver
→ HDTN PVT
HDTN 경로 검증
원본 RAW 해시
vs
HDTN 복원 RAW 해시

그리고:

기준 PVT
vs
HDTN PVT

를 비교합니다.

정상적인 TCP 경로라면 손상된 데이터를 받기보다는 연결 지연이나 전달 실패로 나타날 가능성이 큽니다. 하지만 BPv7의 Bundle ID, 전달 지연, 중복 수신 여부도 별도로 기록해야 합니다.

5. AFS 경로

AFS 경로는 HDTN보다 훨씬 복잡합니다.

HDTN은 RAW 바이트를 네트워크로 직접 운반하지만, AFS 경로는 RAW를 작은 조각으로 분리한 뒤:

AFS 메시지
→ AFS 프레임
→ AFS I/Q 신호

로 변환하고, 수신 측에서 이 과정을 모두 반대로 수행합니다.

송신 PC
공통 GNSS RAW
→ 직렬화
→ 작은 Fragment로 분할
→ AFS 사용자 정의 메시지 생성
→ AFS Data Frame 구성
→ 채널 코딩·확산·변조
→ 디지털 AFS I/Q 생성
→ I/Q 네트워크 패킷화
→ Wi-Fi UDP 브로드캐스트
5.1 RAW 분할이 필요한 이유

직렬화한 GNSS RAW가 AFS 사용자 정의 메시지 한 개에 들어갈 수 있다는 보장이 없습니다.

따라서 다음처럼 나눕니다.

원본 GNSS RAW 1개
├─ Fragment 0
├─ Fragment 1
├─ Fragment 2
└─ Fragment 3

각 조각에는 재조립 정보가 필요합니다.

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

FragmentIndex와 FragmentCount가 있어야 수신 측에서 원본 메시지를 재조립할 수 있습니다.

5.2 AFS Data Frame의 의미

프로젝트에서 사용하는 “AFS Data Frame”은 단순히 GNSS RAW 조각만 의미하는 것이 아닙니다.

개념적으로 다음과 같이 이해하면 됩니다.

AFS Data Frame
├─ 프레임 동기·식별 정보
├─ 프레임 번호·시간 관련 정보
├─ 오류 정정용 정보
├─ AFS 표준 항법 메시지 영역
└─ 사용자 정의 메시지 영역
└─ GNSS RAW Fragment

즉, GNSS RAW 조각은 AFS 프레임 전체가 아니라 프레임 내부 사용자 정의 메시지 영역에 들어가는 응용 데이터입니다.

현재 프로젝트에서 의도하는 프레임 구성은 다음과 같습니다.

AFS Frame
├─ 필수 프레임 정보
│  └─ 규격에 맞는 정상값 생성
│
├─ 표준 항법정보
│  ├─ 시험에 필요한 유효값 사용
│  └─ 선택 가능한 메시지라면 미전송 가능
│
└─ 사용자 정의 메시지
└─ GNSS RAW Fragment

여기서 “표준 항법정보를 아무 값이나 넣는다”는 뜻은 아닙니다. 해당 필드가 프레임 복호나 동기·검증에 사용된다면 규격상 유효한 시험값이 필요합니다.

LANS-AFS-SIM의 기존 프레임 생성 코드를 기반으로:

기존 필수 프레임 구조
채널 코딩
확산
변조
I/Q 생성

은 최대한 재사용하고, GNSS RAW 사용자 정의 메시지를 넣는 부분을 확장 구현하게 됩니다.

5.3 AFS I/Q 생성

AFS Frame은 네트워크에 바로 전송되는 데이터가 아니라, 먼저 신호 샘플로 바뀝니다.

AFS Frame의 비트
→ 오류 정정 부호화
→ 심볼 생성
→ 확산 코드 적용
→ 반송파 및 코드 위상 적용
→ 복소 I/Q 샘플 생성

I/Q는 신호를 디지털 숫자로 표현한 것입니다.

ComplexSample
├─ I: In-phase
└─ Q: Quadrature

이번 프로젝트에서는 실제 RF 장비 대신 I/Q 샘플을 Wi-Fi로 전송합니다.

실제 AFS RF 송신
안테나 → 우주/무선 채널 → 안테나

이번 시험
AFS I/Q → UDP/IP → Wi-Fi → AFS I/Q

따라서 정확한 표현은:

Wi-Fi 브로드캐스트를 이용해 실제 RF 구간 대신 디지털 AFS I/Q 샘플 전달 구간을 구성한다.

입니다.

6. Wi-Fi UDP 브로드캐스트 경로
   송신 PC
   연속 AFS I/Q
   → 2비트 형식 패킹
   → 일정 크기의 UDP Payload로 분할
   → 프로젝트 I/Q 헤더 추가
   → 브로드캐스트 주소:포트로 전송

예:

송신 PC IP        : 192.168.0.10
서브넷 마스크     : 255.255.255.0
브로드캐스트 주소 : 192.168.0.255
UDP 포트          : 50000

송신 목적지는 다음입니다.

192.168.0.255:50000
수신 PC
UDP 50000 포트에 Bind
→ 브로드캐스트 패킷 수신
→ 패킷 CRC 검사
→ 순서번호 확인
→ 중복 제거
→ 지터 버퍼
→ 누락 패킷 처리
→ 연속 I/Q 스트림 복원

수신 PC의 개별 IP를 송신 측에서 지정할 필요는 없습니다. 다만 수신 프로그램은 반드시 동일한 UDP 포트에 Bind해야 합니다.

I/Q UDP 패킷

최소한 다음 정보가 필요합니다.

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

이 헤더는 AFS 표준 프레임이 아니라, AFS I/Q를 Wi-Fi로 안정적으로 운반하기 위한 프로젝트 전용 네트워크 헤더입니다.

계층을 혼동하면 안 됩니다.

UDP Payload
├─ 프로젝트 I/Q 전송 헤더
└─ AFS I/Q 샘플
└─ 신호 안에 AFS Frame이 변조되어 있음
└─ AFS 사용자 정의 메시지
└─ GNSS RAW Fragment
7. 수신 PC의 AFS 처리

수신 측에서는 송신 측의 역순으로 처리합니다.

Wi-Fi UDP 브로드캐스트 수신
→ UDP 패킷 검사
→ I/Q 패킷 순서 정렬
→ 연속 I/Q 스트림 복원
→ PocketSDR-AFS 입력
→ AFS 신호 획득
→ 코드·반송파 추적
→ 프레임 동기
→ 채널 복호
→ AFS Data Frame 추출
→ 사용자 정의 메시지 추출
→ GNSS RAW Fragment 수집
→ 원본 GNSS RAW 재조립
→ 역직렬화
→ PVT Solver
→ AFS PVT

PocketSDR-AFS에서 주로 재사용할 부분은 다음입니다.

AFS 신호 획득
코드 추적
반송파 추적
프레임 동기
LDPC 등 오류 정정 복호
항법 프레임 비트 추출

별도 확장해야 하는 부분은 다음입니다.

UDP I/Q 실시간 입력
I/Q 순서 정렬 및 지터 버퍼
사용자 정의 메시지 식별
GNSS RAW Fragment 재조립
공통 GnssRawEnvelope 역직렬화
PVT 비교 및 시험 판정
8. 최종 PC 2대 배치
   송신 PC
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
수신 PC
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
├─ HDTN 전달 상태
├─ UDP 패킷 손실률
├─ AFS 프레임 복호율
└─ PASS / FAIL / Inconclusive
9. 비교 및 판정 기준

시험은 한 가지 결과만 보고 판정하면 안 됩니다.

검증 단계	비교 대상	목적
HDTN RAW 무결성	원본 RAW ↔ HDTN RAW	BPv7 경로 데이터 보존 확인
AFS RAW 무결성	원본 RAW ↔ AFS RAW	AFS 전체 경로 데이터 보존 확인
HDTN PVT	기준 PVT ↔ HDTN PVT	HDTN 복원 데이터의 계산 가능성 확인
AFS PVT	기준 PVT ↔ AFS PVT	AFS 복원 데이터의 계산 가능성 확인
UDP 품질	송신·수신 패킷 수	Wi-Fi 손실·중복·역순 확인
AFS 품질	송신·복호 프레임 수	AFS 신호처리 성공률 확인
PASS 예시
필수 RAW 메시지 모두 복원
AND RAW CRC 또는 해시 일치
AND PVT 계산 성공
AND 위치·속도·시간 오차가 허용 범위 이내
FAIL 예시
CRC 오류
또는 RAW 값 불일치
또는 필수 AFS 프레임 복호 실패
또는 PVT 오차 허용 범위 초과
Inconclusive 예시
항법 데이터 부족
수신 위성 수 부족
PVT 계산 조건 미충족
시험 설정 자체가 유효하지 않음

Inconclusive는 전송 실패라기보다 정상적인 합격·불합격 판단 자체가 불가능한 상태입니다.

10. Spring Boot 구조로 비유하면

사용자에게 익숙한 구조로 바꾸면 다음과 같습니다.

이번 시스템	Spring Boot 비유
GNSS 수신기 Adapter	외부 API Client
GNSS RAW Parser	외부 DTO 변환기
GnssRawEnvelope	공통 Domain/DTO
PVT Solver	Domain Service
HDTN Adapter	메시지 브로커 Producer/Consumer
BPv7 Bundle	메시지 Envelope
AFS Frame Encoder	특수 Serializer
AFS I/Q Generator	물리계층 변환 서비스
UDP Broadcaster	네트워크 Producer
PocketSDR-AFS	신호 Decoder/Consumer
Validator	검증 Service
시험 결과 저장	Repository

소프트웨어 계층은 다음처럼 설계할 수 있습니다.

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
├─ PvtResult
└─ TestResult

Infrastructure
├─ UbxGnssAdapter
├─ HdtnAdapter
├─ AfsEncoderAdapter
├─ PocketSdrAdapter
├─ UdpBroadcastAdapter
└─ ResultRepository
11. 가장 정확한 최종 흐름
    [송신 PC]

GNSS 수신기
→ RAWX/SFRBX 등 수집
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
│     → HDTN/TCPCLv4
│     → Ethernet
│
└─ AFS 경로
└─ 직렬화
→ RAW Fragment 분할
→ AFS 사용자 정의 메시지
→ AFS Data Frame
→ 채널 코딩·확산·변조
→ 2-bit AFS I/Q
→ UDP 패킷화
→ Wi-Fi 브로드캐스트


[수신 PC]

Ethernet
→ HDTN/BPv7 해제
→ GnssRawEnvelope 복원
→ PVT Solver
→ HDTN PVT

Wi-Fi
→ UDP 패킷 수신·정렬
→ 연속 AFS I/Q 복원
→ PocketSDR-AFS
→ AFS Data Frame 복호
→ 사용자 정의 메시지 추출
→ RAW Fragment 재조립
→ GnssRawEnvelope 복원
→ PVT Solver
→ AFS PVT


[검증]

원본 RAW ↔ HDTN 복원 RAW ↔ AFS 복원 RAW
기준 PVT ↔ HDTN PVT ↔ AFS PVT
→ PASS / FAIL / Inconclusive

한 문장으로 요약하면:

GNSS 수신기에서 추출한 관측값과 항법 데이터를 공통 RAW 모델로 정규화하고, 이를 기준 경로·HDTN/BPv7 Ethernet 경로·AFS I/Q Wi-Fi 브로드캐스트 경로로 각각 처리한 뒤, 수신 측에서 복원된 RAW와 PVT를 원본 및 기준 PVT와 비교하는 종단 간 네트워크 검증 시스템입니다.