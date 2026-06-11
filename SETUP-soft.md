# Setup Guide

This repo contains several independent sub-projects across different stacks:

| # | Project | Stack | Role |
|---|---|---|---|
| 1 | **WongaLoans** | C# / .NET 9 / SQL Server | Modular-monolith banking core |
| 2 | **DT Storefront** | Node.js / React / Vite | Frontend storefront prototype |
| 3 | **SQL-Tools — Atlatl** | C# / .NET 8 / Avalonia | Cross-platform SQL workbench |
| 4 | **ML & AI** | Python 3.10 / PyTorch | Global Machine Learning + AI / RAG environment |
| 5 | **Vivere Web App** | Next.js 16 / React 19 / TypeScript | Digital-wallet & card-management web app (+ a dedicated Python IT allow-list) |

WongaLoans, DT Storefront, SQL-Tools, and Vivere each keep their own separate
stack and setup steps below. The **ML & AI** environment (#4) is a single global
Python install — every Python script and service in the repo imports from it. It
is documented in the [Python — Global Machine Learning & AI Environment](#python--global-machine-learning--ai-environment)
section. The **Vivere Web App** (#5) is a Next.js/TypeScript frontend; its
Node/TS stack **and** a comprehensive, future-proof **Python IT allow-list**
(everything Python that Vivere's backend-for-frontend, tooling, reporting, and
knowledge-base work may ever need) are documented in the
[Vivere Web App](#vivere-web-app-nextjs--digital-wallet--card-management) section
at the end.

---

## WongaLoans (.NET 9 — banking core)

### Prerequisites

| Tool | Version | Notes |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | **9.0** | `dotnet --version` must show `9.x.x` |
| [SQL Server](https://www.microsoft.com/en-us/sql-server/) | 2019 + | Developer or Express edition for local dev |
| [Podman Desktop](https://podman-desktop.io/) | latest | required for local Podman Compose targets |
| [sqlpackage](https://learn.microsoft.com/en-us/sql/tools/sqlpackage/) | latest | deploys `.dacpac` schema bundles |

### Getting Started

```bash
cd WongaLoans
dotnet restore
dotnet build
```

Deploy the SQL schema (from the bundle):

```bash
sqlpackage /Action:Publish /SourceFile:Audit.Sql.dacpac /TargetConnectionString:"..."
```

---

### NuGet Packages — Central Version Management

All package versions are pinned in `WongaLoans/Directory.Packages.props`
(`ManagePackageVersionsCentrally=true`). Individual `.csproj` files reference
packages **without** a `Version` attribute; the props file is the single source
of truth.

---

### Runtime NuGet Packages

#### Entity Framework Core & SQL Server

| Package | Version | Layer(s) |
|---|---|---|
| `Microsoft.EntityFrameworkCore` | 9.0.0 | `Common.Infrastructure`, `Audit.Infrastructure`, all product Infrastructures |
| `Microsoft.EntityFrameworkCore.Relational` | 9.0.0 | `Common.Infrastructure`, `Audit.Infrastructure`, all product Infrastructures |
| `Microsoft.EntityFrameworkCore.SqlServer` | 9.0.0 | `Audit.Infrastructure`, `ProjectStartup`, `Jobs.Worker`, all product Infrastructures |
| `Microsoft.Data.SqlClient` | 5.2.2 | `ProjectStartup` (direct ADO.NET; also a transitive dep of EF SqlServer) |

#### Microsoft.Extensions (ASP.NET Core / hosting)

| Package | Version | Layer(s) |
|---|---|---|
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 9.0.0 | `Common.Domain`, `Common.Application`, `Common.Infrastructure`, `Audit.Domain`, `Audit.Application` |
| `Microsoft.Extensions.Configuration` | 9.0.0 | `Common.Application`, `Jobs.Worker` |
| `Microsoft.Extensions.Logging.Abstractions` | 9.0.0 | transitive via EF / Hosting |
| `Microsoft.Extensions.Logging.Console` | 9.0.0 | `Jobs.Worker` (console exe — SQL Agent CmdExec) |
| `Microsoft.Extensions.Hosting` | 9.0.0 | `Jobs.Worker` host builder |
| `Microsoft.AspNetCore.OpenApi` | 9.0.0 | `ProjectStartup` (OpenAPI/Swagger metadata) |
| `Swashbuckle.AspNetCore` | 7.2.0 | `ProjectStartup` (Swagger UI) |

#### Dependency Injection & Validation

| Package | Version | Layer(s) | Purpose |
|---|---|---|---|
| `Scrutor` | 5.0.1 | `Common.Domain`, `Common.Application` | Assembly-scan DI registration — auto-discovers `IEventHandler`, `IAuditSource`, repositories, factories |
| `FluentValidation` | 11.10.0 | `Audit.Application` | Domain / application-layer input validation |
| `FluentValidation.AspNetCore` | 11.3.0 | `Common.Web` | ASP.NET Core model validation integration |
| `FluentValidation.DependencyInjectionExtensions` | 11.10.0 | `Common.Web` | Registers validators from DI container |

#### Data Lake / Export

| Package | Version | Layer(s) | Purpose |
|---|---|---|---|
| `Parquet.Net` | 5.0.2 | `Audit.Infrastructure` | `ParquetPeriodExporter` — columnar Bronze snapshots for the data lake (`IPeriodExporter` swap-in) |

---

### Audit-Specific Package Map (per project)

| Project | Direct NuGet packages |
|---|---|
| `Audit.Domain` | `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `Audit.Application` | `Microsoft.Extensions.DependencyInjection.Abstractions`, `FluentValidation` |
| `Audit.Infrastructure` | `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`, `Microsoft.EntityFrameworkCore.SqlServer`, `Parquet.Net` |
| `Audit.Web` | *(no direct packages — pulls from `Common.Web` + `Audit.Application` via project refs)* |
| `Common.Domain` | `Scrutor`, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `Common.Application` | `Scrutor`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Configuration` |
| `Common.Infrastructure` | `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `Common.Web` | `FluentValidation.AspNetCore`, `FluentValidation.DependencyInjectionExtensions` |
| `ProjectStartup` | `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.Data.SqlClient`, `Swashbuckle.AspNetCore` |
| `Jobs.Worker` | `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Logging.Console` |

---

### Dev / Tooling NuGet Packages

| Package | Version | Layer(s) | Purpose |
|---|---|---|---|
| `Microsoft.EntityFrameworkCore.Design` | 9.0.0 | *(tooling only)* | EF Core CLI tooling (`dotnet ef`) — not deployed |
| `Microsoft.EntityFrameworkCore.InMemory` | 9.0.0 | `Audit.Tests` | In-memory DB provider for unit / integration tests |
| `Microsoft.NET.Test.Sdk` | 17.11.1 | `Audit.Tests` | MSBuild test runner infrastructure |
| `xunit` | 2.9.2 | `Audit.Tests` | Test framework |
| `xunit.runner.visualstudio` | 2.8.2 | `Audit.Tests` | VS / `dotnet test` adapter |

---

### Available Commands

| Command | Description |
|---|---|
| `dotnet restore` | Restore all NuGet packages |
| `dotnet build` | Build all projects |
| `dotnet test` | Run all tests (`Audit.Tests`, `Amortization.Tests`) |
| `dotnet run --project src/ProjectStartup` | Start the full composition-root API host (serves the web UI + Swagger) |
| `dotnet run --project src/global/Scheduling/targets/Local/src/Jobs.Worker -- audit-seal-period` | Run a worker verb (SQL Agent equivalent) |

---

### Web Interface — Loan-Servicing Client Dashboard

WongaLoans ships a **front-end web interface**: a live client-demo dashboard
(*"Direct Transact — Loan Servicing"*) that drives a loan through its full
lifecycle (originate → disburse → accrue → collect → pay → settle → close),
with account search, a timeframe simulator, ledger / files / settlement /
database-evidence tabs, and per-tester copy-on-write simulations.

It is **deliberately a zero-build, single-file front end** — there is **no
Node.js, no npm/pnpm, no bundler, and no JS framework**. It is plain
HTML + CSS + vanilla JavaScript, served as a static file by the .NET host and
talking to a same-host JSON API. This keeps the banking core's only toolchain
.NET + SQL Server.

#### Front-end assets

| Asset | Path | Role |
|---|---|---|
| `demo.html` | `src/ProjectStartup/wwwroot/demo.html` | The entire UI — markup, CSS, and JS in one file (no build step) |
| `dt-logo.png` | `src/ProjectStartup/wwwroot/dt-logo.png` | Direct Transact brand logo / favicon |

#### How it is served & wired (ASP.NET Core, no extra packages)

| Concern | Mechanism | Source |
|---|---|---|
| Static hosting | `app.UseDefaultFiles()` + `app.UseStaticFiles()` (built into `Microsoft.NET.Sdk.Web` — **no NuGet package**) | `ProjectStartup/Program.cs` |
| Friendly route | `GET /demo` → 302 redirect to `/demo.html` | `ProjectStartup/Program.cs` |
| Backend API | `DemoController` exposes `demo/api/*` (real application services for writes; raw ADO reads of `loa_Loan`, `led_LedgerEntry`, `amz_Schedule_Line`) | `ProjectStartup/Demo/DemoController.cs` |
| API explorer UI | **Swagger UI** at `/swagger` via `Swashbuckle.AspNetCore` (already listed under runtime packages) | `ProjectStartup/Program.cs` |

#### Front-end runtime requirements

| Requirement | Detail |
|---|---|
| Browser | Any modern evergreen browser (Chrome / Edge / Firefox / Safari). Uses `fetch`, `localStorage`, Clipboard API, CSS Grid, `<input type="range">` |
| Host URL | Served by the `ProjectStartup` host — open **`http://localhost:5080/demo`** (the in-page error hint references port `:5080`) |
| JavaScript | Required (the page is a client-rendered SPA-style dashboard); no transpilation — ships modern ES used directly |
| Network font (external) | **Google Fonts — `Inter`** loaded from `https://fonts.googleapis.com`. This is the **only third-party front-end dependency**; offline it falls back to `system-ui, sans-serif` |
| Build tooling | **None** — no `package.json`, no `npm install`, no bundler. Editing `demo.html` is the entire front-end workflow |

#### Opening the interface

```bash
dotnet run --project src/ProjectStartup
# then browse to:
#   http://localhost:5080/demo       → the loan-servicing dashboard
#   http://localhost:5080/swagger    → the REST API explorer
```

---

### Web Interface — React / Next.js rebuild (Vivere-aligned stack)

> **Forward-looking / optional.** The shipping UI is the zero-build `demo.html`
> above. This subsection documents what the **same** loan-servicing dashboard
> would require **if rebuilt as a React + Next.js app** on the **same stack as
> the Vivere Web App** (Next.js 16 / React 19 / TypeScript, pnpm, React Query +
> Zustand, Sentry, Docker + Azure Pipelines). The .NET host stays exactly as-is
> and simply becomes the **REST/JSON backend** the Next.js app calls (its
> OpenAPI/Swagger document drives fully-typed client generation).

A suggested project would live at `WongaLoans/src/ProjectStartup/web/`
(or a sibling `wonga-web/` repo) and be served either standalone (`pnpm dev`)
or built to static/SSR output and reverse-proxied by the .NET host.

#### Prerequisites (Node / TypeScript — matches Vivere)

| Tool | Version | Notes |
|---|---|---|
| [Node.js](https://nodejs.org/) | **≥ 24.0.0** | Enforced via `engines`; manage with `nvm`/`fnm` |
| [pnpm](https://pnpm.io/) | **10.x** (`pnpm@10.24.0`) | Pinned via `packageManager`; primary package manager |
| [Docker](https://www.docker.com/) | latest | Parity with Vivere's `docker/{development,staging,production}` |
| [Azure CLI](https://learn.microsoft.com/cli/azure/) | latest | Optional — Azure Pipelines / deployment |

#### Getting Started (app)

```bash
cd src/ProjectStartup/web
pnpm install
pnpm generate-types     # regenerate TS types from the .NET host's OpenAPI/Swagger doc
pnpm dev                # http://localhost:3000  (calls the .NET host at :5080)
```

#### Core framework & language

| Package | Version | Purpose |
|---|---|---|
| `next` | 16.x | React framework (App Router, SSR/RSC, routing, API proxying) |
| `react` | 19.x | Core UI framework |
| `react-dom` | 19.x | DOM renderer |
| `typescript` | ≥ 5.5 | Static typing (matches Vivere's TS-first codebase) |
| `@types/react` / `@types/react-dom` | 19.x | React type definitions |
| `@types/node` | ≥ 22 | Node type definitions |

#### Data fetching, state & typed API client

The .NET host already emits an **OpenAPI document** (Swashbuckle, `/swagger`),
so the front end binds to it with generated types instead of hand-written DTOs.

| Package | Version | Purpose |
|---|---|---|
| `@tanstack/react-query` | ≥ 5.0 | Server-state: caching, polling the lifecycle/ledger, mutations (run step, advance day) |
| `zustand` | ≥ 5.0 | Client UI state (selected account, active tester, open section, tab) — replaces the `let` globals in `demo.html` |
| `openapi-fetch` | ≥ 0.13 | Tiny typed fetch client bound to the `demo/api/*` OpenAPI schema |
| `openapi-typescript` | ≥ 7.0 | `generate-types` — turns the host's Swagger JSON into TS types (dev dependency) |

#### UI components, styling & icons (Radix + Tailwind, like DT Storefront)

| Package | Version | Purpose |
|---|---|---|
| `tailwindcss` | 4.1.x | Utility-first CSS (ports the hand-written CSS in `demo.html`) |
| `@tailwindcss/postcss` | 4.1.x | Tailwind v4 PostCSS plugin |
| `tailwind-merge` | 3.x | Merge/condition Tailwind class names |
| `class-variance-authority` | 0.7.x | Component variant styling (status pills, badges) |
| `clsx` | 2.x | Conditional class names |
| `lucide-react` | ≥ 0.487 | Icon set (replaces the inline `<svg>` icons) |
| `@radix-ui/react-dialog` | ≥ 1.1 | New-loan modal + command-palette container |
| `@radix-ui/react-tabs` | ≥ 1.1 | Ledger / Files / Settlement / Database-checks tabs |
| `@radix-ui/react-select` | ≥ 2.1 | Tester + "Do next" dropdowns |
| `@radix-ui/react-tooltip` | ≥ 1.1 | Field hints / copy buttons |
| `@radix-ui/react-slider` | ≥ 1.2 | Timeframe day slider |
| `@radix-ui/react-scroll-area` | ≥ 1.2 | Account rail / results scrolling |
| `cmdk` | ≥ 1.1 | ⌘K command-palette account finder |
| `sonner` | ≥ 2.0 | Toast notifications (replaces `alert()` / inline status) |
| `recharts` | ≥ 2.15 | Charts for the live-position / interest-over-time view |
| `date-fns` | ≥ 3.6 | Date math for the simulated-clock timeframe (`dateAt`, `loanDay`) |
| `next-themes` | ≥ 0.4 | Light/dark theming (optional) |

#### Forms & validation

| Package | Version | Purpose |
|---|---|---|
| `react-hook-form` | ≥ 7.55 | New-loan form (principal / rate / term) |
| `zod` | ≥ 3.23 | Schema validation; pairs with generated OpenAPI types |
| `@hookform/resolvers` | ≥ 3.9 | Wire Zod schemas into react-hook-form |

#### Observability (matches Vivere's Sentry usage)

| Package | Version | Purpose |
|---|---|---|
| `@sentry/nextjs` | ≥ 8.0 | Error + performance monitoring (parity with Vivere) |

#### Dev / tooling & testing

| Package | Version | Purpose |
|---|---|---|
| `eslint` | ≥ 9 | Linting |
| `eslint-config-next` | 16.x | Next.js ESLint rules |
| `prettier` | ≥ 3.3 | Formatting (`pnpm format`) |
| `jest` | ≥ 29 | Unit/integration tests (parity with Vivere) |
| `@testing-library/react` | ≥ 16 | Component testing |
| `@testing-library/jest-dom` | ≥ 6 | DOM matchers |
| `@playwright/test` | ≥ 1.44 | End-to-end lifecycle walkthrough tests |
| `postcss` / `autoprefixer` | latest | CSS build pipeline for Tailwind |

#### Suggested scripts (mirrors Vivere's `package.json`)

| Command | Description |
|---|---|
| `pnpm dev` | Next.js dev server (`http://localhost:3000`) |
| `pnpm build` / `pnpm build:{dev,stg,prd}` | Production build per environment |
| `pnpm start` | Serve the production build |
| `pnpm generate-types` | Regenerate TS types from the .NET host's OpenAPI doc |
| `pnpm lint` / `pnpm typecheck` / `pnpm format` | Quality gates |
| `pnpm test` / `test:coverage` / `test:ci` | Jest test suites |
| `pnpm docker-start:{dev,stg,prd}` | Run via the Docker compose files |

> **What changes vs. the vanilla build:** the JS globals + `fetch` calls in
> `demo.html` become React Query hooks against a typed `openapi-fetch` client;
> the inline CSS becomes Tailwind; the hand-rolled command palette, modal, tabs,
> and slider become `cmdk` + Radix primitives; and `localStorage` "recent work"
> moves into a `zustand` persisted store. The **.NET `DemoController` API and
> the SQL Server schema are unchanged** — only the presentation layer is
> re-platformed onto the Vivere/Next.js stack.

---

## DT Storefront – Setup Guide

This is a **Node.js** project (React + Vite). No Python required.

---

## Prerequisites

- [Node.js](https://nodejs.org) v18 or higher
- npm (comes bundled with Node.js)

---

## Getting Started

```bash
cd "3 🌐 DT Storefront_Prototype_Ideal State (Copy)"
npm install
npm run dev
```

Then open **http://localhost:5174/** in your browser.

---

## Runtime Dependencies

### UI & Components

| Package | Version | Purpose |
|---|---|---|
| `react` | 18.3.1 | Core framework |
| `react-dom` | 18.3.1 | DOM rendering |
| `@mui/material` | 7.3.5 | Material UI component library |
| `@mui/icons-material` | 7.3.5 | Material UI icons |
| `@emotion/react` | 11.14.0 | MUI styling engine |
| `@emotion/styled` | 11.14.1 | MUI styled components |
| `lucide-react` | 0.487.0 | Icon set |
| `cmdk` | 1.1.1 | Command palette |
| `vaul` | 1.1.2 | Drawer component |
| `sonner` | 2.0.3 | Toast notifications |
| `embla-carousel-react` | 8.6.0 | Carousel |
| `react-resizable-panels` | 2.1.7 | Resizable panel layouts |
| `react-day-picker` | 8.10.1 | Date picker |
| `react-dnd` | 16.0.1 | Drag and drop |
| `react-dnd-html5-backend` | 16.0.1 | HTML5 drag and drop backend |
| `react-hook-form` | 7.55.0 | Form management |
| `react-responsive-masonry` | 2.7.1 | Masonry grid layout |
| `react-slick` | 0.31.0 | Slider / carousel |
| `react-popper` | 2.3.0 | Tooltip/popover positioning |
| `input-otp` | 1.4.2 | OTP input field |
| `next-themes` | 0.4.6 | Dark / light mode theming |

### Radix UI Primitives

| Package | Version |
|---|---|
| `@radix-ui/react-accordion` | 1.2.3 |
| `@radix-ui/react-alert-dialog` | 1.1.6 |
| `@radix-ui/react-aspect-ratio` | 1.1.2 |
| `@radix-ui/react-avatar` | 1.1.3 |
| `@radix-ui/react-checkbox` | 1.1.4 |
| `@radix-ui/react-collapsible` | 1.1.3 |
| `@radix-ui/react-context-menu` | 2.2.6 |
| `@radix-ui/react-dialog` | 1.1.6 |
| `@radix-ui/react-dropdown-menu` | 2.1.6 |
| `@radix-ui/react-hover-card` | 1.1.6 |
| `@radix-ui/react-label` | 2.1.2 |
| `@radix-ui/react-menubar` | 1.1.6 |
| `@radix-ui/react-navigation-menu` | 1.2.5 |
| `@radix-ui/react-popover` | 1.1.6 |
| `@radix-ui/react-progress` | 1.1.2 |
| `@radix-ui/react-radio-group` | 1.2.3 |
| `@radix-ui/react-scroll-area` | 1.2.3 |
| `@radix-ui/react-select` | 2.1.6 |
| `@radix-ui/react-separator` | 1.1.2 |
| `@radix-ui/react-slider` | 1.2.3 |
| `@radix-ui/react-slot` | 1.1.2 |
| `@radix-ui/react-switch` | 1.1.3 |
| `@radix-ui/react-tabs` | 1.1.3 |
| `@radix-ui/react-toggle` | 1.1.2 |
| `@radix-ui/react-toggle-group` | 1.1.2 |
| `@radix-ui/react-tooltip` | 1.1.8 |

### Animation

| Package | Version | Purpose |
|---|---|---|
| `framer-motion` | ^12.29.3 | Animation library |
| `motion` | 12.23.24 | Motion primitives |

### Data & Charts

| Package | Version | Purpose |
|---|---|---|
| `recharts` | 2.15.2 | Chart components |
| `date-fns` | 3.6.0 | Date formatting utilities |

### Styling Utilities

| Package | Version | Purpose |
|---|---|---|
| `tailwindcss` | 4.1.12 | Utility-first CSS framework |
| `tw-animate-css` | 1.3.8 | Tailwind animation utilities |
| `tailwind-merge` | 3.2.0 | Merge Tailwind class names |
| `class-variance-authority` | 0.7.1 | Component variant styling |
| `clsx` | 2.1.1 | Conditional class names |

---

## Dev Dependencies (build tools only)

| Package | Version | Purpose |
|---|---|---|
| `vite` | 6.3.5 | Dev server & bundler |
| `@vitejs/plugin-react` | 4.7.0 | React support for Vite |
| `@tailwindcss/vite` | 4.1.12 | Tailwind plugin for Vite |

---

## Available Scripts

| Command | Description |
|---|---|
| `npm run dev` | Start local dev server |
| `npm run build` | Build for production |

---

## SQL-Tools — Atlatl (.NET 8 / Avalonia desktop workbench)

A cross-platform desktop SQL + API workbench — a modern port of the original Delphi 2007 "SQL Server Tool". Connects to SQL Server, runs T-SQL with syntax highlighting and completion, browses schema, edits results inline, and keeps a searchable history. Ships on Windows, Linux, and macOS from a single codebase.

---

### Prerequisites

| Tool | Version | Notes |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | **8.0** | `dotnet --version` must show `8.x.x` |
| [Node.js](https://nodejs.org) | **18 +** | Required only for the AI agent sidecar (`atlatl-agent.mjs`) |
| [Podman Desktop](https://podman-desktop.io/) | latest | Required for the SQL sandbox feature (restore `.bak` into a local SQL Server container) |
| [SQL Server](https://www.microsoft.com/en-us/sql-server/) | 2019 + | Developer or Express edition; also available via `podman pull mcr.microsoft.com/mssql/server` |
| [sqlpackage](https://learn.microsoft.com/en-us/sql/tools/sqlpackage/) | latest | Optional — needed to deploy / inspect `.dacpac` bundles via DacFx |

---

### Getting Started

```bash
cd dotnet
dotnet restore SqlTool.sln
dotnet build SqlTool.sln -c Release
dotnet run --project SqlTool.App
```

Run as an MCP server (exposes SQL tools to any MCP-capable host):

```bash
dotnet run --project SqlTool.App -- --mcp-server
```

---

### AI Agent Sidecar — Node.js setup

The agent sidecar (`SqlTool.App/Agent/atlatl-agent.mjs`) is a self-contained ESM script. It is copied to the output directory automatically by the build. To use the AI agent feature you need the Anthropic Claude Agent SDK installed where Node can find it.

```bash
# install globally (simplest — no package.json required)
npm install -g @anthropic-ai/claude-agent-sdk zod

# OR point Atlatl at a local install via the env var
export ATLATL_SDK_PATH=/path/to/node_modules/@anthropic-ai/claude-agent-sdk
```

| Package | Purpose |
|---|---|
| `@anthropic-ai/claude-agent-sdk` | Drives Claude AI as a persistent single-session chat over the agentic API |
| `zod` | Runtime schema validation (used by the SDK internally and by the sidecar) |

Auth runs on your Claude subscription — no API key is set in the app.

---

### Solution structure

```
dotnet/
├── SqlTool.sln
├── Directory.Build.props          ← global compiler settings (nullable, warnings-as-errors)
├── SqlTool.App/                   ← WinExe — Avalonia UI host, MCP server mode
│   └── Agent/atlatl-agent.mjs    ← Node.js AI-agent sidecar (shipped next to the exe)
├── SqlTool.Core/                  ← Class library — all business logic, no UI references
├── SqlTool.App.Tests/             ← Avalonia Headless UI tests (xUnit)
├── SqlTool.Core.Tests/            ← Pure unit tests (xUnit)
└── _mcp_probe/                    ← Minimal MCP probe used during development
```

---

### NuGet Packages

#### SqlTool.App — UI host (`net8.0`, WinExe)

| Package | Version | Purpose |
|---|---|---|
| `Avalonia` | 11.2.2 | Cross-platform UI framework |
| `Avalonia.Desktop` | 11.2.2 | Desktop (Win / Linux / macOS) platform support |
| `Avalonia.Themes.Fluent` | 11.2.2 | Fluent design theme |
| `Avalonia.Fonts.Inter` | 11.2.2 | Inter font bundle |
| `Avalonia.Controls.DataGrid` | 11.2.2 | Result-grid control |
| `Avalonia.AvaloniaEdit` | 11.2.0 | Code editor (T-SQL syntax highlighting + completion) |
| `Avalonia.ReactiveUI` | 11.2.2 | ReactiveUI integration for MVVM |
| `Avalonia.Diagnostics` | 11.2.2 | Dev-time visual inspector (debug builds) |
| `CommunityToolkit.Mvvm` | 8.4.0 | Source-generated MVVM (commands, observables) |
| `ModelContextProtocol` | 0.3.0-preview.4 | MCP server SDK — exposes SQL tools to AI hosts |
| `Microsoft.Extensions.Hosting` | 8.0.1 | Generic host for DI / MCP server mode |

#### SqlTool.Core — Business logic (`net8.0`, class library)

| Package | Version | Purpose |
|---|---|---|
| `Avalonia.AvaloniaEdit` | 11.2.0 | T-SQL highlighting definitions (embedded `.xshd` resources) |
| `Dapper` | 2.1.35 | Lightweight ORM — query history, table defaults |
| `Microsoft.Data.Sqlite` | 8.0.10 | SQLite driver — persists query history (`history.db`) |
| `Microsoft.Data.SqlClient` | 5.2.2 | SQL Server ADO.NET driver — all live query execution |
| `Microsoft.SqlServer.DacFx` | 162.5.57 | `.dacpac` schema extraction and sandbox provisioning |

#### SqlTool.App.Tests — Avalonia headless UI tests (`net8.0`)

| Package | Version | Purpose |
|---|---|---|
| `Avalonia` | 11.2.2 | Core framework (test-side reference) |
| `Avalonia.Headless` | 11.2.2 | Off-screen rendering for CI |
| `Avalonia.Headless.XUnit` | 11.2.2 | xUnit fixture integration for headless tests |
| `Avalonia.Skia` | 11.2.2 | Skia rendering backend (required by headless) |
| `FluentAssertions` | 6.12.2 | Readable assertion DSL |
| `Microsoft.NET.Test.Sdk` | 17.8.0 | MSBuild test runner infrastructure |
| `xunit` | 2.5.3 | Test framework |
| `xunit.runner.visualstudio` | 2.5.3 | VS / `dotnet test` adapter |
| `coverlet.collector` | 6.0.0 | Code coverage collection |

#### SqlTool.Core.Tests — Unit tests (`net8.0`)

| Package | Version | Purpose |
|---|---|---|
| `FluentAssertions` | 6.12.2 | Readable assertion DSL |
| `Microsoft.NET.Test.Sdk` | 17.8.0 | MSBuild test runner infrastructure |
| `xunit` | 2.5.3 | Test framework |
| `xunit.runner.visualstudio` | 2.5.3 | VS / `dotnet test` adapter |
| `coverlet.collector` | 6.0.0 | Code coverage collection |

#### _mcp_probe — Dev-time MCP probe (`net8.0`)

| Package | Version | Purpose |
|---|---|---|
| `ModelContextProtocol` | 0.3.0-preview.4 | MCP SDK (mirrors App reference) |
| `Microsoft.Extensions.Hosting` | 8.0.1 | Generic host |

---

### Available Commands

| Command | Description |
|---|---|
| `dotnet restore SqlTool.sln` | Restore all NuGet packages |
| `dotnet build SqlTool.sln -c Release` | Build all projects |
| `dotnet run --project SqlTool.App` | Launch the desktop workbench |
| `dotnet run --project SqlTool.App -- --mcp-server` | Start in MCP server mode |
| `dotnet test SqlTool.Core.Tests/SqlTool.Core.Tests.csproj` | Run core unit tests |
| `dotnet test SqlTool.App.Tests/SqlTool.App.Tests.csproj` | Run Avalonia headless UI tests |

### Self-contained publish (no .NET install required on target)

```bash
cd dotnet
dotnet publish SqlTool.App/SqlTool.App.csproj -c Release -r win-x64   --self-contained true -o publish/win-x64
dotnet publish SqlTool.App/SqlTool.App.csproj -c Release -r linux-x64 --self-contained true -o publish/linux-x64
dotnet publish SqlTool.App/SqlTool.App.csproj -c Release -r osx-arm64 --self-contained true -o publish/osx-arm64
dotnet publish SqlTool.App/SqlTool.App.csproj -c Release -r osx-x64   --self-contained true -o publish/osx-x64
```

| Platform | Output folder | Launch |
|---|---|---|
| Windows x64 | `publish/win-x64` | `SqlTool.App.exe` |
| Linux x64 | `publish/linux-x64` | `./SqlTool.App` |
| macOS Apple Silicon | `publish/osx-arm64` | `./SqlTool.App` |
| macOS Intel | `publish/osx-x64` | `./SqlTool.App` |

---

### Configuration & data locations

Per-user, in the OS config directory:

| Platform | Path |
|---|---|
| Windows | `%LOCALAPPDATA%\SqlTool\` |
| Linux | `~/.local/share/SqlTool/` |
| macOS | `~/Library/Application Support/SqlTool/` |

Files stored there:

| File | Contents |
|---|---|
| `connections.json` | Saved server connections (credentials AES-GCM-256 encrypted) |
| `settings.json` | App settings (row limit, timeout, theme, etc.) |
| `master.json` | Master password verifier (PBKDF2-SHA256) |
| `history.db` | SQLite — full query history |
| `table-defaults.json` | Per-table remembered default queries |

---

### SQL Sandbox (Podman)

The sandbox feature restores a `.bak` file into a running Podman SQL Server container for isolated testing. The container must already be running — Atlatl does not auto-start Podman.

```bash
# pull and start a local SQL Server container (one-time)
podman pull mcr.microsoft.com/mssql/server:2022-latest
podman run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YourStrong@Passw0rd" \
  -p 1433:1433 --name sql-sandbox -d mcr.microsoft.com/mssql/server:2022-latest
```

Then use **File ▸ Sandbox…** in Atlatl to point at a `.bak` and connect string.

---

## Python — Global Machine Learning & AI Environment

A single global Python environment shared by every Python script and service in
the repo. Install everything below once, globally, and all the ML / AI / RAG
code will run.

### Core runtime & tooling (install distinction)

These are the **core** runtime tools, package managers, and servers — distinct
from the library packages in the tables further below.

| Tool | Type | Purpose |
|---|---|---|
| `python` | Core runtime | CPython interpreter |
| `pip` | Core package manager | Installs Python packages |
| `uvicorn` | Core ASGI server | Runs FastAPI / async Python services |
| `gunicorn` | Core process manager | Production WSGI/ASGI worker manager |
| `node` | Core runtime | Node.js runtime (frontend + agent sidecars) |
| `npm` | Core package manager | Installs Node packages |
| `npx` | Core package runner | Runs Node CLIs |
| `nvm` / `fnm` | Core version manager | Manage multiple Node.js versions side-by-side |
| `dotnet` | Core runtime | .NET SDK (WongaLoans + Atlatl) |
| `git` | Core VCS | Version control |
| `gh` | Core VCS CLI | GitHub CLI (PRs, issues, releases) |
| `neo4j` | Core database | Graph database server (Neo4j 5.x) |
| `qdrant` | Core database | Vector database server |
| `elasticsearch` | Core database | Keyword / hybrid-search server |
| `postgres` (`psql`) | Core database | PostgreSQL relational database server |
| `pgAdmin` | Core DB GUI | PostgreSQL admin / query tool |
| `postgis` | Core DB extension | Spatial / geographic objects for PostgreSQL |
| `mysql` / `mysqld` | Core database | MySQL relational database server |
| `sqlite3` | Core database | Embedded file-based SQL database |

### Prerequisites

| Tool | Version | Notes |
|---|---|---|
| [Python](https://www.python.org/downloads/) | **3.10 – latest** | 3.10 minimum, any newer 3.x works |
| [CUDA Toolkit](https://developer.nvidia.com/cuda-toolkit) | 11.x | **Optional** — GPU acceleration only (`cupy-cuda11x`, GPU Torch). Default Torch is the CPU build |
| [Tesseract OCR](https://github.com/tesseract-ocr/tesseract) | latest | System binary required by `pytesseract` |
| [Poppler](https://poppler.freedesktop.org/) | latest | System binary required by `pdf2image` (`poppler/` is vendored at the repo root on Windows) |
| [Pandoc](https://pandoc.org/) | latest | Required by `pypandoc` for EPUB/RTF conversion |
| [Neo4j](https://neo4j.com/download/) | 5.x | Graph database backing the RAG / knowledge-graph layer |
| [Qdrant](https://qdrant.tech/) | latest | Vector store (Docker/Podman or cloud) |
| [Elasticsearch](https://www.elastic.co/elasticsearch) | 8.x | Keyword / hybrid-search backend |

---

### Databases & Developer Tooling

System-level servers, GUIs, and version managers (not pip packages). Multiple
major versions of Node / PostgreSQL / MySQL may coexist — use a version manager
and per-instance ports rather than uninstalling.

#### Node.js toolchain (multiple versions)

| Tool | Versions | Notes |
|---|---|---|
| [Node.js](https://nodejs.org) | **18 LTS / 20 LTS / 22 LTS** | DT Storefront needs ≥ 18; agent sidecars run on 20/22. Manage with `nvm`/`fnm` |
| `npm` | 9.x (Node 18) / 10.x (Node 20+) | Ships with the matching Node version |
| `npx` | bundled with `npm` | Same version as the active `npm` |
| `nvm` (Linux/macOS) / `nvm-windows` / `fnm` | latest | Install + switch between Node majors per project |
| `yarn` / `pnpm` | latest | Optional alternative package managers |

#### PostgreSQL + PostGIS (multiple versions)

| Tool | Versions | Notes |
|---|---|---|
| [PostgreSQL](https://www.postgresql.org/download/) | **14 / 15 / 16 / 17** | Run different majors on different ports (5432, 5433, …) |
| [pgAdmin 4](https://www.pgadmin.org/) | latest | Web/desktop admin & query GUI |
| [PostGIS](https://postgis.net/) | 3.4 + | Spatial extension — `CREATE EXTENSION postgis;` |
| `postgis_topology` | bundled w/ PostGIS | Topology models |
| `postgis_raster` | bundled w/ PostGIS | Raster / coverage support |
| `postgis_sfcgal` | optional | Advanced 3D geometry (SFCGAL) |
| `pgrouting` | latest | Graph routing over PostGIS networks |
| `address_standardizer` | bundled w/ PostGIS | Address normalization |
| `fuzzystrmatch` | core contrib | Required by the PostGIS geocoder |
| `postgis_tiger_geocoder` | bundled w/ PostGIS | TIGER geocoding |
| `pgvector` | latest | Vector similarity search inside PostgreSQL |
| `h3` / `h3-pg` | optional | Hexagonal hierarchical geospatial index |

#### MySQL

| Tool | Versions | Notes |
|---|---|---|
| [MySQL](https://dev.mysql.com/downloads/) | **8.0 / 8.4 LTS** | Run majors on separate ports (3306, 3307, …) |
| MySQL Workbench | latest | Admin / query GUI |

#### SQLite

| Tool | Version | Notes |
|---|---|---|
| [SQLite](https://www.sqlite.org/) | 3.x | Embedded DB; CLI is `sqlite3`. Used for query-history stores |

> Python DB drivers for all of the above (`psycopg`, `mysqlclient`,
> `GeoAlchemy2`, etc.) are listed in the
> [Relational / Spatial Database Drivers](#relational--spatial-database-drivers) table below.

---

### Core Tensor / Deep-Learning Runtime (the foundation)

Everything else sits on PyTorch tensors. Install this group first.

> **`torch` + `torchvision` are a matched pair** — a given torchvision only
> builds against one torch minor. Bump them together, never independently.

| Package | Version | Purpose |
|---|---|---|
| `torch` | ≥ 2.6, < 2.7 | Core tensor library + autograd + neural-net runtime. (CPU build by default; install a `+cu11x`/`+cu12x` wheel for GPU) |
| `torchvision` | ≥ 0.21, < 0.22 | Vision tensors, image transforms, pretrained CNN backbones — must match `torch` |
| `numpy` | ≥ 1.26.4 | N-dimensional arrays — the substrate under every ML library |
| `scipy` | ≥ 1.10.1 | Sparse matrices, linear algebra, graph Laplacians, signal processing |
| `sympy` | bundled | Symbolic math (Torch dependency) |
| `mpmath` | bundled | Arbitrary-precision math (SymPy dependency) |
| `networkx` | ≥ 3.0 | Graph algorithms, diffusion, knowledge-graph traversal |
| `filelock` | bundled | Safe concurrent model-cache access |
| `fsspec` | bundled | Filesystem abstraction for model / dataset loading |

### Hugging Face — Transformers & Model Hub

> **Do not pin these against each other.** `transformers` already declares the
> compatible ranges for `tokenizers`, `huggingface-hub`, and `safetensors`.
> Pin **only** `transformers` (and `sentence-transformers`), then let pip
> resolve the rest — that is what keeps the "flavours" in lockstep. Hard-pinning
> e.g. `transformers==4.51.3` next to a stray `tokenizers==0.22.x` is exactly
> what triggers resolver conflicts.

| Package | Version | Purpose |
|---|---|---|
| `transformers` | ≥ 4.51, < 5.0 | Transformer models (BERT, GPT, T5, LLaMA, etc.) — inference & fine-tuning. **Drives the deps below.** |
| `tokenizers` | *resolved by `transformers`* | Fast Rust tokenizers — version chosen by `transformers` |
| `huggingface-hub` | *resolved by `transformers`* | Model / dataset / space download + cache |
| `safetensors` | *resolved by `transformers`* | Safe, fast tensor serialization format |
| `sentencepiece` | ≥ 0.1.99 | Sub-word tokenization (T5, XLNet, LLaMA, multilingual models) |
| `accelerate` | ≥ 1.0 | Device placement, mixed-precision, multi-GPU / distributed inference |
| `regex` | *resolved by `transformers`* | Advanced regex used by HF tokenizers |
| `hf_transfer` | ≥ 0.1.9 | Rust-accelerated, high-throughput Hub downloads (`HF_HUB_ENABLE_HF_TRANSFER=1`) |

### Hugging Face — Datasets, Models & Training

The "datasets / models perspective" of the Hub — loading data, fine-tuning,
quantization, and serving.

| Package | Version | Purpose |
|---|---|---|
| `datasets` | ≥ 2.18 | Load / stream / map Hugging Face datasets (Arrow-backed) |
| `evaluate` | ≥ 0.4 | Standardized metrics for models & datasets |
| `huggingface-hub[cli]` | *resolved by `transformers`* | `hf` CLI — download/upload models, datasets, Spaces |
| `peft` | ≥ 0.10 | Parameter-efficient fine-tuning (LoRA / QLoRA / adapters) |
| `trl` | ≥ 0.8 | RLHF / SFT / DPO / GRPO transformer training |
| `diffusers` | ≥ 0.27 | Diffusion models (image / audio generation) |
| `optimum` | ≥ 1.18 | Hardware-optimized inference (ONNX, OpenVINO, etc.) |
| `timm` | ≥ 0.9 | PyTorch image models (ViT, ResNet, EfficientNet, …) |
| `bitsandbytes` | ≥ 0.43 | 8-bit / 4-bit quantization for large models (GPU) |
| `einops` | ≥ 0.7 | Readable tensor reshaping / einsum operations |
| `xformers` | latest | Memory-efficient attention kernels (GPU, optional) |
| `flash-linear-attention` | ≥ 0.1 | Linear-attention / DeltaNet / GLA / gated-attention kernels (the `fla` library — backs this repo's DeltaNet models) |
| `gradio` | ≥ 4.0 | Build ML demo web UIs |
| `streamlit` | ≥ 1.30 | Data / ML dashboards |

### Alternative Tensor / Deep-Learning Frameworks

PyTorch is the default, but these are the other tensor runtimes for
cross-framework models, export, and serving.

| Package | Version | Purpose |
|---|---|---|
| `tensorflow` | ≥ 2.15 | Google's tensor / DL framework |
| `keras` | ≥ 3.0 | High-level neural-net API (multi-backend in Keras 3) |
| `jax` / `jaxlib` | latest | Composable autodiff + XLA-accelerated tensors |
| `flax` | latest | Neural-net library built on JAX |
| `onnx` | ≥ 1.16 | Open Neural Network Exchange model format |
| `onnxruntime` (`-gpu`) | ≥ 1.17 | Cross-platform ONNX inference engine |

### Rust-Backed & Compiled Runtimes

Many of the fastest pieces of the stack are Rust crates exposed as compiled
Python wheels — no Rust toolchain needed to *use* them. A toolchain **is**
required to build from source, compile native Python extensions, or target
WebAssembly (e.g. running `candle` / `tokenizers` in the browser).

#### Rust + WASM build toolchain

| Tool | Purpose |
|---|---|
| `rustup` | Rust toolchain installer / version manager |
| `cargo` | Rust build system + package manager |
| `rustc` | The Rust compiler |
| `clippy` / `rustfmt` | Rust linter + formatter (components via `rustup component add`) |
| `cargo-binstall` | Install prebuilt cargo binaries fast |
| `maturin` | Build & publish Rust → **Python** wheels (PyO3 / `pyproject.toml`) |
| `setuptools-rust` | Build Rust extensions inside setuptools-based packages |
| PyO3 (crate) | Rust ↔ Python bindings (used by `pydantic-core`, `tokenizers`) |
| `target: wasm32-unknown-unknown` | WASM build target (`rustup target add wasm32-unknown-unknown`) |
| `target: wasm32-wasi` / `wasm32-wasip1` | WASI system-interface WASM target |
| `wasm-pack` | Build Rust → WASM + JS bindings for the web/npm |
| `wasm-bindgen` (`-cli`) | Generate JS ↔ WASM glue bindings |
| `wasm-opt` (binaryen) | Optimize / shrink `.wasm` binaries |
| `wasmtime` / `wasmer` | Standalone WASM/WASI runtimes for running modules outside the browser |
| `wasmtime-py` / `wasmer` (pip) | Run WASM modules from Python |
| `cbindgen` | Generate C headers from Rust (FFI) |
| C/C++ build tools (`gcc`/`clang`, `cmake`, `ninja`, MSVC Build Tools on Windows) | Native compilers many crates / wheels link against |

#### Compiled (Rust) acceleration already in the stack

| Package | Backed by | Purpose |
|---|---|---|
| `tokenizers` | Rust | Fast HF tokenizers (resolved by `transformers`) |
| `safetensors` | Rust | Zero-copy tensor serialization |
| `tiktoken` | Rust | OpenAI BPE token counting |
| `pydantic-core` | Rust | Validation core under `pydantic` v2 |
| `neo4j-rust-ext` | Rust | Rust-accelerated Neo4j driver |
| `polars` | Rust | Columnar DataFrames (Arrow) — faster pandas alternative |
| `orjson` | Rust | Fastest JSON (de)serialization |
| `rapidfuzz` | C++/Rust | Fuzzy string matching |

#### Rust-native ML frameworks (optional)

| Tool | Author | Purpose |
|---|---|---|
| [`candle`](https://github.com/huggingface/candle) | Hugging Face | Minimalist Rust ML framework — LLM/transformer inference without Python (WASM/GPU) |
| [`burn`](https://burn.dev/) | Tracel AI | Full Rust deep-learning framework with autodiff + pluggable backends |

#### Google LiteRT — on-device compiled runtime (latest release)

Google's most recent on-device ML release (LiteRT, formerly TensorFlow Lite),
shipped as the **Google Tensor ML SDK** (Beta, May 2026). It compiles models
ahead-of-time and runs them on Pixel Tensor TPUs; precompiled models live on the
LiteRT Hugging Face community.

| Package / SDK | Purpose |
|---|---|
| `ai-edge-litert` | LiteRT Python runtime — load & run `.tflite` / LiteRT models |
| `ai-edge-torch` | Convert PyTorch models → LiteRT for on-device deployment |
| [Google Tensor ML SDK](https://developers.googleblog.com/en/google-tensor-sdk-beta-with-litert/) | Compile + deploy to Pixel Tensor TPU (LiteRT-integrated; precompiled model garden, incl. Gemma 3 1B) |

### Embeddings & Semantic Search (Sentence Transformers)

| Package | Version | Purpose |
|---|---|---|
| `sentence-transformers` | ≥ 2.2.2, < 5.0 | Sentence / passage embeddings, similarity, bi-/cross-encoders, reranking. Depends on `transformers` — keep it a range so the two resolve together |
| `datasketch` | bundled | MinHash / LSH for approximate near-duplicate detection |
| `rapidfuzz` | bundled | Fast fuzzy string matching |
| `python-Levenshtein` | 0.27.1 | Edit-distance scoring |

### Classical ML / Statistics / Data Science

| Package | Version | Purpose |
|---|---|---|
| `scikit-learn` | 1.3.0 | Classical ML — **Random Forest, Extra Trees, Decision Trees, Gradient Boosting, AdaBoost, Bagging, SVM, KNN, logistic/linear regression, k-means, DBSCAN, PCA**, pipelines, metrics |
| `pandas` | 2.0.3 | DataFrames — tabular data wrangling |
| `polars` | ≥ 0.20 | Fast Rust-backed DataFrames (out-of-core, lazy) |
| `numpy` | ≥ 1.26.4 | Arrays (also under Core runtime) |
| `statsmodels` | ≥ 0.14.0 | Statistical models, OLS/GLM regression, time-series |
| `patsy` | bundled | Formula API for statsmodels |
| `imbalanced-learn` | ≥ 0.12 | Resampling (SMOTE, under/over-sampling) for skewed datasets |
| `joblib` | bundled | Parallelism + model persistence (scikit-learn dependency) |
| `threadpoolctl` | bundled | Thread-pool control for BLAS/OpenMP |
| `matplotlib` | ≥ 3.7.0 | Plotting / visualization |
| `seaborn` | ≥ 0.13 | Statistical plotting |
| `plotly` | ≥ 4.14.0 | Interactive charts |
| `contourpy`, `cycler`, `fonttools`, `kiwisolver`, `pyparsing` | bundled | Matplotlib rendering stack |

### Gradient Boosting & Tree Ensembles

The dedicated boosting libraries (faster / more accurate than scikit-learn's
built-in trees for large tabular data). All three have GPU training modes.

| Package | Version | Purpose |
|---|---|---|
| `xgboost` | ≥ 2.0 | Extreme gradient boosting (CPU + CUDA). Also on the DGX Spark stack |
| `lightgbm` | ≥ 4.3 | Microsoft gradient boosting — histogram-based, very fast |
| `catboost` | ≥ 1.2 | Yandex boosting with native categorical support |
| `scikit-learn` | 1.3.0 | `RandomForestClassifier/Regressor`, `GradientBoostingClassifier`, `HistGradientBoosting`, `ExtraTrees`, `AdaBoost` |

### Deep-Learning Architectures & Training (CNN / RNN / LSTM / Transformer)

The architectures themselves (RNN, LSTM, GRU, CNN, Transformer) live inside
`torch.nn` / `keras`; these packages add high-level training loops, schedulers,
and ready-made model zoos.

| Package | Version | Purpose |
|---|---|---|
| `torch.nn` (built-in) | — | `RNN`, `LSTM`, `GRU`, `Conv1d/2d/3d`, `Transformer`, attention, embeddings |
| `pytorch-lightning` | ≥ 2.2 | Structured training loops, multi-GPU, checkpointing |
| `lightning` | ≥ 2.2 | Lightning umbrella (Fabric + Trainer) |
| `torchmetrics` | ≥ 1.3 | GPU-ready metrics for training/eval |
| `skorch` | ≥ 0.15 | scikit-learn-compatible wrapper around PyTorch models |
| `fastai` | ≥ 2.7 | High-level training over PyTorch (vision, text, tabular) |
| `pytorch-forecasting` | ≥ 1.0 | RNN / Temporal-Fusion-Transformer time-series models |
| `torch-geometric` | ≥ 2.5 | Graph Neural Networks (GCN, GAT, GraphSAGE) |
| `keras` | ≥ 3.0 | `LSTM`, `GRU`, `SimpleRNN`, `Conv`, functional/sequential APIs |

### Time-Series Forecasting

| Package | Version | Purpose |
|---|---|---|
| `prophet` | ≥ 1.1 | Additive forecasting (trend/seasonality/holidays) |
| `sktime` | ≥ 0.28 | Unified time-series ML (classification, forecasting) |
| `tslearn` | ≥ 0.6 | Time-series clustering / DTW / shapelets |
| `statsforecast` | ≥ 1.7 | Fast classical forecasting (ARIMA, ETS, Theta) |
| `pmdarima` | ≥ 2.0 | Auto-ARIMA |
| `darts` | ≥ 0.27 | Unified forecasting incl. deep models |

### Model Tuning, AutoML & Explainability

| Package | Version | Purpose |
|---|---|---|
| `optuna` | ≥ 3.6 | Hyperparameter optimization (TPE, pruning) |
| `hyperopt` | ≥ 0.2.7 | Distributed hyperparameter search |
| `scikit-optimize` | ≥ 0.10 | Bayesian optimization over sklearn estimators |
| `shap` | ≥ 0.45 | SHAP feature-attribution explainability |
| `lime` | ≥ 0.2 | Local interpretable model explanations |
| `eli5` | ≥ 0.13 | Inspect / debug ML models |
| `umap-learn` | ≥ 0.5 | UMAP dimensionality reduction |
| `hdbscan` | ≥ 0.8 | Density-based hierarchical clustering |
| `gensim` | ≥ 4.3 | Word2Vec / Doc2Vec / LDA topic modeling |

### Audio / Speech

| Package | Version | Purpose |
|---|---|---|
| `torchaudio` | matches `torch` | Audio tensors, transforms, pretrained speech models |
| `librosa` | ≥ 0.10 | Audio analysis (spectrograms, MFCC, features) |
| `soundfile` | ≥ 0.12 | Read/write audio files |
| `openai-whisper` | latest | Whisper speech-to-text |
| `faster-whisper` | ≥ 1.0 | CTranslate2-accelerated Whisper |
| `speechbrain` | ≥ 1.0 | Speech toolkit (ASR, speaker ID, enhancement) |

### Reinforcement Learning

| Package | Version | Purpose |
|---|---|---|
| `gymnasium` | ≥ 0.29 | RL environments API (successor to OpenAI Gym) |
| `stable-baselines3` | ≥ 2.3 | RL algorithms (PPO, DQN, SAC, A2C) on PyTorch |

### GPU Acceleration & NVIDIA Accelerated Computing (incl. DGX Spark)

Two tiers: the lightweight CPU/GPU JIT already in this repo, and the full
NVIDIA CUDA-X / DGX Spark stack for when models run on NVIDIA hardware.

#### Lightweight (already in this repo)

| Package | Version | Purpose |
|---|---|---|
| `cupy-cuda11x` | 13.3.0 | GPU NumPy-compatible arrays + parallel kernels (CUDA 11.x) |
| `fastrlock` | ≥ 0.5 | Fast GPU memory locking (auto-installed with CuPy) |
| `numba` | ≥ 0.56, < 0.62 | JIT compilation of numeric Python to CPU/GPU machine code |

#### NVIDIA DGX Spark target platform

> **DGX Spark = GB10 Grace Blackwell Superchip**, compute capability **`sm_121`**,
> 20-core ARM64 (Grace) CPU, **128 GB unified LPDDR5x**, **CUDA 13.0**, DGX OS
> (Ubuntu 24.04), Python 3.12. Fine-tunes / serves models up to ~200B params.
>
> **`sm_121` caveat:** PyPI cannot publish multiple CUDA variants, so on DGX
> Spark you install framework wheels from NVIDIA's **custom index / NGC
> containers**, not plain `pip`. `sm_121` runs `sm_120` cubins natively;
> **NVFP4 is not supported on `sm_121`** (hardware limitation — use FP8/Marlin).

#### NVIDIA system stack (DGX OS / driver layer — not pip)

| Component | Version | Purpose |
|---|---|---|
| CUDA Toolkit | **13.0** | Core GPU compute toolkit on DGX Spark |
| cuDNN | latest | Deep-neural-net primitives |
| NCCL | latest | Multi-GPU / multi-node collective comms |
| NVIDIA driver | 580+ | GB10 / Blackwell driver |
| NVIDIA Container Toolkit | latest | GPU passthrough into Docker/Podman |
| NGC | — | NVIDIA container registry (PyTorch, vLLM, TRT-LLM images) |

#### CUDA-X math libraries (bundled with CUDA, used via Python wheels)

| Library | Purpose |
|---|---|
| `cuBLAS` | GPU dense linear algebra |
| `cuSPARSE` | Sparse linear algebra |
| `cuSOLVER` | Dense/sparse solvers |
| `cuFFT` | GPU FFTs |
| `cuRAND` | GPU RNG |
| `cuDSS` | Direct sparse solvers |
| `CUTLASS` | CUDA templates for GEMM / Tensor Cores |

#### RAPIDS — GPU data science (CUDA 13 on DGX Spark)

| Package | Purpose |
|---|---|
| `cudf` | GPU DataFrames (pandas-compatible) |
| `cuml` | GPU scikit-learn (RF, KNN, UMAP, DBSCAN, regression) |
| `cugraph` | GPU graph analytics |
| `cuspatial` | GPU spatial / geo operations |
| `cuvs` | GPU vector search (ANN) |
| Spark RAPIDS | GPU-accelerated Apache Spark |
| `xgboost` (GPU) | GPU-trained gradient boosting (integrates with RAPIDS) |

#### NVIDIA training / inference frameworks (NGC / source builds on `sm_121`)

| Component | DGX Spark status | Purpose |
|---|---|---|
| PyTorch (CUDA) | ✅ via NVIDIA index / NGC | Core training & inference |
| TensorRT | ✅ | Optimized inference engine |
| TensorRT-LLM | ✅ (stable ≥ 1.2.0) | Highest-throughput LLM inference |
| Triton Inference Server | ✅ (container) | Production model serving |
| NeMo Framework | ✅ | LLM / speech model fine-tuning |
| Transformer Engine | ⚠️ limited | FP8 transformer training (MXFP8 unsupported on `sm_121`) |
| DALI | ✅ | GPU data loading / augmentation |
| Megatron-LM | ✅ | Large-scale transformer training |
| `bitsandbytes` | ✅ (≥ 0.49) | 4-bit/8-bit (FP4/NF4) quantization |
| Triton (`triton`) | ⚠️ env fix | GPU kernel compiler (set `TRITON_PTXAS_PATH`) |
| `flash-attn` | ❌ on `sm_121` | Use PyTorch SDPA fallback; FlashInfer ≥ 0.5.2 covers `sm12x` |
| vLLM | ⚠️ container/source | High-throughput LLM serving |
| SGLang | ⚠️ source | Structured LLM serving |
| `llama-cpp-python` (llama.cpp) | ✅ | Lightweight quantized inference (best on DGX Spark) |
| Unsloth | ✅ | Fast QLoRA fine-tuning |

### NLP / Linguistics

| Package | Version | Purpose |
|---|---|---|
| `spacy` | 3.8.4 | Industrial NLP — NER, POS, dependency parsing, entity extraction |
| `en_core_web_sm` | 3.8.x | spaCy English model (install via `python -m spacy download en_core_web_sm`) |
| `spacy-legacy`, `spacy-loggers`, `thinc`, `blis`, `cymem`, `murmurhash`, `preshed`, `srsly`, `wasabi`, `catalogue`, `confection`, `langcodes`, `marisa-trie`, `weasel`, `cloudpathlib` | bundled | spaCy / Thinc internals |
| `nltk` | 3.8.1 | Tokenizers, stopwords, stemmers, WordNet |
| `langdetect` | 1.0.9 | Language detection |
| `tiktoken` | 0.4.0 | OpenAI BPE token counting / budgeting |

### RAG / LLM Orchestration

| Package | Version | Purpose |
|---|---|---|
| `langchain` | 0.3.23 | LLM-app orchestration framework (chains, agents, retrievers) |
| `langchain-core` | 0.3.51 | Core abstractions for LangChain |
| `langchain-community` | 0.3.19 | Community integrations (loaders, vector stores) |
| `langchain-experimental` | 0.3.4 | Experimental chains / agents |
| `langchain-text-splitters` | 0.3.8 | Document chunking strategies |
| `langchain-huggingface` | 0.1.2 | HF embeddings / pipelines for LangChain |
| `langchain-openai` | 0.3.12 | OpenAI provider |
| `langchain-anthropic` | 0.3.9 | Anthropic Claude provider |
| `langchain-groq` | 0.2.5 | Groq LPU provider |
| `langchain-aws` | 0.2.18 | AWS Bedrock provider |
| `langchain-google-vertexai` | 2.0.19 | Google Vertex AI provider |
| `langchain-google-genai` | ≥ 2.0 | Google **Gemini** provider for LangChain |
| `langchain-fireworks` | 0.2.9 | Fireworks AI provider |
| `langchain-neo4j` | 0.4.0 | Neo4j graph retriever / vector store |
| `langgraph` | ≥ 0.2 | Stateful multi-agent / graph workflows |
| `langsmith` | 0.3.26 | Tracing / eval / observability for LangChain |
| `langserve` | 0.3.1 | Serve LangChain runnables as APIs |
| `llama-index` | ≥ 0.10 | LlamaIndex RAG framework (indexes, query engines) |
| `llama-index-core` | ≥ 0.10 | LlamaIndex core abstractions |
| `haystack-ai` | 2.10.3 | Deepset Haystack 2.x RAG pipelines |
| `haystack-experimental` | 0.7.0 | Experimental Haystack components |
| `neo4j-haystack` | 2.2.1 | Neo4j document/vector store for Haystack |
| `openapi-llm` | 0.4.1 | OpenAPI-driven tool calling |
| `instructor` | ≥ 1.0 | Structured / typed LLM outputs via Pydantic |
| `ragas` | 0.2.14 | RAG evaluation metrics (faithfulness, relevancy, etc.) |
| `rouge_score` | 0.1.2 | ROUGE text-generation evaluation |

### LLM / Inference Provider SDKs

| Package | Version | Purpose |
|---|---|---|
| `openai` | ≥ 1.56 | OpenAI API client (GPT, embeddings, etc.) |
| `anthropic` | ≥ 0.25 | Anthropic Claude API client |
| `google-generativeai` | ≥ 0.7 | Google **Gemini** SDK (legacy `genai` namespace) |
| `google-genai` | ≥ 0.3 | Google **Gemini** unified SDK (new client) |
| `groq` | ≥ 0.4 | Groq inference API client |
| `cohere` | ≥ 5.0 | Cohere LLM / embeddings / rerank client |
| `mistralai` | ≥ 1.0 | Mistral AI API client |
| `together` | ≥ 1.0 | Together AI inference client |
| `replicate` | ≥ 0.25 | Replicate hosted-model client |
| `ollama` | ≥ 0.2 | Local LLM runtime client |
| `fireworks-ai` | latest | Fireworks AI client |
| `google-api-core` | 2.24.2 | Google API plumbing |
| `google-auth` / `google-auth-oauthlib` | 2.38.0 / 1.2.1 | Google auth for Vertex / Gemini |
| `google-cloud-aiplatform` | latest | Vertex AI SDK (Gemini on Vertex) |
| `google-cloud-core` | 2.4.3 | Google Cloud client base |
| `google-cloud-logging` | 3.11.4 | Cloud logging |
| `boto3` / `botocore` | 1.37.29 | AWS SDK (Bedrock, S3) |
| `jiter` | bundled | Fast streaming JSON parser for LLM responses |
| `json-repair` | 0.39.1 | Repairs malformed LLM JSON output |

### Local LLM Inference Servers & Runtimes

Self-hosted serving engines (the "vLLM, Ollama, etc." tier). All run on the
DGX Spark stack above — see status flags there for `sm_121` notes.

| Tool / Package | Version | Purpose |
|---|---|---|
| `vllm` | ≥ 0.8 | High-throughput paged-attention LLM serving (OpenAI-compatible API) |
| `ollama` | latest | Easiest local model runtime + REST API (client pkg already listed) |
| `llama-cpp-python` | latest | GGUF / quantized inference (llama.cpp bindings) |
| `text-generation` (TGI) | ≥ 0.7 | Hugging Face Text Generation Inference client |
| `sglang` | ≥ 0.3 | Structured / fast LLM serving with RadixAttention |
| `lmdeploy` | latest | TurboMind LLM serving + quantization |
| `ctransformers` | latest | GGML/GGUF model bindings |
| `litellm` | ≥ 1.40 | Unified OpenAI-style proxy across 100+ providers |
| `openllm` | latest | Self-host any open LLM with one command |
| `mlx-lm` | latest | Apple-Silicon (MLX) LLM runtime (optional) |
| Triton Inference Server | — | NVIDIA production serving (see DGX Spark table) |
| TensorRT-LLM | — | NVIDIA highest-throughput engine (see DGX Spark table) |

### AI Agent Frameworks & Platforms

Agents are a separate layer from chains/RAG. Each major vendor now ships its own
agent platform, and LangChain/community release framework-level agent SDKs.

#### Framework SDKs (pip)

| Package | Vendor | Purpose |
|---|---|---|
| `langgraph` | LangChain | Stateful, graph-based multi-agent workflows (also listed above) |
| `langchain` agents | LangChain | Tool-using agents / ReAct executors |
| `crewai` | CrewAI | Role-based collaborative agent "crews" |
| `camel-ai` | CAMEL-AI | Communicative / role-playing multi-agent framework |
| `autogen-agentchat` | Microsoft | AutoGen multi-agent group chat / orchestration |
| `agent-framework` | Microsoft | **Microsoft Agent Framework** (MAF) — enterprise successor to Semantic Kernel |
| `semantic-kernel` | Microsoft | Model-agnostic agent/plugin SDK (now folding into MAF) |
| `google-adk` | Google | **Agent Development Kit** — code-first agents, deploys to Vertex AI Agent Engine |
| `openai-agents` | OpenAI | OpenAI Agents SDK — handoffs, tools, guardrails |
| `pydantic-ai` | Pydantic | Type-safe production agent framework |
| `smolagents` | Hugging Face | Minimal code-writing agents |
| `dspy` | Stanford | Programmatic prompt/agent optimization |
| `llama-index` agents | LlamaIndex | Data/RAG-centric agents (also listed above) |
| `haystack-ai` agents | deepset | Pipeline-based agents (also listed above) |
| `metagpt` | community | Multi-agent software-company simulation |
| `autogpt` / `agentgpt` | community | Autonomous goal-driven agents |
| `phidata` / `agno` | Agno | Memory + tools agent framework |
| `letta` | Letta (MemGPT) | Stateful agents with long-term memory |

#### Vendor agent platforms (managed)

| Platform | Vendor | Notes |
|---|---|---|
| Vertex AI **Agent Builder** / **Agent Engine** | Google | Deploy/host agents on GCP; pairs with `google-adk` |
| **Agent2Agent (A2A)** protocol | Google | Open agent-to-agent interop protocol |
| **Azure AI Foundry Agent Service** | Microsoft | Managed enterprise agent hosting |
| **Copilot Studio** | Microsoft | Low-code agent / copilot builder |
| **LangGraph Platform** + LangSmith | LangChain | Hosted agent deployment + tracing/eval |
| **Amazon Bedrock Agents** | AWS | Managed agents over Bedrock models |
| **Model Context Protocol (MCP)** | open standard | Tool/data connectivity for agents (`mcp` Python SDK) |

### Vector / Graph / Search Databases (Python clients)

| Package | Version | Purpose |
|---|---|---|
| `neo4j` | 5.x | Neo4j graph database driver |
| `neo4j-rust-ext` | latest | Rust-accelerated Neo4j driver extension |
| `py2neo` | ≥ 2021.2.4 | High-level Neo4j OGM / Cypher helper |
| `graphdatascience` | 1.14 | Neo4j Graph Data Science (GDS) Python client |
| `neurora` | ≥ 1.1.6.12 | Neo4j AI / RAG utilities |
| `qdrant-client` | 1.13.3 | Qdrant vector database client |
| `elasticsearch` | 8.8.0 | Elasticsearch keyword / hybrid search client |
| `elastic-transport` | 8.17.0 | Transport layer for the ES client |
| `faiss-cpu` (`faiss-gpu`) | ≥ 1.8 | Facebook AI similarity search — in-process vector index |
| `chromadb` | ≥ 0.5 | Chroma embedded vector database |
| `pinecone-client` | ≥ 4.0 | Pinecone managed vector DB client |
| `weaviate-client` | ≥ 4.5 | Weaviate vector DB client |
| `pgvector` | ≥ 0.2 | Python bindings for the PostgreSQL `pgvector` extension |
| `redis` | ≥ 5.0 | Redis client (vector + cache backend) |

### Relational / Spatial Database Drivers

Python drivers / ORMs for the relational + spatial servers listed under
[Databases & Developer Tooling](#databases--developer-tooling).

| Package | Version | Purpose |
|---|---|---|
| `SQLAlchemy` | ≥ 2.0.23 | SQL ORM / core toolkit (all relational backends) |
| `psycopg` (`psycopg[binary]`) | ≥ 3.1 | Modern PostgreSQL driver |
| `psycopg2-binary` | ≥ 2.9 | Legacy PostgreSQL driver (still required by some libs) |
| `asyncpg` | ≥ 0.29 | Async PostgreSQL driver |
| `GeoAlchemy2` | ≥ 0.14 | PostGIS / spatial types for SQLAlchemy |
| `Shapely` | ≥ 2.0 | Geometry operations |
| `geopandas` | ≥ 0.14 | Spatial DataFrames |
| `pyproj` | ≥ 3.6 | Coordinate-system transforms |
| `mysqlclient` | ≥ 2.2 | MySQL/MariaDB C driver |
| `PyMySQL` | ≥ 1.1 | Pure-Python MySQL driver |
| `mysql-connector-python` | ≥ 8.0 | Oracle's official MySQL driver |
| `pyodbc` | ≥ 5.0 | ODBC driver (SQL Server, etc.) |
| `sqlite-utils` | ≥ 3.36 | CLI + helpers over the stdlib `sqlite3` |
| `alembic` | ≥ 1.13 | SQLAlchemy schema migrations |

### Git / GitHub / Hub Integration

| Package | Version | Purpose |
|---|---|---|
| `GitPython` | ≥ 3.1 | Drive local git repositories from Python |
| `PyGithub` | ≥ 2.3 | GitHub REST API client (repos, issues, PRs) |
| `ghapi` | ≥ 1.0 | Lightweight, fully-typed GitHub API client |
| `gitdb` / `smmap` | bundled | Git object backends for GitPython |
| `huggingface-hub` | *resolved by `transformers`* | Hugging Face Hub client (models / datasets / Spaces) |
| `dvc` | ≥ 3.0 | Data / model version control (optional) |

### Document Ingestion / Parsing / OCR

| Package | Version | Purpose |
|---|---|---|
| `unstructured[all-docs]` | 0.17.2 | Universal document partitioning (PDF, DOCX, HTML, etc.) |
| `unstructured-client` | 0.32.3 | Hosted unstructured API client |
| `unstructured-inference` | 0.8.10 | Layout-detection models for `unstructured` |
| `PyMuPDF` (`fitz`) | 1.23.0 | Fast PDF text / image extraction |
| `pypdf` / `PyPDF2` | latest / 3.0.1 | PDF reading / splitting |
| `pdfminer.six` | 20221105 | Low-level PDF text extraction |
| `pdfplumber` | ≥ 0.7.0 | Table-aware PDF extraction |
| `pdf2image` | 1.17.0 | PDF → image rasterization (needs Poppler) |
| `pytesseract` | 0.3.10 | Tesseract OCR wrapper |
| `opencv-python` (`cv2`) | 4.8.0.74 | Computer vision / image preprocessing |
| `Pillow` | 10.0.0 | Image I/O and manipulation |
| `scikit-image` | bundled | Image processing algorithms |
| `imageio`, `tifffile`, `lazy_loader` | bundled | Image I/O backends for scikit-image |
| `ImageHash` | bundled | Perceptual image hashing (dedup / cache keys) |
| `PyWavelets` | ≥ 1.8.0 | Wavelet transforms (used by perceptual hashing) |
| `python-docx` | 0.8.11 | Word `.docx` reading |
| `python-pptx` | 0.6.21 | PowerPoint `.pptx` reading |
| `openpyxl` / `XlsxWriter` | 3.1.2 / bundled | Excel `.xlsx` read / write |
| `extract-msg` | 0.29.0 | Outlook `.msg` parsing |
| `python-magic` | 0.4.27 | File-type detection via libmagic |
| `beautifulsoup4` | 4.8.2 | HTML / XML parsing |
| `lxml` | bundled | Fast XML/HTML backend for BeautifulSoup |
| `markdown` | 3.7 | Markdown rendering |
| `pypandoc` / `pypandoc-binary` | 1.15 | Pandoc bridge (EPUB / RTF) |
| `jq` | 1.8.0 | JSON querying |
| `reportlab` | bundled | PDF generation |
| `wikipedia` | 1.4.0 | Wikipedia article fetching |
| `youtube-transcript-api` | 1.0.3 | YouTube transcript ingestion |
| `newspaper3k` | latest | News-article scraping / extraction |
| `chardet` / `charset-normalizer` | 5.2.0 / latest | Character-encoding detection |

### Neuroimaging (specialised analysis)

| Package | Version | Purpose |
|---|---|---|
| `nibabel` | bundled | Neuroimaging file formats (NIfTI, etc.) |
| `nilearn` | bundled | Machine learning for neuroimaging |
| `mne` | bundled | MEG / EEG signal analysis |

### Web / API Serving

| Package | Version | Purpose |
|---|---|---|
| `fastapi` | 0.104.1 | Async API framework |
| `fastapi-health` | 0.4.0 | Health-check endpoints |
| `uvicorn` | 0.22.0 | ASGI server |
| `gunicorn` | 23.0.0 | Production WSGI/ASGI process manager |
| `starlette` / `sse-starlette` / `starlette-session` | 0.46.1 / 2.2.1 / 0.4.3 | ASGI toolkit + server-sent events + sessions |
| `pydantic` | 2.5.2 | Data validation / settings |
| `python-multipart` | 0.0.6 | Multipart form / file uploads |
| `aiofiles` | 23.1.0 | Async file I/O |
| `aiohttp` | ≥ 3.8.6 | Async HTTP client/server |
| `httpx` | bundled | Async HTTP client |
| `jinja2` | 3.1.2 | Templating |
| `Secweb` | 1.18.1 | Security headers middleware |
| `msal` | 1.25.0 | Microsoft auth library |
| `SQLAlchemy` | ≥ 2.0.23 | SQL ORM (dashboard backend) |

### Utilities / Runtime Support

| Package | Version | Purpose |
|---|---|---|
| `python-dotenv` | 1.0.x | `.env` configuration loading |
| `tqdm` | 4.65.0 | Progress bars |
| `psutil` | 7.0.0 | System / process metrics |
| `requests` | 2.31.0 | HTTP client |
| `urllib3` | 1.26.16 | HTTP connection pooling |
| `tenacity`, `backoff` | bundled | Retry / backoff logic |
| `PyYAML` | 6.0.1 | YAML config |
| `protobuf` | 3.20.3 | Protocol Buffers |
| `cryptography` | bundled | Encryption primitives |
| `rich` / `typer` / `click` | bundled | CLI + rich terminal output |
| `tiktoken` | 0.4.0 | Token counting (also listed under NLP) |
| `wrapt` | 1.17.2 | Decorator / proxy helpers |
| `GPUtil` | ≥ 1.4 | GPU utilization / memory monitoring (benchmark + profiling harnesses) |
| `packaging` | bundled | Version parsing / requirement specifiers (build + runtime) |
| `decorator` | bundled | Function-decorator utilities (dependency of several libraries) |
| `setuptools-scm` | ≥ 8.0 | Derive package version from VCS tags (source/wheel builds, e.g. flash-attn) |

### Experiment Tracking & Visualization

| Package | Version | Purpose |
|---|---|---|
| `wandb` | ≥ 0.16 | Weights & Biases experiment tracking |
| `mlflow` | ≥ 2.12 | Experiment tracking + model registry |
| `tensorboard` | ≥ 2.15 | Training-curve / scalar / graph visualization |
| `trackio` | latest | Lightweight metrics logging + dashboards |

### Testing & Code Quality

| Package | Version | Purpose |
|---|---|---|
| `pytest` | 7.4.0 | Test framework |
| `pytest-cov` | 4.1.0 | Coverage plugin |
| `coverage` | bundled | Coverage measurement |
| `httpx` | bundled | Async test client for FastAPI |
| `black` | ≥ 23.0 | Opinionated code formatter (ML/AI training + benchmark scripts) |
| `flake8` | ≥ 6.0 | Linter (ML/AI training + benchmark scripts) |

---

## Vivere Web App (Next.js — digital wallet & card management)

Vivere is a modern, full-featured **digital-wallet and card-management** web app
built with **Next.js 16 / React 19 / TypeScript**. It covers user registration &
authentication (incl. MFA), physical/virtual card management, multi-currency
wallets, transfers, transaction history, statements, reporting, an ATM locator
(Google Maps), and a knowledge base. Backend communication is fully typed via
**OpenAPI 3.0** specs (`global.json`, `identity.json`, `onboarding.json`), state
is handled by React Query + Zustand, monitoring runs on **Sentry**, and CI/CD is
**Azure Pipelines + Docker**.

> The application code itself is **Node.js / TypeScript** — there is no Python in
> the running app today. The Python section below is a deliberately **broad,
> forward-looking IT allow-list**: it pre-approves every Python package family
> that Vivere's supporting work (API/OpenAPI tooling, backend-for-frontend
> microservices, data & reporting, statements/PDF generation, the knowledge
> base / RAG ingestion of the `Knowledge builders` PDFs, QR/card assets, the ATM
> geocoding, DevOps and security tooling) is likely to need now **or in the
> future** — so those implementations can proceed **without logging a new call
> with IT** each time.

### Prerequisites (Node / TypeScript — the running app)

| Tool | Version | Notes |
|---|---|---|
| [Node.js](https://nodejs.org/) | **≥ 24.0.0** | Enforced via `engines` in `package.json`; manage with `nvm`/`fnm` |
| [pnpm](https://pnpm.io/) | **10.x** (`pnpm@10.24.0`) | Pinned via `packageManager`; primary package manager |
| [Docker](https://www.docker.com/) | latest | `docker/{development,staging,production}` compose files |
| [Azure CLI](https://learn.microsoft.com/cli/azure/) | latest | Optional — Azure Pipelines / deployment |

### Getting Started (app)

```bash
cd fewebViviere_next
pnpm install
pnpm use:local        # copies .env.dev -> .env.local
pnpm generate-types   # regenerate types from the OpenAPI specs
pnpm dev              # http://localhost:3000
```

Key scripts (see `package.json`): `pnpm build:{dev,stg,prd}`, `pnpm start`,
`pnpm test` / `test:coverage` / `test:ci` (Jest), `pnpm lint`, `pnpm typecheck`,
`pnpm format`, and `pnpm docker-start:{dev,stg,prd}`.

---

## Python — Vivere Supporting & Future-Implementation Environment (IT allow-list)

This is an **end-to-end Python requirements / allow-list for IT**. Its purpose is
pre-approval and bulk installation so that any current or future Python work on
Vivere can proceed without raising an IT ticket.

> **How IT should read the versions below**
>
> - Versions are stated as **minimums or known-good ranges** (e.g. `≥ 1.2`,
>   `any 2.x`). Treat **every released version within the stated major**, plus
>   each package's **transitive dependencies**, as **pre-approved for install**.
> - Where a package already appears in the
>   [Python — Global Machine Learning & AI Environment](#python--global-machine-learning--ai-environment),
>   it is marked *(global)* — the global install satisfies it; it is listed again
>   here only so this allow-list is self-contained for the Vivere project.
> - Installs should be permitted from **PyPI** (and, where noted, vendor indexes
>   such as Azure / NVIDIA). Both `pip` and `uv`/`poetry`-based installs apply.
> - **Python runtime:** any **CPython 3.10 → latest 3.x** (matches the global env).

### Core runtime, build & environment management

| Package | Version | Purpose |
|---|---|---|
| `python` | 3.10 – latest 3.x | CPython interpreter *(global)* |
| `pip` | latest | Package installer *(global)* |
| `setuptools` / `wheel` / `build` | latest | Build backends for source/wheel installs |
| `pipx` | ≥ 1.4 | Install Python CLIs in isolated envs |
| `virtualenv` / `venv` (stdlib) | latest | Per-service virtual environments |
| `uv` | ≥ 0.4 | Fast resolver/installer (drop-in pip/venv replacement) |
| `poetry` | ≥ 1.8 | Dependency management & packaging (optional) |
| `pip-tools` | ≥ 7.0 | `pip-compile` lockfiles / reproducible installs |
| `hatch` / `hatchling` | latest | PEP 517 build/packaging (optional) |

### Python code quality, typing & supply-chain security

Standard gates for any Python service — frequently the packages most often
blocked by IT, so listed explicitly.

| Package | Version | Purpose |
|---|---|---|
| `black` | ≥ 24.0 | Opinionated code formatter |
| `ruff` | ≥ 0.6 | Fast linter + formatter (Flake8/isort/pyupgrade rules) |
| `isort` | ≥ 5.13 | Import ordering |
| `flake8` | ≥ 7.0 | Linter (plugin ecosystem) |
| `pylint` | ≥ 3.0 | Deep static analysis |
| `mypy` | ≥ 1.10 | Static type checking |
| `pyright` | ≥ 1.1 | Fast type checker (matches the TS-first codebase) |
| `bandit` | ≥ 1.7 | Security/SAST scanner for Python code |
| `safety` | ≥ 3.0 | Dependency CVE scanning |
| `pip-audit` | ≥ 2.7 | Audit installed packages for known vulns |
| `pre-commit` | ≥ 3.7 | Git pre-commit hook runner |
| `tox` / `nox` | ≥ 4.0 / ≥ 2024 | Matrix test/lint automation |
| `commitizen` | ≥ 3.0 | Conventional commits / version bumps |

### OpenAPI / schema / contract tooling

Vivere is OpenAPI-first (`global.json`, `identity.json`, `onboarding.json`).
These let Python validate, mock, fuzz, and generate models from those specs.

| Package | Version | Purpose |
|---|---|---|
| `openapi-spec-validator` | ≥ 0.7 | Validate OpenAPI 3.0/3.1 documents |
| `openapi-core` | ≥ 0.19 | Request/response validation against a spec |
| `prance` | ≥ 23.6 | Resolve/validate/bundle `$ref` OpenAPI specs |
| `datamodel-code-generator` | ≥ 0.25 | Generate Pydantic models from OpenAPI/JSON Schema |
| `openapi-python-client` | ≥ 0.21 | Generate typed Python API clients from a spec |
| `apispec` | ≥ 6.0 | Build OpenAPI specs from code |
| `jsonschema` | ≥ 4.21 | JSON Schema validation |
| `schemathesis` | ≥ 3.30 | Property-based / fuzz testing of API contracts |
| `pyyaml` | ≥ 6.0 | YAML spec/config parsing *(global)* |
| `orjson` / `ujson` | latest | Fast JSON (de)serialization *(global: orjson)* |

### Backend / API serving (backend-for-frontend & microservices)

For any Python BFF, webhook receiver, batch service, or internal API behind
Vivere.

| Package | Version | Purpose |
|---|---|---|
| `fastapi` | ≥ 0.104 | Async API framework *(global)* |
| `uvicorn[standard]` | ≥ 0.22 | ASGI server *(global)* |
| `gunicorn` | ≥ 21.2 | Process manager *(global)* |
| `hypercorn` | ≥ 0.16 | Alt ASGI/HTTP-2 server (optional) |
| `starlette` | ≥ 0.36 | ASGI toolkit *(global)* |
| `flask` | ≥ 3.0 | WSGI microframework (legacy/simple services) |
| `django` / `djangorestframework` | ≥ 5.0 / ≥ 3.15 | Full framework option (optional) |
| `strawberry-graphql` / `ariadne` | latest | GraphQL servers (optional) |
| `python-multipart` | ≥ 0.0.9 | Multipart/file uploads *(global)* |
| `slowapi` | ≥ 0.1.9 | Rate limiting for FastAPI/Starlette |
| `fastapi-pagination` | ≥ 0.12 | Pagination helpers |
| `gevent` / `greenlet` | latest | Async workers / concurrency |

### HTTP clients, resilience & API testing

| Package | Version | Purpose |
|---|---|---|
| `requests` | ≥ 2.31 | Sync HTTP client *(global)* |
| `httpx` | ≥ 0.27 | Sync/async HTTP client *(global)* |
| `aiohttp` | ≥ 3.9 | Async HTTP client/server *(global)* |
| `tenacity` / `backoff` | latest | Retry / backoff *(global)* |
| `pytest` | ≥ 7.4 | Test framework *(global)* |
| `pytest-cov` | ≥ 4.1 | Coverage plugin *(global)* |
| `pytest-asyncio` | ≥ 0.23 | Async test support |
| `pytest-mock` | ≥ 3.12 | Mock fixtures |
| `pytest-xdist` | ≥ 3.5 | Parallel test execution |
| `responses` / `respx` | latest | Mock `requests` / `httpx` calls |
| `tavern` | ≥ 2.9 | YAML-driven API integration tests |
| `locust` | ≥ 2.20 | Load / performance testing |
| `faker` | ≥ 24.0 | Synthetic test data |
| `hypothesis` | ≥ 6.100 | Property-based testing |
| `playwright` | ≥ 1.44 | Browser E2E (parity with the Node E2E suite) |
| `selenium` | ≥ 4.20 | Browser automation (optional) |

### Authentication, MFA, security & cryptography (fintech-grade)

Vivere uses JWT/JOSE (`jose` on the Node side), MFA, CSRF, and secure headers —
these mirror that on the Python side.

| Package | Version | Purpose |
|---|---|---|
| `cryptography` | ≥ 42.0 | Core crypto primitives *(global)* |
| `pyjwt` | ≥ 2.8 | JWT encode/decode |
| `python-jose[cryptography]` | ≥ 3.3 | JOSE / JWE / JWS (parity with Node `jose`) |
| `authlib` | ≥ 1.3 | OAuth2 / OIDC client & server |
| `msal` | ≥ 1.28 | Microsoft Entra ID auth *(global)* |
| `passlib[bcrypt]` | ≥ 1.7.4 | Password hashing |
| `bcrypt` | ≥ 4.1 | bcrypt backend |
| `argon2-cffi` | ≥ 23.1 | Argon2 password hashing |
| `pyotp` | ≥ 2.9 | TOTP/HOTP one-time passwords (MFA) |
| `qrcode[pil]` | ≥ 7.4 | MFA enrolment QR codes |
| `webauthn` | ≥ 2.0 | FIDO2 / passkeys (optional) |
| `itsdangerous` | ≥ 2.1 | Signed tokens / CSRF |
| `secweb` | ≥ 1.18 | Security headers middleware *(global)* |
| `pyopenssl` / `certifi` | latest | TLS plumbing / CA bundle |

### Configuration & secrets

| Package | Version | Purpose |
|---|---|---|
| `pydantic` | ≥ 2.5 | Data validation *(global)* |
| `pydantic-settings` | ≥ 2.2 | Typed env/settings management |
| `python-dotenv` | ≥ 1.0 | `.env` loading *(global)* |
| `dynaconf` | ≥ 3.2 | Multi-environment settings (dev/stg/prd parity) |
| `environs` | ≥ 11.0 | Env var parsing/casting |

### Azure SDK & cloud (matches Azure Pipelines deployment)

| Package | Version | Purpose |
|---|---|---|
| `azure-identity` | ≥ 1.16 | Entra ID / managed-identity credentials |
| `azure-keyvault-secrets` | ≥ 4.8 | Secrets from Azure Key Vault |
| `azure-storage-blob` | ≥ 12.19 | Blob storage (statements, exports, assets) |
| `azure-storage-queue` | ≥ 12.9 | Queue storage |
| `azure-servicebus` | ≥ 7.12 | Service Bus messaging |
| `azure-cosmos` | ≥ 4.6 | Cosmos DB |
| `azure-appconfiguration` | ≥ 1.5 | Centralized app configuration |
| `azure-monitor-opentelemetry` | ≥ 1.4 | App Insights / OpenTelemetry export |
| `azure-mgmt-*` | latest | Resource management SDKs (as needed) |
| `boto3` / `botocore` | ≥ 1.34 | AWS SDK (optional multi-cloud) *(global)* |

### Observability & logging (matches Sentry usage)

| Package | Version | Purpose |
|---|---|---|
| `sentry-sdk[fastapi]` | ≥ 2.0 | Error/perf monitoring (parity with `@sentry/nextjs`) |
| `opentelemetry-api` / `opentelemetry-sdk` | ≥ 1.25 | Tracing/metrics API + SDK |
| `opentelemetry-instrumentation-fastapi` | ≥ 0.46 | Auto-instrument FastAPI |
| `opentelemetry-exporter-otlp` | ≥ 1.25 | OTLP exporter |
| `prometheus-client` | ≥ 0.20 | Prometheus metrics endpoint |
| `structlog` | ≥ 24.1 | Structured logging |
| `loguru` | ≥ 0.7 | Ergonomic logging (optional) |

### Data, finance, currency, dates & locale (wallet/transactions)

| Package | Version | Purpose |
|---|---|---|
| `pandas` | ≥ 2.0 | DataFrames for reporting/exports *(global)* |
| `numpy` | ≥ 1.26 | Numerics *(global)* |
| `polars` | ≥ 0.20 | Fast DataFrames *(global)* |
| `python-dateutil` | ≥ 2.9 | Flexible date parsing |
| `pytz` / `tzdata` | latest | Time zones |
| `pendulum` / `arrow` | latest | Friendlier datetime handling (optional) |
| `babel` | ≥ 2.14 | Locale formatting (currency, dates, numbers) |
| `py-moneyed` | ≥ 3.0 | Money/currency types with correct rounding |
| `forex-python` / `currencyconverter` | latest | FX rates / conversion (multi-currency) |
| `phonenumbers` | ≥ 8.13 | Phone validation/formatting (registration/MFA SMS) |
| `email-validator` | ≥ 2.1 | Email validation |
| `iso4217` / `iso3166` | latest | Currency / country code reference data |

### Databases, ORM & migrations

The app ships a SQLite demo DB (`data/business-demo.db`, via `better-sqlite3`);
these cover SQLite plus likely future relational backends.

| Package | Version | Purpose |
|---|---|---|
| `SQLAlchemy` | ≥ 2.0 | ORM / core toolkit *(global)* |
| `alembic` | ≥ 1.13 | Schema migrations *(global)* |
| `sqlite-utils` | ≥ 3.36 | CLI + helpers over `sqlite3` *(global)* |
| `aiosqlite` | ≥ 0.20 | Async SQLite driver |
| `psycopg[binary]` | ≥ 3.1 | PostgreSQL driver *(global)* |
| `psycopg2-binary` | ≥ 2.9 | Legacy PostgreSQL driver *(global)* |
| `asyncpg` | ≥ 0.29 | Async PostgreSQL driver *(global)* |
| `pyodbc` | ≥ 5.0 | ODBC / SQL Server *(global)* |
| `redis` | ≥ 5.0 | Cache / sessions / queues *(global)* |
| `sqlmodel` | ≥ 0.0.16 | Pydantic + SQLAlchemy models (optional) |

### Messaging, scheduling & background tasks

| Package | Version | Purpose |
|---|---|---|
| `celery` | ≥ 5.3 | Distributed task queue |
| `kombu` | ≥ 5.3 | Messaging library (Celery transport) |
| `redis` | ≥ 5.0 | Broker/result backend *(global)* |
| `pika` | ≥ 1.3 | RabbitMQ (AMQP) client |
| `apscheduler` | ≥ 3.10 | In-process scheduling/cron |
| `dramatiq` | ≥ 1.16 | Lightweight task processing (alt to Celery) |

### Statements, reporting & document generation

Backs the **Statements** and **Reporting** features (PDF/Excel generation).

| Package | Version | Purpose |
|---|---|---|
| `reportlab` | ≥ 4.1 | Programmatic PDF generation *(global)* |
| `weasyprint` | ≥ 61.0 | HTML/CSS → PDF (styled statements) |
| `xhtml2pdf` | ≥ 0.2.16 | Simple HTML → PDF |
| `pdfkit` | ≥ 1.0 | wkhtmltopdf wrapper (optional) |
| `jinja2` | ≥ 3.1 | Templating for statements/emails *(global)* |
| `openpyxl` | ≥ 3.1 | Excel `.xlsx` read/write *(global)* |
| `xlsxwriter` | ≥ 3.2 | Fast `.xlsx` writing |
| `pandas` | ≥ 2.0 | Tabular report assembly *(global)* |
| `tabulate` | ≥ 0.9 | Plain-text/Markdown tables |
| `pypdf` | ≥ 4.2 | Merge/split/stamp PDFs *(global)* |
| `qrcode` / `segno` | latest | QR codes on statements/cards |
| `python-barcode` | ≥ 0.15 | Barcodes for cards/documents |
| `Pillow` | ≥ 10.0 | Image generation/manipulation *(global)* |

### Notifications (email / SMS / push)

| Package | Version | Purpose |
|---|---|---|
| `sendgrid` | ≥ 6.11 | Transactional email |
| `fastapi-mail` | ≥ 1.4 | SMTP email for FastAPI |
| `twilio` | ≥ 9.0 | SMS / WhatsApp (MFA OTP, alerts) |
| `firebase-admin` | ≥ 6.5 | Push notifications (FCM) |
| `jinja2` | ≥ 3.1 | Email templating *(global)* |

### Geospatial — ATM locator (Google Maps parity)

| Package | Version | Purpose |
|---|---|---|
| `googlemaps` | ≥ 4.10 | Google Maps Platform client (geocoding, places) |
| `geopy` | ≥ 2.4 | Geocoding + distance calculations |
| `shapely` | ≥ 2.0 | Geometry operations *(global)* |
| `geopandas` | ≥ 0.14 | Spatial DataFrames *(global)* |
| `pyproj` | ≥ 3.6 | Coordinate transforms *(global)* |
| `h3` | ≥ 4.0 | Hex-grid spatial indexing (ATM clustering) |

### Knowledge base / RAG — `Knowledge builders` ingestion

The `Knowledge builders/` folder holds product PDFs (Landing Page, Card Order,
Card Management, Statements, etc.) for an in-app knowledge base / assistant.
These ingest, chunk, embed, and retrieve that content. Most are satisfied by the
[global ML/AI environment](#python--global-machine-learning--ai-environment); a
generous allow-list is kept here so the knowledge base can be built without an IT
round-trip.

| Package | Version | Purpose |
|---|---|---|
| `langchain` (+ `-core`, `-community`, `-text-splitters`) | ≥ 0.3 | RAG orchestration *(global)* |
| `langchain-openai` / `langchain-anthropic` / `langchain-google-genai` | latest | LLM providers *(global)* |
| `llama-index` (+ `-core`) | ≥ 0.10 | Alt RAG framework *(global)* |
| `unstructured[all-docs]` | ≥ 0.17 | PDF/DOCX/HTML partitioning *(global)* |
| `pypdf` / `pdfplumber` / `PyMuPDF` | latest | PDF text/table extraction *(global)* |
| `pytesseract` + Tesseract | latest | OCR for scanned PDFs *(global)* |
| `sentence-transformers` | ≥ 2.2 | Embeddings *(global)* |
| `qdrant-client` | ≥ 1.13 | Vector store client *(global)* |
| `chromadb` | ≥ 0.5 | Embedded vector DB *(global)* |
| `faiss-cpu` | ≥ 1.8 | In-process vector index *(global)* |
| `tiktoken` | ≥ 0.7 | Token counting/budgeting *(global)* |
| `rapidfuzz` | latest | Fuzzy matching *(global)* |
| `openai` / `anthropic` | latest | LLM provider SDKs *(global)* |

### DevOps, containers & CI (Docker + Azure Pipelines parity)

| Package | Version | Purpose |
|---|---|---|
| `docker` | ≥ 7.0 | Docker Engine API client from Python |
| `python-on-whales` | ≥ 0.71 | Typed Docker/Compose CLI wrapper |
| `pyyaml` | ≥ 6.0 | Pipelines/compose YAML *(global)* |
| `jinja2` | ≥ 3.1 | Config/manifest templating *(global)* |
| `invoke` | ≥ 2.2 | Python task runner (Make-like) |
| `python-gitlab` / `PyGithub` | latest | CI/repo API automation *(global: PyGithub)* |
| `azure-devops` | ≥ 7.1 | Azure DevOps REST client |
| `rich` / `typer` / `click` | latest | CLI tooling / rich output *(global)* |
| `watchdog` | ≥ 4.0 | Filesystem watching (dev tooling) |

### Optional — payments & financial integrations

Pre-approved so future payment/card-network integrations need no new IT request.

| Package | Version | Purpose |
|---|---|---|
| `stripe` | ≥ 9.0 | Stripe payments API (optional) |
| `plaid-python` | ≥ 20.0 | Bank account linking / aggregation (optional) |
| `paypalrestsdk` / `paypal-server-sdk` | latest | PayPal integration (optional) |
| `cryptography` | ≥ 42.0 | PCI-adjacent crypto / tokenization *(global)* |

> **Install hint (bulk approval):** the entire list above can be captured in a
> single `requirements-vivere.txt` using the stated lower bounds (e.g.
> `fastapi>=0.104`, `pyjwt>=2.8`, …) and installed with
> `pip install -r requirements-vivere.txt`. Because each entry is approved for
> any version within its major plus transitive dependencies, routine upgrades
> (`pip install -U`) and new sub-packages from the same families do **not**
> require a new IT call.
