# Akanti API

The backend for **Akanti**, a personal finance app for Nigeria. It is an ASP.NET Core 8 Web API that stores income, expenses, budgets, debts, customers and invoices, powers financial reports, schedules debt reminders, and answers questions through an AI assistant.

The companion frontend lives in [Akanti-Frontend](https://github.com/PsalamDev/Akanti-Frontend).

---

## Contents

- [What it does](#what-it-does)
- [Architecture](#architecture)
- [Project layout](#project-layout)
- [Requirements](#requirements)
- [Running locally](#running-locally)
- [Configuration](#configuration)
- [Database](#database)
- [API overview](#api-overview)
- [AI assistant](#ai-assistant)
- [Debt reminders and notifications](#debt-reminders-and-notifications)
- [Email](#email)
- [Deployment](#deployment)
- [Troubleshooting](#troubleshooting)

---

## What it does

- **Authentication** — email + password sign-in, JWT bearer tokens, password reset by emailed code, change password.
- **Money tracking** — income and expense entries, categories, per-user isolation.
- **Budgets** — monthly/weekly/annual limits per category with threshold alerts.
- **Debts** — money lent and borrowed, partial payments, due dates, optional reminders.
- **Cash flow and reports** — monthly cash-flow series, profit-and-loss, period summaries.
- **Customers and invoices** — lightweight invoicing with line items, defaulting to NGN.
- **Notifications** — per-user inbox with unread counts, mark-read, mark-all-read and delete.
- **AI assistant** — chat sessions backed by a configurable model provider, grounded in the user's own figures, with a deterministic fallback when no model is configured.
- **Admin** — dashboard totals, user list and detail, activate/deactivate users, audit logs, email diagnostics.

## Architecture

```
Client (React / Vercel)
        │  HTTPS + JWT
        ▼
ASP.NET Core 8 Web API  ──►  PostgreSQL (EF Core / Npgsql)
        │
        ├──► AI provider (Groq / OpenAI / Gemini / OpenRouter / Anthropic)
        ├──► SMTP or Resend / Brevo / SendGrid
        └──► Background services
                ├── email queue
                └── debt reminder scheduler (daily)
```

Key pieces:

- **Stateless auth.** Every protected request carries a JWT. The signing key is configuration, not a database value.
- **Per-user data.** Every financial query is scoped by the `UserId` claim; there is no cross-user read path.
- **Background work.** Reminder emails and notifications are queued so request latency is unaffected. The reminder scheduler wakes once a day, checks debts whose due date is inside their reminder window, and records `LastRemindedAt` so it does not send twice.
- **Graceful AI degradation.** If no API key is configured or the provider fails, the assistant still replies using figures computed directly from the database, and flags that the answer came from records rather than the model.

## Project layout

```
Akanti-Backend/
├── Configuration/     Connection-string resolver and options helpers
├── Controllers/       HTTP endpoints (one per resource)
├── Data/              ApplicationDbContext
├── DTOs/              Request/response shapes, grouped by feature
├── Exceptions/        Domain exceptions and handlers
├── Interfaces/        Service contracts
├── Middleware/        Error handling, request plumbing
├── Models/            EF Core entities
├── Properties/        launchSettings.json (local ports)
├── Services/          Business logic, AI, email, reminders
├── Program.cs         Composition root
├── Dockerfile         Render image
└── appsettings.json   Committed defaults (no secrets)
```

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- PostgreSQL 14+ (local or hosted, e.g. Neon)
- Optional: a Groq API key for the AI assistant
- Optional: SMTP credentials or an email provider key

## Running locally

```bash
git clone https://github.com/PsalamDev/Akanti-Backend.git
cd Akanti-Backend
dotnet restore
dotnet run
```

The API listens on **http://localhost:5145** (from `Properties/launchSettings.json`). Swagger UI is available at `/swagger` in Development.

### Local settings

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

Run with the Development environment so that file is loaded:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run
```

> The API refuses to start without a `Jwt:Key`. That is deliberate.

## Configuration

Every setting can be supplied as an environment variable using double underscores (`Section__Key`).

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

Precedence for the database: `ConnectionStrings__DefaultConnection` wins outright; otherwise the committed string is adjusted by any `Database__*` values present.

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

## AI assistant

- Provider and model are configuration, not code. Set `Ai__Provider`, `Ai__Model` and `Ai__ApiKey`.
- The assistant receives **aggregates only** — totals, category breakdowns, budget status and debt summaries — never individual transactions.
- Answers are constrained to figures present in that snapshot; a guard rejects invented amounts and percentages.
- Replies are plain text so the chat window renders them exactly as written.
- If the provider errors or the key is missing, the service returns a deterministic answer from the same snapshot and marks it as a fallback. `/api/ai/status` reports the active provider.
- Rate limiting applies per user (`30` requests per `10` minutes).

## Debt reminders and notifications

- `POST /api/debt/{id}/remind` sends a reminder for a `Lent` debt: an email to the lender, an email to the debtor (when an address is present), an in-app notification to the lender, and — if the debtor's address belongs to a registered, active Akanti account — an in-app notification to the debtor as well.
- A background service performs the same check once a day for debts inside their reminder window, skipping `Paid` debts and throttling repeats.
- Emails are queued in the background; a slow mail server does not slow the API response.
- Nothing new is exposed to the debtor: the lender already chose to email them the same description, amount and due date.

## Email

Two delivery modes:

1. **HTTP provider** — set `Email__Provider` to `resend`, `brevo` or `sendgrid` and provide `Email__ApiKey`.
2. **SMTP** — leave `Email__Provider` empty and fill the `Smtp__*` settings.

In Development, emails go to the configured SMTP account. If email is not configured, reminder notifications still appear in-app.

## Deployment

The repository ships a `Dockerfile` and a `.dockerignore` that excludes local secrets.

**Render (Docker web service)**

1. Create a Web Service from this repository, choose **Docker**.
2. Set the build context to `Akanti-Backend/` (or point the Dockerfile path at it).
3. Add the environment variables from [Configuration](#configuration). At minimum: `ConnectionStrings__DefaultConnection`, `Jwt__Key`, `FRONTEND_URL`, `Ai__ApiKey`, `Ai__Provider=groq`, `Ai__Model=openai/gpt-oss-120b`.
4. Deploy. The container binds to `$PORT` (falling back to 8080) and runs in Production.

On first boot the schema is created automatically. A free Render instance may cold-start after idling; the first request can take up to a minute.

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| `connection refused` to Postgres | Local Postgres listens on a different port — set `Database__Port` |
| API exits immediately with a JWT error | `Jwt__Key` is missing |
| `401` on every call | Missing/expired token, or the key changed and old tokens no longer verify |
| AI replies always come from records | `Ai__ApiKey` missing, provider unreachable, or the configured model is retired |
| `429` | Rate limit hit for auth or AI endpoints |
| Email never arrives | Provider key invalid, sender not verified, or SMTP blocked |

Check `/api/ai/status` for AI configuration, `/swagger` in Development for the live route list, and the application logs for the resolved database source at startup (the password is never logged).
