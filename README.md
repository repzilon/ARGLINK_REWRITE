# ArgLink SFX Re-Rewrite
Argonaut SNES Linker Rewrite, remixed.

## Goals and platforms
* End goal: produce a C version of the linker that is functionally compatible with the original one written by Argonaut software as used in (Ultra)StarFox (2).
* Host OS: DOS with DJGPP, Mac OS X, Linux, Windows (98! and newer)
* Host CPUs: x86, x86_64, arm64, i.e. both non-exotic (looking at you Sparc, MIPS, PPC, Alpha, SuperH, HP-PA and IA64) retro and modern CPUs  

## Roadmap
1. (Started) Implement command line options, starting fron LuigiBlood's C# rewrite, in C#.
2. (In progress) Write a program to translate C# code to compilable C.
3. Correct the translated C program to a have a working version.
4. Correct the translator program accordingly.

Why write a translator program? Setting up and using a C# debugger is much easier than a C debugger. It also allows to separate concerns during development: general logic in C#, manual memory management in C.   

## Programming standards
To ease translation to C, ARGLINK_REWRITE C# code follows some guidelines:
* Single C# file (like LuigiBlood's code)
* Imperative approach (like LuigiBlood's code)
* Main method goes last, and callee methods are put before their callers (so no need to generate prototypes or headers for C)
* Target .NET Framework 2.0 (Windows 98 compatibility)
* Use language version 2 ([simple] generics allowed, but neither lambdas, async nor other fancy features)

C version targets C99 (to have single line comments and variable declarations anywhere, just as in C#)

## Original command line options
| Switch        | Description                                             |
|---------------|---------------------------------------------------------|
| -A1           | Download to ADS SuperChild1 hardware.                   |
| -A2           | Download to ADS SuperChild2 hardware.                   |
| -B            | Set file input/output buffers (0-31), default = 10 KiB. |
| -C            | Duplicate public warnings on.                           |
| -D            | Download to ramboy.                                     |
| -E \<ext>     | Change default file extension, default = '.SOB'.        |
| -F[\<addr>]   | Set Fabcard port address (in hex), default = 0x290.     |
| -H \<size>    | String hash size, default = 256.                        |
| -I            | Display file information while loading.                 |
| -L \<size>    | Display used ROM layout (size is in KiB).               |
| -M \<size>    | Memory size, default = 2 (mebibytes).                   |
| -N            | Download to Nintendo Emulation system.                  |
| -O \<romfile> | Output a ROM file.                                      |
| -P[\<addr>]   | Set Printer port address (in hex), default = 0x378.     |
| -R            | Display ROM block information.                          |
| -S            | Display all public symbols.                             |
| -T[\<type>]   | Set ROM type (in hex), default = 0x7D.                  |
| -W \<prefix>  | Set prefix (Work directory) for object files.           |
| -Y            | Use secondary ADS backplane CIC.                        |
| -Z            | Generate a debugger MAP file.                           |
