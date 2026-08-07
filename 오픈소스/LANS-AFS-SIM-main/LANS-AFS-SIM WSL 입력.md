git clone https://github.com/osqzss/LANS-AFS-SIM.git

cd LANS-AFS-SIM

sudo apt update

#make, gcc, g++, 기본 C/C++ 빌드 도구 설치
sudo apt install -y build-essential

make --version   /   gcc --version

# 저장소 폴더에 빌드
make

ls -lh afs_sim

# 최소 시험 테스트
./afs_sim -t 1 -b 2 test.bin

# AFS 한 프레임인 12초 분량을 생성
./afs_sim -t 12 -b 2 afs_12s.bin

------------------------------------------------

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