# DA Secure — Final Release Provenance & Evidence Matrix

Audit role: **DA Secure — Final Release Provenance & Evidence Auditor**  
Repository: `walidatiyaai2025-gif/QR`  
Audit branch: `worker/da-secure-final-release-provenance`  
Audit snapshot: 2026-08-26 UTC

## Release decision

**FINAL STATUS: WAITING-FOR-FINAL-CONVERGENCE**

The final release candidate SHA does not exist yet. At this snapshot:

- live `lead/da-secure-runtime-convergence` = `3e0864f38d15bbb890833b52fb193cbd19e6464a`
- Admin P1/P0 closure = `dd261fa1b911a8c7856b286f1d07049b02ad492f` and is not yet in the live lead
- Localization P2 closure = `a517b65711e323d23b47201e5a38521c6c0248d1` and is not yet in the live lead
- IIS closure = `d48b7110cfe8bee942351805fc5d8b023b0c2e6a`
- Final E2E QA harness branch = `e3a434e635875ea9fe28141c20b02c7a77ea4e2d`

Therefore:

`EXPECTED_FINAL_SHA = NOT-YET-CREATED`

No current artifact is allowed to carry the label **FINAL**.

## Non-negotiable provenance rule

Ancestry, merge-base relationship, matching source trees, or successful CI on a neighboring SHA are not sufficient.

For final release evidence the following equalities are mandatory:

```text
FINAL_SHA == backend build/test checked-out SHA
FINAL_SHA == Flutter format/analyze/test checked-out SHA
FINAL_SHA == APK build checked-out SHA
FINAL_SHA == E2E production source SHA
FINAL_SHA == IIS BUILD-MANIFEST.sourceSha
```

Any mismatch is **NOT FINAL**.

A GitHub Actions run whose metadata reports `head_sha=FINAL_SHA` is also insufficient if `actions/checkout` actually checks out a synthetic pull-request merge commit. The checked-out commit used by the build is authoritative.

## Live closure state

| Workstream | Branch / PR | Exact SHA | CI | Audit classification | Release consequence |
|---|---|---|---|---|---|
| Live convergence baseline | `lead/da-secure-runtime-convergence` / PR #21 | `3e0864f38d15bbb890833b52fb193cbd19e6464a` | exact-head push run `32914275215` GREEN | TOOLING-ONLY | Valid baseline evidence only; final convergence is incomplete. |
| Admin P0/P1 closure | `worker/da-secure-admin-hotfix-closure` / PR #20 | `dd261fa1b911a8c7856b286f1d07049b02ad492f` | run `32915185055` GREEN | TOOLING-ONLY | Must be integrated by convergence owner and then revalidated at the resulting final SHA. |
| Localization P2 | `worker/da-secure-localization-p2-closure` | `a517b65711e323d23b47201e5a38521c6c0248d1` | run `32915587152` GREEN | TOOLING-ONLY | Must be integrated by convergence owner and then revalidated at the resulting final SHA. |
| IIS closure | `worker/da-secure-iis-deployment-closure` / PR #22 | `d48b7110cfe8bee942351805fc5d8b023b0c2e6a` | run `32915931300` GREEN | TOOLING-ONLY | Packaging mechanism is validated, but package must be rebuilt from final SHA. |
| Final E2E QA harness | `worker/da-secure-final-e2e-device-qa` | `e3a434e635875ea9fe28141c20b02c7a77ea4e2d` | run `32915380364` GREEN | TOOLING-ONLY | QA harness is useful but must be rerun with explicit final production-source SHA after final convergence. |

## Artifact evidence matrix

