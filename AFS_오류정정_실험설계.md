# AFS Frame / UDP 전송 / 오류정정 실험 정리

## 현재 프로그램 적용 상태

현재 LNIS AFS Validator의 공식 운용 화면은 송신부와 수신부 두 개다. 송신부에서 Test A~E와 시험 조건을 선택하고, 수신부는 `SessionStart`에서 시험 종류·오류 수·Seed·SP 손상 간격·UDP Drop 조건을 자동으로 받아 결과를 표시한다. 별도 오류정정 시험 창은 사용하지 않는다.

| 시험 | 현재 종단 간 실행 방식 |
|---|---|
| Test A | 정상 AFS 프레임을 UDP로 송신하고 RAW 무결성을 확인한다. |
| Test B | 실제 `capture.graw`로 만든 프레임에 Random 심볼 오류를 주입해 송신한다. |
| Test C | 실제 `capture.graw`로 만든 프레임에 Burst 심볼 오류를 주입해 송신한다. |
| Test D | 지정 간격 프레임의 SP를 훼손해 송신하고, 수신부가 payload 연속 심볼에서 다음 정상 SP를 재탐색한다. |
| Test E | 정상 프레임의 UDP 데이터그램 복제본을 Seed 기반으로 제거한다. |

Test B/C는 최종 RAW 무결성, Test D는 모든 논리 프레임 수신과 손상되지 않은 예상 프레임 수만큼의 SP 재탐색·복호, Test E는 실제 복원 결과를 기준으로 판정한다. 아래 내용은 RF 심볼 오류와 UDP 패킷 손실을 구분하기 위한 설계 배경이다.

AFS Frame은 1 Frame 기준으로 다음과 같은 구조를 가진다.

```text
총 6000 symbols
Frame duration = 12초

┌────────┬────────┬─────────┬─────────┬─────────┐
│ Sync   │ SB1    │ SB2     │ SB3     │ SB4     │
│ 68     │ 52     │ 2400    │ 1740    │ 1740    │
└────────┴────────┴─────────┴─────────┴─────────┘
```

---

## 1. 목적

본 문서는 AFS Frame을 UDP 방식으로 전송하는 실험을 설계할 때 고려해야 할 사항을 정리한 것이다.

핵심은 다음 두 가지를 구분하는 것이다.

1. **AFS 자체의 RF 오류 정정 성능**
2. **UDP 네트워크 전송 특성에 따른 패킷 손실**

AFS는 원래 RF 환경에서 발생하는 잡음, 감쇠, 간섭 등에 따른 심볼 오류를 고려하여 설계된 신호 구조이다.  
반면 UDP 패킷 손실은 AFS가 본래 상정한 오류 모델과 다르기 때문에, 실험 목적을 명확히 구분해서 설계해야 한다.

---

## 2. RF 방식과 UDP 방식의 차이

### 2.1 RF 방식 - I/Q 신호

실제 RF 환경에서는 여러 PRN 신호가 하나의 연속된 I/Q 파형에 합성되어 전달된다.

RF 환경에서 발생하는 대표적인 문제는 다음과 같다.

- 잡음
- 감쇠
- 간섭
- 신호 세기 저하
- 특정 구간의 심볼 판정 오류
- 비트 반전 또는 심볼 오류

즉, RF 환경에서는 데이터 블록 하나가 통째로 사라진다기보다는 **연속 신호 품질이 나빠지면서 일부 심볼이 잘못 판정되는 형태**가 일반적이다.

AFS Frame은 이러한 RF 환경에서의 오류에 대응하기 위해 다음과 같은 구조를 사용한다.

- Synchronization Pattern(SP)
- CRC-24Q
- LDPC
- Interleaving
- Subframe 구조

이 중 LDPC와 Interleaving은 오류 정정 및 연속 오류 분산에 중요한 역할을 한다.

### 2.2 UDP 방식 - 패킷 전송

UDP에서 패킷은 네트워크에서 한 번에 독립적으로 전달되는 데이터 덩어리이다.

