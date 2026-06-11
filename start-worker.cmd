@echo off
REM ============================================================================
REM  Advisory mutation WORKER — run this ONCE to make the dashboard "Mutate"
REM  button actually execute. It drains the queue and runs the /mutate cycle on
REM  YOUR machine, headless.
REM
REM  Leave this window open. Click "Mutate" in the dashboard and watch the run
REM  go Queued -> Running -> Tests -> PR with a live progress bar.
REM
REM  ONE-TIME AUTH SETUP (required — a background worker can't use the interactive
REM  login; that needs a TTY and races other Claude sessions -> "Not logged in"):
REM    1) In a normal terminal run:  claude setup-token
REM       (prints a 1-year token; requires a Claude subscription)
REM    2) Add it to the repo's .env (gitignored):
REM          CLAUDE_CODE_OAUTH_TOKEN=<the token>
REM       (or instead:  ANTHROPIC_API_KEY=sk-ant-...)
REM  The worker sources .env automatically. This is the SAID-ECHO pattern.
REM
REM  Requirements: git bash, claude CLI, gh CLI, and a key in .env (above).
REM ============================================================================
setlocal
cd /d "%~dp0"

REM Where the API lives (the dashboard's backend). Override if you changed ports.
if "%ADVISORY_API%"=="" set ADVISORY_API=http://localhost:5000/api

REM Find git bash.
set BASH=bash
where %BASH% >nul 2>nul || set BASH=C:\Program Files\Git\bin\bash.exe

echo Starting Advisory mutation worker...
echo   API     : %ADVISORY_API%
echo   Queue   : data\evolution-queue (in your dev root, shared with the API container)
echo   Cadence : every 10s (heartbeat + drain) — clicked tickets start almost immediately
echo.
echo Leave this window open. Press Ctrl+C to stop the worker.
echo.

REM FORCE_RUN bypasses the hour-gate so a clicked ticket runs immediately.
REM IMPORTANT: do NOT set HOME to a Windows path. The cycle's `claude -p` runs inside WSL, where
REM HOME='C:\Users\...' is a RELATIVE path — npm/claude then write their caches INTO the repo as a
REM literal "C:\Users\Carter" tree, which the cycle commits (huge junk PRs). Let each shell use its
REM native HOME; auth comes from CLAUDE_CODE_OAUTH_TOKEN in .env (sourced by the worker), not ~/.claude.
set FORCE_RUN=true
"%BASH%" -c "export ADVISORY_API='%ADVISORY_API%'; export FORCE_RUN=true; ./scripts/mutate-claude.sh --loop 10s"

endlocal
