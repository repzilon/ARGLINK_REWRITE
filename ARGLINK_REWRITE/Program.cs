using System;
using System.Collections.Generic;
using System.IO;

// ReSharper disable SuggestVarOrType_Elsewhere
// ReSharper disable SuggestVarOrType_BuiltInTypes
// ReSharper disable UseObjectOrCollectionInitializer

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

	internal static class Program
	{
		// Default values as stated in usage text
		// ReSharper disable InconsistentNaming
		private static byte s_ioBuffersKiB = 10;
		private static string s_defaultExtension = ".SOB";
		private static ushort s_fabcardPort = 0x290;
		private static byte s_memoryMiB = 2;
		private static ushort s_printerPort = 0x378;
		private static byte s_romType = 0x7D;
		// ReSharper restore InconsistentNaming

		private static void OutputLogo()
		{
			Console.WriteLine(@"ArgLink Re-Rewrite			(c) 2025 Repzilon
Based on ARGLINK_REWRITE		(c) 2017 LuigiBlood
For imitating ArgLink SFX v1.11x	(c) 1993 Argonaut Software Ltd.
");
		}

		private static void OutputUsage()
		{
			Console.WriteLine(@"ARGLINK [opts] obj1 [opts] obj2 [opts] obj3 [opts] obj4 ...
All object file names are prepended with .SOB if no extension is specified.
CLI options can be placed in the ALFLAGS environment variable.
A filename preceded with @ is a file list.
Please note: DOS has a limit on parameters, so please use the @ option.

** Unimplemented Options are:
** -A1		- Download to ADS SuperChild1 hardware.
** -A2		- Download to ADS SuperChild2 hardware.
** -B		- Set file input/output buffers (0-31), default = 10 KiB.
** -C		- Duplicate public warnings on.
** -D		- Download to ramboy.
** -E <ext>	- Change default file extension, default = '.SOB'.
** -F[<addr>]	- Set Fabcard port address (in hex), default = 0x290.
** -H <size>	- String hash size, default = 256.
** -I		- Display file information while loading.
** -L <size>	- Display used ROM layout (size is in KiB).
** -M <size>	- Memory size, default = 2 (mebibytes).
** -N		- Download to Nintendo Emulation system.
** -O <romfile>	- Output a ROM file.
** -P[<addr>]	- Set Printer port address (in hex), default = 0x378.
** -R		- Display ROM block information.
** -S		- Display all public symbols.
** -T[<type>]	- Set ROM type (in hex), default = 0x7D.
** -W <prefix>	- Set prefix (Work directory) for object files.
** -Y		- Use secondary ADS backplane CIC.
** -Z		- Generate a debugger MAP file.
");
		}

		private static void Main(string[] args)
		{
			OutputLogo();
			for (int i = 0; i < args.Length; i++) {
				Console.WriteLine(args[i]);
			}

			if (args.Length < 2) {
				OutputUsage();
			} else {
				BinaryWriter fileOut = new BinaryWriter(File.OpenWrite(args[0]));
				BinaryReader fileSob;
				BinaryReader fileExt;

				//Fill Output file to 1MB
				fileOut.Seek(0, SeekOrigin.Begin);
				for (int i = 0; i < 0x100000; i++) {
					fileOut.BaseStream.WriteByte(0xFF);
				}

				//SOB files, Step 1 & 2 - input all data and list all links
				List<LinkData> link      = new List<LinkData>();
				int            sobInput  = args.Length - 1;
				long[]         startLink = new long[sobInput];

				for (int idx = 0; idx < sobInput; idx++) {
					//Check if SOB file is indeed a SOB file
					int count;
					fileSob = new BinaryReader(File.OpenRead(args[idx + 1]));
					Console.WriteLine("Open {0}", args[idx + 1]);
					fileSob.BaseStream.Seek(0, SeekOrigin.Begin);
					if (fileSob.ReadByte() == 0x53     //S
						&& fileSob.ReadByte() == 0x4F  //O
						&& fileSob.ReadByte() == 0x42  //B
						&& fileSob.ReadByte() == 0x4A) //J
					{
						fileSob.ReadByte();
						fileSob.ReadByte();
						count = fileSob.ReadByte();
						fileSob.ReadByte();

						for (int i = 0; i < count; i++) {
							//Step 1 - Input all data into output
							long start = fileSob.BaseStream.Position;
							int offset = (fileSob.ReadByte() << 0) | (fileSob.ReadByte() << 8) |
										 (fileSob.ReadByte() << 16) | (fileSob.ReadByte() << 24);
							int size = (fileSob.ReadByte() << 0) | (fileSob.ReadByte() << 8) |
									   (fileSob.ReadByte() << 16) | (fileSob.ReadByte() << 24);
							int type = fileSob.ReadByte();

							Console.WriteLine("{0:X}: 0x{1:X}  /// Size: 0x{2:X} / Offset 0x{3:X} / Type {4:X}", i,
								start, size, offset, type);
							byte[] buffer = new byte[size];

							if (type == 0) {
								//Data
								fileSob.Read(buffer, 0, size);
								fileOut.Seek(offset, SeekOrigin.Begin);
								fileOut.Write(buffer, 0, size);
							} else if (type == 1) {
								//External File
								fileSob.ReadByte();
								fileSob.ReadByte();

								//Get file path
								List<char> filepath = new List<char>();
								char       check    = 'A';
								while (check != 0) {
									check = fileSob.ReadChar();
									if (check != 0) {
										filepath.Add(check);
									}
								}

								Console.WriteLine("--Open External File: {0}", string.Concat(filepath));

								fileExt = new BinaryReader(File.OpenRead(String.Concat(filepath)));
								fileExt.Read(buffer, 0, size);
								fileOut.Seek(offset, SeekOrigin.Begin);
								fileOut.Write(buffer, 0, size);

								fileExt.Close();
							}
						}

						//Step 2 - Get all extern names and values
						do {
							LinkData   linktemp = new LinkData();
							List<char> nametemp = new List<char>();
							char       check    = 'A';
							while (check != 0) {
								check = fileSob.ReadChar();
								if (check != 0) {
									nametemp.Add(check);
								}
							}

							if (nametemp.Count <= 0) {
								break;
							}

							linktemp.Name  = String.Concat(nametemp);
							linktemp.Value = fileSob.ReadUInt16() | (fileSob.ReadByte() << 16);
							Console.WriteLine("--{0} : {1:X}", linktemp.Name, linktemp.Value);
							link.Add(linktemp);
						} while (fileSob.ReadByte() == 0);

						startLink[idx] = fileSob.BaseStream.Position;
						fileSob.Close();
						//Repeat
					}
				}

				//Step 3 - Link everything
				Console.WriteLine("----LINK");
				for (int idx = 0; idx < sobInput; idx++) {
					fileSob = new BinaryReader(File.OpenRead(args[idx + 1]));
					Console.WriteLine("Open {0}", args[idx + 1]);
					fileSob.BaseStream.Seek(0, SeekOrigin.Begin);
					if (fileSob.ReadByte() == 0x53     //S
						&& fileSob.ReadByte() == 0x4F  //O
						&& fileSob.ReadByte() == 0x42  //B
						&& fileSob.ReadByte() == 0x4A) //J
					{
						if (startLink[idx] < (fileSob.BaseStream.Length - 3)) {
							Console.WriteLine(startLink[idx].ToString("X"));
							fileSob.BaseStream.Seek(startLink[idx], SeekOrigin.Begin);
							while (fileSob.BaseStream.Position < fileSob.BaseStream.Length - 1) {
								Console.WriteLine("-{0:X}", fileSob.BaseStream.Position);
								//Get name
								List<char> nametemp = new List<char>();
								char       check    = 'A';
								while (check != 0) {
									check = fileSob.ReadChar();
									if (check != 0) {
										nametemp.Add(check);
									}
								}

								string name = string.Concat(nametemp);

								//search
								int nameId = -1;
								for (int i = 0; i < link.Count; i++) {
									if (link[i].Name.Equals(name)) {
										nameId = i;
									}
								}

								List<Calculation> linkcalc = new List<Calculation>();
								Calculation       calctemp = new Calculation();
								calctemp.Deep      = -1;
								calctemp.Priority  = 0;
								calctemp.Operation = 0;
								calctemp.Value     = link[nameId].Value;
								linkcalc.Add(calctemp);

								Console.WriteLine("--{0} : {1:X}", name, link[nameId].Value);

								if (fileSob.ReadByte() != 0) {
									fileSob.BaseStream.Seek(-1, SeekOrigin.Current);

									nametemp = new List<char>();
									check    = 'A';
									while (check != 0) {
										check = fileSob.ReadChar();
										if (check != 0) {
											nametemp.Add(check);
										}
									}

									name = String.Concat(nametemp);

									for (int i = 0; i < link.Count; i++) {
										if (link[i].Name.Equals(name)) {
											nameId = i;
										}
									}

									Console.WriteLine("----{0} : {1:X}", name, link[nameId].Value);
									fileSob.ReadByte();
								}

								fileSob.ReadInt32();
								fileSob.ReadInt32();

								//List all operations
								byte calccheck1 = fileSob.ReadByte();
								byte calccheck2 = fileSob.ReadByte();
								while (calccheck1 != 0 && calccheck2 != 0) {
									calctemp           = new Calculation();
									calctemp.Deep      = (calccheck1 & 0x70) >> 4;
									calctemp.Priority  = (calccheck1 & 0x3);
									calctemp.Operation = calccheck2;
									calctemp.Value     = fileSob.ReadInt32();
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
									for (int i = 1; i < linkcalc.Count; i++) {
										//Get the first highest one
										if (highestdeep < linkcalc[i].Deep) {
											highestdeep    = linkcalc[i].Deep;
											highestdeepidx = i;
										}
									}

									//Check for highest priority
									int highestpri    = -1;
									int highestpriidx = -1;
									for (int i = highestdeepidx; i < linkcalc.Count; i++) {
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
									for (int i = highestpriidx; i >= 0; i--) {
										//Get the first one that comes
										if (highestdeep > linkcalc[i].Deep || highestpri > linkcalc[i].Priority) {
											calcidx = i;
											break;
										}
									}

									//Do the calculation
									calctemp = linkcalc[calcidx];

									if (linkcalc[highestpriidx].Operation == 0x02) {
										//Shift Right
										Console.WriteLine("{0:X} >> {1:X}", calctemp.Value,
											linkcalc[highestpriidx].Value);
										calctemp.Value >>= linkcalc[highestpriidx].Value;
									} else if (linkcalc[highestpriidx].Operation == 0x0C) {
										//Add
										Console.WriteLine("{0:X} + {1:X}", calctemp.Value,
											linkcalc[highestpriidx].Value);
										calctemp.Value += linkcalc[highestpriidx].Value;
									} else if (linkcalc[highestpriidx].Operation == 0x0E) {
										//Sub
										Console.WriteLine("{0:X} - {1:X}", calctemp.Value,
											linkcalc[highestpriidx].Value);
										calctemp.Value -= linkcalc[highestpriidx].Value;
									} else if (linkcalc[highestpriidx].Operation == 0x10) {
										//Mul
										Console.WriteLine("{0:X} * {1:X}", calctemp.Value,
											linkcalc[highestpriidx].Value);
										calctemp.Value *= linkcalc[highestpriidx].Value;
									} else if (linkcalc[highestpriidx].Operation == 0x12) {
										//Div
										Console.WriteLine("{0:X} / {1:X}", calctemp.Value,
											linkcalc[highestpriidx].Value);
										calctemp.Value /= linkcalc[highestpriidx].Value;
									} else if (linkcalc[highestpriidx].Operation == 0x16) {
										//And
										Console.WriteLine("{0:X} & {1:X}", calctemp.Value,
											linkcalc[highestpriidx].Value);
										calctemp.Value &= linkcalc[highestpriidx].Value;
									} else {
										Console.WriteLine("ERROR (CALCULATION) [{0:X}]",
											linkcalc[highestpriidx].Operation);
									}

									linkcalc[calcidx] = calctemp;

									linkcalc.RemoveAt(highestpriidx);
								}

								//And then put the data in
								int offset = fileSob.ReadInt32();
								fileOut.Seek(offset + 1, SeekOrigin.Begin);
								Console.WriteLine("----{0:X} : {1:X}", offset, linkcalc[0].Value);
								if (fileSob.ReadByte() == 0x00) {
									//8-bit
									fileOut.Write((byte)linkcalc[0].Value);
								} else if (fileSob.ReadByte() == 0x02) {
									//16-bit
									fileOut.Write((ushort)linkcalc[0].Value);
								} else if (fileSob.ReadByte() == 0x04) {
									//24-bit
									fileOut.Write((ushort)linkcalc[0].Value);
									fileOut.Write((byte)(linkcalc[0].Value >> 16));
								} else if (fileSob.ReadByte() == 0x0E) {
									//8-bit
									fileOut.Seek(offset, SeekOrigin.Begin);
									fileOut.Write((byte)linkcalc[0].Value);
								} else if (fileSob.ReadByte() == 0x10) {
									//16-bit
									fileOut.Seek(offset, SeekOrigin.Begin);
									fileOut.Write((ushort)linkcalc[0].Value);
								} else {
									Console.WriteLine("ERROR (OUTPUT)");
								}
							}
						} else {
							Console.WriteLine("NOTHING");
						}
					}
				}
			}
		}
	}
}
