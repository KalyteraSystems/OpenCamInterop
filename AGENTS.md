# OpenCamInterop: instructions for agents

OpenCamInterop is a public, Apache-2.0 library and EventLab CLI that turns sanitized Frigate, Scrypted and ONVIF
event quirks into deterministic, offline compatibility tests. Read [README.md](README.md) and
[CONTRIBUTING.md](CONTRIBUTING.md) first; they are the authority for scope, setup, fixtures and pull requests.
These instructions only add what agents need on top of them.

## Boundaries

- The project opens no network connections, discovers no devices, stores no credentials and processes no images
  or video. Keep it offline and keep transports caller-owned.
- This repository is public. Never commit a raw device export, credential, address, serial number, person, face,
  plate, media file, installation URL, local path, site or host name. Fixtures are synthetic or irreversibly
  reduced, and their manifest note says what was removed.
- Open an issue before changing a public contract or the manifest version. Keep one pull request focused on one
  behavior.
- Documentation, identifiers and code are in English.

## Build and validation

Run the commands in [CONTRIBUTING.md](CONTRIBUTING.md#local-setup) (locked restore, Release build, tests,
`dotnet format --verify-no-changes`, vulnerable-package audit) and the corpus `verify` command for fixture
changes. Record what ran in the pull request; a hosted check that did not start is "not run", never passed.

## Branches, merges and worktrees

- Claude Code works on `claude/<topic>` and Codex on `codex/<topic>`; both prefixes are valid for topic branches
  and pull requests. Never push to another agent's branch or to `dependabot/*`, and never force-push.
- Work in your own worktree, push your branch before you stop and hand off in a pull request; the owner merges.
- Pull requests merge with a merge commit (owner decision 2026-10-01). Hand over
  `gh pr merge <number> -R KalyteraSystems/OpenCamInterop --merge --match-head-commit <head-sha>`; never
  `--squash` or `--rebase`.
- An agent removes only its own worktrees, and only once their work is complete (merged, or closed by the owner)
  (owner decision 2026-10-01). Never remove, clean or reset another agent's worktree; ask the owner about any
  other removal.
- Commit with your real identity and a `Co-Authored-By` trailer. Use `Fixes #<n>` only when merging the pull
  request fully resolves that issue.
