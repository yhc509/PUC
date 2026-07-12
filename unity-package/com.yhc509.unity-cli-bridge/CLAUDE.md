# CLAUDE.md

This file provides package-local guidance when working inside `unity-package/com.yhc509.unity-cli-bridge`.

## Key Conventions

- **AI Agent Skill install scope:** Default to project-scoped installs under `<UnityProjectRoot>/.claude/skills/` or `<UnityProjectRoot>/.codex/skills/`; global installs remain available but can shadow project copies.