| Classification | Component | Source branch | Workflow run | Build / workflow SHA | Artifact ID | Artifact name | Artifact digest | Timestamp UTC | Exact current lead? | Exact final candidate? | Decision |
|---|---|---|---:|---|---:|---|---|---|---|---|---|
| TOOLING-ONLY | Backend tests — exact-head baseline | `lead/da-secure-runtime-convergence` | `32914275215` | `3e0864f38d15bbb890833b52fb193cbd19e6464a` | `9587618833` | `da-secure-runtime-convergence-backend-tests` | `sha256:72520264f89e7ffd53d53a18809a3e55781de65acfd9fd209fd6be99926029c9` | 2026-08-26 00:16:50 | YES | NO — final not created | Keep as baseline proof only. |
| TOOLING-ONLY | Debug APK — exact-head baseline | `lead/da-secure-runtime-convergence` | `32914275215` | `3e0864f38d15bbb890833b52fb193cbd19e6464a` | `9587692787` | `da-secure-runtime-convergence-debug-apk` | `sha256:a636b8082a274cf3c3316759a47ee5cad1d058e4425cf0129020a78c6add9007` | 2026-08-26 00:19:48 | YES | NO — final not created | Keep as baseline proof only. |
| UNVERIFIED | Backend tests — latest PR run | `lead/da-secure-runtime-convergence` / PR #21 | `32914755630` | metadata `3e0864f...`; actual checkout `3b7c3dfdfbe77f258e3c9714df5483eb32907055` | `9587779237` | `da-secure-runtime-convergence-backend-tests` | `sha256:a1be594085e94efed94b539f6863e056aa2a0789fd077bd5cc9c76030795b486` | 2026-08-26 00:23:17 | NO — synthetic PR merge checkout | NO | GREEN is not sufficient for exact-SHA provenance. |
| UNVERIFIED | Debug APK — latest PR run | `lead/da-secure-runtime-convergence` / PR #21 | `32914755630` | metadata `3e0864f...`; actual checkout `3b7c3dfdfbe77f258e3c9714df5483eb32907055` | `9587880078` | `da-secure-runtime-convergence-debug-apk` | `sha256:92f9de1d66e9c550b471bdfef234ce99b1da367ac0425d7313f56e29507519a1` | 2026-08-26 00:27:21 | NO — synthetic PR merge checkout | NO | Must not be promoted to exact-head final APK evidence. |
| TOOLING-ONLY | Admin hotfix test evidence | `worker/da-secure-admin-hotfix-closure` | `32915185055` | `dd261fa1b911a8c7856b286f1d07049b02ad492f` | `9587924506` | `da-secure-admin-hotfix-test-results` | `sha256:d31631a2b74c23eb3ecd39cd0a03eed5461cf2669b66b4a7f822294fa731590c` | 2026-08-26 00:29:11 | NO | NO | Valid worker evidence; final SHA must rerun integrated tests. |
| TOOLING-ONLY | Localization P2 CI | `worker/da-secure-localization-p2-closure` | `32915587152` | `a517b65711e323d23b47201e5a38521c6c0248d1` | — | no artifact uploaded | — | 2026-08-26 00:33–00:35 | NO | NO | CI is GREEN; exact final convergence must revalidate. |
| TOOLING-ONLY | E2E backend evidence | `worker/da-secure-final-e2e-device-qa` | `32915380364` | `e3a434e635875ea9fe28141c20b02c7a77ea4e2d` | `9588001379` | `da-secure-final-e2e-backend-evidence` | `sha256:2079678122413d473dc3455e8279df63a24f8878ed1fa6db133b2d327a6b8433` | 2026-08-26 00:32:25 | NO | NO | Production tree is based on current lead, but exact equality with final SHA is absent. |
| TOOLING-ONLY | E2E debug APK | `worker/da-secure-final-e2e-device-qa` | `32915380364` | `e3a434e635875ea9fe28141c20b02c7a77ea4e2d` | `9588086759` | `da-secure-final-e2e-debug-apk` | `sha256:b9d0841c12c7bfb200da4c2bafefe6ca9e9daa760710fe3cbc477e606f4a2266` | 2026-08-26 00:36:08 | NO | NO | Raw APK SHA256 `a799c6d8abeb4c94c68890813adddfea52eebcbfce24778654e9a41dc6579ff3`; rebuild after final SHA. |
| TOOLING-ONLY | IIS deployment package | `worker/da-secure-iis-deployment-closure` | `32915931300` | `d48b7110cfe8bee942351805fc5d8b023b0c2e6a` | `9588195365` | `da-secure-iis-1.0.0-iis.2-d48b7110cfe8bee942351805fc5d8b023b0c2e6a` | `sha256:c26f9ffff15cfcd65af195daf8ebb6f2553eeda47cd4edc7786e83ac824cbc46` | 2026-08-26 00:40:58 | NO | NO | Internally consistent package; must be rebuilt from final SHA. |
| STALE | Historical E2E candidate evidence | historical QA matrix | `32914294391` | historical candidate/checkpoint (`e9c62cd...` / `29525857...`) | `9587616887`, `9587733258` | historical backend evidence / debug APK | historical matrix values | before current closure snapshot | NO | NO | Do not label FINAL. |
| SUPERSEDED | Historical IIS artifact referenced by PR #22 body | `worker/da-secure-iis-deployment-closure` | older run | older IIS worker state | `9587905221` | older IIS deployment artifact | older digest | before run `32915931300` | NO | NO | Superseded by artifact `9588195365`, which itself remains TOOLING-ONLY. |

