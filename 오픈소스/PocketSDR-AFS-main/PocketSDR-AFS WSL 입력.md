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
