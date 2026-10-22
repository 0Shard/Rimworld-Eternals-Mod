// Author: 0Shard
// Description: Strict dependency-free loading of operational benchmark profiles.
#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Eternal.InGameTests.Performance
{
    public sealed class PerformanceProfile
    {
        public string Name { get; }
        public int Population { get; }
        public int MapSize { get; }
        public bool EmptyControl { get; }
        public int Samples { get; }
        public PerformanceProfile(string name, int population, int mapSize, bool emptyControl, int samples = 0)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Any(c => !char.IsLetterOrDigit(c) && c != '-'))
            {
                throw new ArgumentException("Profile name must use letters, digits, or hyphens");
            }

            if (population < 1 || population > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(population));
            }

            if (mapSize < 25 || mapSize > 500)
            {
                throw new ArgumentOutOfRangeException(nameof(mapSize));
            }

            if (samples < 0 || samples > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(samples));
            }

            Name = name; Population = population; MapSize = mapSize; EmptyControl = emptyControl; Samples = samples;
        }
    }

    public sealed class PerformanceConfiguration
    {
        public int Seed { get; private set; }
        public int WarmupFrames { get; private set; }
        public int BatchFrames { get; private set; }
        public int Batches { get; private set; }
        public int Samples { get; private set; }
        public int EventEveryBatches { get; private set; }
        public int DeathValidationTicks { get; private set; }
        public int ResurrectionValidationTicks { get; private set; }
        public IReadOnlyList<int> FrameSteps { get; private set; }
        public IReadOnlyList<PerformanceProfile> Profiles { get; private set; }
        public string OutputDirectory { get; private set; }

        public static PerformanceConfiguration Load(string path)
        {
            var document = XDocument.Load(path);
            var root = document.Root;
            if (root == null || root.Name != "gameTest")
            {
                throw new FormatException("Expected gameTest configuration root");
            }

            var benchmark = root.Element("performance");
            if (benchmark == null)
            {
                throw new FormatException("Missing performance configuration");
            }

            var configuration = new PerformanceConfiguration
            {
                Seed = RequiredInt(benchmark, "seed", 0, int.MaxValue),
                WarmupFrames = RequiredInt(benchmark, "warmupFrames", 1, 100000),
                BatchFrames = RequiredInt(benchmark, "batchFrames", 1, 100000),
                Batches = RequiredInt(benchmark, "batches", 2, 1000),
                Samples = RequiredInt(benchmark, "samples", 1, 100),
                EventEveryBatches = RequiredInt(benchmark, "eventEveryBatches", 1, 1000),
                DeathValidationTicks = RequiredInt(benchmark, "deathValidationTicks", 1, 1000000),
                ResurrectionValidationTicks = RequiredInt(benchmark, "resurrectionValidationTicks", 1, 1000000),
                OutputDirectory = RequiredString(benchmark, "outputDirectory")
            };
            var steps = RequiredString(benchmark, "frameSteps").Split(',').Select(s =>
            {
                if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 0 || value > 10000)
                {
                    throw new FormatException("frameSteps must be a comma-separated sequence of 0..10000");
                }

                return value;
            }).ToArray();
            if (steps.Length < 2 || !steps.Contains(0) || !steps.Any(s => s > 1) || !steps.Contains(1))
            {
                throw new FormatException("frameSteps must contain repeated-T (0), single ticks (1), and multi-tick jumps");
            }

            configuration.FrameSteps = Array.AsReadOnly(steps);
            var profiles = benchmark.Elements("profile").Select(p => new PerformanceProfile(
                RequiredString(p, "name"), RequiredInt(p, "population", 1, 100),
                RequiredInt(p, "mapSize", 25, 500), RequiredBool(p, "emptyControl"),
                p.Attribute("samples") == null ? 0 : RequiredInt(p, "samples", 1, 100))).ToArray();
            if (profiles.Length == 0 || profiles.Select(p => p.Name).Distinct().Count() != profiles.Length)
            {
                throw new FormatException("Profiles must exist and have distinct names");
            }

            configuration.Profiles = Array.AsReadOnly(profiles);
            return configuration;
        }
        private static string RequiredString(XElement element, string name)
        {
            var value = (string)element.Attribute(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new FormatException("Missing configuration attribute " + name);
            }

            return value;
        }
        private static int RequiredInt(XElement element, string name, int min, int max)
        {
            if (!int.TryParse(RequiredString(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                || value < min || value > max)
            {
                throw new FormatException(name + " must be " + min + ".." + max);
            }

            return value;
        }
        private static bool RequiredBool(XElement element, string name)
        {
            if (!bool.TryParse(RequiredString(element, name), out bool value))
            {
                throw new FormatException(name + " must be true or false");
            }

            return value;
        }
    }
}
#endif
