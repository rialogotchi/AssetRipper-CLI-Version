using AssetRipper.Export.Configuration;
using AssetRipper.Export.PrimaryContent;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;
using AssetRipper.Processing;

namespace AssetRipper.Headless;

/// <summary>
/// Web server, browser and native dialog free command line frontend.
/// </summary>
internal static class Program
{
	private const string Usage = """
		AssetRipper.Headless - command line version of AssetRipper

		Usage:
		  AssetRipper.Headless -i <file-or-folder> [-i <file-or-folder> ...] -o <output-dir> [options]
		  AssetRipper.Headless <file-or-folder> [<file-or-folder> ...] -o <output-dir> [options]

		Options:
		  -i, --input <path>     Input file or folder. Can be repeated.
		  -o, --output <dir>     Output directory. (required)
		  -m, --mode <mode>      'project' (Unity project, default) or 'primary' (primary content only).
		  -c, --clean            Delete the output directory first if it is not empty.
		  -l, --log <file>       Also write the log to this file.
		  -h, --help             Show this help.

		Settings are read from AssetRipper.json next to the executable, if present.

		Exit codes: 0 = success, 1 = failure, 2 = invalid arguments.
		""";

	private enum ExportMode
	{
		Project,
		Primary,
	}

	private static int Main(string[] args)
	{
		List<string> inputs = [];
		string? output = null;
		string? logPath = null;
		bool clean = false;
		ExportMode mode = ExportMode.Project;

		for (int i = 0; i < args.Length; i++)
		{
			string arg = args[i];
			switch (arg)
			{
				case "-h" or "--help" or "-?":
					Console.WriteLine(Usage);
					return 0;
				case "-i" or "--input":
					if (!TryGetValue(args, ref i, out string? input)) return InvalidArguments($"Missing value for {arg}");
					inputs.Add(input);
					break;
				case "-o" or "--output":
					if (!TryGetValue(args, ref i, out output)) return InvalidArguments($"Missing value for {arg}");
					break;
				case "-l" or "--log":
					if (!TryGetValue(args, ref i, out logPath)) return InvalidArguments($"Missing value for {arg}");
					break;
				case "-c" or "--clean":
					clean = true;
					break;
				case "-m" or "--mode":
					if (!TryGetValue(args, ref i, out string? modeString)) return InvalidArguments($"Missing value for {arg}");
					switch (modeString.ToLowerInvariant())
					{
						case "project": mode = ExportMode.Project; break;
						case "primary": mode = ExportMode.Primary; break;
						default: return InvalidArguments($"Unknown mode '{modeString}'");
					}
					break;
				default:
					if (arg.StartsWith('-'))
					{
						return InvalidArguments($"Unknown option '{arg}'");
					}
					inputs.Add(arg);
					break;
			}
		}

		if (inputs.Count == 0)
		{
			return InvalidArguments("No input path was provided.");
		}
		if (string.IsNullOrWhiteSpace(output))
		{
			return InvalidArguments("No output directory was provided.");
		}

		foreach (string inputPath in inputs)
		{
			if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
			{
				return InvalidArguments($"Input path '{inputPath}' does not exist.");
			}
		}

		Logger.Add(new ConsoleLogger());
		if (!string.IsNullOrEmpty(logPath))
		{
			Logger.Add(new FileLogger(logPath));
		}
		Logger.LogSystemInformation("AssetRipper.Headless");

		try
		{
			return Run(inputs.Select(static p => Path.GetFullPath(p)).ToArray(), Path.GetFullPath(output), mode, clean);
		}
		catch (Exception ex)
		{
			Logger.Error(LogCategory.General, "Unhandled exception", ex);
			return 1;
		}
	}

	private static int Run(string[] inputs, string output, ExportMode mode, bool clean)
	{
		if (!IsSafeOutputDirectory(output))
		{
			Logger.Error(LogCategory.Export, $"Refusing to use '{output}' as an output directory.");
			return 1;
		}

		FullConfiguration settings = new();
		settings.LoadFromDefaultPath();
		settings.LogConfigurationValues();

		ExportHandler exportHandler = new(settings);
		GameData gameData = exportHandler.LoadAndProcess(inputs, LocalFileSystem.Instance);

		if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
		{
			if (!clean)
			{
				Logger.Error(LogCategory.Export, $"Output directory '{output}' is not empty. Use --clean to delete it first.");
				return 1;
			}
			Logger.Info(LogCategory.Export, "Clearing export directory...");
			Directory.Delete(output, true);
		}
		Directory.CreateDirectory(output);

		switch (mode)
		{
			case ExportMode.Project:
				exportHandler.Export(gameData, output, LocalFileSystem.Instance);
				break;
			case ExportMode.Primary:
				Logger.Info(LogCategory.Export, "Starting primary content export");
				settings.ExportRootPath = output;
				PrimaryContentExporter.CreateDefault(gameData, settings).Export(gameData.GameBundle, settings, LocalFileSystem.Instance);
				Logger.Info(LogCategory.Export, "Finished exporting primary content.");
				break;
		}

		Logger.Info(LogCategory.General, "Done.");
		return 0;
	}

	private static bool TryGetValue(string[] args, ref int index, [NotNullWhen(true)] out string? value)
	{
		if (index + 1 < args.Length)
		{
			value = args[++index];
			return true;
		}
		value = null;
		return false;
	}

	private static bool IsSafeOutputDirectory(string path)
	{
		string? root = Path.GetPathRoot(path);
		if (string.IsNullOrEmpty(path) || string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(root ?? ""), StringComparison.Ordinal))
		{
			return false;
		}
		return Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is not ("Desktop" or "Documents" or "Downloads");
	}

	private static int InvalidArguments(string message)
	{
		Console.Error.WriteLine(message);
		Console.Error.WriteLine();
		Console.Error.WriteLine(Usage);
		return 2;
	}
}
