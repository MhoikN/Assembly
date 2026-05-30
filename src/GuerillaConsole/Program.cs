using System;
using System.IO;
using System.Linq;
using Guerilla;

namespace GuerillaConsole
{
	internal static class Program
	{
		private static void Main(string[] args)
		{
			var sourceRoot = FindSourceRoot();

			Console.WriteLine("Guerilla loose tag MVP");
			var target = SelectTargetGame(sourceRoot);
			if (target == null)
				return;

			Console.WriteLine("Tags directory:");
			var tagsRoot = Console.ReadLine();
			if (string.IsNullOrWhiteSpace(tagsRoot) || !Directory.Exists(tagsRoot))
			{
				Console.WriteLine("Directory was not found.");
				return;
			}

			var registry = TagGroupRegistry.Load(target.GroupNamesPath);
			var pluginLoader = new PluginDefinitionLoader(target.PluginDirectory);
			var deserializer = new RawTagDeserializer(tagsRoot, registry, pluginLoader, target.Profile);
			var scenarios = deserializer.FindScenarioFiles().OrderBy(p => p).ToArray();

			if (scenarios.Length == 0)
			{
				Console.WriteLine("No .scenario files were found.");
				return;
			}

			Console.WriteLine();
			Console.WriteLine("Scenarios:");
			for (var i = 0; i < scenarios.Length; i++)
				Console.WriteLine("{0}. {1}", i + 1, MakeRelative(tagsRoot, scenarios[i]));

			Console.WriteLine();
			Console.Write("Select scenario: ");
			var selectedText = Console.ReadLine();
			int selected;
			if (!int.TryParse(selectedText, out selected) || selected < 1 || selected > scenarios.Length)
			{
				Console.WriteLine("Invalid selection.");
				return;
			}

			Console.WriteLine();
			Console.WriteLine("Deserializing as {0}...", target.Name);
			var root = deserializer.LoadRecursive(scenarios[selected - 1], 8);
			PrintTag(root, 0);
			Console.WriteLine("Press any key to exit");
			Console.Read();
		}

		private static TargetGame SelectTargetGame(string sourceRoot)
		{
			var targets = new[]
			{
				new TargetGame("Halo 1", Path.Combine(sourceRoot, "Blamite", "Formats", "Halo1", "H1_GroupNames.xml"),
					Path.Combine(sourceRoot, "Assembly", "Plugins", "Halo1"), RawTagProfile.Halo1()),
				new TargetGame("Halo 2", Path.Combine(sourceRoot, "Blamite", "Formats", "Halo2Vista", "H2V_GroupNames.xml"),
					Path.Combine(sourceRoot, "Assembly", "Plugins", "Halo2"), RawTagProfile.Halo2()),
				new TargetGame("Halo 2 Xbox", Path.Combine(sourceRoot, "Blamite", "Formats", "Halo2Vista", "H2V_GroupNames.xml"),
					Path.Combine(sourceRoot, "Assembly", "Plugins", "Halo2Xbox"), RawTagProfile.Halo2()),
				new TargetGame("Halo 2 MCC", Path.Combine(sourceRoot, "Blamite", "Formats", "Halo2Vista", "H2V_GroupNames.xml"),
					Path.Combine(sourceRoot, "Assembly", "Plugins", "Halo2MCC"), RawTagProfile.Halo2())
			};

			Console.WriteLine("Target game:");
			for (var i = 0; i < targets.Length; i++)
				Console.WriteLine("{0}. {1}", i + 1, targets[i].Name);

			Console.Write("Select target game: ");
			var selectedText = Console.ReadLine();
			int selected;
			if (!int.TryParse(selectedText, out selected) || selected < 1 || selected > targets.Length)
			{
				Console.WriteLine("Invalid selection.");
				return null;
			}

			var target = targets[selected - 1];
			if (!File.Exists(target.GroupNamesPath))
			{
				Console.WriteLine("Group names file was not found: {0}", target.GroupNamesPath);
				return null;
			}
			if (!Directory.Exists(target.PluginDirectory))
			{
				Console.WriteLine("Plugin directory was not found: {0}", target.PluginDirectory);
				return null;
			}

			return target;
		}

