# Appointments and provider scheduling

This module implements the first scheduling increment: date-specific provider availability, deterministic slot generation, appointment booking, retrieval and cancellation. It is separate from patient registration and from the future front-desk queue and clinical encounter modules.

## Routes

All routes are tenant-scoped and hospital-scoped under `/api/v1/hospitals/{hospitalId}`.

| Method | Route | Permission |
|---|---|---|
| POST | `/availability-sessions` | `availability.manage` |
| GET | `/availability-sessions?date=YYYY-MM-DD&departmentId=...` | `availability.read` |
| GET | `/availability-sessions/{sessionId}/slots` | `availability.read` |
| POST | `/appointments` | `appointment.manage` |
| GET | `/appointments/{appointmentId}` | `appointment.read` |
| POST | `/appointments/{appointmentId}/cancel` | `appointment.manage` |
| POST | `/appointments/{appointmentId}/reschedule` | `appointment.manage` |
| POST | `/appointments/{appointmentId}/check-in` | `queue.manage` |
| POST | `/queue/walk-ins` | `queue.manage` |
| GET | `/queue?departmentId=...&date=YYYY-MM-DD` | `queue.read` |
| POST | `/queue/{queueEntryId}/{action}` | `queue.manage` |

Queue actions are `call`, `recall`, `start`, `complete`, `cancel` and `noshow`. Collection results are paginated with a maximum page size of 100. Slot results are bounded by the configured session duration and slot length.

## Scheduling rules

- Availability is published as an explicit local-date session for one provider, hospital and department. Recurring schedules are not inferred.
- The provider must be an active staff profile with a provider designation and an active, effective-dated assignment to the same hospital and department on the session date. This is operational scheduling metadata, not credential verification or clinical privilege.
- The request supplies an IANA time-zone identifier. Local start/end values are converted to UTC for storage. Ambiguous or nonexistent local times are rejected rather than guessed.
- Session duration is limited to 12 hours; slot duration is 5–120 minutes and must divide the session evenly.
- PostgreSQL exclusion constraints prevent overlapping sessions for the same tenant/provider and prevent overlapping active appointments for the same tenant/patient. A filtered unique index independently protects one active booking per provider slot.
- Booking requires an existing patient registration for the same tenant and hospital. It does not create a patient or silently associate one across facilities.
- Appointment states are `Booked`, `Cancelled`, `Completed` and `NoShow`. Only booked appointments can be cancelled or rescheduled. Rescheduling retains the patient and is rejected after check-in. Appointment check-in is idempotent and is allowed from 60 minutes before until 30 minutes after the slot start. The early/late window is a provisional pilot default and should be validated by each hospital.
- Appointment reason is optional, limited to 250 characters and intended for short operational context only. Do not put diagnoses, symptoms, triage notes or clinical instructions here.


## Front-desk queue

- Appointment check-in creates exactly one queue entry. A unique tenant/appointment index and transaction-scoped PostgreSQL advisory lock protect idempotency and token allocation.
- Walk-ins require an existing registration at the same hospital and enter the queue directly; they do not create an appointment.
- Queue tokens increment per tenant, hospital, department and hospital-local date. The supplied IANA time zone determines the local queue date. Tokens are not MRNs.
- Queue states are `Waiting`, `Called`, `InProgress`, `Completed`, `Cancelled` and `NoShow`. Allowed transitions are Waiting → Called → InProgress → Completed; Called can return to Waiting for recall; Waiting/Called can be cancelled or marked no-show with a reason.
- Queue listings expose token, state and operational timestamps only. Patient identifiers, names, MRNs and clinical information are deliberately excluded from queue DTOs.
- Queue collections are paginated (maximum 100). Tenant/hospital/department authorization is checked independently of permission claims. The first pilot role mapping grants queue operations to HospitalAdmin and PatientRegistrar; this mapping is provisional.
- Clinical triage, emergency prioritization, encounter creation and clinical notes remain outside this module. Emergency arrivals must follow the hospital's separate emergency pathway.

## Authorization

The initial role mapping is explicit: HospitalAdmin receives `availability.read` and `availability.manage`; PatientRegistrar receives `availability.read`, `appointment.read` and `appointment.manage`. Other roles do not inherit these permissions. These are provisional pilot mappings and must be reviewed with each hospital before production deployment. Organization scope is checked independently of permission claims.

## Persistence and privacy

All records are tenant-owned, tenant-filtered and protected by tenant-aware composite foreign keys. Patient linkage is constrained to the hospital's existing registration. No patient contact data, clinical data, reminders, external calendar integration or cross-tenant patient search is introduced.

## Verification

The integration suite exercises availability publication, local-time conversion, slot enumeration, patient registration enforcement, duplicate booking conflicts, cancellation and rebooking against PostgreSQL. The Docker Integration Verification workflow runs the isolated PostgreSQL test environment for pull requests.
