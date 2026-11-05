# Product Requirements Document (PRD)

## Digital Album Web --- MhX Solutions

**Document status:** Initial MVP draft\
**Version:** 0.1\
**Product type:** Private digital photo album web application\
**Architecture direction:** Modular Monolith\
**Frontend:** React + TypeScript + Vite\
**Backend:** ASP.NET Core Web API\
**Database:** PostgreSQL\
**Object storage:** Private S3-compatible storage (Cloudflare R2
proposed)

------------------------------------------------------------------------

## 1. Product Summary

Digital Album Web enables a photographer to deliver completed photo
sessions to customers through private, branded digital albums. The
photographer manages albums through a web dashboard. Customers access an
album through a unique link and a separate access code, view
display-sized photos, and download the complete album as a ZIP file.

The MVP is designed for one partner photographer. The system should be
structured so that album ownership is explicit in the data model,
without implementing a multi-photographer marketplace or multi-tenant
administration experience in the MVP.

## 2. Problem Statement

Photographers need a convenient way to deliver a collection of edited
photos to customers. A private web album provides a simpler customer
experience than sending many individual files, while access controls and
private storage help protect the photographer's work.

## 3. Product Goals

1.  Allow the photographer to create and manage customer albums.
2.  Provide private customer access using both a unique link and an
    access code.
3.  Present photos in a responsive, customer-friendly album interface.
4.  Allow customers to download all original album photos as one ZIP
    file.
5.  Protect original photo files and prevent unauthorized access.
6.  Keep the MVP maintainable through a modular monolith rather than
    distributed services.

## 4. Non-Goals and Out of Scope

Unless separately approved, the MVP does not include:

-   Multiple photographer accounts and multi-photographer administration
    workflows.
-   Progressive Web App (PWA) or offline access.
-   PDF album export.
-   A visual template editor.
-   Online payments or subscriptions.
-   Customer photo selection, favorites, or proofing workflows.
-   Microservices or independently deployed backend services.

## 5. Users and Roles

### 5.1 Photographer

The photographer is the authenticated operator of the dashboard. They
can create albums, upload photos, manage album details, configure
customer access, and view album status.

### 5.2 Customer

The customer does not create an account. They receive a unique album
link and an access code from the photographer. They can verify access,
view the album, and download the complete album as a ZIP file.

## 6. MVP Scope and Functional Requirements

### 6.1 Photographer authentication

-   The photographer can sign in using email and password.
-   Authentication is handled by ASP.NET Core Identity.
-   Protected dashboard/API operations require authentication.
-   The server must enforce album ownership for every album operation.

### 6.2 Album management

The photographer can:

-   Create an album.
-   Enter the customer name, album title, and session date.
-   Select an available predefined template.
-   View a list of their albums and their statuses.
-   Open an album to manage its details and photos.
-   Update permitted album metadata.
-   Archive an album where supported by the agreed lifecycle rules.

Each album stores: - Photographer ownership. - Customer name. - Album
title. - Session date. - Template reference. - `CreatedAt`. -
`ExpiresAt`. - `Status`: `Draft`, `Active`, `Expired`, or `Archived`.

An album is valid for one year from `CreatedAt`. `ExpiresAt` is
calculated at creation and does not change except through an explicitly
approved renewal process.

### 6.3 Photo upload and management

-   The photographer can upload photos to an album.
-   The backend validates file type and file size.
-   The system generates opaque storage object names rather than using
    original filenames.
-   The system stores original files privately.
-   The system creates or stores a display-sized derivative for album
    viewing.
-   The photographer can arrange photo display order.
-   The system records storage keys, display keys, order, and relevant
    file metadata.
-   Customer-facing pages use display derivatives only.
-   Original files are used for ZIP downloads only.

**Open product decisions:** maximum upload size, allowed image formats,
maximum photos per album, and whether upload should support
drag-and-drop or resumable/multipart uploads.

### 6.4 Customer access

-   The system generates a cryptographically random, hard-to-guess album
    token.
-   The customer receives a link containing or resolving to that token.
-   The customer must enter a separate access code.
-   The database stores only a password hash of the access code.
-   The system limits repeated failed attempts and temporarily throttles
    or blocks abusive sources.
-   The customer is not required to create an account.
-   A valid token and valid access code are both required before
    protected album content is returned.
-   Expired or archived albums must not expose customer photo content.

The precise access-code format, token rotation behavior, and lockout
duration require product confirmation.

### 6.5 Customer album viewing

-   The customer can open the album link.
-   The customer sees an access-code screen before protected content is
    shown.
-   After successful verification, the customer can view the album title
    and photos.
-   The album UI uses `DisplayKey` assets only.
-   Photos are displayed in a responsive layout suitable for desktop and
    mobile.
-   The customer sees a clear message if the link is invalid, access is
    denied, or the album has expired.
-   Original photo files must never be used as display assets.

The exact gallery layout, navigation, download placement, and visual
template behavior are to be finalized in the design flow.

### 6.6 ZIP download

-   The customer can request a download of the complete album.
-   The backend builds the ZIP from original photo objects.
-   ZIP generation and response delivery use streaming; the full album
    must not be buffered in application memory.
-   The system must handle missing objects and interrupted downloads
    safely.
-   The ZIP should use safe, generated or sanitized entry names and
    should not expose internal storage keys.

**Open product decisions:** ZIP filename convention, whether to preserve
photographer filenames inside the ZIP, and whether to show download
progress.

### 6.7 Album lifecycle

-   New albums begin in `Draft`.
-   An album becomes `Active` when it is ready for customer access,
    according to the activation workflow.
