using System;
using System.Collections.Generic;
using System.IO;
using CSize = System.Int32;

// ReSharper disable UseSymbolAlias
// ReSharper disable SuggestVarOrType_SimpleTypes
// ReSharper disable SuggestVarOrType_Elsewhere
// ReSharper disable SuggestVarOrType_BuiltInTypes
// ReSharper disable UseObjectOrCollectionInitializer
#pragma warning disable CC0001  // You should use 'var' whenever possible.
#pragma warning disable CC0105  // You should use 'var' whenever possible.
#pragma warning disable U2U1104 // Do not use composite formatting to concatenate strings
#pragma warning disable HAA0601 // Value type to reference type conversion causing boxing allocation
#pragma warning disable HAA0101 // Array allocation for params parameter

namespace ARGLINK_REWRITE
{
	internal struct LinkData
	{
		public string Name;
		public int Value;
	}

	internal struct Calculation
	{
		public int Deep;
		public int Priority;
		public int Operation;
		public int Value;
	}

	internal enum BSDExitCodes : byte
	{
		Success = 0,
		BadCLIUsage = 64
	}

	internal static class Program
	{
		// Default values as stated in usage text
		// ReSharper disable InconsistentNaming
		private static byte s_ioBuffersKiB = 10;
		private static string s_defaultExtension = ".SOB";
		private static ushort s_fabcardPort = 0x290;
		private static ushort s_stringHashSize = 256;
		private static byte s_memoryMiB = 2;
		private static ushort s_printerPort = 0x378;
		private static byte s_romType = 0x7D;

		private static bool s_verbose; // = false;
		// ReSharper restore InconsistentNaming

