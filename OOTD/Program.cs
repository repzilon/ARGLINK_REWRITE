using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Exploratorium.ArgSfx.OutOfThisDimension
{
	internal static class Program
	{
		private static readonly IDictionary<string, string[]> s_dicUsingToIncludes = new Dictionary<string, string[]> {
			{ "System", new[] { "stdbool.h", "stdint.h", "string.h" } },
			{ "System.IO", new[] { "stdio.h" } }
		};

		private static readonly IDictionary<string, string> s_dicTypeMapping = new Dictionary<string, string> {
			{ "string", "char*" }, { "ushort", "uint16_t" }, { "int", "int32_t" }, { "long", "int64_t" },
			{ "byte", "uint8_t" }, { "BinaryReader", "FILE*" }, { "BinaryWriter", "FILE*" },
			{ "List<char>", "char*" }, { "List<LinkData>", "LinkData*" }, { "List<Calculation>", "Calculation*" }
		};

		private static void OutputLogo()
		{
			Console.WriteLine("Out Of This Dimension\t(c) 2025 Repzilon\n");
		}

		private static void OutputUsage()
		{
			Console.WriteLine("NAME\n\tOOTD - Out Of This Dimension\n\n" +
							  "SYNOPSIS\n\tootd <orig-source.cs> <translated.c>|-\n\n" +
							  "DESCRIPTION\n\tOOTD is a regular expression powered special purpose C# to C translator\n" +
							  "\tmade specifically for ARGLINK_REWRITE. No artificial intelligence is\n" +
							  "\tinvolved, just old fashioned software text replacement.\n" +
							  "OPTIONS\n\tNone per se, but '-' can be used for standard output.\n");
			//puts("ENVIRONMENT\n\t\n");
			Console.WriteLine("EXIT STATUS\n\t0 on command success, 1 when this general help message is shown,\n" +
							  "\t2 on an internally caught error.\n");
			//puts("EXAMPLES\n\t\n");
			//puts("COMPATIBILITY\n\t\n");
			//puts("SEE ALSO\n\t\n");
			//puts("STANDARDS\n\t\n");
			//puts("HISTORY\n\t\n");
			//puts("BUGS\n\t\n");
		}

		static int Main(string[] args)
		{
			OutputLogo();
			if (args.Length < 2) {
				OutputUsage();
				return 1;
			} else if (args[0] == args[1]) {
				Console.Error.WriteLine("OOTD Error: source and destination are the same file.");
				return 2;
			} else {
				var        strAllSource = File.ReadAllText(args[0]);
				var        blnFile      = true;
				TextWriter destination  = null;
				try {
					// Open output stream
					if (args[1] == "-") {
						blnFile     = false;
						destination = Console.Out;
					} else {
						destination = new StreamWriter(args[1]);
					}

					// Write C Declarations
					foreach (var strInclude in UsingsToIncludes(strAllSource)) {
						destination.WriteLine("#include <{0}>", strInclude);
					}

					destination.WriteLine();

					// Translate C# code to C
					var strCleaned = ExtractBody(strAllSource, "namespace", out var strIndent);
					strCleaned = TearDownPartition(strCleaned, "class", strIndent);
					var mtcStrings = FindStringLiterals(strCleaned);
					strCleaned = RemoveAnyKeyword(strCleaned, mtcStrings, "internal", "private", "public", "protected");
					mtcStrings = FindStringLiterals(strCleaned);
					strCleaned = RemoveAnyKeyword(strCleaned, mtcStrings, "static");
					strCleaned = Regex.Replace(strCleaned, @"// ReSharper [a-z]+ [A-Za-z_]+\r?\n", "");
					strCleaned = RedeclareStructs(strCleaned);
					strCleaned = ReplaceDataTypeOfVariables(strCleaned);
					mtcStrings = FindStringLiterals(strCleaned);
					strCleaned = ConvertVerbatimStrings(strCleaned, mtcStrings);

					// Write C code
					strCleaned = strCleaned.Trim().Replace("\n\n\n", "\n\n");
					strCleaned = Regex.Replace(strCleaned, "[ ]+", " ");
					destination.WriteLine(strCleaned);
					return 0;
				} finally {
					if (blnFile && (destination != null)) {
						destination.Close();
					}
				}
			}
		}

		private static string[] UsingsToIncludes(string allSource)
		{
			var mtcUsings   = Regex.Matches(allSource, @"using ([A-Za-z0-9._]+);");
			var lstIncludes = new List<string>();
			foreach (Match m in mtcUsings) {
				if (s_dicUsingToIncludes.TryGetValue(m.Groups[1].Value, out var includes)) {
					lstIncludes.AddRange(includes);
				}
			}

			lstIncludes.Sort();
			return lstIncludes.ToArray();
		}

		private static Match MatchPartition(string sourceCode, string keyword)
		{
			return Regex.Match(sourceCode, keyword + @" [A-Za-z0-9._]+\s+{(.*)}", RegexOptions.Singleline);
		}

		private static string RemoveFirstIndent(string extracted, string indentSequence)
		{
			return Regex.Replace(extracted, "^" + indentSequence.Replace("\t", "\\t"), "", RegexOptions.Multiline);
		}

		private static string ExtractBody(string allSource, string keyword, out string indentSequence)
		{
			var strExtracted = MatchPartition(allSource, keyword).Groups[1].Value.Trim();
			indentSequence = Regex.Match(strExtracted, @"^\s+", RegexOptions.Multiline).Value;
			return RemoveFirstIndent(strExtracted, indentSequence);
		}

		private static string TearDownPartition(string allSource, string keyword, string indentSequence)
		{
			var match = MatchPartition(allSource, keyword);
			return match.Success
				? allSource.Substring(0, match.Index) + RemoveFirstIndent(match.Groups[1].Value, indentSequence) +
				  allSource.Substring(match.Index + match.Length)
				: allSource;
		}

		private static string RemoveAnyKeyword(string extract, MatchCollection stringLiterals, params string[] keywords)
		{
			return Regex.Replace(extract, @"(^|\b)(" + String.Join("|", keywords) + ") ",
				m => RemoveOutsideLiteral(m, stringLiterals));
		}

		private static MatchCollection FindStringLiterals(string extract)
		{
			return Regex.Matches(extract, @"(@?)[""](.*?)[""]", RegexOptions.Singleline);
		}

		private static string RemoveOutsideLiteral(Match m, MatchCollection literals)
		{
			return literals.Cast<Match>().Any(x => InsideStringLiteral(m, x)) ? m.Value : "";
		}

		private static bool InsideStringLiteral(Match what, Match literal)
		{
			var whatStart    = what.Index;
			var literalStart = literal.Index;
			return whatStart >= literalStart && whatStart < literalStart + literal.Length;
		}

		private static string RedeclareStructs(string translating)
		{
			return Regex.Replace(translating, @"struct ([A-Za-z0-9_]+)\s+{(.*?)}", "typedef struct $1 {$2} $1;",
				RegexOptions.Singleline);
		}

		private static string ReplaceDataTypeOfVariables(string translating)
		{
			foreach (var kvp in s_dicTypeMapping) {
				var isGeneric   = kvp.Key.Contains('<');
				var pattern     = isGeneric ? kvp.Key + " " : @"\b" + kvp.Key + @"\b";
				var replacement = isGeneric ? kvp.Value + " " : kvp.Value;
				translating = Regex.Replace(translating, pattern, replacement);
			}

			return translating;
		}

		private static string ConvertVerbatimStrings(string translating, MatchCollection stringLiterals)
		{
			var c = stringLiterals.Count;
			// Process in reverse order to keep positions in stringLiterals valid
			for (var i = c - 1; i >= 0; i--) {
				if (stringLiterals[i].Value.StartsWith("@")) {
					var strarLines = stringLiterals[i].Groups[2].Value
						.Split("\r\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
					var stbLiteral = new StringBuilder(stringLiterals[i].Length);
					for (var j = 0; j < strarLines.Length; j++) {
						stbLiteral.Append('"').Append(strarLines[j].Replace("\t", "\\t")).AppendLine("\\n\"");
					}
					translating = translating.Replace(stringLiterals[i].Value, stbLiteral.ToString());
				}
			}

			return translating;
		}
	}
}
