# Trusted publishing in CI

NuGet's trusted-publishing posture puts credential acquisition with the CI platform, not the
release tool: the CI platform exchanges its own identity for a short-lived NuGet key.
ReleaseGen performs no OIDC token acquisition (D018).

**Trusted publishing is preferred in CI; the API key path remains the documented local and
manual route (D019).**

## The two-job procedure

The safe pattern under ReleaseGen is two CI jobs (D018). The
[stage handoff contract](stage-handoff.md) is load-bearing here: both jobs preserve the
`release/` tree layout and the `releasegen.json` Manifest.

1. **Job 1 - build through package.** Check out the repository and run the stages up to
   packaging: `releasegen build`, `releasegen test`, `releasegen package` (or the equivalent
   up to packaging). Then upload the `release/` tree as a pipeline artifact.
2. **Job 2 - publish.** Download the `release/` tree into the same directory layout, have the
   CI platform exchange its credentials for a fresh, short-lived nuget.org key (valid about
   one hour), deliver that key through the `NUGET_API_KEY` environment channel, and run
   `releasegen publish`. When the Manifest declares a test section, a standalone publish
   consults the test gate by running the test stage in the publish job, so the gate holds even
   across the job boundary (D004).

The credential exchange itself - GitHub's or GitLab's trusted-publishing setup - stays on the
CI platform's side and outside ReleaseGen's scope (D018).

## The local and manual path

Outside CI, publishing uses a standard long-lived NuGet API key delivered through the same
channel the publish stage already reads (D011):

```bash
export NUGET_API_KEY=<your-key>   # or pass --api-key <key>
releasegen publish
```

The key is never read from the Manifest, and it is redacted from console output.

## See also

- [Stage handoff contract](stage-handoff.md) - the artifact-location story both jobs preserve
- [Command reference](commands.md) - the `publish` command and its flag
- [Manifest schema](manifest-schema.md) - `publish.sources`