		#region Utility methods
		private static void OutputLogo()
		{
			Console.WriteLine(@"ArgLink Re-Rewrite			(c) 2025 Repzilon
Based on ARGLINK_REWRITE		(c) 2017 LuigiBlood
For imitating ArgLink SFX v1.11x	(c) 1993 Argonaut Software Ltd.");
		}

		private static void OutputUsage()
		{
			Console.WriteLine(@"ARGLINK [opts] <obj1> [opts] obj2 [opts] obj3 [opts] obj4 ...
All object file names are appended with .SOB if no extension is specified.
CLI options can be placed in the ALFLAGS environment variable.
A filename preceded with @ is a file list.
Note: DOS has a 126-char limit on parameters, so please use the @ option.

** Available Options are:
** -B<kib>	- Set file input/output buffers (0-31), default = 10 KiB.
** -E<.ext>	- Change default file extension, default = '.SOB'.
** -O<romfile>	- Output a ROM file.

** Re-rewrite Added Options are:
** -Q		- Turn off banner on startup.
** -V		- Turn on LuigiBlood's ARGLINK_REWRITE output to std. error.
** -X<file>	- Export public symbols to a text file, one per line

** Unimplemented Options are:
** -A1		- Download to ADS SuperChild1 hardware.
** -A2		- Download to ADS SuperChild2 hardware.
** -C		- Duplicate public warnings on.
** -D		- Download to ramboy.
** -F<addr>	- Set Fabcard port address (in hex), default = 0x290.
** -H<size>	- String hash size, default = 256.
** -I		- Display file information while loading.
** -L<size>	- Display used ROM layout (size is in KiB).
** -M<size>	- Memory size, default = 2 (mebibytes).
** -N		- Download to Nintendo Emulation system.
** -P<addr>	- Set Printer port address (in hex), default = 0x378.
** -R		- Display ROM block information.
** -S		- Display all public symbols.
** -T<type>	- Set ROM type (in hex), default = 0x7D.
** -W<prefix>	- Set prefix (Work directory) for object files.
** -Y		- Use secondary ADS backplane CIC.
** -Z		- Generate a debugger MAP file.");
		}

		private static int Search(List<LinkData> link, string name)
		{
			int nameId = -1;
			// ReSharper disable once RedundantCast
			for (int i = 0; i < (int)link.Count; i++) { // Cast for MSVC
				if (link[i].Name.Equals(name)) {
					nameId = i;
				}
			}

			return nameId;
		}

		private static List<char> GetNameChars(BinaryReader fileSob)
		{
			List<char> nametemp = new List<char>();
			char       check    = 'A';
			while (check != 0) {
				check = fileSob.ReadChar();
				if (check != 0) {
					nametemp.Add(check);
				}
			}

			return nametemp;
		}

		private static string GetName(BinaryReader fileSob)
		{
			return new String(GetNameChars(fileSob).ToArray());
		}

		private static bool SOBJWasRead(BinaryReader fileSob)
		{
			return fileSob.ReadByte() == 0x53     //S
				   && fileSob.ReadByte() == 0x4F  //O
				   && fileSob.ReadByte() == 0x42  //B
				   && fileSob.ReadByte() == 0x4A; //J
		}

		private static Calculation InitCalculation(int deep, int priority, int operation, int value)
		{
			Calculation calctemp = new Calculation();
			calctemp.Deep      = deep;
			calctemp.Priority  = priority;
			calctemp.Operation = operation;
			calctemp.Value     = value;
			return calctemp;
		}

		private static int ReadLEInt32(BinaryReader fileSob)
		{
			return fileSob.ReadByte() | (fileSob.ReadByte() << 8) | (fileSob.ReadByte() << 16) |
				   (fileSob.ReadByte() << 24);
		}

		private static void Recopy(BinaryReader source, CSize size, BinaryWriter destination, int offset)
		{
			byte[] buffer = new byte[size];
			source.Read(buffer, 0, size);
			destination.Seek(offset, SeekOrigin.Begin);
			destination.Write(buffer, 0, size);
		}
		#endregion

		#region Verbose output
		private static void LuigiOut(string text)
		{
			if (s_verbose) {
				Console.Error.WriteLine(text);
			}
		}

		private static void LuigiFormat(string format, params object[] ellipsis)
		{
			if (s_verbose) {
				Console.Error.WriteLine(format, ellipsis);
			}
		}
		#endregion

		#region Command line parsing
		private static bool IsSimpleFlag(char flag, string argument)
		{
			if (argument.Length == 2) {
				char c0 = argument[0];
				if ((c0 == '-') || (c0 == '/')) {
					char c1 = argument[1];
					return (c1 == Char.ToUpper(flag)) || (c1 == Char.ToLower(flag));
				}
			}

			return false;
		}

		private static bool IsStringFlag(char flag, string argument, out string value)
		{
			if (argument.Length > 2) {
				char c0 = argument[0];
				if ((c0 == '-') || (c0 == '/')) {
					char c1 = argument[1];
					if ((c1 == Char.ToUpper(flag)) || (c1 == Char.ToLower(flag))) {
						value = argument.Substring(2);
						return true;
					}
				}
			}

			value = null;
			return false;
		}

		private static bool IsByteFlag(char flag, string argument, byte min, byte max, out byte value)
		{
			if (argument.Length > 2) {
				char c0 = argument[0];
				if ((c0 == '-') || (c0 == '/')) {
					char c1 = argument[1];
					if ((c1 == Char.ToUpper(flag)) || (c1 == Char.ToLower(flag))) {
						byte parsed;
						if (Byte.TryParse(argument.Substring(2), out parsed)) {
							if (parsed < min) {
								value = min;
								Console.WriteLine("ArgLink warning: switch -{0} set to {1}", flag, min);
							} else if (parsed > max) {
								value = max;
								Console.WriteLine("ArgLink warning: switch -{0} set to {1}", flag, max);
							} else {
								value = parsed;
							}

							return true;
						}
					}
				}
			}

			value = 0;
			return false;
		}

		private static string ExtensionOf(string path)
		{
			return Path.GetExtension(path);
		}

		private static string AppendExtensionIfAbsent(string argSfxObjectFile)
		{
			// Note: it is written this way to ease translation to C (passing char* in call chains is hard)
			string ext = ExtensionOf(argSfxObjectFile);
			// ReSharper disable once ConvertIfStatementToReturnStatement
			if (String.IsNullOrEmpty(ext)) {
				// ReSharper disable once JoinDeclarationAndInitializer
				string corrected;
				// ReSharper disable once UseStringInterpolation
				corrected = String.Format("{0}{1}", argSfxObjectFile, s_defaultExtension);
				return corrected;
			} else {
				return argSfxObjectFile;
			}
		}
		#endregion

		#region Linking phases
		private static void InputSobStepOne(int i, BinaryWriter fileOut, BinaryReader fileSob)
		{
			long  start  = fileSob.BaseStream.Position;
			int   offset = ReadLEInt32(fileSob);
			CSize size   = ReadLEInt32(fileSob);
			int   type   = fileSob.ReadByte();

			LuigiFormat("{0:X}: 0x{1:X}  /// Size: 0x{2:X} / Offset 0x{3:X} / Type {4:X}", i,
				start, size, offset, type);

			if (type == 0) {
				//Data
				Recopy(fileSob, size, fileOut, offset);
			} else if (type == 1) {
				//External File
				fileSob.ReadByte();
				fileSob.ReadByte();

				//Get file path
				string filepath = GetName(fileSob);
				LuigiFormat("--Open External File: {0}", filepath);
				BinaryReader fileExt = new BinaryReader(new FileStream(filepath, FileMode.Open, FileAccess.Read, FileShare.Read, s_ioBuffersKiB * 1024));
				Recopy(fileExt, size, fileOut, offset);
				fileExt.Close();
			}
		}

		private static List<LinkData> InputSobStepTwo(BinaryReader fileSob, List<LinkData> link)
		{
			do {
				LinkData   linktemp = new LinkData();
				List<char> nametemp = GetNameChars(fileSob);

				if (nametemp.Count <= 0) {
					break;
				}

				linktemp.Name  = new String(nametemp.ToArray());
				linktemp.Value = fileSob.ReadUInt16() | (fileSob.ReadByte() << 16);
				LuigiFormat("--{0} : {1:X}", linktemp.Name, linktemp.Value);
				link.Add(linktemp);
			} while (fileSob.ReadByte() == 0);
			// The return statement is needed to update the reference to link in the C version
			return link;
		}

		private static void PerformLink(string sobjFile, BinaryWriter fileOut, long[] startLink, int n, List<LinkData> link)
		{
			BinaryReader fileSob  = new BinaryReader(new FileStream(sobjFile, FileMode.Open, FileAccess.Read, FileShare.Read, s_ioBuffersKiB * 1024));
			long         fileSize = fileSob.BaseStream.Length;
			LuigiFormat("Open {0}", sobjFile);
			fileSob.BaseStream.Seek(0, SeekOrigin.Begin);
			if (SOBJWasRead(fileSob)) {
				long startIndex = startLink[n];
				if (startIndex < (fileSize - 3)) {
					LuigiFormat("{0:X}", startIndex);
					fileSob.BaseStream.Seek(startIndex, SeekOrigin.Begin);
					while (fileSob.BaseStream.Position < fileSize - 1) {
						LuigiFormat("-{0:X}", fileSob.BaseStream.Position);
						string name   = GetName(fileSob);
						int    nameId = Search(link, name);

						List<Calculation> linkcalc = new List<Calculation>();
						Calculation       calctemp = InitCalculation(-1, 0, 0, link[nameId].Value);
						linkcalc.Add(calctemp);

						LuigiFormat("--{0} : {1:X}", name, link[nameId].Value);

						if (fileSob.ReadByte() != 0) {
							fileSob.BaseStream.Seek(-1, SeekOrigin.Current);
							name   = GetName(fileSob);
							nameId = Search(link, name);
							LuigiFormat("----{0} : {1:X}", name, link[nameId].Value);
							fileSob.ReadByte();
						}

						fileSob.ReadInt32();
						fileSob.ReadInt32();

						//List all operations
						byte calccheck1 = fileSob.ReadByte();
						byte calccheck2 = fileSob.ReadByte();
						while (calccheck1 != 0 && calccheck2 != 0) {
							// Note: ReadInt32() introduces a side effect and must be called under any circumstances
							calctemp = InitCalculation((calccheck1 & 0x70) >> 4, calccheck1 & 0x3,
								calccheck2, fileSob.ReadInt32());
							if (calccheck1 > 0x80) {
								calctemp.Value = link[nameId].Value;
							}

							calccheck1 = fileSob.ReadByte();
							calccheck2 = fileSob.ReadByte();
							linkcalc.Add(calctemp);
						}

						//All operations have been found, now do the calculations
						while (linkcalc.Count > 1) {
							//Check for highest deep
							int highestdeep    = -1;
							int highestdeepidx = -1;
							int i;
							// ReSharper disable once RedundantCast
							for (i = 1; i < (int)linkcalc.Count; i++) { // Cast for MSVC
								//Get the first highest one
								if (highestdeep < linkcalc[i].Deep) {
									highestdeep    = linkcalc[i].Deep;
									highestdeepidx = i;
								}
							}

							//Check for highest priority
							int highestpri    = -1;
							int highestpriidx = -1;
							// ReSharper disable once RedundantCast
							for (i = highestdeepidx; i < (int)linkcalc.Count; i++) { // Cast for MSVC
								//Get the first highest one
								if (linkcalc[i].Deep != highestdeep || highestpri > linkcalc[i].Priority) {
									break;
								}

								if (highestpri < linkcalc[i].Priority && linkcalc[i].Deep == highestdeep) {
									highestpri    = linkcalc[i].Priority;
									highestpriidx = i;
								}
							}

							//Check for latest deep
							int calcidx = -1;
							for (i = highestpriidx; i >= 0; i--) {
								//Get the first one that comes
								if (highestdeep > linkcalc[i].Deep || highestpri > linkcalc[i].Priority) {
									calcidx = i;
									break;
								}
							}

							//Do the calculation
							calctemp = linkcalc[calcidx];

							int operation = linkcalc[highestpriidx].Operation;
							int calcValue = linkcalc[highestpriidx].Value;
							if (operation == 0x02) { //Shift Right
								LuigiFormat("{0:X} >> {1:X}", calctemp.Value, calcValue);
								calctemp.Value >>= calcValue;
							} else if (operation == 0x0C) { //Add
								LuigiFormat("{0:X} + {1:X}", calctemp.Value, calcValue);
								calctemp.Value += calcValue;
							} else if (operation == 0x0E) { //Sub
								LuigiFormat("{0:X} - {1:X}", calctemp.Value, calcValue);
								calctemp.Value -= calcValue;
							} else if (operation == 0x10) { //Mul
								LuigiFormat("{0:X} * {1:X}", calctemp.Value, calcValue);
								calctemp.Value *= calcValue;
							} else if (operation == 0x12) { //Div
								LuigiFormat("{0:X} / {1:X}", calctemp.Value, calcValue);
								calctemp.Value /= calcValue;
							} else if (operation == 0x16) { //And
								LuigiFormat("{0:X} & {1:X}", calctemp.Value, calcValue);
								calctemp.Value &= calcValue;
							} else {
								LuigiFormat("ERROR (CALCULATION) [{0:X}]", operation);
							}

							linkcalc[calcidx] = calctemp;
							linkcalc.RemoveAt(highestpriidx);
						}

						//And then put the data in
						int offset = fileSob.ReadInt32();
						fileOut.Seek(offset + 1, SeekOrigin.Begin);
						LuigiFormat("----{0:X} : {1:X}", offset, linkcalc[0].Value);
						byte format     = fileSob.ReadByte();
						int  firstValue = linkcalc[0].Value;
						if (format == 0x00) { // 8-bit
							fileOut.Write((byte)firstValue);
						} else if (format == 0x02) { // 16-bit
							fileOut.Write((ushort)firstValue);
						} else if (format == 0x04) { // 24-bit
							fileOut.Write((ushort)firstValue);
							fileOut.Write((byte)(firstValue >> 16));
						} else if (format == 0x0E) { // 8-bit
							fileOut.Seek(offset, SeekOrigin.Begin);
							fileOut.Write((byte)firstValue);
						} else if (format == 0x10) { // 16-bit
							fileOut.Seek(offset, SeekOrigin.Begin);
							fileOut.Write((ushort)firstValue);
						} else {
							LuigiOut("ERROR (OUTPUT)");
						}
					}
				} else {
					LuigiOut("NOTHING");
				}
			}
			fileSob.Close();
		}
		#endregion

		#region Main entry point
		private static int Main(string[] args)
		{
			// TODO : get, split and parse ALFLAGS environment variable
			// Do it before parsing command line so command line can override environment

			// Parse command line
			// "Sob" is the default file extension for ArgSfxX output, not to insult anybody
			int    idx;
			bool[] areSobs   = new bool[args.Length];
			int    totalSobs = args.Length;
			bool   showLogo  = true;
			string romFile   = null;
			string pubsPath  = null;
			string passed;
			byte   parsedU8;
			for (idx = 0; idx < args.Length; idx++) {
				// ReSharper disable once RedundantAssignment
				passed = null;
				if (IsSimpleFlag('V', args[idx])) {
					s_verbose    = true;
					areSobs[idx] = false;
					totalSobs--;
				} else if (IsSimpleFlag('Q', args[idx])) {
					showLogo     = false;
					areSobs[idx] = false;
					totalSobs--;
				} else if (IsStringFlag('O', args[idx], out passed)) {
					romFile      = passed;
					areSobs[idx] = false;
					totalSobs--;
				} else if (IsByteFlag('B', args[idx], 0, 31, out parsedU8)) {
					s_ioBuffersKiB = parsedU8;
					areSobs[idx]   = false;
					totalSobs--;
				} else if (IsStringFlag('E', args[idx], out passed)) {
					if ((passed != null) && (passed.Length >= 2) && (passed[0] == '.')) {
						s_defaultExtension = passed;
					} else {
						Console.WriteLine("ArgLink warning: default extension override must start with a dot.");
					}

					areSobs[idx] = false;
					totalSobs--;
				} else if (IsStringFlag('X', args[idx], out passed)) {
					pubsPath     = passed;
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
				for (idx = 0; idx < args.Length; idx++) {
					Console.Error.WriteLine(args[idx]);
				}
			}

			if (totalSobs < 1) {
				OutputUsage();
				return (int)BSDExitCodes.BadCLIUsage;
			} else if (String.IsNullOrEmpty(romFile)) {
				// Standard error is reserved for verbose output
				Console.WriteLine("ArgLink error: no ROM file was specified.");
				return (int)BSDExitCodes.BadCLIUsage;
			} else {
				BinaryReader fileSob;
				BinaryWriter fileOut = new BinaryWriter(new FileStream(romFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, s_ioBuffersKiB * 1024));
				// Fill Output file to 1 MiB
				Console.WriteLine("Constructing ROM Image.");
				fileOut.Seek(0, SeekOrigin.Begin);
				for (idx = 0; idx < 0x100000; idx++) {
					fileOut.BaseStream.WriteByte(0xFF);
				}

				// Steps 1 & 2: Input all data and list all links
				Console.WriteLine("Processing Externals.");
				List<LinkData> link      = new List<LinkData>();
				long[]         startLink = new long[totalSobs];
				int            firstSob  = -1;
				int            n         = 0;
				string         sobjFile;
				for (idx = 0; idx < args.Length; idx++) {
					if (areSobs[idx]) {
						if (firstSob < 0) {
							firstSob = idx;
						}

						//Check if SOB file is indeed a SOB file
						sobjFile = AppendExtensionIfAbsent(args[idx]);
						fileSob  = new BinaryReader(new FileStream(sobjFile, FileMode.Open, FileAccess.Read, FileShare.Read, s_ioBuffersKiB * 1024));
						LuigiFormat("Open {0}", sobjFile);
						fileSob.BaseStream.Seek(0, SeekOrigin.Begin);
						if (SOBJWasRead(fileSob)) {
							fileSob.ReadByte();
							fileSob.ReadByte();
							int count = fileSob.ReadByte();
							fileSob.ReadByte();

							for (int i = 0; i < count; i++) {
								// Step 1: Input all data into output
								InputSobStepOne(i, fileOut, fileSob);
							}

							// Step 2: Get all extern names and values
							link = InputSobStepTwo(fileSob, link);

							startLink[n] = fileSob.BaseStream.Position;
							n++;
							fileSob.Close();
							//Repeat
						}
					}
				}

				// Step 3: Link everything
				Console.WriteLine("Writing Image.");
				LuigiOut("----LINK");
				n = 0;
				for (idx = firstSob; idx < args.Length; idx++) {
					if (areSobs[idx]) {
						sobjFile = AppendExtensionIfAbsent(args[idx]);
						PerformLink(sobjFile, fileOut, startLink, n, link);
						n++;
					}
				}

				long finalSize = fileOut.BaseStream.Length;
				finalSize = (finalSize / 1024) + ((finalSize % 1024) > 0 ? 1 : 0);
				Console.WriteLine("| Publics: {0}\tFiles: {1}\tROM Size: {2}KiB |", link.Count, totalSobs, finalSize);

				fileOut.Close();

				if (!String.IsNullOrEmpty(pubsPath)) {
					StreamWriter filePubs = new StreamWriter(new FileStream(pubsPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, s_ioBuffersKiB * 1024));
					// ReSharper disable once RedundantCast
					for (idx = 0; idx < (int)link.Count; idx++) { // Cast for MSVC
						filePubs.WriteLine("{0}", link[idx].Name);
					}
					filePubs.Close();
				}

				return (int)BSDExitCodes.Success;
			}
		}
		#endregion
	}
}
