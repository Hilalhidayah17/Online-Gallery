# Digital Album Web — Implementation Design Flow

**Status:** Approved for implementation planning
**Date:** 2026-11-05
**Sources:** `docs/PRD.md`, `docs/DESIGN-FLOW.md`, `AGENTS.md`, product-owner decisions (below)
**Codebase baseline:** `AlbumApp.sln` with empty `AlbumApp.Core` / `AlbumApp.Infrastructure` and template-only `AlbumApp.Web` on .NET 10; `tests/` empty; React client not yet scaffolded.

---

## 1. Confirmed Product Decisions (Decision Log)

| # | Decision | Choice | Consequence |
|---|----------|--------|-------------|
| D1 | Frontend hosting | React served by ASP.NET Core (single deployment) | Vite build output → `wwwroot`; same-origin cookies; no CORS in prod; Vite dev proxy in development |
| D2 | **Customer access model** | **Unique link only (token-only). No access code.** | ⚠️ Approved change to AGENTS.md §5 Rule 1 and PRD §6.4. Docs must be updated (see §14). |
| D3 | Link lifecycle | Permanent link; no regenerate/revoke in MVP | Simpler; a leaked link cannot be contained. Accepted product limitation. |
| D4 | Token storage | Hashed at rest | SHA-256 over a 256-bit crypto-random token (fast hash is correct for high-entropy secrets; BCrypt/Argon2 is unnecessary here). DB leaks don't expose live links. |
| D5 | Link display | One-time display at generation | Because of D3+D4: **if the photographer loses the link, the album can never be shared again.** UI must force a copy/acknowledge step. Accepted limitation. |
| D6 | Upload constraints | 25 MB/file; JPEG, PNG, WebP (magic-byte verified); max 300 photos/album | Enforced server-side. |
| D7 | Album activation | Manual by photographer | `Draft → Active` only via explicit action, guarded by prerequisites (≥1 photo + link generated). |
| D8 | Display-image delivery | Deferred to Phase 5 | Abstraction supports both; **short-lived signed URLs recommended**. No domain impact. |
| D9 | Customer lockout | None (token-only makes code-guessing moot) | Login rate limiting remains mandatory (AGENTS.md §6). Light per-IP throttle on customer endpoints kept as hygiene. |
| D10 | Customer session | None — stateless | Token is presented on every customer request; no cookie/session needed. This resolves DESIGN-FLOW open question #10. |

**Deferred with proposed defaults (confirm at implementation time):**
- Display derivative spec: longest edge 2048 px, JPEG q80 (WebP optional).
- ZIP entry names: sanitized original filenames, deduplicated (`photo.jpg`, `photo-2.jpg`).
- ZIP download filename: `{album-title-slug}-photos.zip`.

---

## 2. Architecture Overview

```text
Browser
  |  React + TypeScript (Vite)  ── served as static files by ASP.NET Core (prod)
  |  HTTPS / REST API           ── Vite dev proxy in development
  v
ASP.NET Core Web API (.NET 10, Minimal APIs, feature folders)
  ├── Features/Authentication   cookie auth (Identity), rate-limited login, antiforgery
  ├── Features/Albums           CRUD, archive, activate (owner-enforced)
  ├── Features/Photos           upload/validate, derivatives, ordering
  ├── Features/AlbumAccess      one-time link generation (photographer)
  ├── Features/CustomerAlbum    token resolution, gallery data, expiry enforcement
  └── Features/Downloads        streaming ZIP (originals, revalidated)
        |
        +--> PostgreSQL 18 (EF Core + Npgsql, Identity tables)
        +--> Private S3-compatible bucket (Cloudflare R2) behind IObjectStorage
        +--> Serilog (console + file; secrets/tokens/URLs never logged)
```

Dependency rules (unchanged, per AGENTS.md §3):
`Core` → nothing. `Infrastructure` → `Core`. `Web` → `Core` + `Infrastructure`.

---

## 3. Domain Model (AlbumApp.Core)

### 3.1 Entities

**Album**
| Field | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| PhotographerId | string | FK → Identity user (see 3.3) |
| CustomerName | string | required, max length enforced |
| Title | string | required |
| SessionDate | DateOnly | |
| TemplateId | Guid | FK → Template |
| CreatedAt | DateTime (UTC) | set at creation |
| ExpiresAt | DateTime (UTC) | `CreatedAt + 1 year`, **immutable** (renewal only via approved future process) |
| Status | AlbumStatus | `Draft`, `Active`, `Expired`, `Archived` |

