# AGENTS.md --- Digital Album Web (MhX Solutions)

This file contains the project-wide instructions for AI coding agents
(including OpenCode). Read and follow it before inspecting or changing
code.

## 1. Project Overview

Digital Album Web is a web application that allows a photographer to
create private digital photo albums for customers. A customer can access
an album only with its unique link and valid access code. Customers can
download the complete album as a single ZIP file.

The MVP is intended for one partner photographer. Multi-photographer
support, offline/PWA functionality, PDF export, a template editor,
customer photo selection, and online payments are out of scope unless
explicitly approved.

Product requirements and user journeys are defined in: - `docs/PRD.md` -
`docs/DESIGN-FLOW.md`

Read both documents before implementing a major feature. If a
requirement is unclear or conflicts with these documents, ask before
making a consequential product or architecture decision.

## 2. Architecture and Technology Stack

The application uses a **Modular Monolith** architecture. It is one
product and one backend deployment unit, organized into modules with
clear responsibilities and dependency boundaries. Do not introduce
microservices for the MVP.

### Frontend

-   React with TypeScript.
-   Vite for development and production builds.
-   React is responsible for the photographer dashboard and
    customer-facing album experience.
-   Use a consistent component structure and keep API communication in
    dedicated service/client modules.
-   Do not use Razor Pages or MVC views for the application UI.

### Backend

-   ASP.NET Core Web API using C#.
-   Prefer .NET 10 LTS for a new implementation. If the repository is
    already pinned to another supported .NET version, do not upgrade it
    without discussing the change first.
-   Use REST-style endpoints. Minimal APIs or Controllers may be used
    consistently; do not mix styles without a clear reason.
-   The backend owns authentication, authorization, business rules,
    validation, persistence, photo processing/storage coordination, and
    ZIP downloads.

### Data and infrastructure

-   Database: PostgreSQL.
-   ORM: EF Entity Core.
-   File storage: private S3-compatible object storage; Cloudflare R2 is
    the proposed provider.
-   Access object storage through an appropriate .NET SDK/client. Keep
    provider-specific code behind an infrastructure abstraction.
-   Logging: Serilog to console and file.
-   Tests: xUnit; use `WebApplicationFactory` for API integration tests.

### Authentication

-   Photographer: ASP.NET Core Identity using email and password.
-   Customer: no account. Customer access uses a unique album link/token
    and a separate access code.
-   Never treat possession of the link alone as sufficient
    authorization.

### Monolith deployment

-   Keep frontend and backend in the same repository.
-   The React app may be built as static assets and served by ASP.NET
    Core for a single-deployment setup.
-   A separately hosted React frontend is possible later, but do not
    introduce separate deployments, services, queues, or infrastructure
    for the MVP without approval.
-   If frontend and API use different origins in development or
    deployment, configure CORS and cookie/security settings
    deliberately.

## 3. Suggested Repository Structure

Adapt to the existing repository rather than moving files unnecessarily.

``` text
DigitalAlbum/
├── AGENTS.md
├── DigitalAlbum.sln
├── docs/
│   ├── PRD.md
│   └── DESIGN-FLOW.md
├── src/
│   ├── AlbumApp.Web/                 # ASP.NET Core API and optional static-file hosting
│   │   ├── Features/
│   │   │   ├── Authentication/
│   │   │   ├── Albums/
│   │   │   ├── Photos/
│   │   │   ├── AlbumAccess/
│   │   │   └── Downloads/
│   │   └── Program.cs
│   ├── AlbumApp.Core/                # Domain entities, rules, abstractions
│   ├── AlbumApp.Infrastructure/      # EF Core, storage, Identity, external adapters
│   └── album-app.client/             # React + TypeScript + Vite
│       ├── src/
│       │   ├── app/
│       │   ├── components/
│       │   ├── features/
│       │   ├── pages/
│       │   ├── services/
│       │   └── styles/
│       └── package.json
└── tests/
    ├── AlbumApp.Core.Tests/
    └── AlbumApp.Web.Tests/
```

### Dependency rules

