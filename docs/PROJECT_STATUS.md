# Project status and evidence

This page keeps project health separate from promotional activity. Counts describe public evidence as of 2026-09-05 and should change only when the underlying evidence changes.

## Baseline

| Measure | Count | Qualification rule |
| --- | ---: | --- |
| Executable fixture cases | 4 | A manifest case exercised by the production verifier |
| Synthetic payload files | 3 | A registered JSON or XML payload |
| Input adapters | 2 | A tested native input family: Frigate or ONVIF |
| Externally derived behavior cases | 0 | A distinct behavior reported by someone outside project maintenance and merged as a sanitized executable case |
| Independent downstream consumers | 0 | A non-Kalytera repository that runs or references OpenCamInterop in real CI or code |
| Non-maintainer behavior contributors | 0 | A unique person outside maintenance whose substantive behavior case was merged |
| First-party consumers | 1 | IPCamLapse uses the library source directly |
| Standalone releases | 1 | A tagged release in the standalone repository |
| Known privacy incidents | 0 | A fixture or report requiring removal of private data |

Release evidence: [`v0.1.0-alpha.1`](https://github.com/KalyteraSystems/OpenCamInterop/releases/tag/v0.1.0-alpha.1).

All current fixtures are synthetic. They establish parser and contract behavior only, not physical-device coverage, firmware support, vendor compatibility, certification, or adoption.

## What counts

The north-star measure is a distinct externally reported interoperability behavior merged as a sanitized fixture plus an executable expected result. Also useful are independent downstream CI consumers, unique substantive contributors, repeat-contributor rate, issue-to-green-pull-request time, cross-platform determinism, and privacy incidents.

The following do not count as external adoption or behavior coverage:

- stars, watches, clones, or page views;
- maintainer-created issues, commits, fixtures, or pull requests;
- generated compatibility rows;
- mechanically split variants of one payload;
- model or vendor names without a reproducing payload and assertion; or
- trivial edits created to increase contribution counts.

## Learning checkpoints

These are product-learning targets, not claims or eligibility guarantees:

- 30 days after launch: one external behavior case merged and one independent CI consumer.
- 90 days after launch: three distinct external behavior contributions, two independent consumers, and at least one repeat contributor.

If those measures remain zero, reassess the scenario format and outreach fit. Do not manufacture contributor-sized work or relax privacy and evidence requirements.

No open-source benefit program qualification is claimed. Any future application should use the program provider's then-current criteria and independently verifiable public metrics.
