using Sandbox;
using System;
using System.Reflection;

namespace SboxMcp.Mcp;

/// <summary>
/// Marks a static method as an MCP tool. The method's name (override-able via
/// <see cref="Name"/>) becomes the MCP tool name; its parameters become the
/// JSON Schema input.
///
/// Parameters can be decorated with <see cref="ParamDescriptionAttribute"/>
/// (or System.ComponentModel.DescriptionAttribute is also honoured) to
/// provide schema descriptions.
///
/// The return value can be <c>string</c>, <c>object</c>, or <c>Task&lt;...&gt;</c>;
/// the registry serialises it into the <c>content</c> array of a ToolCallResult.
/// </summary>
[AttributeUsage( AttributeTargets.Method )]
public sealed class McpToolAttribute : Attribute
{
	public string Name { get; }
	public string Description { get; set; }

	public McpToolAttribute( string name )
	{
		Name = name;
	}
}

/// <summary>
/// Describes a parameter (used in JSON Schema generation). Optional —
/// System.ComponentModel.DescriptionAttribute also works.
/// </summary>
[AttributeUsage( AttributeTargets.Parameter )]
public sealed class ParamDescriptionAttribute : Attribute
{
	public string Description { get; }
	public ParamDescriptionAttribute( string description ) { Description = description; }
}

/// <summary>
/// Marker for classes that contain <see cref="McpToolAttribute"/>-decorated
/// methods. Lets us short-circuit reflection scans to a known set of types.
/// </summary>
[AttributeUsage( AttributeTargets.Class )]
public sealed class McpToolGroupAttribute : Attribute
{
}