-   `AlbumApp.Core` must not reference `AlbumApp.Infrastructure`,
    `AlbumApp.Web`, or frontend code.
-   `AlbumApp.Infrastructure` may reference `AlbumApp.Core`.
-   `AlbumApp.Web` may reference `AlbumApp.Core` and
    `AlbumApp.Infrastructure`.
-   React communicates with the backend through HTTP APIs; it must not
    access the database or object storage directly.
-   Keep business rules out of endpoint handlers and UI components. Put
    domain rules in Core and application orchestration in an appropriate
    backend feature/service layer.

## 4. Domain Model

The initial domain includes:

-   **Photographer:** owner of the dashboard account. The MVP has one
    partner photographer, but the model should associate albums with a
    photographer to avoid hard-coding ownership assumptions.
-   **Album:** belongs to a photographer. Includes customer name, title,
    session date, selected template, `CreatedAt`, `ExpiresAt`, and
    `Status` (`Draft`, `Active`, `Expired`, `Archived`).
-   **AlbumPhoto:** a photo belonging to an album. Includes `StorageKey`
    (original object key), `DisplayKey` (display-sized object key),
    `Order`, and file metadata/size.
-   **AlbumAccess:** customer access credentials. Includes a
    cryptographically random, hard-to-guess token and an
    `AccessCodeHash`. Never store the access code in plain text.
-   **Template:** predefined presentation/template data, separate from
    photos so a template can be reused by multiple albums.

An album is active for one year from `CreatedAt`. Calculate `ExpiresAt`
when the album is created. It must not change except through an
explicitly approved renewal process.

## 5. Mandatory Business Rules

1.  **Album access must be validated through `AlbumAccess`.** The
    URL/token alone is not sufficient. The customer must also provide
    the correct access code.
2.  **Hash access codes.** Use a suitable password-hashing algorithm
    such as BCrypt or Argon2. Never store or log plain-text access
    codes.
3.  **Rate-limit failed access attempts.** After repeated failures,
    temporarily block or throttle attempts from the relevant source.
    Avoid revealing whether a token or code was the incorrect part.
4.  **Keep the storage bucket private.** Never expose public object
    URLs. Display files only through short-lived signed URLs or an
    authenticated server-side proxy.
5.  **Never display original files in the album UI.** Customer-facing
    album pages use `DisplayKey` derivatives only. Original files are
    reserved for ZIP downloads.
6.  **Expired albums are inaccessible to customers.** Show a clear
    expired-album message without exposing protected photo content.
7.  **Stream ZIP downloads.** Do not load the entire album into
    application memory before sending the ZIP response.
8.  **Generate opaque object names.** Upload objects using generated
    identifiers (for example, GUIDs), not photographer-supplied
    filenames.
9.  **Enforce ownership on every photographer operation.** A
    photographer may only view or modify albums they own.
10. **Validate uploads.** Enforce allowed file types, maximum file size,
    and reasonable upload limits. Do not trust the supplied filename or
    MIME type alone.
11. **Use anti-forgery protections where cookie-authenticated
    state-changing requests require them.**
12. **Do not permanently delete albums or photos without explicit
    confirmation.**

## 6. Security Requirements

-   Do not commit secrets, credentials, access codes, signing keys, or
    connection strings.
-   Use `dotnet user-secrets` for local .NET development and environment
    variables or a managed secret store in production.
-   Never log access tokens, access codes, passwords, signed URLs, or
    credentials.
-   Use HTTPS outside local development.
-   Validate and normalize all user input on the server.
-   Apply authentication and authorization on the server; hiding a UI
    control is not authorization.
-   Use secure, HttpOnly, appropriately SameSite cookies if cookie-based
    authentication is selected. Define CSRF protections for
    state-changing requests.
-   Apply rate limiting to login, access-code verification, and other
    abuse-prone endpoints.
-   Keep storage private and use short-lived access URLs where required.
-   Avoid leaking whether a customer token exists during failed access
    attempts.
-   Set appropriate security headers and restrict CORS to known frontend
    origins.
-   Do not expose original object keys or internal storage details to
    customers.

