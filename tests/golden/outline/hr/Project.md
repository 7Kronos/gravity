# Project@1

## Identity

| name | type |
| --- | --- |
| id | UUID |

## Relations

| name | target | cardinality | optionality | semantic |
| --- | --- | --- | --- | --- |
| lead | Employee | one | required | _ |

## Properties

| name | type |
| --- | --- |
| name | String |
| code | String |
| start_date | Date |
| target_end_date | Date? |

## Lifecycle

### States

- Planned
- Active
- OnHold
- Completed
- Cancelled

### Transitions

| from | to | on |
| --- | --- | --- |
| Planned | Active | Started |
| Active | OnHold | Held |
| OnHold | Active | Resumed |
| Active | Completed | Completed |
| Active | Cancelled | Cancelled |
| Planned | Cancelled | Cancelled |
| OnHold | Cancelled | Cancelled |

## Events

### Started

| field | type |
| --- | --- |
| started_at | DateTime |

### Held

| field | type |
| --- | --- |
| held_at | DateTime |
| reason | String? |

### Resumed

| field | type |
| --- | --- |
| resumed_at | DateTime |

### Completed

| field | type |
| --- | --- |
| completed_at | DateTime |

### Cancelled

| field | type |
| --- | --- |
| cancelled_at | DateTime |
| reason | String |

## Commands

### Plan

| arg | type |
| --- | --- |
| name | String |
| code | String |
| start_date | Date |
| target_end_date | Date? |
| lead_id | UUID |

returns: PlanResult
with side_effect: Started

### Start

_(no arguments)_

returns: StartResult
with side_effect: Started

### Hold

| arg | type |
| --- | --- |
| reason | String? |

returns: HoldResult
with side_effect: Held

### Resume

_(no arguments)_

returns: ResumeResult
with side_effect: Resumed

### Complete

_(no arguments)_

returns: CompleteResult
with side_effect: Completed

### Cancel

| arg | type |
| --- | --- |
| reason | String |

returns: CancelResult
with side_effect: Cancelled