-   An album becomes `Expired` when its expiration time is reached.
-   An album may be `Archived` according to the photographer's permitted
    workflow.
-   Customers cannot access expired or archived albums.
-   Expiration must be enforced by the backend, not only by frontend
    display logic.
-   The one-year retention period cannot be changed without approval.

The exact transition rules and whether archived albums can be restored
need confirmation.

## 7. Non-Functional Requirements

### 7.1 Security and privacy

-   The object storage bucket must be private.
-   Display images are delivered through short-lived signed URLs or an
    authenticated server-side proxy.
-   Original files are never exposed through public URLs or customer
    display endpoints.
-   Access codes are hashed with a suitable password-hashing algorithm
    such as BCrypt or Argon2.
-   Tokens, access codes, passwords, credentials, and signed URLs must
    not be written to logs.
-   Apply rate limiting to login and customer access-code verification.
-   Validate all user input and uploaded file content.
-   Enforce photographer ownership on the server.
-   Use HTTPS in deployed environments.
-   Use appropriate CSRF protection if cookie-based authentication is
    used.
-   Do not reveal sensitive information through error messages.

### 7.2 Performance

-   Photo display should use appropriately sized derivatives rather than
    original files.
-   ZIP downloads must stream.
-   Upload and download operations should use asynchronous I/O.
-   Avoid loading entire albums into application memory.

### 7.3 Responsiveness and usability

-   Photographer dashboard and customer album pages must work on desktop
    and mobile browsers.
-   Customer access should be simple and require no account creation.
-   Loading, empty, invalid-link, incorrect-code, expired-album, and
    failure states must be designed.

### 7.4 Maintainability

-   Use a Modular Monolith with clear module boundaries.
-   Keep domain rules independent of web and infrastructure concerns.
-   Use dependency injection and DTOs.
-   Add unit and integration tests for critical business rules and
    workflows.
-   Use EF Core migrations for database schema changes.

## 8. Technical Architecture

### 8.1 High-level structure

``` text
Browser
  |
  | React application (TypeScript + Vite)
  |
  | HTTPS / REST API
  v
ASP.NET Core Web API
  |
  +-- Authentication and Authorization
  +-- Album module
  +-- Photo module
  +-- Album Access module
  +-- Download module
  |
  +--> PostgreSQL (EF Core)
  |
  +--> Private S3-compatible object storage (Cloudflare R2 proposed)
```

### 8.2 Deployment direction

The MVP should remain a single product with a modular backend. The React
application can be built into static assets and served by ASP.NET Core
so the initial deployment can be operated as one application. Hosting
the frontend separately may be considered later without changing the
modular-monolith backend architecture.

### 8.3 Core domain entities

-   `Photographer`
-   `Album`
-   `AlbumPhoto`
-   `AlbumAccess`
-   `Template`

See `AGENTS.md` for the initial domain field descriptions and mandatory
implementation rules.

## 9. Acceptance Criteria

The MVP is not considered complete until the following are demonstrated:

1.  Photographer can sign in.
2.  Photographer can create an album with customer name, title, session
    date, and template.
3.  Photographer can upload photos and the system stores originals
    privately and display derivatives separately.
4.  Photographer can configure customer access and obtain a link and
    access code.
5.  Customer cannot access album content with the link alone.
6.  Customer can access the album with a valid link and correct access
    code.
7.  Customer is denied access with an incorrect access code.
8.  Repeated failed access attempts are rate-limited or temporarily
    blocked.
9.  Customer can view display-sized photos, not original files.
10. Customer can download all original photos as a ZIP using streaming.
11. Expired and archived albums cannot be accessed by customers.
12. A photographer cannot access or modify another photographer's album.
13. `dotnet build`, backend tests, and frontend production build
    complete successfully.

## 10. Risks and Dependencies

-   Object storage configuration and private-bucket policy must be
    verified before customer access is enabled.
-   Image derivative generation needs a defined approach and resource
    limits.
-   ZIP streaming must be tested with large albums and interrupted
    connections.
-   Authentication cookie/CORS/CSRF behavior must be designed
    consistently for the selected deployment topology.
-   Product decisions listed below must be resolved before the
    corresponding features are finalized.

## 11. Open Questions Requiring Product Confirmation

These are intentionally not assumed in this draft:

1.  What is the maximum photo file size and maximum number of photos per
    album?
2.  Which upload formats are allowed (for example JPEG, PNG, WebP,
    HEIC)?
3.  Should the system automatically generate display derivatives, and
    what display dimensions/quality are expected?
4.  What access-code format should be used (for example numeric digits
    or alphanumeric), and how long should temporary lockout last?
5.  Should photographers be able to regenerate/revoke an album link or
    access code?
6.  Can archived albums be restored, and what should archiving mean for
    customer access?
7.  Should customers download the ZIP with original filenames or
    generated/sanitized filenames?
8.  Is there a required visual direction or brand identity for the
    photographer dashboard and customer album?
9.  Should the React app be served by ASP.NET Core in the first
    deployment, or hosted separately?
10. What is the expected hosting environment and operational budget?

## 12. MVP Delivery Approach

Recommended implementation order:

1.  Repository setup, solution structure, configuration, and health
    endpoint.
2.  Core domain model and unit tests.
3.  PostgreSQL/EF Core persistence and approved initial migration.
4.  Photographer authentication and authorization.
5.  Album management APIs and dashboard screens.
6.  Private photo upload, object storage integration, and display
    derivatives.
7.  Customer access-token and access-code verification.
8.  Customer album viewing experience.
9.  Streaming ZIP download.
10. Expiration enforcement, security hardening, integration tests, and
    deployment validation.
