#include <ctype.h>
#include <stdarg.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define STRINGIZE_DETAIL(x) #x
#define STRINGIZE(x) STRINGIZE_DETAIL(x)

typedef struct LinkData {
	char* Name;
	int32_t Value;
} LinkData;

typedef struct Calculation {
	int32_t Deep;
	int32_t Priority;
	int32_t Operation;
	int32_t Value;
} Calculation;

typedef enum {
	Success = 0,
	BadCLIUsage = 64
} BSDExitCodes;

// Default values as stated in usage text
uint8_t s_ioBuffersKiB = 10;
char* s_defaultExtension = ".SOB";
uint16_t s_fabcardPort = 0x290;
uint16_t s_stringHashSize = 256;
uint8_t s_memoryMiB = 2;
uint16_t s_printerPort = 0x378;
uint8_t s_romType = 0x7D;

bool s_verbose; // = false;

void OutputLogo()
{
	puts("ArgLink Re-Rewrite\t\t\t(c) 2025 Repzilon\n"
"Based on ARGLINK_REWRITE\t\t(c) 2017 LuigiBlood\n"
"For imitating ArgLink SFX v1.11x\t(c) 1993 Argonaut Software Ltd.\n"
);
}

void OutputUsage()
{
	puts("ARGLINK [opts] <obj1> [opts] obj2 [opts] obj3 [opts] obj4 ...\n"
"All object file names are appended with .SOB if no extension is specified.\n"
"CLI options can be placed in the ALFLAGS environment variable.\n"
"A filename preceded with @ is a file list.\n"
"Note: DOS has a 126-char limit on parameters, so please use the @ option.\n"
"\n"
"** Available Options are:\n"
"** -B<kib>\t- Set file input/output buffers (0-31), default = 10 KiB.\n"
"** -E<.ext>\t- Change default file extension, default = '.SOB'.\n"
"** -O<romfile>\t- Output a ROM file.\n"
"\n"
"** Re-rewrite Added Options are:\n"
"** -Q\t\t- Turn off banner on startup.\n"
"** -V\t\t- Turn on LuigiBlood's ARGLINK_REWRITE output to std. error.\n"
"\n"
"** Unimplemented Options are:\n"
"** -A1\t\t- Download to ADS SuperChild1 hardware.\n"
"** -A2\t\t- Download to ADS SuperChild2 hardware.\n"
"** -C\t\t- Duplicate public warnings on.\n"
"** -D\t\t- Download to ramboy.\n"
"** -F<addr>\t- Set Fabcard port address (in hex), default = 0x290.\n"
"** -H<size>\t- String hash size, default = 256.\n"
"** -I\t\t- Display file information while loading.\n"
"** -L<size>\t- Display used ROM layout (size is in KiB).\n"
"** -M<size>\t- Memory size, default = 2 (mebibytes).\n"
"** -N\t\t- Download to Nintendo Emulation system.\n"
"** -P<addr>\t- Set Printer port address (in hex), default = 0x378.\n"
"** -R\t\t- Display ROM block information.\n"
"** -S\t\t- Display all public symbols.\n"
"** -T<type>\t- Set ROM type (in hex), default = 0x7D.\n"
"** -W<prefix>\t- Set prefix (Work directory) for object files.\n"
"** -Y\t\t- Use secondary ADS backplane CIC.\n"
"** -Z\t\t- Generate a debugger MAP file.\n"
);
}

int32_t Search(LinkData* link, size_t linkCount, char* name)
{
	int32_t nameId = -1;
	for (int32_t i = 0; i < linkCount; i++) {
		if (strcmp(link[i].Name, name) == 0) {
			nameId = i;
		}
	}

	return nameId;
}

char* GetNameChars(FILE* fileSob, size_t* nametempCount)
{
	char* nametemp = NULL; *nametempCount = 0;
	char check = 'A';
	while (check != 0) {
		{ int whatRead = fgetc(fileSob); if (whatRead == EOF) { puts("ArgLink error: reading byte from fileSob failed, source code line " STRINGIZE(__LINE__)); exit(74); } else { check = (char)whatRead; } };
		if (check != 0) {
			(*nametempCount)++; nametemp = (char*)realloc(nametemp, *nametempCount * sizeof(char)); nametemp[*nametempCount - 1] = check;
		}
	}

	return nametemp;
}

