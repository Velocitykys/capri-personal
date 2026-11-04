# Shared Capri Brain: Discord integration plan

## Goal

Discord must be another interface to the existing Capri, not a second assistant that imitates her. The desktop implementation remains the source of truth for personality, conversation behaviour, memory and model configuration.

## Current state

- `discord-bot/src/bot.js` is a standalone Node.js bot. It calls an OpenAI-compatible provider directly and keeps its own JSON conversation and memory store.
- The desktop application uses the C# `Brain` implementation. Its `Ask(string text)` path contains much more than a system prompt: recall, learning, memory, model routing, tool handling and desktop-specific behaviour.
- The desktop brain has process-wide static configuration and paths, including `Keeper`, `TranscriptPath`, `MemoriesPath`, `PersonalityPath`, `Wordiness` and `Recall`. Creating multiple `Brain` objects alone does not establish isolated users.
- The current Discord branch was based on the older remote `capri/enhancements-cleanup-v2` commit `29500ebbe5d10b92004449385025ac6f32309839`. The desktop working branch has additional local commits that are not yet present on GitHub. Do not implement the bridge against that stale C# snapshot.
- `discord-bot/personality.md` has been aligned to the desktop personality text supplied from the current local branch. Discord-specific privacy and capability boundaries remain appended by `systemPrompt()` in `bot.js`.

## Target architecture

1. **Capri Core (C#):** the one authoritative implementation of the desktop brain. Personality, model selection, response rules, recall and memory behaviour should be reused rather than reimplemented in JavaScript.
2. **Adapters:** desktop UI and Discord are separate ways to send messages to the core. They should not own competing personality prompts or independent reasoning pipelines.
3. **Per-person profiles:** each person gets a separate conversation history, transcript, long-term memory and identity/profile context. A Discord server channel has an explicit shared-channel context, separate from every member's private DM context.
4. **Capability policy:** Discord requests must not silently gain desktop control, screen access, local file access or other PC tools. Expose only capabilities deliberately enabled for that Discord context.
5. **Model/config parity:** Discord should use the same selected model and relevant settings as desktop Capri, rather than a separately hard-coded NVIDIA model and temperature.

## Required engineering work

### 1. Bring the real desktop source into the GitHub working branch

Before editing C# integration code, publish the user's local desktop commits to a separate source branch or otherwise provide the exact current source. Preserve the local uncommitted personality/safety stash and all untracked files. Never force-push, reset, clean, or overwrite the user's working tree.

### 2. Map the real request lifecycle

Inspect the current local versions of:
- `Brain.Ask` and its request/history construction;
- `Brain.Learn`, recall and transcript/memory folding;
- model/provider selection and generation settings;
- `Capri.cs` startup/configuration and the point where `Brain.Keeper` is assigned;
- tool routing and all desktop-only actions.

Trace exactly which static values are read during a request. Do not assume separate `Brain` instances are isolated.

### 3. Introduce an explicit conversation context

Refactor the core so each request carries an explicit profile/context object (identity, conversation history, transcript path, memory path, personality/config snapshot and allowed capabilities). Move user-specific state away from process-wide static fields where needed. Keep desktop behaviour unchanged through a compatibility adapter and regression tests.

### 4. Add a local bridge

Expose a small authenticated local IPC/HTTP interface from the desktop process or a dedicated Capri Core host. The Discord bot should submit a message plus a stable Discord identity/context and receive Capri's actual response. Do not expose the bridge to the public internet by default; use a local-only listener and a secret or OS-level access control.

### 5. Enforce privacy and capabilities

- DMs map to a private per-user profile.
- Server-channel context is explicitly shared and never receives private DM memories.
- A member's private profile is never exposed to other members.
- Desktop tools are denied by default for Discord until individually designed and authorised.
- Discord messages are untrusted input and cannot alter system instructions, reveal secrets or bypass capability checks.

### 6. Retire duplicate behaviour

Once the bridge is tested, remove the standalone Node provider call and duplicate personality/memory implementation from the Discord path. Keep Discord gateway, message routing and Discord-specific context handling in Node if useful.

## Acceptance tests

- The same prompt through desktop and Discord uses the same core personality and selected model.
- Short replies, slang frequency, jokes, emotional tone and recall follow the same core rules.
- Two Discord users cannot read or influence one another's private memories.
- Shared server messages do not leak into private DMs.
- Discord cannot trigger desktop actions or read local files unless an explicit, separately authorised capability is implemented.
- A desktop-only regression test confirms existing PC Capri behaviour is unchanged.
- Restart/reconnect behaviour preserves each profile's history and memory.

## Not implemented yet

This document is an implementation plan, not a claim that Discord already uses the C# brain. The current Discord bot remains a separate AI client until the current local C# source is safely available and the bridge is implemented and tested.
