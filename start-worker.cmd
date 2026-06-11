@echo off
REM ============================================================================
REM  Advisory mutation WORKER — run this ONCE to make the dashboard "Mutate"
REM  button actually execute. It drains the queue and runs the /mutate cycle on
REM  YOUR machine using your Claude login (the container can't — it has no login).
REM
REM  Leave this window open. Click "Mutate" in the dashboard and watch the run
REM  go Queued -> Running -> Tests -> PR with a live progress bar.
REM
REM  Requirements (already true on this machine): git bash, claude CLI, gh CLI.
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
REM Use a NON-login shell (-c, not -lc) so a broken ~/.profile can't perturb the environment,
REM and pin HOME to USERPROFILE so the claude CLI always finds your login at ~/.claude.
set FORCE_RUN=true
"%BASH%" -c "export HOME='%USERPROFILE%'; export ADVISORY_API='%ADVISORY_API%'; export FORCE_RUN=true; ./scripts/mutate-claude.sh --loop 10s"

endlocal
