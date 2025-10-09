using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

#pragma warning disable HAA0603 // Delegate allocation from a method group

namespace Exploratorium.ArgSfx.OutOfThisDimension
{
	internal static class Program
	{
		private static readonly Dictionary<string, string[]> s_dicUsingToIncludes = new Dictionary<string, string[]> {
			{ "System", new[] { "stdbool.h", "stdint.h", "string.h", "stdlib.h" } },
			{ "System.IO", new[] { "stdio.h" } }
		};

		private static readonly Dictionary<string, string> s_dicTypeMapping = new Dictionary<string, string> {
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
					strCleaned = RemoveAnyKeyword(strCleaned, "internal", "private", "public", "protected");
					strCleaned = RemoveAnyKeyword(strCleaned, "static");
					strCleaned = Regex.Replace(strCleaned, @"// ReSharper [a-z]+ [A-Za-z_]+\r?\n", "");
					strCleaned = RedeclareStructs(strCleaned);
					strCleaned = ReplaceDataTypeOfVariables(strCleaned);
					strCleaned = Regex.Replace(strCleaned, @"([A-Za-z0-9*_]+)\[\]\s+([A-Za-z0-9_]+)", "$1 $2[]");
					strCleaned = ConvertVerbatimStrings(strCleaned);
					strCleaned = TranslateConsoleOutputCalls(strCleaned);
					strCleaned = TranslateFileInputOutput(strCleaned);
					strCleaned = TranslateInitObjects(strCleaned);
					strCleaned = AddressMemberAccess(strCleaned);
					strCleaned = HandleCounts(strCleaned);
					strCleaned = TranslateSpecificCalls(strCleaned);

					// Write C code
					strCleaned = strCleaned.Trim().Replace("\n\n\n", "\n\n");
					strCleaned = Regex.Replace(strCleaned, "[ ]+", " ");
					strCleaned = strCleaned.Replace("\t\t ", "\t\t");
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

		private static string RemoveAnyKeyword(string extract, params string[] keywords)
		{
			var mtcStrings = FindStringLiterals(extract);
			return Regex.Replace(extract, @"(^|\b)(" + String.Join("|", keywords) + ") ",
				m => RemoveOutsideLiteral(m, mtcStrings));
		}

		private static MatchCollection FindStringLiterals(string extract)
		{
			return Regex.Matches(extract, @"(@?)[""](.*?)[""]", RegexOptions.Singleline);
		}

		private static string RemoveOutsideLiteral(Match m, MatchCollection literals)
		{
			//return literals.Cast<Match>().Any(x => InsideStringLiteral(m, x)) ? m.Value : "";
			foreach (Match x in literals) {
				if (InsideStringLiteral(m, x)) {
					return m.Value;
				}
			}
			return "";
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
				var isGeneric   = kvp.Key.IndexOf('<') >= 0;
				var pattern     = isGeneric ? kvp.Key + " " : @"\b" + kvp.Key + @"\b";
				var replacement = isGeneric ? kvp.Value + " " : kvp.Value;
				translating = Regex.Replace(translating, pattern, replacement);
			}

			return translating;
		}