Domain behaviors (all unit-tested):
- `Activate(now)` — throws unless `Draft`, has ≥1 photo, and access link exists. Sets `Active`.
- `Archive()` — `Draft|Active → Archived`.
- `IsExpiredAt(now)` — `now >= ExpiresAt`.
- `EnsureCustomerAccessible(now)` — throws `AlbumExpiredException` / `AlbumUnavailableException` unless `Active` and not expired.

**AlbumPhoto**
| Field | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| AlbumId | Guid | FK |
| StorageKey | string | original object key (opaque GUID-based) — **never exposed to customers** |
| DisplayKey | string | display derivative key |
| OriginalFileName | string | sanitized; used only inside ZIP |
| Order | int | display order |
| ContentType | string | sniffed, not trusted from client |
| SizeBytes | long | |
| UploadedAt | DateTime | |

**AlbumAccess** (1:1 with Album)
| Field | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| AlbumId | Guid | unique FK |
| TokenHash | string | SHA-256 hex of the token; unique index |
| CreatedAt | DateTime | |

Token: 256-bit via `RandomNumberGenerator`, base64url (~43 chars). Plaintext token exists only in memory and in the **one-time** API response (D5).

**Template**
| Id (Guid), Key (string), Name (string), IsActive (bool) |
Seeded with 1–2 predefined templates; no template editor (out of scope).

### 3.2 Core abstractions (ports)

```csharp
public interface ISecureTokenGenerator { string Generate(); }        // 256-bit base64url
public interface ITokenHasher { string Hash(string token); }         // SHA-256
public interface IObjectStorage {
    Task PutAsync(string key, Stream content, string contentType, CancellationToken ct);
    Task<Stream> OpenReadAsync(string key, CancellationToken ct);
    Task<string> CreateSignedReadUrlAsync(string key, TimeSpan ttl); // used only if D8 = signed URLs
    Task DeleteAsync(string key, CancellationToken ct);
}
public interface IImageDerivativeService {
    Task<DerivativeResult> CreateDisplayDerivativeAsync(Stream original, CancellationToken ct);
}
```

### 3.3 Photographer ownership

ASP.NET Core Identity lives in **Infrastructure** (`ApplicationUser : IdentityUser`). Core keeps no Identity dependency; `Album.PhotographerId` carries the Identity user id. Every photographer-scoped query/command filters by the current user id — enforced in the application layer, covered by integration tests (cross-photographer access must fail).

---

## 4. Infrastructure Design (AlbumApp.Infrastructure)

- **Persistence:** `AppDbContext : IdentityDbContext<ApplicationUser>`, Npgsql, per-entity `IEntityTypeConfiguration` classes.
  - Unique indexes: `AlbumAccess.TokenHash` (unique), `AlbumAccess.AlbumId` (unique), `(AlbumId, Order)` on photos.
  - Delete behavior: Album → Photos/Access cascade (only exercised after explicit confirmation).
- **Storage:** AWS SDK S3 client configured against the R2 endpoint, isolated behind `IObjectStorage`. Bucket private; no public ACLs ever.
  - Key scheme: `albums/{albumId}/originals/{guid}{ext}` and `albums/{albumId}/display/{guid}.jpg`.
- **Derivatives:** **ImageSharp** (SixLabors.ImageSharp). *Dependency justification (required by AGENTS.md §7):* server-side resize/encode of display derivatives; pure managed code, actively maintained, no native dependencies — the standard choice for ASP.NET Core image processing.
- **Hashing/tokens:** `System.Security.Cryptography` (SHA-256, RNG) — no extra dependency.
- **Logging:** Serilog console + rolling file. Explicit rule: never log tokens, token hashes in plaintext context, signed URLs, or Identity credentials.
- **Rate limiting:** built-in ASP.NET Core limiter — strict on `POST /api/auth/login` (e.g. 10/min/IP), light fixed window on `/api/a/*` (enumeration hygiene, no lockout semantics).

---

## 5. API Surface (AlbumApp.Web, Minimal APIs, one style everywhere)

Base: `/api`. Error responses: consistent `{ error: { code, message } }`; never reveal token existence (generic 404).

