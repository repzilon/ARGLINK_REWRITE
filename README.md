# ArgLink SFX Re-Rewrite
Argonaut SNES Linker Rewrite, remixed.

## Goals and platforms
* End goal: produce a C version of the linker that is functionally compatible with the original one written by Argonaut software as used in (Ultra)StarFox (2).
* Host OS: DOS with DJGPP, Mac OS X, Linux, Windows (98! and newer)
* Host CPUs: x86, x86_64, arm64, i.e. both non-exotic (looking at you Sparc, MIPS, PowerPC, Alpha, SuperH, HP-PA and IA64) retro and modern CPUs  

## Roadmap
1. (In progress) Implement command line options, starting fron LuigiBlood's C# rewrite, in C#.
2. (Done) Write a program to translate C# code to compilable C.
3. (Done) Correct the translated C program to a have a working version.
4. (In progress) Correct the translator program accordingly.
5. Correct the ROM output currently leading to graphic and sound glitches in game
6. Improve the debugger map output for easier debugging

Why write a translator program? Setting up and using a C# debugger is much easier than a C debugger. It also allows to separate concerns during development: general logic in C#, manual memory management in C.   

## Programming standards
To ease translation to C, ARGLINK_REWRITE C# code follows some guidelines:
* Single C# file (like LuigiBlood's code)
* Imperative approach (like LuigiBlood's code)
* Main method goes last, and callee methods are put before their callers (so no need to generate prototypes or headers for C)
* Target .NET Framework 2.0 (Windows 98 compatibility)
* Use language version 2 ([simple] generics allowed, but neither lambdas, async nor other fancy features)
* No exception throws and catches (unless you are willing to implement this in the translator)

C version targets C99 (for single line comments and variable declarations anywhere, just like C#).
Also, every memory allocation failure is considered fatal, with immediate termination (using the BSD constant ``EX_SOFTWARE`` as exit code).

Exit codes follow the recommendations of the BSD style guide and defined in ``sysexits.h``.

## Command line options status
| Supported | Switch       | Description                                             |
|:---------:|--------------|---------------------------------------------------------|
|           | -A1          | Download to ADS SuperChild1 hardware.                   |
|           | -A2          | Download to ADS SuperChild2 hardware.                   |
|    Yes    | -B<kib>      | Set file input/output buffers (0-31), default = 10 KiB. |
|    Yes    | -C           | Duplicate public warnings on.                           |
|           | -D           | Download to ramboy.                                     |
|    Yes    | -E\<.ext>    | Change default file extension, default = '.SOB'.        |
|           | -F\<addr>    | Set Fabcard port address (in hex), default = 0x290.     |
|    Yes    | -H\<size>    | String hash size, default = 256.                        |
|           | -I           | Display file information while loading.                 |
|           | -L\<size>    | Display used ROM layout (size is in KiB).               |
|           | -M\<size>    | Memory size, default = 2 (mebibytes).                   |
|           | -N           | Download to Nintendo Emulation system.                  |
|    Yes    | -O\<romfile> | Output a ROM file.                                      |
|           | -P\<addr>    | Set Printer port address (in hex), default = 0x378.     |
|           | -R           | Display ROM block information.                          |
|    Yes    | -S           | Display all public symbols.                             |
|           | -T\<type>    | Set ROM type (in hex), default = 0x7D.                  |
|           | -W\<prefix>  | Set prefix (Work directory) for object files.           |
|           | -Y           | Use secondary ADS backplane CIC.                        |
|           | -Z           | Generate a debugger MAP file.                           |
