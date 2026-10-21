# Database schema deployment

This app uses `EnsureCreated()` rather than EF migrations. That creates the whole schema
when the database is empty, but it **does nothing when tables already exist** — so a new
table added to the model is *not* created on an existing database. Those changes have to
be applied deliberately.

## First-time setup (empty database)

Nothing to do. The API creates everything on first startup.

## Adding tables to an existing database

Run the relevant script from this folder against the target database:

```
psql "Host=...;Port=5432;Database=...;Username=...;Password=..." -f deploy/sql/002_paypal_invoicing.sql
```

| Script | Adds |
|---|---|
| `002_paypal_invoicing.sql` | `Customers`, `Invoices`, `InvoiceItems`, `PayPalWebhookEvents`, `InvoiceDrafts` |

Each script is written to be run once. It wraps its work in a transaction; if it reports
that a relation already exists, the change is already applied.

## Why not migrations?

`EnsureCreated()` and EF migrations are mutually exclusive — you cannot switch a database
that was created with `EnsureCreated()` to migrations without dropping it or stamping a
baseline. Switching to migrations is a reasonable future step, but it is a deliberate
migration project, not a drop-in change. Until then, schema additions ship as scripts
here **and** are applied to every environment (local, Neon) at the same time the model
changes.
