#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

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


// Default values as stated in usage text
uint8_t s_ioBuffersKiB = 10;
char* s_defaultExtension = ".SOB";
uint16_t s_fabcardPort = 0x290;
uint8_t s_memoryMiB = 2;
uint16_t s_printerPort = 0x378;
uint8_t s_romType = 0x7D;

void OutputLogo()
{
	puts("ArgLink Re-Rewrite\t\t\t(c) 2025 Repzilon\n"
"Based on ARGLINK_REWRITE\t\t(c) 2017 LuigiBlood\n"
"For imitating ArgLink SFX v1.11x\t(c) 1993 Argonaut Software Ltd.\n"
);
}

void OutputUsage()
{
	puts("ARGLINK [opts] obj1 [opts] obj2 [opts] obj3 [opts] obj4 ...\n"
"All object file names are appended with .SOB if no extension is specified.\n"
"CLI options can be placed in the ALFLAGS environment variable.\n"
"A filename preceded with @ is a file list.\n"
"Please note: DOS has a limit on parameters, so please use the @ option.\n"
"** Unimplemented Options are:\n"
"** -A1\t\t- Download to ADS SuperChild1 hardware.\n"
"** -A2\t\t- Download to ADS SuperChild2 hardware.\n"
"** -B\t\t- Set file input/output buffers (0-31), default = 10 KiB.\n"
"** -C\t\t- Duplicate public warnings on.\n"
"** -D\t\t- Download to ramboy.\n"
"** -E <ext>\t- Change default file extension, default = '.SOB'.\n"
"** -F[<addr>]\t- Set Fabcard port address (in hex), default = 0x290.\n"
"** -H <size>\t- String hash size, default = 256.\n"
"** -I\t\t- Display file information while loading.\n"
"** -L <size>\t- Display used ROM layout (size is in KiB).\n"
"** -M <size>\t- Memory size, default = 2 (mebibytes).\n"
"** -N\t\t- Download to Nintendo Emulation system.\n"
"** -O <romfile>\t- Output a ROM file.\n"
"** -P[<addr>]\t- Set Printer port address (in hex), default = 0x378.\n"
"** -R\t\t- Display ROM block information.\n"
"** -S\t\t- Display all public symbols.\n"
"** -T[<type>]\t- Set ROM type (in hex), default = 0x7D.\n"
"** -W <prefix>\t- Set prefix (Work directory) for object files.\n"
"** -Y\t\t- Use secondary ADS backplane CIC.\n"
"** -Z\t\t- Generate a debugger MAP file.\n"
);
}

int32_t Search(LinkData* link, int32_t linkCount, char* name)
{
	int32_t nameId = -1;
	for (int32_t i = 0; i < linkCount; i++) {
		if (strcmp(link[i].Name, name) == 0) {
			nameId = i;
		}
	}

	return nameId;
}

char* GetNameChars(FILE* fileSob, int32_t* nametempCount)
{
	char* nametemp = NULL; *nametempCount = 0;
	char check = 'A';
	while (check != 0) {
		check = fgetc(fileSob);
		if (check != 0) {
			(*nametempCount)++; nametemp = (char*)realloc(nametemp, *nametempCount * sizeof(char)); nametemp[*nametempCount - 1] = check;
		}
	}

	return nametemp;
}