### Photographer (cookie-authenticated; antiforgery on mutations)
| Endpoint | Purpose |
|---|---|
| `POST /api/auth/login` · `POST /api/auth/logout` · `GET /api/auth/me` | Identity cookie auth |
| `GET /api/templates` | Active templates for the create form |
| `GET /api/albums` · `POST /api/albums` | List own / create (sets `CreatedAt`, `ExpiresAt`, `Draft`) |
| `GET /api/albums/{id}` · `PATCH /api/albums/{id}` | Detail / update permitted metadata |
| `POST /api/albums/{id}/archive` | Archive |
| `POST /api/albums/{id}/activate` | Manual activation (guard: ≥1 photo + link exists) |
| `POST /api/albums/{id}/photos` | Multipart upload (multi-file; validate type/size/count) |
| `GET /api/albums/{id}/photos` | List with display URLs for dashboard preview |
| `PUT /api/albums/{id}/photos/order` | Reorder (ordered id list) |
| `DELETE /api/albums/{id}/photos/{photoId}` | Requires explicit confirmation flag (AGENTS.md Rule 12) |
| `POST /api/albums/{id}/access` | Generate token + link; **returned exactly once** (D5) |
| `GET /api/albums/{id}/access/status` | `{ hasLink: bool }` — never returns the token |

### Customer (token in path, no account, no session)
| Endpoint | Purpose |
|---|---|
| `GET /api/a/{token}` | Resolve token → hash → album. Invalid → generic 404. Expired/Archived/Draft → `410 { code: "album-expired" }` (no content). Valid → album metadata + ordered display-image list (signed URLs if D8, else proxy URLs) |
| `GET /api/a/{token}/download` | Revalidate, then stream ZIP of **originals** |

Customer UI routes: `/a/{token}` (gallery or expired/unavailable state). The old `/a/:token/access` code-entry page is **removed** per D2.

---

## 6. Frontend Design (`src/album-app.client` — React + TS + Vite)

- **Build/deploy:** `vite build` → output copied into `AlbumApp.Web/wwwroot`; ASP.NET Core serves static files + SPA fallback. Dev: Vite dev server proxies `/api` to the Kestrel port.
- **Structure (per AGENTS.md §3):** `app/` (router, providers), `pages/`, `features/` (albums, photos, access, customer), `components/`, `services/` (single typed API client, `fetch` + `credentials: "include"`), `styles/`.
- **No state-management/UI/data-fetching libraries** (AGENTS.md §7). Additions: `react-router-dom` only (justification: route structure defined in DESIGN-FLOW §7).
- **Photographer pages:** Login → Overview → Album List → Create Album → Album Detail (Info / Photos / Access tabs). Access tab: generate link → **modal with copy + "I have saved this link" acknowledgment** (D5) → activation button with prerequisite checklist.
- **Customer pages:** `/a/:token` gallery (responsive grid of display images), Expired state, Unavailable state. No code-entry page (D2).
- **Rules:** no business logic client-side; tokens never in localStorage (they live only in the URL, by design); all destructive actions get confirm dialogs.

---

## 7. Database Schema (first migration — requires explicit approval before creation)

Tables: `AspNet*` (Identity), `Albums`, `AlbumPhotos`, `AlbumAccesses`, `Templates` (+ seed).
Approval gate per AGENTS.md §8: schema review → `dotnet ef migrations add InitialCreate` → `database update`.

---

## 8. Upload & Derivative Pipeline

1. Multipart upload; per-file: extension allowlist → **magic-byte sniff** (JPEG `FF D8`, PNG `89 50 4E 47`, WebP `RIFF…WEBP`) → size ≤ 25 MB → album count < 300.
2. Generate GUID keys; stream original to R2 (`originals/`).
3. ImageSharp derivative (≤2048 px longest edge, JPEG q80) → stream to R2 (`display/`).
4. Persist `AlbumPhoto` (keys, order = append, metadata). Failures: per-file error response; no partial DB records without storage objects.
5. Gallery/dashboard never receive `StorageKey`; DTOs expose display URL + id + order only.

---

## 9. Streaming ZIP Download

1. Revalidate token + status + expiry at request time.
2. `Response.ContentType = application/zip`; `Content-Disposition: attachment; filename="{slug}-photos.zip"`; no buffering — `ZipArchive` directly over the response stream.
3. Per photo: open S3 original stream → copy into entry stream. Entry names: sanitized `OriginalFileName`, deduplicated.
4. Missing object → skip, log warning (no storage details to client). Client disconnect → abort quietly.
5. Integration test asserts: streamed (no full in-memory buffer), originals only, valid token required.

---

## 10. Expiration Enforcement

- Server-side on every customer request: `IsExpiredAt(now)` → treat as `Expired` (persisted status can lazily follow; no background job needed for MVP).
- `Draft`/`Archived` are never customer-visible.

---

## 11. Security Checklist vs. AGENTS.md (post-decision)

