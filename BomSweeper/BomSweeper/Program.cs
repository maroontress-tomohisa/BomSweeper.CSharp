namespace BomSweeper
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Maroontress.Cui;

    /// <summary>
    /// The bootstrap class.
    /// </summary>
    public sealed class Program
    {
        private Action<Action> doIfVerbose = a => { };
        private Action chdirAction = () => { };
        private Action<string> bomFileConsumer;
        private int maxDepth = PathFinder.DefaultMaxDepth;

        /// <summary>
        /// Initializes a new instance of the <see cref="Program"/> class.
        /// </summary>
        /// <param name="args">
        /// The command-line options.
        /// </param>
        public Program(string[] args)
        {
            var maxDepthDescription
                = "The maximum number of directory levels to search.\n"
                + $"(Default: '{maxDepth}')";

            bomFileConsumer = PrintBomFilename;

            static int ParseMaxDepth(ValueOption o)
            {
                var s = o.Value;
                if (!int.TryParse(s, out var value)
                    || value <= 0)
                {
                    throw new OptionParsingException(
                        o, $"invalid number for the maximum depth: {s}");
                }
                return value;
            }

            void ShowUsageAndExit(Option o)
            {
                Usage(o.Schema);
                throw new TerminateProgramException(1);
            }

            var schema = Options.NewSchema()
                .Add(
                    "remove",
                    'R',
                    o => bomFileConsumer = BomKit.RemoveBom,
                    "Remove a BOM")
                .Add(
                    "directory",
                    'C',
                    o => chdirAction = () => ChangeDirectory(o),
                    "DIR",
                    "Change to directory. (Default: '.')")
                .Add(
                    "max-depth",
                    'D',
                    o => maxDepth = ParseMaxDepth(o),
                    "N",
                    maxDepthDescription)
                .Add(
                    "verbose",
                    'v',
                    o => doIfVerbose = a => a(),
                    "Be verbose")
                .Add(
                    "help",
                    'h',
                    ShowUsageAndExit,
                    "Show this message and exit");

            Setting = schema.Parse(args);

            if (!Setting.Arguments.Any())
            {
                Usage(schema);
                throw new TerminateProgramException(1);
            }
        }

        /// <summary>
        /// Gets the setting of the command-line options.
        /// </summary>
        private Setting Setting { get; }

        /// <summary>
        /// The entry point.
        /// </summary>
        /// <param name="args">
        /// The command line options.
        /// </param>
        public static void Main(string[] args)
        {
            try
            {
                var program = new Program(args);
                program.Launch();
                Environment.Exit(0);
            }
            catch (OptionParsingException e)
            {
                Console.WriteLine(e.Message);
                Usage(e.Schema);
                Environment.Exit(1);
            }
            catch (TerminateProgramException e)
            {
                Environment.Exit(e.StatusCode);
            }
        }

        private static void ChangeDirectory(ValueOption o)
        {
            var dir = o.Value;
            if (dir is null)
            {
                return;
            }

            static void Abort(string m)
            {
                Console.WriteLine(m);
                throw new TerminateProgramException(1);
            }

            try
            {
                Directory.SetCurrentDirectory(dir);
            }
            catch (ArgumentException)
            {
                Abort($"{dir}: Invalid path.");
            }
            catch (PathTooLongException)
            {
                Abort($"{dir}: Too long path.");
            }
            catch (FileNotFoundException)
            {
                Abort($"{dir}: Not found.");
            }
            catch (DirectoryNotFoundException)
            {
                Abort($"{dir}: Not found.");
            }
            catch (IOException)
            {
                Abort($"{dir}: An I/O error occurred.");
            }
        }

        private static void Usage(OptionSchema schema)
        {
            var dllName = typeof(Program).Assembly.GetName().Name;
            var all = $"usage: dotnet {dllName}.dll [Options]... PATTERN...\n"
                + "\n"
                + "Options are:";
            foreach (var m in all.Split('\n'))
            {
                Console.WriteLine(m);
            }
            var lines = schema.GetHelpMessage();
            foreach (var s in lines)
            {
                Console.WriteLine(s);
            }
        }

        private void PrintBomFilename(string file)
        {
            Console.WriteLine($"{file}: Starts with a BOM.");
        }

        private void Launch()
        {
            chdirAction();

            static Regex NewRegex(string p)
            {
                var options = RegexOptions.CultureInvariant
                    | RegexOptions.Singleline;
                return new Regex(p, options);
            }

            var pattern = Globs.ToPattern(Setting.Arguments);

            doIfVerbose(() =>
            {
                Console.WriteLine($"Pattern: {pattern}");
            });

            var regex = NewRegex(pattern);
            var prefix = "." + Path.DirectorySeparatorChar;
            var files = PathFinder.GetFiles(".", maxDepth)
                .Where(f => f.StartsWith(prefix))
                .Select(f => f.Substring(prefix.Length)
                    .Replace(Path.DirectorySeparatorChar, '/'))
                .Where(f => regex.IsMatch(f))
                .Select(f => f.Replace('/', Path.DirectorySeparatorChar));

            doIfVerbose(() =>
            {
                Console.WriteLine("Matched files:");
                foreach (var f in files)
                {
                    Console.WriteLine(f);
                }
            });

            var bomFiles = files.Where(BomKit.StartsWithBom);
            foreach (var f in bomFiles)
            {
                bomFileConsumer(f);
            }

            throw new TerminateProgramException(bomFiles.Any() ? 1 : 0);
        }
    }
}