UDP는 다음을 보장하지 않는다.

- 전달 보장
- 재전송
- 순서 보장
- 중복 제거

따라서 다음과 같은 상황에서 패킷 손실이 발생할 수 있다.

- 네트워크 혼잡
- Wi-Fi 간섭
- 신호 세기 저하
- 송신 버퍼 초과
- 수신 버퍼 초과
- OS 또는 애플리케이션 처리 지연
- AP 또는 네트워크 장비 처리 문제

따라서 UDP에서 발생하는 오류는 RF에서의 일부 심볼 오류와 다르게 **패킷 단위의 데이터가 통째로 사라질 수 있는 형태**이다.

---

## 3. AFS Frame과 UDP 패킷의 관계

AFS Frame은 1 Frame 기준으로 다음 구조를 가진다.

- 총 6000 symbols
- 12초
- Sync: 68 symbols
- SB1: 52 symbols
- SB2: 2400 symbols
- SB3: 1740 symbols
- SB4: 1740 symbols

6000개의 이진 심볼을 1bit씩 bit packing 하면 다음과 같다.

```text
6000 bit / 8 = 750 byte
```

따라서 AFS Frame 1개를 750 byte로 표현할 경우 일반적인 UDP 데이터그램 하나에 충분히 담을 수 있다.

다만 다음 두 문장은 서로 다른 의미이다.

```text
AFS Frame 1개를 UDP 패킷 1개에 담을 수 있다.
```

와

```text
UDP 패킷이 절대 손실되지 않는다.
```

는 동일하지 않다.

AFS Frame 하나를 UDP 패킷 하나에 담은 경우, 해당 UDP 패킷이 손실되면 **AFS Frame 하나 전체를 수신하지 못하는 결과**가 발생할 수 있다.

이는 RF에서 특정 심볼 몇 개가 오류나는 상황과 성격이 다르다.

---

## 4. 1bit / 2bit / 8bit 표현에 대한 이해

AFS 규격 자체는 6000개의 심볼 구조를 정의한다.

이를 파일이나 네트워크에서 어떻게 저장하고 표현할지는 구현 방식에 따라 달라질 수 있다.

예를 들어:

```text
1 bit / symbol
→ 6000 bit
→ 750 byte

2 bit / symbol
→ 12000 bit
→ 1500 byte

8 bit / symbol
→ 6000 byte
```

즉 2bit 또는 8bit로 표현하더라도 AFS 규격의 심볼 구조 자체가 바뀌는 것은 아니다.

다만 저장 용량과 네트워크 전송량은 증가한다.

현재 오픈소스에서 사용하는 1bit 심볼 표현은 실험 및 비교 관점에서 단순하고 효율적이므로, 초기 검증 단계에서는 그대로 유지하는 것이 적절하다.

---

## 5. PRN / I/Q / AFS Frame 관계

여러 위성이 동시에 신호를 송신하는 환경에서는 PRN별 신호가 하나의 RF 대역 안에서 동시에 존재한다.

개념적으로는 다음과 같다.

```text
PRN 1 AFS Signal ─┐
PRN 2 AFS Signal ─┤
PRN 3 AFS Signal ─┤
...               ├─→ Composite I/Q
PRN 8 AFS Signal ─┘
```

즉 PRN별 AFS Frame이 시간 순서대로 이어붙는 것이 아니라, 각 PRN의 신호가 동시에 합성되어 하나의 I/Q 신호가 된다.

수신기는 하나의 I/Q 신호에서 PRN별 코드 사본을 이용해 **상관연산(Correlation)** 을 수행하여 각 PRN 신호를 분리한다.

수신기의 개념적 처리 흐름은 다음과 같다.

```text
Composite I/Q
↓
PRN별 상관연산
↓
PRN별 신호 추적
↓
심볼 복원
↓
SP 탐색
↓
SB1 / SB2 / SB3 / SB4 분리
↓
LDPC Decode
↓
CRC 검사
↓
항법 데이터
↓
PVT
```

