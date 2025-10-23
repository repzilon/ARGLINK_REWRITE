using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

#pragma warning disable HAA0603 // Delegate allocation from a method group

namespace Exploratorium.ArgSfx.OutOfThisDimension
{
	internal enum BSDExitCodes : byte
	{
		Success = 0,
		BadCLIUsage = 64,
		UnreadableInputFile = 66,
		InternalError = 70,
		CannotCreateOutputFile = 73,
		BadFileIO = 74,
	}

	internal static class Program
	{
		private static readonly Dictionary<string, string[]> s_dicUsingToIncludes = new Dictionary<string, string[]> {
			{ "System", new[] { "ctype.h", "inttypes.h", "stdarg.h", "stdbool.h", "string.h", "stdlib.h" } },
			{ "System.IO", new[] { "stdio.h" } }
		};

		// Yes, CSize is intentionally signed in C# and unsigned in C
		private static readonly Dictionary<string, string> s_dicTypeMapping = new Dictionary<string, string> {
			{ "string", "char*" }, { "ushort", "uint16_t" }, { "CSize", "size_t" }, { "int", "int32_t" },
			{ "long", "int64_t" }, { "byte", "uint8_t" }, { "BinaryReader", "FILE*" }, { "BinaryWriter", "FILE*" },
			{ "List<char>", "char*" }, { "List<LinkData>", "LinkData*" }, { "List<Calculation>", "Calculation*" },
			{ "StreamWriter", "FILE*" }
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
				return (int)BSDExitCodes.BadCLIUsage;
			} else if (args[0] == args[1]) {
				Console.Error.WriteLine("OOTD Error: source and destination are the same file.");
				return (int)BSDExitCodes.CannotCreateOutputFile;
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

					destination.WriteLine("#define STRINGIZE_DETAIL(x) #x");
					destination.WriteLine("#define STRINGIZE(x) STRINGIZE_DETAIL(x)");
					destination.WriteLine();

					// Translate C# code to C
					var strCleaned = ExtractBody(strAllSource, "namespace", out var strIndent);
					strCleaned = RegionsToObjectiveCPragmaMarks(strCleaned);
					strCleaned = TearDownPartition(strCleaned, "class", strIndent);
					strCleaned = RemoveAnyKeyword(strCleaned, "internal", "private", "public", "protected");
					strCleaned = RemoveAnyKeyword(strCleaned, "static");
					strCleaned = Regex.Replace(strCleaned, @"(?:\t| )*// ReSharper [a-z]+(?: [a-z]+)? [A-Za-z_]+\r?\n", "");
					strCleaned = RedeclareStructs(strCleaned);
					strCleaned = RedeclareEnums(strCleaned);
					strCleaned = ReplaceDataTypeOfVariables(strCleaned);
					strCleaned = ConvertParamArrayArguments(strCleaned);
					strCleaned = ConvertOutArguments(strCleaned);
					strCleaned = Regex.Replace(strCleaned, @"([A-Za-z0-9*_]+)\[\]\s+([A-Za-z0-9_]+)", "$1 $2[]");
					strCleaned = ConvertVerbatimStrings(strCleaned);
					strCleaned = TranslateConsoleOutputCalls(strCleaned);
					strCleaned = TranslateFileInputOutput(strCleaned);
					strCleaned = TranslateInitObjects(strCleaned);
					strCleaned = Regex.Replace(strCleaned, @"([=!]=) null", "$1 NULL");
					strCleaned = AddressMemberAccess(strCleaned);
					strCleaned = HandleCounts(strCleaned);
					strCleaned = TranslateSpecificCalls(strCleaned);

					// Write C code
					strCleaned = strCleaned.Trim().Replace("\r\n", "\n").Replace('\r', '\n'); // Normalize to LF
					strCleaned = strCleaned.Replace("\n\n\n", "\n\n");                        // Coalesce white space
					strCleaned = Regex.Replace(strCleaned, "[ ]+", " ");
					strCleaned = strCleaned.Replace("\t\t ", "\t\t");
					strCleaned = strCleaned.Replace("\n", Environment.NewLine); // Normalize to platform line ending
					destination.WriteLine(strCleaned);
					return (int)BSDExitCodes.Success;
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

		private static MatchCollection FindStringLiterals(string extract)
		{
			return Regex.Matches(extract, @"(@?)[""](.*?)[""]", RegexOptions.Singleline);
		}

		private static string RegionsToObjectiveCPragmaMarks(string sourceCode)
		{
			sourceCode = Regex.Replace(sourceCode, @"(?:\t| )*#endregion.*\r?\n", "");
			return Regex.Replace(sourceCode, @"#region (.*)", "#pragma mark - $1");
		}

		#region RemoveAnyKeyword
		private static string RemoveAnyKeyword(string extract, params string[] keywords)
		{
			var mtcStrings = FindStringLiterals(extract);
			return Regex.Replace(extract, @"(^|\b)(" + String.Join("|", keywords) + ") ",
				m => RemoveOutsideLiteral(m, mtcStrings));
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
		#endregion

		private static string RedeclareStructs(string translating)
		{
			return Regex.Replace(translating, @"struct ([A-Za-z0-9_]+)\s+{(.*?)}", "typedef struct $1 {$2} $1;",
				RegexOptions.Singleline);
		}

		#region RedeclareEnums
		private static string RedeclareEnums(string translating)
		{
			var lstEnums = new List<string>();
			translating = Regex.Replace(translating, @"enum ([A-Za-z0-9_]+)(?:\s+:\s+([A-Za-z0-9_]+))?\s+{(.*?)}",
				m => RedeclareEnum(m, lstEnums), RegexOptions.Singleline);
			var c = lstEnums.Count;
			for (var i = 0; i < c; i++) {
				translating = translating.Replace(lstEnums[i] + ".", "");
			}
			return translating;
		}

		private static string RedeclareEnum(Match m, List<string> lstEnums)
		{
			var g        = m.Groups;
			var enumName = g[1].Value;
			lstEnums.Add(enumName);
			return QuickFormat("typedef enum {{1}} {0};", enumName, g[3].Value);
		}
		#endregion

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

		private static string ConvertParamArrayArguments(string translating)
		{
			translating = translating.Replace("params object[] ellipsis", "...");
			translating = Regex.Replace(translating, @"params object\[\] ([A-Za-z0-9_]+)", "va_list $1");
			return translating;
		}

		#region ConvertOutArguments
		private static string ConvertOutArguments(string translating)
		{
			// Function signature
			// "$1* $2"
			var lstOutVars = new List<Match>();
			translating = Regex.Replace(translating, @"out\s+([A-Za-z0-9_*]+)\s+([A-Za-z0-9_]+)",
				m => ConvertOutSignature(m, lstOutVars));
			// Usage
			translating = Regex.Replace(translating, @"out\s+([A-Za-z0-9_]+)", "&$1");
			// Value assignment
			var me = new MatchEvaluator(ConvertOutAssign);
			foreach (var m2 in lstOutVars) {
				translating = Regex.Replace(translating, "(" + m2.Groups[2].Value + @")\s+=\s+([A-Za-z0-9_]+);", me);
			}
			// Clean *(variable)
			translating = Regex.Replace(translating, @"[*][(]([a-z]+)[)]", "*$1");

			return translating;
		}

		private static string ConvertOutSignature(Match m, List<Match> lstOutVars)
		{
			var g = m.Groups;
			lstOutVars.Add(m);
			return g[1].Value + "* " + g[2].Value;
		}

		private static string ConvertOutAssign(Match m3)
		{
			var g3 = m3.Groups;
			return QuickFormat("*({0}) = {1};", g3[1].Value, g3[2].Value);
		}
		#endregion

		private static string ConvertVerbatimStrings(string translating)
		{
			var stringLiterals = FindStringLiterals(translating);
			var c              = stringLiterals.Count;
			// Process in reverse order to keep positions in stringLiterals valid
			for (var i = c - 1; i >= 0; i--) {
				if (stringLiterals[i].Value.StartsWith("@", StringComparison.Ordinal)) {
					var strarLines = stringLiterals[i].Groups[2].Value.Replace("\r\n", "\n").Split('\n');
					var stbLiteral = new StringBuilder(stringLiterals[i].Length);
					for (var j = 0; j < strarLines.Length; j++) {
						stbLiteral.Append('"').Append(strarLines[j].Replace("\t", "\\t")).AppendLine("\\n\"");
					}

					translating = translating.Replace(stringLiterals[i].Value, stbLiteral.ToString());
				}
			}

			return translating;
		}

		#region TranslateConsoleOutputCalls
		private static string TranslateConsoleOutputCalls(string translating)
		{
			translating = Regex.Replace(translating, @"Console(.Error)?.Write(Line)?[(](.*)", ReplaceSingleConsoleCall);
			translating = Regex.Replace(translating, @"LuigiFormat[(](.*)", ConvertLuigiFormatArgument);
			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\s+=\s+String\.Format[(](.*)", ConvertStringFormatArgument);
			return translating;
		}

		private static string ReplaceSingleConsoleCall(Match m)
		{
			var g           = m.Groups;
			var blnStdError = !String.IsNullOrEmpty(g[1].Value);
			var blnNewLine  = !String.IsNullOrEmpty(g[2].Value);
			var strArgument = g[3].Value;
			var mtcEllipsis = Regex.Match(strArgument, @"[(]?([A-Za-z0-9_]+).*\bellipsis\b");

			if (mtcEllipsis.Success) {
				// v(f)printf
				var strFormatArg = mtcEllipsis.Groups[1].Value;
				var stbVariadic  = new StringBuilder(127);
				stbVariadic.Append("va_list ellipsis; va_start(ellipsis, ").Append(strFormatArg).Append("); ");
				stbVariadic.Append(blnStdError ? "vfprintf(stderr, " : "vprintf(").Append(strArgument.Trim()).Append(" va_end(ellipsis);");
				if (blnNewLine) {
					stbVariadic.Append(blnStdError ? " fputs(\"\\n\", stderr);" : " puts(\"\");");
				}

				return stbVariadic.ToString();
			} else {
				var blnLiteral = strArgument.StartsWith("\"", StringComparison.Ordinal);
				var mtcFormats = Regex.Matches(strArgument, @"[{]\d+(.*?)[}]");
				if (blnLiteral && (mtcFormats.Count > 0)) {
					// printf or fprintf
					strArgument = ReformatArgumentCore(strArgument, blnNewLine);
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
		}

		private static string ConvertLuigiFormatArgument(Match m)
		{
			return "LuigiFormat(" + ReformatArgument(m.Groups[1].Value, true).Trim();
		}

		private static string ConvertStringFormatArgument(Match m)
		{
			var g           = m.Groups;
			var strArgument = ReformatArgument(g[2].Value, false);
			// Note: strArgument ends with «);»
			return QuickFormat(
				"int nbytes = snprintf(NULL, 0, {1} if (nbytes < 0) { " +
				"puts(\"ArgLink error: cannot evaluate length with snprintf, source code line \" STRINGIZE(__LINE__)); exit({2}); } else { " +
				"nbytes++; {0} = (char*)calloc((size_t)nbytes, sizeof(char)); if ({0} == NULL) { " +
				"puts(\"ArgLink error: cannot allocate memory, source code line \" STRINGIZE(__LINE__)); exit({2}); } else { " +
				"snprintf({0}, (size_t)nbytes, {1} } }",
				g[1].Value, strArgument.Trim(), ((byte)BSDExitCodes.InternalError).ToString());
		}

		private static string ReformatArgument(string extracted, bool addNewLine)
		{
			var blnLiteral = extracted.StartsWith("\"", StringComparison.Ordinal);
			var mtcFormats = Regex.Matches(extracted, @"[{]\d+(.*?)[}]");
			if (blnLiteral && (mtcFormats.Count > 0)) {
				extracted = ReformatArgumentCore(extracted, addNewLine);
			}

			return extracted;
		}

		private static string ReformatArgumentCore(string extracted, bool addNewLine)
		{
			var mtcItems = Regex.Matches(extracted, @",\s+([A-Za-z0-9_.\]\[""]+)");
			var strReformatted = Regex.Replace(extracted, @"[{](\d+)(.*?)[}]",
				m2 => ConvertFormatSpecifier(m2, mtcItems));
			if (addNewLine && strReformatted.Trim().EndsWith(");", StringComparison.Ordinal)) {
				// Replace only the first ending quote, not all ending quotes
				var firstEndQuote = strReformatted.IndexOf("\",", StringComparison.Ordinal);
				strReformatted = strReformatted.Substring(0, firstEndQuote) + "\\n\"," +
								 strReformatted.Substring(firstEndQuote + 2);
			}
			return strReformatted;
		}

		private static bool ContainsText(string haystack, string needle)
		{
			return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static string ConvertFormatSpecifier(Match m2, MatchCollection itemsToFormat)
		{
			// Rough but works for now
			var   g  = m2.Groups;
			var   g2 = g[2].Value;
			short width;
			if (g2.EndsWith(":X")) {
				return g2.StartsWith(",") && Int16.TryParse(g2.Substring(1, g2.Length - 3), out width)
					? "%" + width + "X" : "%X";
			} else {
				var position = Byte.Parse(g[1].Value);
				var item     = itemsToFormat[position].Groups[1].Value;
				if (item == "flag") { // char
					return "%c";
				} else if ((item == "min") || (item == "max")) { // uint8_t
					return "%hhu";
				} else if (item == "finalSize") {         // int64_t
					return "%\" PRId64 \"";               // "%lld";
				} else if (ContainsText(item, "count")) { // size_t
					return "%\" PRIuPTR \"";              // "%u";
				} else if (ContainsText(item, "total") ||
						   ContainsText(item, "size")) { // int32_t
					return "%\" PRId32 \"";              // "%d";
				} else {
					return g2.StartsWith(",") && Int16.TryParse(g2.Substring(1), out width) ? "%" + width + "s" : "%s";
				}
			}
		}
		#endregion

		#region TranslateFileInputOutput
		private static string TranslateFileInputOutput(string translating)
		{
			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\s+=\s+new FILE\*\(File.Open([A-Za-z0-9_]+)[(](.*)[)][)]", ReplaceOpenCall);
			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\s+=\s+new FILE\*\(new FileStream[(](.+?),\s+FileMode\.[A-Za-z]+,\s+FileAccess\.([A-Za-z]+),\s+FileShare\.[A-Za-z]+,\s+(.+?)[)][)]", ReplaceBufferedOpen);

			translating = translating.Replace("SeekOrigin.Begin", "SEEK_SET").Replace("SeekOrigin.Current", "SEEK_CUR")
				.Replace("SeekOrigin.End", "SEEK_END");
			translating = Regex.Replace(translating, @"([A-Za-z0-9_.]+)\.Seek[(]([A-Za-z0-9_\]\[\+ -]+),", ReplaceSeekCall);

			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.BaseStream\.Position", "ftell($1)");

			// I would normally save the current position to a variable, then restore it with fseek, but there is
			// already a fseek to the first byte right after.
			translating = Regex.Replace(translating,
				@"([A-Za-z0-9_]+)\s+([A-Za-z0-9_]+)\s+=\s+([A-Za-z0-9_]+)\.BaseStream\.Length",
				"fseek($3, 0, SEEK_END); $1 $2 = ftell($3)");

			translating = Regex.Replace(translating,
				@"(?:(?:([A-Za-z0-9_]+)\s+)?([A-Za-z0-9_]+)\s+=\s+)?([A-Za-z0-9_]+)\.Read(Char|Byte)\(\)(?:\s+([^\s]*))?",
				ReplaceReadByte);
			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.ReadInt32\(\)", "ReadLEInt32($1)");
			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.ReadUInt16\(\)", "fgetc($1) | (fgetc($1) << 8)");

			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.BaseStream\.WriteByte[(](.*)[)]", "fputc($2, $1)");
			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.Write\(\(uint8_t[)](.*)[)]", "fputc($2, $1)");
			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.Write\(\(uint16_t[)](.*)[)]", "fputc($2 & 0xff, $1); fputc($2 >> 8, $1)");

			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.(Read|Write)[(](.*?), 0, (.*)[)];", "f$2($3, sizeof(uint8_t), $4, $1);");
			translating = translating.Replace("fRead(", "fread(").Replace("fWrite(", "fwrite(");

			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)[.]Write(Line)?[(](.*)", ReplaceWriteTextFile);

			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.Close\(\)", "fclose($1); free($1Buffer)");

			return translating;
		}

		private static string ReplaceReadByte(Match m)
		{
			var g            = m.Groups;
			var fileVariable = g[3].Value;
			var postOperator = g[5].Value; // == != | <<
			if (String.IsNullOrEmpty(postOperator)) {
				var outputType     = g[1].Value;
				var outputVariable = g[2].Value;
				var strBadFileIO   = ((byte)BSDExitCodes.BadFileIO).ToString();
				if (String.IsNullOrEmpty(outputType) && String.IsNullOrEmpty(outputVariable)) { // byte skip;
					return QuickFormat(
						"if (fgetc({0}) == EOF) { puts(\"ArgLink error: reading byte from {0} failed, source code line \" STRINGIZE(__LINE__)); exit({1}); }",
						fileVariable, strBadFileIO);
				} else if (String.IsNullOrEmpty(outputType)) { // assignment of previously declared variable
					var readWidth = g[4].Value;
					return String.Format(CultureInfo.InvariantCulture,
						"{{ int whatRead = fgetc({1}); if (whatRead == EOF) {{ puts(\"ArgLink error: reading byte from {1} failed, source code line \" STRINGIZE(__LINE__)); exit({3}); }} else {{ {0} = ({2})whatRead; }} }}",
						outputVariable, fileVariable, readWidth == "Char" ? "char" : "uint8_t", strBadFileIO);
				} else { // declaration and assignment of variable
					return String.Format(CultureInfo.InvariantCulture, (outputType == "int") || (outputType == "int32_t") ?
						"{0} {1} = fgetc({2}); if ({1} == EOF) {{ puts(\"ArgLink error: reading byte from {2} failed, source code line \" STRINGIZE(__LINE__)); exit({3}); }}" :
						"{0} {1}; {{ int whatRead = fgetc({2}); if (whatRead == EOF) {{ puts(\"ArgLink error: reading byte from {2} failed, source code line \" STRINGIZE(__LINE__)); exit({3}); }} else {{ {1} = ({0})whatRead; }} }}",
						outputType, outputVariable, fileVariable, strBadFileIO);
				}
			} else {
				return QuickFormat("fgetc({0}) {1}", fileVariable, postOperator);
			}
		}

		private static string CFileOpenMode(string method)
		{
			var strMode = "";
			if (method == "Read") {
				strMode = "rb";
			} else if (method == "Write") {
				strMode = "wb";
			} /*else if (strMode == "Text") {
				strMode = "r";
			}// */
			return strMode;
		}

		private static string ReplaceOpenCall(Match m)
		{
			var g = m.Groups; // handle, access, filePath
			return ReplaceAnyOpen("{0} = fopen({1}, \"{4}\"); if ({0} == NULL) {{ " +
								  "puts(\"ArgLink error: cannot open {1} in {2} mode, source code line \" STRINGIZE(__LINE__)); exit({5}); }}",
				g[1].Value, g[3].Value, g[2].Value, "");
		}

		private static string ReplaceBufferedOpen(Match m)
		{
			var g = m.Groups; // handle, filePath, access, bufferSize
			return ReplaceAnyOpen("{0} = fopen({1}, \"{4}\"); if ({0} == NULL) {{ " +
								  "puts(\"ArgLink error: cannot open {1} in {2} mode, source code line \" STRINGIZE(__LINE__)); exit({5}); }}; " +
								  "size_t {0}Zone = (size_t)({3}); char* {0}Buffer = ({0}Zone > 0) ? (char*)calloc({0}Zone, sizeof(char)) : NULL; setvbuf({0}, {0}Buffer, {0}Buffer ? _IOFBF : _IONBF, {0}Zone)",
				g[1].Value, g[2].Value, g[3].Value, g[4].Value);
		}

		private static string ReplaceAnyOpen(string format, string handle, string filePath, string access,
		string bufferExpression)
		{
			return String.Format(CultureInfo.InvariantCulture, format,
				handle, filePath, access, bufferExpression, CFileOpenMode(access),
				access == "Write" ? (byte)BSDExitCodes.CannotCreateOutputFile : (byte)BSDExitCodes.UnreadableInputFile);
		}

		private static string ReplaceSeekCall(Match m)
		{
			return "fseek(" + m.Groups[1].Value.Replace(".BaseStream", "") + ", " + m.Groups[2].Value + ",";
		}

		private static string ReplaceWriteTextFile(Match m)
		{
			var g = m.Groups;
			return QuickFormat("fprintf({0}, {1}", g[1].Value,
				ReformatArgument(g[3].Value, !String.IsNullOrEmpty(g[2].Value)));
		}
		#endregion

		#region TranslateInitObjects
		private static string TranslateInitObjects(string translating)
		{
			translating = Regex.Replace(translating,
				@"([A-Za-z0-9_\[\]*]+)\s+([A-Za-z0-9_\[\]]+)\s+= new ([A-Za-z0-9<>_\[\]() .-]+);", ConvertInit);
			translating = translating.Replace("Calculation InitCalculation(", "Calculation* InitCalculation(");
			translating = Regex.Replace(translating, @"Calculation\s+([A-Za-z0-9_]+)\s+= InitCalculation[(]",
				"Calculation* $1 = InitCalculation(");
			translating = translating.Replace("= null;", "= NULL;");
			return translating;
		}

		private static string ConvertInit(Match m)
		{
			const byte kAllocFailedCode = (byte)BSDExitCodes.InternalError;
			const string kAllocFailed =
				" if ({1} == NULL) {{ puts(\"ArgLink error: cannot allocate for {1} of type {0}*, source code line \" STRINGIZE(__LINE__)); exit({2}); }}";

			var g           = m.Groups;
			var type        = g[1].Value;
			var variable    = g[2].Value;
			var ctor        = g[3].Value;
			var ciInvariant = CultureInfo.InvariantCulture;
			if (variable.EndsWith("[]", StringComparison.Ordinal)) { // array
				var bracket = ctor.IndexOf('[');
				var count   = ctor.Substring(bracket + 1, ctor.Length - bracket - 2);
				return String.Format(ciInvariant,
					"{0}* {1} = ({0}*)calloc((size_t){3}, sizeof({0}));" + kAllocFailed,
					type, variable.Replace("[]", ""), kAllocFailedCode, count);
			} else if (ctor.StartsWith("List<", StringComparison.Ordinal)) {
				return QuickFormat("{0} {1} = NULL; size_t {1}Count = 0;", type, variable);
			} else if (ctor.EndsWith("()", StringComparison.Ordinal)) { // struct
				return String.Format(ciInvariant,
					"{0}* {1} = ({0}*)calloc(1, sizeof({0}));" + kAllocFailed,
					type, variable, kAllocFailedCode);
			} else {
				return m.Value; // i.e. no change
			}
		}
		#endregion

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
			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)[.]Count", "$1Count");

			translating = Regex.Replace(translating, @"\s+Main[(]char[*]\s+args\[\][)]", " main(int argc, char* argv[])");
			translating = translating.Replace("int32_t main(", "int main("); // DJGPP dislikes anything other than int
			translating = translating.Replace("char* args[]", "char* argv[]");
			translating = translating.Replace("args.Length", "(argc - 1)").Replace("args[", "argv[1 + ");
			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.Length", "strlen($1)");
			translating = Regex.Replace(translating, @",\s+args,", ", argv,");

			// TODO : Have a more general way to handle parameter type List<LinkData> => LinkData*, size_t.
			// It will also have an impact on TranslateListAdd.
			translating = translating.Replace("Search(LinkData* link, char* name)", "Search(LinkData* link, size_t linkCount, char* name)");
			translating = translating.Replace(", LinkData* link)", ", LinkData* link, size_t* linkCount)");
			translating = Regex.Replace(translating, @"= Search[(]([A-Za-z0-9_]+),", "= Search($1, *$1Count,");
			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)[(](.*), link\)", "$1($2, link, &linkCount)");

			translating = translating.Replace("char* GetNameChars(FILE* fileSob)", "char* GetNameChars(FILE* fileSob, size_t* nametempCount)");
			translating = translating.Replace("char* nametemp = NULL; size_t nametempCount = 0;", "char* nametemp = NULL; *nametempCount = 0;");
			translating = translating.Replace("char* nametemp = GetNameChars(fileSob);", "size_t nametempCount; char* nametemp = GetNameChars(fileSob, &nametempCount);");
			translating = translating.Replace("return String.Concat(GetNameChars(fileSob));", "size_t Count; return String.Concat(GetNameChars(fileSob, &Count));");
			translating = translating.Replace("return new String(GetNameChars(fileSob).ToArray());", "size_t Count; return new String(GetNameChars(fileSob, &Count).ToArray());");
			return translating;
		}

		#region TranslateSpecficCalls
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

			translating = Regex.Replace(translating, @"Char\.(To[A-Za-z]+er)", MetaChangeCase);

			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\s+=\s+([A-Za-z0-9_]+)\.Substring[(](\d+)[)]", "*$1 = ($2 + $3)");
			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.Substring[(](\d+)[)]", "($1 + $2)");

			translating = Regex.Replace(translating, @"String\.IsNullOrEmpty[(]([A-Za-z0-9_]+)[)]", "(($1 == NULL) || (strlen($1) < 1))");

			// Translate <T>.TryParse after .Substring because .Substring can be the argument of TryParse
			translating = Regex.Replace(translating, @"([A-Za-z0-9_]+)\.TryParse[(](.*),\s+([A-Za-z0-9_&*]+)[)]", TranslateTryParse);

			translating = Regex.Replace(translating, @"return Path\.GetExtension[(]([A-Za-z0-9_]+)[)]", "char* dot = strrchr($1, '.'); return (!dot || dot == $1) ? NULL : dot");

			translating = Regex.Replace(translating,
				@"([A-Za-z0-9_]+)\s*=\s*([A-Za-z0-9_]+)[.]Replace[(]'(.+?)',\s*'(.+?)'[)]", InPlaceCharReplace);

			return translating;
		}

		private static string MetaChangeCase(Match m)
		{
			return m.Groups[1].Value.ToLowerInvariant();
		}

		private static string TranslateListRemoveAt(Match m, string translating1)
		{
			var list     = m.Groups[1].Value;
			var at       = m.Groups[2].Value;
			var listType = Regex.Match(translating1, @"([A-Za-z0-9_]+)[*]\s+" + list + " = NULL").Groups[1].Value;
			return String.Format(CultureInfo.InvariantCulture,
				"size_t after = {1}Count - 1 - {2};{0}if (after > 0) {{{0}" +
				"\tmemmove(&({1}[{2}]), &({1}[{2} + 1]), after * sizeof({3}));{0}" +
				"}}{0}{1}Count--;{0}{1} = ({3}*)realloc({1}, {1}Count * sizeof({3})); " +
				"if ({1} == NULL) {{ puts(\"ArgLink error: cannot trim list of {3} named {1}, source code line \" STRINGIZE(__LINE__)); exit({4}); }}",
				Environment.NewLine + "\t\t\t\t\t\t\t", list, at, listType, (byte)BSDExitCodes.InternalError);
		}

		private static Match LastMatchBefore(int limit, string haystack, string pattern)
		{
			var mtcAll = Regex.Matches(haystack, pattern);
			var c      = mtcAll.Count;
			for (int i = c - 1; i >= 0; i--) {
				if (mtcAll[i].Success && (mtcAll[i].Index < limit)) {
					return mtcAll[i];
				}
			}
			return null;
		}

		private static string TranslateListAdd(Match m, string translating1)
		{
			var list  = m.Groups[1].Value;
			var item  = m.Groups[2].Value;
			var limit = m.Index;
			var mtcParam = LastMatchBefore(limit, translating1,
				@"([A-Za-z0-9_]+)[*]\s+" + list + @", (([A-Za-z0-9_*]+)\s+|[*])" + list + @"Count\)");
			string countType, listType;
			if (mtcParam != null) {
				listType  = mtcParam.Groups[1].Value;
				countType = mtcParam.Groups[2].Value.Trim();
			} else {
				countType = LastMatchBefore(limit, translating1, @"(([A-Za-z0-9_]+)\s+|[*])" + list + "Count = 0").Groups[1].Value.Trim();
				listType = LastMatchBefore(limit, translating1, @"([A-Za-z0-9_]+)[*]\s+" + list + " = NULL").Groups[1].Value;
			}

			// (*nametempCount)++; nametemp = realloc(nametemp, *nametempCount * sizeof(char)); nametemp[*nametempCount - 1] = check
			const string kReallocFailed =
				"if ({0} == NULL) {{ puts(\"ArgLink error: cannot grow list of {2} named {0}, source code line \" STRINGIZE(__LINE__)); exit({4}); }};";
			return String.Format(CultureInfo.InvariantCulture, countType.EndsWith("*", StringComparison.Ordinal) ? // is count is a passed pointer?
					"(*{0}Count)++; {0} = ({2}*)realloc({0}, *{0}Count * sizeof({2}));" + kReallocFailed + " {0}[*{0}Count - 1] = {3}{1}"
					: "{0}Count++; {0} = ({2}*)realloc({0}, {0}Count * sizeof({2}));" + kReallocFailed + " {0}[{0}Count - 1] = {3}{1}",
				list, item, listType, listType == "char" ? "" : "*", (byte)BSDExitCodes.InternalError);
		}

		private static string TranslateStringFromCharArray(Match m)
		{
			var leftHand  = m.Groups[1].Value.Trim();
			var rightHand = m.Groups[2].Value;
			var openParen = rightHand.IndexOf('(');
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

		private static string TranslateTryParse(Match m)
		{
			var g = m.Groups;
			return QuickFormat("sscanf({0}, \"{1}\", {2})", g[2].Value, ManagedTypeToScanfPattern(g[1].Value), g[3].Value);
		}

		private static string ManagedTypeToScanfPattern(string managedType)
		{
			if (managedType == nameof(Byte)) {
				return "%hhu";
			} else {
				throw new NotSupportedException();
			}
		}

		private static string InPlaceCharReplace(Match m)
		{
			var g      = m.Groups;
			var source = g[2].Value;
			return g[1].Value == source ? QuickFormat(
				"for (char* current_pos; (current_pos = strchr({0}, '{1}')) != NULL; *current_pos = '{2}')",
				source, g[3].Value, g[4].Value) : m.Value;
		}
		#endregion

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