		private static void PrintTag(RawTag tag, int depth)
		{
			var indent = new string(' ', depth * 2);
			Console.WriteLine("{0}- {1} [{2}/{3}] data=0x{4:X}", indent, tag.RelativePath, tag.GroupName, tag.GroupMagic, tag.DataOffset);

			foreach (var message in tag.Messages)
				Console.WriteLine("{0}  ! {1}", indent, message);

			Console.WriteLine("{0}  fields: {1}, refs: {2}, deps: {3}, children: {4}", indent, CountFields(tag), tag.References.Count, tag.Dependencies.Count, tag.Children.Count);
			PrintUnresolvedBlocks(tag, indent);

			foreach (var dependency in tag.Dependencies)
			{
				var status = dependency.ResolvedFile == null ? "missing" : "loaded";
				Console.WriteLine("{0}  dep {1}: {2}:{3} ({4})", indent, dependency.Source, dependency.GroupMagic, dependency.Path, status);
			}
			
			foreach (var reference in tag.References)
			{
				if (reference.HasDatumIndex)
				{
					if (string.IsNullOrEmpty(reference.Path))
						Console.WriteLine("{0}  ref {1}: {2}:datum=0x{3:X8}", indent, reference.FieldName, reference.GroupMagic, reference.DatumIndex);
					else
						Console.WriteLine("{0}  ref {1}: {2}:{3} datum=0x{4:X8}", indent, reference.FieldName, reference.GroupMagic, reference.Path, reference.DatumIndex);
				}
				else
				{
					var status = reference.ResolvedFile == null ? "missing" : "loaded";
					Console.WriteLine("{0}  ref {1}: {2}:{3} ({4})", indent, reference.FieldName, reference.GroupMagic, reference.Path, status);
				}
			}

			foreach (var child in tag.Children)
				PrintTag(child, depth + 1);
		}

		private static int CountFields(RawTag tag)
		{
			return tag.Fields.Sum(CountField);
		}

		private static void PrintUnresolvedBlocks(RawTag tag, string indent)
		{
			var unresolved = tag.Fields.SelectMany(EnumerateFields)
				.Where(f => f.Kind == "tagblock" && f.Value != null && f.Value.ToString().StartsWith("unresolved:", StringComparison.Ordinal))
				.Take(20)
				.ToArray();

			foreach (var field in unresolved)
				Console.WriteLine("{0}  block {1}: {2}", indent, field.Name, field.Value);

			var total = tag.Fields.SelectMany(EnumerateFields)
				.Count(f => f.Kind == "tagblock" && f.Value != null && f.Value.ToString().StartsWith("unresolved:", StringComparison.Ordinal));
			if (total > unresolved.Length)
				Console.WriteLine("{0}  ... {1} more unresolved blocks", indent, total - unresolved.Length);
		}

		private static System.Collections.Generic.IEnumerable<RawTagField> EnumerateFields(RawTagField field)
		{
			yield return field;
			foreach (var child in field.Children.SelectMany(EnumerateFields))
				yield return child;
		}

		private static int CountField(RawTagField field)
		{
			return 1 + field.Children.Sum(CountField);
		}

		private static string FindSourceRoot()
		{
			var directory = AppDomain.CurrentDomain.BaseDirectory;
			while (!string.IsNullOrEmpty(directory))
			{
				if (Directory.Exists(Path.Combine(directory, "Blamite")) && Directory.Exists(Path.Combine(directory, "Assembly")))
					return directory;

				var parent = Directory.GetParent(directory);
				directory = parent == null ? null : parent.FullName;
			}

			return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
		}

		private static string MakeRelative(string root, string path)
		{
			var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			var fullPath = Path.GetFullPath(path);
			return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
				? fullPath.Substring(fullRoot.Length)
				: fullPath;
		}

		private sealed class TargetGame
		{
			public TargetGame(string name, string groupNamesPath, string pluginDirectory, RawTagProfile profile)
			{
				Name = name;
				GroupNamesPath = groupNamesPath;
				PluginDirectory = pluginDirectory;
				Profile = profile;
			}

			public string Name { get; private set; }
			public string GroupNamesPath { get; private set; }
			public string PluginDirectory { get; private set; }
			public RawTagProfile Profile { get; private set; }
		}
	}
}
