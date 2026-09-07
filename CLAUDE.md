# Claude project instructions

## Important first notice: limited internet data allowance

The developer is currently working with a limited internet data allowance.
Use external network access only when it is strictly necessary for the current
task and local repository files, installed dependencies, caches, and existing
documentation are insufficient. Keep the number of requests, transferred data,
and downloads to the absolute minimum. Ask for permission before any external
access that is not clearly essential.

## Project instructions

Before planning, discussing, or changing this project, read `AGENTS.md` in
full and follow all instructions and document-reading requirements defined
there. Treat `AGENTS.md` as the canonical source for project workflow, scope,
architecture, validation, and Git rules.

## Validation from the sandbox: the check watcher

This session has no .NET and cannot run `./scripts/check.sh` itself. A watcher
the developer keeps running in a terminal tab runs it instead, so do not ask
for a manual run — request one:

```sh
./scripts/check-agent-run.sh            # the default path
./scripts/check-agent-run.sh --tests    # additionally the test projects
./scripts/check-agent-run.sh --poll     # keep waiting for a long run
./scripts/check-agent-run.sh --full     # the whole output instead of a tail
```

Exit codes: 0 the run succeeded, 1 it failed and `check.sh`'s own code is in
the `exit=` line of the header, 4 it is still running — call again with
`--poll` rather than requesting a second run. 2 means `--poll` without an open
request of this session, and 5 that the request was dropped; request again
there instead of waiting.

Exit code 3 always needs the developer: say plainly that the watcher is off,
ask them to type `checkw start` in a terminal tab, and wait for their
confirmation instead of falling back to "please run it by hand".

Beyond exit code 3, stop and ask the developer whenever the obstacle is not
yours to remove: the same request fails or is dropped three times in a row, a
run keeps polling far past its usual duration, or the failure names something
about the machine rather than the code — a missing toolchain, a full disk, a
binary that is gone. Say what you tried, what you saw, and what you need.
Changing code in response to a broken environment is worse than waiting.

The watcher answers through `.agent-check/`, which is git-ignored. You do not
read it: `check-agent-run.sh` prints your run's header — id, exit, args,
duration, lines — and its output, `--full` instead of the tail. The `result`
and `last.log` at the top of that directory are a copy of whichever run
finished last, whoever asked for it; they are the developer's view, and with
several sessions sharing one watcher they may belong to someone else.

The one place worth a look is `manual-result` with its `manual.log`. Those are
written by the developer's own shell function when he runs the check by hand,
not by the watcher, and they appear only once he has done so. Their header
carries the same fields plus `head` and `dirty`, so you can tell which commit
the run belongs to before trusting it instead of requesting one yourself.

`scripts/check-agent.sh` and `scripts/check-agent-run.sh` come unchanged from
world01 and carry no project knowledge — they ask Git for the root and call
`./scripts/check.sh`. Leave them exactly as they are; they are meant to stay
identical across repositories.

None of this changes what has to be verified: the rules in `AGENTS.md` apply
unchanged, and the watcher is only how a run is requested from here.

## Git from the sandbox: no optional locks

The sandbox cannot delete files, so a plain `git status` in this session leaves
a stale `.git/index.lock` behind that blocks the developer's next Git command.
Always use `git --no-optional-locks ...` for read-only Git commands from here
(`status`, `diff --stat`, `grep`); it refreshes nothing and leaves no lock.

## Deleting files from the sandbox

This session cannot delete files. When a change requires removing a file, move
it into `_to_delete/` at the repository root instead. Do this silently: do not
ask first, and do not report it afterwards. The developer empties that folder
when the day's work is done.

Commit the change as usual, but never stage `_to_delete/` itself. It stays
untracked on purpose, so that the folder appears in the developer's `git
status` at the end of the day while nothing of it reaches the history.

Never read, search, or reference anything below `_to_delete/`. Its contents are
discarded work; treat the folder as if it were empty.

The empty `.gdignore` in that folder keeps the Godot editor from importing what
is parked there. Leave it in place.