char* GetName(FILE* fileSob)
{
	int32_t Count; char* functionResult = GetNameChars(fileSob, &Count); char* resultString = (char*)calloc(Count + 1, sizeof(char)); memmove(resultString, functionResult, Count); return resultString;
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

void Recopy(FILE* source, int32_t size, FILE* destination, int32_t offset)
{
	uint8_t* buffer = (uint8_t*)calloc(size, sizeof(uint8_t));
	fread(buffer, sizeof(uint8_t), size, source);
	fseek(destination, offset, SEEK_SET);
	fwrite(buffer, sizeof(uint8_t), size, destination);
}

int main(int argc, char* argv[])
{
	OutputLogo();
	int32_t i;
	for (i = 0; i < (argc - 1); i++) {
		puts(argv[1 + i]);
	}

	if ((argc - 1) < 2) {
		OutputUsage();
	} else {
		FILE* fileOut = fopen(argv[1 + 0], "wb");
		FILE* fileSob;
		FILE* fileExt;

		//Fill Output file to 1MB
		fseek(fileOut, 0, SEEK_SET);
		for (i = 0; i < 0x100000; i++) {
			fputc(0xFF, fileOut);
		}

		//SOB files, Step 1 & 2 - input all data and list all links
		LinkData* link = NULL; int32_t linkCount = 0;
		int32_t sobInput = (argc - 1) - 1;
		int64_t* startLink = (int64_t*)calloc(sobInput, sizeof(int64_t));
		int32_t idx;

		for (idx = 0; idx < sobInput; idx++) {
			//Check if SOB file is indeed a SOB file
			int32_t count;
			fileSob = fopen(argv[1 + idx + 1], "rb");
			printf("Open %s", argv[1 + idx + 1]);
			fseek(fileSob, 0, SEEK_SET);
			if (SOBJWasRead(fileSob)) {
				fgetc(fileSob);
				fgetc(fileSob);
				count = fgetc(fileSob);
				fgetc(fileSob);

				for (i = 0; i < count; i++) {
					//Step 1 - Input all data into output
					int64_t start = ftell(fileSob);
					int32_t offset = ReadLEInt32(fileSob);
					int32_t size = ReadLEInt32(fileSob);
					int32_t type = fgetc(fileSob);

					printf("%dX: 0x%dX /// Size: 0x%dX / Offset 0x%dX / Type %dX", i,
						start, size, offset, type);

					if (type == 0) {
						//Data
						Recopy(fileSob, size, fileOut, offset);
					} else if (type == 1) {
						//External File
						fgetc(fileSob);
						fgetc(fileSob);

						//Get file path
						char* filepath = GetName(fileSob);

						printf("--Open External File: %s", filepath);

						fileExt = fopen(filepath, "rb");
						Recopy(fileExt, size, fileOut, offset);

						fclose(fileExt);
					}
				}

				//Step 2 - Get all extern names and values
				do {
					LinkData* linktemp = (LinkData*)calloc(1, sizeof(LinkData));
					int32_t nametempCount; char* nametemp = GetNameChars(fileSob, &nametempCount);

					if (nametempCount <= 0) {
						break;
					}

					char* nametempString = (char*)calloc(nametempCount + 1, sizeof(char)); memmove(nametempString, nametemp, nametempCount);linktemp->Name = nametempString;
					linktemp->Value = fgetc(fileSob) | (fgetc(fileSob) << 8) | (fgetc(fileSob) << 16);
					printf("--%s : %dX", linktemp->Name, linktemp->Value);
					linkCount++; link = (LinkData*)realloc(link, linkCount * sizeof(LinkData)); link[linkCount - 1] = *linktemp;
				} while (fgetc(fileSob) == 0);

				startLink[idx] = ftell(fileSob);
				fclose(fileSob);
				//Repeat
			}
		}

		//Step 3 - Link everything
		puts("----LINK");
		for (idx = 0; idx < sobInput; idx++) {
			fileSob = fopen(argv[1 + idx + 1], "rb");
			fseek(fileSob, 0, SEEK_END); int64_t fileSize = ftell(fileSob);
			printf("Open %s", argv[1 + idx + 1]);
			fseek(fileSob, 0, SEEK_SET);
			if (SOBJWasRead(fileSob)) {
				if (startLink[idx] < (fileSize - 3)) {
					printf("%dX", startLink[idx]);
					fseek(fileSob, startLink[idx], SEEK_SET);
					while (ftell(fileSob) < fileSize - 1) {
						printf("-%dX", ftell(fileSob));
						char* name = GetName(fileSob);
						int32_t nameId = Search(link, linkCount, name);

						Calculation* linkcalc = NULL; int32_t linkcalcCount = 0;
						Calculation* calctemp = InitCalculation(-1, 0, 0, link[nameId].Value);
						linkcalcCount++; linkcalc = (Calculation*)realloc(linkcalc, linkcalcCount * sizeof(Calculation)); linkcalc[linkcalcCount - 1] = *calctemp;

						printf("--%s : %dX", name, link[nameId].Value);

						if (fgetc(fileSob) != 0) {
							fseek(fileSob, -1, SEEK_CUR);
							name = GetName(fileSob);
							nameId = Search(link, linkCount, name);
							printf("----%s : %dX", name, link[nameId].Value);
							fgetc(fileSob);
						}

						ReadLEInt32(fileSob);
						ReadLEInt32(fileSob);

						//List all operations
						uint8_t calccheck1 = fgetc(fileSob);
						uint8_t calccheck2 = fgetc(fileSob);
						while (calccheck1 != 0 && calccheck2 != 0) {
							// Note: ReadInt32() introduces a side effect and must be called
							// under any circumstances
							calctemp = InitCalculation((calccheck1 & 0x70) >> 4, calccheck1 & 0x3,
								calccheck2, ReadLEInt32(fileSob));
							if (calccheck1 > 0x80) {
								calctemp->Value = link[nameId].Value;
							}

							calccheck1 = fgetc(fileSob);
							calccheck2 = fgetc(fileSob);
							linkcalcCount++; linkcalc = (Calculation*)realloc(linkcalc, linkcalcCount * sizeof(Calculation)); linkcalc[linkcalcCount - 1] = *calctemp;
						}

						//All operations have been found, now do the calculations
						while (linkcalcCount > 1) {
							//Check for highest deep
							int32_t highestdeep = -1;
							int32_t highestdeepidx = -1;
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
								printf("%dX >> %dX", calctemp->Value, calcValue);
								calctemp->Value >>= calcValue;
							} else if (operation == 0x0C) { //Add
								printf("%dX + %dX", calctemp->Value, calcValue);
								calctemp->Value += calcValue;
							} else if (operation == 0x0E) { //Sub
								printf("%dX - %dX", calctemp->Value, calcValue);
								calctemp->Value -= calcValue;
							} else if (operation == 0x10) { //Mul
								printf("%dX * %dX", calctemp->Value, calcValue);
								calctemp->Value *= calcValue;
							} else if (operation == 0x12) { //Div
								printf("%dX / %dX", calctemp->Value, calcValue);
								calctemp->Value /= calcValue;
							} else if (operation == 0x16) { //And
								printf("%dX & %dX", calctemp->Value, calcValue);
								calctemp->Value &= calcValue;
							} else {
								printf("ERROR (CALCULATION) [%dX]", operation);
							}

							linkcalc[calcidx] = *calctemp;
							int32_t after = linkcalcCount - 1 - highestpriidx;
							if (after > 0) {
								memmove(&(linkcalc[highestpriidx]), &(linkcalc[highestpriidx + 1]), after * sizeof(Calculation));
							}
							linkcalcCount--;
							linkcalc = (Calculation*)realloc(linkcalc, linkcalcCount * sizeof(Calculation));
						}

						//And then put the data in
						int32_t offset = ReadLEInt32(fileSob);
						fseek(fileOut, offset + 1, SEEK_SET);
						printf("----%dX : %dX", offset, linkcalc[0].Value);
						uint8_t format = fgetc(fileSob);
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
							puts("ERROR (OUTPUT)");
						}
					}
				} else {
					puts("NOTHING");
				}
			}
		}
	}
}
