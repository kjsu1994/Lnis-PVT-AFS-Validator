#
#  makefile for pocket_conf
#

SRC = ../../src
LIB = ../../lib
BIN = ../../bin
USE_FFTW ?= 0

ifeq ($(OS),Windows_NT)
    CC = g++
    INCLUDE = -I$(SRC) -I$(LIB)/cyusb -I$(LIB)/RTKLIB/src
    OPTIONS = -DWIN32
    LDLIBS = -static $(LIB)/win32/libsdr.a $(LIB)/win32/librtk.a $(LIB)/cyusb/CyAPI.a \
        -lsetupapi -lavrt -lwinmm -lws2_32 -lpthread
    ifeq ($(USE_FFTW),1)
        LDLIBS += -lfftw3f
    else
        LDLIBS += $(LIB)/win32/libpocketfft.a
    endif
else ifeq ($(shell uname -sm),Darwin arm64)
    CC = clang++
    INCLUDE = -I$(SRC) -I$(LIB)/RTKLIB/src -I/opt/homebrew/include
    OPTIONS = -Wno-deprecated
    LDLIBS = -L/opt/homebrew/lib $(LIB)/macos/libsdr.a $(LIB)/macos/librtk.a \
        -lusb-1.0 -lpthread
    ifeq ($(USE_FFTW),1)
        LDLIBS += -lfftw3f
    else
        LDLIBS += $(LIB)/macos/libpocketfft.a
    endif
else
    CC = g++
    INCLUDE = -I$(SRC) -I$(LIB)/RTKLIB/src
    OPTIONS =
    LDLIBS = $(LIB)/linux/libsdr.a $(LIB)/linux/librtk.a -lusb-1.0 -lpthread
    ifeq ($(USE_FFTW),1)
        LDLIBS += -lfftw3f
    else
        LDLIBS += $(LIB)/linux/libpocketfft.a
    endif
endif

WARNOPT = -Wall -Wextra -Wno-missing-field-initializers -Wno-unused-parameter

CFLAGS = -O3 $(INCLUDE) $(WARNOPT) $(OPTIONS) -g

TARGET = pocket_conf

all: $(TARGET)

pocket_conf: pocket_conf.o

pocket_conf.o: $(SRC)/pocket_sdr.h

clean:
	rm -f $(TARGET) *.exe *.o *.obj *.stackdump

install:
	cp $(TARGET) $(BIN)

