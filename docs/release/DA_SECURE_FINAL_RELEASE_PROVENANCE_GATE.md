# DA Secure — Final Release Artifact Provenance Gate

Snapshot date: 2026-08-26

This document is release evidence / artifact integrity only. It does not authorize a merge or modify production behavior.

## Gate rule

A final release is admissible only when one authoritative production candidate SHA exists and all final evidence identifies that exact SHA:

`FINAL_SHA == backend CI SHA == Flutter/APK CI SHA == E2E QA production source SHA == IIS BUILD-MANIFEST sourceSha`

Ancestry is not sufficient. Branch names, artifact names, or the word `final` are not provenance.

## Live convergence state

- Current production lead: `lead/da-secure-runtime-convergence@3e0864f38d15bbb890833b52fb193cbd19e6464a`
- Expected final SHA: **NOT-YET-CREATED**
- PR #20: OPEN / mergeable, head `dd261fa1b911a8c7856b286f1d07049b02ad492f`; exact-head backend gate GREEN, 130/130 tests PASS.
- PR #21: OPEN / mergeable, head `3e0864f38d15bbb890833b52fb193cbd19e6464a`; this is the current production lead but is not release-final because later closure branches remain unconverged.
- Localization P2: branch `worker/da-secure-localization-p2-closure@a517b65711e323d23b47201e5a38521c6c0248d1`; exact-head backend + Flutter gate GREEN; no workflow artifact uploaded.
- PR #22: OPEN / mergeable / DRAFT, head `d48b7110cfe8bee942351805fc5d8b023b0c2e6a`; IIS provenance tooling gate GREEN.
- Final E2E QA worker: `worker/da-secure-final-e2e-device-qa@e3a434e635875ea9fe28141c20b02c7a77ea4e2d`; latest workflow GREEN, but the checked-in QA matrix still identifies production candidate `e9c62cd8ba215e5f1e038cdddb9776acc70990bb`, so exact production-source provenance for the current lead is not recorded.

## Artifact inventory

### AUTHORITATIVE — current lead only, not final release authority

1. Backend test evidence
   - Source SHA: `3e0864f38d15bbb890833b52fb193cbd19e6464a`
   - Run: `32914755630`
   - Artifact: `9587779237` — `da-secure-runtime-convergence-backend-tests`
   - GitHub artifact digest: `sha256:a1be594085e94efed94b539f6863e056aa2a0789fd077bd5cc9c76030795b486`
   - TRX SHA-256: `19bf59153284805e4f14edf482938a0c934afacd7d98d748e3ac9e95ddddd585`
   - Result: 125/125 PASS
   - Created: `2026-08-26T00:23:17Z`
   - Expires: `2026-09-09T00:23:16Z`

2. Debug APK
   - Source SHA: `3e0864f38d15bbb890833b52fb193cbd19e6464a`
   - Run: `32914755630`
   - Artifact: `9587880078` — `da-secure-runtime-convergence-debug-apk`
   - GitHub artifact digest: `sha256:92f9de1d66e9c550b471bdfef234ce99b1da367ac0425d7313f56e29507519a1`
   - Raw `app-debug.apk` SHA-256: `c399b5ab11ad6ac7d2357d4f92b5b2a42ecccf39c9f31dcd04ef0b9d696902b0`
   - Created: `2026-08-26T00:27:21Z`
   - Expires: `2026-09-09T00:27:16Z`

These two artifacts are exact for the current production lead. They become stale for release purposes as soon as the production candidate SHA changes.

### TOOLING-ONLY

1. Admin hotfix evidence
   - SHA: `dd261fa1b911a8c7856b286f1d07049b02ad492f`
   - Run: `32915185055`
   - Artifact: `9587924506`
   - Digest: `sha256:d31631a2b74c23eb3ecd39cd0a03eed5461cf2669b66b4a7f822294fa731590c`
   - TRX SHA-256: `5011eaf6147eb616d105a883f29952b3f8b7791d0db4c6546724acbf47a87836`
   - 130/130 PASS
   - Reason: production changes are not yet converged into the final candidate.

2. Localization P2 validation
   - SHA: `a517b65711e323d23b47201e5a38521c6c0248d1`
   - Run: `32915587152`
   - Backend build/test: GREEN
   - Flutter format/analyze/test: GREEN
   - Artifact: none
   - Reason: production changes are not yet converged into the final candidate.

3. Latest E2E QA evidence
   - Workflow head: `e3a434e635875ea9fe28141c20b02c7a77ea4e2d`
   - Run: `32915380364`
   - Backend artifact: `9588001379`, digest `sha256:2079678122413d473dc3455e8279df63a24f8878ed1fa6db133b2d327a6b8433`
   - Backend TRX SHA-256: `6c91a490fa19e76b9476d4b80f90b773e9366cf36cd2dbd9d09022abe8d526ec`, 127/127 PASS
   - Static QA JSON SHA-256: `5951602dc8ffeff521b823c46318b73ce354acc1dd58f50a0ee6f9cc3e990ad5`
   - APK artifact: `9588086759`, digest `sha256:b9d0841c12c7bfb200da4c2bafefe6ca9e9daa760710fe3cbc477e606f4a2266`
   - Raw APK SHA-256: `a799c6d8abeb4c94c68890813adddfea52eebcbfce24778654e9a41dc6579ff3`
   - Reason: workflow is GREEN, but current QA provenance does not explicitly identify the current lead/future final SHA as its production source; checked-in matrix still identifies `e9c62cd...`.

