# Akanti

Akanti is a personal finance application built for Nigeria. It helps people record income and expenses, set budgets, track money lent and borrowed, see where their money goes, and ask an AI assistant for guidance grounded in their own figures.

The project is made of two applications that deploy together:

| Part | Stack | Host |
| --- | --- | --- |
| API | ASP.NET Core 8, PostgreSQL, JWT | Render (Docker) |
| Web app | React 18, Vite, Tailwind CSS | Vercel |

This is the general README for the whole project. It lives in the backend repository; the frontend is its own repository.

- Backend: <https://github.com/PsalamDev/Akanti-Backend>
- Frontend: <https://github.com/PsalamDev/Akanti-Frontend>
- Live app: <https://akanti-finance.vercel.app>
- Live API: <https://akanti-backend.onrender.com>

---

## Contents

- [What it does](#what-it-does)
- [Architecture](#architecture)
- [Project layout](#project-layout)
- [Requirements](#requirements)
- [Quick start](#quick-start)
- [Configuration](#configuration)
- [Database](#database)
- [API overview](#api-overview)
- [How it works](#how-it-works)
- [Design](#design)
- [Deployment](#deployment)
- [Troubleshooting](#troubleshooting)

---

## What it does

- **Authentication** — email and password sign-in with JWT, password reset by emailed code, change password.
- **Money tracking** — income and expenses with categories, dates and notes.
- **Budgets** — limits per category with progress and threshold alerts.
- **Debts** — money lent and borrowed, partial payments, due dates and reminders.
- **Cash flow and reports** — monthly trends, profit and loss, and period summaries.
- **Exports** — reports download as CSV or PDF for the period currently in view.
- **Notifications** — a per-user inbox with an unread badge in the header.
- **Ask Akanti** — an AI assistant that answers from aggregated figures, with a deterministic fallback when no model is available.
- **Admin panel** — platform totals, user management, activate/deactivate, audit logs.
- **Naira by default** — every amount is displayed in Nigerian Naira (₦).

## Architecture

```
┌─────────────────────────┐        ┌──────────────────────────┐
│  React SPA (Vercel)     │  /api  │  ASP.NET Core 8 (Render) │
│  React Router, Tailwind │ ─────► │  JWT, EF Core, services  │
└─────────────────────────┘        └────────────┬─────────────┘
                                                │
                        ┌───────────────────────┼───────────────────────┐
                        ▼                       ▼                       ▼
                 PostgreSQL              AI provider            Email provider
                 (Neon / local)      (Groq by default)     (SMTP / Resend / Brevo)
```

- The frontend is a static SPA. In production, Vercel rewrites `/api/*` to the Render backend, so the browser sees a single origin and no CORS preflight is needed.
- The backend is stateless apart from PostgreSQL; every protected request carries a JWT.
- Reminder emails and notifications are queued in the background. A daily scheduler handles automatic reminders.
- The AI assistant receives aggregates only — never individual transactions — and its answers are checked against those figures.

## Project layout

```
Akanti-Backend/
├── Configuration/     Connection-string resolver and options helpers
├── Controllers/       One controller per resource
├── Data/              ApplicationDbContext
├── DTOs/              Request and response shapes, grouped by feature
├── Exceptions/        Domain exceptions and handlers
├── Interfaces/        Service contracts
├── Middleware/        Error handling and request plumbing
├── Models/            EF Core entities
├── Properties/        launchSettings.json (local ports)
├── Services/          Business logic, AI, email, reminders
├── Program.cs         Composition root
├── Dockerfile         Render image
└── appsettings.json   Committed defaults (no secrets)

Akanti-Frontend/
├── public/            Logo, used also as the loading indicator
├── src/
│   ├── components/    Header, sidebars, modals, floating assistant
│   │   └── ui/        Button, Card, Badge, Spinner, Loading
│   ├── context/       AuthContext, ThemeContext
│   ├── layouts/       DashboardLayout, AdminLayout
│   ├── pages/         One component per route
│   ├── services/      Axios client and endpoint groups
│   ├── App.jsx        Routes, lazy loading, guards
│   └── index.css      Tailwind layers and CSS variables
├── vercel.json        SPA fallback and /api proxy
├── tailwind.config.js Brand colours
└── vite.config.js     Dev server and proxy
```

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js](https://nodejs.org/) 18 or newer
- PostgreSQL 14+ (local or hosted, e.g. Neon)
- Optional: a Groq API key for the AI assistant
- Optional: SMTP credentials or an email provider key

## Quick start

Two terminals. Start the API first; the frontend proxies to it.

### Backend

```powershell
cd Akanti-Backend
dotnet restore
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run
```

Listens on **http://localhost:5145**. Swagger UI is at `/swagger` in Development.

### Frontend

```powershell
cd Akanti-Frontend
npm install
npm run dev
```

Opens on **http://localhost:5180** and proxies `/api` to `http://localhost:5145`. Both ports are fixed in the frontend's `vite.config.js`.

### Local backend settings

Do not put secrets in `appsettings.json`. Create `appsettings.Development.json` (already gitignored):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=AkantiDb;Username=postgres;Password=postgres;"
  },
  "Database": {
    "Port": "5433"
  },
  "Jwt": {
    "Key": "a-long-random-string-at-least-32-characters"
  },
  "AdminPassword": "choose-a-strong-password",
  "Ai": {
    "Provider": "groq",
    "Model": "openai/gpt-oss-120b",
    "ApiKey": "gsk_..."
  }
}
```

> The API refuses to start without a `Jwt:Key`. That is deliberate.

The frontend needs no environment variables in development or on Vercel, because it uses the Vite proxy locally and the `vercel.json` rewrite in production. If the backend URL changes, update both the proxy target and the rewrite destination.

## Configuration

Every backend setting can be supplied as an environment variable using double underscores (`Section__Key`).

| Variable | Purpose | Default |
| --- | --- | --- |
| `ConnectionStrings__DefaultConnection` | Full PostgreSQL connection string | localhost:5432/AkantiDb |
| `Database__Host` | Override just the host | from connection string |
| `Database__Port` | Override just the port | from connection string |
| `Database__Name` | Override just the database name | from connection string |
| `Database__Username` | Override just the user | from connection string |
| `Database__Password` | Override just the password | from connection string |
| `Jwt__Key` | JWT signing key (**required**) | — |
| `Jwt__Issuer` | Token issuer | `AkantiAPI` |
| `Jwt__Audience` | Token audience | `AkantiApp` |
| `Jwt__ExpirationHours` | Token lifetime | `24` |
| `AdminEmail` | Admin account seeded at startup | `admin@akanti.com` |
| `AdminPassword` | Password for the seeded admin | — |
| `Ai__Provider` | `groq`, `openai`, `gemini`, `openrouter`, `anthropic` | `groq` |
| `Ai__Model` | Model name | `openai/gpt-oss-120b` |
| `Ai__ApiKey` | Provider key | — |
| `Ai__BaseUrl` | Custom endpoint | provider default |
| `Ai__Enabled` | Turn the assistant off | `true` |
| `Email__Provider` | `resend`, `brevo`, `sendgrid`, or empty for SMTP | empty |
| `Email__ApiKey` | Provider API key | — |
| `Email__FromEmail` | Sender address | — |
| `Email__FromName` | Sender display name | `Akanti` |
| `Smtp__Host` / `Smtp__Port` | SMTP server | `smtp.gmail.com:465` |
| `Smtp__Username` / `Smtp__Password` | SMTP credentials | — |
| `Smtp__FromEmail` / `Smtp__FromName` | SMTP sender | — |
| `FRONTEND_URL` | Allowed CORS origin(s), comma or semicolon separated | `http://localhost:5180` |
| `ASPNETCORE_ENVIRONMENT` | `Development` or `Production` | `Production` in Docker |
| `PORT` | HTTP port injected by the platform | `8080` |

Database precedence: `ConnectionStrings__DefaultConnection` wins outright; otherwise the committed string is adjusted by any `Database__*` values present.

## Database

- Provider: **PostgreSQL** via `Npgsql.EntityFrameworkCore.PostgreSQL`.
- Schema: created automatically on startup with `EnsureCreated()`. There is no migration step to run.
- The committed connection string is a safe fallback for a stock local Postgres. Most local installs differ in port — set `Database__Port` rather than editing the committed file.
- Passwords are hashed before storage. Nothing sensitive is written to logs.

## API overview

All routes are under `/api`. Except for registration, login, password reset and `ai/status`, endpoints require `Authorization: Bearer <token>`.

### Auth — `/api/auth`

| Method | Route | Description |
| --- | --- | --- |
| POST | `/register` | Create an account |
| POST | `/login` | Exchange credentials for a JWT |
| POST | `/verify-email` | Confirm an emailed code |
| POST | `/resend-code` | Send a new verification code |
| GET | `/me` | Current user profile |
| POST | `/forgot-password` | Start password reset |
| POST | `/verify-reset-code` | Check a reset code |
| POST | `/reset-password` | Set a new password |
| POST | `/change-password` | Change password while signed in |

### Financial data

| Resource | Routes |
| --- | --- |
| Income | `GET/POST /api/income`, `GET/PUT/DELETE /api/income/{id}`, `GET /api/income/total` |
| Expenses | `GET/POST /api/expense`, `GET/PUT/DELETE /api/expense/{id}`, `GET /api/expense/total` |
| Budgets | `GET/POST /api/budget`, `GET/PUT/DELETE /api/budget/{id}`, `GET /api/budget/alerts` |
| Debts | `GET/POST /api/debt`, `GET/PUT/DELETE /api/debt/{id}`, `POST /api/debt/{id}/payment`, `GET /api/debt/upcoming`, `POST /api/debt/{id}/remind` |
| Categories | `GET/POST /api/category`, `GET /api/category/{id}` |
| Customers | `GET/POST /api/customers`, `GET/PUT/DELETE /api/customers/{id}` |
| Invoices | `GET/POST /api/invoices`, `GET/PUT/DELETE /api/invoices/{id}`, `GET /api/invoices/summary` |

### Insight and reporting

| Method | Route | Description |
| --- | --- | --- |
| GET | `/api/dashboard` | Dashboard aggregates |
| GET | `/api/cashflow/monthly` | Monthly income vs expenses |
| GET | `/api/report/profit-loss` | Profit and loss for a period |

### Notifications — `/api/notification`

| Method | Route | Description |
| --- | --- | --- |
| GET | `/` | List notifications (`?unreadOnly=true`) |
| GET | `/unread-count` | Unread badge count |
| PUT | `/{id}/read` | Mark one as read |
| PUT | `/read-all` | Mark all as read |
| DELETE | `/{id}` | Delete one |

### AI — `/api/ai`

| Method | Route | Description |
| --- | --- | --- |
| GET | `/status` | Provider, model and whether a key is configured (anonymous) |
| GET/POST | `/chat/sessions` | List or create conversations |
| GET/POST | `/chat/sessions/{id}/messages` | Read or send messages |
| DELETE | `/chat/sessions/{id}` | Delete a conversation |

### Admin — `/api/admin` (admin role required)

| Method | Route | Description |
| --- | --- | --- |
| GET | `/dashboard` | Platform-wide totals |
| GET | `/users` | Paginated user list |
| GET | `/users/{id}` | User detail |
| GET | `/users/{id}/totals` | A user's money totals |
| PUT | `/users/{id}/toggle-active` | Activate or deactivate |
| GET | `/audit-logs` | Recent audit events |
| POST | `/test-email` | Send a diagnostic email |

## How it works

### Authentication

The frontend stores the JWT in `localStorage` and exposes it through `AuthContext`.

- `ProtectedRoute` renders a page only when a token exists; otherwise it redirects to `/login`.
- `PublicRoute` sends signed-in users to `/dashboard`.
- `AdminRoute` additionally checks `user.isAdmin`.
- An Axios interceptor attaches `Authorization: Bearer <token>` to every request; a `401` clears the session.
- Logging out asks for confirmation before clearing the token.

### Data flow

Pages own their data. On mount they call the relevant group in `src/services/api.js` (`incomeAPI`, `expenseAPI`, `budgetAPI`, `debtAPI`, `notificationAPI`, `aiAPI`, `adminAPI`), keep the result in local state, and re-fetch after a mutation. There is no global store; the trade-off is simplicity and predictable refreshes.

### Reports and exports

The reports page groups transactions by the selected tab:

- **Summary** — totals for all time
- **Daily / Weekly / Monthly / Annual** — grouped trends with a bar chart

The **Download Report** button offers only two choices: **CSV** and **PDF**. The export uses whichever tab is active, so the period is chosen on the page and only the format is chosen in the menu.

- **CSV** — a spreadsheet-friendly file with `Income (NGN)` and `Expenses (NGN)` columns; values stay numeric.
- **PDF** — generated in the browser with `html2pdf.js` and saved directly as `akanti-<period>-report.pdf`. No print dialog is involved.

### Notifications

The header bell polls `/api/notification/unread-count` every 30 seconds and listens for a local `notifications-updated` event, so marking an item read updates the badge immediately. The notifications page supports mark-one, mark-all and delete.

Debt reminders are created on the server. If the person who owes money owns an Akanti account, they receive the reminder in this same bell.

### Debt reminders

`POST /api/debt/{id}/remind` sends a reminder for a `Lent` debt:

- an email to the lender,
- an email to the debtor when an address is present,
- an in-app notification to the lender,
- and, if the debtor's address belongs to a registered active account, an in-app notification to the debtor.

A background service performs the same check once a day for debts inside their reminder window, skipping `Paid` debts and throttling repeats. Emails are queued so a slow mail server does not slow the API response. Nothing new is exposed to the debtor: the lender already chose to email them the same description, amount and due date.

### Ask Akanti

`FloatingAssistant` renders a launcher at the bottom-right of signed-in pages and links to the full-page assistant. It is deliberately not in the sidebar.

- Provider and model are configuration, not code: `Ai__Provider`, `Ai__Model`, `Ai__ApiKey`.
- The assistant receives **aggregates only** — totals, category breakdowns, budget status and debt summaries — never individual transactions.
- Answers are constrained to figures present in the snapshot; a guard rejects invented amounts and percentages.
- Replies are plain text so the chat window renders them exactly as written.
- If the provider errors or the key is missing, the service returns a deterministic answer from the same snapshot and marks it as a fallback. `/api/ai/status` reports the active provider.
- Rate limiting applies per user (30 requests per 10 minutes).

### Admin

Admins see an **Admin Panel** entry in the sidebar with its own layout:

- `/admin` — platform totals
- `/admin/users` — search, paginate, activate/deactivate
- `/admin/users/:id` — per-user totals
- `/admin/audit-logs` — recent events

Non-admins are redirected away.

### Email

Two delivery modes:

1. **HTTP provider** — set `Email__Provider` to `resend`, `brevo` or `sendgrid` and provide `Email__ApiKey`.
2. **SMTP** — leave `Email__Provider` empty and fill the `Smtp__*` settings.

If email is not configured, reminder notifications still appear in-app.

## Design

- **Background:** `#071A17` dark surface for the signature look.
- **Accent:** `#10B981` green, exposed as `akanti.accent` in Tailwind and `--akanti-accent` in CSS.
- Light and dark modes are supported; the toggle lives in the header.
- Page loading uses the Akanti logo with a gentle pulse rather than a generic spinner.
- All money is shown in **Nigerian Naira (₦)** with thousands separators.

## Deployment

### Backend — Render (Docker)

The repository ships a `Dockerfile` and a `.dockerignore` that excludes local secrets.

1. Create a Web Service from the backend repository and choose **Docker**.
2. Set the build context to `Akanti-Backend/` (or point the Dockerfile path at it).
3. Add the environment variables from [Configuration](#configuration). At minimum:
   - `ConnectionStrings__DefaultConnection`
   - `Jwt__Key`
   - `FRONTEND_URL`
   - `Ai__ApiKey`
   - `Ai__Provider=groq`
   - `Ai__Model=openai/gpt-oss-120b`
4. Deploy. The container binds to `$PORT` (falling back to 8080) and runs in Production.

On first boot the schema is created automatically. A free Render instance may cold-start after idling; the first request can take up to a minute.

### Frontend — Vercel

1. Import the frontend repository into Vercel.
2. Set the project root to `Akanti-Frontend/`.
3. Framework preset: **Vite**. Build command `npm run build`, output `dist`.
4. Deploy.

`vercel.json` provides:

```json
{
  "rewrites": [
    { "source": "/api/(.*)", "destination": "https://akanti-backend.onrender.com/api/$1" },
    { "source": "/(.*)", "destination": "/index.html" }
  ]
}
```

The first rule proxies API calls through the same origin, avoiding CORS. The second makes client-side routes survive a refresh. If the backend URL changes, update the first rule.

### Smoke test

From the project root, `verify-deploy.ps1` checks the backend build, frontend asset, signup and login, email diagnostics, and the AI assistant:

```powershell
powershell -ExecutionPolicy Bypass -File .\verify-deploy.ps1
```

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| `connection refused` to Postgres | Local Postgres listens on a different port — set `Database__Port` |
| API exits immediately with a JWT error | `Jwt__Key` is missing |
| `401` on every call | Missing/expired token, or the backend key changed |
| `429` | Rate limit hit for auth or AI endpoints |
| AI replies always come from records | `Ai__ApiKey` missing, provider unreachable, or the model is retired |
| Email never arrives | Provider key invalid, sender not verified, or SMTP blocked |
| Blank page with 404s on refresh | SPA rewrite missing — check `vercel.json` |
| Frontend API calls fail locally | Backend not running on port 5145, or port mismatch |
| Charts render empty | No transactions in the selected period |
| PDF button downloads nothing | Browser blocked the automatic download, or there is no data to export |
| Notifications badge stuck | Backend unreachable, or token expired |

Check `/api/ai/status` for AI configuration, `/swagger` in Development for the live route list, and the application logs for the resolved database source at startup (the password is never logged).
