# Support Desk frontend

Angular standalone application using Material, reactive forms, Signals and HttpClient.

```powershell
npm ci
npm start
```

The development server runs at http://localhost:4200 and proxies /api to http://localhost:5080.

```powershell
npm run build
npm test -- --watch=false
```

Pages and components are grouped under src/app/features. Ticket contracts are in ticket-models.ts, HTTP operations in ticket-api.ts, and shared request error handling in core/api-errors.ts.

See the [project README](../../README.md) for backend, database and provider configuration.