---

## 6. AFS가 고려한 오류 모델과 UDP 오류 모델

AFS는 본래 RF 채널을 고려한 구조이다.

즉 다음과 같은 오류를 주로 고려한다.

```text
정상 심볼:
1 0 1 1 0 1 0 0 1 0

RF 잡음 발생:
1 0 1 0 0 1 0 0 1 0
      ↑
특정 심볼 오류
```

반면 UDP 패킷 손실은 다음과 같은 형태이다.

```text
Packet #100 → 수신
Packet #101 → 손실
Packet #102 → 수신
```

AFS Frame 하나를 UDP 패킷 하나에 실었다면:

```text
AFS Frame #100 → 수신
AFS Frame #101 → 전체 손실
AFS Frame #102 → 수신
```

이 될 수 있다.

따라서 UDP 패킷 손실 자체를 AFS의 LDPC로 복구할 수 있다고 보는 것은 적절하지 않다.

AFS의 LDPC는 **전송된 코드워드 안에서 발생한 비트 또는 심볼 오류를 복구하는 기술**이다.

---

# 7. 실험 목적 분리

실험 목적은 최소 세 단계로 분리하는 것이 적절하다.

## 실험 1. 정상 End-to-End 및 PVT 검증

### 목적

UDP는 단순한 전송 수단으로 사용하고, AFS 인코딩부터 PVT까지의 전체 기능이 정상적으로 동작하는지 확인한다.

### 흐름

```text
GNSS / Simulation Data
↓
AFS Frame 생성
↓
CRC
↓
LDPC
↓
Interleaving
↓
UDP 전송
↓
수신
↓
Deinterleaving
↓
LDPC Decode
↓
CRC 검사
↓
항법정보 복원
↓
PVT 계산
```

### 확인 항목

- 송신 Frame 수
- 수신 Frame 수
- PRN별 수신 여부
- SP Sync 성공 여부
- LDPC Decode 성공 여부
- CRC-24Q 성공 여부
- PVT 계산 성공 여부
- 기준 PVT와 수신 PVT 차이

이 실험에서는 UDP 자체의 성능을 검증하는 것이 아니라 **AFS End-to-End 파이프라인이 정상 동작하는지**를 확인하는 것이 목적이다.

## 실험 2. AFS 오류 정정 성능 검증

### 목적

AFS가 RF 환경에서 발생할 수 있는 비트 또는 심볼 오류를 얼마나 복구할 수 있는지 확인한다.

이 실험에서는 UDP 패킷 자체를 버리는 것이 아니라, 정상적인 AFS Frame을 생성한 후 **전송 심볼 일부를 의도적으로 반전**시킨다.

### 오류 주입 위치

가장 적절한 위치는 다음과 같다.

```text
원본 항법 데이터
↓
CRC 생성
↓
LDPC Encode
↓
Interleaving
↓
AFS 전송 심볼 완성
↓
[여기서 일부 심볼 반전]
↓
UDP 전송
↓
수신
↓
Deinterleaving
↓
LDPC Decode
↓
CRC 검사
```

즉 실제 RF 채널에서 오류가 발생한 것처럼 **완성된 전송 심볼에 오류를 주입**한다.

### 중요한 점

데이터 비트만 선택적으로 변경하고 패리티 비트는 보호하는 방식은 실제 RF 환경과 다르다.

실제 RF에서는:

```text
Data bit
Parity bit
Sync symbol
```

모두 동일하게 채널 오류에 노출된다.

따라서 AFS 오류 정정 성능 실험에서는 **LDPC parity를 포함한 전체 전송 심볼 중 일부를 랜덤 또는 특정 패턴으로 반전**시키는 것이 적절하다.

---

## 8. 오류 주입 방식

오류 주입 방식은 최소 두 종류로 구분하는 것이 좋다.

### 8.1 랜덤 오류

전체 AFS 전송 심볼 중 임의 위치를 선택하여 반전한다.

예시:

