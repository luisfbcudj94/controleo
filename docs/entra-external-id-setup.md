# Entra External ID + Google setup (Controleo)

## 1) App registrations

Create 3 app registrations in Entra External ID:

1. **API app**
   - Expose API scope: `access_as_user`
   - Application ID URI: `api://<API_CLIENT_ID>`
2. **Web app (Angular)**
   - Redirect URI: `http://localhost:4200`
   - Add delegated permission to API scope `access_as_user`
3. **Mobile app (MAUI)**
   - Public client redirect URI: `msal<MOBILE_CLIENT_ID>://auth`
   - Add delegated permission to API scope `access_as_user`

## 2) Google identity provider

In External ID tenant:
- Add Google as external identity provider
- Configure Google client id/secret
- Include Google in your sign-in user flow/policy

## 3) API configuration

Set `Controleo.Api/local.settings.json` or app settings:

- `EntraAuth__Enabled=true`
- `EntraAuth__MetadataAddress=https://<tenant-subdomain>.ciamlogin.com/<tenant-id>/v2.0/.well-known/openid-configuration`
- `EntraAuth__ValidAudiences__0=api://<API_CLIENT_ID>`
- `EntraAuth__RequiredScope=access_as_user`

Also configure Cosmos settings:
- `COSMOS_DB_ENDPOINT`
- `COSMOS_DB_KEY`

## 4) Web configuration

Update:
- `Controleo.Web/src/environments/environment.ts`
- `Controleo.Web/src/environments/environment.prod.ts`

Replace placeholders in `auth`:
- `clientId`
- `authority`
- `redirectUri`
- `postLogoutRedirectUri`
- `scopes` (use `api://<API_CLIENT_ID>/access_as_user`)

## 5) Mobile configuration

Set environment variables before running MAUI:
- `CONTROLEO_MOBILE_CLIENT_ID`
- `CONTROLEO_MOBILE_AUTHORITY`
- `CONTROLEO_MOBILE_SCOPES` (comma-separated)

## 6) Quick validation

1. Start API and confirm `/api/catalogs` returns `401` without token.
2. Sign in from Angular `/login` and call expenses endpoints.
3. Sign in from MAUI login screen and save/read expenses.
4. Verify each user only sees their own data.
