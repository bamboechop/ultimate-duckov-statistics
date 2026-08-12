# Repository agent instructions

## GitHub authentication on Windows

- GitHub CLI authentication for this checkout is stored in the Windows credential manager. A `gh auth status` command executed inside the Codex sandbox can incorrectly report that the stored token is invalid because the sandbox cannot access the keyring.
- Do not ask the user to reauthenticate, open a browser, or log in to GitHub based only on a sandboxed authentication failure.
- Prefer the connected GitHub tools for supported GitHub reads and operations.
- When the `gh` CLI is genuinely required, rerun the exact command with the necessary approval outside the sandbox so it can access the Windows keyring.
- Ask the user to reauthenticate only if `gh auth status` also fails outside the sandbox.
- Never work around the sandbox by using `--insecure-storage`, copying the token into repository files, or persisting it in an environment variable.
