using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Exploratorium.ArgSfx.ClassifySobjStrings
{
	internal enum SobjString : byte
	{
		Public,
		FileReference,
		Message,
		Whitespace,
		Junk
	}

	internal static class Program
	{
		internal static void Main(string[] args)
		{
			foreach (var inputPath in args) {
				if (IsValidInput(inputPath)) {
					ClassifyFile(inputPath);
				}
			}
		}

		private static void ClassifyFile(string inputPath)
		{
			string[] karCategories = new string[] { "publics", "filerefs", "messages", "empty", "junk" };

			int[] intarCounts = new int[5];
			int   i;

			using (var smrInput = new StreamReader(inputPath)) {
				TextWriter[] twarSplit = new TextWriter[3];
				try {
					for (i = 0; i < 3; i++) {
						twarSplit[i] = new StreamWriter(Path.GetFileNameWithoutExtension(inputPath) + "-" +
														karCategories[i] + ".txt");
					}

					while (!smrInput.EndOfStream) {
						var strLineRead = smrInput.ReadLine();
						if (String.IsNullOrEmpty(strLineRead)) {
							intarCounts[(int)SobjString.Whitespace]++;
						} else {
							var mtcPublics = Regex.Matches(strLineRead, @"[A-Z][A-Z_0-9]+");
							if ((mtcPublics.Count == 1) && mtcPublics[0].Success &&
								(mtcPublics[0].Value == strLineRead)) {
								ClassifyLine(strLineRead, SobjString.Public, intarCounts, twarSplit);
							} else if (strLineRead.Contains("\\") && strLineRead.Contains(".")) {
								ClassifyLine(strLineRead, SobjString.FileReference, intarCounts, twarSplit);
							} else if (strLineRead.Contains(" ") && !IsJunk(strLineRead)) {
								ClassifyLine(strLineRead, SobjString.Message, intarCounts, twarSplit);
							} else {
								intarCounts[(int)SobjString.Junk]++;
							}
						}
					}
				} finally {
					for (i = 0; i < 3; i++) {
						if (twarSplit[i] != null) {
							twarSplit[i].Dispose();
						}
					}
				}

				for (i = 0; i < 5; i++) {
					Console.Write("{0,4} {1}   ", intarCounts[i], karCategories[i]);
				}
				Console.WriteLine(inputPath);
			}
		}

		private static void ClassifyLine(string line, SobjString kind, int[] counts, TextWriter[] outputFiles)
		{
			counts[(int)kind]++;
			outputFiles[(int)kind].WriteLine(line);
		}

		private static bool IsValidInput(string path)
		{
			return !String.IsNullOrEmpty(path) && (path.IndexOfAny(Path.GetInvalidPathChars()) < 0) &&
				   File.Exists(path);
		}

		private static bool IsJunk(string line)
		{
			return line.Contains("\f") || line.Contains("^") ||
				   " ()!\"$'*<+-;:1234567890?@%&/,.`{}~".IndexOfAny(new char[] { line[0] }) >= 0;
		}
	}
}
