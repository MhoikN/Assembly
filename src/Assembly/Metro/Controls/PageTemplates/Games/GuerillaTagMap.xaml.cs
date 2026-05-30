using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Guerilla;
using Xceed.Wpf.AvalonDock.Layout;

namespace Assembly.Metro.Controls.PageTemplates.Games
{
	public sealed class GuerillaSourceTagGroup
	{
		public GuerillaSourceTagGroup(string groupMagic, string description)
		{
			GroupMagic = string.IsNullOrEmpty(groupMagic) ? "????" : groupMagic;
			Description = string.IsNullOrEmpty(description) ? "unknown" : description;
			Children = new ObservableCollection<GuerillaSourceTagEntry>();
		}

		public string GroupMagic { get; set; }
		public string Description { get; set; }
		public ObservableCollection<GuerillaSourceTagEntry> Children { get; set; }

		public GuerillaSourceTagGroup CloneWithoutChildren()
		{
			return new GuerillaSourceTagGroup(GroupMagic, Description);
		}
	}

	public sealed class GuerillaSourceTagEntry
	{
		public GuerillaSourceTagEntry(RawTag tag)
		{
			Tag = tag;
			GroupMagic = string.IsNullOrEmpty(tag.GroupMagic) ? "????" : tag.GroupMagic;
			TagFileName = tag.RelativePath;
			TagToolTip = string.Format("{0}\r\nFields: {1}\r\nDependencies: {2}\r\nFile: {3}",
				tag.RelativePath, tag.Fields.Count, tag.Dependencies.Count, tag.FilePath);
		}

		public RawTag Tag { get; set; }
		public string GroupMagic { get; set; }
		public string TagFileName { get; set; }
		public string TagToolTip { get; set; }

		public bool Matches(string query)
		{
			return TagFileName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
				GroupMagic.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
		}
	}

	public partial class GuerillaTagMap : UserControl
	{
		private const int MaxRecursiveDepth = 8;
		private readonly string _scenarioPath;
		private readonly LayoutDocument _tab;
		private readonly List<GuerillaSourceTagGroup> _allTagGroups;
		private GuerillaSourceTagEntry _selectedTag;

		public GuerillaTagMap(string scenarioPath, LayoutDocument tab)
		{
			InitializeComponent();

			_scenarioPath = scenarioPath;
			_tab = tab;
			HeaderItems = new ObservableCollection<HeaderItem>();
			VisibleTags = new ObservableCollection<GuerillaSourceTagGroup>();
			_allTagGroups = new List<GuerillaSourceTagGroup>();
			DataContext = this;

			_tab.Title = Path.GetFileName(scenarioPath);
			lblSelectedDetails.Text = scenarioPath;
			LoadScenario();
		}

		public ObservableCollection<HeaderItem> HeaderItems { get; private set; }
		public ObservableCollection<GuerillaSourceTagGroup> VisibleTags { get; private set; }

		private void LoadScenario()
		{
			doingAction.Visibility = Visibility.Visible;

			var worker = new BackgroundWorker();
			worker.DoWork += (sender, args) => args.Result = LoadScenarioDetails();
			worker.RunWorkerCompleted += (sender, args) =>
			{
				doingAction.Visibility = Visibility.Collapsed;
				if (args.Error != null)
				{
					ShowLoadError(args.Error);
					return;
				}

				var result = (LoadedTagProject)args.Result;
				ApplyLoadedProject(result);
			};
			worker.RunWorkerAsync();
		}

		private LoadedTagProject LoadScenarioDetails()
		{
			var tagsRoot = FindTagsRoot(_scenarioPath);
			var sourceRoot = FindSourceRoot();
			var groupNamesPath = Path.Combine(sourceRoot, "Blamite", "Formats", "Halo2Vista", "H2V_GroupNames.xml");
			var pluginPath = Path.Combine(sourceRoot, "Assembly", "Plugins", "Halo2");

			var groups = TagGroupRegistry.Load(groupNamesPath);
			var plugins = new PluginDefinitionLoader(pluginPath);
			var deserializer = new RawTagDeserializer(tagsRoot, groups, plugins, RawTagProfile.Halo2());
			var root = deserializer.LoadRecursive(_scenarioPath, MaxRecursiveDepth);

			return new LoadedTagProject(tagsRoot, groupNamesPath, pluginPath, root);
		}

