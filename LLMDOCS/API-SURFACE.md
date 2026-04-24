# PoRepoLineTracker – Public API Surface

> Auto-generated reference. Update when endpoint signatures or models change.

---

## Authentication Endpoints

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/auth/login` | None | Redirects to GitHub OAuth |
| GET | `/api/auth/logout` | Cookie | Clears auth cookie, redirects home |
| GET | `/api/auth/me` | Cookie | Returns current user info |

### GET /api/auth/me
**Response 200:**
```json
{
  "id": "guid",
  "username": "string",
  "displayName": "string",
  "avatarUrl": "string",
  "email": "string",
  "isAuthenticated": true
}
```

---

## Repository Endpoints

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/repositories` | Cookie | List user's repositories |
| POST | `/api/repositories` | Cookie | Add a repository |
| GET | `/api/repositories/{id}` | Cookie | Get repository details |
| DELETE | `/api/repositories/{id}` | Cookie | Remove repository |
| POST | `/api/repositories/{id}/refresh` | Cookie | Trigger re-analysis |
| GET | `/api/repositories/{id}/linecounts` | Cookie | Daily line count history |
| GET | `/api/repositories/{id}/topfiles` | Cookie | Top files by line count |
| GET | `/api/repositories/{id}/extensions` | Cookie | Line counts by file extension |

### GET /api/repositories
**Response 200:**
```json
[
  {
    "id": "guid",
    "owner": "string",
    "name": "string",
    "fullName": "string",
    "description": "string",
    "lastAnalyzedAt": "datetime",
    "totalLines": 12345
  }
]
```

### POST /api/repositories
**Request:**
```json
{
  "owner": "string",
  "name": "string"
}
```

---

## GitHub Endpoints

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/github/repositories` | Cookie | List user's GitHub repos |
| GET | `/api/github/repositories/{owner}/{repo}/statistics` | Cookie | Get repo stats from GitHub |

---

## Settings Endpoints

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/settings` | Cookie | Get user preferences |
| PUT | `/api/settings` | Cookie | Update user preferences |

### PUT /api/settings
**Request:**
```json
{
  "theme": "light|dark",
  "defaultBranch": "string",
  "maxRepositories": 50
}
```

---

## Failed Operations

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/failed-operations` | Cookie | List retryable failures |
| POST | `/api/failed-operations/{id}/retry` | Cookie | Retry a failed operation |

---

## Health & Diagnostics

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/health` | None | JSON health check |
| GET | `/diag` | Cookie | Full diagnostics (masked secrets) |

### GET /health
**Response 200:**
```json
{
  "status": "Healthy",
  "checks": {
    "azure_table_storage": { "status": "Healthy" }
  }
}
```

---

## Dev-Only Endpoints (Development Environment Only)

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/dev-login/{userId}` | None | Impersonate any test user |
| POST | `/test-login` | None | Anonymous test login |
| GET | `/test-login-redirect` | None | Browser-based test login |
| POST | `/api/log/client` | None | Client-side log relay |

### POST /test-login
**Request:**
```json
{
  "email": "test@example.com",
  "password": "optional",
  "userAgent": "TestClient"
}
```

**Response 200:**
```json
{
  "success": true,
  "message": "Login successful",
  "userId": "guid"
}