char* GetName(FILE* fileSob)
{
	size_t Count; char* functionResult = GetNameChars(fileSob, &Count); char* resultString = (char*)calloc(Count + 1, sizeof(char)); memmove(resultString, functionResult, Count); return resultString;
}

bool SOBJWasRead(FILE* fileSob)
{
	return fgetc(fileSob) == 0x53 //S
		&& fgetc(fileSob) == 0x4F //O
		&& fgetc(fileSob) == 0x42 //B
		&& fgetc(fileSob) == 0x4A; //J
}

Calculation* InitCalculation(int32_t deep, int32_t priority, int32_t operation, int32_t value)
{
	Calculation* calctemp = (Calculation*)calloc(1, sizeof(Calculation));
	calctemp->Deep = deep;
	calctemp->Priority = priority;
	calctemp->Operation = operation;
	calctemp->Value = value;
	return calctemp;
}

int32_t ReadLEInt32(FILE* fileSob)
{
	return fgetc(fileSob) | (fgetc(fileSob) << 8) | (fgetc(fileSob) << 16) |
		(fgetc(fileSob) << 24);
}

void Recopy(FILE* source, size_t size, FILE* destination, int32_t offset)
{
	uint8_t* buffer = (uint8_t*)calloc((size_t)size, sizeof(uint8_t));
	fread(buffer, sizeof(uint8_t), size, source);
	fseek(destination, offset, SEEK_SET);
	fwrite(buffer, sizeof(uint8_t), size, destination);
}

void LuigiOut(char* text)
{
	if (s_verbose) {
		fputs(text, stderr);fputs("\n", stderr);
	}
}

void LuigiFormat(char* format, ...)
{
	if (s_verbose) {
		va_list ellipsis; va_start(ellipsis, format); vfprintf(stderr, format, ellipsis); va_end(ellipsis); fputs("\n", stderr);
	}
}

bool IsSimpleFlag(char flag, char* argument)
{
	if (strlen(argument) == 2) {
		char c0 = argument[0];
		if ((c0 == '-') || (c0 == '/')) {
			char c1 = argument[1];
			return (c1 == toupper(flag)) || (c1 == tolower(flag));
		}
	}

	return false;
}

bool IsStringFlag(char flag, char* argument, char** value)
{
	if (strlen(argument) > 2) {
		char c0 = argument[0];
		if ((c0 == '-') || (c0 == '/')) {
			char c1 = argument[1];
			if ((c1 == toupper(flag)) || (c1 == tolower(flag))) {
				*value = (argument + 2);
				return true;
			}
		}
	}

	*value = NULL;
	return false;
}

bool IsByteFlag(char flag, char* argument, uint8_t min, uint8_t max, uint8_t* value)
{
	if (strlen(argument) > 2) {
		char c0 = argument[0];
		if ((c0 == '-') || (c0 == '/')) {
			char c1 = argument[1];
			if ((c1 == toupper(flag)) || (c1 == tolower(flag))) {
				uint8_t parsed;
				if (sscanf((argument + 2), "%hhu", &parsed)) {
					if (parsed < min) {
						*value = min;
						printf("ArgLink warning: switch -%c set to %hhu\n", flag, min);
					} else if (parsed > max) {
						*value = max;
						printf("ArgLink warning: switch -%c set to %hhu\n", flag, max);
					} else {
						*value = parsed;
					}

					return true;
				}
			}
		}
	}

	*value = 0;
	return false;
}

char* ExtensionOf(char* path)
{
	char* dot = strrchr(path, '.'); return (!dot || dot == path) ? NULL : dot;
}

char* AppendExtensionIfAbsent(char* argSfxObjectFile)
{
	// Note: it is written this way to ease translation to C (passing char* in call chains is hard)
	char* ext = ExtensionOf(argSfxObjectFile);
	if (((ext == NULL) || (strlen(ext) < 1))) {
		char* corrected;
		int nbytes = snprintf(NULL, 0, "%s%s", argSfxObjectFile, s_defaultExtension); if (nbytes < 0) { puts("ArgLink error: cannot evaluate length with snprintf, source code line " STRINGIZE(__LINE__)); exit(70); } else { nbytes++; corrected = (char*)calloc((size_t)nbytes, sizeof(char)); if (corrected == NULL) { puts("ArgLink error: cannot allocate memory, source code line " STRINGIZE(__LINE__)); exit(70); } else { snprintf(corrected, (size_t)nbytes, "%s%s", argSfxObjectFile, s_defaultExtension); } }
		return corrected;
	} else {
		return argSfxObjectFile;
	}
}