```text
1 bit error
2 bit errors
5 bit errors
10 bit errors
20 bit errors
50 bit errors
...
```

각 조건에서 여러 번 반복 수행한다.

예:

```text
1 bit error × 100회
2 bit error × 100회
5 bit error × 100회
...
```

오류 위치는 매 반복마다 랜덤하게 변경한다.

### 8.2 Burst Error / 연속 오류

연속된 심볼 구간을 의도적으로 반전한다.

예:

```text
1 symbol 연속 오류
5 symbols 연속 오류
10 symbols 연속 오류
20 symbols 연속 오류
50 symbols 연속 오류
...
```

이는 특정 시간 구간에서 잡음이나 간섭이 집중된 상황을 모사한다.

Interleaving의 효과를 확인하는 데 특히 중요하다.

---

## 9. CRC-24Q 성공률

CRC-24Q 성공률은 다음 의미를 가진다.

> 오류가 포함된 AFS Frame을 LDPC Decode한 뒤 원본 데이터가 정상적으로 복원되었는지를 판단하는 지표

예를 들어:

```text
10 bit random error
100회 실험
```

결과가:

```text
CRC PASS = 94회
CRC FAIL = 6회
```

라면:

```text
CRC Success Rate = 94%
```

이다.

CRC 성공률은 **데이터 복원 관점의 성능 지표**이다.

---

## 10. PVT 성공률

PVT는 여러 PRN의 유효한 항법 정보와 관측값이 충분히 확보되어야 계산할 수 있다.

현재 PocketSDR-AFS 구현에서는 최소 4개의 유효한 위성 관측이 필요하다.

따라서 AFS Frame 일부가 복호 실패하면 해당 PRN의 유효한 항법 정보가 부족해질 수 있고, 최종적으로 PVT 계산이 실패할 수 있다.

예를 들어:

```text
100회 실험

CRC 통과 PRN 수 충분
→ PVT 계산 성공 90회

PRN 부족 또는 항법정보 부족
→ PVT 계산 실패 10회
```

이면:

```text
PVT Success Rate = 90%
```

이다.

PVT 성공률은 **최종 항법 서비스 관점의 성능 지표**라고 볼 수 있다.

---

## 11. CRC 성공률과 PVT 성공률의 차이

```text
CRC 성공률
↓
"AFS 데이터 자체를 정상 복원했는가?"

PVT 성공률
↓
"복원된 데이터를 이용하여 실제 위치 계산까지 성공했는가?"
```

따라서 CRC 성공률이 높더라도 여러 PRN 중 일부가 지속적으로 실패하면 PVT는 계산되지 않을 수 있다.

반대로 8개 PRN 중 1~2개가 복호 실패하더라도 4개 이상의 유효 관측이 남아 있으면 PVT 계산은 가능할 수 있다.

---

# 12. 실험 3. 재동기 성능 검증

AFS 오류 정정 성능과 Frame Sync 복구 성능은 별개의 실험으로 보는 것이 좋다.

### 목적

SP 또는 프레임 구조가 크게 훼손되었거나 한 프레임을 수신하지 못했을 때, 다음 정상 프레임에서 수신기가 얼마나 빨리 다시 Sync를 획득하는지 확인한다.

### 예시

```text
Frame 100 → 정상
Frame 101 → 큰 오류 또는 누락
Frame 102 → 정상
```

확인 항목:

- Frame 101에서 Sync 손실 여부
- Frame 102에서 SP 재탐색 여부
- 재동기까지 걸린 시간
- PRN Tracking 유지 여부
- 정상 Decode 복귀 시점
- PVT 정상 복귀 시점

이 실험의 지표는 다음과 같다.

```text
Loss
↓
Sync Lost
↓
Next SP Detection
↓
Frame Decode 정상화
↓
PVT 정상화
```

따라서 해당 실험은 **손실된 Frame 자체를 복구하는지 확인하는 것이 아니라, 손실 후 정상 상태로 얼마나 빠르게 돌아오는지 확인하는 시험**이다.

