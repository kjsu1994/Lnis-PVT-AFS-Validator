### PocketSDR-AFS 공식 README에서도 Ubuntu 빌드 전에 libusb-1.0-0-dev와 libfftw3-dev 설치를 요구
```text
sudo apt update
sudo apt install -y \
    git \
    build-essential \
    libusb-1.0-0-dev \
    libfftw3-dev

gcc --version  make --version

git clone https://github.com/osqzss/PocketSDR-AFS.git

cd ~/lnis/PocketSDR-AFS
```
### PocketSDR-AFS의 lib 폴더로 이동
```text
cd ~/lnis/PocketSDR-AFS/lib
```
### 스크립트 실행권한 부여
```text
chmod +x clone_lib.sh
```
### 실행
```text
./clone_lib.sh   # libfec, LDPC-codes, build폴더 생성
```
### 빌드폴더 이동
```text
cd ~/lnis/PocketSDR-AFS/lib/build
```
### 빌드
```text
make
```
### 설치 (생성된 파일을 다른 프로그램이 사용할 위치에 복사)
```text
sudo make install
```
### 라이브러리 캐시 갱싱
```text
sudo ldconfig
```
### pocket_trk 폴더로 이동
```text
cd ~/lnis/PocketSDR-AFS/app/pocket_trk

make

ls -lh pocket_trk
```
### 스크립트에도 실행권한 부여
```text
chmod +x pocket_trk.sh
```
### 실행권한 또는 #!/bin/bash 인식하도록
```text
cd ~/lnis/PocketSDR-AFS/app/pocket_trk

ls -lh pocket_trk.sh
chmod +x pocket_trk.sh
```
### 줄바꿈 Linux 형식으로 변환
```text
sed -i 's/\r$//' pocket_trk.sh
```
### test.bin 실행
```text
./pocket_trk.sh ~/lnis/LANS-AFS-SIM/test.bin   # 안될경우 : bash pocket_trk.sh test.bin
```
### 작동할경우 24초짜리 테스트(생성, 복호화)
```text
cd ~/lnis/LANS-AFS-SIM

./afs_sim -t 24 -b 2 afs_24s.bin

cd ~/lnis/PocketSDR-AFS/app/pocket_trk

./pocket_trk.sh ~/lnis/LANS-AFS-SIM/afs_24s.bin
```

---------------------------------------

## PowerShell
### 파워쉘 exe파일 생성하기

### MSYS2 설치 및 필요 컴파일러 설치
```text
winget install --id MSYS2.MSYS2 -e

Test-Path C:\msys64   

//파워쉘 새로열고 작업
C:\msys64\usr\bin\pacman.exe -Syu

C:\msys64\usr\bin\pacman.exe -S --needed mingw-w64-x86_64-gcc mingw-w64-x86_64-make mingw-w64-x86_64-fftw

// 세션에 Path 추가
$env:Path = "C:\msys64\mingw64\bin;$env:Path"

//설치확인
gcc --version
g++ --version
mingw32-make --version
```

### 윈도우에서 컴파일
```text
cd O:\3.ing\LNIS\LnisAfsValidator\오픈소스\PocketSDR-AFS-main\app\pocket_trk

//윈도우 라이브러리 확인
Get-ChildItem .\lib\win32

// 결과 True 확인
Test-Path .\lib\cyusb\CyAPI.a

// pocket_trk 폴더로 이동
cd .\app\pocket_trk
Get-Location

$env:OS = "Windows_NT"

//기존파일 삭제 후 빌드
mingw32-make clean
mingw32-make

// libsdr.a 컴파일
cd "O:\3.ing\LNIS\LnisAfsValidator\오픈소스\PocketSDR-AFS-main\lib\build"

$env:Path = "C:\msys64\mingw64\bin;$env:Path"
$env:OS = "Windows_NT"

mingw32-make -f libsdr.mk libsdr.a

Get-Item .\libsdr.a

  Copy-Item `
      -LiteralPath .\libsdr.a `
      -Destination ..\win32\libsdr.a `
      -Force
      
// 실행기 강제로 다시 링크
  cd ..\..\app\pocket_trk
  mingw32-make -B pocket_trk
  
// 시간 갱신 확인
Get-Item ..\..\lib\win32\libsdr.a, .\pocket_trk.exe |
Select-Object FullName, Length, LastWriteTime

// 실행
cd O:\3.ing\LNIS\LnisAfsValidator\오픈소스\PocketSDR-AFS-main\app\pocket_trk

.\pocket_trk.exe `
      -sig AFSD -prn 2-8 `
      -sig AFSP -prn 2-8 `
      -f 12 `
      -IQ 2 `
      -log log.txt `
      "O:\3.ing\LNIS\LnisAfsValidator\오픈소스\LANS-AFS-SIM-main\90s_2qb.bin"
```