# MCP Server for s&box

In-editor MCP server for s&box — drive the editor in natural language with Claude Code (or any MCP client). Editor open = MCP up, close = gone. No subprocess, no WebSocket bridge, no reconnect dance.

## What it can do (48 tools)

**Scene graph (12)** — `scene_list_objects`, `scene_get_object`, `scene_create_object`, `scene_delete_object`, `scene_clone_object`, `scene_reparent_object`, `scene_set_transform`, `scene_get_hierarchy`, `scene_load`, `scene_find_objects` (wildcards), `scene_find_by_component`, `scene_find_by_tag`

**Components (5)** — `component_list`, `component_get`, `component_set` (typed: Model, Material, Color, Vector3, Angles, cloud-asset idents), `component_add`, `component_remove`

**Tags (3)** — `tag_add`, `tag_remove`, `tag_list`

**Cloud assets (4)** — `asset_search` (s&box library), `asset_fetch`, `asset_mount` (auto-pins into `.sbproj` PackageReferences), `asset_browse_local`

**Editor (11)** — `editor_get_selection`, `editor_select_object`, `editor_undo`/`editor_redo`, `editor_save_scene`, `editor_take_screenshot` (CameraComponent → PNG), `editor_play`/`editor_stop`/`editor_is_playing`, `editor_scene_info`, `editor_console_output`

**Files & execution (7)** — `file_read`, `file_write` (`.cs` → `code/`, else `Assets/`), `file_list` (glob), `project_info`, `console_run`, `execute_csharp` (Roslyn scripting when available), `get_server_status`

**Docs & API search (6)** — built-in crawler over `docs.facepunch.com` (180+ pages) and the Facepunch API schema (1,800+ types):
- `sbox_search_docs` (fuzzy search, optional category filter)
- `sbox_get_doc_page` (Markdown, chunked output)
- `sbox_list_doc_categories`
- `sbox_search_api` (types + members)
- `sbox_get_api_type` (full type details — methods, properties, fields, XML docs)
- `sbox_cache_status`

## Features under the hood

- **HTTP/SSE transport** on `localhost:29015` — no external processes
- **Reflection-based tool discovery** via `[McpToolGroup]` + `[McpTool]` attributes — new tool = new method with an attribute
- **Main-thread dispatch** — all editor-API calls land safely on the editor's main thread (no Qt crash)
- **Dirty tracking** via `FullUndoSnapshot` + `OnEdited` + reflection setters — close-without-save prompt works
- **Process singleton** — survives hot-reloads, no bind-conflict loop
- **Dock UI** with live status, request counter, uptime, activity log
- **Graceful degradation** — Roslyn-scripting fallback, multi-stage schema-URL resolver, doc-crawl cold-start retry

## Setup in 30 seconds

1. Clone the repo
2. s&box: `File → Open Project` → `src/SboxMcp/.sbproj`
3. `claude mcp add --transport http -s user sbox http://localhost:29015/mcp`
4. Done — Claude sees all 48 tools

## Architecture

```
Claude / MCP client  ── HTTP :29015 ──>  s&box editor
                                          ├── McpHttpServer (HttpListener)
                                          ├── ToolRegistry (reflection)
                                          ├── HandlerDispatcher (main-thread)
                                          ├── Handlers/ (editor APIs)
                                          └── DocsService (crawler + fuzzy index)
```

One process. 48 tools. Direct access to every editor API. Ask Claude what you want.
