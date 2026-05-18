# TimeEntry@1

## Identity

| name | type |
| --- | --- |
| id | UUID |

## Relations

| name | target | cardinality | optionality | semantic |
| --- | --- | --- | --- | --- |
| employee | Employee | one | required | _ |
| project | Project | one | required | _ |
| submitter | Employee | one | required | submitted_by |
| approver | Employee | one | optional | approved_by |

## Properties

| name | type |
| --- | --- |
| date | Date |
| hours | Decimal |
| billable | Boolean |
| description | String? |
| rejection_reason | String? |

## Lifecycle

### States

- Draft
- Submitted
- Approved
- Rejected
- Reopened

### Transitions

| from | to | on |
| --- | --- | --- |
| Draft | Submitted | Submitted |
| Submitted | Approved | Approved |
| Submitted | Rejected | Rejected |
| Rejected | Draft | Resubmitted |
| Approved | Reopened | Reopened |
| Reopened | Submitted | Submitted |

## Events

### Submitted

| field | type |
| --- | --- |
| submitted_at | DateTime |

### Approved

| field | type |
| --- | --- |
| approver_id | UUID |
| approved_at | DateTime |

### Rejected

| field | type |
| --- | --- |
| approver_id | UUID |
| reason | String |

### Resubmitted

_(no payload)_

### Reopened

| field | type |
| --- | --- |
| reopener_id | UUID |
| reason | String |

## Commands

### Submit

_(no arguments)_

returns: SubmissionResult
with side_effect: Submitted

### Approve

| arg | type |
| --- | --- |
| approver_id | UUID |

returns: ApprovalResult
with side_effect: Approved

### Reject

| arg | type |
| --- | --- |
| approver_id | UUID |
| reason | String |

returns: RejectionResult
with side_effect: Rejected

