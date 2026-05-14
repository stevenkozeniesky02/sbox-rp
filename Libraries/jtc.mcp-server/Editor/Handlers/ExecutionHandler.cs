using Sandbox;
using System.Reflection;
using SboxMcp;

namespace SboxMcp.Handlers;

/// <summary>
/// Handles code execution and console commands: execute.csharp, console.run.
/// </summary>
public static class ExecutionHandler
{
	private static readonly Lazy<ScriptRunner> _runner = new( ScriptRunner.TryCreate );

	/// <summary>
	/// execute.csharp — Evaluate a C# expression or statement block in the editor context.
	///
	/// Uses Microsoft.CodeAnalysis.CSharp.Scripting (loaded via reflection from the
	/// s&amp;box AppDomain) when available. Returns a clear error if the scripting
	/// assembly isn't loaded — instead of a misleading silent stub like before.
	///
	/// Params: { "code": "C# expression or statements", "imports": "optional, comma-separated namespaces" }
	/// </summary>
	public static async Task<object> ExecuteCSharp( HandlerRequest request )
	{
		var code = GetParam( request, "code" );
		var imports = GetOptionalParam( request, "imports" );

		Log.Info( $"[MCP] execute.csharp: {code.Length} chars" );

		var runner = _runner.Value;
		if ( runner is null )
		{
			return new
			{
				executed = false,
				result   = "",
				error    = "Roslyn scripting (Microsoft.CodeAnalysis.CSharp.Scripting) is not loaded in this s&box build.",
				note     = "Use console.run / file.write + hot-reload to add code permanently.",
			};
		}

		try
		{
			var result = await runner.EvaluateAsync( code, imports );
			return new
			{
				executed = true,
				result   = result is null ? "(null)" : result.ToString(),
				type     = result is null ? "void" : result.GetType().FullName,
			};
		}
		catch ( Exception ex )
		{
			// Roslyn wraps compile errors in CompilationErrorException — surface its message verbatim.
			Log.Warning( $"[MCP] execute.csharp failed: {ex.Message}" );
			return new
			{
				executed = false,
				result   = "",
				error    = ex.Message,
				stack    = ex.StackTrace ?? "",
			};
		}
	}

	/// <summary>
	/// console.run — Execute a console command.
	/// Params: { "command": "convar value" }
	/// </summary>
	public static Task<object> RunConsoleCommand( HandlerRequest request )
	{
		var command = GetParam( request, "command" );

		Log.Info( $"[MCP] console.run: {command}" );

		try
		{
			Sandbox.ConsoleSystem.Run( command );

			return Task.FromResult<object>( (object)new
			{
				executed = true,
				command,
				note = "Command dispatched to ConsoleSystem.",
			} );
		}
		catch ( Exception ex )
		{
			throw new InvalidOperationException( $"Console command failed: {ex.Message}", ex );
		}
	}

	// -------------------------------------------------------------------------
	// Roslyn-via-reflection runner
	// -------------------------------------------------------------------------

	/// <summary>
	/// Reflective adapter to <c>Microsoft.CodeAnalysis.CSharp.Scripting.CSharpScript</c>.
	/// We can't take a hard reference because s&box doesn't expose the package as a
	/// project-level dependency, but the assembly is loaded into the editor's
	/// AppDomain when the engine compiler is initialised.
	/// </summary>
	private sealed class ScriptRunner
	{
		private readonly MethodInfo _evaluateAsync;
		private readonly Type _scriptOptionsType;
		private readonly object _scriptOptions;

		private ScriptRunner( MethodInfo evaluateAsync, Type scriptOptionsType, object scriptOptions )
		{
			_evaluateAsync = evaluateAsync;
			_scriptOptionsType = scriptOptionsType;
			_scriptOptions = scriptOptions;
		}