		private void ApplyLoadedProject(LoadedTagProject result)
		{
			HeaderItems.Clear();
			AddHeader("Target Game", "Halo 2");
			AddHeader("Scenario", result.Root.RelativePath);
			AddHeader("Tags Root", result.TagsRoot);
			AddHeader("Group", result.Root.GroupMagic + " / " + result.Root.GroupName);
			AddHeader("Data Offset", "0x" + result.Root.DataOffset.ToString("X"));
			AddHeader("Fields", result.Root.Fields.Count.ToString());
			AddHeader("Dependencies", result.Root.Dependencies.Count.ToString());
			AddHeader("Loaded Children", CountTagTree(result.Root).ToString());
			AddHeader("Plugins", result.PluginPath);

			foreach (var message in result.Root.Messages)
				AddHeader("Message", message);

			_allTagGroups.Clear();
			_allTagGroups.AddRange(BuildGroupedTags(result.Root));
			RefreshVisibleTags();

			treeTags.ItemsSource = VisibleTags;
			SelectTag(FindFirstTag(VisibleTags));
			_tab.Title = Path.GetFileName(_scenarioPath);
		}

		private void ShowLoadError(Exception error)
		{
			HeaderItems.Clear();
			VisibleTags.Clear();
			_allTagGroups.Clear();
			AddHeader("Load Failed", error.Message);
			lblSelectedTag.Text = "Unable to load tag";
			lblSelectedDetails.Text = error.ToString();
			panelSelectedFields.ItemsSource = null;
		}

		private void AddHeader(string title, string data)
		{
			HeaderItems.Add(new HeaderItem(title, data));
		}

		private static IEnumerable<GuerillaSourceTagGroup> BuildGroupedTags(RawTag root)
		{
			var tags = FlattenTags(root)
				.GroupBy(t => t.FilePath, StringComparer.OrdinalIgnoreCase)
				.Select(g => g.First())
				.OrderBy(t => t.GroupMagic)
				.ThenBy(t => t.RelativePath)
				.ToList();

			foreach (var group in tags.GroupBy(t => t.GroupMagic ?? "", StringComparer.OrdinalIgnoreCase)
				.OrderBy(g => g.Key))
			{
				var first = group.First();
				var sourceGroup = new GuerillaSourceTagGroup(first.GroupMagic, first.GroupName);
				foreach (var tag in group)
					sourceGroup.Children.Add(new GuerillaSourceTagEntry(tag));
				yield return sourceGroup;
			}
		}

		private static IEnumerable<RawTag> FlattenTags(RawTag tag)
		{
			var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			return FlattenTags(tag, visited);
		}

		private static IEnumerable<RawTag> FlattenTags(RawTag tag, ISet<string> visited)
		{
			if (tag == null || !visited.Add(tag.FilePath))
				yield break;

			yield return tag;
			foreach (var child in tag.Children)
				foreach (var flattened in FlattenTags(child, visited))
					yield return flattened;
		}

		private static int CountTagTree(RawTag tag)
		{
			if (tag == null)
				return 0;
			return 1 + tag.Children.Sum(CountTagTree);
		}

		private void RefreshVisibleTags()
		{
			VisibleTags.Clear();
			var query = txtSearch.Text == null ? "" : txtSearch.Text.Trim();

			foreach (var group in _allTagGroups)
			{
				var filtered = FilterGroup(group, query);
				if (filtered != null)
					VisibleTags.Add(filtered);
			}
		}

		private static GuerillaSourceTagGroup FilterGroup(GuerillaSourceTagGroup group, string query)
		{
			if (group == null)
				return null;
			if (string.IsNullOrEmpty(query))
				return group;

			var groupMatches = group.GroupMagic.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
				group.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
			var clone = group.CloneWithoutChildren();
			foreach (var child in group.Children.Where(c => groupMatches || c.Matches(query)))
				clone.Children.Add(child);

			return clone.Children.Count > 0 ? clone : null;
		}

		private void SelectTag(GuerillaSourceTagEntry item)
		{
			if (item == null || item.Tag == null)
			{
				_selectedTag = null;
				lblSelectedTag.Text = "Raw tag source";
				lblSelectedDetails.Text = _scenarioPath;
				panelSelectedFields.ItemsSource = null;
				lblPropertyStatus.Text = "";
				return;
			}

			_selectedTag = item;
			lblSelectedTag.Text = item.GroupMagic + ":" + item.TagFileName;
			lblSelectedDetails.Text = item.Tag.FilePath;
			panelSelectedFields.ItemsSource = FlattenFields(item.Tag.Fields).Take(500).ToList();
			lblPropertyStatus.Text = "";
		}

		private void SelectGroup(GuerillaSourceTagGroup group)
		{
			if (group == null)
				return;

			lblSelectedTag.Text = group.GroupMagic + " - " + group.Description;
			lblSelectedDetails.Text = group.Children.Count + " loaded source tags";
			panelSelectedFields.ItemsSource = null;
			lblPropertyStatus.Text = "";
		}

		private static GuerillaSourceTagEntry FindFirstTag(IEnumerable<GuerillaSourceTagGroup> groups)
		{
			return groups == null ? null : groups.SelectMany(g => g.Children).FirstOrDefault();
		}