		private static string ConvertVerbatimStrings(string translating)
		{
			var stringLiterals = FindStringLiterals(translating);
			var c              = stringLiterals.Count;
			// Process in reverse order to keep positions in stringLiterals valid
			for (var i = c - 1; i >= 0; i--) {
				if (stringLiterals[i].Value.StartsWith("@", StringComparison.Ordinal)) {
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

		private static string TranslateConsoleOutputCalls(string translating)
		{
			return Regex.Replace(translating, @"Console(.Error)?.Write(Line)?[(](.*)", ReplaceSingleConsoleCall);
		}

		private static string ReplaceSingleConsoleCall(Match m)
		{
			var blnStdError = !String.IsNullOrEmpty(m.Groups[1].Value);
			var blnNewLine  = !String.IsNullOrEmpty(m.Groups[2].Value);
			var strArgument = m.Groups[3].Value;
			var blnLiteral  = strArgument.StartsWith("\"", StringComparison.Ordinal);
			var mtcFormats  = Regex.Matches(strArgument, @"[{]\d+(.*?)[}]");
			if (blnLiteral && (mtcFormats.Count > 0)) {
				// printf or fprintf
				strArgument = Regex.Replace(strArgument, @"[{]\d+(.*?)[}]", ConvertFormatSpecifier);
				if (blnNewLine && strArgument.Trim().EndsWith(");", StringComparison.Ordinal)) {
					strArgument = strArgument.Replace("\",", "\\n\",");
				}

				return (blnStdError ? "fprintf(stderr, " : "printf(") + strArgument;
			} else {
				// puts or fputs
				if (blnStdError) {
					var strTrimmedArg = strArgument.Trim();
					if (strTrimmedArg.EndsWith("\");", StringComparison.Ordinal)) {
						strArgument = strArgument.Replace("\");", blnNewLine ? "\\n\", stderr);" : "\", stderr);");
					} else if (strTrimmedArg.EndsWith(");", StringComparison.Ordinal)) {
						strArgument = strArgument.Replace(");", blnNewLine ? ", stderr);fputs(\"\\n\", stderr);" : ", stderr);");
					}
				}

				return (blnStdError ? "fputs(" : "puts(") + strArgument;
			}
		}

		private static string ConvertFormatSpecifier(Match m2)
		{
			// Very rough but works for now
			return m2.Groups[1].Value == ":X" ? "%8X" : "%s";
		}

		private static string TranslateFileInputOutput(string translating)
		{
			translating = Regex.Replace(translating, @"new FILE\*\(File.Open([A-Za-z0-9_]+)[(](.*)[)][)]", ReplaceOpenCall);

			translating = translating.Replace("SeekOrigin.Begin", "SEEK_SET").Replace("SeekOrigin.Current", "SEEK_CUR")
				.Replace("SeekOrigin.End", "SEEK_END");
			translating = Regex.Replace(translating, @"([A-Za-z0-9_.]+)\.Seek[(]([A-Za-z0-9_\]\[\+ -]+),", ReplaceSeekCall);

			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.BaseStream\.Position", "ftell($1)");

			// I would normally save the current position to a variable, then restore it with fseek, but there is
			// already a fseek to the first byte right after.
			translating = Regex.Replace(translating,
				@"([A-Za-z0-9_]+)\s+([A-Za-z0-9_]+)\s+=\s+([A-Za-z0-9_]+)\.BaseStream\.Length",
				"fseek($3, 0, SEEK_END); $1 $2 = ftell($3)");

			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.Read(Char|Byte)\(\)", "fgetc($1)");
			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.ReadInt32\(\)", "ReadLEInt32($1)");
			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.ReadUInt16\(\)", "fgetc($1) | (fgetc($1) << 8)");

			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.BaseStream\.WriteByte[(](.*)[)]", "fputc($2, $1)");
			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.Write\(\(uint8_t[)](.*)[)]", "fputc($2, $1)");
			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.Write\(\(uint16_t[)](.*)[)]", "fputc($2 & 0xff, $1); fputc($2 >> 8, $1)");

			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.(Read|Write)[(](.*?), 0, (.*?)[)]", "f$2($3, sizeof(uint8_t), $4, $1)");
			translating = translating.Replace("fRead(", "fread(").Replace("fWrite(", "fwrite(");

			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.Close\(\)", "fclose($1)");

			return translating;
		}

		private static string ReplaceOpenCall(Match m)
		{
			var strMethod = m.Groups[1].Value;
			var strMode   = "";
			if (strMethod == "Read") {
				strMode = "rb";
			} else if (strMethod == "Write") {
				strMode = "wb";
			} /*else if (strMode == "Text") {
				strMode = "r";
			}// */

			return "fopen(" + m.Groups[2].Value + ", \"" + strMode + "\")";
		}

		private static string ReplaceSeekCall(Match m)
		{
			return "fseek(" + m.Groups[1].Value.Replace(".BaseStream", "") + ", " + m.Groups[2].Value + ",";
		}

		private static string TranslateInitObjects(string translating)
		{
			translating = Regex.Replace(translating,
				@"([A-Za-z0-9_\[\]*]+)\s+([A-Za-z0-9_\[\]]+)\s+= new ([A-Za-z0-9<>_\[\]()]+);", ConvertInit);
			translating = translating.Replace("Calculation InitCalculation(", "Calculation* InitCalculation(");
			translating = Regex.Replace(translating, @"Calculation\s+([A-Za-z0-9_]+)\s+= InitCalculation[(]",
				"Calculation* $1 = InitCalculation(");
			return translating;
		}

		private static string ConvertInit(Match m)
		{
			var type     = m.Groups[1].Value;
			var variable = m.Groups[2].Value;
			var ctor     = m.Groups[3].Value;
			if (variable.EndsWith("[]", StringComparison.Ordinal)) { // array
				var bracket = ctor.IndexOf('[');
				var count   = ctor.Substring(bracket + 1, ctor.Length - bracket - 2);
				return QuickFormat("{0}* {1} = ({0}*)calloc({2}, sizeof({0}));", type, variable.Replace("[]", ""), count);
			} else if (ctor.StartsWith("List<", StringComparison.Ordinal)) {
				return QuickFormat("{0} {1} = NULL; int32_t {1}Count = 0;", type, variable);
			} else if (ctor.EndsWith("()", StringComparison.Ordinal)) { // struct
				return QuickFormat("{0}* {1} = ({0}*)calloc(1, sizeof({0}));", type, variable);
			} else {
				return m.Value; // i.e. no change
			}
		}

		private static string AddressMemberAccess(string translating)
		{
			var mtcStructs = Regex.Matches(translating, @"struct ([A-Za-z0-9_]+)\s+{(.*?)}", RegexOptions.Singleline);
			for (var i = 0; i < mtcStructs.Count; i++) {
				var mtcMembers = Regex.Matches(mtcStructs[i].Groups[2].Value, @"[A-Za-z0-9*_]+\s+([A-Za-z0-9_]+);");
				for (var j = 0; j < mtcMembers.Count; j++) {
					var member = mtcMembers[j].Groups[1].Value;
					translating = translating.Replace("." + member, "->" + member);
				}
			}

			return translating.Replace("]->", "].")
				.Replace("calctemp = linkcalc[calcidx]", "calctemp = &linkcalc[calcidx]")
				.Replace("linkcalc[calcidx] = calctemp", "linkcalc[calcidx] = *calctemp");
		}

		private static string HandleCounts(string translating)
		{
			// translating = Regex.Replace(translating, @"", "");
			translating = Regex.Replace(translating, @"([<=>]+) ([A-Za-z0-9_]+)[.]Count", "$1 $2Count");
			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)[.]Count ([<=>]+)", "$1Count $2");
			translating = translating.Replace("void Main(char* args[])", "int main(int argc, char* argv[])");
			translating = translating.Replace("args.Length", "(argc - 1)").Replace("args[", "argv[1 + ");
			translating = translating.Replace("Search(LinkData* link, char* name)", "Search(LinkData* link, int32_t linkCount, char* name)");
			translating = Regex.Replace(translating, @"Search[(]([A-Za-z]+),", "Search($1, $1Count,");
			translating = translating.Replace("char* GetNameChars(FILE* fileSob)", "char* GetNameChars(FILE* fileSob, int32_t* nametempCount)");
			translating = translating.Replace("char* nametemp = NULL; int32_t nametempCount = 0;", "char* nametemp = NULL; *nametempCount = 0;");
			translating = translating.Replace("char* nametemp = GetNameChars(fileSob);", "int32_t nametempCount; char* nametemp = GetNameChars(fileSob, &nametempCount);");
			translating = translating.Replace("return String.Concat(GetNameChars(fileSob));", "int32_t Count; return String.Concat(GetNameChars(fileSob, &Count));");
			translating = translating.Replace("return new String(GetNameChars(fileSob).ToArray());", "int32_t Count; return new String(GetNameChars(fileSob, &Count).ToArray());");
			return translating;
		}

		private static string TranslateSpecificCalls(string translating)
		{
			translating = Regex.Replace(translating, @"([A-Za-z0-9_\[\]]+)\.Name\.Equals[(]([A-Za-z0-9_]+)[)]",
				"strcmp($1.Name, $2) == 0");

			var translating1 = translating;
			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.Add[(]([A-Za-z0-9_]+)[)]",
				m => TranslateListAdd(m, translating1));

			MatchEvaluator meNewString = TranslateStringFromCharArray;
			translating = Regex.Replace(translating, @"(return|[A-Za-z0-9_>-]+\s+=) String\.Concat[(](.+)[)]", meNewString);

			translating = Regex.Replace(translating, @"(return|[A-Za-z0-9_>-]+\s+=) new String[(](.+)[.]ToArray[(][)][)]",
				meNewString);

			var translating2 = translating;
			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.RemoveAt[(]([A-Za-z0-9_]+)[)];",
				m => TranslateListRemoveAt(m, translating2));

			return translating;
		}

