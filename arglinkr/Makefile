## Makefile (GNU dialect) to build ArgLink Re-Rewrite under DJGPP, Linux, macOS

# Use spaces ONLY to indent inside conditionals
ifneq ($(strip ${DJGPP}),)
  CC := gcc
  # Note : you cannot use -pipe with DOS
  CFLAGS_OPTIM := -O2 -fomit-frame-pointer -march=pentium3 -s
  # Note: "del" must be lowercase even in DOS
  RM := del
  CP := copy /y
  DIRSEP := \\
  EXE := .EXE
else
  CC := $(shell which cc)
  CFLAGS_OPTIM := -O2 -pipe -fomit-frame-pointer
  # I really mean an NT-based Windows here
  ifeq (${OS},Windows_NT)
    RM := del /q
    CP := copy /y
    DIRSEP := \\
    EXE := .exe
  else
    # *nix assumed here
    RM := rm -f
    ifneq (,$(wildcard /etc/alpine-release))
      CP := install -p
      FORMFACTOR := alpine
    else
      ifeq (Darwin,$(shell uname))
        CP := install -CSv
      else
        CP := install -Cv
      endif
      FORMFACTOR := unknown
    endif
    DIRSEP := /
    EXE :=
  endif
endif

CFLAGS_HARDEN := -Wall -Wformat -Wformat=2 -Wconversion -Wimplicit-fallthrough -Werror=format-security \
-U_FORTIFY_SOURCE -D_FORTIFY_SOURCE=3 \
-fstack-protector-strong \
-fno-delete-null-pointer-checks -fno-strict-overflow -fno-strict-aliasing \
-Werror=implicit -Werror=incompatible-pointer-types -Werror=int-conversion
ifneq ($(strip ${DJGPP}),)
  CFLAGS_GCC := -fstack-clash-protection -Wtrampolines
else
  ifeq (Linux,$(shell uname))
    CFLAGS_LINUX := -Wl,-z,nodlopen -Wl,-z,noexecstack -Wl,-z,relro -Wl,-z,now -Wl,--as-needed -Wl,--no-copy-dt-needed-entries
  endif
  CFLAGS_NOTDJGPP := -fstrict-flex-arrays=3 -ftrivial-auto-var-init=zero
  ifeq (gcc,$(CC))
    CFLAGS_GCC := -march=native -fstack-clash-protection -Wtrampolines -Wbidi-chars=any
  else 
    CC_IS_SYMLINK := $(shell test -L $(CC); echo $$?)
    # test returns a process exit code; 0 is a success (anything else a failure)
    ifeq (0,$(CC_IS_SYMLINK))
      CC_TARGET := $(shell readlink $(CC))
      ifeq (gcc,$(CC_TARGET))
        CFLAGS_GCC := -march=native -fstack-clash-protection -Wtrampolines -Wbidi-chars=any
      endif
    endif
  endif
  ISA := $(shell uname -m)
  ifeq (x86_64,$(ISA))
    CFLAGS_ISA := -fcf-protection=full
  endif
endif

# FIXME : On aarch64, add the following to CFLAGS_WARN
#-mbranch-protection=standard

COMPILE := $(CC) $(CFLAGS_OPTIM) $(CFLAGS_HARDEN) $(CFLAGS_LINUX) $(CFLAGS_GCC) $(CFLAGS_NOTDJGPP) $(CFLAGS_ISA)
$(info Compiler and flags: $(COMPILE))

all: arglinkr$(EXE)

arglinkr$(EXE): arglinkr.c
	$(COMPILE) $< -o $@

clean:
	$(RM) arglinkr$(EXE)

distclean: clean
	$(RM) arglinkr.o
	$(RM) ARGLINKR.o
	$(RM) Makefile.win
	$(RM) *.log
	$(RM) *.bak
	$(RM) arglinkr.exe

help:
	@echo "Available targets: all clean distclean"

.PHONY: all clean distclean help
