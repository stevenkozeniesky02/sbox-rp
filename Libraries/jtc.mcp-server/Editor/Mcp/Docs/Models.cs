using Sandbox;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SboxMcp.Mcp.Docs;

public sealed class CachedPage
{
	public string Url { get; set; } = "";
	public string Title { get; set; } = "";
	public string Category { get; set; } = "";
	public string Markdown { get; set; } = "";
	public long FetchedAt { get; set; }
	public string LastUpdated { get; set; }
}

public sealed class ApiDocumentation
{
	public string Summary { get; set; }
	public string Remarks { get; set; }
	public string Return { get; set; }
	public Dictionary<string, string> Params { get; set; }
	public Dictionary<string, string> Exceptions { get; set; }
	public Dictionary<string, string> TypeParams { get; set; }
	public List<string> SeeAlso { get; set; }
	public List<string> Examples { get; set; }
}

public sealed class ApiParameter
{
	public string Name { get; set; } = "";
	public bool Out { get; set; }
	public bool In { get; set; }
	public string Type { get; set; }
}

public sealed class ApiMethod
{
	public string FullName { get; set; } = "";
	public string Name { get; set; } = "";
	public bool IsPublic { get; set; }
	public bool IsProtected { get; set; }
	public bool IsStatic { get; set; }
	public bool IsExtension { get; set; }
	public string ReturnType { get; set; }
	public bool IsVirtual { get; set; }
	public bool IsOverride { get; set; }
	public List<ApiParameter> Parameters { get; set; }
	public ApiDocumentation Documentation { get; set; }
}

public sealed class ApiProperty
{
	public string FullName { get; set; } = "";
	public string Name { get; set; } = "";
	public bool IsPublic { get; set; }
	public bool IsProtected { get; set; }
	public bool IsStatic { get; set; }
	public string PropertyType { get; set; }
	public ApiDocumentation Documentation { get; set; }
}

public sealed class ApiField
{
	public string FullName { get; set; } = "";
	public string Name { get; set; } = "";
	public bool IsPublic { get; set; }
	public bool IsProtected { get; set; }
	public bool IsStatic { get; set; }
	public string FieldType { get; set; }
	public ApiDocumentation Documentation { get; set; }
}

public sealed class ApiType
{
	public string FullName { get; set; } = "";
	public string Name { get; set; } = "";
	public string Namespace { get; set; }
	public string BaseType { get; set; }
	public bool IsPublic { get; set; }
	public bool IsProtected { get; set; }
	public bool IsStatic { get; set; }
	public bool IsClass { get; set; }
	public bool IsInterface { get; set; }
	public bool IsAbstract { get; set; }
	public bool IsSealed { get; set; }
	public bool IsAttribute { get; set; }
	public List<ApiMethod> Methods { get; set; }
	public List<ApiMethod> Constructors { get; set; }
	public List<ApiProperty> Properties { get; set; }
	public List<ApiField> Fields { get; set; }
	public ApiDocumentation Documentation { get; set; }
}

public sealed class ApiSchemaWrapper
{
	public List<ApiType> Types { get; set; }
}

internal static class JsonOpts
{
	public static readonly System.Text.Json.JsonSerializerOptions Default = new()
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = null,
		WriteIndented = false,
	};
}
