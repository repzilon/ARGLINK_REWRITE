using System;

namespace Exploratorium.ArgSfx.OutOfThisDimension
{
	internal static class Program
	{
		private static void OutputLogo()
		{
			Console.WriteLine("Out Of This Dimension\t(c) 2025 Repzilon\n");
		}

		private static void OutputUsage()
		{
			Console.WriteLine("NAME\n\tOOTD - Out Of This Dimension\n\n" +
							  "SYNOPSIS\n\tootd <orig-source.cs> <translated.c>\n\n" +
							  "DESCRIPTION\n\tOOTD is a regular expression powered special purpose C# to C translator\n" +
							  "\tmade specifically for ARGLINK_REWRITE. No artificial intelligence is\n" +
							  "\tinvolved, just old fashioned software text replacement.\n");
			//puts("OPTIONS\n\t\n");
			//puts("ENVIRONMENT\n\t\n");
			Console.WriteLine("EXIT STATUS\n\t0 on command success, 1 when this general help message is shown,\n" +
							  "\t2 on an internally caught error.\n");
			//puts("EXAMPLES\n\t\n");
			//puts("COMPATIBILITY\n\t\n");
			//puts("SEE ALSO\n\t\n");
			//puts("STANDARDS\n\t\n");
			//puts("HISTORY\n\t\n");
			//puts("BUGS\n\t\n"););
		}

		static int Main(string[] args)
		{
			OutputLogo();
			if (args.Length < 2) {
				OutputUsage();
				return 1;
			} else {
				return 2;
			}
		}
	}
}
