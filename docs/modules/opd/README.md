# OPD encounters

## Responsibility

The OPD module records a clinical interaction after front-desk hand-off. It is separate from appointment scheduling and queue management:

- Appointments reserve a provider slot.
- Queue entries record arrival and operational work.
- OPD encounters record the clinical interaction.

An encounter is created from an `InProgress` queue entry. It retains the tenant, hospital, department, patient registration and queue-entry relationship. One queue entry can have at most one encounter. Appointment-linked and walk-in queue entries use the same encounter workflow. The queue-entry lookup returns the existing encounter to support safe resume after a refresh; it does not expose patient details through the queue listing.

## API

All routes are tenant-scoped and hospital-scoped under `/api/v1/hospitals/{hospitalId}/opd`.

| Method | Route | Permission |
|---|---|---|
| POST | `/queue/{queueEntryId}/encounter` | `encounter.manage` |
| GET | `/queue/{queueEntryId}/encounter` | `encounter.read` |
| GET | `/encounters/{encounterId}` | `encounter.read` |
| PUT | `/encounters/{encounterId}/note` | `encounter.manage` |
| POST | `/encounters/{encounterId}/sign` | `encounter.sign` |
| POST | `/encounters/{encounterId}/amendments` | `encounter.amend` |
| POST | `/encounters/{encounterId}/close` | `encounter.manage` |
| POST | `/encounters/{encounterId}/cancel` | `encounter.manage` |

The initial role mapping is the explicitly provisioned `ClinicalUser` membership role. It receives read-only `facility.read` to load its assigned hospital's department context, read-only `queue.read` to discover operational queue tokens, and the four encounter permissions. It does not receive `queue.manage`; clinical users cannot call, cancel, mark no-show or otherwise transition queue entries. All access still requires a matching hospital/department scope. This role is an administrative authorization assertion; it does not prove a professional qualification, licence or clinical privilege. Queue permissions do not grant access to clinical notes.

## Encounter lifecycle

- `InProgress`: created from a queue entry whose status is `InProgress`; draft note sections can be updated.
- `Signed`: chief complaint and assessment are present; the authenticated signer issuer/subject and signing time are recorded. Normal note updates are rejected.
- `Closed`: a signed encounter is administratively/clinically closed without changing signed content.
- `Cancelled`: an unsigned in-progress encounter is cancelled with a required reason.

The initial note sections are chief complaint (500 characters), history (4,000), examination (4,000), assessment (2,000) and plan (4,000). The first increment does not implement coded vital observations because permitted codes, units, ranges and requiredness need pilot-clinician validation.

Signed notes are immutable through normal APIs. An amendment is an append-only record with reason, content, author issuer/subject and timestamp; it never overwrites the signed note. Encounter reads and mutations are covered by metadata-only request audit middleware. Clinical note text, patient names and other clinical content must not be copied into general logs, traces or metric labels.

## Explicit exclusions

No diagnosis catalogue/coding, allergy or medication reconciliation, prescriptions, lab/radiology orders, referrals, procedures, triage, clinical decision support, emergency/inpatient workflow, discharge summary, patient portal, document attachments or external interoperability. The API records clinician-entered information only and does not diagnose, recommend treatment or prescribe.

## Pilot and production gate

This is a narrow implementation foundation, not a complete EMR or production clinical specification. Before production activation, clinical stakeholders must validate note templates, vital observation policy, signer/author roles, assignment restrictions, amendment/correction and retention policy, privacy notices, audit review and emergency/break-glass access. Qualified privacy/legal stakeholders must validate applicable requirements; implementation alone is not a compliance certification.