void InputSobStepOne(int32_t i, FILE* fileOut, FILE* fileSob)
{
	int64_t start = ftell(fileSob);
	int32_t offset = ReadLEInt32(fileSob);
	size_t size = ReadLEInt32(fileSob);
	int32_t type = fgetc(fileSob); if (type == EOF) { puts("ArgLink error: reading byte from fileSob failed, source code line " STRINGIZE(__LINE__)); exit(74); };

	LuigiFormat("%X: 0x%X /// Size: 0x%X / Offset 0x%X / Type %X", i,
		start, size, offset, type);

	if (type == 0) {
		//Data
		Recopy(fileSob, size, fileOut, offset);
	} else if (type == 1) {
		//External File
		if (fgetc(fileSob) == EOF) { puts("ArgLink error: reading byte from fileSob failed, source code line " STRINGIZE(__LINE__)); exit(74); };
		if (fgetc(fileSob) == EOF) { puts("ArgLink error: reading byte from fileSob failed, source code line " STRINGIZE(__LINE__)); exit(74); };

		//Get file path
		char* filepath = GetName(fileSob);
		LuigiFormat("--Open External File: %s\n", filepath);
		FILE* fileExt = fopen(filepath, "rb"); uint16_t fileExtZone = s_ioBuffersKiB * 1024; char* fileExtBuffer = (fileExtZone > 0) ? (char*)calloc(fileExtZone, sizeof(char)) : NULL; setvbuf(fileExt, fileExtBuffer, fileExtBuffer ? _IOFBF : _IONBF, fileExtZone);
		Recopy(fileExt, size, fileOut, offset);
		fclose(fileExt); free(fileExtBuffer);
	}
}

void InputSobStepTwo(FILE* fileSob, LinkData* link, size_t* linkCount)
{
	do {
		LinkData* linktemp = (LinkData*)calloc(1, sizeof(LinkData));
		size_t nametempCount; char* nametemp = GetNameChars(fileSob, &nametempCount);

		if (nametempCount <= 0) {
			break;
		}

		char* nametempString = (char*)calloc(nametempCount + 1, sizeof(char)); memmove(nametempString, nametemp, nametempCount);linktemp->Name = nametempString;
		linktemp->Value = fgetc(fileSob) | (fgetc(fileSob) << 8) | (fgetc(fileSob) << 16);
		LuigiFormat("--%s : %X\n", linktemp->Name, linktemp->Value);
		(*linkCount)++; link = (LinkData*)realloc(link, *linkCount * sizeof(LinkData)); link[*linkCount - 1] = *linktemp;
	} while (fgetc(fileSob) == 0);
}

