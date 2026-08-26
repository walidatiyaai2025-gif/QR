# SUPERSEDED — DA Secure Final Release Artifact Provenance Gate

> **SUPERSEDED / NOT AUTHORITATIVE FOR RELEASE PROVENANCE.**
>
> This earlier snapshot relied on GitHub workflow `head_sha` metadata for PR #21 run `32914755630`. Subsequent job-log inspection proved that `actions/checkout` actually checked out synthetic PR merge commit `3b7c3dfdfbe77f258e3c9714df5483eb32907055`, not `3e0864f38d15bbb890833b52fb193cbd19e6464a`.
>
> Under the release rule that ancestry and metadata are insufficient, artifacts `9587779237` and `9587880078` are **UNVERIFIED for exact-SHA provenance** and must not be treated as authoritative exact-head artifacts.
>
> The authoritative audit is now:
> `docs/mobile-release/DA_SECURE_FINAL_RELEASE_PROVENANCE.md`
>
> This file is retained only as superseded audit history. It does not authorize a release, merge, or artifact promotion.

Snapshot date: 2026-08-26

## Supersession reason

The exact release gate is:

`FINAL_SHA == actual backend checkout SHA == actual Flutter/APK checkout SHA == E2E production source SHA == IIS BUILD-MANIFEST sourceSha`

A workflow `head_sha`, branch name, artifact name, ancestry relationship, or synthetic pull-request merge build does not satisfy this equality.

At supersession time the final convergence SHA was still **NOT-YET-CREATED** because the live lead `3e0864f38d15bbb890833b52fb193cbd19e6464a` had not yet converged Admin hotfix `dd261fa1b911a8c7856b286f1d07049b02ad492f` and Localization P2 `a517b65711e323d23b47201e5a38521c6c0248d1`.

## Historical artifact classifications corrected

- Run `32914275215` artifacts `9587618833` (backend) and `9587692787` (APK): **TOOLING-ONLY exact-head baseline evidence** for live lead `3e0864f...`; not FINAL because final convergence does not yet exist.
- Run `32914755630` artifacts `9587779237` and `9587880078`: **UNVERIFIED for exact-SHA provenance** because the job built synthetic PR merge SHA `3b7c3df...`.
- Admin run `32915185055` / artifact `9587924506`: **TOOLING-ONLY** worker evidence.
- Localization P2 run `32915587152`: **TOOLING-ONLY** worker evidence.
- E2E run `32915380364` / artifacts `9588001379` and `9588086759`: **TOOLING-ONLY** QA evidence pending explicit `productionSourceSha == FINAL_SHA` after final convergence.
- IIS run `32915931300` / artifact `9588195365`: **TOOLING-ONLY** package evidence; generated manifest is internally correct for `d48b7110cfe8bee942351805fc5d8b023b0c2e6a` but does not match a final production SHA.
- Historical E2E candidate `e9c62cd...` artifacts: **STALE**.
- Earlier IIS artifacts superseded by `9588195365`: **SUPERSEDED**.

## Current decision at supersession

`EXPECTED_FINAL_SHA = NOT-YET-CREATED`

`PROVENANCE_MATCH = WAITING-FOR-FINAL-CONVERGENCE`

`FINAL STATUS = WAITING-FOR-FINAL-CONVERGENCE`

No main merge. No auto-merge. No force push. No self-merge.