---

# 13. UDP 패킷 손실 시험에 대한 판단

UDP 패킷 손실은 실제 AFS RF 채널과 동일한 오류 모델은 아니다.

따라서 프로젝트 목적이 AFS 오류 정정 성능과 PVT 검증이라면, UDP 패킷 손실 자체를 주된 성능 지표로 삼을 필요는 없다.

UDP는 가능한 한 단순하고 안정적인 운반 수단으로 사용하고 다음 정도만 관리하는 것이 적절하다.

- Sequence Number
- Frame Number
- PRN
- Timestamp
- Payload Length

예:

```text
UDP Header
{
    Sequence = 1001
    FrameId  = 28
    PRN      = 3
    Length   = 750
}
```

이를 통해 문제가 발생했을 때:

```text
AFS Decode 문제인지
UDP 패킷 손실인지
송수신 버퍼 문제인지
```

를 구분할 수 있다.

---

# 14. 권장 실험 구성

## Test A - 정상 조건

```text
Error = 0
```

목적:

- End-to-End 정상 동작 확인
- 기준 PVT 확보

## Test B - Random Bit Error

```text
1 / 2 / 5 / 10 / 20 / 50 ...
```

각 조건 100회 이상 수행 권장.

확인:

- LDPC Decode 결과
- CRC Success Rate
- PRN별 Decode Success Rate
- PVT Success Rate

## Test C - Burst Error

```text
5 / 10 / 20 / 50 / 100 symbols
```

확인:

- Interleaving 효과
- LDPC 복구 성능
- CRC Success Rate
- PVT Success Rate

## Test D - Sync Loss

SP 또는 Frame 앞부분을 크게 훼손한다.

확인:

- SP 재탐색
- Sync Recovery Time
- 다음 정상 Frame Decode 여부
- PVT Recovery Time

## Test E - UDP Packet Loss

필요 시 네트워크 환경 검증 목적으로 별도 수행한다.

```text
Packet Drop Rate
0%
0.1%
1%
5%
...
```

단, 이는 AFS RF 오류 정정 능력 시험과는 별도로 분리하여 평가한다.

---

# 15. 주요 성능 지표

실험 결과는 다음 항목으로 정리하는 것이 적절하다.

| 항목 | 의미 |
|---|---|
| Bit/Symbol Error Count | 인위적으로 주입한 오류 개수 |
| Error Type | Random / Burst |
| LDPC Decode Success | LDPC 복호 성공 여부 |
| CRC-24Q Success Rate | 데이터 정상 복원 비율 |
| PRN Decode Success Rate | PRN별 AFS Frame 복호 성공률 |
| Valid PRN Count | PVT 계산에 사용 가능한 PRN 수 |
| PVT Success Rate | 최종 위치 계산 성공률 |
| Position Error | 기준 위치 대비 PVT 위치 오차 |
| Sync Recovery Time | 동기 손실 후 정상 Frame 복귀 시간 |

---

# 16. 핵심 결론

AFS Frame은 본래 RF 환경의 잡음, 감쇠, 간섭 등으로 발생하는 **비트 또는 심볼 오류를 보강하기 위한 구조**이다.

UDP는 AFS의 원래 RF 채널과 다른 오류 특성을 가지며, UDP에서는 **패킷 전체가 손실될 수 있다**.

따라서 실험 목적은 다음과 같이 분리하는 것이 적절하다.

```text
1. 정상 AFS End-to-End / PVT 검증

2. 정상 AFS 전송 심볼에
   랜덤 또는 Burst 오류를 주입하여
   LDPC / CRC 오류 정정 성능 확인

3. 큰 오류 또는 Frame 손실 시
   SP 재동기 및 PVT 정상 복귀 시간 확인

4. 필요할 경우 별도로
   UDP Packet Loss 환경 검증
```

특히 AFS 오류 정정 성능을 검증하고자 할 경우에는 UDP Packet을 일부러 손실시키는 방식보다:

