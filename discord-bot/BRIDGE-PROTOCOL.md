# Shared brain bridge

The Discord adapter will call a loopback-only authenticated endpoint hosted by Capri Desktop. The desktop brain remains authoritative for replies. Each request carries a Discord user ID and a conversation scope. Direct-message memory is private to that user; server channel context is scoped to its channel. Desktop-control tools are disabled for Discord requests.

The bridge must reject unauthenticated requests, non-loopback binding, oversized payloads, malformed IDs, and requests outside the supported Discord operations. It must never expose desktop files, screen contents, or computer-control tools to Discord.
