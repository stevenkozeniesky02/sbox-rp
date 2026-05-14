// Global using directives for the addon — without these, compiling the addon as
// a standalone project (vs piggybacked inside another project's compile unit)
// fails with hundreds of "name does not exist" / "type or namespace not found"
// errors because every BCL primitive and every s&box namespace would otherwise
// have to be `using`-imported in every single file.

global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Net;
global using System.Reflection;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.Threading;
global using System.Threading.Tasks;

global using Sandbox;
global using Editor;
