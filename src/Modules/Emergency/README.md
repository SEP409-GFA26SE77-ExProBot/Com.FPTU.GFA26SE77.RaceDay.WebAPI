# Module template (copy-paste)

1. Copy folder `src/Modules/_Template` → `src/Modules/YourModule`
2. Rename projects/namespaces `RaceDay.Modules._Template.*` → `RaceDay.Modules.YourModule.*`
3. Implement Domain → Application → Infrastructure → Api → Contracts
4. Add to `RaceDay.slnx` (if not already)
5. In `src/Hosts/RaceDay.Api/Program.cs`:
   - `builder.Services.AddYourModuleModule(builder.Configuration);`
   - `app.MapYourModuleEndpoints();`
6. Add Gateway routes for `/api/v1/...` (public) and/or `/admin/v1/...` (internal only)
7. Add unit/integration tests under `tests/`

Do **not** reference this template from Host in production — it is a starter only.