void PerformLink(char* sobjFile, FILE* fileOut, int64_t startLink[], int32_t n, LinkData* link, size_t* linkCount)
{
	FILE* fileSob = fopen(sobjFile, "rb"); uint16_t fileSobZone = s_ioBuffersKiB * 1024; char* fileSobBuffer = (fileSobZone > 0) ? (char*)calloc(fileSobZone, sizeof(char)) : NULL; setvbuf(fileSob, fileSobBuffer, fileSobBuffer ? _IOFBF : _IONBF, fileSobZone);
	fseek(fileSob, 0, SEEK_END); int64_t fileSize = ftell(fileSob);
	LuigiFormat("Open %s\n", sobjFile);
	fseek(fileSob, 0, SEEK_SET);
	if (SOBJWasRead(fileSob)) {
		int64_t startIndex = startLink[n];
		if (startIndex < (fileSize - 3)) {
			LuigiFormat("%X\n", startIndex);
			fseek(fileSob, startIndex, SEEK_SET);
			while (ftell(fileSob) < fileSize - 1) {
				LuigiFormat("-%X\n", ftell(fileSob));
				char* name = GetName(fileSob);
				int32_t nameId = Search(link, *linkCount, name);

				Calculation* linkcalc = NULL; size_t linkcalcCount = 0;
				Calculation* calctemp = InitCalculation(-1, 0, 0, link[nameId].Value);
				linkcalcCount++; linkcalc = (Calculation*)realloc(linkcalc, linkcalcCount * sizeof(Calculation)); linkcalc[linkcalcCount - 1] = *calctemp;

				LuigiFormat("--%s : %X\n", name, link[nameId].Value);

				if (fgetc(fileSob) != 0) {
					fseek(fileSob, -1, SEEK_CUR);
					name = GetName(fileSob);
					nameId = Search(link, *linkCount, name);
					LuigiFormat("----%s : %X\n", name, link[nameId].Value);
					if (fgetc(fileSob) == EOF) { puts("ArgLink error: reading byte from fileSob failed, source code line " STRINGIZE(__LINE__)); exit(74); };
				}

				ReadLEInt32(fileSob);
				ReadLEInt32(fileSob);

				//List all operations
				uint8_t calccheck1; { int whatRead = fgetc(fileSob); if (whatRead == EOF) { puts("ArgLink error: reading byte from fileSob failed, source code line " STRINGIZE(__LINE__)); exit(74); } else { calccheck1 = (uint8_t)whatRead; } };
				uint8_t calccheck2; { int whatRead = fgetc(fileSob); if (whatRead == EOF) { puts("ArgLink error: reading byte from fileSob failed, source code line " STRINGIZE(__LINE__)); exit(74); } else { calccheck2 = (uint8_t)whatRead; } };
				while (calccheck1 != 0 && calccheck2 != 0) {
					// Note: ReadInt32() introduces a side effect and must be called
					// under any circumstances
					calctemp = InitCalculation((calccheck1 & 0x70) >> 4, calccheck1 & 0x3,
						calccheck2, ReadLEInt32(fileSob));
					if (calccheck1 > 0x80) {
						calctemp->Value = link[nameId].Value;
					}

					{ int whatRead = fgetc(fileSob); if (whatRead == EOF) { puts("ArgLink error: reading byte from fileSob failed, source code line " STRINGIZE(__LINE__)); exit(74); } else { calccheck1 = (uint8_t)whatRead; } };
					{ int whatRead = fgetc(fileSob); if (whatRead == EOF) { puts("ArgLink error: reading byte from fileSob failed, source code line " STRINGIZE(__LINE__)); exit(74); } else { calccheck2 = (uint8_t)whatRead; } };
					linkcalcCount++; linkcalc = (Calculation*)realloc(linkcalc, linkcalcCount * sizeof(Calculation)); linkcalc[linkcalcCount - 1] = *calctemp;
				}

				//All operations have been found, now do the calculations
				while (linkcalcCount > 1) {
					//Check for highest deep
					int32_t highestdeep = -1;
					int32_t highestdeepidx = -1;
					int32_t i;
					for (i = 1; i < linkcalcCount; i++) {
						//Get the first highest one
						if (highestdeep < linkcalc[i].Deep) {
							highestdeep = linkcalc[i].Deep;
							highestdeepidx = i;
						}
					}

					//Check for highest priority
					int32_t highestpri = -1;
					int32_t highestpriidx = -1;
					for (i = highestdeepidx; i < linkcalcCount; i++) {
						//Get the first highest one
						if (linkcalc[i].Deep != highestdeep || highestpri > linkcalc[i].Priority) {
							break;
						}

						if (highestpri < linkcalc[i].Priority && linkcalc[i].Deep == highestdeep) {
							highestpri = linkcalc[i].Priority;
							highestpriidx = i;
						}
					}

					//Check for latest deep
					int32_t calcidx = -1;
					for (i = highestpriidx; i >= 0; i--) {
						//Get the first one that comes
						if (highestdeep > linkcalc[i].Deep || highestpri > linkcalc[i].Priority) {
							calcidx = i;
							break;
						}
					}

					//Do the calculation
					calctemp = &linkcalc[calcidx];

					int32_t operation = linkcalc[highestpriidx].Operation;
					int32_t calcValue = linkcalc[highestpriidx].Value;
					if (operation == 0x02) { //Shift Right
						LuigiFormat("%X >> %X\n", calctemp->Value, calcValue);
						calctemp->Value >>= calcValue;
					} else if (operation == 0x0C) { //Add
						LuigiFormat("%X + %X\n", calctemp->Value, calcValue);
						calctemp->Value += calcValue;
					} else if (operation == 0x0E) { //Sub
						LuigiFormat("%X - %X\n", calctemp->Value, calcValue);
						calctemp->Value -= calcValue;
					} else if (operation == 0x10) { //Mul
						LuigiFormat("%X * %X\n", calctemp->Value, calcValue);
						calctemp->Value *= calcValue;
					} else if (operation == 0x12) { //Div
						LuigiFormat("%X / %X\n", calctemp->Value, calcValue);
						calctemp->Value /= calcValue;
					} else if (operation == 0x16) { //And
						LuigiFormat("%X & %X\n", calctemp->Value, calcValue);
						calctemp->Value &= calcValue;
					} else {
						LuigiFormat("ERROR (CALCULATION) [%X]\n", operation);
					}

					linkcalc[calcidx] = *calctemp;
					size_t after = linkcalcCount - 1 - highestpriidx;
							if (after > 0) {
								memmove(&(linkcalc[highestpriidx]), &(linkcalc[highestpriidx + 1]), after * sizeof(Calculation));
							}
							linkcalcCount--;
							linkcalc = (Calculation*)realloc(linkcalc, linkcalcCount * sizeof(Calculation));
				}

				//And then put the data in
				int32_t offset = ReadLEInt32(fileSob);
				fseek(fileOut, offset + 1, SEEK_SET);
				LuigiFormat("----%X : %X\n", offset, linkcalc[0].Value);
				uint8_t format; { int whatRead = fgetc(fileSob); if (whatRead == EOF) { puts("ArgLink error: reading byte from fileSob failed, source code line " STRINGIZE(__LINE__)); exit(74); } else { format = (uint8_t)whatRead; } };
				int32_t firstValue = linkcalc[0].Value;
				if (format == 0x00) { // 8-bit
					fputc(firstValue, fileOut);
				} else if (format == 0x02) { // 16-bit
					fputc(firstValue & 0xff, fileOut); fputc(firstValue >> 8, fileOut);
				} else if (format == 0x04) { // 24-bit
					fputc(firstValue & 0xff, fileOut); fputc(firstValue >> 8, fileOut);
					fputc((firstValue >> 16), fileOut);
				} else if (format == 0x0E) { // 8-bit
					fseek(fileOut, offset, SEEK_SET);
					fputc(firstValue, fileOut);
				} else if (format == 0x10) { // 16-bit
					fseek(fileOut, offset, SEEK_SET);
					fputc(firstValue & 0xff, fileOut); fputc(firstValue >> 8, fileOut);
				} else {
					LuigiOut("ERROR (OUTPUT)");
				}
			}
		} else {
			LuigiOut("NOTHING");
		}
	}
	fclose(fileSob); free(fileSobBuffer);
}