| Rule | Status under token-only model |
|---|---|
| #1 access via AlbumAccess | **Amended (approved):** token validated through `AlbumAccess` (hash lookup); no access code |
| #2 hash secrets | Applied to the token (SHA-256); access-code rule no longer applies |
| #3 rate-limit failures | Login: yes. Customer: light per-IP throttle; **no lockout (D9)** |
| #4–#12 (private bucket, display-only derivatives, expiry, streaming ZIP, opaque keys, ownership, upload validation, antiforgery, confirmed deletion) | Unchanged, all apply |

Plus §6: user-secrets for dev (R2 keys, connection string), HTTPS, security headers, SPA fallback routing doesn't bypass API auth.

---

## 12. Testing Plan

**Core unit tests (xUnit):** expiry calculation; activation guards (no photos / no link / wrong state); archive transitions; `EnsureCustomerAccessible`; token generator entropy/length; hasher determinism.

**API integration tests (`WebApplicationFactory` + dedicated local Postgres test DB — no Docker available):**
- Photographer login (success/failure), logout, `/me`.
- Create album → Draft, `ExpiresAt = CreatedAt + 1y`.
- Upload photos → originals + display derivatives stored; validation rejects bad type/size.
- Generate link → one-time token in response; `access/status` reports existence only.
- Activate → guard failures then success.
- Customer: valid token → metadata + display refs (**never** `StorageKey`/original URLs); invalid token → generic 404 (indistinguishable); expired album → 410; draft/archived → unavailable.
- Cross-photographer access denied on every album endpoint.
- ZIP: streams, contains originals, requires valid token.
- Photo delete requires confirmation flag.

**Frontend:** `npm run build` + `npm run lint` green; component tests deferred until a framework is chosen (AGENTS.md §9).

---

## 13. Phased To-Do Flow (exit criteria per phase)

- [ ] **P0 — Foundation:** remove template weather code + `Class1.cs`; Serilog; `/health`; create `tests/AlbumApp.Core.Tests` + `tests/AlbumApp.Web.Tests`, add to solution; **exit:** `dotnet build` + `dotnet test` green.
- [ ] **P1 — Core domain:** entities, enums, behaviors, abstractions, unit tests; **exit:** all Core rules tested.
- [ ] **P2 — Docs sync (approved change D2/D3):** update `AGENTS.md` (§5 rules 1–3, §6), `PRD.md` (§6.4, §9), `DESIGN-FLOW.md` (§4.1, §7, §10) to token-only access. *Do this immediately after leaving Plan mode so docs and code never diverge.*
- [ ] **P3 — Persistence:** DbContext, configurations, Identity; **present schema → get approval →** initial migration; **exit:** migration applied locally.
- [ ] **P4 — Photographer auth:** login/logout/me, antiforgery, login rate limit; seed the single photographer account via user-secrets/config; **exit:** auth integration tests.
- [ ] **P5 — Albums module:** CRUD + archive + activate; templates seed; **exit:** ownership + lifecycle integration tests.
- [ ] **P6 — Photos + storage:** `IObjectStorage` (R2; in-memory fake for local dev until R2 credentials exist), upload validation, ImageSharp derivatives, ordering, confirmed delete; **decide D8 here (signed URLs recommended)**; **exit:** upload/derivative tests.
- [ ] **P7 — Customer access + gallery API:** token generation + one-time display endpoint, token resolution, expiry enforcement, gallery DTOs; **exit:** customer access test matrix (valid/invalid/expired).
- [ ] **P8 — ZIP download:** streaming implementation + tests.
- [ ] **P9 — React client:** scaffold Vite+TS, router, services, dashboard pages, customer gallery/expired/unavailable, one-time-link modal; static-file hosting + SPA fallback; **exit:** `npm run build` + `npm run lint` green, manual smoke test.
- [ ] **P10 — Hardening & acceptance:** security headers, config review, full test suite, PRD §9 acceptance walkthrough (adjusted for token-only), production build validation.

---

## 14. Risks / Follow-ups

| Item | Handling |
|---|---|
| **Lost link = unshareable album** (D3+D4+D5) | One-time display modal with forced acknowledgment; documented limitation. If this becomes painful, the fix is encrypted-at-rest tokens or link regeneration (future approval). |
| Docs/code divergence on access model | P2 docs sync is mandatory before feature work. |
| R2 credentials not yet available | `IObjectStorage` in-memory fake keeps P6/P7 unblocked for local dev; verify private-bucket policy before any real customer use (PRD §10). |
| No Docker on dev machine | Integration tests use a dedicated local PostgreSQL test database instead of Testcontainers. |
| Leaked link has no containment (D3) | Accepted. Re-evaluate revocation after MVP feedback. |
