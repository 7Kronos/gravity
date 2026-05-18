# Employee@1

## Identity

| name | type |
| --- | --- |
| id | UUID |

## Relations

_(none)_

## Properties

| name | type |
| --- | --- |
| first_name | String |
| last_name | String |
| email | String |
| hire_date | Date |
| contract_type | ContractType |
| contact | ContactInfo? |

## Lifecycle

### States

- Onboarding
- Active
- OnLeave
- Terminated

### Transitions

| from | to | on |
| --- | --- | --- |
| Onboarding | Active | Activated |
| Active | OnLeave | LeaveStarted |
| OnLeave | Active | Returned |
| Active | Terminated | Terminated |
| OnLeave | Terminated | Terminated |

## Events

### Activated

| field | type |
| --- | --- |
| activated_at | DateTime |

### LeaveStarted

| field | type |
| --- | --- |
| started_at | DateTime |
| reason | String? |

### Returned

| field | type |
| --- | --- |
| returned_at | DateTime |

### Terminated

| field | type |
| --- | --- |
| terminated_at | DateTime |
| reason | String |

## Commands

### Onboard

| arg | type |
| --- | --- |
| first_name | String |
| last_name | String |
| email | String |
| hire_date | Date |
| contract_type | ContractType |

returns: OnboardResult
with side_effect: Activated

### Activate

_(no arguments)_

returns: ActivationResult
with side_effect: Activated

### GoOnLeave

| arg | type |
| --- | --- |
| reason | String? |

returns: LeaveResult
with side_effect: LeaveStarted

### Return

_(no arguments)_

returns: ReturnResult
with side_effect: Returned

### Terminate

| arg | type |
| --- | --- |
| reason | String |

returns: TerminationResult
with side_effect: Terminated

