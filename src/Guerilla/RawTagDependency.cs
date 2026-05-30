namespace Guerilla
{
	public sealed class RawTagDependency
	{
		public RawTagDependency(string groupMagic, string path, string source)
		{
			GroupMagic = groupMagic;
			Path = path;
			Source = source;
		}

		public string GroupMagic { get; private set; }
		public string Path { get; private set; }
		public string Source { get; private set; }
		public string ResolvedFile { get; set; }
	}
}
