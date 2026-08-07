## Linux
### git 오픈소스 복제
```text
git clone https://github.com/osqzss/LANS-AFS-SIM.git

cd LANS-AFS-SIM

sudo apt update
```
### make, gcc, g++, 기본 C/C++ 빌드 도구 설치
```text
sudo apt install -y build-essential

make --version   /   gcc --version
```
### 저장소 폴더에 빌드
```text
make

ls -lh afs_sim
```
### 최소 시험 테스트
```text
./afs_sim -t 1 -b 2 test.bin
```
### AFS 한 프레임인 12초 분량을 생성
```text
./afs_sim -t 12 -b 2 afs_12s.bin
```
####결과예시
```text
xyz =     -6516.5,      7990.0,  -1737469.4   # 기본 수신기 위치정보
llh =  -89.660000,  129.200000,       100.0
Start time = 2023/08/30,16:44:48 (2277:319488)   # 시뮬레이션 시작 시각
AFS time: WN = 2277, ITOW = 266, TOI = 24, fsec = 0.0
Number of channels = 7
SV    AZ    EL     RANGE     DOPP
02   92.4  35.0  7374450.9  -1507.6
03  141.1  49.9  9075002.2     -1.6
04  189.5  34.7  7382482.6  +1503.5
05  246.6   4.8  5360421.9  -1899.8
06  295.4  47.0  8645460.5   -746.9
07  345.7  47.2  8641696.8   +750.4
08   34.9   5.2  5347945.6  +1902.3
Generating baseband signals...
Time =  1.0
```
------------------------------------------
### 윈도우용 exe설치
```text
sudo apt update
sudo apt install -y mingw-w64

cd ~/lnis/LANS-AFS-SIM

x86_64-w64-mingw32-gcc \
-Ofast \
-fopenmp \
-I./ldpc \
-I./rtklib \
-I./pocketsdr \
afs_sim.c \
afs_nav.c \
afs_rand.c \
./ldpc/alloc.c \
./ldpc/mod2sparse.c \
./rtklib/rtkcmn.c \
./pocketsdr/pocketsdr.c \
-lm \
-static \
-static-libgcc \
-o afs_sim.exe
```

### 생성된 exe파일 윈도우저장소(소스코드 폴더)로 복사 후 실행
-- 008_Weil1500hex210prns.txt, default_almanac.txt 같은 런타임 데이터 파일이 있고, afs_sim.c가 tertiary code 파일을 읽도록 되어 있음
```text
.\afs_sim.exe -t 90 -b 2 test_iq2.bin
```
-----------------------------------------

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
cd O:\3.ing\LNIS\오픈소스\LANS-AFS-SIM-main

//기존 실행파일 지우기
Remove-Item .\afs_sim.exe -ErrorAction SilentlyContinue

//컴파일
gcc `
    -Ofast `
    -fopenmp `
    -I.\ldpc `
    -I.\rtklib `
    -I.\pocketsdr `
    .\afs_sim.c `
    .\afs_nav.c `
    .\afs_rand.c `
    .\ldpc\alloc.c `
    .\ldpc\mod2sparse.c `
    .\rtklib\rtkcmn.c `
    .\pocketsdr\pocketsdr.c `
    -lm `
    -static `
    -static-libgcc `
    -o afs_sim.exe

//설치 확인    
Get-Item .\afs_sim.exe

Get-ChildItem `
    .\008_Weil1500hex210prns.txt, `
    .\default_almanac.txt
    
// 실행
.\afs_sim.exe -t 90 -b 2 90s_2qb.bin
```