int32_t main(int argc, char* argv[])
{
	// TODO : get, split and parse ALFLAGS environment variable
	// Do it before parsing command line so command line can override environment

	// Parse command line
	// "Sob" is the default file extension for ArgSfxX output, not to insult anybody
	int32_t idx;
	bool* areSobs = (bool*)calloc((size_t)(argc - 1), sizeof(bool));
	int32_t totalSobs = (argc - 1);
	bool showLogo = true;
	char* romFile = NULL;
	char* passed;
	uint8_t parsedU8;
	for (idx = 0; idx < (argc - 1); idx++) {
		passed = NULL;
		if (IsSimpleFlag('V', argv[1 + idx])) {
			s_verbose = true;
			areSobs[idx] = false;
			totalSobs--;
		} else if (IsSimpleFlag('Q', argv[1 + idx])) {
			showLogo = false;
			areSobs[idx] = false;
			totalSobs--;
		} else if (IsStringFlag('O', argv[1 + idx], &passed)) {
			romFile = passed;
			areSobs[idx] = false;
			totalSobs--;
		} else if (IsByteFlag('B', argv[1 + idx], 0, 31, &parsedU8)) {
			s_ioBuffersKiB = parsedU8;
			areSobs[idx] = false;
			totalSobs--;
		} else if (IsStringFlag('E', argv[1 + idx], &passed)) {
			if ((passed != NULL) && (strlen(passed) >= 2) && (passed[0] == '.')) {
				s_defaultExtension = passed;
			} else {
				puts("ArgLink warning: default extension override must start with a dot.");
			}
			areSobs[idx] = false;
			totalSobs--;
		} else {
			areSobs[idx] = true;
		}
	}

	if (showLogo) {
		OutputLogo();
	}

	if (s_verbose) {
		for (idx = 0; idx < (argc - 1); idx++) {
			fputs(argv[1 + idx], stderr);fputs("\n", stderr);
		}
	}

	if (totalSobs < 1) {
		OutputUsage();
		return (int32_t)BadCLIUsage;
	} else if (((romFile == NULL) || (strlen(romFile) < 1))) {
		// Standard error is reserved for verbose output
		puts("ArgLink error: no ROM file was specified.");
		return (int32_t)BadCLIUsage;
	} else {
		FILE* fileSob;
		FILE* fileOut = fopen(romFile, "wb"); uint16_t fileOutZone = s_ioBuffersKiB * 1024; char* fileOutBuffer = (fileOutZone > 0) ? (char*)calloc(fileOutZone, sizeof(char)) : NULL; setvbuf(fileOut, fileOutBuffer, fileOutBuffer ? _IOFBF : _IONBF, fileOutZone);
		// Fill Output file to 1 MiB
		puts("Constructing ROM Image.");
		fseek(fileOut, 0, SEEK_SET);
		for (idx = 0; idx < 0x100000; idx++) {
			fputc(0xFF, fileOut);
		}

		// Steps 1 & 2: Input all data and list all links
		puts("Processing Externals.");
		LinkData* link = NULL; size_t linkCount = 0;
		int64_t* startLink = (int64_t*)calloc((size_t)totalSobs, sizeof(int64_t));
		int32_t firstSob = -1;
		int32_t n = 0;
		char* sobjFile;
		for (idx = 0; idx < (argc - 1); idx++) {
			if (areSobs[idx]) {
				if (firstSob < 0) {
					firstSob = idx;
				}

				//Check if SOB file is indeed a SOB file
				sobjFile = AppendExtensionIfAbsent(argv[1 + idx]);
				fileSob = fopen(sobjFile, "rb"); uint16_t fileSobZone = s_ioBuffersKiB * 1024; char* fileSobBuffer = (fileSobZone > 0) ? (char*)calloc(fileSobZone, sizeof(char)) : NULL; setvbuf(fileSob, fileSobBuffer, fileSobBuffer ? _IOFBF : _IONBF, fileSobZone);
				LuigiFormat("Open %s\n", sobjFile);
				fseek(fileSob, 0, SEEK_SET);
				if (SOBJWasRead(fileSob)) {
					if (fgetc(fileSob) == EOF) { puts("ArgLink error: reading byte from fileSob failed, source code line " STRINGIZE(__LINE__)); exit(74); };
					if (fgetc(fileSob) == EOF) { puts("ArgLink error: reading byte from fileSob failed, source code line " STRINGIZE(__LINE__)); exit(74); };
					int32_t count = fgetc(fileSob); if (count == EOF) { puts("ArgLink error: reading byte from fileSob failed, source code line " STRINGIZE(__LINE__)); exit(74); };
					if (fgetc(fileSob) == EOF) { puts("ArgLink error: reading byte from fileSob failed, source code line " STRINGIZE(__LINE__)); exit(74); };

					for (int32_t i = 0; i < count; i++) {
						// Step 1: Input all data into output
						InputSobStepOne(i, fileOut, fileSob);
					}

					// Step 2: Get all extern names and values
					InputSobStepTwo(fileSob, link, &linkCount);

					startLink[n] = ftell(fileSob);
					n++;
					fclose(fileSob); free(fileSobBuffer);
					//Repeat
				}
			}
		}

		// Step 3: Link everything
		puts("Writing Image.");
		LuigiOut("----LINK");
		n = 0;
		for (idx = firstSob; idx < (argc - 1); idx++) {
			if (areSobs[idx]) {
				sobjFile = AppendExtensionIfAbsent(argv[1 + idx]);
				PerformLink(sobjFile, fileOut, startLink, n, link, &linkCount);
				n++;
			}
		}

		fseek(fileOut, 0, SEEK_END); int64_t finalSize = ftell(fileOut);
		finalSize = (finalSize / 1024) + ((finalSize % 1024) > 0 ? 1 : 0);
		printf("| Publics: %u\tFiles: %d\tROM Size: %lldKiB |\n", linkCount, totalSobs, finalSize);

		fclose(fileOut); free(fileOutBuffer);

		return (int32_t)Success;
	}
}
