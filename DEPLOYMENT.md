## Deployment

This repo deploys from the existing folders:

- Backend: `KrishAgent`
- Frontend: `krishagentui`
- Source control and CI/CD: GitHub
- Backend hosting: Render
- Frontend hosting: Vercel
- Database: Supabase

The folder names are already wired into the deployment config, so there is no risky physical restructure.

### GitHub secrets

Add these repository secrets before enabling the workflow:

- `VERCEL_TOKEN`
- `VERCEL_ORG_ID`
- `VERCEL_PROJECT_ID`
- `NEXT_PUBLIC_API_BASE_URL`
- `RENDER_DEPLOY_HOOK_URL`

Recommended value:

- `VERCEL_ORG_ID=team_Vv199lkQ4BbWWcuFKZK8dmgP`
- `VERCEL_PROJECT_ID=prj_Tv4AvBBNBjpxQ9RFAxpcHBBsUeN8`
- `NEXT_PUBLIC_API_BASE_URL=https://trading-backend.onrender.com/api`

Resolved from this workspace:

- GitHub repo: `https://github.com/arungopichand/TradingAgent`
- Vercel team: `arungopichands-projects`
- Vercel project: `trading-agent-ui`

### Render backend

Render uses [render.yaml](/c:/Users/arung/TradingAgent/render.yaml) and builds the API from [KrishAgent/Dockerfile](/c:/Users/arung/TradingAgent/KrishAgent/Dockerfile).

Set these environment variables in Render:

- `FINNHUB__APIKEY`
- `SUPABASE_URL`
- `SUPABASE_KEY`
- `DATABASE_URL`
- `ConnectionStrings__DefaultConnection`
- `CORS_ALLOWED_ORIGINS`
- `OpenAI__ApiKey`
- `OpenAI__Model`

The API already binds to the dynamic Render `PORT`, and the background services are guarded so failed scans do not bring down the process.

### Vercel frontend

Import `krishagentui` as the Vercel project root and keep `main` as the production branch.

Set this environment variable in Vercel:

- `NEXT_PUBLIC_API_BASE_URL`

The frontend now reads its API base from env instead of falling back to localhost.

### GitHub Actions pipeline

The workflow at [.github/workflows/deploy.yml](/c:/Users/arung/TradingAgent/.github/workflows/deploy.yml) runs on every push to `main` and:

- restores and builds the ASP.NET backend
- installs and builds the frontend
- deploys the frontend to Vercel with the CLI
- triggers a Render backend deploy through the deploy hook

### One-time setup

1. Connect the repo to Render and create the `trading-backend` service from the root `render.yaml`.
2. Create a Render deploy hook and save it as `RENDER_DEPLOY_HOOK_URL` in GitHub secrets.
3. Import `krishagentui` into Vercel and confirm the project IDs match your GitHub secrets.
4. Add `NEXT_PUBLIC_API_BASE_URL` in both Vercel and GitHub secrets.
5. Push to `main`.

After that, `git push` to `main` will build and deploy both apps automatically.
