# Handoff — cross-machine context

Working notes for Claude sessions across multiple machines. Read this first if you're a Claude session opening this repo for the first time.

## Project context

This is a private fork of [sousou63/DarkRP](https://github.com/sousou63/DarkRP), being developed by Steven Kozeniesky as a personal s&box RP gamemode.

The owner originally had a [Mauve RP](https://sbox.game/mauve/mauve_rp) server hosted on Physgun and wanted to mod it. After determining MauveRP is closed-source proprietary code (Aventured LLC), we chose the legitimate path: fork an open-source DarkRP port (sousou63's), customize it, publish under owner's own org, and swap it on the server.

The **roadmap** is at issue [#1](https://github.com/stevenkozeniesky02/sbox-rp/issues/1) — that's the canonical task list.

## Machines and roles

| Machine | OS | Role |
|---|---|---|
| Mac | macOS | Code edits, git, planning, asset inspection. No s&box editor. |
| PC | Linux | s&box editor (via Proton/Steam), build, publish, local testing. |
| Physgun server | Debian (container) | Production server. Pulls published packages from sbox.game. |

Git is the bridge between Mac and PC. Don't try to share files any other way.

## Workflow

1. Code/asset changes happen on Mac or Linux (whichever you're at).
2. `git push` from the working machine.
3. `git pull` on the Linux PC.
4. Build and test in s&box editor (Proton).
5. Publish package to sbox.game as `obsidianrp/rp`.
6. In Physgun panel → S&box Packages → apply `obsidianrp.rp`.
7. Restart server, test.

## Current state

- Forked from sousou63/DarkRP at commit `b90cdc9`.
- Rebrand commit `d01b718`: `sandbox.sbproj` Org/Ident/Title updated.
- Final gamemode name is **deferred** — placeholder `Title: "RP (working title)"`.
- **Published** to sbox.game on 2026-05-13 as `obsidianrp/rp` (org: `obsidianrp`), visibility Unlisted.
- **Live on Physgun** (TestRP server) as of 2026-05-13. Phase 1 baseline complete.
- Phase 3 chat commands (/roll, /ticket, /report) implemented locally (commit `be4430d`) but not yet republished.

## Direction (decided in initial planning)

- **Tone:** Chaotic DarkRP-style, fun-first.
- **Theme:** Modern city, stick with `rp_downtown` for now.
- **Priorities:** New jobs, custom commands, economy tweaks, map customization.

## Important context

- `AGENTS.md` (already in repo) has sousou63's coding guidelines — server-authoritative, modular, validate client input. Follow them.
- The original Mauve RP source/manifest files are kept at `~/sbox_mauverp/source/` on the **Mac only** for architecture reference. Never copy MauveRP code into this fork — they assert copyright. Study the patterns, write our own implementations.
- The `Job` system is data-driven: a new job = a new `.jobdef` file under `Assets/jobs/`. No code change required unless the job has custom behavior. See `Code/Jobs/JobDefinition.cs` for properties.
- Player class uses partial class pattern (`Player.Admin.cs`, `Player.Jobs.cs`, `Player.ConsoleCommands.cs`, etc.). New player-facing features should add a new partial file.

## Starter prompt for a fresh Claude session on the Linux PC

```
I'm continuing work on an s&box RP gamemode fork. The repo is cloned at <path>.
Read HANDOFF.md and the roadmap issue #1 on this repo first.
I'm on Linux, the s&box editor runs via Proton.
We're working on <pick a phase from the roadmap> right now.
```