## Exact-head baseline validation

The live lead has one useful exact-head push run: `32914275215`.

The run event is `push`, the workflow `head_sha` is `3e0864f38d15bbb890833b52fb193cbd19e6464a`, and the checkout log resolves exactly to the same SHA.

Baseline results:

- backend restore: PASS
- backend Release build: PASS, 0 errors
- backend tests: 125 passed / 0 failed
- Flutter dependency resolution: PASS
- Dart format gate: PASS
- Flutter analyze: PASS
- Flutter tests: PASS
- Android identity gate: PASS
- debug APK build: PASS
- backend artifact: `9587618833`
- APK artifact: `9587692787`

These are authoritative for the **current lead baseline only**, not for the unreconciled final release.

### PR synthetic-checkout warning

The later PR #21 run `32914755630` reports workflow metadata `head_sha=3e0864f38d15bbb890833b52fb193cbd19e6464a`, but the job log shows that `actions/checkout` fetched and checked out synthetic PR merge commit:

`3b7c3dfdfbe77f258e3c9714df5483eb32907055`

Accordingly, artifacts `9587779237` and `9587880078` are **UNVERIFIED for exact-SHA provenance** and cannot replace the exact-head push artifacts above.

## Admin hotfix evidence

PR #20 head: `dd261fa1b911a8c7856b286f1d07049b02ad492f`.

Latest observed CI run `32915185055` is GREEN and uploaded artifact:

- ID: `9587924506`
- name: `da-secure-admin-hotfix-test-results`
- digest: `sha256:d31631a2b74c23eb3ecd39cd0a03eed5461cf2669b66b4a7f822294fa731590c`

This is admissible worker evidence only. It does not satisfy final-release provenance until the resulting final convergence commit is validated directly.

## Localization P2 evidence

P2 branch head: `a517b65711e323d23b47201e5a38521c6c0248d1`.

Run `32915587152` is GREEN. It validates backend and Flutter localization closure at that worker SHA. No workflow artifact is uploaded by that run.

This remains TOOLING-ONLY until integrated into the final convergence SHA and revalidated there.

## E2E evidence

Latest E2E branch head: `e3a434e635875ea9fe28141c20b02c7a77ea4e2d`.

Run `32915380364` is GREEN:

- backend/Admin/security job: PASS
- Flutter format: PASS
- Flutter analyze: PASS
- Flutter tests: 109 passed
- Android identity: PASS
- debug APK: PASS
- backend evidence artifact ID: `9588001379`
- APK artifact ID: `9588086759`
- raw APK SHA256: `a799c6d8abeb4c94c68890813adddfea52eebcbfce24778654e9a41dc6579ff3`

The E2E branch merge commit has the live lead `3e0864f38d15bbb890833b52fb193cbd19e6464a` as a parent, and the diff from the live lead contains QA/test/tooling files only. This is useful evidence of the QA harness, but **ancestry/tree equivalence is not accepted as FINAL provenance**. After final convergence, the E2E workflow must explicitly capture and prove `productionSourceSha == FINAL_SHA`.