		private static string TranslateListRemoveAt(Match m, string translating1)
		{
			var list     = m.Groups[1].Value;
			var at       = m.Groups[2].Value;
			var listType = Regex.Match(translating1, @"([A-Za-z0-9_]+)[*]\s+" + list + " = NULL").Groups[1].Value;
			return String.Format(CultureInfo.InvariantCulture,
				"int32_t after = {1}Count - 1 - {2};{0}if (after > 0) {{{0}"+
				"\tmemmove(&({1}[{2}]), &({1}[{2} + 1]), after * sizeof({3}));{0}"+
				"}}{0}{1}Count--;{0}{1} = ({3}*)realloc({1}, {1}Count * sizeof({3}));",
				Environment.NewLine + "\t\t\t\t\t\t\t", list, at, listType);
		}

		private static string TranslateListAdd(Match m, string translating1)
		{
			var list      = m.Groups[1].Value;
			var item      = m.Groups[2].Value;
			var countType = Regex.Match(translating1, @"(([A-Za-z0-9_]+)\s+|[*])" + list + "Count = 0").Groups[1].Value.Trim();
			var listType = Regex.Match(translating1, @"([A-Za-z0-9_]+)[*]\s+" + list + " = NULL").Groups[1].Value;
			// (*nametempCount)++; nametemp = realloc(nametemp, *nametempCount * sizeof(char)); nametemp[*nametempCount - 1] = check
			return String.Format(CultureInfo.InvariantCulture, countType == "*" ? // is count is a passed pointer?
					"(*{0}Count)++; {0} = ({2}*)realloc({0}, *{0}Count * sizeof({2})); {0}[*{0}Count - 1] = {3}{1}"
					: "{0}Count++; {0} = ({2}*)realloc({0}, {0}Count * sizeof({2})); {0}[{0}Count - 1] = {3}{1}",
				list, item, listType, listType == "char" ? "" : "*");
		}

