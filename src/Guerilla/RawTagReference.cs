namespace Guerilla
{
	public sealed class RawTagReference
	{
		public RawTagReference(string fieldName, string groupMagic, string path)
			: this(fieldName, groupMagic, path, 0, false)
		{
		}

		public RawTagReference(string fieldName, string groupMagic, string path, uint datumIndex, bool hasDatumIndex)
		{
			FieldName = fieldName;
			GroupMagic = groupMagic;
			Path = path;
			DatumIndex = datumIndex;
			HasDatumIndex = hasDatumIndex;
		}

		public string FieldName { get; private set; }
		public string GroupMagic { get; private set; }
		public string Path { get; private set; }
		public uint DatumIndex { get; private set; }
		public bool HasDatumIndex { get; private set; }
		public string ResolvedFile { get; set; }

		public void ResolveFromDependency(RawTagDependency dependency)
		{
			if (dependency == null)
				return;
			GroupMagic = dependency.GroupMagic;
			Path = dependency.Path;
			ResolvedFile = dependency.ResolvedFile;
		}
	}
}