The checked-in historical E2E QA matrix that still names candidate `e9c62cd8ba215e5f1e038cdddb9776acc70990bb` is STALE for final provenance.

## IIS package provenance

Latest IIS package run: `32915931300` on exact worker SHA:

`d48b7110cfe8bee942351805fc5d8b023b0c2e6a`

Artifact:

- ID: `9588195365`
- name: `da-secure-iis-1.0.0-iis.2-d48b7110cfe8bee942351805fc5d8b023b0c2e6a`
- artifact ZIP SHA256: `c26f9ffff15cfcd65af195daf8ebb6f2553eeda47cd4edc7786e83ac824cbc46`

Generated `BUILD-MANIFEST.json` inside the downloaded artifact records:

```text
packageVersion = 1.0.0-iis.2
sourceBranch = worker/da-secure-iis-deployment-closure
sourceSha = d48b7110cfe8bee942351805fc5d8b023b0c2e6a
buildTimestampUtc = 2026-08-26T00:40:52.6246710Z
dotnetVersion = 10.0.400
targetFramework = net10.0
artifactHash.target = publish/SecureQrPortal.dll
artifactHash.value = c2a60512e831fed2dfbc7b9e46b121265294716133069761d50ba285e371885d
```

Independent download inspection confirmed the packaged `publish/SecureQrPortal.dll` SHA256 is exactly:

`c2a60512e831fed2dfbc7b9e46b121265294716133069761d50ba285e371885d`

The IIS job also validated two clean publishes against the same payload hash and verified that generated manifest `sourceSha`, checked-out HEAD, and `GITHUB_SHA` were equal.

This makes the packaging process internally trustworthy at `d48b7110...`, but it is **not a final IIS package** because `d48b7110... != FINAL_SHA`.

## Security artifact audit

Result: **PASS for the artifacts inspected in this snapshot.**

Downloaded and inspected:

- latest exact-lead APK artifact ZIP
- latest E2E APK artifact ZIP
- latest E2E backend evidence ZIP
- latest lead backend-test ZIP
- latest IIS package ZIP

Checks performed without exposing secret values:

- no Firebase service-account credential JSON filenames
- no `.pfx` / `.p12` / private-key files
- no PEM private-key signatures
- no `.env` secret files
- no `appsettings.Secrets.json`
- no credential/token JSON artifacts
- no forbidden secret/key entries inside either APK archive
- published IIS `appsettings*.json` contained no populated key names matching password/secret/API-key/token/credential/private-key categories
- no `App_Data` payload was packaged in the IIS release artifact
- IIS workflow includes an explicit service-account JSON rejection gate

Strings such as `private_key`, `client_email`, or `appsettings.Secrets.json` may exist in deployment **safety code/documentation that detects or preserves secrets**; those marker strings are not credential material and were not treated as a secret leak.

The secret scan must be repeated on every artifact regenerated from the final candidate SHA.

## Final release checklist — current state

| Requirement | Current state | Final decision |
|---|---|---|
| One final convergence SHA | NOT YET CREATED | WAIT |
| Backend restore/build/tests at final SHA | Baseline GREEN at `3e0864f...`; Admin/P2 not integrated | REBUILD/RETEST |
| Flutter format/analyze/tests at final SHA | Baseline GREEN at `3e0864f...`; P2 not integrated | REBUILD/RETEST |
| Debug APK at final SHA | Baseline APK exists; final SHA absent | REBUILD |
| Final E2E exact production source | QA harness GREEN against current-lead-derived source; final SHA absent | RERUN |
| IIS package from final SHA | Packaging GREEN at `d48b7110...` | REBUILD |
| IIS manifest `sourceSha == FINAL_SHA` | `sourceSha=d48b7110...` today | REBUILD |
| IIS payload SHA256 recorded | YES for tooling package | RECOMPUTE AT FINAL SHA |
| App_Data preservation/exclusion | PASS in tooling package/process | RECHECK |
| Secret exclusion | PASS in inspected artifacts | RECHECK |
| No stale artifact labeled FINAL | PASS in this matrix | KEEP ENFORCED |

## Exact artifacts/evidence to regenerate after final SHA exists

