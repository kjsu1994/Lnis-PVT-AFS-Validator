# LNIS AFS End-to-End Validator

.NET 8 WPF에서 `LANS-AFS-SIM`과 `PocketSDR-AFS`를 별도 프로세스로 실행하여 AFS I/Q 생성, TCP 전송, 복호/PVT 처리 및 기준값 비교를 자동화한다.

## 1차 범위

```text
LANS-AFS-SIM -> INT8X2 I/Q -> TCP chunk/CRC32/SHA-256
             -> PocketSDR-AFS -> $LOG/$SB2/$POS -> 달 PVT 비교
```

실행에는 `LANS-AFS-SIM`과 `PocketSDR-AFS`만 사용한다. 범용 `PocketSDR`는 향후 GNSS 장비 Raw 입력과 지구 GNSS 기준 PVT Adapter를 구현할 때 참고한다. LNIS Codec, SF3/SF4 payload 삽입, BPv7/HDTN, SDR/RF와 실시간 스트리밍은 포함하지 않는다.

## 빌드

솔루션은 배포 가능한 WPF 본체 1개와 자동 테스트 프로젝트 1개로 구성된다. `Core`와 `Infrastructure`는 별도 프로젝트가 아닌 본체 내부 소스 폴더다.

```powershell
dotnet restore LnisAfsValidator.sln
dotnet build LnisAfsValidator.sln
dotnet test LnisAfsValidator.sln
dotnet run --project LnisAfsValidator.csproj
```

## WSL 준비

현재 확인 환경은 Ubuntu 24.04 WSL2이다. 애플리케이션은 패키지 설치나 저장소 복제를 자동 수행하지 않는다.

```bash
sudo apt update
sudo apt install build-essential git libusb-1.0-0-dev libfftw3-dev
cd /home/$USER
git clone https://github.com/osqzss/LANS-AFS-SIM.git
cd LANS-AFS-SIM && make

cd /home/$USER
git clone --recurse-submodules https://github.com/osqzss/PocketSDR-AFS.git
cd PocketSDR-AFS
git submodule update --init --recursive
cd lib/build && make && make install
cd ../../app/pocket_trk && make
```

기본 UI 경로는 `/home/imt` 기준 예시이므로 각 PC의 WSL 사용자 경로에 맞게 수정해야 한다. `O:` 드라이브 마운트에는 의존하지 않는다. WSL 산출물은 Windows의 `\\wsl.localhost\<배포판>\...` UNC를 통해 비동기로 읽고 쓴다.

## 사용 방법

공통으로 위치·시간 허용오차를 반드시 입력한다. `-b 2` 출력만 확인된 직접 호환 형식이므로 1차 UI는 `PocketSDR.INT8X2`, I/Q 2채널 형식을 고정 사용한다. 기준 시작 시각은 알마낙에서 결정된 생성기 stderr의 `Start time`을 사용한다.

### 로컬 시험

1. 역할을 `Local`로 선택한다.
2. 두 도구의 Native 또는 WSL 실행 경로와 작업 경로를 입력한다.
3. 월면 위치, 시험시간, 샘플률, 알마낙, PRN, 최소 위성 수와 허용오차를 입력한다.
4. `시험 시작`을 누른다. localhost에서도 실제 TCP 전송 경로를 사용한다.

### 두 PC 시험

1. 수신 PC에서 역할 `Receiver`, 사용할 포트와 PocketSDR-AFS 경로를 설정하고 먼저 시작한다.
2. OS 방화벽에서 해당 TCP 포트의 인바운드 연결을 허용한다.
3. 송신 PC에서 역할 `Sender`, 수신 PC 주소·동일 포트와 LANS-AFS-SIM 경로를 설정하고 시작한다.
4. 수신 PC는 무결성 ACK 후 복호/PVT를 수행하고 같은 연결로 최종 결과를 송신 PC에 반환한다.

## 판정과 결과

PASS에는 다음 근거가 모두 필요하다.

- 전송 byte count, chunk 순서, IEEE CRC32와 전체 SHA-256 일치
- 설정한 최소 위성 수 이상의 `SIGNAL FOUND`
- 최소 위성 수 이상의 `$SB2` 성공: AFS frame sync와 SB2 LDPC/CRC 성공의 간접 근거
- `$POS`와 마지막 필드의 실제 관측 위성 수
- 최종 `$POS` 위치·시간 오차가 사용자가 입력한 허용치 이내
- PocketSDR-AFS 정상 종료

명확한 필수 조건 위반은 Fail, 로그 형식 미지원이나 기준값 파싱 불가 등 근거 부족은 Inconclusive이다. `$POS`의 끝에서 두 번째 위성 수는 현재 AFS 소스에서 상수 `5`이므로 판정에 사용하지 않는다. `$SB3/$SB4`와 frame error는 기록하지만 1차 필수 조건은 아니다.

결과는 `%LOCALAPPDATA%\LnisAfsValidator\Runs` 아래에 manifest, JSON 결과, 생성기/수신기 원본 로그와 함께 저장한다. PASS면 대용량 I/Q를 삭제하고 Fail/Inconclusive이면 원본 분석을 위해 보관한다. 마지막 UI 설정은 `%LOCALAPPDATA%\LnisAfsValidator\settings.json`에 저장한다.

## TCP 프로토콜 v1

- magic/version: ASCII `LAFSIQ01`
- 최대 64 KiB 길이 제한 JSON manifest
- chunk: big-endian index, payload length, IEEE CRC32, payload
- 기본 chunk 1 MiB, 허용 범위 4 KiB~16 MiB, 기본 최대 파일 32 GiB
- 수신 경로는 원격 파일명을 사용하지 않고 시험 GUID로 생성
- SHA-256 확인 전에는 `.partial`, 성공 후에만 `.iq`로 원자적 변경

## 확장 지점

- `IIqDataSource`: 현재 LANS-AFS-SIM, 향후 GNSS 장비 Raw 및 PocketSDR 기준 PVT Adapter
- `IArtifactTransport`: 현재 TCP, 향후 BPv7/HDTN
- `IAfsReceiverAdapter`: 현재 PocketSDR-AFS, 향후 다른 AFS 수신기
- `IVerdictEvaluator`: 향후 LNIS message 및 SF3/SF4 비교

GNSS 장비 Raw 형식이 확인되기 전에는 이를 `Unknown` 산출물로 취급하고 성공으로 판정하지 않아야 한다. LNIS payload와 BPv7 bundle을 AFS SF3/SF4에 넣는 규격이 확인된 뒤 별도 Adapter로 구현한다.

## 현재 검증 제약

- .NET 솔루션 빌드와 자동 테스트는 수행 가능하다.
- 현재 PC의 WSL에는 필수 개발 패키지와 빌드된 오픈소스 실행 파일이 없어 실제 90초 AFS E2E는 아직 실행하지 못했다.
- 저장소에 AFS 실제 실행 로그 fixture가 없어 파서 테스트는 실제 소스에서 확인한 로그 형식을 사용한다. WSL 빌드 후 생성한 원본 로그로 fixture를 교체·보강해야 한다.
- `O:`가 WSL `/mnt/o`에 자동 마운트되지 않는 환경을 기준으로 UNC staging을 구현했다.
