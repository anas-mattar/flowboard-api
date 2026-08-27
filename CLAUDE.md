# CLAUDE.md — flowboard-api

This repository is governed by the FlowBoard governance repo one level up
(`D:\solutions\flowboard`). Read there before changing anything here:

- `../CLAUDE.md` — always-loaded core (stack profile, workflow, strict rules)
- `../.specify/memory/constitution.md` — supersedes everything
- `../docs/rulebooks/backend-rules.md` — this repo's binding tier rules
- `../docs/rulebooks/database-rules.md` — schema/migration rules
- `../docs/rulebooks/backend/` — detailed rule packs (security, performance,
  database standards, examples, external-API rules, integration patterns)
- `../docs/domain/flowboard-invariants.md` — constitutional force
- `../docs/sdlc/gate-command.md` — the gate; the user certifies exit code 0

Gate for this repo (run from this directory): `dotnet build --warnaserror && dotnet test`

Specs live in `../specs/`. Implement only from an approved spec, one phase at a time.