## 7. API and Frontend Conventions

-   Keep API contracts explicit and consistent. Document endpoints and
    request/response models.
-   Use DTOs rather than exposing EF Core entities directly.
-   Return appropriate HTTP status codes and consistent error responses.
-   Use `async`/`await` for I/O operations.
-   Use dependency injection; do not manually instantiate application
    services inside other classes.
-   Use English for class, method, property, component, and variable
    names. Comments may be in Indonesian.
-   Prefer one class per file in .NET.
-   Keep React components focused. Separate page composition, reusable
    components, feature logic, and API calls.
-   Do not put business rules in React; the backend is authoritative.
-   Do not add a dependency or library without first explaining why it
    is needed.
-   Do not introduce state-management, UI, or data-fetching libraries by
    default when existing project capabilities are sufficient.

## 8. Database Changes and Migrations

-   Use EF Core migrations for every database schema change.
-   Before changing the schema, present the intended
    entities/fields/relationships and migration impact.
-   Wait for explicit approval before creating or applying a migration.
-   Never silently drop or destructively alter production data.
-   Keep domain constraints and database constraints aligned where
    practical.

Typical commands:

``` bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/AlbumApp.Web

# Example EF Core migration commands (run only after approval)
dotnet ef migrations add <MigrationName> \
  --project src/AlbumApp.Infrastructure \
  --startup-project src/AlbumApp.Web

dotnet ef database update \
  --project src/AlbumApp.Infrastructure \
  --startup-project src/AlbumApp.Web
```

Frontend commands (from `src/album-app.client`):

``` bash
npm install
npm run dev
npm run build
npm run lint
```

Use the package manager and scripts already established in the
repository if they differ.

## 9. Testing

-   Every business rule in `Core` must have unit tests.
-   API integration tests must cover the main workflows:
    -   Photographer login.
    -   Create an album.
    -   Upload photos and generate display derivatives.
    -   Create or rotate customer album access credentials.
    -   Customer opens an album with the correct access code.
    -   Customer is denied access with an incorrect code.
    -   Repeated incorrect codes trigger throttling/temporary blocking.
    -   Expired albums cannot be opened.
    -   A photographer cannot access another photographer's album.
    -   Customer downloads the album ZIP.
-   Test that ZIP downloads stream and that original files are not used
    by customer-facing display endpoints.
-   Add frontend tests for important UI states and interactions when a
    frontend test framework is established.
-   Before considering a task complete, run the relevant tests and
    `dotnet build`; run frontend build/lint as applicable. Report
    commands and results accurately. Do not claim tests passed if they
    were not run.

## 10. Working Agreement for AI Coding Agents

-   Read this file, `docs/PRD.md`, and `docs/DESIGN-FLOW.md` before
    major work.
-   Implement only the requested scope. Do not silently add out-of-MVP
    features.
-   If a requirement is missing, ambiguous, or contradictory, ask a
    focused question before making a significant product decision.
-   For schema changes, present a plan and wait for approval before
    generating migrations.
-   Make small, reviewable changes. Prefer one logical task per commit.
-   Do not overwrite user changes or delete files without explicit
    approval.
-   If a technical decision conflicts with this document, explain the
    conflict and ask before changing architecture or mandatory business
    rules.
-   Update this file and the relevant product/design documents when an
    approved architectural or product decision changes.

## 11. Explicitly Out of Scope / Prohibited Without Approval

-   No public endpoint for original photo files.
-   No plain-text access codes in database records, seed data, logs, or
    tests.
-   No permanent album/photo deletion without explicit confirmation.
-   No customer photo-selection or favorite-selection feature.
-   No change to the one-year album retention period without approval.
-   No multi-photographer product workflows in the MVP unless approved.
-   No PWA/offline mode, PDF export, template editor, or online payment
    in the MVP unless approved.
-   No microservices architecture for the MVP.

## 12. Answer Ruling
- Only explain with a simple word
- dont change many file at once, give me an explanation at the change
- Approval gate for every change: present ONE change (target file + content summary), wait for explicit user approval, then write it. No writing code before approval, no batching changes.