4. Current IIS provenance-tooling package
   - Workflow head / manifest source SHA: `d48b7110cfe8bee942351805fc5d8b023b0c2e6a`
   - Run: `32915931300`
   - Artifact: `9588195365` — `da-secure-iis-1.0.0-iis.2-d48b7110cfe8bee942351805fc5d8b023b0c2e6a`
   - Artifact ZIP SHA-256: `c26f9ffff15cfcd65af195daf8ebb6f2553eeda47cd4edc7786e83ac824cbc46`
   - BUILD-MANIFEST file SHA-256: `4f5567fd0c5274c3e1ca609e4dd426e0559c5ed0ce60c2eea082f7199e996fbd`
   - Manifest `sourceSha`: `d48b7110cfe8bee942351805fc5d8b023b0c2e6a`
   - Payload `publish/SecureQrPortal.dll` SHA-256: `c2a60512e831fed2dfbc7b9e46b121265294716133069761d50ba285e371885d`
   - Created: `2026-08-26T00:40:58Z`
   - Expires: `2026-09-09T00:40:54Z`
   - Reason: packaging implementation is now provenance-safe, but the package was built from the IIS worker SHA, not the final production candidate.

### SUPERSEDED

1. Earlier exact-current-lead convergence artifacts, run `32914275215`:
   - APK `9587692787`, digest `sha256:a636b8082a274cf3c3316759a47ee5cad1d058e4425cf0129020a78c6add9007`
   - Backend tests `9587618833`, digest `sha256:72520264f89e7ffd53d53a18809a3e55781de65acfd9fd209fd6be99926029c9`
   - Both use source `3e0864f...` but are superseded by the later successful exact-SHA run `32914755630`.

2. Earlier Final E2E QA artifacts, run `32914294391`, QA head `29525857f858c43e5bc26814a70a7dd54bb3cf97`:
   - Backend `9587616887`, digest `sha256:b0262ce3f2f22b2deb85e8b98709a80cc6581ba52d06c4fb8cb5bf6742220609`
   - APK `9587733258`, digest `sha256:482bae327deeb80ef251858365031288194434b18c40d183518d99ec2a48a6ef`
   - Documented production candidate: `e9c62cd8ba215e5f1e038cdddb9776acc70990bb`
   - Superseded by later lead convergence and later QA runs.

3. IIS package `9587905221`, run `32915083312`, workflow head `7fca811cbd10ee0f839ac36f989ffed3b0054b51`:
   - Artifact ZIP SHA-256: `e171bc7f72fc5d5a517205fe4ab4b00d163aa06adf6e74673b2ef95c3e3bf13e`
   - BUILD-MANIFEST file SHA-256: `80e5a62e53023dcf72d6c1e985faf8601f5560b0f7202dbc0936ff390cb2ff21`
   - Manifest `sourceSha`: `e9c62cd8ba215e5f1e038cdddb9776acc70990bb`
   - Payload DLL SHA-256: `951abf580c97e1885416c1e872dfa3db6f910107928fd55869d616c840590110`
   - REJECT for final release provenance: workflow source at this checkpoint hardcoded `SourceRevisionId=e9c62cd8...` and hardcoded the manifest source SHA instead of binding identity to checked-out HEAD/GITHUB_SHA.

## Security scan

Downloaded current lead APK/backend evidence, current E2E APK/backend evidence, current IIS package, historical IIS package, and Admin hotfix test evidence were inspected without printing secret contents.

PASS for the inspected artifact set:

- no service-account JSON file
- no PFX
- no PEM/private-key file
- no `.key` private-key file
- no `appsettings.Secrets.json` payload
- no `.env` secret file
- no PEM private-key material
- no JWT-shaped production token
- no Google OAuth token-shaped value
- no GitHub token-shaped value
- no AWS access-key-shaped value

Text such as `private_key`, `client_email`, `appsettings.Secrets.json`, and `refresh_token` appears only in safety scripts, documentation, or test names/contracts; no credential-shaped value was found.

## Final release checklist

Do not declare READY until all items below are true against one exact `FINAL_SHA`:

- [ ] Lead has converged all required production closures, including PR #20 and localization P2.
- [ ] One exact production `FINAL_SHA` is selected after convergence.
- [ ] Backend Release build runs on `FINAL_SHA` and passes.
- [ ] Backend tests run on `FINAL_SHA` and pass; artifact ID/digest/expiry recorded.
- [ ] Flutter format runs on `FINAL_SHA` and passes.
- [ ] Flutter analyze runs on `FINAL_SHA` and passes.
- [ ] Flutter tests run on `FINAL_SHA` and pass.
- [ ] Debug APK is built from `FINAL_SHA`; GitHub artifact digest and raw APK SHA-256 recorded.
- [ ] Final E2E QA explicitly records `productionSourceSha = FINAL_SHA`; do not infer by ancestry or by comparing files.
- [ ] E2E backend/security/admin/mobile evidence passes for that exact production source.
- [ ] IIS package workflow runs from `FINAL_SHA` after provenance tooling is available on that candidate.
- [ ] IIS generated BUILD-MANIFEST has `sourceSha == FINAL_SHA`.
- [ ] Published DLL embeds `SourceRevisionId == FINAL_SHA`.
- [ ] IIS package payload SHA-256 and artifact ZIP digest recorded.
- [ ] Final artifact secret scan passes.
- [ ] No artifact called `final` is accepted unless all exact-SHA checks above pass.

## Current decision

`PROVENANCE_MATCH = WAITING-FOR-FINAL-SHA`

`STATUS = WAITING-FOR-FINAL-CONVERGENCE`
