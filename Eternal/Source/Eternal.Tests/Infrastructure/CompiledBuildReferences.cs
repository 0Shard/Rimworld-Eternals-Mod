// Author: 0Shard
// Description: Validate an exact compiler-reference/artifact manifest, never search package versions.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace Eternal.Tests.Infrastructure
{
    internal static class CompiledBuildReferences
    {
        private static readonly Lazy<XElement> Manifest = new Lazy<XElement>(Load);
        private static string captureDirectory;
        public static CompiledAssemblyContract OpenProduction() => new CompiledAssemblyContract(ProductionPath);
        public static string ProductionPath => ValidatedPath(Manifest.Value.Element("artifact"), BesideTests("Eternal.dll"));
        public static string GameReferencePath => ValidatedPath(GameReference(Manifest.Value));

        private static XElement GameReference(XElement manifest)
        {
            var references = manifest.Elements("reference").Where(r => System.IO.Path.GetFileName((string)r.Attribute("path")) == "Assembly-CSharp.dll").ToArray();
            string pin = (string)manifest.Attribute("rimworldPackage");
            if (references.Length != 1 || string.IsNullOrEmpty(pin) || (string)references[0].Attribute("package") != pin)
            {
                throw new InvalidOperationException("Exactly one game reference matching pinned project.assets package required");
            }

            return references[0];
        }

        public static string OptionalPath(string mod)
        {
            var row = Manifest.Value.Elements("optional").SingleOrDefault(r => (string)r.Attribute("name") == mod);
            if (row == null)
            {
                throw new InvalidOperationException("Optional assembly has no configured path: " + mod);
            }

            return (string)row.Attribute("path");
        }
        public static bool RequireOptional => (string)Manifest.Value.Attribute("optionalPolicy") == "require_installed";
        internal static XElement CapturedManifest => new XElement(Manifest.Value);
        private static string BesideTests(string file) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(new Uri(typeof(CompiledBuildReferences).Assembly.CodeBase).LocalPath), file);

        private static XElement Load()
        {
            string path = Environment.GetEnvironmentVariable("ETERNAL_COMPILED_MANIFEST");
            if (string.IsNullOrEmpty(path))
            {
                var directory = new DirectoryInfo(System.IO.Path.GetDirectoryName(new Uri(typeof(CompiledBuildReferences).Assembly.CodeBase).LocalPath));
                while (directory != null && !File.Exists(System.IO.Path.Combine(directory.FullName, "quality-check.config.json")))
                {
                    directory = directory.Parent;
                }

                if (directory == null)
                {
                    throw new InvalidOperationException("Standalone metadata checks require the owned checkout or ETERNAL_COMPILED_MANIFEST");
                }

                string artifact = BesideTests("Eternal.dll");
                // Standalone checks query the same installed SDK/restored compiler inputs; they do not build or restore.
                var start = new ProcessStartInfo("python3")
                {
                    Arguments = "tools/quality_evidence.py manifest --artifact " + Quote(artifact),
                    WorkingDirectory = directory.FullName,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var process = Process.Start(start);
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("Cannot resolve standalone compile manifest: " + error + output);
                }

                path = output.Trim().Split('\n').Last().Trim();
            }
            var manifest = XElement.Load(path);
            captureDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            var artifactDirectory = new DirectoryInfo(System.IO.Path.GetDirectoryName(BesideTests("Eternal.dll")));
            return ValidateManifest(manifest, BesideTests("Eternal.dll"), artifactDirectory.Parent?.Name);
        }

        internal static XElement ValidateManifest(XElement manifest, string artifact, string configuration)
        {
            if (manifest == null || manifest.Name != "compiledBuild" || (string)manifest.Attribute("schema") != "1")
            {
                throw new InvalidOperationException("Malformed compiled-build manifest");
            }

            foreach (string entry in new[] { "assets", "project", "artifact" })
            {
                if (manifest.Elements(entry).Count() != 1)
                {
                    throw new InvalidOperationException("Exactly one compiled manifest " + entry + " entry required");
                }
            }

            if ((string)manifest.Attribute("configuration") != configuration || (configuration != "Debug" && configuration != "Release"))
            {
                throw new InvalidOperationException("Manifest configuration does not match the loaded test build");
            }

            if (!new[] { "report_unavailable", "require_installed" }.Contains((string)manifest.Attribute("optionalPolicy")))
            {
                throw new InvalidOperationException("Unknown optional_checks policy");
            }

            ValidatedPath(manifest.Element("assets"));
            ValidatedPath(manifest.Element("project"));
            foreach (var reference in manifest.Elements("reference"))
            {
                ValidatedPath(reference);
            }

            GameReference(manifest);
            ValidatedPath(manifest.Element("artifact"), artifact);
            ValidateCanonicalCapture(manifest, configuration);
            return manifest;
        }

        private static void ValidateCanonicalCapture(XElement manifest, string configuration)
        {
            if (manifest.Elements("capture").Count() != 1 || captureDirectory == null)
            {
                throw new InvalidOperationException("Exactly one canonical compiler reference capture required");
            }
            string authorityPath = System.IO.Path.Combine(captureDirectory, "compiler-capture-" + configuration + ".xml");
            if (System.IO.Path.GetFullPath((string)manifest.Element("capture").Attribute("path")) != authorityPath)
            {
                throw new InvalidOperationException("Manifest must use the same-build canonical compiler reference capture");
            }
            ValidatedPath(manifest.Element("capture"), authorityPath);
            var authority = XElement.Load(authorityPath);
            if (authority.Name != "compilerCapture" || (string)authority.Attribute("configuration") != configuration)
            {
                throw new InvalidOperationException("Malformed canonical compiler reference capture");
            }
            foreach (string kind in new[] { "project", "assets", "artifact" })
            {
                var row = authority.Elements(kind).Single();
                ValidatedPath(row);
                if (!XNode.DeepEquals(row, manifest.Element(kind)))
                {
                    throw new InvalidOperationException("Canonical compiler reference " + kind + " differs from manifest");
                }
            }
            string project = ValidatedPath(authority.Element("project"));
            var package = XElement.Load(project).Descendants("PackageReference").Single(r => (string)r.Attribute("Include") == "Krafs.Rimworld.Ref");
            string pin = "Krafs.Rimworld.Ref/" + (string)package.Attribute("Version");
            // The capture emitter validates project.assets with its existing JSON
            // parser. Bind that assets-derived pin and its exact input hash to the
            // owning project's declared pin, independently of manifest labels.
            if ((string)authority.Attribute("rimworldPackage") != pin || (string)manifest.Attribute("rimworldPackage") != pin)
            {
                throw new InvalidOperationException("Game reference does not match pinned owning project/assets inputs");
            }
            string itemsPath = ValidatedPath(authority.Elements("items").Single());
            string identitiesPath = ValidatedPath(authority.Elements("identities").Single());
            if (itemsPath != System.IO.Path.Combine(captureDirectory, configuration + "-references.txt") ||
                identitiesPath != System.IO.Path.Combine(captureDirectory, configuration + "-reference-identities.txt"))
            {
                throw new InvalidOperationException("Canonical compiler reference files must belong to the same build");
            }
            var items = File.ReadAllLines(itemsPath);
            var identities = File.ReadAllLines(identitiesPath);
            var references = manifest.Elements("reference").ToArray();
            if (references.Select(r => (string)r.Attribute("path")).Distinct().Count() != references.Length)
            {
                throw new InvalidOperationException("Manifest contains duplicate compiler reference paths");
            }
            if (items.Length == 0 || items.Length != identities.Length || references.Length != items.Length)
            {
                throw new InvalidOperationException("Manifest differs from complete canonical compiler reference count");
            }
            var canonicalPaths = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < items.Length; i++)
            {
                string[] fields = items[i].Split('|');
                if (fields.Length != 4 || !canonicalPaths.Add(fields[0]) || string.IsNullOrEmpty(identities[i]))
                {
                    throw new InvalidOperationException("Malformed or duplicate canonical compiler reference item");
                }
                var row = references.SingleOrDefault(r => (string)r.Attribute("path") == fields[0]);
                string expectedPackage = string.IsNullOrEmpty(fields[2]) ? "" : fields[2] + "/" + fields[3];
                if (row == null || (string)row.Attribute("identity") != identities[i] ||
                    !string.Equals((string)row.Attribute("package"), expectedPackage, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Manifest differs from canonical compiler reference item: " + fields[0]);
                }
            }
        }

        private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        internal static string ValidatedPath(XElement row, string expectedCopy = null)
        {
            if (row == null)
            {
                throw new InvalidOperationException("Required compiled manifest entry missing");
            }

            string path = (string)row.Attribute("path");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                throw new InvalidOperationException("Declared compiled input/artifact missing: " + path);
            }

            using var sha = SHA256.Create();
            string Hash(string file) { using var input = File.OpenRead(file); return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant(); }
            string expected = (string)row.Attribute("sha256");
            if (expected == null || Hash(path) != expected || (expectedCopy != null && (!File.Exists(expectedCopy) || Hash(expectedCopy) != expected)))
            {
                throw new InvalidOperationException("Compiled input/artifact hash mismatch: " + path);
            }

            string identity = (string)row.Attribute("identity");
            if ((row.Name == "artifact" || row.Name == "reference") && string.IsNullOrEmpty(identity))
            {
                throw new InvalidOperationException("Compiled assembly identity missing: " + path);
            }

            if (identity != null && AssemblyName.GetAssemblyName(path).FullName != new AssemblyName(identity).FullName)
            {
                throw new InvalidOperationException("Compiled input/artifact assembly identity mismatch: " + path);
            }

            return path;
        }
    }
}