		private static string TranslateStringFromCharArray(Match m)
		{
			var leftHand    = m.Groups[1].Value.Trim();
			var rightHand   = m.Groups[2].Value;
			var openParen   = rightHand.IndexOf('(');
			if ((openParen >= 0) && (rightHand.IndexOf(')') > openParen)) { // function call on right hand
				// GetNameChars[(](.*),\s+(.*Count)[)]
				var functionName = rightHand.Substring(0, openParen);
				var mtcCall      = Regex.Match(rightHand, functionName + @"[(](.*),\s+(.*Count)[)]");
				var countArg     = mtcCall.Groups[2].Value.Replace("&", "");
				return QuickFormat(
					"char* functionResult = {1}; char* resultString = (char*)calloc({2} + 1, sizeof(char)); memmove(resultString, functionResult, {2}); {0} resultString",
					leftHand, rightHand, countArg);
			} else {
				return QuickFormat(
					"char* {1}String = (char*)calloc({1}Count + 1, sizeof(char)); memmove({1}String, {1}, {1}Count);{0} {1}String",
					leftHand, rightHand);
			}
		}

		private static string QuickFormat(string format, string arg0, string arg1)
		{
			return format.Replace("{0}", arg0).Replace("{1}", arg1);
		}

		private static string QuickFormat(string format, string arg0, string arg1, string arg2)
		{
			return format.Replace("{0}", arg0).Replace("{1}", arg1).Replace("{2}", arg2);
		}
	}
}