		private static IEnumerable<FieldItem> FlattenFields(IEnumerable<RawTagField> fields)
		{
			foreach (var field in fields)
			{
				yield return new FieldItem(field);
				foreach (var child in FlattenFields(field.Children))
					yield return child;
			}
		}

		private void treeTags_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
		{
			var tag = e.NewValue as GuerillaSourceTagEntry;
			if (tag != null)
			{
				SelectTag(tag);
				return;
			}

			SelectGroup(e.NewValue as GuerillaSourceTagGroup);
		}

		private void treeTags_ItemDoubleClick(object sender, MouseButtonEventArgs e)
		{
			var item = treeTags.SelectedItem as GuerillaSourceTagEntry;
			if (item != null)
				SelectTag(item);
		}

		private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (VisibleTags == null)
				return;
			RefreshVisibleTags();
		}

		private void btnSaveTagProperties_Click(object sender, RoutedEventArgs e)
		{
			if (_selectedTag == null || _selectedTag.Tag == null)
				return;

			var fields = panelSelectedFields.ItemsSource as IEnumerable<FieldItem>;
			if (fields == null)
				return;

			var changed = fields.Where(f => f.IsEditable && f.IsDirty).ToList();
			if (changed.Count == 0)
			{
				lblPropertyStatus.Text = "No changes.";
				return;
			}

			try
			{
				using (var stream = new FileStream(_selectedTag.Tag.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
				using (var writer = new BinaryWriter(stream))
				{
					foreach (var field in changed)
						WriteFieldValue(writer, stream.Length, field);
				}

				foreach (var field in changed)
					field.Commit();

				lblPropertyStatus.Text = "Saved " + changed.Count + " change(s).";
			}
			catch (Exception ex)
			{
				lblPropertyStatus.Text = "Save failed: " + ex.Message;
			}
		}

		private void btnReloadTagProperties_Click(object sender, RoutedEventArgs e)
		{
			if (_selectedTag == null || _selectedTag.Tag == null)
				return;

			try
			{
				var tagsRoot = FindTagsRoot(_selectedTag.Tag.FilePath);
				var sourceRoot = FindSourceRoot();
				var groups = TagGroupRegistry.Load(Path.Combine(sourceRoot, "Blamite", "Formats", "Halo2Vista", "H2V_GroupNames.xml"));
				var plugins = new PluginDefinitionLoader(Path.Combine(sourceRoot, "Assembly", "Plugins", "Halo2"));
				var deserializer = new RawTagDeserializer(tagsRoot, groups, plugins, RawTagProfile.Halo2());
				var reloaded = deserializer.LoadSingle(_selectedTag.Tag.FilePath);
				_selectedTag.Tag = reloaded;
				SelectTag(_selectedTag);
				lblPropertyStatus.Text = "Reloaded.";
			}
			catch (Exception ex)
			{
				lblPropertyStatus.Text = "Reload failed: " + ex.Message;
			}
		}

		private static void WriteFieldValue(BinaryWriter writer, long fileLength, FieldItem field)
		{
			if (field.Offset < 0 || field.Offset >= fileLength)
				throw new InvalidOperationException(field.Name + " is outside the file.");

			writer.BaseStream.Position = field.Offset;
			switch (field.Kind)
			{
				case "uint8":
				case "byte":
					writer.Write(ParseByte(field.EditedValue, field.Name));
					return;
				case "int8":
					writer.Write(ParseSByte(field.EditedValue, field.Name));
					return;
				case "uint16":
				case "flags16":
				case "enum16":
					writer.Write(ParseUInt16(field.EditedValue, field.Name));
					return;
				case "int16":
					writer.Write(ParseInt16(field.EditedValue, field.Name));
					return;
				case "uint32":
				case "flags32":
				case "enum32":
				case "datum":
				case "oldstringid":
					writer.Write(ParseUInt32(field.EditedValue, field.Name));
					return;
				case "int32":
				case "undefined":
					writer.Write(ParseInt32(field.EditedValue, field.Name));
					return;
				case "float32":
				case "float":
				case "degree":
					writer.Write(ParseSingle(field.EditedValue, field.Name));
					return;
				case "ascii":
					WriteFixedString(writer, field.EditedValue, field.Size, Encoding.ASCII, field.Name);
					return;
				case "utf16":
					WriteFixedString(writer, field.EditedValue, field.Size, Encoding.Unicode, field.Name);
					return;
				default:
					throw new InvalidOperationException(field.Kind + " fields are read-only.");
			}
		}

		private static byte ParseByte(string value, string name)
		{
			return byte.Parse(NormalizeNumber(value), NumberStyles.Integer, CultureInfo.InvariantCulture);
		}

		private static sbyte ParseSByte(string value, string name)
		{
			return sbyte.Parse(NormalizeNumber(value), NumberStyles.Integer, CultureInfo.InvariantCulture);
		}

		private static ushort ParseUInt16(string value, string name)
		{
			return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
				? Convert.ToUInt16(value.Substring(2), 16)
				: ushort.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
		}

		private static short ParseInt16(string value, string name)
		{
			return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
				? unchecked((short)Convert.ToUInt16(value.Substring(2), 16))
				: short.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
		}

		private static uint ParseUInt32(string value, string name)
		{
			return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
				? Convert.ToUInt32(value.Substring(2), 16)
				: uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
		}

		private static int ParseInt32(string value, string name)
		{
			return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
				? unchecked((int)Convert.ToUInt32(value.Substring(2), 16))
				: int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
		}

		private static float ParseSingle(string value, string name)
		{
			return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
		}

		private static string NormalizeNumber(string value)
		{
			if (value != null && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
				return Convert.ToUInt32(value.Substring(2), 16).ToString(CultureInfo.InvariantCulture);
			return value;
		}

		private static void WriteFixedString(BinaryWriter writer, string value, int size, Encoding encoding, string name)
		{
			if (size <= 0)
				throw new InvalidOperationException(name + " does not have a fixed size.");

			var bytes = encoding.GetBytes(value ?? "");
			if (bytes.Length >= size)
				throw new InvalidOperationException(name + " is too long for its fixed field size.");

			var buffer = new byte[size];
			Array.Copy(bytes, buffer, bytes.Length);
			writer.Write(buffer);
		}

		private static string FindTagsRoot(string selectedTagPath)
		{
			var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(selectedTagPath)));
			while (directory != null)
			{
				if (string.Equals(directory.Name, "tags", StringComparison.OrdinalIgnoreCase))
					return directory.FullName;
				directory = directory.Parent;
			}

			return Path.GetDirectoryName(Path.GetFullPath(selectedTagPath));
		}

		private static string FindSourceRoot()
		{
			var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
			while (directory != null)
			{
				var groupNamesPath = Path.Combine(directory.FullName, "Blamite", "Formats", "Halo2Vista", "H2V_GroupNames.xml");
				var pluginPath = Path.Combine(directory.FullName, "Assembly", "Plugins", "Halo2");
				if (File.Exists(groupNamesPath) && Directory.Exists(pluginPath))
					return directory.FullName;

				directory = directory.Parent;
			}

			throw new DirectoryNotFoundException("Could not find the Assembly source root for Halo 2 group names and plugins.");
		}

		public sealed class HeaderItem
		{
			public HeaderItem(string title, string data)
			{
				Title = title;
				Data = data;
			}

			public string Title { get; private set; }
			public string Data { get; private set; }
		}

		public sealed class FieldItem
		{
			public FieldItem(RawTagField field)
			{
				Field = field;
				Name = field.Name;
				Kind = field.Kind;
				Value = field.Value == null ? "" : field.Value.ToString();
				EditedValue = Value;
				Offset = field.Offset;
				Size = field.Size;
				IsEditable = IsSupportedEditableKind(Kind) && field.Children.Count == 0;
			}

			public RawTagField Field { get; private set; }
			public string Name { get; set; }
			public string Kind { get; set; }
			public string Value { get; set; }
			public string EditedValue { get; set; }
			public long Offset { get; set; }
			public int Size { get; set; }
			public bool IsEditable { get; set; }
			public bool IsDirty
			{
				get { return !string.Equals(Value, EditedValue, StringComparison.Ordinal); }
			}

			public void Commit()
			{
				Value = EditedValue;
				Field.Value = EditedValue;
			}

			private static bool IsSupportedEditableKind(string kind)
			{
				switch (kind)
				{
					case "uint8":
					case "byte":
					case "int8":
					case "uint16":
					case "flags16":
					case "enum16":
					case "int16":
					case "uint32":
					case "flags32":
					case "enum32":
					case "datum":
					case "oldstringid":
					case "int32":
					case "undefined":
					case "float32":
					case "float":
					case "degree":
					case "ascii":
					case "utf16":
						return true;
					default:
						return false;
				}
			}
		}

		private sealed class LoadedTagProject
		{
			public LoadedTagProject(string tagsRoot, string groupNamesPath, string pluginPath, RawTag root)
			{
				TagsRoot = tagsRoot;
				GroupNamesPath = groupNamesPath;
				PluginPath = pluginPath;
				Root = root;
			}

			public string TagsRoot { get; private set; }
			public string GroupNamesPath { get; private set; }
			public string PluginPath { get; private set; }
			public RawTag Root { get; private set; }
		}
	}
}