Once the convergence owner creates the single final candidate commit, the following are mandatory and must all bind to that exact SHA:

1. **Final backend CI evidence**
   - checkout exactly `FINAL_SHA`, not a PR synthetic merge ref
   - `dotnet restore SecureQrPortal.sln`
   - Release build
   - full backend tests
   - uploaded test-results artifact whose workflow source checkout is exactly `FINAL_SHA`
   - record artifact ID and artifact digest

2. **Final Flutter CI evidence**
   - checkout exactly `FINAL_SHA`
   - `flutter pub get`
   - `dart format --output=none --set-exit-if-changed lib test`
   - `flutter analyze`
   - `flutter test`
   - record test totals and zero-error state

3. **Final debug APK**
   - built in the same exact-`FINAL_SHA` workflow context
   - record raw `app-debug.apk` SHA256
   - upload artifact
   - record artifact ID and uploaded ZIP digest

4. **Final E2E evidence**
   - rerun/rebase the E2E QA harness against `FINAL_SHA`
   - evidence must explicitly record `productionSourceSha=FINAL_SHA`
   - backend/Admin/security E2E PASS
   - Flutter E2E/security PASS
   - any external live-device/provider evidence must remain UNVERIFIED unless actually observed; mocks cannot promote LIVE SMS/FCM/device status

5. **Final IIS package**
   - build from exact `FINAL_SHA`
   - generated `BUILD-MANIFEST.sourceSha == FINAL_SHA`
   - `SourceRevisionId == FINAL_SHA`
   - deterministic/reproducible payload check PASS
   - record `publish/SecureQrPortal.dll` SHA256
   - record IIS artifact ID and artifact ZIP digest
   - App_Data exclusion/preservation checks PASS
   - service-account / PFX / PEM / `.env` / secrets-file exclusion PASS

6. **Final provenance matrix update**
   - replace `EXPECTED_FINAL_SHA = NOT-YET-CREATED` with the exact final SHA
   - mark evidence AUTHORITATIVE only where exact equality is proven
   - keep all nonmatching historical artifacts STALE, SUPERSEDED, TOOLING-ONLY, or UNVERIFIED

## Prohibited release shortcuts

Do not:

- treat ancestry as provenance equality
- promote PR synthetic-merge build artifacts as exact-head artifacts
- reuse worker APKs or IIS packages after final convergence
- relabel old artifacts as FINAL
- copy a source SHA into a manifest without building from that exact checkout
- claim LIVE SMS/FCM/device evidence from mocks or static tests
- include runtime secrets in release artifacts
- merge this audit branch into `main` from this worker

## Current authoritative handoff state

```text
LIVE LEAD SHA: 3e0864f38d15bbb890833b52fb193cbd19e6464a
EXPECTED FINAL SHA: NOT-YET-CREATED

ADMIN HOTFIX SHA: dd261fa1b911a8c7856b286f1d07049b02ad492f
ADMIN HOTFIX CI: GREEN (32915185055)

P2 SHA: a517b65711e323d23b47201e5a38521c6c0248d1
P2 CI: GREEN (32915587152)

EXACT-LEAD BACKEND BASELINE RUN: 32914275215
EXACT-LEAD BACKEND ARTIFACT: 9587618833
EXACT-LEAD APK ARTIFACT: 9587692787

E2E QA SHA: e3a434e635875ea9fe28141c20b02c7a77ea4e2d
E2E QA RUN: 32915380364
E2E STATUS: TOOLING-ONLY / GREEN

IIS SHA: d48b7110cfe8bee942351805fc5d8b023b0c2e6a
IIS RUN: 32915931300
IIS ARTIFACT ID: 9588195365
IIS MANIFEST SOURCE SHA: d48b7110cfe8bee942351805fc5d8b023b0c2e6a
IIS STATUS: TOOLING-ONLY / GREEN

SECRET SCAN: PASS
PROVENANCE MATCH: WAITING-FOR-FINAL-CONVERGENCE
FINAL STATUS: WAITING-FOR-FINAL-CONVERGENCE
```

No main merge. No auto-merge. No force push. No self-merge.