		public static ScriptRunner TryCreate()
		{
			try
			{
				var scriptingAsm = AppDomain.CurrentDomain.GetAssemblies()
					.FirstOrDefault( a => a.GetName().Name == "Microsoft.CodeAnalysis.CSharp.Scripting" );

				if ( scriptingAsm is null )
				{
					try { scriptingAsm = Assembly.Load( "Microsoft.CodeAnalysis.CSharp.Scripting" ); }
					catch { return null; }
				}

				var scriptType = scriptingAsm == null ? null : scriptingAsm.GetType( "Microsoft.CodeAnalysis.CSharp.Scripting.CSharpScript" );
				if ( scriptType is null ) return null;

				// Pick: Task<object> EvaluateAsync(string, ScriptOptions, object, Type, CancellationToken)
				var eval = scriptType.GetMethods( BindingFlags.Public | BindingFlags.Static )
					.Where( m => m.Name == "EvaluateAsync" && !m.IsGenericMethodDefinition )
					.FirstOrDefault( m =>
					{
						var p = m.GetParameters();
						return p.Length >= 1 && p[0].ParameterType == typeof( string );
					} );
				if ( eval is null ) return null;

				var optionsType = eval.GetParameters().Length > 1 ? eval.GetParameters()[1].ParameterType : null;
				if ( optionsType is null ) return null;

				// ScriptOptions.Default
				var defaultProp = optionsType.GetProperty( "Default", BindingFlags.Public | BindingFlags.Static );
				var defaultOpts = defaultProp is null ? null : defaultProp.GetValue( null );
				if ( defaultOpts is null ) return null;

				// .WithReferences( ... ) and .WithImports( ... ) accept loaded assemblies + namespace strings
				var refs = AppDomain.CurrentDomain.GetAssemblies()
					.Where( a => !a.IsDynamic && !string.IsNullOrEmpty( a.Location ) )
					.ToArray();

				var withRefs = optionsType.GetMethod( "WithReferences", new[] { typeof( IEnumerable<Assembly> ) } );
				if ( withRefs is not null )
				{
					var maybe = withRefs.Invoke( defaultOpts, new object[] { refs } );
					if ( maybe is not null ) defaultOpts = maybe;
				}

				var withImports = optionsType.GetMethod( "WithImports", new[] { typeof( IEnumerable<string> ) } );
				if ( withImports is not null )
				{
					var defaultImports = new[]
					{
						"System", "System.Linq", "System.Collections.Generic",
						"Sandbox", "Editor",
					};
					var maybe = withImports.Invoke( defaultOpts, new object[] { defaultImports } );
					if ( maybe is not null ) defaultOpts = maybe;
				}

				return new ScriptRunner( eval, optionsType, defaultOpts );
			}
			catch ( Exception ex )
			{
				Log.Warning( $"[MCP] Could not initialise Roslyn scripting: {ex.Message}" );
				return null;
			}
		}

		public async Task<object> EvaluateAsync( string code, string extraImports )
		{
			var opts = _scriptOptions;
			if ( !string.IsNullOrWhiteSpace( extraImports ) )
			{
				var addImports = _scriptOptionsType.GetMethod( "AddImports", new[] { typeof( IEnumerable<string> ) } );
				if ( addImports is not null )
				{
					var ns = extraImports.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
					var maybe = addImports.Invoke( opts, new object[] { ns } );
					if ( maybe is not null ) opts = maybe;
				}
			}

			// Build EvaluateAsync arguments. The full signature is
			//   EvaluateAsync(string code, ScriptOptions options, object globals, Type globalsType, CancellationToken token)
			var paramInfo = _evaluateAsync.GetParameters();
			var args = new object[paramInfo.Length];
			args[0] = code;
			if ( paramInfo.Length > 1 ) args[1] = opts;
			for ( var i = 2; i < paramInfo.Length; i++ )
			{
				args[i] = paramInfo[i].HasDefaultValue ? paramInfo[i].DefaultValue : null;
			}

			var task = (Task)_evaluateAsync.Invoke( null, args );
			await task.ConfigureAwait( false );
			var resultProp = task.GetType().GetProperty( "Result" );
			return resultProp is null ? null : resultProp.GetValue( task );
		}
	}

	// -------------------------------------------------------------------------
	// Helpers
	// -------------------------------------------------------------------------

	private static string GetParam( HandlerRequest request, string key )
	{
		if ( request.Params is JsonElement el && el.TryGetProperty( key, out var prop ) )
		{
			var val = prop.GetString();
			if ( val is not null ) return val;
		}
		throw new ArgumentException( $"Missing required parameter: {key}" );
	}

	private static string GetOptionalParam( HandlerRequest request, string key )
	{
		if ( request.Params is JsonElement el && el.TryGetProperty( key, out var prop ) )
		{
			return prop.GetString();
		}
		return null;
	}
}
