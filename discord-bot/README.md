# Capri Discord Bot (early version)

A separate Discord gateway service that reuses Capri's character prompt without changing the Windows desktop build.

## Requirements

- Node.js 20.11 or newer
- A Discord application with a bot user
- Message Content Intent enabled in the Discord Developer Portal
- An API key for an OpenAI-compatible chat-completions provider

## Setup on Windows

1. Open PowerShell in this folder.
2. Install dependencies:

   ```powershell
   npm install
   ```

3. Copy the environment template:

   ```powershell
   Copy-Item .env.example .env
   notepad .env
   ```

4. Put your Discord bot token in `CAPRI_BOT_TOKEN`.
5. Put your provider API key in `CAPRI_API_KEY`. Keep both secrets in `.env`; never commit or send them to anyone.
6. Start Capri:

   ```powershell
   npm start
   ```

## Provider settings

The defaults target NVIDIA NIM. You can change:

- `CAPRI_API_BASE_URL` — OpenAI-compatible API base URL
- `CAPRI_API_KEY` — provider key
- `CAPRI_MODEL` — model ID

The Discord bot uses its own provider connection. It does not automatically inherit the API key or selected model from the desktop app.

## How conversations are separated

- **DMs:** each Discord user gets a separate conversation history and private memory profile, keyed by their immutable Discord user ID.
- **Private memory commands:** in a DM, use `!capri remember <note>` to save a private note. Use `!capri forget` to clear your private notes and DM history.
- **Shared server:** Capri responds when mentioned, replied to, addressed by name at the start of a message, or when the same person sends a follow-up inside the short continuity window. Server history is shared per channel.
- **Privacy boundary:** private DM memories are never loaded into a server conversation. Server messages do not get copied into a member's private DM history.

## Current limitations

- This first version does not execute desktop/PC commands, see screens, or access desktop files.
- Memory is stored in `data/memory.json` on the machine running the bot. Back up this folder. For hosting, use a persistent disk; ephemeral server filesystems will lose memory on restart/redeploy.
- The bot must stay running to receive Discord messages. A Windows PC can host it for initial testing; later it can be moved to a persistent Node host.
- This service uses the same character instructions but a separate API session from the desktop app. A later integration can route requests through Capri's actual C# brain and desktop tools.

## Safety and secrets

Do not commit `.env` or `data/`. The bot ignores other bots and only replies in servers when addressed. Never paste the bot token or provider key into Discord or GitHub.