> **정상적으로 CRC, LDPC, Interleaving이 완료된 AFS 전송 심볼 전체에서 일부 심볼을 인위적으로 반전시킨 후, 수신 측에서 LDPC Decode 및 CRC-24Q를 수행하여 복호 성공률과 최종 PVT 성공률을 측정하는 방식이 가장 적절하다.**

---

# 17. 회의용 핵심 문장

> AFS는 RF 환경에서 발생하는 심볼 오류를 보강하기 위해 LDPC, Interleaving, CRC 등을 사용하는 구조이며, UDP 패킷 손실은 AFS가 원래 상정한 오류 모델과 다르다. 따라서 UDP는 우선 단순 전송 수단으로 사용하고, AFS 오류 정정 성능은 정상 AFS 전송 심볼에 랜덤 또는 연속 오류를 의도적으로 주입하여 CRC 성공률과 PVT 성공률을 측정하는 방식으로 검증하는 것이 적절하다.

---

---
---
# AFS Frame / UDP 전송 / 오류정정 / Wi-Fi Broadcast 실험 설계 정리

## 1. 목적

본 문서는 AFS Frame을 네트워크, 특히 Wi-Fi UDP Broadcast 방식으로 전송하는 실험을 설계할 때 고려해야 할 사항을 정리한 것이다.

핵심은 다음 세 가지를 서로 구분하는 것이다.

1. **AFS 자체의 RF 오류 정정 성능**
2. **UDP 네트워크 전송 특성에 따른 패킷 손실**
3. **왜 Wi-Fi Broadcast 방식을 사용하는지에 대한 실험 목적**

AFS는 원래 RF 환경에서 발생하는 잡음, 감쇠, 간섭 등에 따른 심볼 오류를 고려하여 설계된 신호 구조이다.

반면 UDP 패킷 손실은 AFS가 본래 상정한 오류 모델과 다르기 때문에, 실험 목적을 명확히 나누어 설계해야 한다.

---

# 2. RF 방식과 UDP 방식의 차이

## 2.1 RF 방식 - I/Q 신호

실제 RF 환경에서는 여러 PRN 신호가 하나의 연속된 I/Q 파형에 합성되어 전달된다.

RF 환경에서 발생하는 대표적인 문제는 다음과 같다.

- 잡음
- 감쇠
- 간섭
- 신호 세기 저하
- 특정 구간의 심볼 판정 오류
- 비트 반전
- 연속된 심볼 오류

즉 RF 환경에서는 데이터 블록 하나가 통째로 사라진다기보다는, **연속 신호의 품질이 나빠지면서 일부 심볼이 잘못 판정되는 형태**가 일반적이다.

AFS Frame은 이러한 RF 환경의 오류에 대응하기 위해 다음과 같은 구조를 사용한다.

- Synchronization Pattern, SP
- CRC-24Q
- LDPC
- Interleaving
- Subframe 구조

이 중 LDPC는 오류 정정 역할을 하고, Interleaving은 연속 오류를 분산시켜 LDPC가 복구하기 쉬운 형태로 만드는 역할을 한다.

---

## 2.2 UDP 방식 - 패킷 전송

UDP에서 패킷은 네트워크에서 한 번에 독립적으로 전달되는 데이터 덩어리이다.

UDP는 기본적으로 다음을 보장하지 않는다.

- 전달 보장
- 재전송
- 순서 보장
- 중복 제거

패킷 손실은 다음과 같은 이유로 발생할 수 있다.

- 네트워크 혼잡
- Wi-Fi 간섭
- 신호 세기 저하
- 송신 버퍼 초과
- 수신 버퍼 초과
- OS 또는 애플리케이션 처리 지연
- AP 또는 네트워크 장비 처리 문제

즉 UDP에서는 RF처럼 몇 개의 심볼만 조금 틀리는 것이 아니라, **패킷 단위의 데이터가 통째로 사라질 수 있다.**

따라서 RF 오류와 UDP 패킷 손실은 오류의 형태 자체가 다르다.

---
